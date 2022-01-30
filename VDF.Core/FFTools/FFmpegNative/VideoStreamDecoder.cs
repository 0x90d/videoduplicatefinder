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

using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	sealed unsafe class VideoStreamDecoder : IDisposable {
		private readonly AVCodecContext* _pCodecContext;
		private readonly AVFormatContext* _pFormatContext;
		private readonly AVFrame* _pFrame;
		private readonly AVPacket* _pPacket;
		private readonly AVFrame* _pReceivedFrame;
		private readonly int _streamIndex;

		public VideoStreamDecoder(string url, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE) {
			_pFormatContext = ffmpeg.avformat_alloc_context();
			_pReceivedFrame = ffmpeg.av_frame_alloc();
			var pFormatContext = _pFormatContext;
			ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();
			ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();
			AVCodec* codec = null;

			_streamIndex = ffmpeg.av_find_best_stream(_pFormatContext,
				AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0).ThrowExceptionIfError();
			_pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
			if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
				ffmpeg.av_hwdevice_ctx_create(&_pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0).ThrowExceptionIfError();
			ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar).ThrowExceptionIfError();
			ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

			CodecName = ffmpeg.avcodec_get_name(codec->id);
			FrameSize = new Size(_pCodecContext->width, _pCodecContext->height);
			PixelFormat = HWDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? _pCodecContext->pix_fmt : GetHWPixelFormat(HWDeviceType, codec);

			_pPacket = ffmpeg.av_packet_alloc();
			_pFrame = ffmpeg.av_frame_alloc();
		}

		public string CodecName { get; }
		public Size FrameSize { get; }
		public AVPixelFormat PixelFormat { get; }

		public void Dispose() {
			AVFrame* pFrame = _pFrame;
			ffmpeg.av_frame_free(&pFrame);
			AVFrame* pReceivedFrame = _pReceivedFrame;
			ffmpeg.av_frame_free(&pReceivedFrame);

			AVPacket* pPacket = _pPacket;
			ffmpeg.av_packet_free(&pPacket);

			AVCodecContext* pCodecContext = _pCodecContext;
			ffmpeg.avcodec_free_context(&pCodecContext);

			AVFormatContext* pFormatContext = _pFormatContext;
			ffmpeg.avformat_close_input(&pFormatContext);
		}

		public bool TryDecodeFrame(out AVFrame frame, TimeSpan position) {
			ffmpeg.av_frame_unref(_pFrame);
			ffmpeg.av_frame_unref(_pReceivedFrame);
			int error;

			AVRational timebase = _pFormatContext->streams[_streamIndex]->time_base;
			float AV_TIME_BASE = (float)timebase.den / timebase.num;
			long tc = Convert.ToInt64(position.TotalSeconds * AV_TIME_BASE);

			if (ffmpeg.av_seek_frame(_pFormatContext, _streamIndex, tc, ffmpeg.AVSEEK_FLAG_BACKWARD) < 0)
				ffmpeg.av_seek_frame(_pFormatContext, _streamIndex, tc, ffmpeg.AVSEEK_FLAG_ANY).ThrowExceptionIfError();
			do {
				try {
					do {
						ffmpeg.av_packet_unref(_pPacket);
						error = ffmpeg.av_read_frame(_pFormatContext, _pPacket);

						if (error == ffmpeg.AVERROR_EOF) {
							frame = *_pFrame;
							return false;
						}

						error.ThrowExceptionIfError();
					} while (_pPacket->stream_index != _streamIndex);

					ffmpeg.avcodec_send_packet(_pCodecContext, _pPacket).ThrowExceptionIfError();
				}
				finally {
					ffmpeg.av_packet_unref(_pPacket);
				}

				error = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
			} while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

			error.ThrowExceptionIfError();

			if (_pCodecContext->hw_device_ctx != null) {
				ffmpeg.av_hwframe_transfer_data(_pReceivedFrame, _pFrame, 0).ThrowExceptionIfError();
				frame = *_pReceivedFrame;
			}
			else
				frame = *_pFrame;

			return true;
		}

		private unsafe AVPixelFormat GetHWPixelFormat(AVHWDeviceType hwDevice, AVCodec* codec) {
			const int AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 1;
			AVPixelFormat pixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;

			for (int i = 0; ; i++) {
				AVCodecHWConfig* hwConfig = ffmpeg.avcodec_get_hw_config(codec, i);
				if (hwConfig == null) {
					throw new Exception($"Failed to find compatible pixel format for {hwDevice}");
				}
				if ((hwConfig->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == 0 || hwConfig->device_type != hwDevice) {
					continue;
				}

				AVHWFramesConstraints* hwConstraints = ffmpeg.av_hwdevice_get_hwframe_constraints(_pCodecContext->hw_device_ctx, hwConfig);
				if (hwConstraints != null) {
					for (AVPixelFormat* p = hwConstraints->valid_sw_formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++) {
						pixelFormat = *p;
						if (ffmpeg.sws_isSupportedInput(pixelFormat) > 0) {
							break;
						}
						else {
							pixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
						}
					}

					ffmpeg.av_hwframe_constraints_free(&hwConstraints);
				}

				if (pixelFormat != AVPixelFormat.AV_PIX_FMT_NONE) {
					return pixelFormat;
				}
			}
		}
	}
}
