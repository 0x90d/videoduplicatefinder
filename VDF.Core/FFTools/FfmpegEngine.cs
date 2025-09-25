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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

			const int N = 32;
			const int ExpectedBytes = N * N;
			bool isGrayByte = settings.GrayScale == 1;

			try {
				if (UseNativeBinding) {


					AVHWDeviceType HWDevice = HardwareAccelerationMode switch {
						FFHardwareAccelerationMode.vdpau => AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU,
						FFHardwareAccelerationMode.dxva2 => AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2,
						FFHardwareAccelerationMode.vaapi => AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI,
						FFHardwareAccelerationMode.qsv => AVHWDeviceType.AV_HWDEVICE_TYPE_QSV,
						FFHardwareAccelerationMode.cuda => AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA,
						FFHardwareAccelerationMode.videotoolbox => AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX,
						FFHardwareAccelerationMode.d3d11va => AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
						FFHardwareAccelerationMode.drm => AVHWDeviceType.AV_HWDEVICE_TYPE_DRM,
						//FFHardwareAccelerationMode.opencl => AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL, OpenCL support is irrelevant for frame extraction
						FFHardwareAccelerationMode.mediacodec => AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC,
						FFHardwareAccelerationMode.vulkan => AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
						_ => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
					};

					using var vsd = new VideoStreamDecoder(settings.File, HWDevice);
					if (vsd.PixelFormat < 0 || vsd.PixelFormat >= AVPixelFormat.AV_PIX_FMT_NB)
						throw new Exception($"Invalid source pixel format");

					Size sourceSize = vsd.FrameSize;
					Size destinationSize = isGrayByte ? new Size(N, N) :
						settings.Fullsize == 1 ?
							sourceSize :
							new Size(100, Convert.ToInt32(sourceSize.Height * (100 / (double)sourceSize.Width)));

					AVPixelFormat destinationPixelFrmt = isGrayByte ?
						AVPixelFormat.AV_PIX_FMT_GRAY8 :
						AVPixelFormat.AV_PIX_FMT_BGRA;

					using var vfc = new VideoFrameConverter(
										sourceSize: vsd.FrameSize,
										sourcePixelFormat: vsd.PixelFormat,
										destinationSize: destinationSize,
										destinationPixelFormat: destinationPixelFrmt,
										quality: VideoFrameConverter.ScaleQuality.Bicubic,
										bitExact: false);

					if (!vsd.TryDecodeFrame(out var srcFrame, settings.Position))
						throw new Exception($"TryDecodeFrame failed at pos={settings.Position} for '{settings.File}'. srcPixFmt={vsd.PixelFormat} size={sourceSize.Width}x{sourceSize.Height}");
					AVFrame convertedFrame = vfc.Convert(srcFrame);

					if (convertedFrame.data[0] == null)
						throw new Exception("Converted frame has no data[0] (null).");


					if (isGrayByte) {
						int width = convertedFrame.width; // should be 32
						if (convertedFrame.linesize[0] < width)
							throw new Exception($"Invalid linesize ({convertedFrame.linesize[0]}) for width {width}.");
						int height = convertedFrame.height; // should be 32
						int srcStride = convertedFrame.linesize[0]; // can be >= width (padding)
						IntPtr srcPtr = (IntPtr)convertedFrame.data[0];

						if (width != N || height != N)
							throw new Exception($"Unexpected size {width}x{height}, expected {N}x{N}.");

						byte[] outBuf = new byte[width * height]; // 1024
						for (int y = 0; y < height; y++) {
							// Source: y*stride bytes offset; Target: y*width bytes
							Marshal.Copy(srcPtr + y * srcStride, outBuf, y * width, width);
						}
						return outBuf;
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
						return stream.ToArray();
					}
				}
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed using native FFmpeg binding on '{settings.File}', try switching to process mode. Exception: {e}");
			}

			var psi = new ProcessStartInfo {
				FileName = FFmpegPath,
				CreateNoWindow = true,
				RedirectStandardInput = false,
				RedirectStandardOutput = true,
				WorkingDirectory = Path.GetDirectoryName(FFmpegPath)!,
				RedirectStandardError = extendedLogging,
				WindowStyle = ProcessWindowStyle.Hidden
			};

			psi.ArgumentList.Add("-hide_banner");
			psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add((extendedLogging ? "error" : "quiet"));

			psi.ArgumentList.Add("-nostdin");

			if (HardwareAccelerationMode != FFHardwareAccelerationMode.none) {
				psi.ArgumentList.Add("-hwaccel");
				psi.ArgumentList.Add(HardwareAccelerationMode.ToString());
			}			

			// -ss before -i (faster seek, may be less accurate; OK for frame sampling)
			psi.ArgumentList.Add("-ss"); psi.ArgumentList.Add(settings.Position.ToString(null, CultureInfo.InvariantCulture));
			psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(FFToolsUtils.LongPathFix(settings.File));

			// Filter chain: scale + gray
			if (isGrayByte) {
				psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"scale={N}:{N}:flags=bicubic,format=gray");
				psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
				psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("gray");
			}
			else {
				if (settings.Fullsize != 1) {
					psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add("scale=100:-1");
				}
				psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("mjpeg");
			}

			psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");

			if (!string.IsNullOrWhiteSpace(CustomFFArguments)) {
				foreach (var item in CustomFFArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
					psi.ArgumentList.Add(item);
				}
			}
			psi.ArgumentList.Add("pipe:1"); // stdout

			////https://docs.microsoft.com/en-us/dotnet/csharp/how-to/concatenate-multiple-strings#string-literals
			//string ffmpegArguments = $" -hide_banner -loglevel {(extendedLogging ? "error" : "quiet")}" +
			//	$" -y -hwaccel {HardwareAccelerationMode} -ss {settings.Position} -i \"{FFToolsUtils.LongPathFix(settings.File)}\"" +
			//	$" -t 1 -f {(isGrayByte ? "rawvideo -pix_fmt gray" : "mjpeg")} -vframes 1" +
			//	$" {(isGrayByte ? "-s 16x16" : (settings.Fullsize == 1 ? string.Empty : "-vf scale=100:-1"))} {CustomFFArguments} \"-\"";

			using var process = new Process {
				StartInfo = psi
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
				else if (isGrayByte && bytes.Length != ExpectedBytes) {
					errOut += $"{Environment.NewLine}graybytes length != {ExpectedBytes} (got {bytes.Length})";
					bytes = null;
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
				string message = $"{((bytes == null) ? "ERROR: Failed to retrieve" : "WARNING: Problems while retrieving")} {(isGrayByte ? "graybytes" : "thumbnail")} from: {settings.File}";
				if (extendedLogging)
					message += $":{Environment.NewLine}{FFmpegPath} {psi.ArgumentList}";
				Logger.Instance.Info($"{message}{errOut}");
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
					return false;
				}
				if (!GrayBytesUtils.VerifyGrayScaleValues(data))
					tooDarkCounter++;
				videoFile.grayBytes.Add(position, data);
				videoFile.PHashes.Add(position, pHash.PerceptualHash.ComputePHashFromGray32x32(data));
			}
			if (tooDarkCounter == positions.Count) {
				videoFile.Flags.Set(EntryFlags.TooDark);
				Logger.Instance.Info($"ERROR: Graybytes too dark of: {videoFile.Path}");
				return false;
			}
			return true;
		}
	}

	internal struct FfmpegSettings {
		public byte GrayScale;
		public byte Fullsize;
		public string File;
		public TimeSpan Position;
	}
}
