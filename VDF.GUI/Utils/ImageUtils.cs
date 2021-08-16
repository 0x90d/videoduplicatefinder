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

using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using VDF.Core.Utils;

namespace VDF.GUI.Utils {
	static class ImageUtils {
		public static string ThumbnailDirectory => Core.Utils.FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "Thumbnails");
		public static Bitmap JoinImages(List<System.Drawing.Image> pImgList) {
			if (pImgList == null || pImgList.Count == 0) return null;

			string file = Core.Utils.FileUtils.SafePathCombine(ThumbnailDirectory, Path.GetRandomFileName() + ".jpeg");

			int height = pImgList[0].Height;
			int width = 0;
			for (int i = 0; i <= pImgList.Count - 1; i++)
				width += pImgList[i].Width;
			using System.Drawing.Bitmap img = new System.Drawing.Bitmap(width, height);
			using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(img)) {
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				int tmpwidth = 0;
				for (int i = 0; i <= pImgList.Count - 1; i++) {
					g.DrawImage(pImgList[i], tmpwidth, 0);
					tmpwidth += pImgList[i].Width;
				}
				g.Save();
			}
			img.Save(file, img.RawFormat);
			return new Bitmap(file);
		}
	}
}
