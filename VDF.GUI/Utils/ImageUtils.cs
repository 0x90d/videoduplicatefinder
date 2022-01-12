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


using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace VDF.GUI.Utils {
	static class ImageUtils {
		public static Bitmap? JoinImages(List<Image> pImgList) {
			if (pImgList == null || pImgList.Count == 0) return null;

			int height = pImgList[0].Height;
			int width = 0;
			for (int i = 0; i <= pImgList.Count - 1; i++)
				width += pImgList[i].Width;

			using var img = new Image<Rgba32>(width, height); // create output image of the correct dimensions

			List<Point> locations = new(pImgList.Count);
			int tmpwidth = 0;
			for (int i = 0; i <= pImgList.Count - 1; i++) {
				img.Mutate(a => a.DrawImage(pImgList[i], new Point(tmpwidth, 0), 1f));
				tmpwidth += pImgList[i].Width;
			}

			using MemoryStream ms = new();
			img.Save(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
			ms.Position = 0;
			return new Bitmap(ms);
		}

		public static byte[] ToByteArray(this Bitmap image) {
			using MemoryStream ms = new();
			image.Save(ms);
			return ms.ToArray();
		}
		public static byte[] ToByteArray(this Image image) {
			using MemoryStream ms = new();
			image.Save(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
			return ms.ToArray();
		}
	}
}
