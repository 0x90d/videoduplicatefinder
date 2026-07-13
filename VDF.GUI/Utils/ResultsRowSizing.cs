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

namespace VDF.GUI.Utils {
	/// <summary>
	/// Sizing rules of the new results list: the Preview column width sets the thumbnail
	/// size, and the row height follows the loaded composite's real aspect ratio so the
	/// preview fills its row instead of floating in an empty box (#834). While the
	/// thumbnail is still loading, the mockup ratio serves as the estimate. Pure math,
	/// unit-tested.
	/// </summary>
	internal static class ResultsRowSizing {
		// Height/width ratio from the approved mockup (thumb height = 0.62 × column width).
		// Once the composite is loaded its real aspect wins and this only caps the height.
		const double ComfortableRatio = 0.62;
		const double CompactRatio = 0.40;
		// Horizontal space the preview cell loses to the image's right margin.
		const double PreviewGutter = 8;

		internal static double ImageHeight(double previewWidth, bool compact, double thumbWidth = 0, double thumbHeight = 0) {
			double min = compact ? 28 : 40;
			double max = compact ? 340 : 600;
			double estimate = Math.Clamp(previewWidth * (compact ? CompactRatio : ComfortableRatio), min, max);
			if (thumbWidth <= 0 || thumbHeight <= 0)
				return estimate;
			// The Image renders Uniform + DownOnly: it fills the cell width but never
			// upscales past the composite bitmap (#787), so reserve exactly the height
			// it will actually get. The estimate stays as ceiling so portrait content
			// can't blow the row up.
			double displayedWidth = Math.Min(Math.Max(previewWidth - PreviewGutter, 16), thumbWidth);
			return Math.Clamp(displayedWidth * thumbHeight / thumbWidth, min, estimate);
		}

		/// <summary>
		/// Row height: the preview plus breathing room, but never below what the two
		/// text lines (file name + path) need. Without the preview column the text
		/// baseline alone decides.
		/// </summary>
		internal static double RowHeight(double previewWidth, bool compact, bool previewVisible, double thumbWidth = 0, double thumbHeight = 0) {
			double textBaseline = compact ? 42 : 68;
			if (!previewVisible)
				return textBaseline;
			return Math.Max(textBaseline, ImageHeight(previewWidth, compact, thumbWidth, thumbHeight) + (compact ? 8 : 12));
		}
	}
}
