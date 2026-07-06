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
	/// size, and the (uniform) row height follows it so wider previews actually get taller
	/// rows — mirroring how the old DataGrid grew with the thumbnail. Pure math, unit-tested.
	/// </summary>
	internal static class ResultsRowSizing {
		// Height/width ratio from the approved mockup (thumb height = 0.62 × column width).
		const double ComfortableRatio = 0.62;
		const double CompactRatio = 0.40;

		internal static double ImageHeight(double previewWidth, bool compact) =>
			compact
				? Math.Clamp(previewWidth * CompactRatio, 28, 200)
				: Math.Clamp(previewWidth * ComfortableRatio, 40, 340);

		/// <summary>
		/// Uniform row height: the preview plus breathing room, but never below what the
		/// two text lines (file name + path) need. Without the preview column the text
		/// baseline alone decides.
		/// </summary>
		internal static double RowHeight(double previewWidth, bool compact, bool previewVisible) {
			double textBaseline = compact ? 42 : 68;
			if (!previewVisible)
				return textBaseline;
			return Math.Max(textBaseline, ImageHeight(previewWidth, compact) + (compact ? 8 : 12));
		}
	}
}
