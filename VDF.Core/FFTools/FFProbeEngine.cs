// /*
//     Copyright (C) 2025 0x90d
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
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	static class FFProbeEngine {
		public static readonly string FFprobePath;
		const int TimeoutDuration = 15_000; //15 seconds
		static FFProbeEngine() => FFprobePath = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFProbe) ?? string.Empty;

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
						if (e.Data?.Length > 0)
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
				string message = $"{((mediaInfo == null) ? "ERROR: Failed to retrieve" : "WARNING: Problems while retrieving")} media info from: {file}";
				if (extendedLogging) {
					var args = string.Join(" ", process.StartInfo.ArgumentList);
					message += $":{Environment.NewLine}{FFprobePath} {args}";
				}
				Logger.Instance.Info($"{message}{errOut}");
			}
			return mediaInfo;
		}
	}
}

