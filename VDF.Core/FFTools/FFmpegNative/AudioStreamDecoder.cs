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

using System.Diagnostics;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {

	/// <summary>
	/// Decodes the full audio stream of a file and resamples it to mono 16-bit
	/// signed PCM at a target sample rate, feeding chunks into a callback as
	/// they become available.  Modelled after <see cref="VideoStreamDecoder"/>.
	/// </summary>
	unsafe sealed class AudioStreamDecoder : IDisposable {
		private AVFormatContext* _pFormatContext;
		private AVCodecContext* _pCodecContext;
		private SwrContext* _pSwrContext;
		private AVFrame* _pFrame;
		private AVPacket* _pPacket;
		private readonly int _streamIndex;
		private readonly AVIOInterruptCB_callback _interruptCbDelegate;
		private readonly long _timeoutTicks;
		private long _deadlineTicks;
		private CancellationToken _ct;

		private const AVSampleFormat TargetFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;

		// Input layout the resampler is currently configured for. Corrupt streams (e.g. an
		// AAC channel-config change mid-stream, issue #861) can emit frames whose layout
		// diverges from these values; feeding such a frame into a stale SwrContext makes
		// swresample read plane pointers that do not exist — a native access violation
		// that kills the whole process. DecodeAll() compares every frame against these
		// and rebuilds the resampler on mismatch.
		private int _swrInFormat;
		private int _swrInSampleRate;
		private int _swrInChannels;

		// Reusable output buffer for resampled PCM (grown as needed, avoids per-frame allocation)
		private byte[] _outBuf = new byte[8192];

		/// <summary>True if the file contained a usable audio stream.</summary>
		public bool HasAudioStream { get; }

		/// <summary>Total stream duration in seconds, or 0 when unknown.</summary>
		public double DurationSeconds { get; private set; }

		private readonly int _targetSampleRate;

		public AudioStreamDecoder(string url, int targetSampleRate, CancellationToken ct = default, int timeoutMs = 120_000) {
			_targetSampleRate = targetSampleRate;
			_pFormatContext = ffmpeg.avformat_alloc_context();
			if (_pFormatContext == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVFormatContext.");

			// Interrupt callback: aborts blocking I/O when the per-operation
			// deadline expires or the scan is cancelled.  The deadline is reset
			// after each successful av_read_frame in DecodeAll(), so total decode
			// time for long files is not capped — only individual hung calls.
			_ct = ct;
			_timeoutTicks = (long)(timeoutMs / 1000.0 * Stopwatch.Frequency);
			_deadlineTicks = Stopwatch.GetTimestamp() + _timeoutTicks;
			_interruptCbDelegate = _ => (_ct.IsCancellationRequested || Stopwatch.GetTimestamp() > _deadlineTicks) ? 1 : 0;
			_pFormatContext->interrupt_callback = new AVIOInterruptCB { callback = _interruptCbDelegate };

			// avformat_open_input frees the context and nulls the local on failure.
			// Sync the field with the local before checking the result so the finalizer
			// does not later see a dangling pointer if the open fails.
			var pFormatContext = _pFormatContext;
			int openRet = ffmpeg.avformat_open_input(&pFormatContext, url, null, null);
			_pFormatContext = pFormatContext;
			openRet.ThrowExceptionIfError();
			ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();

			AVCodec* codec = null;
			int streamIdx = ffmpeg.av_find_best_stream(_pFormatContext,
				AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &codec, 0);
			if (streamIdx < 0) {
				HasAudioStream = false;
				return;
			}
			HasAudioStream = true;
			_streamIndex = streamIdx;

			// Duration for progress reporting — AV_TIME_BASE is microseconds.
			// Prefer stream duration (converted via its own time_base) over container duration, which can be 0 for some formats.
			if (_pFormatContext->duration > 0)
				DurationSeconds = _pFormatContext->duration / (double)ffmpeg.AV_TIME_BASE;
			else {
				var audioStream = _pFormatContext->streams[_streamIndex];
				if (audioStream->duration > 0) {
					var tb = audioStream->time_base;
					DurationSeconds = audioStream->duration * (double)tb.num / tb.den;
				}
			}

			// Tell the demuxer to discard all non-audio streams at the container
			// level.  For interleaved formats (MKV, MP4, …) this lets the demuxer
			// skip video/subtitle packets without reading their payload from disk,
			// which dramatically reduces I/O — especially over network storage.
			for (int i = 0; i < (int)_pFormatContext->nb_streams; i++) {
				if (i != _streamIndex)
					_pFormatContext->streams[i]->discard = AVDiscard.AVDISCARD_ALL;
			}

			_pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
			if (_pCodecContext == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVCodecContext.");
			ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar).ThrowExceptionIfError();
			ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

			// Set up resampler: source format → mono s16 at targetSampleRate
			CreateResampler(_pCodecContext->ch_layout, _pCodecContext->sample_fmt, _pCodecContext->sample_rate);

			_pPacket = ffmpeg.av_packet_alloc();
			if (_pPacket == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVPacket.");
			_pFrame = ffmpeg.av_frame_alloc();
			if (_pFrame == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVFrame.");
		}

		/// <summary>
		/// (Re)creates the SwrContext for the given input layout and records the layout so
		/// <see cref="DecodeAll"/> can detect frames that no longer match it.
		/// </summary>
		private void CreateResampler(AVChannelLayout inLayout, AVSampleFormat inFormat, int inSampleRate) {
			if (_pSwrContext != null) {
				SwrContext* old = _pSwrContext;
				ffmpeg.swr_free(&old);
				_pSwrContext = null;
			}
			SwrContext* swr = null;
			var outLayout = new AVChannelLayout();
			ffmpeg.av_channel_layout_default(&outLayout, 1); // mono
			try {
				ffmpeg.swr_alloc_set_opts2(&swr,
					&outLayout, TargetFormat, _targetSampleRate,
					&inLayout, inFormat, inSampleRate,
					0, null).ThrowExceptionIfError();
				ffmpeg.swr_init(swr).ThrowExceptionIfError();
			}
			catch {
				if (swr != null)
					ffmpeg.swr_free(&swr);
				throw;
			}
			_pSwrContext = swr;
			_swrInFormat = (int)inFormat;
			_swrInSampleRate = inSampleRate;
			_swrInChannels = inLayout.nb_channels;
		}

		/// <summary>
		/// True when a decoded frame's layout still matches what the resampler was configured
		/// for. Channel COUNT is what matters for memory safety: fewer planes than configured
		/// makes swr_convert dereference null/garbage plane pointers (issue #861). Format and
		/// rate mismatches additionally corrupt the fingerprint. Same-count channel-order
		/// changes are irrelevant here — everything is downmixed to mono anyway.
		/// </summary>
		internal static bool ResamplerInputMatches(
			int frameFormat, int frameSampleRate, int frameChannels,
			int configuredFormat, int configuredSampleRate, int configuredChannels) =>
			frameFormat == configuredFormat &&
			frameSampleRate == configuredSampleRate &&
			frameChannels == configuredChannels;

		/// <summary>True when the frame reports a layout that cannot be resampled at all.</summary>
		internal static bool IsUndecodableFrameLayout(int frameFormat, int frameSampleRate, int frameChannels, int nbSamples) =>
			frameFormat < 0 || frameSampleRate <= 0 || frameChannels <= 0 || nbSamples <= 0;

		/// <summary>
		/// Ensures the resampler matches <c>_pFrame</c>'s actual layout, rebuilding it when a
		/// (corrupt) stream changed layout mid-decode. Returns the samples flushed out of the
		/// old resampler, or -1 when the frame is garbage and must be skipped.
		/// </summary>
		private int EnsureResamplerMatchesFrame(Action<ReadOnlySpan<short>> onSamples) {
			int frameFormat = _pFrame->format;
			int frameRate = _pFrame->sample_rate;
			int frameChannels = _pFrame->ch_layout.nb_channels;
			if (IsUndecodableFrameLayout(frameFormat, frameRate, frameChannels, _pFrame->nb_samples))
				return -1;
			if (ResamplerInputMatches(frameFormat, frameRate, frameChannels, _swrInFormat, _swrInSampleRate, _swrInChannels))
				return 0;
			// Flush the tail buffered in the old resampler before replacing it.
			int flushed = ConvertAndDeliver(null, 0, onSamples);
			CreateResampler(_pFrame->ch_layout, (AVSampleFormat)frameFormat, frameRate);
			return flushed;
		}

		/// <summary>
		/// Decodes the entire audio stream and calls <paramref name="onSamples"/>
		/// with each chunk of resampled mono s16 PCM samples.
		/// </summary>
		/// <returns>Total number of output samples produced.</returns>
		public int DecodeAll(Action<ReadOnlySpan<short>> onSamples, CancellationToken ct, Action<double>? onProgress = null) {
			if (!HasAudioStream) return 0;

			_ct = ct;
			_deadlineTicks = Stopwatch.GetTimestamp() + _timeoutTicks;
			int totalSamples = 0;
			// Safety cap: a broken codec could emit unbounded frames per packet. 10K matches the video decoder cap.
			const int maxFramesPerPacket = 10_000;
			double expectedSamples = DurationSeconds * _targetSampleRate;
			int lastReportedPercent = -1;

			while (!ct.IsCancellationRequested) {
				ffmpeg.av_packet_unref(_pPacket);
				int readResult = ffmpeg.av_read_frame(_pFormatContext, _pPacket);
				if (readResult == ffmpeg.AVERROR_EOF)
					break;
				readResult.ThrowExceptionIfError();
				_deadlineTicks = Stopwatch.GetTimestamp() + _timeoutTicks;

				if (_pPacket->stream_index != _streamIndex)
					continue;

				int sendResult = ffmpeg.avcodec_send_packet(_pCodecContext, _pPacket);
				if (sendResult < 0 && sendResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
					continue; // skip corrupted / problematic packets instead of aborting

				for (int iter = 0; iter < maxFramesPerPacket && !ct.IsCancellationRequested; iter++) {
					int recvResult = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
					if (recvResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || recvResult == ffmpeg.AVERROR_EOF)
						break;
					recvResult.ThrowExceptionIfError();

					int flushed = EnsureResamplerMatchesFrame(onSamples);
					if (flushed < 0) { // garbage frame layout — skip it
						ffmpeg.av_frame_unref(_pFrame);
						continue;
					}
					totalSamples += flushed;
					totalSamples += ConvertAndDeliver(_pFrame->extended_data, _pFrame->nb_samples, onSamples);
					ffmpeg.av_frame_unref(_pFrame);
				}

				if (onProgress != null && expectedSamples > 0) {
					// 100.0 forces double math: `100 * totalSamples` as int×int overflows past
					// ~21.5M samples (≈32.5 min at 11025 Hz), wrapping the percentage negative
					// partway through any long file. Clamp low too — duration metadata can
					// under-report, pushing the ratio past 100 before EOF.
					int pct = Math.Clamp((int)(100.0 * totalSamples / expectedSamples), 0, 99);
					if (pct != lastReportedPercent) {
						lastReportedPercent = pct;
						onProgress(pct / 100.0);
					}
				}
			}

			// Flush the decoder (send null packet)
			if (!ct.IsCancellationRequested) {
				ffmpeg.avcodec_send_packet(_pCodecContext, null);
				for (int iter = 0; iter < maxFramesPerPacket; iter++) {
					int recvResult = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
					if (recvResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || recvResult == ffmpeg.AVERROR_EOF)
						break;
					recvResult.ThrowExceptionIfError();

					int flushed = EnsureResamplerMatchesFrame(onSamples);
					if (flushed < 0) { // garbage frame layout — skip it
						ffmpeg.av_frame_unref(_pFrame);
						continue;
					}
					totalSamples += flushed;
					totalSamples += ConvertAndDeliver(_pFrame->extended_data, _pFrame->nb_samples, onSamples);
					ffmpeg.av_frame_unref(_pFrame);
				}
			}

			// Flush the resampler (may hold buffered samples)
			if (!ct.IsCancellationRequested) {
				totalSamples += ConvertAndDeliver(null, 0, onSamples);
			}

			return totalSamples;
		}

		/// <summary>
		/// Resamples audio from <paramref name="inputData"/> (or flushes the resampler
		/// when <paramref name="inputData"/> is null) and delivers the result via callback.
		/// </summary>
		private int ConvertAndDeliver(byte** inputData, int inputSamples, Action<ReadOnlySpan<short>> onSamples) {
			int outSamples = ffmpeg.swr_get_out_samples(_pSwrContext, inputSamples);
			if (outSamples <= 0) return 0;

			int requiredBytes = outSamples * 2; // s16 = 2 bytes per sample
			if (_outBuf.Length < requiredBytes)
				_outBuf = new byte[requiredBytes];

			int converted;
			fixed (byte* outPtr = _outBuf) {
				byte* outBuf = outPtr;
				converted = ffmpeg.swr_convert(_pSwrContext,
					&outBuf, outSamples,
					inputData, inputSamples);
			}
			if (converted <= 0) return 0;

			var span = MemoryMarshal.Cast<byte, short>(_outBuf.AsSpan(0, converted * 2));
			onSamples(span);
			return converted;
		}

		public void Dispose() {
			// Null each field after freeing so a finalizer running after Dispose
			// (or a double Dispose) can't pass dangling pointers back to FFmpeg.
			if (_pFrame != null) {
				AVFrame* pFrame = _pFrame;
				ffmpeg.av_frame_free(&pFrame);
				_pFrame = null;
			}
			if (_pPacket != null) {
				AVPacket* pPacket = _pPacket;
				ffmpeg.av_packet_free(&pPacket);
				_pPacket = null;
			}
			if (_pSwrContext != null) {
				SwrContext* swr = _pSwrContext;
				ffmpeg.swr_free(&swr);
				_pSwrContext = null;
			}
			if (_pCodecContext != null) {
				AVCodecContext* pCodecContext = _pCodecContext;
				ffmpeg.avcodec_free_context(&pCodecContext);
				_pCodecContext = null;
			}
			if (_pFormatContext != null) {
				AVFormatContext* pFormatContext = _pFormatContext;
				ffmpeg.avformat_close_input(&pFormatContext);
				_pFormatContext = null;
			}
			GC.SuppressFinalize(this);
		}

		~AudioStreamDecoder() {
			Dispose();
		}
	}
}
