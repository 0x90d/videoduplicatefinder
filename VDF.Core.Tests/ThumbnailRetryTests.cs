// /*
//     Copyright (C) 2025 0x90d
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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests;

// Pins the retry-eligibility filter behind RetrieveThumbnailsForItems / RetrieveThumbnails.
// Regression guard for #748: after #739's fix added a NoThumbnailImage placeholder to items
// whose every sample position failed, the retry filter (which only checked Count == 0)
// silently skipped those items, so "Load thumbnails for group" no-oped on the very rows
// the user was trying to recover. ShouldRetryThumbnails must treat a placeholder-only item
// as eligible for retry.
public class ThumbnailRetryTests {
	static Image NewPlaceholder() => new Image<Rgba32>(1, 1);
	static Image NewRealThumb() => new Image<Rgba32>(2, 2);

	[Fact]
	public void EmptyImageList_IsEligibleForRetry() {
		var item = new DuplicateItem();
		var placeholder = NewPlaceholder();

		Assert.Empty(item.ImageList);
		Assert.True(ScanEngine.ShouldRetryThumbnails(item, placeholder));
	}

	[Fact]
	public void PlaceholderOnly_IsEligibleForRetry() {
		var item = new DuplicateItem();
		var placeholder = NewPlaceholder();
		item.SetThumbnails(new List<Image> { placeholder }, new List<TimeSpan> { TimeSpan.Zero });

		Assert.True(ScanEngine.ShouldRetryThumbnails(item, placeholder),
			"#748 regression: placeholder-only items must remain eligible for explicit retry.");
	}

	[Fact]
	public void RealSingleThumbnail_IsNotRetried() {
		var item = new DuplicateItem();
		var placeholder = NewPlaceholder();
		var real = NewRealThumb();
		item.SetThumbnails(new List<Image> { real }, new List<TimeSpan> { TimeSpan.Zero });

		Assert.False(ScanEngine.ShouldRetryThumbnails(item, placeholder));
	}

	[Fact]
	public void MultipleRealThumbnails_AreNotRetried() {
		var item = new DuplicateItem();
		var placeholder = NewPlaceholder();
		item.SetThumbnails(
			new List<Image> { NewRealThumb(), NewRealThumb(), NewRealThumb() },
			new List<TimeSpan> { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) });

		Assert.False(ScanEngine.ShouldRetryThumbnails(item, placeholder));
	}

	[Fact]
	public void RealThumbnailEqualToPlaceholderByValue_StillSkipped() {
		// The check is reference equality (the singleton NoThumbnailImage instance), so a
		// thumbnail that *happens* to encode the same pixels as the placeholder is not a
		// retry candidate.
		var item = new DuplicateItem();
		var placeholder = NewPlaceholder();
		var lookalike = NewPlaceholder(); // same pixels, different instance
		item.SetThumbnails(new List<Image> { lookalike }, new List<TimeSpan> { TimeSpan.Zero });

		Assert.False(ScanEngine.ShouldRetryThumbnails(item, placeholder));
	}

	[Fact]
	public void NullPlaceholder_OnlyEmptyListsRetried() {
		var withImages = new DuplicateItem();
		withImages.SetThumbnails(new List<Image> { NewRealThumb() }, new List<TimeSpan> { TimeSpan.Zero });

		var empty = new DuplicateItem();

		Assert.False(ScanEngine.ShouldRetryThumbnails(withImages, placeholder: null));
		Assert.True(ScanEngine.ShouldRetryThumbnails(empty, placeholder: null));
	}

	[Fact]
	public void PlaceholderAlongsideRealThumbs_IsNotRetried() {
		// Mixed list (count > 1) means at least one real thumb succeeded — not a candidate
		// for retry even if one of the entries happens to be the placeholder reference.
		var item = new DuplicateItem();
		var placeholder = NewPlaceholder();
		item.SetThumbnails(
			new List<Image> { placeholder, NewRealThumb() },
			new List<TimeSpan> { TimeSpan.Zero, TimeSpan.FromSeconds(1) });

		Assert.False(ScanEngine.ShouldRetryThumbnails(item, placeholder));
	}
}
