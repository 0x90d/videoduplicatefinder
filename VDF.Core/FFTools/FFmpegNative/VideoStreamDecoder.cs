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
using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	unsafe class VideoStreamDecoder : IDisposable {
		private AVCodecContext* _pCodecContext;
		private AVFormatContext* _pFormatContext;
		private AVFrame* _pFrame;
		private AVPacket* _pPacket;
		private AVFrame* _pReceivedFrame;
		private readonly int _streamIndex;
		private readonly AVIOInterruptCB_callback _interruptCbDelegate;
		private readonly long _timeoutTicks;
		private long _deadlineTicks;

		public VideoStreamDecoder(string url, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE, int timeoutMs = 15_000) {
			_pFormatContext = ffmpeg.avformat_alloc_context();
			if (_pFormatContext == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVFormatContext.");

			// Set up an interrupt callback so FFmpeg aborts blocking I/O when the
			// timeout expires.  This lets Dispose() run normally and release the
			// file handle — unlike killing a thread, which would leak it.
			// The deadline is re-armed at the start of every TryDecodeFrame: the same
			// decoder serves all sampled positions of a file (batch extraction), and a
			// single construction-time deadline made the TOTAL decode time of the batch
			// count against one 15 s budget — long/slow files tripped the interrupt
			// halfway through and every remaining position failed to CLI fallback.
			_timeoutTicks = (long)(timeoutMs / 1000.0 * Stopwatch.Frequency);
			_deadlineTicks = Stopwatch.GetTimestamp() + _timeoutTicks;
			_interruptCbDelegate = _ => Stopwatch.GetTimestamp() > _deadlineTicks ? 1 : 0;
			_pFormatContext->interrupt_callback = new AVIOInterruptCB { callback = _interruptCbDelegate };

			_pReceivedFrame = ffmpeg.av_frame_alloc();
			if (_pReceivedFrame == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVFrame for received frame.");
			// avformat_open_input frees the context and nulls the local on failure.
			// Sync the field with the local before checking the result so the finalizer
			// does not later see a dangling pointer if the open fails.
			var pFormatContext = _pFormatContext;
			int openRet = ffmpeg.avformat_open_input(&pFormatContext, url, null, null);
			_pFormatContext = pFormatContext;
			openRet.ThrowExceptionIfError();
			ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();
			AVCodec* codec = null;

			_streamIndex = ffmpeg.av_find_best_stream(_pFormatContext,
				AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0).ThrowExceptionIfError();
			_pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
			if (_pCodecContext == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVCodecContext.");
			if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
				ffmpeg.av_hwdevice_ctx_create(&_pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0).ThrowExceptionIfError();
			ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar).ThrowExceptionIfError();
			ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

			CodecName = ffmpeg.avcodec_get_name(codec->id);
			// Container-level pixel aspect ratio for anamorphic content; 0/1 when unknown.
			StreamSampleAspectRatio = _pFormatContext->streams[_streamIndex]->sample_aspect_ratio;
			FrameSize = new Size(_pCodecContext->width, _pCodecContext->height);
			if (FrameSize.Width <= 0 || FrameSize.Height <= 0)
				throw new FFInvalidExitCodeException($"Invalid frame dimensions {FrameSize.Width}x{FrameSize.Height}.");
			// For HW decode we intentionally defer the source pixel format until the
			// first frame has been downloaded with av_hwframe_transfer_data — only then
			// do we know the real sw_format (e.g. P010LE for 10-bit HEVC vs NV12 for
			// 8-bit). Guessing before decode breaks 10-bit content.
			PixelFormat = HWDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
				? _pCodecContext->pix_fmt
				: AVPixelFormat.AV_PIX_FMT_NONE;
			IsHardwareDecode = HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

			_pPacket = ffmpeg.av_packet_alloc();
			if (_pPacket == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVPacket.");
			_pFrame = ffmpeg.av_frame_alloc();
			if (_pFrame == null)
				throw new FFInvalidExitCodeException("Failed to allocate AVFrame.");
		}

		public string CodecName { get; }
		public Size FrameSize { get; }
		public AVPixelFormat PixelFormat { get; }
		public bool IsHardwareDecode { get; }
		public AVRational StreamSampleAspectRatio { get; }

		protected virtual void Dispose(bool disposing) {
			ReleaseUnmanaged();
		}

		~VideoStreamDecoder() {
			Dispose(false);
		}

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public void ReleaseUnmanaged() {
			// Null each field after freeing so a partially-constructed object's finalizer
			// or a double-Dispose can't pass dangling pointers back to FFmpeg.
			if (_pFrame != null) {
				AVFrame* pFrame = _pFrame;
				ffmpeg.av_frame_free(&pFrame);
				_pFrame = null;
			}
			if (_pReceivedFrame != null) {
				AVFrame* pReceivedFrame = _pReceivedFrame;
				ffmpeg.av_frame_free(&pReceivedFrame);
				_pReceivedFrame = null;
			}
			if (_pPacket != null) {
				AVPacket* pPacket = _pPacket;
				ffmpeg.av_packet_free(&pPacket);
				_pPacket = null;
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
		}

		public bool TryDecodeFrame(out AVFrame frame, TimeSpan position) {
			// Fresh timeout budget per position — see the constructor note.
			_deadlineTicks = Stopwatch.GetTimestamp() + _timeoutTicks;
			ffmpeg.av_frame_unref(_pFrame);
			ffmpeg.av_frame_unref(_pReceivedFrame);

			AVRational timebase = _pFormatContext->streams[_streamIndex]->time_base;
			double AV_TIME_BASE = (double)timebase.den / timebase.num;
			long targetPts = Convert.ToInt64(position.TotalSeconds * AV_TIME_BASE);

			// Only seek when a non-zero position is requested. Seeking to the start is at best a
			// no-op and on single-frame image demuxers (image2/mjpeg) it overshoots the lone
			// packet, leaving nothing to decode — which silently broke native still-image decode
			// and forced a CLI fallback for every JPEG (native analogue of the #801 -ss-on-stills
			// bug). For position 0 we just decode forward from the start, matching the CLI grabber.
			if (targetPts > 0) {
				if (ffmpeg.av_seek_frame(_pFormatContext, _streamIndex, targetPts, ffmpeg.AVSEEK_FLAG_BACKWARD) < 0)
					ffmpeg.av_seek_frame(_pFormatContext, _streamIndex, targetPts, ffmpeg.AVSEEK_FLAG_ANY).ThrowExceptionIfError();

				ffmpeg.avcodec_flush_buffers(_pCodecContext);
			}

			// Decode forward from keyframe until we reach the target PTS.
			// Cap iterations to prevent infinite loops on corrupt files.
			const int maxIterations = 10_000;
			// AVERROR_INVALIDDATA on the first read(s) after seek is normal: the demuxer
			// can hand us partial packets between the seek target and the next keyframe.
			// Skip them silently rather than tearing down the decoder and falling back
			// to the CLI process — see issue #731. Cap so a truly corrupt file still bails.
			const int maxBadPackets = 64;
			int badPacketCount = 0;
			// Once the demuxer is exhausted we send a single null packet to put the decoder
			// into draining mode. Intra single-frame codecs (e.g. MJPEG still images) buffer
			// their only frame and emit it only after a flush; without draining that frame is
			// never received and still-image decoding fails outright, forcing a CLI fallback
			// (native analogue of the #801 -ss-on-stills bug).
			bool draining = false;
			for (int iter = 0; iter < maxIterations; iter++) {
				if (!draining) {
					int error;
					while (true) {
						ffmpeg.av_packet_unref(_pPacket);
						error = ffmpeg.av_read_frame(_pFormatContext, _pPacket);
						if (error == ffmpeg.AVERROR_EOF) {
							// No more packets — flush the decoder rather than giving up.
							ffmpeg.av_packet_unref(_pPacket);
							ffmpeg.avcodec_send_packet(_pCodecContext, null).ThrowExceptionIfError();
							draining = true;
							break;
						}
						if (error == ffmpeg.AVERROR_INVALIDDATA) {
							if (++badPacketCount > maxBadPackets) {
								frame = *_pFrame;
								return false;
							}
							continue;
						}
						error.ThrowExceptionIfError();
						if (_pPacket->stream_index == _streamIndex) break;
					}

					if (!draining) {
						int sendErr;
						try {
							sendErr = ffmpeg.avcodec_send_packet(_pCodecContext, _pPacket);
						}
						finally {
							ffmpeg.av_packet_unref(_pPacket);
						}
						if (sendErr == ffmpeg.AVERROR_INVALIDDATA || sendErr == ffmpeg.AVERROR(ffmpeg.EINVAL)) {
							if (++badPacketCount > maxBadPackets) {
								frame = *_pFrame;
								return false;
							}
							continue;
						}
						sendErr.ThrowExceptionIfError();
					}
				}

				int recvErr = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
				if (recvErr == ffmpeg.AVERROR(ffmpeg.EAGAIN)) {
					// In draining mode EAGAIN cannot occur; if it somehow does, the decoder
					// has nothing left to give, so bail rather than spin to maxIterations.
					if (draining) {
						frame = *_pFrame;
						return false;
					}
					continue;
				}
				if (recvErr < 0) { // includes AVERROR_EOF: decoder fully drained, no frame at target
					frame = *_pFrame;
					return false;
				}

				// Check if we've reached or passed the target position
				if (_pFrame->pts >= targetPts || _pFrame->pts == ffmpeg.AV_NOPTS_VALUE)
					break;

				// Not at target yet - discard this frame and decode the next
				ffmpeg.av_frame_unref(_pFrame);
			}

			// Only download when the frame actually lives in GPU memory. Hardware
			// decoders can silently fall back to software frames (unsupported
			// profile/level); calling av_hwframe_transfer_data on those returns
			// EINVAL and needlessly failed the whole file to the CLI fallback.
			// Callers already read the source format from the frame itself when
			// hardware decode was requested, so a software frame flows through fine.
			if (_pCodecContext->hw_device_ctx != null && _pFrame->hw_frames_ctx != null) {
				ffmpeg.av_hwframe_transfer_data(_pReceivedFrame, _pFrame, 0).ThrowExceptionIfError();
				// The transfer copies only pixel data; frame properties live on the source
				// hw frame and must be carried across explicitly. Notably sample_aspect_ratio,
				// which the display-thumbnail path reads to widen anamorphic content — without
				// this the SAR correction silently no-ops under hardware decode.
				ffmpeg.av_frame_copy_props(_pReceivedFrame, _pFrame);
				frame = *_pReceivedFrame;
			}
			else
				frame = *_pFrame;

			return true;
		}

	}
}
