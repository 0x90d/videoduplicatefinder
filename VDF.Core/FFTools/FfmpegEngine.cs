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
	static class FfmpegEngine {
		public static readonly string FFmpegPath;
		const int TimeoutDuration = 15_000; //15 seconds
		public static bool UseCuda;
		static FfmpegEngine() => FFmpegPath = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) ?? string.Empty;

		public static byte[]? GetThumbnail(FfmpegSettings settings) {
			using var process = new Process {
				StartInfo = new ProcessStartInfo {
					Arguments = $" -hide_banner -loglevel panic -y {(UseCuda ? "-hwaccel cuda" : string.Empty)} -ss {settings.Position} -i \"{settings.File}\" -t 1 -f {(settings.GrayScale == 1 ? "rawvideo -pix_fmt gray" : "mjpeg")} -vframes 1 {(settings.GrayScale == 1 ? "-s 16x16" : "-vf scale=100:-1")} \"-\"",
					FileName = FFmpegPath,
					CreateNoWindow = true,
					RedirectStandardInput = false,
					RedirectStandardOutput = true,
					WorkingDirectory = Path.GetDirectoryName(FFmpegPath)!,
					RedirectStandardError = true,
					WindowStyle = ProcessWindowStyle.Hidden
				}
			};
			try {
				process.EnableRaisingEvents = true;
				process.Start();
				using var ms = new MemoryStream();
				process.StandardOutput.BaseStream.CopyTo(ms);
				if (!process.WaitForExit(TimeoutDuration)) {
					Logger.Instance.Info($"FFmpeg timed out on file '{settings.File}'");
					throw new Exception();
				}
				return ms.ToArray();
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
		public static void GetGrayBytesFromVideo(FileEntry videoFile, List<float> positions) {
			int tooDarkCounter = 0;

			for (int i = 0; i < positions.Count; i++) {
				double position = videoFile.GetGrayBytesIndex(positions[i]);
				if (videoFile.grayBytes.ContainsKey(position))
					continue;

				var data = GetThumbnail(new FfmpegSettings {
					File = videoFile.Path,
					Position = TimeSpan.FromSeconds(position),
					GrayScale = 1
				});
				if (data == null || data.Length == 0) {
					videoFile.Flags.Set(EntryFlags.ThumbnailError);
					return;
				}
				if (!GrayBytesUtils.VerifyGrayScaleValues(data))
					tooDarkCounter++;
				videoFile.grayBytes.Add(position, data);
			}
			if (tooDarkCounter == positions.Count)
				videoFile.Flags.Set(EntryFlags.TooDark);

		}
	}

	struct FfmpegSettings {
		public byte GrayScale;
		public string File;
		public TimeSpan Position;
	}
}
