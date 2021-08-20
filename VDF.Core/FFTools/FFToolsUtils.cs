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

using System.Runtime.InteropServices;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	static class FFToolsUtils {

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
		public static string? GetPath(FFTool tool) {

			if (File.Exists($"{CoreUtils.CurrentFolder}\\bin\\{(tool == FFTool.FFmpeg ? ffMpegPlatformName : ffProbePlatformName)}"))
				return $"{CoreUtils.CurrentFolder}\\bin\\{(tool == FFTool.FFmpeg ? ffMpegPlatformName : ffProbePlatformName)}";
			if (File.Exists(Path.Combine(CoreUtils.CurrentFolder, tool == FFTool.FFmpeg ? ffMpegPlatformName : ffProbePlatformName)))
				return Path.Combine(CoreUtils.CurrentFolder, tool == FFTool.FFmpeg ? ffMpegPlatformName : ffProbePlatformName);

			var environmentVariables = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
			if (environmentVariables == null) return null;

			foreach (var path in environmentVariables) {
				if (!Directory.Exists(path))
					continue;

				try {
					FileInfo[] files = new DirectoryInfo(path).GetFiles(tool == FFTool.FFmpeg ? ffMpegPlatformName : ffProbePlatformName, new EnumerationOptions {
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
	}

}
