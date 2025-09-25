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

		/// <summary>
		/// Gets path of ffprobe or ffmpeg
		/// </summary>
		/// <param name="tool"></param>
		/// <returns>path or null if not found</returns>
		internal static string? GetPath(FFTool tool) {

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

		/// <summary>
		/// Returns a path with long path prefix
		/// </summary>
		/// <param name="path">Path of the file</param>
		/// <returns>On Windows: path with long path prefix. Otherwise same as input</returns>
		internal static string LongPathFix(string path) {
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return path;
			//Check if path is UNC, see https://github.com/0x90d/videoduplicatefinder/issues/443
			if (path.StartsWith('\\'))
				return $"\\\\?\\UNC\\{path.TrimStart('\\')}";
			return $"\\\\?\\{path}";
		}
	}

}
