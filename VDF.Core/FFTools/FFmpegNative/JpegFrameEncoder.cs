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

using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	/// <summary>
	/// Encodes a single AVFrame into JPEG bytes using FFmpeg's MJPEG encoder.
	/// The frame must already be in <see cref="AVPixelFormat.AV_PIX_FMT_YUVJ420P"/>
	/// (use <see cref="VideoFrameConverter"/>); MJPEG encodes that directly, which
	/// also avoids the BGRA detour the old ImageSharp-based path needed.
	/// </summary>
	static unsafe class JpegFrameEncoder {
		/// <summary>
		/// Encodes <paramref name="frame"/> as a baseline JPEG.
		/// </summary>
		/// <param name="frame">A YUVJ420P frame whose buffers are still owned by the converter.</param>
		/// <param name="quality">JPEG quality 1-100; mapped onto MJPEG's 2-31 qscale (lower = better).</param>
		internal static byte[] Encode(AVFrame frame, int quality = 90) {
			AVCodec* codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MJPEG);
			if (codec == null)
				throw new FFInvalidExitCodeException("MJPEG encoder not available.");

			AVCodecContext* ctx = ffmpeg.avcodec_alloc_context3(codec);
			if (ctx == null)
				throw new FFInvalidExitCodeException("Failed to allocate MJPEG encoder context.");

			AVPacket* packet = null;
			try {
				ctx->width = frame.width;
				ctx->height = frame.height;
				ctx->pix_fmt = (AVPixelFormat)frame.format;
				ctx->time_base = new AVRational { num = 1, den = 25 };
				ctx->flags |= ffmpeg.AV_CODEC_FLAG_QSCALE;
				// quality 100 -> q2 (best MJPEG allows), 90 -> q3, 50 -> q7, ...
				int qscale = Math.Clamp(2 + (100 - quality) / 10, 2, 31);
				ctx->global_quality = ffmpeg.FF_QP2LAMBDA * qscale;

				ffmpeg.avcodec_open2(ctx, codec, null).ThrowExceptionIfError();

				packet = ffmpeg.av_packet_alloc();
				if (packet == null)
					throw new FFInvalidExitCodeException("Failed to allocate AVPacket.");

				AVFrame localFrame = frame;
				localFrame.quality = ctx->global_quality;
				// Copying the AVFrame struct leaves extended_data pointing at the SOURCE
				// frame's data array. avcodec_send_frame -> av_frame_ref requires
				// extended_data == data for formats with <= AV_NUM_DATA_POINTERS planes
				// and otherwise fails with EINVAL ("Invalid argument"). That made every
				// native JPEG encode throw and silently fall back to the FFmpeg process
				// (issues #793/#795). Re-point extended_data at this copy's own data array.
				localFrame.extended_data = (byte**)&localFrame.data;
				ffmpeg.avcodec_send_frame(ctx, &localFrame).ThrowExceptionIfError();
				ffmpeg.avcodec_send_frame(ctx, null); // flush — single-frame encode
				ffmpeg.avcodec_receive_packet(ctx, packet).ThrowExceptionIfError();

				byte[] jpeg = new byte[packet->size];
				Marshal.Copy((IntPtr)packet->data, jpeg, 0, packet->size);
				return jpeg;
			}
			finally {
				if (packet != null)
					ffmpeg.av_packet_free(&packet);
				ffmpeg.avcodec_free_context(&ctx);
			}
		}
	}
}
