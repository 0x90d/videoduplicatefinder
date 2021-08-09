// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	static class FFProbeEngine {
		public static readonly string FFprobePath;
		const int TimeoutDuration = 15_000; //15 seconds
		static FFProbeEngine() {
#pragma warning disable CS8601 // Possible null reference assignment.
			FFprobePath = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFProbe);
#pragma warning restore CS8601 // Possible null reference assignment.
		}

		public static  MediaInfo? GetMediaInfo(string file) {
			using var process = new Process {
				StartInfo = new ProcessStartInfo {
					Arguments = $" -hide_banner -loglevel error -print_format json -sexagesimal -show_format -show_streams  \"{file}\"",
					FileName = FFprobePath,
					CreateNoWindow = true,
					RedirectStandardInput = true,
					WorkingDirectory = Path.GetDirectoryName(FFprobePath)!,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					WindowStyle = ProcessWindowStyle.Hidden
				}
			};
			try {
				process.EnableRaisingEvents = true;
				process.Start();
				if (!process.WaitForExit(TimeoutDuration)) { 
					Logger.Instance.Info($"FFprobe timed out on file '{file}'");
					throw new Exception();
				}
				using var ms = new MemoryStream();
				process.StandardOutput.BaseStream.CopyTo(ms);
				return FFProbeJsonReader.Read(ms.ToArray(), file);
			}
			catch (Exception) {
				try {
					if (process.HasExited == false)
						process.Kill();
				}
				catch { }
				return null;
			}
		}
	}
}

