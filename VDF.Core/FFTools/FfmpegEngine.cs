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
using System.Threading;
using FFmpeg.AutoGen;
using VDF.Core.FFTools.FFmpegNative;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	internal static class FfmpegEngine {
		// Re-probes when unresolved (or the binary vanished): a once-only static cache made
		// an FFmpeg installed/downloaded while the app was running invisible until restart,
		// so the GUI kept offering the download forever (issue #788).
		public static string FFmpegPath {
			get {
				if (field.Length == 0 || !File.Exists(field))
					field = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) ?? string.Empty;
				return field;
			}
		} = string.Empty;
		const int TimeoutDuration = 15_000; //15 seconds
		public static FFHardwareAccelerationMode HardwareAccelerationMode;
		public static string CustomFFArguments = string.Empty;

		public static bool UseNativeBinding {
			get;
			set {
				field = value;
				// Reset the per-scan native-health state whenever native binding is (re)configured,
				// i.e. at the start of each scan.
				_nativeConsecutiveFailures = 0;
				_nativeFailureLogCount = 0;
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

		// Per-scan cap on native-failure log output. The consecutive-failure circuit breaker
		// above never trips on a library where working files keep resetting the counter, so a
		// scan with many isolated bad files logged a full stack trace per failure — one report
		// (issue #861) reached an 820 MB log. Total-count tiers instead: full detail for the
		// first few, a compact line for a while, then only periodic running-count summaries.
		static int _nativeFailureLogCount;
		const int NativeFailureFullDetailLimit = 20;
		const int NativeFailureCompactLimit = 200;
		const int NativeFailureSummaryEvery = 100;

		internal enum NativeFailureLogMode { Full, Compact, Summary, Suppressed }

		internal static NativeFailureLogMode GetNativeFailureLogMode(int totalFailures) {
			if (totalFailures <= NativeFailureFullDetailLimit)
				return NativeFailureLogMode.Full;
			if (totalFailures <= NativeFailureCompactLimit)
				return NativeFailureLogMode.Compact;
			return totalFailures % NativeFailureSummaryEvery == 0
				? NativeFailureLogMode.Summary
				: NativeFailureLogMode.Suppressed;
		}

		static void RecordNativeFailure(string file, Exception e) {
			if (_nativeDisabledForSession)
				return;
			int n = ++_nativeConsecutiveFailures;
			if (n >= NativeFailureThreshold) {
				_nativeDisabledForSession = true;
				Logger.Instance.Warn(
					$"Native FFmpeg binding failed on {n} consecutive files; using process mode for the rest of this scan. " +
					$"Last error on '{file}': {e.GetType().Name}: {e.Message}.{BuildNativeFailureDetail(e)} " +
					$"If this persists, set hardware acceleration to 'none' or disable 'Use native FFmpeg binding'.");
				return;
			}
			int logged = Interlocked.Increment(ref _nativeFailureLogCount);
			switch (GetNativeFailureLogMode(logged)) {
				case NativeFailureLogMode.Full:
					Logger.Instance.Warn($"Failed using native FFmpeg binding on '{file}', switching to process mode. Exception: {e}{BuildNativeFailureDetail(e)}");
					break;
				case NativeFailureLogMode.Compact:
					Logger.Instance.Warn($"Failed using native FFmpeg binding on '{file}', switching to process mode ({logged} native failures this scan; stack traces suppressed after the first {NativeFailureFullDetailLimit}): {e.Message}{BuildNativeFailureDetail(e)}");
					break;
				case NativeFailureLogMode.Summary:
					Logger.Instance.Warn($"Native FFmpeg binding has failed on {logged} files this scan (per-file warnings suppressed after the first {NativeFailureCompactLimit}). Last: '{file}': {e.Message}");
					break;
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
					Logger.Instance.Warn(
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

		/// <summary>
		/// Copies a 224x224 RGB24 frame produced by <see cref="VideoFrameConverter"/> into a
		/// packed <see cref="AI.FramePool"/> buffer (these 150 KB frames are large-object
		/// allocations, and one per sampled frame churned the LOH badly — #878), dropping
		/// swscale's per-row alignment padding. The embedding pipeline recycles the buffer
		/// after use.
		/// </summary>
		static unsafe byte[] ExtractRgb224FromFrame(AVFrame convertedFrame) {
			int width = convertedFrame.width;
			int height = convertedFrame.height;
			if (width != AI.OnnxEmbedder.InputSide || height != AI.OnnxEmbedder.InputSide)
				throw new Exception($"Unexpected size {width}x{height}, expected {AI.OnnxEmbedder.InputSide}x{AI.OnnxEmbedder.InputSide}.");
			if (convertedFrame.data[0] == null)
				throw new Exception("Converted frame has no data[0] (null).");
			int rowBytes = width * 3;
			int srcStride = convertedFrame.linesize[0];
			if (srcStride < rowBytes)
				throw new Exception($"Invalid linesize ({srcStride}) for width {width}.");

			// Size is validated to exactly InputSide² * 3 above, so the pooled buffer fits.
			byte[] outBuf = AI.FramePool.Shared.Rent();
			fixed (byte* destPtr = outBuf) {
				byte* sourcePtr = convertedFrame.data[0];
				if (srcStride == rowBytes) {
					Buffer.MemoryCopy(sourcePtr, destPtr, outBuf.Length, outBuf.Length);
				}
				else {
					for (int y = 0; y < height; y++)
						Buffer.MemoryCopy(sourcePtr + (y * srcStride), destPtr + (y * rowBytes), rowBytes, rowBytes);
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
		/// Source pixel format for scaling a decoded frame. The frame's own format is
		/// authoritative: the open-time codec-context value comes from container metadata,
		/// which corrupt files contradict (and mid-stream format changes never update it) —
		/// scaling with the stale value made swscale read out of bounds and crash the
		/// process (issue #861). The open-time value only remains as a fallback for frames
		/// that report no format.
		/// </summary>
		internal static AVPixelFormat ResolveSourcePixelFormat(int frameFormat, AVPixelFormat openTimeFormat) =>
			frameFormat >= 0 ? (AVPixelFormat)frameFormat : openTimeFormat;

		/// <summary>
		/// Opens a single <see cref="VideoStreamDecoder"/> and a single <see cref="VideoFrameConverter"/>
		/// for the file, then walks the requested positions reusing both. This avoids the per-position
		/// avformat_open_input + sws_getContext cost of looping <see cref="GetThumbnail"/>.
		///
		/// On any FFmpeg error we abort and return false; the caller falls back to the per-sample
		/// CLI/native path so partial extraction still succeeds. Already-cached positions are skipped.
		/// <paramref name="failureCategory"/> reports the classified cause of a failure so the
		/// caller can decide whether that fallback is worth attempting at all (#867).
		/// </summary>
		static unsafe bool TryGetGrayBytesFromVideoNativeBatch(
			FileEntry videoFile,
			List<float> positions,
			double maxSamplingDurationSeconds,
			ref int tooDarkCounter,
			Action<int>? onSampleComplete,
			out FfmpegErrorCategory failureCategory,
			AI.IEmbeddingFrameSink? embeddingSink = null) {
			const int N = 32;
			failureCategory = FfmpegErrorCategory.Unknown;
			try {
				FfmpegLogCapture.Reset();
				using var vsd = new VideoStreamDecoder(videoFile.Path, GetConfiguredHardwareDeviceType());
				VideoFrameConverter? converter = null;
				VideoFrameConverter? aiConverter = null;
				Size converterSourceSize = default;
				AVPixelFormat converterSrcFmt = AVPixelFormat.AV_PIX_FMT_NONE;
				try {
					for (int i = 0; i < positions.Count; i++) {
						double position = videoFile.GetGrayBytesIndex(positions[i], maxSamplingDurationSeconds);
						bool needGray = !videoFile.grayBytes.ContainsKey(position);
						bool needEmbedding = embeddingSink?.WantsEmbedding(videoFile, position) == true;
						if (!needGray && !needEmbedding) {
							onSampleComplete?.Invoke(i + 1);
							continue;
						}

						if (!vsd.TryDecodeFrame(out var srcFrame, TimeSpan.FromSeconds(position)))
							throw new Exception($"TryDecodeFrame failed at pos={position} for '{videoFile.Path}'");

						Size sourceSize = new(
							srcFrame.width > 0 ? srcFrame.width : vsd.FrameSize.Width,
							srcFrame.height > 0 ? srcFrame.height : vsd.FrameSize.Height);
						AVPixelFormat srcPixFmt = ResolveSourcePixelFormat(srcFrame.format, vsd.PixelFormat);
						if (srcPixFmt < 0 || srcPixFmt >= AVPixelFormat.AV_PIX_FMT_NB)
							throw new Exception($"Invalid source pixel format {srcPixFmt}");
						if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
							throw new Exception($"Invalid source frame dimensions {sourceSize.Width}x{sourceSize.Height}");

						// Reuse the SwsContext across positions when the source layout is unchanged.
						// In practice this is the common case for the same file; the rebuild branch
						// fires when a later frame reports a different resolution or pixel format
						// (mid-stream change, HW sw_format switch, corrupt file).
						if (converter == null || sourceSize != converterSourceSize || srcPixFmt != converterSrcFmt) {
							converter?.Dispose();
							converter = new VideoFrameConverter(
								sourceSize, srcPixFmt,
								new Size(N, N), AVPixelFormat.AV_PIX_FMT_GRAY8,
								VideoFrameConverter.ScaleQuality.Bicubic, bitExact: false);
							converterSourceSize = sourceSize;
							converterSrcFmt = srcPixFmt;
							// The AI converter shares the source-layout cache; rebuild in lockstep.
							aiConverter?.Dispose();
							aiConverter = null;
						}

						if (needGray) {
							AVFrame convertedFrame = converter.Convert(srcFrame);
							byte[] data = ExtractGray32FromFrame(convertedFrame);

							if (!GrayBytesUtils.VerifyGrayScaleValues(data))
								tooDarkCounter++;
							videoFile.grayBytes.Add(position, data);
							videoFile.PHashes.Add(position, pHash.PerceptualHash.ComputePHashFromGray32x32(data));
						}

						if (needEmbedding) {
							aiConverter ??= new VideoFrameConverter(
								converterSourceSize, converterSrcFmt,
								new Size(AI.OnnxEmbedder.InputSide, AI.OnnxEmbedder.InputSide), AVPixelFormat.AV_PIX_FMT_RGB24,
								VideoFrameConverter.ScaleQuality.Bicubic, bitExact: false);
							embeddingSink!.SubmitFrame(videoFile, position, ExtractRgb224FromFrame(aiConverter.Convert(srcFrame)));
						}

						onSampleComplete?.Invoke(i + 1);
					}
				}
				finally {
					converter?.Dispose();
					aiConverter?.Dispose();
				}
				RecordNativeSuccess();
				return true;
			}
			catch (Exception e) {
				// Same diagnostics combination BuildNativeFailureDetail logs, so the category
				// always matches the Hint the user sees for this failure.
				string diagnostics = FfmpegLogCapture.GetRecent();
				failureCategory = FfmpegErrorClassifier.Categorize(
					diagnostics.Length > 0 ? $"{diagnostics} {e.Message}" : e.Message);
				// One failure recorded per video file (not per position) so the session
				// circuit breaker reflects per-file native health (issues #793/#795). The
				// per-sample fallback below still re-attempts native but does not record.
				RecordNativeFailure(videoFile.Path, e);
				return false;
			}
		}

		/// <summary>
		/// Whether a failed native batch extraction must not be retried through the FFmpeg
		/// process. True only for a failure classified as a broken file while decoding in
		/// software: the process fallback runs the same libavcodec over the same bitstream,
		/// so it cannot succeed either — it only grinds through the damaged stream again,
		/// burning up to a full timeout per attempt and stalling the scan on every corrupt
		/// file (#867). Under hardware decode a corrupt-looking error can still be a
		/// GPU/driver quirk, and the process fallback genuinely rescues those.
		/// </summary>
		internal static bool ShouldSkipProcessRetryForCorruptFile(FfmpegErrorCategory failureCategory, AVHWDeviceType hardwareDeviceType) =>
			failureCategory == FfmpegErrorCategory.CorruptOrTruncated &&
			hardwareDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

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
					// Always decode in software here (#863): this path serves the partial-clip
					// visual gate, which samples 1-3 tiny frames per file - a hardware decode
					// session costs more to set up than it saves, and an access violation
					// inside the GPU driver (nvcuda64.dll on the reporter's machine) takes the
					// whole process down because this decode runs in-process. The per-frame
					// fallback below skips hardware acceleration for the same reason.
					using var vsd = new VideoStreamDecoder(filePath, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE);
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
							AVPixelFormat srcPixFmt = ResolveSourcePixelFormat(srcFrame.format, vsd.PixelFormat);
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
					GrayScale = 1,
					SoftwareDecodeOnly = true
				}, extendedLogging);
			}
			return frames;
		}

		public static unsafe byte[]? GetThumbnail(FfmpegSettings settings, bool extendedLogging) {

			const int N = 32;
			bool isRgbFrame = settings.Rgb224;
			bool isGrayByte = settings.GrayScale == 1 && !isRgbFrame;
			bool isRawOutput = isGrayByte || isRgbFrame;
			int expectedBytes = isRgbFrame
				? AI.OnnxEmbedder.InputSide * AI.OnnxEmbedder.InputSide * 3
				: N * N;

			try {
				if (ShouldUseNativeBinding) {

					FfmpegLogCapture.Reset();

					AVHWDeviceType HWDevice = settings.SoftwareDecodeOnly
						? AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
						: GetConfiguredHardwareDeviceType();

					using var vsd = new VideoStreamDecoder(settings.File, HWDevice);

					// Tiled HEIF (Apple photos): the picture only exists as an assembled tile
					// grid; the native binding would decode one tile or an aux stream (#869).
					if (vsd.HasStreamGroups && FileUtils.IsHeifImageFile(settings.File))
						throw new Exception($"Tiled HEIF needs FFmpeg's grid assembly; using the process fallback for '{settings.File}'");

					// Decode first so we know the real source layout. The frame's own
					// dimensions and pixel format are authoritative — container metadata
					// (vsd.FrameSize / vsd.PixelFormat) can lie for corrupt files, and
					// scaling with the stale values crashes swscale (issue #861). For HW
					// decode the sw_format is only knowable post-decode anyway (NV12 for
					// 8-bit, P010LE for 10-bit HEVC, etc.).
					if (!vsd.TryDecodeFrame(out var srcFrame, settings.Position))
						throw new Exception($"TryDecodeFrame failed at pos={settings.Position} for '{settings.File}'. size={vsd.FrameSize.Width}x{vsd.FrameSize.Height}");

					Size sourceSize = new(
						srcFrame.width > 0 ? srcFrame.width : vsd.FrameSize.Width,
						srcFrame.height > 0 ? srcFrame.height : vsd.FrameSize.Height);
					AVPixelFormat srcPixFmt = ResolveSourcePixelFormat(srcFrame.format, vsd.PixelFormat);
					if (srcPixFmt < 0 || srcPixFmt >= AVPixelFormat.AV_PIX_FMT_NB)
						throw new Exception($"Invalid source pixel format {srcPixFmt}");

					if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
						throw new Exception($"Invalid source frame dimensions {sourceSize.Width}x{sourceSize.Height}.");

					// Anamorphic streams store non-square pixels: widen the coded raster by
					// the sample aspect ratio for display thumbnails, or DVD-style content
					// (e.g. 720x576 16:9) is shown squished. Gray bytes are unaffected —
					// they are force-scaled to a fixed square, which erases aspect ratio.
					AVRational sar = vsd.StreamSampleAspectRatio;
					if (sar.num <= 0 || sar.den <= 0)
						sar = srcFrame.sample_aspect_ratio;
					// Raw comparison frames (gray bytes, AI frames) are force-scaled to a fixed
					// square that erases aspect ratio, so SAR correction only applies to thumbnails.
					Size displaySize = isRawOutput ? sourceSize : ApplySampleAspectRatio(sourceSize, sar.num, sar.den);

					Size destinationSize = isRgbFrame ? new Size(AI.OnnxEmbedder.InputSide, AI.OnnxEmbedder.InputSide) :
						isGrayByte ? new Size(N, N) :
						settings.Fullsize == 1 ?
							displaySize :
							ScaleToMaxWidth(displaySize, settings.MaxWidth > 0 ? settings.MaxWidth : 100);

					AVPixelFormat destinationPixelFrmt = isRgbFrame ?
						AVPixelFormat.AV_PIX_FMT_RGB24 :
						isGrayByte ?
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


					if (isRgbFrame) {
						return ExtractRgb224FromFrame(convertedFrame);
					}
					else if (isGrayByte) {
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
				Logger.Instance.Warn($"Failed using native FFmpeg binding on '{settings.File}', try switching to process mode. Exception: {e}{BuildNativeFailureDetail(e)}");
			}

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

			// Filter chain (scale + gray/rgb) and output format, decided once — the HEIF
			// grid-assembly retry below reuses the same chain in a complex filtergraph.
			string? filterChain;
			var outputFormatArgs = new List<string>();
			if (isRgbFrame) {
				// Deliberately NO user -vf here: the native decode path cannot apply
				// CustomFFArguments, so embedding inputs must be uniformly unfiltered on
				// every path — mixing filtered CLI frames with unfiltered native frames
				// would silently sink AI similarity for exactly the files that fell back
				// to the CLI (the dense AI sweep is equally unfiltered).
				filterChain = $"scale={AI.OnnxEmbedder.InputSide}:{AI.OnnxEmbedder.InputSide}:flags=bicubic,format=rgb24";
				outputFormatArgs.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "rgb24" });
			}
			else if (isGrayByte) {
				string vfChain = $"scale={N}:{N}:flags=bicubic,format=gray";
				if (userVfFilter != null) vfChain = $"{userVfFilter},{vfChain}";
				filterChain = vfChain;
				outputFormatArgs.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "gray" });
			}
			else {
				// SAR normalization first, so anamorphic videos render at display width
				// and the bounding box below sees display dimensions (matching the native
				// path). sar==0 (unknown) counts as square pixels. Videos only — image
				// demuxer pipelines are fragile (#806) and images have square pixels.
				string? sarChain = BuildSarNormalizationFilter(settings.File);
				if (settings.Fullsize != 1) {
					int maxW = settings.MaxWidth > 0 ? settings.MaxWidth : 100;
					// Downscale-only fit into a maxW x maxW bounding box (matching the native
					// path and the old resize semantics) — small sources keep their size.
					string vfChain = $"scale=min({maxW}\\,iw):min({maxW}\\,ih):force_original_aspect_ratio=decrease";
					if (sarChain != null) vfChain = $"{sarChain},{vfChain}";
					if (userVfFilter != null) vfChain = $"{vfChain},{userVfFilter}";
					filterChain = vfChain;
				}
				else {
					string? vfChain = sarChain;
					if (userVfFilter != null) vfChain = vfChain != null ? $"{vfChain},{userVfFilter}" : userVfFilter;
					filterChain = vfChain;
				}
				outputFormatArgs.AddRange(new[] { "-f", "mjpeg" });
				// Map 1-100 quality onto MJPEG's 2-31 qscale (lower = better), same curve
				// as JpegFrameEncoder so CLI and native output comparable quality.
				int quality = settings.JpegQuality > 0 ? settings.JpegQuality : DefaultJpegQuality;
				outputFormatArgs.Add("-q:v");
				outputFormatArgs.Add(Math.Clamp(2 + (100 - quality) / 10, 2, 31).ToString(CultureInfo.InvariantCulture));
			}

			ProcessStartInfo BuildPsi(bool gridAssembly) {
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

				if (filterChain != null) {
					if (gridAssembly) {
						// Tiled HEIF: read the tile-grid stream group directly ([0:g:0] = the
						// primary grid of an Apple photo) so FFmpeg assembles the full picture,
						// and run our chain in the same complex graph — a plain -vf on that
						// stream is rejected ("Simple and complex filtering cannot be used
						// together for the same stream") on FFmpeg 8.1+ (#869).
						psi.ArgumentList.Add("-filter_complex");
						psi.ArgumentList.Add($"[0:g:0]{filterChain}[vdf]");
						psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[vdf]");
					}
					else {
						psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add(filterChain);
					}
				}
				foreach (string arg in outputFormatArgs)
					psi.ArgumentList.Add(arg);

				psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");

				foreach (var item in remainingCustomArgs)
					psi.ArgumentList.Add(item);
				psi.ArgumentList.Add("pipe:1"); // stdout
				return psi;
			}

			byte[]? RunGrab(ProcessStartInfo psi, ref string errOut) {
				using var process = new Process {
					StartInfo = psi
				};
				string localErr = string.Empty;
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
					FFToolsUtils.LowerChildPriority(process);
					process.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => {
						if (e.Data?.Length > 0) {
							if (e.Data == lastErrLine) {
								repeatCount++;
							}
							else {
								if (repeatCount > 0) {
									localErr += $" (repeated {repeatCount} more time{(repeatCount == 1 ? string.Empty : "s")})";
									repeatCount = 0;
								}
								localErr += Environment.NewLine + e.Data;
								lastErrLine = e.Data;
							}
						}
					});
					process.BeginErrorReadLine();
					using var ms = new MemoryStream();
					// Bounded read + wait: a synchronous CopyTo made the timeout below unreachable
					// and blocked the worker forever on a wedged ffmpeg (#865).
					FFToolsUtils.ReadStdoutBounded(process, ms, TimeoutDuration, "FFmpeg", settings.File);

					if (process.ExitCode != 0)
						throw new FFInvalidExitCodeException($"FFmpeg exited with: {process.ExitCode}");

					bytes = ms.ToArray();
					if (bytes.Length == 0)
						bytes = null;   // Makes subsequent checks easier
					else if (isRawOutput && bytes.Length != expectedBytes) {
						localErr += $"{Environment.NewLine}{(isGrayByte ? "graybytes" : "AI frame")} length != {expectedBytes} (got {bytes.Length})";
						bytes = null;
					}
				}
				catch (Exception e) {
					localErr += $"{Environment.NewLine}{e.Message}";
					try {
						if (process.HasExited == false)
							process.Kill();
					}
					catch { }
					bytes = null;
				}
				if (repeatCount > 0)
					localErr += $" (repeated {repeatCount} more time{(repeatCount == 1 ? string.Empty : "s")})";
				errOut += localErr;
				return bytes;
			}

			string errOut = string.Empty;
			ProcessStartInfo psiUsed = BuildPsi(gridAssembly: false);
			byte[]? bytes = RunGrab(psiUsed, ref errOut);
			// Tiled HEIF (Apple photos) on FFmpeg 8.1+: the picture FFmpeg selects is
			// assembled from tiles by an internal complex filtergraph, and the -vf above is
			// rejected against it, yielding no output. Retry once with the same chain as a
			// complex graph on the tile-grid stream group (#869). HEIF images only, so every
			// other format keeps the single-attempt behavior; single-stream HEICs succeed on
			// the first attempt and never get here.
			if (bytes == null && filterChain != null && FileUtils.IsHeifImageFile(settings.File)) {
				errOut += $"{Environment.NewLine}Retrying with HEIF tile-grid assembly ([0:g:0]):";
				psiUsed = BuildPsi(gridAssembly: true);
				bytes = RunGrab(psiUsed, ref errOut);
			}
			// When we still extracted the frame from a still image, drop FFmpeg's benign
			// demuxer chatter: its image2/png_pipe demuxer probes past the single frame and
			// misreads mid-stream PNG IDAT bytes as a second image, emitting bogus
			// "Invalid PNG signature"/"chunk too big" decode errors even though the frame
			// decoded fine (issues #805/#809/#815). Keep the full stderr on real failures.
			if (bytes != null && errOut.Length > 0 && FileUtils.IsImageFile(settings.File))
				errOut = FilterBenignImageDemuxerNoise(errOut);
			// Failures always log (including FFmpeg's stderr); success-with-warnings only
			// when extended logging is enabled, to avoid noise from benign decoder chatter.
			if (bytes == null || (extendedLogging && errOut.Length > 0)) {
				string message = $"{((bytes == null) ? "ERROR: Failed to retrieve" : "WARNING: Problems while retrieving")} {(isGrayByte ? "graybytes" : isRgbFrame ? "AI frame" : "thumbnail")} from: {settings.File}";
				if (extendedLogging) {
					var args = string.Join(" ", psiUsed.ArgumentList);
					message += $":{Environment.NewLine}{FFmpegPath} {args}";
				}
				// On an outright failure, classify FFmpeg's stderr into a plain-language hint so
				// users (and the maintainer triaging reports) can tell incompatible hardware from
				// a damaged file from a real bug without reproducing it.
				string? hint = bytes == null ? FfmpegErrorClassifier.Classify(errOut) : null;
				string hintSuffix = hint != null ? $"{Environment.NewLine}Hint: {hint}" : string.Empty;
				Logger.Instance.Warn($"{message}{errOut}{hintSuffix}");
			}
			return bytes;
		}

		/// <summary>
		/// CLI fallback fetching BOTH the 32x32 gray frame and the 224x224 RGB embedding
		/// frame from one FFmpeg invocation — one seek+decode instead of two full runs
		/// per sampled position. The filter graph splits the decoded frame into the exact
		/// chains the two single-output calls use, so the results are byte-identical.
		/// Gray arrives on stdout; RGB lands in a unique temp file, because two rawvideo
		/// outputs on one pipe have no deterministic framing (the write order of ffmpeg's
		/// muxers is not a contract across versions, and raw gray bytes cannot be told
		/// apart from RGB header bytes after the fact).
		/// Callers must ensure CustomFFArguments is empty: a user -vf belongs on the gray
		/// chain but embedding inputs must stay unfiltered on every path, and remaining
		/// custom args are ambiguous with two outputs — those scans keep the two-call path.
		/// </summary>
		internal static (byte[]? GrayBytes, byte[]? Rgb224) GetGrayAndRgb224Cli(string file, TimeSpan position, bool softwareDecodeOnly, bool extendedLogging) {
			const int N = 32;
			const int grayExpectedBytes = N * N;
			int rgbExpectedBytes = AI.OnnxEmbedder.InputSide * AI.OnnxEmbedder.InputSide * 3;
			string rgbTempPath = Path.Combine(Path.GetTempPath(), $"VDF.AiFrame.{Guid.NewGuid():N}.rgb");

			bool isImage = FileUtils.IsImageFile(file);
			// Tiled HEIF (Apple photos): [0:v] resolves to the first coded stream, which is a
			// single 512x512 tile — the full picture only exists as the tile-grid stream
			// group, read via [0:g:0] so FFmpeg assembles it (#869). Single-stream HEICs have
			// no groups (the specifier "matches no streams"), so they fall back to the plain
			// [0:v] attempt; every other format keeps the single [0:v] invocation.
			string[] inputLabels = isImage && FileUtils.IsHeifImageFile(file)
				? new[] { "0:g:0", "0:v" }
				: new[] { "0:v" };

			ProcessStartInfo BuildPsi(string inputLabel) {
				var psi = new ProcessStartInfo {
					FileName = FFmpegPath,
					CreateNoWindow = true,
					RedirectStandardInput = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					WorkingDirectory = Path.GetDirectoryName(FFmpegPath)!,
					WindowStyle = ProcessWindowStyle.Hidden
				};
				psi.ArgumentList.Add("-hide_banner");
				psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
				psi.ArgumentList.Add("-nostdin");
				if (HardwareAccelerationMode != FFHardwareAccelerationMode.none && !softwareDecodeOnly) {
					psi.ArgumentList.Add("-hwaccel");
					psi.ArgumentList.Add(HardwareAccelerationMode.ToString());
				}
				// No input -ss for still images — see the matching comment in GetThumbnail (#801).
				if (!isImage) {
					psi.ArgumentList.Add("-ss"); psi.ArgumentList.Add(position.ToString(null, CultureInfo.InvariantCulture));
				}
				psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(FFToolsUtils.LongPathFix(file));
				psi.ArgumentList.Add("-filter_complex");
				psi.ArgumentList.Add(
					$"[{inputLabel}]split=2[g][r];" +
					$"[g]scale={N}:{N}:flags=bicubic,format=gray[gout];" +
					$"[r]scale={AI.OnnxEmbedder.InputSide}:{AI.OnnxEmbedder.InputSide}:flags=bicubic,format=rgb24[rout]");
				psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[gout]");
				psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
				psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
				psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("gray");
				psi.ArgumentList.Add("pipe:1");
				psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("[rout]");
				psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
				psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
				psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("rgb24");
				psi.ArgumentList.Add("-y");
				psi.ArgumentList.Add(rgbTempPath);
				return psi;
			}

			string errOut = string.Empty;
			byte[]? gray = null;
			byte[]? rgb = null;
			ProcessStartInfo psiUsed = null!;
			for (int attempt = 0; attempt < inputLabels.Length; attempt++) {
				if (attempt > 0)
					errOut += $"{Environment.NewLine}Retrying with input [{inputLabels[attempt]}]:";
				psiUsed = BuildPsi(inputLabels[attempt]);
				using var process = new Process { StartInfo = psiUsed };
				string lastErrLine = string.Empty;
				int repeatCount = 0;
				gray = null;
				rgb = null;
				try {
					process.EnableRaisingEvents = true;
					process.Start();
					FFToolsUtils.LowerChildPriority(process);
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
					// Bounded read + wait, see the note in FFToolsUtils.ReadStdoutBounded (#865).
					FFToolsUtils.ReadStdoutBounded(process, ms, TimeoutDuration, "FFmpeg", file);

					if (process.ExitCode != 0)
						throw new FFInvalidExitCodeException($"FFmpeg exited with: {process.ExitCode}");

					gray = ms.ToArray();
					if (gray.Length != grayExpectedBytes) {
						errOut += $"{Environment.NewLine}graybytes length != {grayExpectedBytes} (got {gray.Length})";
						gray = null;
					}
					if (File.Exists(rgbTempPath)) {
						rgb = File.ReadAllBytes(rgbTempPath);
						if (rgb.Length != rgbExpectedBytes) {
							errOut += $"{Environment.NewLine}AI frame length != {rgbExpectedBytes} (got {rgb.Length})";
							rgb = null;
						}
					}
				}
				catch (Exception e) {
					errOut += $"{Environment.NewLine}{e.Message}";
					try {
						if (process.HasExited == false)
							process.Kill();
					}
					catch { }
					gray = null;
					rgb = null;
				}
				finally {
					try { if (File.Exists(rgbTempPath)) File.Delete(rgbTempPath); } catch { }
				}
				if (repeatCount > 0)
					errOut += $" (repeated {repeatCount} more time{(repeatCount == 1 ? string.Empty : "s")})";
				if (gray != null)
					break;
			}
			// Same benign-demuxer-noise handling as GetThumbnail (#805/#809/#815).
			if (gray != null && errOut.Length > 0 && isImage)
				errOut = FilterBenignImageDemuxerNoise(errOut);
			if (gray == null || rgb == null || (extendedLogging && errOut.Length > 0)) {
				string what = gray == null ? "graybytes+AI frame" : "AI frame";
				string message = $"{(gray == null || rgb == null ? "ERROR: Failed to retrieve" : "WARNING: Problems while retrieving")} {what} from: {file}";
				if (extendedLogging) {
					var args = string.Join(" ", psiUsed.ArgumentList);
					message += $":{Environment.NewLine}{FFmpegPath} {args}";
				}
				string? hint = gray == null ? FfmpegErrorClassifier.Classify(errOut) : null;
				string hintSuffix = hint != null ? $"{Environment.NewLine}Hint: {hint}" : string.Empty;
				Logger.Instance.Warn($"{message}{errOut}{hintSuffix}");
			}
			return (gray, rgb);
		}

		internal static bool GetGrayBytesFromVideo(FileEntry videoFile, List<float> positions, double maxSamplingDurationSeconds, bool extendedLogging, Action<int>? onSampleComplete = null, AI.IEmbeddingFrameSink? embeddingSink = null) {
			// Count missing up front so the TooDark check below compares against samples
			// we actually extracted this run, not the total positions (which may already
			// be partially cached from a prior scan).
			int missingPositions = CountMissingGrayBytePositions(videoFile, positions, maxSamplingDurationSeconds);
			bool missingEmbeddings = false;
			if (embeddingSink != null) {
				for (int i = 0; i < positions.Count && !missingEmbeddings; i++)
					missingEmbeddings = embeddingSink.WantsEmbedding(videoFile, videoFile.GetGrayBytesIndex(positions[i], maxSamplingDurationSeconds));
			}
			if (missingPositions == 0 && !missingEmbeddings) {
				for (int i = 0; i < positions.Count; i++)
					onSampleComplete?.Invoke(i + 1);
				return true;
			}

			int tooDarkCounter = 0;

			// Native batch path: open file + decoder + sws context once, walk all positions.
			// The for-loop fallback below recreates them per position, so on a 4-position scan
			// this avoids ~3x of the per-file FFmpeg setup cost.
			if (ShouldUseNativeBinding) {
				if (TryGetGrayBytesFromVideoNativeBatch(videoFile, positions, maxSamplingDurationSeconds, ref tooDarkCounter, onSampleComplete, out FfmpegErrorCategory nativeFailureCategory, embeddingSink)) {
					if (missingPositions > 0 && tooDarkCounter == missingPositions) {
						videoFile.Flags.Set(EntryFlags.TooDark);
						Logger.Instance.Warn($"Graybytes too dark of: {videoFile.Path}");
						return false;
					}
					return true;
				}
				if (ShouldSkipProcessRetryForCorruptFile(nativeFailureCategory, GetConfiguredHardwareDeviceType())) {
					// The gray frames may all be present (cached, or extracted before the
					// failure) with only AI embedding frames missing — then the file stays
					// fully comparable and the AI pass simply abstains for it.
					if (CountMissingGrayBytePositions(videoFile, positions, maxSamplingDurationSeconds) == 0)
						return true;
					videoFile.Flags.Set(EntryFlags.ThumbnailError);
					Logger.Instance.Warn(
						$"Skipping process-mode retry for '{videoFile.Path}': the decode failure above indicates a truncated or corrupt file, " +
						"and the FFmpeg process would fail the same way, only slower. The file is excluded from this scan " +
						"(and future scans, unless 'Always retry failed sampling' is enabled).");
					return false;
				}
			}

			// Re-count: the batch path may have populated some positions before throwing.
			missingPositions = CountMissingGrayBytePositions(videoFile, positions, maxSamplingDurationSeconds);

			tooDarkCounter = 0;
			for (int i = 0; i < positions.Count; i++) {
				double position = videoFile.GetGrayBytesIndex(positions[i], maxSamplingDurationSeconds);
				bool needGray = !videoFile.grayBytes.ContainsKey(position);
				bool needRgb = embeddingSink?.WantsEmbedding(videoFile, position) == true;

				// Both frames wanted: one seek+decode via the split filter instead of two
				// full FFmpeg runs. Not with CustomFFArguments — see GetGrayAndRgb224Cli.
				if (needGray && needRgb && string.IsNullOrWhiteSpace(CustomFFArguments)) {
					(byte[]? data, byte[]? rgb) = GetGrayAndRgb224Cli(videoFile.Path, TimeSpan.FromSeconds(position), softwareDecodeOnly: false, extendedLogging);
					if (data == null) {
						videoFile.Flags.Set(EntryFlags.ThumbnailError);
						return false;
					}
					if (!GrayBytesUtils.VerifyGrayScaleValues(data))
						tooDarkCounter++;
					videoFile.grayBytes.Add(position, data);
					videoFile.PHashes.Add(position, pHash.PerceptualHash.ComputePHashFromGray32x32(data));
					// RGB failure is not fatal — the entry simply stays without this
					// embedding and the AI pass abstains for it.
					if (rgb != null)
						embeddingSink!.SubmitFrame(videoFile, position, rgb);
					onSampleComplete?.Invoke(i + 1);
					continue;
				}

				if (needGray) {
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

				// AI frame for the same position: its failure is not fatal — the entry
				// simply stays without this embedding and the AI pass abstains for it.
				if (needRgb) {
					byte[]? rgb = GetThumbnail(new FfmpegSettings {
						File = videoFile.Path,
						Position = TimeSpan.FromSeconds(position),
						Rgb224 = true,
					}, extendedLogging);
					if (rgb != null)
						embeddingSink!.SubmitFrame(videoFile, position, rgb);
				}
				onSampleComplete?.Invoke(i + 1);
			}
			if (missingPositions > 0 && tooDarkCounter == missingPositions) {
				videoFile.Flags.Set(EntryFlags.TooDark);
				Logger.Instance.Warn($"Graybytes too dark of: {videoFile.Path}");
				return false;
			}
			return true;
		}

		// Markers for FFmpeg PNG demuxer false-positives that occur after a still frame
		// has already been decoded successfully (issues #805/#809/#815).
		static readonly string[] BenignImageDemuxerMarkers = {
			"Invalid PNG signature",
			"chunk too big",
		};

		/// <summary>
		/// Strips known-benign FFmpeg demuxer lines (and the png decoder's follow-up
		/// "Decoding error" line) from captured stderr. Only used for still images whose
		/// frame was nonetheless extracted, so a non-fatal decode line cannot hide a real
		/// failure. Returns the surviving lines with the original leading newline layout.
		/// </summary>
		static string FilterBenignImageDemuxerNoise(string errOut) {
			var lines = errOut.Split(Environment.NewLine);
			var kept = new List<string>(lines.Length);
			foreach (var line in lines) {
				if (line.Length == 0)
					continue;
				bool benign = false;
				foreach (var marker in BenignImageDemuxerMarkers)
					if (line.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
						benign = true;
						break;
					}
				// The png decoder emits a paired "Decoding error: Invalid data ..." line
				// alongside the bogus signature; drop it too when it names the png decoder.
				if (!benign && line.Contains("/png @", StringComparison.Ordinal) &&
					line.Contains("Decoding error", StringComparison.Ordinal))
					benign = true;
				if (!benign)
					kept.Add(line);
			}
			return kept.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, kept);
		}

		static List<string> TokenizeArgs(string args) {
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
		/// Dense AI sampling for the visual partial-duplicate pass: decodes ONLY keyframes
		/// (<c>-skip_frame nokey</c>) and emits one 224x224 RGB24 frame per
		/// <paramref name="intervalSeconds"/> across the whole file in a single FFmpeg pass.
		/// Deliberately always the CLI, even when the native binding is enabled: a
		/// sequential keyframe sweep maps naturally onto one process run, and this pass is
		/// throughput-bound, not seek-bound — the same trade-off ChromaprintEngine makes
		/// for audio (which also means partial detection already requires the ffmpeg
		/// executable). Frames STREAM to <paramref name="onFrame"/> as they arrive - the
		/// old whole-file buffering held up to 60 MB per file (twice) and was the largest
		/// memory hotspot in the app (#878). Each callback receives an exact-size
		/// <see cref="AI.FramePool"/> buffer whose ownership transfers to the callback;
		/// frame k represents ≈ k·interval seconds. Returns the number of frames
		/// delivered, or -1 on failure (already logged) - the caller must then discard
		/// whatever the callbacks produced, matching the old all-or-nothing contract.
		/// </summary>
		internal static int StreamDenseAiFrames(string filePath, double intervalSeconds, int maxFrames, bool extendedLogging, Action<byte[]> onFrame, CancellationToken cancelToken = default) {
			const int frameBytes = AI.OnnxEmbedder.InputSide * AI.OnnxEmbedder.InputSide * 3;
			var psi = new ProcessStartInfo {
				FileName = FFmpegPath,
				CreateNoWindow = true,
				RedirectStandardInput = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = Path.GetDirectoryName(FFmpegPath)!,
				WindowStyle = ProcessWindowStyle.Hidden
			};
			psi.ArgumentList.Add("-hide_banner");
			psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
			psi.ArgumentList.Add("-nostdin");
			psi.ArgumentList.Add("-skip_frame"); psi.ArgumentList.Add("nokey");
			psi.ArgumentList.Add("-an"); psi.ArgumentList.Add("-sn"); psi.ArgumentList.Add("-dn");
			psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(FFToolsUtils.LongPathFix(filePath));
			psi.ArgumentList.Add("-vf");
			// round=up: with the keyframe-only stream, fps' default nearest-rounding emits
			// ZERO frames for videos whose only keyframe sits at t=0 (short clips) — the
			// single frame lands between output ticks and is dropped at EOF.
			psi.ArgumentList.Add(FormattableString.Invariant(
				$"fps=1/{intervalSeconds:0.###}:round=up,scale={AI.OnnxEmbedder.InputSide}:{AI.OnnxEmbedder.InputSide}:flags=bicubic,format=rgb24"));
			psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add(maxFrames.ToString(CultureInfo.InvariantCulture));
			psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("rawvideo");
			psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("rgb24");
			psi.ArgumentList.Add("pipe:1");

			using var process = new Process { StartInfo = psi };
			string errOut = string.Empty;
			Task<int>? pendingRead = null;
			int frameCount = 0;
			try {
				process.Start();
				FFToolsUtils.LowerChildPriority(process);
				process.ErrorDataReceived += (_, e) => {
					if (e.Data?.Length > 0)
						errOut += Environment.NewLine + e.Data;
				};
				process.BeginErrorReadLine();
				Stream stdout = process.StandardOutput.BaseStream;
				// Per-read inactivity budget. Reads are async so this bounded wait stays
				// authoritative: a synchronous read only returns once ffmpeg writes, which
				// never happens when the process stalls mid-decode (dead network share,
				// wedged demuxer) — timeout and Stop were unreachable in exactly those
				// cases (#865). A keyframe sweep of a multi-hour file takes minutes in
				// TOTAL, but a healthy ffmpeg keeps bytes flowing; only a wedged one is
				// silent this long. The clock deliberately does not run while the caller
				// is busy inside onFrame (embedding) and nobody is reading.
				int readTimeoutMs = (int)TimeSpan.FromMinutes(15).TotalMilliseconds;
				while (frameCount < maxFrames) {
					byte[] frame = AI.FramePool.Shared.Rent();
					int filled = 0;
					try {
						while (filled < frameBytes) {
							pendingRead = stdout.ReadAsync(frame, filled, frameBytes - filled, cancelToken);
							if (!pendingRead.Wait(readTimeoutMs, cancelToken))
								throw new TimeoutException($"FFmpeg timed out on file: {filePath}");
							int bytesRead = pendingRead.Result;
							pendingRead = null;
							if (bytesRead == 0)
								break; // EOF — a partial trailing frame is discarded
							filled += bytesRead;
						}
					}
					catch {
						AI.FramePool.Shared.Return(frame);
						throw;
					}
					if (filled < frameBytes) {
						AI.FramePool.Shared.Return(frame);
						break;
					}
					frameCount++;
					onFrame(frame); // ownership transfers to the callback
				}
				if (!process.WaitForExit(30_000))
					throw new TimeoutException($"FFmpeg did not exit after closing its output: {filePath}");
				process.WaitForExit(); // flush async stderr handlers

				if (process.ExitCode != 0)
					throw new FFInvalidExitCodeException($"FFmpeg exited with: {process.ExitCode}");
				if (frameCount == 0)
					throw new Exception("FFmpeg produced no frames");
				if (extendedLogging && errOut.Length > 0)
					Logger.Instance.Warn($"WARNING: Problems while dense-sampling AI frames from: {filePath}{errOut}");
				return frameCount;
			}
			catch (OperationCanceledException) {
				FFToolsUtils.KillAndDrain(process, pendingRead);
				throw;
			}
			catch (Exception e) {
				FFToolsUtils.KillAndDrain(process, pendingRead);
				string? hint = FfmpegErrorClassifier.Classify(errOut);
				Logger.Instance.Warn($"ERROR: Failed dense-sampling AI frames from: {filePath}{errOut}{Environment.NewLine}{e.Message}" +
					(hint != null ? $"{Environment.NewLine}Hint: {hint}" : string.Empty));
				return -1;
			}
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

		/// <summary>
		/// Widens a coded frame size to its display size using the stream's sample
		/// (pixel) aspect ratio. Unknown (0), degenerate or implausible SARs leave the
		/// size unchanged — bad container metadata must not distort the thumbnail.
		/// </summary>
		internal static Size ApplySampleAspectRatio(Size codedSize, int sarNum, int sarDen) {
			if (sarNum <= 0 || sarDen <= 0 || sarNum == sarDen)
				return codedSize;
			long displayWidth = (long)Math.Round(codedSize.Width * (double)sarNum / sarDen);
			if (displayWidth <= 0 || displayWidth > 65536)
				return codedSize;
			return new Size((int)displayWidth, codedSize.Height);
		}

		/// <summary>
		/// The CLI <c>-vf</c> fragment that widens anamorphic video to its display width
		/// (<c>setsar=1</c>), or null for images (square pixels; the image2 demuxer pipeline
		/// is fragile — #806). Mirrors <see cref="ApplySampleAspectRatio"/>: an unknown SAR
		/// (0) is treated as square, and an implausible SAR that would push the display width
		/// past 65536 falls back to the coded width — bad container metadata must not ask
		/// ffmpeg for a multi-hundred-megapixel frame (which just OOMs / fails the thumbnail).
		/// </summary>
		internal static string? BuildSarNormalizationFilter(string file) {
			if (FileUtils.IsImageFile(file))
				return null;
			// SAR multiplier, unknown (0) treated as 1. Commas inside the expression are
			// escaped so the filtergraph parser doesn't read them as filter separators.
			const string sarMul = "if(eq(sar\\,0)\\,1\\,sar)";
			return $"scale=if(gt(iw*{sarMul}\\,65536)\\,iw\\,trunc(iw*{sarMul})):ih,setsar=1";
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
				// Tiled HEIF (Apple photos): the real picture only exists as an assembled
				// tile grid, which the native binding cannot produce — decoding the "best"
				// stream would silently hash a single tile or an aux depth/gain map (#869).
				if (vsd.HasStreamGroups && FileUtils.IsHeifImageFile(path))
					throw new Exception($"Tiled HEIF needs FFmpeg's grid assembly; using the process fallback for '{path}'");
				if (!vsd.TryDecodeFrame(out var srcFrame, TimeSpan.Zero))
					throw new Exception($"TryDecodeFrame failed for image '{path}'");

				Size sourceSize = new(
					srcFrame.width > 0 ? srcFrame.width : vsd.FrameSize.Width,
					srcFrame.height > 0 ? srcFrame.height : vsd.FrameSize.Height);
				AVPixelFormat srcPixFmt = ResolveSourcePixelFormat(srcFrame.format, vsd.PixelFormat);
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
					Logger.Instance.Warn($"Native image decode failed on '{path}', falling back to process mode. Exception: {e}");
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
					Logger.Instance.Warn($"Native BGRA->JPEG encode failed, falling back to process mode. Exception: {e}");
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
			Task? readTask = null;
			try {
				process.Start();
				FFToolsUtils.LowerChildPriority(process);
				using var ms = new MemoryStream();
				// Write input and read output concurrently to avoid pipe-buffer deadlocks.
				readTask = process.StandardOutput.BaseStream.CopyToAsync(ms);
				process.StandardInput.BaseStream.Write(bgra, 0, width * height * 4);
				process.StandardInput.BaseStream.Flush();
				process.StandardInput.Close();
				if (!readTask.Wait(TimeoutDuration))
					throw new TimeoutException("FFmpeg timed out encoding JPEG from raw pixels.");
				if (!process.WaitForExit(TimeoutDuration))
					throw new TimeoutException("FFmpeg did not exit after encoding JPEG from raw pixels.");
				if (process.ExitCode != 0)
					throw new FFInvalidExitCodeException($"FFmpeg exited with: {process.ExitCode}");
				byte[] jpeg = ms.ToArray();
				return jpeg.Length > 0 ? jpeg : null;
			}
			catch (Exception e) {
				Logger.Instance.Warn($"BGRA->JPEG encode via FFmpeg process failed: {e.Message}");
				// Also drains/observes the pending stdout copy — the disposed MemoryStream
				// would otherwise fault it after the fact as an unobserved task exception.
				FFToolsUtils.KillAndDrain(process, readTask);
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
		/// <summary>
		/// Produce a raw 224×224 RGB24 frame (the AI embedding input) instead of gray
		/// bytes or a JPEG thumbnail. Like gray bytes it is force-scaled to a fixed
		/// square, so aspect ratio is erased identically on both sides of a comparison.
		/// </summary>
		public bool Rgb224;
	}
}
