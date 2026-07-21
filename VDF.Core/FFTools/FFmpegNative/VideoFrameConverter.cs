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

using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	sealed unsafe class VideoFrameConverter : IDisposable {
		private readonly AVFrame* _pConvertedFrame;
		private readonly SwsContext* _pConvertContext;
		private readonly Size _sourceSize;
		private readonly AVPixelFormat _sourcePixelFormat;

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

			_sourceSize = sourceSize;
			_sourcePixelFormat = sourcePixelFormat;
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

		/// <summary>
		/// Throws when a frame does not match the layout the SwsContext was built for.
		/// sws_scale trusts the context's configuration when reading the source planes, so a
		/// diverging frame (corrupt file, mid-stream format/resolution change — issue #861)
		/// makes it read out of bounds: a native access violation that kills the process.
		/// A managed exception here instead fails the file over to the CLI fallback.
		/// </summary>
		internal static void ValidateSourceFrame(in AVFrame frame, Size expectedSize, AVPixelFormat expectedFormat) {
			if (frame.width != expectedSize.Width || frame.height != expectedSize.Height || frame.format != (int)expectedFormat)
				throw new FFInvalidExitCodeException(
					$"Source frame layout {frame.width}x{frame.height} (format {frame.format}) does not match " +
					$"converter configuration {expectedSize.Width}x{expectedSize.Height} (format {(int)expectedFormat}).");
			if (frame.data[0] == null)
				throw new FFInvalidExitCodeException("Source frame has no pixel data (data[0] is null).");
		}

		public AVFrame Convert(AVFrame sourceFrame) {
			ValidateSourceFrame(in sourceFrame, _sourceSize, _sourcePixelFormat);
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
