using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace VideoDuplicateFinderLinux {
	static class Utils {
		public static string ThumbnailDirectory => DuplicateFinderEngine.Utils.SafePathCombine(Path.GetDirectoryName(typeof(MainWindow).Assembly.Location), "Thumbnails");
		public static Bitmap JoinImages(List<System.Drawing.Image> pImgList) {
			if (pImgList == null || pImgList.Count == 0) return null;

			var file =
				DuplicateFinderEngine.Utils.SafePathCombine(ThumbnailDirectory, Path.GetRandomFileName() + ".jpeg");

			var height = pImgList[0].Height;
			var width = 0;
			for (var i = 0; i <= pImgList.Count - 1; i++)
				width += pImgList[i].Width;
			using var img = new System.Drawing.Bitmap(width, height);
			using (var g = System.Drawing.Graphics.FromImage(img)) {
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				var tmpwidth = 0;
				for (var i = 0; i <= pImgList.Count - 1; i++) {
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
