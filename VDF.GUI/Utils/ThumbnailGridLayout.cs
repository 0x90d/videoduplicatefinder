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
	/// Picks the column count for the composite preview thumbnail. Joining every frame
	/// into one horizontal strip made high thumbnail counts unusable — 12 frames of a
	/// 16:9 video give a ~21:1 strip whose frames stay tiny at any sane column width
	/// (#834) — so many frames wrap into multiple rows instead.
	/// </summary>
	internal static class ThumbnailGridLayout {
		// Composite width/height ratio the layout steers toward. Roughly two 16:9
		// frames side by side; wide enough that the common 1-3 frame cases stay a
		// single row.
		const double TargetAspect = 3.5;

		/// <summary>
		/// Column count for <paramref name="count"/> frames of the given width/height
		/// aspect: the grid whose overall shape lands closest to the target aspect.
		/// Grids whose last row would be more than half empty are skipped — a lone
		/// frame under a full row looks broken, not compact.
		/// </summary>
		internal static int Columns(int count, double frameAspect) {
			if (count <= 1) return 1;
			if (frameAspect <= 0 || double.IsNaN(frameAspect)) frameAspect = 16.0 / 9;
			int best = count;
			double bestScore = double.MaxValue;
			for (int columns = 1; columns <= count; columns++) {
				int rows = Rows(count, columns);
				int emptyCells = columns * rows - count;
				if (emptyCells * 2 > columns) continue;
				double score = Math.Abs(Math.Log(columns * frameAspect / rows / TargetAspect));
				if (score < bestScore) {
					bestScore = score;
					best = columns;
				}
			}
			return best;
		}

		internal static int Rows(int count, int columns) => (count + columns - 1) / columns;
	}
}
