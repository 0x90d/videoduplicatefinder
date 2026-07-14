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
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	public class WrappedPreviewLayoutTests {
		// Default extraction size: ThumbnailMaxWidth 100, 16:9 -> 100×56 frames.
		const double CellW = 100, CellH = 56;

		// #847: five frames in a wide enough column stay on ONE line, at natural size.
		[Fact]
		public void WideColumn_AllFramesOnOneLine() {
			var layout = WrappedPreviewLayout.Compute(600, compact: false, CellW, CellH, frameCount: 5);
			Assert.Equal(5, layout.FramesPerRow);
			Assert.Equal(1, layout.Rows);
			Assert.Equal(CellW, layout.FrameWidth, precision: 5);
			Assert.Equal(CellH, layout.FrameHeight, precision: 5);
		}

		// #834: a narrow column wraps — but only once frames would drop below the
		// readability floor, and the wrapped frames stay above it.
		[Fact]
		public void NarrowColumn_WrapsBeforeFramesGetUnreadable() {
			var layout = WrappedPreviewLayout.Compute(152, compact: false, CellW, CellH, frameCount: 5);
			Assert.Equal(2, layout.FramesPerRow);
			Assert.Equal(3, layout.Rows);
			Assert.True(layout.FrameHeight >= WrappedPreviewLayout.MinFrameHeightComfortable);
			// One line of five would have been 17px tall — unreadable.
			Assert.True(layout.FrameHeight < CellH); // shrunk to fit two per line
		}

		[Fact]
		public void MidColumn_PartialWrap() {
			// Two natural-size frames per line fit (2·100 + 2 gap = 202); one line of
			// five would be 22px tall, under the floor.
			var layout = WrappedPreviewLayout.Compute(210, compact: false, CellW, CellH, frameCount: 5);
			Assert.Equal(2, layout.FramesPerRow);
			Assert.Equal(3, layout.Rows);
			Assert.Equal(CellW, layout.FrameWidth, precision: 5);
		}

		// User report 2026-07-14: three LARGE frames (ThumbnailMaxWidth ~425) in a
		// ~470px column must shrink onto ONE line like the classic strip — not stack
		// full-size below each other.
		[Fact]
		public void LargeFrames_ShrinkOntoOneLine_InsteadOfStacking() {
			var layout = WrappedPreviewLayout.Compute(462, compact: false, cellWidth: 425, cellHeight: 239, frameCount: 3);
			Assert.Equal(3, layout.FramesPerRow);
			Assert.Equal(1, layout.Rows);
			Assert.True(layout.FrameHeight >= WrappedPreviewLayout.MinFrameHeightComfortable);
			Assert.True(layout.TotalWidth <= 462 + 0.001);
		}

		// The floor only forces a wrap when the wrap actually helps; a single frame
		// below the floor (tiny column) still renders as large as the column allows.
		[Fact]
		public void FloorUnreachable_FallsBackToWidthFit() {
			var layout = WrappedPreviewLayout.Compute(40, compact: false, CellW, CellH, frameCount: 3);
			Assert.Equal(1, layout.FramesPerRow);
			Assert.Equal(3, layout.Rows);
			Assert.Equal(40, layout.FrameWidth, precision: 5);
		}

		// #787: frames never upscale past their extracted size, no matter the room.
		[Fact]
		public void NeverUpscales() {
			var layout = WrappedPreviewLayout.Compute(5000, compact: false, CellW, CellH, frameCount: 2);
			Assert.Equal(CellW, layout.FrameWidth, precision: 5);
			Assert.Equal(CellH, layout.FrameHeight, precision: 5);
		}

		[Fact]
		public void TotalHeight_IsCapped() {
			// 30 frames stacked in a narrow column would be ~1738px tall at natural
			// size; the cap scales them down instead.
			var comfortable = WrappedPreviewLayout.Compute(152, compact: false, CellW, CellH, frameCount: 30);
			Assert.True(comfortable.TotalHeight <= WrappedPreviewLayout.MaxTotalHeightComfortable + 0.001);
			var compact = WrappedPreviewLayout.Compute(152, compact: true, CellW, CellH, frameCount: 30);
			Assert.True(compact.TotalHeight <= WrappedPreviewLayout.MaxTotalHeightCompact + 0.001);
			Assert.True(compact.TotalHeight < comfortable.TotalHeight);
		}

		[Fact]
		public void TotalWidth_NeverExceedsAvailableWidth() {
			foreach (double width in new[] { 56.0, 152, 210, 320, 480, 990, 1600 })
				for (int n = 1; n <= 12; n++) {
					var layout = WrappedPreviewLayout.Compute(width, compact: false, CellW, CellH, n);
					Assert.True(layout.TotalWidth <= width + 0.001, $"width={width} n={n}: {layout.TotalWidth}");
					Assert.True(layout.FramesPerRow * layout.Rows >= n, $"width={width} n={n}: grid too small");
				}
		}

		// Widening the column collapses the wrap — the #847 remedy must be "drag the
		// column wider", with no setting involved.
		[Fact]
		public void WideningColumn_ReducesRows() {
			int rowsNarrow = WrappedPreviewLayout.Compute(152, compact: false, CellW, CellH, 5).Rows;
			int rowsMid = WrappedPreviewLayout.Compute(320, compact: false, CellW, CellH, 5).Rows;
			int rowsWide = WrappedPreviewLayout.Compute(600, compact: false, CellW, CellH, 5).Rows;
			Assert.True(rowsMid < rowsNarrow);
			Assert.Equal(1, rowsWide);
		}

		[Fact]
		public void SingleFrame_FillsColumnDownOnly() {
			var layout = WrappedPreviewLayout.Compute(80, compact: false, CellW, CellH, 1);
			Assert.Equal(1, layout.FramesPerRow);
			Assert.Equal(1, layout.Rows);
			Assert.Equal(80, layout.FrameWidth, precision: 5);
		}

		[Fact]
		public void DegenerateInputs_AreSafe() {
			var layout = WrappedPreviewLayout.Compute(200, compact: false, 0, 0, 5);
			Assert.Equal(0, layout.FrameWidth);
			layout = WrappedPreviewLayout.Compute(double.NaN, compact: false, CellW, CellH, 0);
			Assert.Equal(1, layout.Rows);
			layout = WrappedPreviewLayout.Compute(double.PositiveInfinity, compact: false, CellW, CellH, 3);
			Assert.Equal(3, layout.FramesPerRow);
			Assert.Equal(CellW, layout.FrameWidth, precision: 5);
		}

		// Row sizing consumes the same math: the row must reserve exactly the wrap height.
		[Fact]
		public void ImageHeight_MultiFrame_MatchesWrapHeight() {
			// Composite stored as 3 columns × 2 rows (5 frames): 300×112.
			double h = ResultsRowSizing.ImageHeight(480, compact: false, thumbWidth: 300, thumbHeight: 112, frameCount: 5, gridColumns: 3);
			var layout = WrappedPreviewLayout.Compute(480 - 8, compact: false, CellW, CellH, 5);
			Assert.Equal(layout.TotalHeight, h, precision: 5);
		}

		[Fact]
		public void ImageHeight_MultiFrame_SingleLineWhenColumnIsWide() {
			// Wide column: one line of 56px-tall frames — the row shrinks accordingly (#847).
			double h = ResultsRowSizing.ImageHeight(600, compact: false, thumbWidth: 300, thumbHeight: 112, frameCount: 5, gridColumns: 3);
			Assert.Equal(56, h, precision: 5);
		}

		[Fact]
		public void ImageHeight_WithoutGridInfo_KeepsLegacyBehavior() {
			// Older saved results carry no frame info (0/0): the composite renders as
			// one image and the pre-wrap sizing stays byte-identical.
			Assert.Equal(
				ResultsRowSizing.ImageHeight(480, compact: false, thumbWidth: 300, thumbHeight: 112),
				ResultsRowSizing.ImageHeight(480, compact: false, thumbWidth: 300, thumbHeight: 112, frameCount: 0, gridColumns: 0));
		}

		[Fact]
		public void RowHeight_MultiFrame_NeverDropsBelowTextBaseline() {
			Assert.Equal(68, ResultsRowSizing.RowHeight(600, compact: false, previewVisible: true, thumbWidth: 300, thumbHeight: 112, frameCount: 5, gridColumns: 3));
		}

		[Fact]
		public void ThumbnailFrameAspect_ParsesFrameSize() {
			Assert.Equal(16.0 / 9, DuplicateItemVM.ThumbnailFrameAspect(new VDF.Core.ViewModels.DuplicateItem { FrameSize = "1920x1080" }), precision: 5);
			Assert.Equal(0.5625, DuplicateItemVM.ThumbnailFrameAspect(new VDF.Core.ViewModels.DuplicateItem { FrameSize = "1080x1920" }), precision: 5);
		}

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("garbage")]
		[InlineData("0x0")]
		[InlineData("x1080")]
		public void ThumbnailFrameAspect_FallsBackTo16By9(string? frameSize) {
			Assert.Equal(16.0 / 9, DuplicateItemVM.ThumbnailFrameAspect(new VDF.Core.ViewModels.DuplicateItem { FrameSize = frameSize }), precision: 5);
		}
	}
}
