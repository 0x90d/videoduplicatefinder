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
		static string _FFmpegPath = string.Empty;
		// Re-probes when unresolved (or the binary vanished): a once-only static cache made
		// an FFmpeg installed/downloaded while the app was running invisible until restart,
		// so the GUI kept offering the download forever (issue #788).
		public static string FFmpegPath {
			get {
				if (_FFmpegPath.Length == 0 || !File.Exists(_FFmpegPath))
					_FFmpegPath = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) ?? string.Empty;
				return _FFmpegPath;
			}
		}
		const int TimeoutDuration = 15_000; //15 seconds
		public static FFHardwareAccelerationMode HardwareAccelerationMode;
		public static string CustomFFArguments = string.Empty;

		static bool _useNativeBinding;
		public static bool UseNativeBinding {
			get => _useNativeBinding;
			set {
				_useNativeBinding = value;
				// Reset the per-scan native-health state whenever native binding is (re)configured,
				// i.e. at the start of each scan.
				_nativeConsecutiveFailures = 0;
				_nativeDisabledForSession = false;
				_vulkanNativeWarningLogged = false;
			}
		}

		// Native-binding health. When the libraries load but native operations keep failing
		// (e.g. a hardware-decode mismatch — issue #795), fall back to process mode for the
		// rest of the scan after a few consecutive failures, with one summary message instead
		// of a per-file stack-trace storm. A native success resets the counter so an isolated
		// bad file doesn't disable native for the whole library.
		static int _nativeConsecutiveFailures;
		static bool _nativeDisabledForSession;
		const int NativeFailureThreshold = 5;

		/// <summary>True when a native FFmpeg operation should be attempted.</summary>
		static bool ShouldUseNativeBinding =>
			UseNativeBinding && !_nativeDisabledForSession && FFmpegNative.FFmpegHelper.CanLoadNativeLibraries;

		static void RecordNativeSuccess() => _nativeConsecutiveFailures = 0;

		static void RecordNativeFailure(string file, Exception e) {
			if (_nativeDisabledForSession)
				return;
			int n = ++_nativeConsecutiveFailures;
			string detail = BuildNativeFailureDetail(e);
			if (n >= NativeFailureThreshold) {
				_nativeDisabledForSession = true;
				Logger.Instance.Info(
					$"Native FFmpeg binding failed on {n} consecutive files; using process mode for the rest of this scan. " +
					$"Last error on '{file}': {e.GetType().Name}: {e.Message}.{detail} " +
					$"If this persists, set hardware acceleration to 'none' or disable 'Use native FFmpeg binding'.");
			}
			else {
				Logger.Instance.Info($"Failed using native FFmpeg binding on '{file}', switching to process mode. Exception: {e}{detail}");
			}
		}

		/// <summary>
		/// Builds the extra diagnostic suffix for a native failure: the FFmpeg log lines captured
		/// on this thread for the failed file (otherwise lost by the native binding) plus a
		/// classified, plain-language hint about the likely cause. Empty when nothing useful was
		/// captured and the cause is unknown.
		/// </summary>
		static string BuildNativeFailureDetail(Exception e) {
			string diagnostics = FfmpegLogCapture.GetRecent();
			string? hint = FfmpegErrorClassifier.Classify(
				diagnostics.Length > 0 ? $"{diagnostics} {e.Message}" : e.Message);
			string detail = string.Empty;
			if (diagnostics.Length > 0)
				detail += $" FFmpeg log: {diagnostics}.";
			if (hint != null)
				detail += $" Hint: {hint}";
			return detail;
		}

		const int DefaultJpegQuality = 90;


		// Vulkan hardware decoding through the native FFmpeg binding segfaults the whole
		// process on at least some NVIDIA setups (#799) — a native crash we cannot catch.
		// The CLI path runs FFmpeg out-of-process, so a crash there is isolated and merely
		// fails the file, but the native path takes the app down with it. Guard the native
		// binding by decoding in software when Vulkan is requested; the warning is emitted
		// once per scan instead of once per file.
		static bool _vulkanNativeWarningLogged;

		internal static AVHWDeviceType GetConfiguredHardwareDeviceType() {
			if (HardwareAccelerationMode == FFHardwareAccelerationMode.vulkan) {
				if (!_vulkanNativeWarningLogged) {
					_vulkanNativeWarningLogged = true;
					Logger.Instance.Info(
						"Vulkan hardware acceleration is not supported with the native FFmpeg binding " +
						"(it crashes the process on some drivers, #799); decoding in software instead. " +
						"Disable 'Use native FFmpeg binding' to run Vulkan via the CLI, or pick another " +
						"hardware acceleration mode such as 'cuda'.");
				}
				return AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
			}
			return HardwareAccelerationMode switch {
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
				_ => AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
			};
		}

		/// <summary>
		/// Copies a 32x32 GRAY8 frame produced by <see cref="VideoFrameConverter"/> into a
		/// freshly-allocated 1024-byte buffer. swscale uses an aligned padded destination
		/// (linesize >= width); the common case is linesize == 32 because we asked for
		/// align=0 and 32 is already aligned, in which case a single copy is enough.
		/// </summary>
		static unsafe byte[] ExtractGray32FromFrame(AVFrame convertedFrame) {
			const int N = 32;
			int width = convertedFrame.width;
			int height = convertedFrame.height;
			if (width != N || height != N)
				throw new Exception($"Unexpected size {width}x{height}, expected {N}x{N}.");
			if (convertedFrame.data[0] == null)
				throw new Exception("Converted frame has no data[0] (null).");
			int srcStride = convertedFrame.linesize[0];
			if (srcStride < width)
				throw new Exception($"Invalid linesize ({srcStride}) for width {width}.");

			byte[] outBuf = new byte[width * height];
			fixed (byte* destPtr = outBuf) {
				byte* sourcePtr = convertedFrame.data[0];
				if (srcStride == width) {
					Buffer.MemoryCopy(sourcePtr, destPtr, width * height, width * height);
				}
				else {
					for (int y = 0; y < height; y++)
						Buffer.MemoryCopy(sourcePtr + (y * srcStride), destPtr + (y * width), width, width);
				}
			}
			return outBuf;
		}

		static int CountMissingGrayBytePositions(FileEntry videoFile, List<float> positions, double maxSamplingDurationSeconds) {
			int missing = 0;
			for (int i = 0; i < positions.Count; i++) {
				double position = videoFile.GetGrayBytesIndex(positions[i], maxSamplingDurationSeconds);
				if (!videoFile.grayBytes.ContainsKey(position))
					missing++;
			}
			return missing;
		}

		/// <summary>
		/// Opens a single <see cref="VideoStreamDecoder"/> and a single <see cref="VideoFrameConverter"/>
		/// for the file, then walks the requested positions reusing both. This avoids the per-position
		/// avformat_open_input + sws_getContext cost of looping <see cref="GetThumbnail"/>.
		///
		/// On any FFmpeg error we abort and return false; the caller falls back to the per-sample
		/// CLI/native path so partial extraction still succeeds. Already-cached positions are skipped.
		/// </summary>
		static unsafe bool TryGetGrayBytesFromVideoNativeBatch(
			FileEntry videoFile,
			List<float> positions,
			double maxSamplingDurationSeconds,
			ref int tooDarkCounter,
			Action<int>? onSampleComplete) {
			const int N = 32;
			try {
				FfmpegLogCapture.Reset();
				using var vsd = new VideoStreamDecoder(videoFile.Path, GetConfiguredHardwareDeviceType());
				VideoFrameConverter? converter = null;
				Size converterSourceSize = default;
				AVPixelFormat converterSrcFmt = AVPixelFormat.AV_PIX_FMT_NONE;
				try {
					for (int i = 0; i < positions.Count; i++) {
						double position = videoFile.GetGrayBytesIndex(positions[i], maxSamplingDurationSeconds);
						if (videoFile.grayBytes.ContainsKey(position)) {
							onSampleComplete?.Invoke(i + 1);
							continue;
						}

						if (!vsd.TryDecodeFrame(out var srcFrame, TimeSpan.FromSeconds(position)))
							throw new Exception($"TryDecodeFrame failed at pos={position} for '{videoFile.Path}'");

						// HW decode reports the real (downloaded) sw_format on the frame itself,
						// not on the codec context, so we read it post-decode. SW decode keeps it
						// stable on the codec context.
						Size sourceSize = new(
							srcFrame.width > 0 ? srcFrame.width : vsd.FrameSize.Width,
							srcFrame.height > 0 ? srcFrame.height : vsd.FrameSize.Height);
						AVPixelFormat srcPixFmt = vsd.IsHardwareDecode ? (AVPixelFormat)srcFrame.format : vsd.PixelFormat;
						if (srcPixFmt < 0 || srcPixFmt >= AVPixelFormat.AV_PIX_FMT_NB)
							throw new Exception($"Invalid source pixel format {srcPixFmt}");
						if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
							throw new Exception($"Invalid source frame dimensions {sourceSize.Width}x{sourceSize.Height}");

						// Reuse the SwsContext across positions when the source layout is unchanged.
						// In practice this is the common case for the same file; the rebuild branch
						// only fires if HW decode hands us a different sw_format on a later frame.
						if (converter == null || sourceSize != converterSourceSize || srcPixFmt != converterSrcFmt) {
							converter?.Dispose();
							converter = new VideoFrameConverter(
								sourceSize, srcPixFmt,
								new Size(N, N), AVPixelFormat.AV_PIX_FMT_GRAY8,
								VideoFrameConverter.ScaleQuality.Bicubic, bitExact: false);
							converterSourceSize = sourceSize;
							converterSrcFmt = srcPixFmt;
						}

						AVFrame convertedFrame = converter.Convert(srcFrame);
						byte[] data = ExtractGray32FromFrame(convertedFrame);

						if (!GrayBytesUtils.VerifyGrayScaleValues(data))
							tooDarkCounter++;
						videoFile.grayBytes.Add(position, data);
						videoFile.PHashes.Add(position, pHash.PerceptualHash.ComputePHashFromGray32x32(data));
						onSampleComplete?.Invoke(i + 1);
					}
				}
				finally {
					converter?.Dispose();
				}
				RecordNativeSuccess();
				return true;
			}
			catch (Exception e) {
				// One failure recorded per video file (not per position) so the session
				// circuit breaker reflects per-file native health (issues #793/#795). The
				// per-sample fallback below still re-attempts native but does not record.
				RecordNativeFailure(videoFile.Path, e);
				return false;
			}
		}

		/// <summary>
		/// Extracts one 32x32 grayscale frame per position, opening a single decoder and
		/// reusing one sws context for the whole file instead of paying the open/seek/teardown
		/// cost per frame. Returns an array aligned with <paramref name="positionsSeconds"/>;
		/// entries are null when that frame could not be decoded. Positions the native batch
		/// could not produce (or all of them, without the native binding) fall back to the
		/// per-frame <see cref="GetThumbnail"/> path, which itself falls back to the FFmpeg process.
		/// </summary>
		internal static unsafe byte[]?[] GetGrayFrames(string filePath, IReadOnlyList<double> positionsSeconds, bool extendedLogging) {
			const int N = 32;
			var frames = new byte[]?[positionsSeconds.Count];
			if (ShouldUseNativeBinding) {
				try {
					FfmpegLogCapture.Reset();
					using var vsd = new VideoStreamDecoder(filePath, GetConfiguredHardwareDeviceType());
					VideoFrameConverter? converter = null;
					Size converterSourceSize = default;
					AVPixelFormat converterSrcFmt = AVPixelFormat.AV_PIX_FMT_NONE;
					try {
						for (int i = 0; i < positionsSeconds.Count; i++) {
							if (!vsd.TryDecodeFrame(out var srcFrame, TimeSpan.FromSeconds(positionsSeconds[i])))
								continue;

							Size sourceSize = new(
								srcFrame.width > 0 ? srcFrame.width : vsd.FrameSize.Width,
								srcFrame.height > 0 ? srcFrame.height : vsd.FrameSize.Height);
							AVPixelFormat srcPixFmt = vsd.IsHardwareDecode ? (AVPixelFormat)srcFrame.format : vsd.PixelFormat;
							if (srcPixFmt < 0 || srcPixFmt >= AVPixelFormat.AV_PIX_FMT_NB ||
								sourceSize.Width <= 0 || sourceSize.Height <= 0)
								continue;

							if (converter == null || sourceSize != converterSourceSize || srcPixFmt != converterSrcFmt) {
								converter?.Dispose();
								converter = new VideoFrameConverter(
									sourceSize, srcPixFmt,
									new Size(N, N), AVPixelFormat.AV_PIX_FMT_GRAY8,
									VideoFrameConverter.ScaleQuality.Bicubic, bitExact: false);
								converterSourceSize = sourceSize;
								converterSrcFmt = srcPixFmt;
							}

							frames[i] = ExtractGray32FromFrame(converter.Convert(srcFrame));
						}
					}
					finally {
						converter?.Dispose();
					}
					RecordNativeSuccess();
				}
				catch (Exception e) {
					// One failure recorded per video file; the per-frame fallback below still
					// re-attempts native but does not record (issues #793/#795).
					RecordNativeFailure(filePath, e);
				}
			}

			for (int i = 0; i < positionsSeconds.Count; i++) {
				frames[i] ??= GetThumbnail(new FfmpegSettings {
					File = filePath,
					Position = TimeSpan.FromSeconds(positionsSeconds[i]),
					GrayScale = 1
				}, extendedLogging);
			}
			return frames;
		}

		public static unsafe byte[]? GetThumbnail(FfmpegSettings settings, bool extendedLogging) {

			const int N = 32;
			const int ExpectedBytes = N * N;
			bool isGrayByte = settings.GrayScale == 1;

			try {
				if (ShouldUseNativeBinding) {

					FfmpegLogCapture.Reset();

					AVHWDeviceType HWDevice = settings.SoftwareDecodeOnly
						? AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
						: GetConfiguredHardwareDeviceType();

					using var vsd = new VideoStreamDecoder(settings.File, HWDevice);

					Size sourceSize = vsd.FrameSize;

					// Decode first so we know the real source pixel format. For HW decode
					// we can't know this up front — the downloaded sw_format depends on
					// the stream's bit depth (NV12 for 8-bit, P010LE for 10-bit HEVC, etc.).
					if (!vsd.TryDecodeFrame(out var srcFrame, settings.Position))
						throw new Exception($"TryDecodeFrame failed at pos={settings.Position} for '{settings.File}'. size={sourceSize.Width}x{sourceSize.Height}");

					AVPixelFormat srcPixFmt = vsd.IsHardwareDecode
						? (AVPixelFormat)srcFrame.format
						: vsd.PixelFormat;
					if (srcPixFmt < 0 || srcPixFmt >= AVPixelFormat.AV_PIX_FMT_NB)
						throw new Exception($"Invalid source pixel format {srcPixFmt}");

					if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
						throw new Exception($"Invalid source frame dimensions {sourceSize.Width}x{sourceSize.Height}.");

					Size destinationSize = isGrayByte ? new Size(N, N) :
						settings.Fullsize == 1 ?
							sourceSize :
							ScaleToMaxWidth(sourceSize, settings.MaxWidth > 0 ? settings.MaxWidth : 100);

					AVPixelFormat destinationPixelFrmt = isGrayByte ?
						AVPixelFormat.AV_PIX_FMT_GRAY8 :
						AVPixelFormat.AV_PIX_FMT_YUVJ420P;

					using var vfc = new VideoFrameConverter(
										sourceSize: sourceSize,
										sourcePixelFormat: srcPixFmt,
										destinationSize: destinationSize,
										destinationPixelFormat: destinationPixelFrmt,
										quality: VideoFrameConverter.ScaleQuality.Bicubic,
										bitExact: false);

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
						fixed (byte* destPtr = outBuf) {
							byte* sourcePtr = (byte*)srcPtr;
							for (int y = 0; y < height; y++) {
								// Source: y*stride bytes offset; Target: y*width bytes
								Buffer.MemoryCopy(sourcePtr + (y * srcStride), destPtr + (y * width), width, width);
							}
						}
						return outBuf;
					}
					else {
						if (convertedFrame.width <= 0 || convertedFrame.height <= 0)
							throw new Exception($"Invalid converted frame dimensions {convertedFrame.width}x{convertedFrame.height}.");
						return JpegFrameEncoder.Encode(convertedFrame,
							settings.JpegQuality > 0 ? settings.JpegQuality : DefaultJpegQuality);
					}
				}
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed using native FFmpeg binding on '{settings.File}', try switching to process mode. Exception: {e}{BuildNativeFailureDetail(e)}");
			}

			var psi = new ProcessStartInfo {
				FileName = FFmpegPath,
				CreateNoWindow = true,
				RedirectStandardInput = false,
				RedirectStandardOutput = true,
				WorkingDirectory = Path.GetDirectoryName(FFmpegPath)!,
				// Always capture stderr: when FFmpeg fails, its error output is the only
				// diagnostic there is. Logged on failure regardless of the logging setting
				// (issue #780 — 'exited with: 134' with no further detail is undebuggable).
				RedirectStandardError = true,
				WindowStyle = ProcessWindowStyle.Hidden
			};

			psi.ArgumentList.Add("-hide_banner");
			psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");

			psi.ArgumentList.Add("-nostdin");

			if (HardwareAccelerationMode != FFHardwareAccelerationMode.none && !settings.SoftwareDecodeOnly) {
				psi.ArgumentList.Add("-hwaccel");
				psi.ArgumentList.Add(HardwareAccelerationMode.ToString());
			}

			// -ss before -i (faster seek, may be less accurate; OK for frame sampling).
			// Skip it entirely for still images: they are a single frame with no seek position,
			// and an input -ss (even -ss 0) makes FFmpeg discard that frame on some JPEGs —
			// EOF before any frame reaches the filter graph, so it writes 0 bytes and exits 0
			// with no error, surfacing as "Failed to retrieve graybytes" (#801).
			if (!FileUtils.IsImageFile(settings.File)) {
				psi.ArgumentList.Add("-ss"); psi.ArgumentList.Add(settings.Position.ToString(null, CultureInfo.InvariantCulture));
			}
			psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(FFToolsUtils.LongPathFix(settings.File));

			// Parse CustomFFArguments up front so we can detect a user-supplied -vf and merge it
			// into our own filter chain rather than letting a second -vf silently override the
			// scale filter (last -vf wins in ffmpeg). See: https://github.com/0x90d/videoduplicatefinder/issues/588
			string? userVfFilter = null;
			var remainingCustomArgs = new List<string>();
			if (!string.IsNullOrWhiteSpace(CustomFFArguments)) {
				var tokens = TokenizeArgs(CustomFFArguments);
				for (int ti = 0; ti < tokens.Count; ti++) {
					if ((tokens[ti] == "-vf" || tokens[ti] == "-filter:v") && ti + 1 < tokens.Count)
						userVfFilter = tokens[++ti];
					else
						remainingCustomArgs.Add(tokens[ti]);
				}
			}

			// Filter chain: scale + gray
			if (isGrayByte) {
				string vfChain = $"scale={N}:{N}:flags=bicubic,format=gray";
				if (userVfFilter != null) vfChain = $"{userVfFilter},{vfChain}";
				psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add(vfChain);
				psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
				psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("gray");
			}
			else {
				if (settings.Fullsize != 1) {
					int maxW = settings.MaxWidth > 0 ? settings.MaxWidth : 100;
					// Downscale-only fit into a maxW x maxW bounding box (matching the native
					// path and the old resize semantics) — small sources keep their size.
					string vfChain = $"scale=min({maxW}\\,iw):min({maxW}\\,ih):force_original_aspect_ratio=decrease";
					if (userVfFilter != null) vfChain = $"{vfChain},{userVfFilter}";
					psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add(vfChain);
				}
				else if (userVfFilter != null) {
					psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add(userVfFilter);
				}
				psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("mjpeg");
				// Map 1-100 quality onto MJPEG's 2-31 qscale (lower = better), same curve
				// as JpegFrameEncoder so CLI and native output comparable quality.
				int quality = settings.JpegQuality > 0 ? settings.JpegQuality : DefaultJpegQuality;
				psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add(Math.Clamp(2 + (100 - quality) / 10, 2, 31).ToString(CultureInfo.InvariantCulture));
			}

			psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");

			foreach (var item in remainingCustomArgs)
				psi.ArgumentList.Add(item);
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
			// Collapse consecutive identical stderr lines: a single broken HEVC/H.264
			// stream can emit the same decoder error tens of thousands of times per
			// file (e.g. "[hevc] Error constructing the frame RPS"), turning the log
			// into noise. Track the last line and a repeat count, then flush.
			string lastErrLine = string.Empty;
			int repeatCount = 0;
			byte[]? bytes = null;
			try {
				process.EnableRaisingEvents = true;
				process.Start();
				process.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => {
					if (e.Data?.Length > 0) {
						if (e.Data == lastErrLine) {
							repeatCount++;
						}
						else {
							if (repeatCount > 0) {
								errOut += $" (repeated {repeatCount} more time{(repeatCount == 1 ? string.Empty : "s")})";
								repeatCount = 0;
							}
							errOut += Environment.NewLine + e.Data;
							lastErrLine = e.Data;
						}
					}
				});
				process.BeginErrorReadLine();
				using var ms = new MemoryStream();
				process.StandardOutput.BaseStream.CopyTo(ms);

				if (!process.WaitForExit(TimeoutDuration)) {
					throw new TimeoutException($"FFmpeg timed out on file: {settings.File}");
				}
				else
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
			if (repeatCount > 0)
				errOut += $" (repeated {repeatCount} more time{(repeatCount == 1 ? string.Empty : "s")})";
			// Failures always log (including FFmpeg's stderr); success-with-warnings only
			// when extended logging is enabled, to avoid noise from benign decoder chatter.
			if (bytes == null || (extendedLogging && errOut.Length > 0)) {
				string message = $"{((bytes == null) ? "ERROR: Failed to retrieve" : "WARNING: Problems while retrieving")} {(isGrayByte ? "graybytes" : "thumbnail")} from: {settings.File}";
				if (extendedLogging) {
					var args = string.Join(" ", psi.ArgumentList);
					message += $":{Environment.NewLine}{FFmpegPath} {args}";
				}
				// On an outright failure, classify FFmpeg's stderr into a plain-language hint so
				// users (and the maintainer triaging reports) can tell incompatible hardware from
				// a damaged file from a real bug without reproducing it.
				string? hint = bytes == null ? FfmpegErrorClassifier.Classify(errOut) : null;
				string hintSuffix = hint != null ? $"{Environment.NewLine}Hint: {hint}" : string.Empty;
				Logger.Instance.Info($"{message}{errOut}{hintSuffix}");
			}
			return bytes;
		}
		internal static bool GetGrayBytesFromVideo(FileEntry videoFile, List<float> positions, double maxSamplingDurationSeconds, bool extendedLogging, Action<int>? onSampleComplete = null) {
			// Count missing up front so the TooDark check below compares against samples
			// we actually extracted this run, not the total positions (which may already
			// be partially cached from a prior scan).
			int missingPositions = CountMissingGrayBytePositions(videoFile, positions, maxSamplingDurationSeconds);
			if (missingPositions == 0) {
				for (int i = 0; i < positions.Count; i++)
					onSampleComplete?.Invoke(i + 1);
				return true;
			}

			int tooDarkCounter = 0;

			// Native batch path: open file + decoder + sws context once, walk all positions.
			// The for-loop fallback below recreates them per position, so on a 4-position scan
			// this avoids ~3x of the per-file FFmpeg setup cost.
			if (ShouldUseNativeBinding && TryGetGrayBytesFromVideoNativeBatch(videoFile, positions, maxSamplingDurationSeconds, ref tooDarkCounter, onSampleComplete)) {
				if (tooDarkCounter == missingPositions) {
					videoFile.Flags.Set(EntryFlags.TooDark);
					Logger.Instance.Info($"ERROR: Graybytes too dark of: {videoFile.Path}");
					return false;
				}
				return true;
			}

			// Re-count: the batch path may have populated some positions before throwing.
			missingPositions = CountMissingGrayBytePositions(videoFile, positions, maxSamplingDurationSeconds);
			if (missingPositions == 0)
				return true;

			tooDarkCounter = 0;
			for (int i = 0; i < positions.Count; i++) {
				double position = videoFile.GetGrayBytesIndex(positions[i], maxSamplingDurationSeconds);
				if (videoFile.grayBytes.ContainsKey(position)) {
					onSampleComplete?.Invoke(i + 1);
					continue;
				}

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
				onSampleComplete?.Invoke(i + 1);
			}
			if (tooDarkCounter == missingPositions) {
				videoFile.Flags.Set(EntryFlags.TooDark);
				Logger.Instance.Info($"ERROR: Graybytes too dark of: {videoFile.Path}");
				return false;
			}
			return true;
		}

		private static List<string> TokenizeArgs(string args) {
			var tokens = new List<string>();
			var current = new System.Text.StringBuilder();
			bool inQuotes = false;
			foreach (char c in args) {
				if (c == '"') {
					inQuotes = !inQuotes;
				}
				else if (c == ' ' && !inQuotes) {
					if (current.Length > 0) {
						tokens.Add(current.ToString());
						current.Clear();
					}
				}
				else {
					current.Append(c);
				}
			}
			if (current.Length > 0)
				tokens.Add(current.ToString());
			return tokens;
		}

		/// <summary>
		/// Extracts a single JPEG thumbnail from a video or image file at the given
		/// position (ignored for images). FFmpeg does the scaling and encoding directly.
		/// Returns null if extraction fails.
		/// </summary>
		public static byte[]? ExtractThumbnailJpeg(string filePath, TimeSpan position, int maxWidth = 0, bool extendedLogging = false, int jpegQuality = 0) {
			return GetThumbnail(new FfmpegSettings {
				File = filePath,
				Position = position,
				GrayScale = 0,
				Fullsize = (byte)(maxWidth == 0 ? 1 : 0),
				MaxWidth = maxWidth,
				JpegQuality = jpegQuality,
			}, extendedLogging);
		}

		/// <summary>Downscale-only fit into a maxDim x maxDim bounding box, preserving aspect ratio.</summary>
		static Size ScaleToMaxWidth(Size source, int maxDim) {
			if (source.Width <= maxDim && source.Height <= maxDim)
				return source;
			double factor = Math.Max(source.Width / (double)maxDim, source.Height / (double)maxDim);
			return new Size(
				Math.Max(1, (int)Math.Round(source.Width / factor)),
				Math.Max(1, (int)Math.Round(source.Height / factor)));
		}

		/// <summary>
		/// Native fast path for hashing a still image: decodes the (single) frame once and
		/// returns both the 32x32 gray bytes and the source dimensions, avoiding a separate
		/// ffprobe call. Returns false when the native binding is unavailable or decoding
		/// fails — callers fall back to the CLI path.
		/// </summary>
		internal static unsafe bool TryGetImageInfoAndGrayBytes(string path, out byte[]? grayBytes, out int width, out int height, bool extendedLogging) {
			const int N = 32;
			grayBytes = null;
			width = 0;
			height = 0;
			if (!ShouldUseNativeBinding)
				return false;
			try {
				// Stills never benefit from HW decoders (and some HW paths reject them).
				using var vsd = new VideoStreamDecoder(path);
				if (!vsd.TryDecodeFrame(out var srcFrame, TimeSpan.Zero))
					throw new Exception($"TryDecodeFrame failed for image '{path}'");

				Size sourceSize = new(
					srcFrame.width > 0 ? srcFrame.width : vsd.FrameSize.Width,
					srcFrame.height > 0 ? srcFrame.height : vsd.FrameSize.Height);
				AVPixelFormat srcPixFmt = vsd.IsHardwareDecode ? (AVPixelFormat)srcFrame.format : vsd.PixelFormat;
				if (srcPixFmt < 0 || srcPixFmt >= AVPixelFormat.AV_PIX_FMT_NB)
					throw new Exception($"Invalid source pixel format {srcPixFmt}");
				if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
					throw new Exception($"Invalid source dimensions {sourceSize.Width}x{sourceSize.Height}");

				using var converter = new VideoFrameConverter(
					sourceSize, srcPixFmt,
					new Size(N, N), AVPixelFormat.AV_PIX_FMT_GRAY8,
					VideoFrameConverter.ScaleQuality.Bicubic, bitExact: false);
				AVFrame convertedFrame = converter.Convert(srcFrame);
				grayBytes = ExtractGray32FromFrame(convertedFrame);
				width = sourceSize.Width;
				height = sourceSize.Height;
				return true;
			}
			catch (Exception e) {
				if (extendedLogging)
					Logger.Instance.Info($"Native image decode failed on '{path}', falling back to process mode. Exception: {e}");
				return false;
			}
		}

		/// <summary>
		/// Encodes raw BGRA pixels into a JPEG, optionally downscaling to
		/// <paramref name="maxWidth"/>. Used by the GUI to encode composed thumbnail
		/// strips for the on-disk cache. Native binding preferred; falls back to an
		/// FFmpeg process fed via stdin.
		/// </summary>
		public static unsafe byte[]? EncodeJpegFromBgra(byte[] bgra, int width, int height, int maxWidth = 0, int quality = 0) {
			if (bgra == null || width <= 0 || height <= 0 || bgra.Length < (long)width * height * 4)
				return null;
			if (quality <= 0) quality = DefaultJpegQuality;
			Size destSize = maxWidth > 0 ? ScaleToMaxWidth(new Size(width, height), maxWidth) : new Size(width, height);

			if (ShouldUseNativeBinding) {
				try {
					AVFrame* srcFrame = ffmpeg.av_frame_alloc();
					if (srcFrame == null) throw new FFInvalidExitCodeException("Failed to allocate AVFrame.");
					try {
						srcFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
						srcFrame->width = width;
						srcFrame->height = height;
						ffmpeg.av_frame_get_buffer(srcFrame, 0).ThrowExceptionIfError();
						int srcStride = srcFrame->linesize[0];
						int rowBytes = width * 4;
						fixed (byte* src = bgra) {
							for (int y = 0; y < height; y++)
								Buffer.MemoryCopy(src + (long)y * rowBytes, srcFrame->data[0] + (long)y * srcStride, rowBytes, rowBytes);
						}
						using var converter = new VideoFrameConverter(
							new Size(width, height), AVPixelFormat.AV_PIX_FMT_BGRA,
							destSize, AVPixelFormat.AV_PIX_FMT_YUVJ420P,
							VideoFrameConverter.ScaleQuality.Bicubic, bitExact: false);
						AVFrame converted = converter.Convert(*srcFrame);
						return JpegFrameEncoder.Encode(converted, quality);
					}
					finally {
						ffmpeg.av_frame_free(&srcFrame);
					}
				}
				catch (Exception e) {
					Logger.Instance.Info($"Native BGRA->JPEG encode failed, falling back to process mode. Exception: {e}");
				}
			}

			// CLI fallback: raw BGRA via stdin -> mjpeg via stdout.
			var psi = new ProcessStartInfo {
				FileName = FFmpegPath,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				WorkingDirectory = Path.GetDirectoryName(FFmpegPath)!,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			psi.ArgumentList.Add("-hide_banner");
			psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("quiet");
			psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
			psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("bgra");
			psi.ArgumentList.Add("-video_size"); psi.ArgumentList.Add($"{width}x{height}");
			psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("pipe:0");
			if (destSize.Width != width)
				{ psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"scale={destSize.Width}:-1"); }
			psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("mjpeg");
			psi.ArgumentList.Add("-q:v"); psi.ArgumentList.Add(Math.Clamp(2 + (100 - quality) / 10, 2, 31).ToString(CultureInfo.InvariantCulture));
			psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
			psi.ArgumentList.Add("pipe:1");

			using var process = new Process { StartInfo = psi };
			try {
				process.Start();
				using var ms = new MemoryStream();
				// Write input and read output concurrently to avoid pipe-buffer deadlocks.
				var readTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
				process.StandardInput.BaseStream.Write(bgra, 0, width * height * 4);
				process.StandardInput.BaseStream.Flush();
				process.StandardInput.Close();
				readTask.Wait(TimeoutDuration);
				if (!process.WaitForExit(TimeoutDuration))
					throw new TimeoutException("FFmpeg timed out encoding JPEG from raw pixels.");
				if (process.ExitCode != 0)
					throw new FFInvalidExitCodeException($"FFmpeg exited with: {process.ExitCode}");
				byte[] jpeg = ms.ToArray();
				return jpeg.Length > 0 ? jpeg : null;
			}
			catch (Exception e) {
				Logger.Instance.Info($"BGRA->JPEG encode via FFmpeg process failed: {e.Message}");
				try { if (!process.HasExited) process.Kill(); } catch { }
				return null;
			}
		}
	}

	internal struct FfmpegSettings {
		public byte GrayScale;
		public byte Fullsize;
		public string File;
		public TimeSpan Position;
		/// <summary>Target max width for non-fullsize thumbnails; 0 = default (100). Downscale only.</summary>
		public int MaxWidth;
		/// <summary>JPEG quality 1-100; 0 = default (90).</summary>
		public int JpegQuality;
		/// <summary>Skip hardware acceleration (used for still images).</summary>
		public bool SoftwareDecodeOnly;
	}
}
