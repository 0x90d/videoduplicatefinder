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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using VDF.Core.FFTools.FFmpegNative;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	static class FfmpegEngine {
		public static readonly string FFmpegPath;
		const int TimeoutDuration = 15_000; //15 seconds
		public static FFHardwareAccelerationMode HardwareAccelerationMode;
		public static string CustomFFArguments = string.Empty;
		public static bool UseNativeBinding;
		static FfmpegEngine() => FFmpegPath = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) ?? string.Empty;

		public static unsafe byte[]? GetThumbnail(FfmpegSettings settings, bool extendedLogging) {

			try {
				if (UseNativeBinding) {
					bool isGrayByte = settings.GrayScale == 1;

					AVHWDeviceType HWDevice = HardwareAccelerationMode switch {
						FFHardwareAccelerationMode.vdpau => AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
						FFHardwareAccelerationMode.dxva2 => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
						FFHardwareAccelerationMode.vaapi => AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
						FFHardwareAccelerationMode.qsv => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
						FFHardwareAccelerationMode.cuda => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
						_ => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
					};

					using var vsd = new VideoStreamDecoder(settings.File, HWDevice);
					if (vsd.PixelFormat < 0 || vsd.PixelFormat >= AVPixelFormat.AV_PIX_FMT_NB)
						throw new Exception($"Invalid source pixel format");

					Size sourceSize = vsd.FrameSize;
					Size destinationSize = isGrayByte ? new Size(16, 16) : new Size(100, Convert.ToInt32(sourceSize.Height * (100 / (double)sourceSize.Width)));
					AVPixelFormat destinationPixelFormat = isGrayByte ? AVPixelFormat.AV_PIX_FMT_GRAY8 : AVPixelFormat.AV_PIX_FMT_BGRA;
					using var vfc =
						new VideoFrameConverter(sourceSize, vsd.PixelFormat, destinationSize, destinationPixelFormat);

					if (!vsd.TryDecodeFrame(out var srcFrame, settings.Position))
						throw new Exception($"Failed decoding frame at {settings.Position}");
					AVFrame convertedFrame = vfc.Convert(srcFrame);

					if (isGrayByte) {
						int length = ffmpeg.av_image_get_buffer_size(destinationPixelFormat, convertedFrame.width,
							convertedFrame.height, 1).ThrowExceptionIfError();
						byte[] data = new byte[length];
						Marshal.Copy((IntPtr)convertedFrame.data[0], data, 0, length);
						return data;
					}
					else {
						int width = convertedFrame.width;
						int height = convertedFrame.height;
						var totalBytes = width * height * 4;
						var rgbaBytes = new byte[totalBytes];
						int stride = convertedFrame.linesize[0];
						if (stride == width * 4) {
							Marshal.Copy((IntPtr)convertedFrame.data[0], rgbaBytes, 0, totalBytes);
						}
						else {
							var sourceOffset = 0;
							var destOffset = 0;
							var byteWidth = width * 4;
							for (var y = 0; y < height; y++) {
								Marshal.Copy((IntPtr)convertedFrame.data[0] + sourceOffset, rgbaBytes, destOffset, byteWidth);
								sourceOffset += stride;
								destOffset += byteWidth;
							}
						}
						var image = Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Bgra32>(rgbaBytes, width, height);
						using MemoryStream stream = new();
						image.Save(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
						bool equal = rgbaBytes.SequenceEqual(stream.ToArray());
						return stream.ToArray();
					}
				}
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed using native FFmpeg binding on '{settings.File}', try switching to process mode. Exception: {e}");
			}


			//https://docs.microsoft.com/en-us/dotnet/csharp/how-to/concatenate-multiple-strings#string-literals
			string ffmpegArguments = $" -hide_banner -loglevel {(extendedLogging ? "error" : "quiet")}" +
				$" -y -hwaccel {HardwareAccelerationMode} -ss {settings.Position} -i \"{settings.File}\"" +
				$" -t 1 -f {(settings.GrayScale == 1 ? "rawvideo -pix_fmt gray" : "mjpeg")} -vframes 1" +
				$" {(settings.GrayScale == 1 ? "-s 16x16" : "-vf scale=100:-1")} {CustomFFArguments} \"-\"";

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
					throw new TimeoutException($"FFmpeg timed out on file: {settings.File}");
				}
				else if (extendedLogging)
					process.WaitForExit(); // Because of asynchronous event handlers, see: https://github.com/dotnet/runtime/issues/18789

				if (process.ExitCode != 0)
					throw new FFInvalidExitCodeException($"FFmpeg exited with: {process.ExitCode}");

				bytes = ms.ToArray();
				if (bytes.Length == 0)
					bytes = null;   // Makes subsequent checks easier
				else if (settings.GrayScale == 1 && bytes.Length != 256) {
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
				string message = $"{((bytes == null) ? "ERROR: Failed to retrieve" : "WARNING: Problems while retrieving")} {(settings.GrayScale == 1 ? "graybytes" : "thumbnail")} from: {settings.File}";
				if (extendedLogging)
					message += $":{Environment.NewLine}{FFmpegPath} {ffmpegArguments}";
				Logger.Instance.Info($"{message}{errOut}");
			}
			return bytes;
		}
		internal static void GetGrayBytesFromVideo(FileEntry videoFile, List<float> positions, bool extendedLogging) {
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
