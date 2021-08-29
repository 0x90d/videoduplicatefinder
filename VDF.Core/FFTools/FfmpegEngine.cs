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

		public static byte[]? GetThumbnail(FfmpegSettings settings, bool extendedLogging) {
			string ffmpegArguments = $" -hide_banner -loglevel {(extendedLogging ? "error" : "panic")} -y {(UseCuda ? "-hwaccel cuda" : string.Empty)} -ss {settings.Position} -i \"{settings.File}\" -t 1 -f {(settings.GrayScale == 1 ? "rawvideo -pix_fmt gray" : "mjpeg")} -vframes 1 {(settings.GrayScale == 1 ? "-s 16x16" : "-vf scale=100:-1")} \"-\"";
			using var process = new Process {
				StartInfo = new ProcessStartInfo {
					Arguments = ffmpegArguments,
					FileName = FFmpegPath,
					CreateNoWindow = true,
					RedirectStandardInput = false,
					RedirectStandardOutput = true,
					WorkingDirectory = Path.GetDirectoryName(FFmpegPath)!,
					RedirectStandardError = extendedLogging,
					WindowStyle = ProcessWindowStyle.Hidden
				}
			};
			string errOut = string.Empty;
			byte[]? bytes = null;
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

				if (!process.WaitForExit(TimeoutDuration)) {
					errOut += $"{Environment.NewLine}FFmpeg timed out";
					throw new Exception();
				}
				else if (extendedLogging)
					process.WaitForExit(); // Because of asynchronous event handlers, see: https://github.com/dotnet/runtime/issues/18789

				bytes = ms.ToArray();
				if (bytes?.Length == 0)
					bytes = null;   // Makes subsequent checks easier
				else if (settings.GrayScale == 1 && bytes?.Length != 16 * 16) {
					bytes = null;
					errOut += $"{Environment.NewLine}graybytes length != 256";
				}
			}
			catch (Exception e) {
				errOut += $"{Environment.NewLine}{e.Message}";
				try {
					if (process.HasExited == false)
						process.Kill();
				}
				catch { }
				bytes = null;
			}
			if (bytes == null || errOut.Length > 0) {
				string message = $"{((bytes == null) ? "ERROR: Failed to retrieve " : "WARNING: Problems while retrieving")} {(settings.GrayScale == 1 ? "graybytes" : "thumbnail")} from: {settings.File}";
				if (extendedLogging)
					message += $":{Environment.NewLine}{FFmpegPath}{ffmpegArguments}";
				Logger.Instance.Info($"{message}{errOut}");
			}
			return bytes;
		}
		public static void GetGrayBytesFromVideo(FileEntry videoFile, List<float> positions, bool extendedLogging) {
			int tooDarkCounter = 0;

			for (int i = 0; i < positions.Count; i++) {
				double position = videoFile.GetGrayBytesIndex(positions[i]);
				if (videoFile.grayBytes.ContainsKey(position))
					continue;

				var data = GetThumbnail(new FfmpegSettings {
					File = videoFile.Path,
					Position = TimeSpan.FromSeconds(position),
					GrayScale = 1,
				}, extendedLogging);
				if (data == null) {
					videoFile.Flags.Set(EntryFlags.ThumbnailError);
					return;
				}
				if (!GrayBytesUtils.VerifyGrayScaleValues(data))
					tooDarkCounter++;
				videoFile.grayBytes.Add(position, data);
			}
			if (tooDarkCounter == positions.Count) {
				videoFile.Flags.Set(EntryFlags.TooDark);
				Logger.Instance.Info($"ERROR: Graybytes too dark of: {videoFile.Path}");
			}
		}
	}

	struct FfmpegSettings {
		public byte GrayScale;
		public string File;
		public TimeSpan Position;
	}
}
