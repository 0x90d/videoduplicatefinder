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

using System.Globalization;
using VDF.Core.ViewModels;
using VDF.GUI.Mvvm;
using VDF.GUI.Utils;

namespace VDF.GUI.Tests;

// #862: rows were sized by a flat estimate until the composite bitmap loaded, then
// snapped to the real aspect - thumbnails resolving asynchronously above the viewport
// shifted the scroll offset and the whole list bounced under the user. The prediction
// reserves the final size up front; these tests pin it to the numbers the real
// extraction/composition pipeline produces.
public class ThumbnailSizePredictionTests {

	[Fact]
	public void Video_PredictsDownscaledFramesInTheChosenGrid() {
		var item = new DuplicateItem { FrameSize = "1920x1080" };

		var p = ThumbnailSizePrediction.For(item, 0, 0, configuredThumbnailCount: 4, thumbnailMaxWidth: 100);

		Assert.NotNull(p);
		// Extractor fit (ScaleToMaxWidth): 1920x1080 into a 100px box -> 100x56.
		// Grid choice: the same ThumbnailGridLayout call the thumbnail loader makes.
		int columns = ThumbnailGridLayout.Columns(4, 1920.0 / 1080);
		int rows = ThumbnailGridLayout.Rows(4, columns);
		Assert.Equal(4, p!.FrameCount);
		Assert.Equal(columns, p.GridColumns);
		Assert.Equal(columns * 100, p.Width);
		Assert.Equal(rows * 56, p.Height);
	}

	[Fact]
	public void SmallImage_KeepsItsNaturalSize_AndAlwaysHasOneFrame() {
		// DownOnly (#787): sources below the max width are never upscaled, and images
		// get a single frame regardless of the configured video thumbnail count.
		var item = new DuplicateItem { FrameSize = "80x60", IsImage = true };

		var p = ThumbnailSizePrediction.For(item, 0, 0, configuredThumbnailCount: 4, thumbnailMaxWidth: 100);

		Assert.NotNull(p);
		Assert.Equal(1, p!.FrameCount);
		Assert.Equal(1, p.GridColumns);
		Assert.Equal(80, p.Width);
		Assert.Equal(60, p.Height);
	}

	[Fact]
	public void PersistedCompositeGeometry_TakesPrecedenceOverTheConfiguredCount() {
		// Restored results carry the composite's actual frame count/columns; the current
		// settings must not override what is actually stored in the thumbnail pack.
		var item = new DuplicateItem { FrameSize = "1920x1080" };

		var p = ThumbnailSizePrediction.For(item, knownFrameCount: 6, knownGridColumns: 3,
			configuredThumbnailCount: 2, thumbnailMaxWidth: 100);

		Assert.NotNull(p);
		Assert.Equal(6, p!.FrameCount);
		Assert.Equal(3, p.GridColumns);
		Assert.Equal(300, p.Width);
		Assert.Equal(2 * 56, p.Height); // 6 frames in 3 columns = 2 rows
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("x")]
	[InlineData("0x0")]
	[InlineData("-1x100")]
	public void UnknownOrBrokenFrameSize_YieldsNoPrediction(string? frameSize) {
		var item = new DuplicateItem { FrameSize = frameSize };
		Assert.Null(ThumbnailSizePrediction.For(item, 0, 0, 4, 100));
	}

	[Fact]
	public void RowHeight_DoesNotChangeWhenThePredictedCompositeArrives() {
		// The invariant behind #862: the row height computed from the prediction BEFORE
		// the bitmap loads equals the height computed from the loaded composite's
		// dimensions, so the arrival of the thumbnail cannot shift the list.
		var item = new DuplicateItem { FrameSize = "1920x1080" };
		var p = ThumbnailSizePrediction.For(item, 0, 0, 4, 100)!;

		double before = ResultsRowSizing.RowHeight(320, false, true, p.Width, p.Height, p.FrameCount, p.GridColumns);
		// What the loader will actually compose: 100x56 frames in the same grid.
		int rows = ThumbnailGridLayout.Rows(4, p.GridColumns);
		double after = ResultsRowSizing.RowHeight(320, false, true, p.GridColumns * 100, rows * 56, 4, p.GridColumns);

		Assert.Equal(after, before);
	}

	[Fact]
	public void Converter_UsesThePredictionWhileTheBitmapIsMissing() {
		var conv = new ResultsRowSizingConverter();
		var item = new DuplicateItem { FrameSize = "640x480" };
		var p = ThumbnailSizePrediction.For(item, 0, 0, 1, 100)!;

		object? height = conv.Convert(
			new List<object?> { 320d, false, true, null, 0, 0, p },
			typeof(double), "row", CultureInfo.InvariantCulture);

		double expected = ResultsRowSizing.RowHeight(320, false, true, p.Width, p.Height, p.FrameCount, p.GridColumns);
		Assert.Equal(expected, Assert.IsType<double>(height));
	}

	[Fact]
	public void Converter_WithoutPrediction_KeepsTheClassicEstimate() {
		var conv = new ResultsRowSizingConverter();

		object? height = conv.Convert(
			new List<object?> { 320d, false, true, null, 0, 0, null },
			typeof(double), "row", CultureInfo.InvariantCulture);

		Assert.Equal(ResultsRowSizing.RowHeight(320, false, true), Assert.IsType<double>(height));
	}
}
