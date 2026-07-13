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
			Assert.Equal(600, ResultsRowSizing.ImageHeight(5000, compact: false));
			Assert.Equal(28, ResultsRowSizing.ImageHeight(10, compact: true));
			Assert.Equal(340, ResultsRowSizing.ImageHeight(5000, compact: true));
		}

		// #834: a loaded wide filmstrip must not reserve the mockup-ratio box — the row
		// shrinks to what the image actually renders at (Uniform, fills cell width).
		[Fact]
		public void ImageHeight_FollowsLoadedCompositeAspect() {
			// 2 frames at 100×56 joined horizontally -> 200×56 composite.
			double h = ResultsRowSizing.ImageHeight(480, compact: false, thumbWidth: 200, thumbHeight: 56);
			Assert.Equal(56, h);
			// Without the composite the same column width reserved a ~298px box.
			Assert.True(h < ResultsRowSizing.ImageHeight(480, compact: false) / 4);
		}

		[Fact]
		public void ImageHeight_NeverUpscalesPastComposite() {
			// DownOnly (#787): a 640×90 strip in a 1600px column renders at natural size.
			Assert.Equal(90, ResultsRowSizing.ImageHeight(1600, compact: false, thumbWidth: 640, thumbHeight: 90));
		}

		[Fact]
		public void ImageHeight_GrowsWithColumnUntilNaturalSize() {
			// 1200×300 composite: at 960px column it renders width-limited...
			double h = ResultsRowSizing.ImageHeight(960, compact: false, thumbWidth: 1200, thumbHeight: 300);
			Assert.Equal((960 - 8) * 300d / 1200, h, precision: 5);
			// ...and keeps growing as the column widens (the reporter's actual complaint).
			Assert.True(ResultsRowSizing.ImageHeight(1400, compact: false, thumbWidth: 1200, thumbHeight: 300) > h);
		}

		[Fact]
		public void ImageHeight_PortraitCompositeIsCappedByEstimate() {
			// A tall portrait still must not blow the row up past the mockup-ratio box.
			double cap = ResultsRowSizing.ImageHeight(200, compact: false);
			Assert.Equal(cap, ResultsRowSizing.ImageHeight(200, compact: false, thumbWidth: 100, thumbHeight: 178));
		}

		[Fact]
		public void RowHeight_FollowsImageHeightWhenPreviewVisible() {
			double row = ResultsRowSizing.RowHeight(340, compact: false, previewVisible: true);
			double image = ResultsRowSizing.ImageHeight(340, compact: false);
			Assert.True(row > image);
		}

		[Fact]
		public void RowHeight_ShrinksToTextBaselineForThinStrips() {
			// The 200×56 strip fits inside the text lines' room -> no empty box (#834).
			Assert.Equal(68, ResultsRowSizing.RowHeight(480, compact: false, previewVisible: true, thumbWidth: 200, thumbHeight: 56));
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
