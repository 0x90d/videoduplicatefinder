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

namespace VDF.Core.FFTools.FFmpegNative {
	/// <summary>
	/// How a decoded picture has to be turned to be shown upright: the display matrix a
	/// phone writes instead of rotating the pixels (#910). The FFmpeg command line applies it
	/// by default (autorotate); the libraries hand it over as side data and leave it to the
	/// caller, so the native path hashed the picture sideways or upside down, and a rotated
	/// original never matched its re-encoded, upright copy. Expressed as a transpose followed
	/// by flips, which covers every multiple of 90 degrees with or without a mirror.
	/// </summary>
	readonly record struct FrameOrientation(bool Transpose, bool FlipH, bool FlipV) {
		public static FrameOrientation None => default;
		public bool IsIdentity => !Transpose && !FlipH && !FlipV;

		/// <summary>Managed av_display_rotation_get: the counterclockwise angle, NaN for a degenerate matrix.</summary>
		internal static double DisplayRotationDegrees(ReadOnlySpan<int> m) {
			const double Fixed16 = 65536.0;
			double scale0 = Math.Sqrt(m[0] / Fixed16 * (m[0] / Fixed16) + m[3] / Fixed16 * (m[3] / Fixed16));
			double scale1 = Math.Sqrt(m[1] / Fixed16 * (m[1] / Fixed16) + m[4] / Fixed16 * (m[4] / Fixed16));
			if (scale0 == 0 || scale1 == 0)
				return double.NaN;
			double rotation = Math.Atan2(m[1] / Fixed16 / scale1, m[0] / Fixed16 / scale0) * 180 / Math.PI;
			return -rotation;
		}

		/// <summary>
		/// The same decision the FFmpeg command line takes for its autorotate filters
		/// (fftools: get_rotation and the transpose/hflip/vflip choice in ffmpeg_filter.c),
		/// so native and process decoding produce the same picture. Angles that are not a
		/// multiple of 90 degrees are left alone; the command line would rotate those onto a
		/// larger canvas, which no square hash can reproduce anyway.
		/// </summary>
		internal static FrameOrientation FromDisplayMatrix(ReadOnlySpan<int> m) {
			if (m.Length < 9)
				return None;
			double rotation = DisplayRotationDegrees(m);
			if (double.IsNaN(rotation))
				return None;
			double theta = -Math.Round(rotation);
			theta -= 360 * Math.Floor(theta / 360 + 0.9 / 360);

			if (Math.Abs(theta - 90) < 1.0)
				// transpose=cclock_flip or transpose=clock
				return m[3] > 0 ? new(true, false, false) : new(true, true, false);
			if (Math.Abs(theta - 180) < 1.0)
				return new(false, m[0] < 0, m[4] < 0);
			if (Math.Abs(theta - 270) < 1.0)
				// transpose=clock_flip or transpose=cclock
				return m[3] < 0 ? new(true, true, true) : new(true, false, true);
			if (Math.Abs(theta) < 1.0)
				return new(false, false, m[4] < 0);
			return None;
		}

		/// <summary>Width and height after turning.</summary>
		public (int Width, int Height) Apply(int width, int height) => Transpose ? (height, width) : (width, height);

		/// <summary>Turns a tightly packed picture (gray bytes, RGB24) of <paramref name="bytesPerPixel"/>.</summary>
		public byte[] Apply(byte[] source, int width, int height, int bytesPerPixel) {
			if (IsIdentity)
				return source;
			var result = new byte[source.Length];
			unsafe {
				fixed (byte* src = source)
				fixed (byte* dst = result)
					ApplyPlane(src, width * bytesPerPixel, width, height, bytesPerPixel, dst, Apply(width, height).Width * bytesPerPixel);
			}
			return result;
		}

		/// <summary>Turns one plane with strides, e.g. a plane of a YUV frame.</summary>
		public unsafe void ApplyPlane(byte* src, int srcStride, int width, int height, int bytesPerPixel, byte* dst, int dstStride) {
			var (dw, dh) = Apply(width, height);
			for (int dy = 0; dy < dh; dy++) {
				int ty = FlipV ? dh - 1 - dy : dy;
				for (int dx = 0; dx < dw; dx++) {
					int tx = FlipH ? dw - 1 - dx : dx;
					int sx = Transpose ? ty : tx;
					int sy = Transpose ? tx : ty;
					byte* s = src + (long)sy * srcStride + (long)sx * bytesPerPixel;
					byte* d = dst + (long)dy * dstStride + (long)dx * bytesPerPixel;
					for (int b = 0; b < bytesPerPixel; b++)
						d[b] = s[b];
				}
			}
		}
	}
}
