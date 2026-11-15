// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	static class FFToolsUtils {

		// Child decoders (ffmpeg thumbnail/audio extraction) are the CPU/disk hogs during a
		// scan — dropping them to Idle keeps foreground apps responsive while an unattended
		// scan still runs at full speed (Idle only cedes under contention). Best-effort: a
		// fast child may already have exited, which throws; ignore.
		internal static void LowerChildPriority(Process process) {
			try { process.PriorityClass = ProcessPriorityClass.Idle; } catch { }
		}

		/// <summary>
		/// Every ffmpeg/ffprobe child dies with VDF. Without this a crashed or killed VDF (Task
		/// Manager, a native crash in a decoder) left one ffmpeg per worker running, decoding into
		/// a pipe nobody reads, until each finished its file. Windows does it with a job object,
		/// Linux with PR_SET_PDEATHSIG from a thread .NET keeps alive for that; macOS has no
		/// equivalent, and there the children still end at their own timeout.
		/// </summary>
		internal static void DieWithVdf(ProcessStartInfo psi) {
			if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
				psi.KillOnParentExit = true;
		}

		/// <summary>What a bounded tool run produced: all of stdout, stderr as text, the exit code.</summary>
		internal readonly record struct ToolOutput(byte[] Stdout, string Stderr, int ExitCode);

		/// <summary>
		/// Starts <paramref name="psi"/> at Idle priority, reads all of its stdout and stderr and
		/// waits for it to exit, each bounded by <paramref name="timeoutMs"/>. Kills the child and
		/// throws <see cref="TimeoutException"/> naming <paramref name="toolName"/> and
		/// <paramref name="file"/> when it overruns.
		/// </summary>
		/// <remarks>
		/// The bounded read is the whole point. Reading a pipe synchronously
		/// (<c>CopyTo</c>/<c>ReadToEnd</c>) returns only once the child closes it, which a wedged
		/// ffmpeg/ffprobe never does (dead network share, sleeping USB drive, stalled demuxer, an
		/// input seek that turns into an endless linear scan). A <c>WaitForExit(timeout)</c> after
		/// such a read is unreachable in exactly the cases it exists for: the worker blocked
		/// forever and the scan froze without a word (#865). <see cref="Process.ReadAllBytes"/>
		/// (.NET 11) reads both pipes together on this thread under one deadline, so neither pipe
		/// can fill up and stall the child, and no thread-pool copy is left behind to fault after
		/// the kill, which the async copies before it needed an observer for.
		/// </remarks>
		internal static ToolOutput RunCaptured(ProcessStartInfo psi, int timeoutMs, string toolName, string file) {
			psi.UseShellExecute = false;
			psi.RedirectStandardOutput = true;
			psi.RedirectStandardError = true;
			DieWithVdf(psi);
			TimeSpan timeout = TimeSpan.FromMilliseconds(timeoutMs);
			using Process process = Process.Start(psi) ?? throw new InvalidOperationException($"{toolName} did not start.");
			LowerChildPriority(process);
			byte[] stdout, stderr;
			try {
				(stdout, stderr) = process.ReadAllBytes(timeout);
			}
			catch (Exception e) {
				try { process.Kill(); } catch { }
				if (e is TimeoutException)
					throw new TimeoutException($"{toolName} timed out on file: {file}");
				throw;
			}
			// Both pipes are at EOF, so the child is done writing; anything but a prompt exit now
			// means it is wedged on teardown.
			ProcessExitStatus status = process.SafeHandle.WaitForExitOrKillOnTimeout(timeout);
			if (status.Canceled)
				throw new TimeoutException($"{toolName} did not exit after closing its output: {file}");
			return new ToolOutput(stdout, Encoding.UTF8.GetString(stderr), status.ExitCode);
		}

		/// <summary>
		/// A tool's stderr the way the log shows it: one line each, each preceded by a line break,
		/// and a run of identical lines collapsed into one plus a count. A single broken HEVC/H.264
		/// stream can print the same decoder error tens of thousands of times per file.
		/// </summary>
		internal static string FormatStderr(string stderr, Func<string, bool>? keep = null) {
			var sb = new StringBuilder();
			string? last = null;
			int repeats = 0;
			foreach (string raw in stderr.Split('\n')) {
				string line = raw.TrimEnd('\r');
				if (line.Length == 0 || (keep != null && !keep(line)))
					continue;
				if (line == last) {
					repeats++;
					continue;
				}
				AppendRepeats(sb, repeats);
				repeats = 0;
				sb.Append(Environment.NewLine).Append(line);
				last = line;
			}
			AppendRepeats(sb, repeats);
			return sb.ToString();

			static void AppendRepeats(StringBuilder sb, int repeats) {
				if (repeats > 0)
					sb.Append($" (repeated {repeats} more time{(repeats == 1 ? string.Empty : "s")})");
			}
		}

		/// <summary>
		/// Best-effort kill plus a short drain of a pending stdout read. Killing the child breaks
		/// the pipe, so the read completes promptly — the bounded wait keeps a stuck kill from
		/// re-introducing the very hang this exists to prevent.
		/// </summary>
		internal static void KillAndDrain(Process process, Task? readTask) {
			try {
				if (!process.HasExited)
					process.Kill();
			}
			catch { }
			// A fault inside the drain window is observed by the Wait (it throws into the
			// empty catch); only a copy that outlives the window escapes it.
			try { readTask?.Wait(2000); } catch { }
			if (readTask is { IsCompleted: false })
				ObserveAbandonedRead(readTask);
		}

		/// <summary>
		/// Detaches an abandoned stdout copy. It will usually still fault - broken pipe
		/// after the kill, or ObjectDisposedException once the caller's destination stream
		/// is disposed under it - and an unawaited fault resurfaces on the finalizer thread
		/// as an UnobservedTaskException (#865: 105k identical log entries in one scan of a
		/// resource-starved machine). Attaching an exception observer keeps it silent.
		/// </summary>
		internal static void ObserveAbandonedRead(Task readTask) =>
			readTask.ContinueWith(static t => _ = t.Exception,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);

		const string FFprobeExecutableName = "ffprobe";
		const string FFmpegExecutableName = "ffmpeg";
		static readonly string ffProbePlatformName;
		static readonly string ffMpegPlatformName;
		public enum FFTool {
			FFProbe,
			FFmpeg
		}

		static FFToolsUtils() {
			ffProbePlatformName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? FFprobeExecutableName + ".exe" : FFprobeExecutableName;
			ffMpegPlatformName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? FFmpegExecutableName + ".exe" : FFmpegExecutableName;
		}

		/// <summary>
		/// Gets path of ffprobe or ffmpeg
		/// </summary>
		/// <param name="tool"></param>
		/// <returns>path or null if not found</returns>
		internal static string? GetPath(FFTool tool) {
			var toolExecutable = tool == FFTool.FFmpeg ? ffMpegPlatformName : ffProbePlatformName;
			var toolPath = Path.Combine(CoreUtils.CurrentFolder, "bin", toolExecutable);
			if (File.Exists(toolPath))
				return toolPath;

			toolPath = Path.Combine(CoreUtils.CurrentFolder, toolExecutable);
			if (File.Exists(toolPath))
				return toolPath;

			static string? ScanPathDirs(string? pathVariable, string toolExecutable) {
				if (pathVariable == null) return null;
				foreach (var path in pathVariable.Split(Path.PathSeparator)) {
					if (!Directory.Exists(path))
						continue;

					try {
						FileInfo[] files = new DirectoryInfo(path).GetFiles(toolExecutable, new EnumerationOptions {
							IgnoreInaccessible = true,
							MatchCasing = MatchCasing.CaseInsensitive
						});

						if (files.Length > 0)
							return files[0].FullName;
					}
					catch (Exception) {
#if DEBUG
						throw;
#endif
					}
				}
				return null;
			}

			toolPath = ScanPathDirs(Environment.GetEnvironmentVariable("PATH"), toolExecutable);
			if (toolPath != null)
				return toolPath;

			// The process PATH is a snapshot taken when the app was launched. On Windows an
			// FFmpeg installed afterwards (winget/scoop/choco or a manual PATH edit) shows up
			// in new consoles but not here, so users see "ffmpeg -version" work while VDF
			// keeps reporting it missing (issue #788). Re-read the registry-backed user and
			// machine PATH, which is always current.
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				toolPath = ScanPathDirs(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), toolExecutable)
					?? ScanPathDirs(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine), toolExecutable);
				if (toolPath != null)
					return toolPath;
			}

			// A GUI app launched from Finder (macOS) or a desktop launcher (Linux) can inherit
			// a minimal PATH that omits the directory the binaries actually live in, so the
			// scan above misses an otherwise correctly installed FFmpeg. Probe the standard
			// install locations explicitly. See issue #764.
			string[] wellKnownBinDirs =
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin"] :
				RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ["/usr/local/bin", "/usr/bin", "/bin", "/snap/bin"] :
				[];
			foreach (var binDir in wellKnownBinDirs) {
				toolPath = Path.Combine(binDir, toolExecutable);
				if (File.Exists(toolPath))
					return toolPath;
			}

			return null;
		}

		// Windows MAX_PATH: paths at or beyond this length need the extended-length
		// "\\?\" prefix to be opened. Shorter paths are passed through verbatim.
		const int WindowsMaxPath = 260;

		/// <summary>
		/// On Windows, prefixes the path with the extended-length "\\?\" form when (and only
		/// when) it is long enough to require it. Other platforms return the path unchanged.
		/// </summary>
		/// <remarks>
		/// The prefix is applied conditionally on purpose. It contains a '?', which FFmpeg's
		/// image2 demuxer treats as a glob/sequence metacharacter, so prefixing every path made
		/// still images fail to open ("Could not open file" / "Could find no file or sequence",
		/// #806). Only paths that actually exceed MAX_PATH need the prefix; normal-length paths
		/// (the overwhelming majority) are now handed to FFmpeg as-is and open correctly.
		/// </remarks>
		/// <param name="path">Path of the file</param>
		/// <returns>On Windows: long paths get the "\\?\" prefix. Otherwise same as input.</returns>
		/// <summary>
		/// Runs <c>&lt;tool&gt; -version</c> and returns its first output line (the
		/// "ffmpeg version …" banner), or a short diagnostic string if the tool is missing
		/// or could not be run. Used by the GUI diagnostics report for bug submissions.
		/// </summary>
		internal static string GetToolVersionLine(FFTool tool) {
			string? path = GetPath(tool);
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return $"{tool}: not found";
			try {
				var psi = new ProcessStartInfo {
					FileName = path,
					Arguments = "-version",
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden
				};
				// Bounded, see RunCaptured: this one runs on the UI thread (diagnostics report),
				// where a synchronous drain of a wedged tool froze the window.
				var output = RunCaptured(psi, 5000, tool.ToString(), path);
				string firstLine = Encoding.UTF8.GetString(output.Stdout)
					.Split('\n', 2)[0].TrimEnd('\r');
				return firstLine.Length > 0 ? firstLine : $"{tool}: no version output";
			}
			catch (Exception e) {
				return $"{tool}: {e.GetType().Name}: {e.Message}";
			}
		}

		internal static string LongPathFix(string path) {
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return path;
			if (path.StartsWith("\\\\?\\")) //already extended-length
				return path;
			if (path.Length < WindowsMaxPath)
				return path;
			//Check if path is UNC, see https://github.com/0x90d/videoduplicatefinder/issues/443
			if (path.StartsWith('\\'))
				return $"\\\\?\\UNC\\{path.TrimStart('\\')}";
			return $"\\\\?\\{path}";
		}
	}

}
