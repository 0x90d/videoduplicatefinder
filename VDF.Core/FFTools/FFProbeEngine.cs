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
using System.Globalization;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	static class FFProbeEngine {
		static string _FFprobePath = string.Empty;
		// Re-probes when unresolved (or the binary vanished) — see FfmpegEngine.FFmpegPath (issue #788).
		public static string FFprobePath {
			get {
				if (_FFprobePath.Length == 0 || !File.Exists(_FFprobePath))
					_FFprobePath = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFProbe) ?? string.Empty;
				return _FFprobePath;
			}
		}
		const int TimeoutDuration = 15_000; //15 seconds

		public static MediaInfo? GetMediaInfo(string file, bool extendedLogging) {
			var psi = new ProcessStartInfo {
				FileName = FFprobePath,
				CreateNoWindow = true,
				RedirectStandardInput = false,
				WorkingDirectory = Path.GetDirectoryName(FFprobePath)!,
				RedirectStandardOutput = true,
				RedirectStandardError = extendedLogging,
				WindowStyle = ProcessWindowStyle.Hidden
			};

			psi.ArgumentList.Add("-hide_banner");
			psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add((extendedLogging ? "error" : "quiet"));
			psi.ArgumentList.Add("-print_format"); psi.ArgumentList.Add("json");
			psi.ArgumentList.Add("-sexagesimal");
			psi.ArgumentList.Add("-show_format");
			psi.ArgumentList.Add("-show_streams");
			psi.ArgumentList.Add(FFToolsUtils.LongPathFix(file));

			using var process = new Process {
				StartInfo = psi
			};
			MediaInfo? mediaInfo = null;
			string errOut = string.Empty;
			try {
				process.EnableRaisingEvents = true;
				process.Start();
				if (extendedLogging) {
					process.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => {
						// "Referenced QT chapter track not found" is a benign QuickTime quirk
						// emitted by ffprobe at error level. The video stream is fine; the
						// message just floods the log on otherwise-valid MP4/MOV files.
						if (e.Data?.Length > 0 && !e.Data.Contains("Referenced QT chapter track not found", StringComparison.Ordinal))
							errOut += Environment.NewLine + e.Data;
					});
					process.BeginErrorReadLine();
				}
				using var ms = new MemoryStream();
				process.StandardOutput.BaseStream.CopyTo(ms);
				if (!process.WaitForExit(TimeoutDuration))
					throw new TimeoutException($"FFprobe timed out on file: {file}");
				else if (extendedLogging)
					process.WaitForExit(); // Because of asynchronous event handlers, see: https://github.com/dotnet/runtime/issues/18789

				if (process.ExitCode != 0)
					throw new FFInvalidExitCodeException($"FFprobe exited with: {process.ExitCode}");

				mediaInfo = FFProbeJsonReader.Read(ms.ToArray(), file);
			}
			catch (Exception e) {
				errOut += $"{Environment.NewLine}{e.Message}";
				try {
					if (process.HasExited == false)
						process.Kill();
				}
				catch { }
				mediaInfo = null;
			}
			if (mediaInfo == null || errOut.Length > 0) {
				string message = $"{((mediaInfo == null) ? "Failed to retrieve" : "Problems while retrieving")} media info from: {file}";
				if (extendedLogging) {
					var args = string.Join(" ", process.StartInfo.ArgumentList);
					message += $":{Environment.NewLine}{FFprobePath} {args}";
				}
				Logger.Instance.Warn($"{message}{errOut}");
			}
			return mediaInfo;
		}

		/// <summary>
		/// Reads the container-level <c>creation_time</c> tag from a media file. Used for HEIC/HEIF
		/// images, whose EXIF date is lost when FFmpeg transcodes them to JPEG for hashing and
		/// thumbnails. Returns <c>null</c> when the tag is absent, empty or unparseable.
		/// </summary>
		public static DateTime? GetCreationTime(string file) {
			var psi = new ProcessStartInfo {
				FileName = FFprobePath,
				CreateNoWindow = true,
				RedirectStandardInput = false,
				WorkingDirectory = Path.GetDirectoryName(FFprobePath)!,
				RedirectStandardOutput = true,
				RedirectStandardError = false,
				WindowStyle = ProcessWindowStyle.Hidden
			};

			psi.ArgumentList.Add("-hide_banner");
			psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("quiet");
			psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("format_tags=creation_time");
			psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
			psi.ArgumentList.Add(FFToolsUtils.LongPathFix(file));

			using var process = new Process { StartInfo = psi };
			try {
				process.Start();
				string output = process.StandardOutput.ReadToEnd();
				if (!process.WaitForExit(TimeoutDuration)) {
					try { if (!process.HasExited) process.Kill(); } catch { }
					return null;
				}
				if (process.ExitCode != 0)
					return null;

				output = output.Trim();
				if (output.Length == 0)
					return null;

				// FFprobe emits creation_time as ISO 8601, typically UTC (e.g. "2023-08-15T12:34:56.000000Z").
				if (DateTime.TryParse(output, CultureInfo.InvariantCulture,
						DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
					return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
			}
			catch (Exception e) {
				Logger.Instance.Warn($"Failed reading creation_time from '{file}': {e.Message}");
				try { if (!process.HasExited) process.Kill(); } catch { }
			}
			return null;
		}
	}
}

