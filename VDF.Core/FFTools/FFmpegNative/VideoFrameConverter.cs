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

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace VDF.Core.FFTools.FFmpegNative {
	sealed unsafe class VideoFrameConverter : IDisposable {
		private readonly IntPtr _convertedFrameBufferPtr;
		private readonly Size _destinationSize;
		private readonly byte_ptrArray4 _dstData;
		private readonly int_array4 _dstLinesize;
		private readonly SwsContext* _pConvertContext;

		public enum ScaleQuality {
			FastBilinear,
			Bilinear,
			Bicubic,   // empfohlen
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

			_destinationSize = destinationSize;

			int flags = quality switch {
				ScaleQuality.FastBilinear => ffmpeg.SWS_FAST_BILINEAR,
				ScaleQuality.Bilinear => ffmpeg.SWS_BILINEAR,
				ScaleQuality.Bicubic => ffmpeg.SWS_BICUBIC,
				ScaleQuality.Lanczos => ffmpeg.SWS_LANCZOS,
				ScaleQuality.Spline => ffmpeg.SWS_SPLINE,
				ScaleQuality.Area => ffmpeg.SWS_AREA,
				_ => ffmpeg.SWS_BICUBIC
			};

			if (bitExact) flags |= ffmpeg.SWS_BITEXACT;

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


			int convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat,
				destinationSize.Width,
				destinationSize.Height,
				1).ThrowExceptionIfError();
			_convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
			_dstData = new byte_ptrArray4();
			_dstLinesize = new int_array4();

			ffmpeg.av_image_fill_arrays(ref _dstData,
				ref _dstLinesize,
				(byte*)_convertedFrameBufferPtr,
				destinationPixelFormat,
				destinationSize.Width,
				destinationSize.Height,
				1).ThrowExceptionIfError();
		}

		public void Dispose() {
			Marshal.FreeHGlobal(_convertedFrameBufferPtr);
			ffmpeg.sws_freeContext(_pConvertContext);
		}

		public AVFrame Convert(AVFrame sourceFrame) {
			ffmpeg.sws_scale(_pConvertContext,
				sourceFrame.data,
				sourceFrame.linesize,
				0,
				sourceFrame.height,
				_dstData,
				_dstLinesize).ThrowExceptionIfError();

			byte_ptrArray8 data = new();
			data.UpdateFrom(_dstData);
			int_array8 linesize = new();
			linesize.UpdateFrom(_dstLinesize);

			return new AVFrame {
				data = data,
				linesize = linesize,
				width = _destinationSize.Width,
				height = _destinationSize.Height
			};
		}

	}
}
