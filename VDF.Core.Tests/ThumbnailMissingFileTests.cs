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

using VDF.Core.ViewModels;

namespace VDF.Core.Tests;

/// <summary>
/// Thumbnail retrieval for result rows whose file is missing while missing files are part
/// of the scan (IncludeNonExistingFiles or RememberDeletedContent). The skip-missing branch
/// used to fall through into SetThumbnails with NULL timestamps: Debug builds died on a
/// Debug.Assert, Release builds stored a null ThumbnailTimestamps list that the Web UI and
/// the thumbnail comparer dereference. Missing rows must instead get the placeholder (or
/// keep the thumbnails they already have) with a valid timestamp list.
/// </summary>
public class ThumbnailMissingFileTests {
	static ScanEngine MakeEngine() {
		var engine = new ScanEngine();
		engine.Settings.RememberDeletedContent = true; // makes IncludeMissingFiles true
		engine.Settings.ThumbnailCount = 1;
		engine.NoThumbnailImage = new byte[] { 9, 9, 9 };
		return engine;
	}

	static DuplicateItem MissingItem() => new() {
		Path = @"C:\__vdf_thumb_test__\does-not-exist.mp4",
	};

	[Fact]
	public async Task MissingFile_GetsPlaceholderAndValidTimestamps() {
		var engine = MakeEngine();
		var item = MissingItem();

		await engine.RetrieveThumbnailsForItems(new[] { item });

		var image = Assert.Single(item.ImageList);
		Assert.Same(engine.NoThumbnailImage, image);
		Assert.NotNull(item.ThumbnailTimestamps); // Release-mode failure mode was null here
		Assert.Single(item.ThumbnailTimestamps);
	}

	[Fact]
	public async Task MissingFile_WithExistingThumbnails_KeepsThem() {
		// A width-upgrade retry can select an item that already has real thumbnails; if its
		// file is missing, the pass must keep them rather than downgrade to the placeholder.
		var engine = MakeEngine();
		engine.Settings.ThumbnailMaxWidth = 300;
		var item = MissingItem();
		var real = new byte[] { 1, 2, 3, 4 };
		item.SetThumbnails(new List<byte[]> { real }, new List<TimeSpan> { TimeSpan.FromSeconds(3) });
		item.ThumbnailWidth = 100; // below required width -> eligible for the retry pass

		await engine.RetrieveThumbnailsForItems(new[] { item });

		var image = Assert.Single(item.ImageList);
		Assert.Same(real, image);
		Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(item.ThumbnailTimestamps));
	}

	[Fact]
	public async Task MissingFile_PlaceholderOnlyItem_StaysPlaceholderWithoutNulls() {
		// Placeholder-only items stay retry-eligible (#748) and re-enter the pass every time;
		// a missing file must leave them stable instead of stacking or nulling anything.
		var engine = MakeEngine();
		var item = MissingItem();
		item.SetThumbnails(new List<byte[]> { engine.NoThumbnailImage! }, new List<TimeSpan> { TimeSpan.Zero });

		await engine.RetrieveThumbnailsForItems(new[] { item });

		Assert.Single(item.ImageList);
		Assert.NotNull(item.ThumbnailTimestamps);
		Assert.Single(item.ThumbnailTimestamps);
	}
}
