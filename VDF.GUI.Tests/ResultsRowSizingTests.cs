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
	public class ResultsRowSizingTests {

		[Fact]
		public void ImageHeight_ScalesWithPreviewWidth() {
			double narrow = ResultsRowSizing.ImageHeight(120, compact: false);
			double wide = ResultsRowSizing.ImageHeight(340, compact: false);
			Assert.True(wide > narrow);
			Assert.Equal(340 * 0.62, wide, precision: 1);
		}

		[Fact]
		public void ImageHeight_CompactIsSmallerThanComfortable() {
			Assert.True(
				ResultsRowSizing.ImageHeight(200, compact: true) <
				ResultsRowSizing.ImageHeight(200, compact: false));
		}

		[Fact]
		public void ImageHeight_IsClamped() {
			Assert.Equal(40, ResultsRowSizing.ImageHeight(10, compact: false));
			Assert.Equal(340, ResultsRowSizing.ImageHeight(5000, compact: false));
			Assert.Equal(28, ResultsRowSizing.ImageHeight(10, compact: true));
			Assert.Equal(200, ResultsRowSizing.ImageHeight(5000, compact: true));
		}

		[Fact]
		public void RowHeight_FollowsImageHeightWhenPreviewVisible() {
			double row = ResultsRowSizing.RowHeight(340, compact: false, previewVisible: true);
			double image = ResultsRowSizing.ImageHeight(340, compact: false);
			Assert.True(row > image);
		}

		[Fact]
		public void RowHeight_NeverDropsBelowTextBaseline() {
			// Tiny preview: two text lines still need their room.
			Assert.Equal(68, ResultsRowSizing.RowHeight(56, compact: false, previewVisible: true));
			Assert.Equal(42, ResultsRowSizing.RowHeight(56, compact: true, previewVisible: true));
		}

		[Fact]
		public void RowHeight_IgnoresPreviewWidthWhenColumnHidden() {
			Assert.Equal(68, ResultsRowSizing.RowHeight(480, compact: false, previewVisible: false));
			Assert.Equal(42, ResultsRowSizing.RowHeight(480, compact: true, previewVisible: false));
		}
	}
}
