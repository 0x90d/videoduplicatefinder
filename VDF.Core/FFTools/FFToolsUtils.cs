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
		/// Copies the child's stdout into <paramref name="destination"/> and waits for it to
		/// exit, both bounded by <paramref name="timeoutMs"/>. Kills the child and throws
		/// <see cref="TimeoutException"/> when it overruns.
		/// </summary>
		/// <remarks>
		/// The bounded read is the whole point. Reading the pipe synchronously
		/// (<c>CopyTo</c>/<c>ReadToEnd</c>) returns only once the child closes stdout — which a
		/// wedged ffmpeg/ffprobe never does (dead network share, sleeping USB drive, stalled
		/// demuxer, an input seek that turns into an endless linear scan). A
		/// <c>WaitForExit(timeout)</c> placed after such a read is unreachable code in exactly
		/// the cases it exists for: the worker thread blocked forever, the scan's progress
		/// counter froze, nothing was logged, and Stop had nothing it could interrupt because
		/// the cancellation token is not observed inside a synchronous pipe read either (#865).
		/// </remarks>
		internal static void ReadStdoutBounded(Process process, Stream destination, int timeoutMs,
				string toolName, string file, CancellationToken cancelToken = default) {
			Task? readTask = null;
			try {
				readTask = process.StandardOutput.BaseStream.CopyToAsync(destination, cancelToken);
				if (!readTask.Wait(timeoutMs, cancelToken))
					throw new TimeoutException($"{toolName} timed out on file: {file}");
				// stdout is at EOF here, so the child is finished writing; anything but a prompt
				// exit now means it is wedged on teardown.
				if (!process.WaitForExit(timeoutMs))
					throw new TimeoutException($"{toolName} did not exit after closing its output: {file}");
				process.WaitForExit(); // flush async stderr handlers, see dotnet/runtime#18789
			}
			catch (Exception e) {
				KillAndDrain(process, readTask);
				// Task.Wait wraps a failed read; callers log e.Message, so hand them the real one.
				throw e is AggregateException ae && ae.InnerExceptions.Count == 1 ? ae.InnerExceptions[0] : e;
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
			try { readTask?.Wait(2000); } catch { }
		}

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
				using var process = new Process {
					StartInfo = new ProcessStartInfo {
						FileName = path,
						Arguments = "-version",
						CreateNoWindow = true,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						UseShellExecute = false,
						WindowStyle = ProcessWindowStyle.Hidden
					}
				};
				process.Start();
				// Bounded, see ReadStdoutBounded: this one runs on the UI thread (diagnostics
				// report), where a synchronous drain of a wedged tool froze the window.
				using var ms = new MemoryStream();
				ReadStdoutBounded(process, ms, 5000, tool.ToString(), path);
				string firstLine = Encoding.UTF8.GetString(ms.ToArray())
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
