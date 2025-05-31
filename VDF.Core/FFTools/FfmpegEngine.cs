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
	internal static class FfmpegEngine {
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
						FFHardwareAccelerationMode.videotoolbox => AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
						FFHardwareAccelerationMode.d3d11va => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
						FFHardwareAccelerationMode.drm => AVHWDeviceType.AV_HWDEVICE_TYPE_DRM,
						FFHardwareAccelerationMode.opencl => AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL,
						FFHardwareAccelerationMode.mediacodec => AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
						FFHardwareAccelerationMode.vulkan => AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
						_ => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
					};

					using var vsd = new VideoStreamDecoder(settings.File, HWDevice);
					if (vsd.PixelFormat < 0 || vsd.PixelFormat >= AVPixelFormat.AV_PIX_FMT_NB)
						throw new Exception($"Invalid source pixel format");

					Size sourceSize = vsd.FrameSize;
					Size destinationSize = isGrayByte ? new Size(16, 16) :
						settings.Fullsize == 1 ?
							sourceSize :
							new Size(100, Convert.ToInt32(sourceSize.Height * (100 / (double)sourceSize.Width)));
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
						// bool equal = rgbaBytes.SequenceEqual(stream.ToArray()); // This line seems unused, consider removing
						return stream.ToArray();
					}
				}
			}
			catch (Exception e) {
				Logger.Instance.Info($"WARNING: Failed using native FFmpeg binding on '{settings.File}', try switching to process mode. Exception: {e}");
			}


			//https://docs.microsoft.com/en-us/dotnet/csharp/how-to/concatenate-multiple-strings#string-literals
			string ffmpegArguments = $" -hide_banner -loglevel {(extendedLogging ? "error" : "quiet")}" +
				$" -y -hwaccel {HardwareAccelerationMode} -ss {settings.Position} -i \"{FFToolsUtils.LongPathFix(settings.File)}\"" +
				$" -t 1 -f {(settings.GrayScale == 1 ? "rawvideo -pix_fmt gray" : "mjpeg")} -vframes 1" +
				$" {(settings.GrayScale == 1 ? "-s 16x16" : (settings.Fullsize == 1 ? string.Empty : "-vf scale=100:-1"))} {CustomFFArguments} \"-\"";

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
					process.WaitForExit();

				if (process.ExitCode != 0)
					throw new FFInvalidExitCodeException($"FFmpeg exited with: {process.ExitCode}");

				bytes = ms.ToArray();
				if (bytes.Length == 0)
					bytes = null;
				else if (settings.GrayScale == 1 && bytes.Length != 256) {
					bytes = null;
					// This specific detail will be part of the consolidated log message if extendedLogging is true
					if(extendedLogging) errOut += $"{Environment.NewLine}CustomDetail: graybytes length != 256";
				}
			}
			catch (Exception e) {
				if(extendedLogging) errOut += $"{Environment.NewLine}ExceptionDetail: {e.GetType().Name}: {e.Message}";
				bytes = null;
			}
			finally {
				try {
					if (process != null && !process.HasExited) {
						process.Kill();
						if(extendedLogging) errOut += $"{Environment.NewLine}CustomDetail: Process was killed due to an issue or timeout.";
					}
				}
				catch {/* Best effort */}
			}

			if (bytes == null || (extendedLogging && !string.IsNullOrEmpty(errOut))) {
				string prefix = bytes == null ? "ERROR: " : "WARNING: ";
				string mainMessage = bytes == null ? "Failed to retrieve" : "Problems while retrieving";

				string logMessage = $"{prefix}{mainMessage} {(settings.GrayScale == 1 ? "graybytes" : "thumbnail")} from: {settings.File}";

				if (extendedLogging) {
					logMessage += $":{Environment.NewLine}FFmpeg Path: {FFmpegPath}{Environment.NewLine}Arguments: {ffmpegArguments}";
                    if (!string.IsNullOrEmpty(errOut)) {
                        logMessage += $"{Environment.NewLine}Stderr: {errOut}";
                    }
                } else if (bytes == null && string.IsNullOrEmpty(errOut)) {
                    logMessage += ". No extended error output.";
                }
				Logger.Instance.Info(logMessage);
			}
			return bytes;
		}
		internal static bool GetGrayBytesFromVideo(FileEntry videoFile, List<float> positions, bool extendedLogging) {
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
					// No specific log here as GetThumbnail would have logged the failure.
					return false;
				}
				if (!GrayBytesUtils.VerifyGrayScaleValues(data))
					tooDarkCounter++;
				videoFile.grayBytes.Add(position, data);
			}
			if (tooDarkCounter == positions.Count) {
				videoFile.Flags.Set(EntryFlags.TooDark);
				Logger.Instance.Info($"ERROR: Graybytes too dark of: {videoFile.Path}");
				return false;
			}
			return true;
		}

		public static Dictionary<double, byte[]?> GetThumbnailsForSegment(string videoPath, TimeSpan segmentStart, TimeSpan segmentEnd, int numberOfThumbnails, bool extendedLogging) {
			var result = new Dictionary<double, byte[]?>();

			if (numberOfThumbnails <= 0) {
				Logger.Instance.Info("ERROR: GetThumbnailsForSegment: numberOfThumbnails must be greater than 0.");
				return result;
			}

			if (segmentStart >= segmentEnd) {
				Logger.Instance.Info("ERROR: GetThumbnailsForSegment: segmentStart must be less than segmentEnd.");
				return result;
			}

			if (string.IsNullOrEmpty(videoPath)) {
				Logger.Instance.Info("ERROR: GetThumbnailsForSegment: videoPath cannot be null or empty.");
				return result;
			}

			if (string.IsNullOrEmpty(FFmpegPath)) {
				Logger.Instance.Info("ERROR: GetThumbnailsForSegment: FFmpeg path is not configured.");
				return result;
			}

			List<TimeSpan> timestamps = new List<TimeSpan>();
			TimeSpan segmentDuration = segmentEnd - segmentStart;

			if (numberOfThumbnails == 1) {
				timestamps.Add(segmentStart + TimeSpan.FromSeconds(segmentDuration.TotalSeconds / 2));
			} else {
				for (int i = 0; i < numberOfThumbnails; i++) {
					double stepRatio = (double)i / (numberOfThumbnails - 1);
					TimeSpan timestamp = segmentStart + TimeSpan.FromSeconds(segmentDuration.TotalSeconds * stepRatio);
					timestamps.Add(timestamp);
				}
			}

			foreach (TimeSpan ts in timestamps) {
				var settings = new FfmpegSettings {
					File = videoPath,
					Position = ts,
					GrayScale = 0,
					Fullsize = 0
				};

				byte[]? thumbnailData = GetThumbnail(settings, extendedLogging);

				if (thumbnailData != null && thumbnailData.Length > 0) {
					result.Add(ts.TotalSeconds, thumbnailData);
				} else {
					Logger.Instance.Info($"WARNING: GetThumbnailsForSegment: Failed to retrieve thumbnail for {videoPath} at {ts}.");
				}
			}

			return result;
		}
	}

	internal struct FfmpegSettings {
		public byte GrayScale;
		public byte Fullsize;
		public string File;
		public TimeSpan Position;
	}
}
