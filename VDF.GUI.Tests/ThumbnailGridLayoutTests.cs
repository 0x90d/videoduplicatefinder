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

using VDF.GUI.Utils;

namespace VDF.GUI.Tests {
	public class ThumbnailGridLayoutTests {
		const double Wide = 16.0 / 9;   // landscape video frame
		const double Tall = 9.0 / 16;   // portrait video frame

		[Theory]
		// The common low counts keep the familiar single-strip look.
		[InlineData(1, 1)]
		[InlineData(2, 2)]
		[InlineData(3, 3)]
		// From 4 frames on the strip gets too wide and wraps (#834).
		[InlineData(4, 2)]   // 2×2
		[InlineData(6, 4)]   // 4+2
		[InlineData(7, 4)]   // 4+3
		[InlineData(12, 4)]  // 4×3 — was a ~21:1 strip of tiny frames
		public void Columns_LandscapeFrames(int count, int expectedColumns) {
			Assert.Equal(expectedColumns, ThumbnailGridLayout.Columns(count, Wide));
		}

		[Fact]
		public void Columns_PortraitFramesAllowMorePerRow() {
			Assert.Equal(8, ThumbnailGridLayout.Columns(12, Tall));
			Assert.True(ThumbnailGridLayout.Columns(12, Tall) > ThumbnailGridLayout.Columns(12, Wide));
		}

		[Fact]
		public void Columns_LastRowNeverMoreThanHalfEmpty() {
			for (int count = 2; count <= 24; count++) {
				int columns = ThumbnailGridLayout.Columns(count, Wide);
				int rows = ThumbnailGridLayout.Rows(count, columns);
				int empty = columns * rows - count;
				Assert.True(empty * 2 <= columns, $"count={count}: {columns} cols leave {empty} empty cells");
			}
		}

		[Fact]
		public void Columns_InvalidAspectFallsBackToLandscape() {
			Assert.Equal(ThumbnailGridLayout.Columns(12, Wide), ThumbnailGridLayout.Columns(12, 0));
			Assert.Equal(ThumbnailGridLayout.Columns(12, Wide), ThumbnailGridLayout.Columns(12, double.NaN));
		}

		[Theory]
		[InlineData(12, 4, 3)]
		[InlineData(7, 4, 2)]
		[InlineData(1, 1, 1)]
		public void Rows_CeilingDivision(int count, int columns, int expectedRows) {
			Assert.Equal(expectedRows, ThumbnailGridLayout.Rows(count, columns));
		}
	}
}
