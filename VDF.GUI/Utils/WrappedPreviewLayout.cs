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
	/// Display-time layout for the results preview: the composite is stored as a fixed
	/// grid (ThumbnailGridLayout) but rendered frame by frame, re-wrapped to the actual
	/// Preview column width. Frames behave like the classic strip — they shrink to fit
	/// the line — until they would become unreadably small; only then a row is added
	/// (#834). A wide enough column therefore always lays every frame on one line
	/// (#847), and few large frames stay a single line of scaled-down frames rather
	/// than a tower of full-size ones. Pure math, unit-tested.
	/// </summary>
	internal static class WrappedPreviewLayout {
		/// <summary>Spacing between frames, both directions.</summary>
		internal const double Gap = 2;
		// Total preview height caps — the same values the estimate box has always
		// clamped to (ResultsRowSizing). When the wrap would exceed them, all frames
		// scale down uniformly until it fits.
		internal const double MaxTotalHeightComfortable = 600;
		internal const double MaxTotalHeightCompact = 340;
		// Readability floor: a frame this short is the point where wrapping another
		// row beats shrinking further. Matches the list's minimum image heights.
		internal const double MinFrameHeightComfortable = 40;
		internal const double MinFrameHeightCompact = 28;

		internal readonly record struct Layout(int FramesPerRow, int Rows, double FrameWidth, double FrameHeight) {
			public double TotalWidth => FramesPerRow * FrameWidth + (FramesPerRow - 1) * Gap;
			public double TotalHeight => Rows * FrameHeight + (Rows - 1) * Gap;
		}

		/// <summary>
		/// Picks the fewest rows whose frames stay above the readability floor. Within
		/// a row count the frames fill the column width but never exceed their natural
		/// (extracted) size (#787). When even one frame per row can't reach the floor
		/// (very narrow column), the frames simply get as large as the column allows.
		/// </summary>
		internal static Layout Compute(double availableWidth, bool compact, double cellWidth, double cellHeight, int frameCount) {
			if (frameCount < 1) frameCount = 1;
			if (cellWidth <= 0 || cellHeight <= 0 || double.IsNaN(cellWidth) || double.IsNaN(cellHeight))
				return new Layout(1, frameCount, 0, 0);
			availableWidth = double.IsFinite(availableWidth) ? Math.Max(availableWidth, 16) : double.MaxValue;
			double maxTotalHeight = compact ? MaxTotalHeightCompact : MaxTotalHeightComfortable;
			// A floor above the natural frame height would be unreachable by design.
			double minFrameHeight = Math.Min(compact ? MinFrameHeightCompact : MinFrameHeightComfortable, cellHeight);

			for (int rows = 1; rows <= frameCount; rows++) {
				int perRow = (frameCount + rows - 1) / rows; // fewest columns reaching this row count
				if ((frameCount + perRow - 1) / perRow != rows) continue; // row count not achievable
				double scale = Scale(availableWidth, maxTotalHeight, cellWidth, cellHeight, perRow, rows);
				if (scale * cellHeight >= minFrameHeight)
					return new Layout(perRow, rows, cellWidth * scale, cellHeight * scale);
			}
			// No row count keeps the frames readable: one per row, as large as fits.
			double fallback = Math.Max(0.01, Scale(availableWidth, maxTotalHeight, cellWidth, cellHeight, 1, frameCount));
			return new Layout(1, frameCount, cellWidth * fallback, cellHeight * fallback);
		}

		static double Scale(double availableWidth, double maxTotalHeight, double cellWidth, double cellHeight, int perRow, int rows) {
			double widthScale = (availableWidth - (perRow - 1) * Gap) / (perRow * cellWidth);
			double heightScale = (maxTotalHeight - (rows - 1) * Gap) / (rows * cellHeight);
			return Math.Min(1, Math.Min(widthScale, heightScale));
		}
	}
}
