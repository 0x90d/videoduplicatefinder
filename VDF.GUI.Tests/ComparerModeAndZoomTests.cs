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

using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests;

// The comparer's dual modes (Swipe/SideBySide/Stacked) must depend on which items are
// SELECTED, not on whether their bitmaps have finished loading — keying off the loaded
// images knocked the mode dropdown back to Single whenever the user picked a mode while
// thumbnails were still loading asynchronously.
public class ComparerModeTests {
	[Theory]
	[InlineData(CompareMode.Single, false, false, false)]
	[InlineData(CompareMode.Single, true, true, false)]
	[InlineData(CompareMode.Swipe, true, true, false)]
	[InlineData(CompareMode.SideBySide, true, true, false)]
	[InlineData(CompareMode.Stacked, true, true, false)]
	[InlineData(CompareMode.Swipe, true, false, true)]
	[InlineData(CompareMode.Swipe, false, false, true)]
	[InlineData(CompareMode.SideBySide, false, true, true)]
	[InlineData(CompareMode.Stacked, false, true, true)]
	public void DualModesDependOnSelections_NotBitmapLoadTiming(CompareMode mode, bool hasA, bool hasB, bool expectForceSingle) =>
		Assert.Equal(expectForceSingle, ThumbnailComparerVM.ShouldForceSingleView(mode, hasA, hasB));
}

// The diff overlay is only worth computing while the highlight toggle is on and both
// frames are on screen; every other state must clear it instead of burning a
// background task.
public class ComparerDiffGateTests {
	[Theory]
	[InlineData(true, true, true, true)]
	[InlineData(true, false, true, false)]
	[InlineData(true, true, false, false)]
	[InlineData(false, true, true, false)]
	[InlineData(false, false, false, false)]
	public void DiffComputesOnlyWhenToggledOnWithBothImages(bool highlightOn, bool hasA, bool hasB, bool expected) =>
		Assert.Equal(expected, ThumbnailComparerVM.ShouldComputeDiff(highlightOn, hasA, hasB));
}

// Pan/zoom offsets are clamped so content can never be pushed fully out of the viewport,
// and content smaller than the viewport is centered instead of pannable.
public class ZoomPanClampTests {
	[Fact]
	public void ContentSmallerThanViewport_IsCentered() {
		// 100px content at zoom 1 in a 400px viewport → centered at 150 regardless of input.
		Assert.Equal(150d, ZoomPanPresenter.ClampOffsetToViewport(-500d, 400d, 100d, 1d));
		Assert.Equal(150d, ZoomPanPresenter.ClampOffsetToViewport(500d, 400d, 100d, 1d));
	}

	[Fact]
	public void ContentLargerThanViewport_ClampsToEdges() {
		// 400px content at zoom 2 (=800px) in a 400px viewport: offset must stay in [-400, 0].
		Assert.Equal(0d, ZoomPanPresenter.ClampOffsetToViewport(50d, 400d, 400d, 2d));
		Assert.Equal(-400d, ZoomPanPresenter.ClampOffsetToViewport(-450d, 400d, 400d, 2d));
		Assert.Equal(-123d, ZoomPanPresenter.ClampOffsetToViewport(-123d, 400d, 400d, 2d));
	}

	[Fact]
	public void ExactViewportFit_LocksToOrigin() {
		Assert.Equal(0d, ZoomPanPresenter.ClampOffsetToViewport(37d, 400d, 400d, 1d));
	}

	[Theory]
	[InlineData(0d, 400d)]   // no viewport yet (pre-layout)
	[InlineData(400d, 0d)]   // no content yet
	public void MissingMeasurements_LeaveOffsetUntouched(double viewport, double content) {
		Assert.Equal(42d, ZoomPanPresenter.ClampOffsetToViewport(42d, viewport, content, 1d));
	}

	[Fact]
	public void NonFiniteInputs_LeaveOffsetUntouched() {
		Assert.Equal(double.NaN, ZoomPanPresenter.ClampOffsetToViewport(double.NaN, 400d, 400d, 1d));
		Assert.Equal(42d, ZoomPanPresenter.ClampOffsetToViewport(42d, 400d, 400d, double.PositiveInfinity));
	}
}
