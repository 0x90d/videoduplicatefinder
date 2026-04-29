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

using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	sealed unsafe class VideoFrameConverter : IDisposable {
		private readonly AVFrame* _pConvertedFrame;
		private readonly SwsContext* _pConvertContext;

		public enum ScaleQuality {
			FastBilinear,
			Bilinear,
			Bicubic,
			Lanczos,
			Spline,
			Area
		}

		public VideoFrameConverter(Size sourceSize,
			AVPixelFormat sourcePixelFormat,
			Size destinationSize,
			AVPixelFormat destinationPixelFormat,
			ScaleQuality quality = ScaleQuality.Bicubic,
			bool bitExact = false) {

			int flags = quality switch {
				ScaleQuality.FastBilinear => (int)SwsFlags.SWS_FAST_BILINEAR,
				ScaleQuality.Bilinear => (int)SwsFlags.SWS_BILINEAR,
				ScaleQuality.Bicubic => (int)SwsFlags.SWS_BICUBIC,
				ScaleQuality.Lanczos => (int)SwsFlags.SWS_LANCZOS,
				ScaleQuality.Spline => (int)SwsFlags.SWS_SPLINE,
				ScaleQuality.Area => (int)SwsFlags.SWS_AREA,
				_ => (int)SwsFlags.SWS_BICUBIC
			};

			if (bitExact) flags |= (int)SwsFlags.SWS_BITEXACT;

			_pConvertContext = ffmpeg.sws_getContext(sourceSize.Width,
				sourceSize.Height,
				sourcePixelFormat,
				destinationSize.Width,
				destinationSize.Height,
				destinationPixelFormat,
				flags,
				null,
				null,
				null);
			if (_pConvertContext == null)
				throw new FFInvalidExitCodeException("Could not initialize the conversion context.");

			_pConvertedFrame = ffmpeg.av_frame_alloc();
			if (_pConvertedFrame == null)
				throw new FFInvalidExitCodeException("Failed to allocate destination AVFrame.");

			_pConvertedFrame->format = (int)destinationPixelFormat;
			_pConvertedFrame->width = destinationSize.Width;
			_pConvertedFrame->height = destinationSize.Height;

			// Give swscale a real padded destination frame instead of a tightly packed align=1 buffer.
			// Passing 0 lets FFmpeg pick the alignment that fits the current CPU (recommended by libavutil).
			ffmpeg.av_frame_get_buffer(_pConvertedFrame, 0).ThrowExceptionIfError();
		}

		public void Dispose() {
			AVFrame* convertedFrame = _pConvertedFrame;
			ffmpeg.av_frame_free(&convertedFrame);
			ffmpeg.sws_freeContext(_pConvertContext);
		}

		public AVFrame Convert(AVFrame sourceFrame) {
			ffmpeg.av_frame_make_writable(_pConvertedFrame).ThrowExceptionIfError();
			ffmpeg.sws_scale(_pConvertContext,
				sourceFrame.data,
				sourceFrame.linesize,
				0,
				sourceFrame.height,
				_pConvertedFrame->data,
				_pConvertedFrame->linesize).ThrowExceptionIfError();

			return *_pConvertedFrame;
		}
	}
}
