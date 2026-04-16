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
		private readonly AVFormatContext* _pFormatContext;
		private readonly AVCodecContext* _pCodecContext;
		private readonly SwrContext* _pSwrContext;
		private readonly AVFrame* _pFrame;
		private readonly AVPacket* _pPacket;
		private readonly int _streamIndex;
		private readonly AVIOInterruptCB_callback _interruptCbDelegate;
		private readonly long _timeoutTicks;
		private long _deadlineTicks;
		private CancellationToken _ct;

		private const AVSampleFormat TargetFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;

		// Reusable output buffer for resampled PCM (grown as needed, avoids per-frame allocation)
		private byte[] _outBuf = new byte[8192];

		/// <summary>True if the file contained a usable audio stream.</summary>
		public bool HasAudioStream { get; }

		public AudioStreamDecoder(string url, int targetSampleRate, CancellationToken ct = default, int timeoutMs = 120_000) {
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

			var pFormatContext = _pFormatContext;
			ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();
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
			SwrContext* swr = null;
			var outLayout = new AVChannelLayout();
			ffmpeg.av_channel_layout_default(&outLayout, 1); // mono
			var inLayout = _pCodecContext->ch_layout;

			ffmpeg.swr_alloc_set_opts2(&swr,
				&outLayout, TargetFormat, targetSampleRate,
				&inLayout, _pCodecContext->sample_fmt, _pCodecContext->sample_rate,
				0, null).ThrowExceptionIfError();
			ffmpeg.swr_init(swr).ThrowExceptionIfError();
			_pSwrContext = swr;

			_pPacket = ffmpeg.av_packet_alloc();
			if (_pPacket == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVPacket.");
			_pFrame = ffmpeg.av_frame_alloc();
			if (_pFrame == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVFrame.");
		}

		/// <summary>
		/// Decodes the entire audio stream and calls <paramref name="onSamples"/>
		/// with each chunk of resampled mono s16 PCM samples.
		/// </summary>
		/// <returns>Total number of output samples produced.</returns>
		public int DecodeAll(Action<ReadOnlySpan<short>> onSamples, CancellationToken ct) {
			if (!HasAudioStream) return 0;

			_ct = ct;
			_deadlineTicks = Stopwatch.GetTimestamp() + _timeoutTicks;
			int totalSamples = 0;

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

				while (!ct.IsCancellationRequested) {
					int recvResult = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
					if (recvResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || recvResult == ffmpeg.AVERROR_EOF)
						break;
					recvResult.ThrowExceptionIfError();

					totalSamples += ConvertAndDeliver(_pFrame->extended_data, _pFrame->nb_samples, onSamples);
					ffmpeg.av_frame_unref(_pFrame);
				}
			}

			// Flush the decoder (send null packet)
			if (!ct.IsCancellationRequested) {
				ffmpeg.avcodec_send_packet(_pCodecContext, null);
				while (true) {
					int recvResult = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
					if (recvResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || recvResult == ffmpeg.AVERROR_EOF)
						break;
					recvResult.ThrowExceptionIfError();

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
			if (_pFrame != null) {
				AVFrame* pFrame = _pFrame;
				ffmpeg.av_frame_free(&pFrame);
			}
			if (_pPacket != null) {
				AVPacket* pPacket = _pPacket;
				ffmpeg.av_packet_free(&pPacket);
			}
			if (_pSwrContext != null) {
				SwrContext* swr = _pSwrContext;
				ffmpeg.swr_free(&swr);
			}
			if (_pCodecContext != null) {
				AVCodecContext* pCodecContext = _pCodecContext;
				ffmpeg.avcodec_free_context(&pCodecContext);
			}
			if (_pFormatContext != null) {
				AVFormatContext* pFormatContext = _pFormatContext;
				ffmpeg.avformat_close_input(&pFormatContext);
			}
			GC.SuppressFinalize(this);
		}

		~AudioStreamDecoder() {
			Dispose();
		}
	}
}
