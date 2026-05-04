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

namespace VDF.Core.Tests;

public class EntryFlagTests {
	[Fact]
	public void Any_SingleFlagSet_ReturnsTrue() {
		var flags = EntryFlags.IsImage;
		Assert.True(flags.Any(EntryFlags.IsImage));
	}

	[Fact]
	public void Any_NoFlagsSet_ReturnsFalse() {
		var flags = (EntryFlags)0;
		Assert.False(flags.Any(EntryFlags.IsImage));
	}

	[Fact]
	public void Any_DifferentFlagSet_ReturnsFalse() {
		var flags = EntryFlags.IsImage;
		Assert.False(flags.Any(EntryFlags.ManuallyExcluded));
	}

	[Fact]
	public void Any_MultipleFlags_OneMatches_ReturnsTrue() {
		var flags = EntryFlags.IsImage | EntryFlags.ThumbnailError;
		Assert.True(flags.Any(EntryFlags.ThumbnailError | EntryFlags.MetadataError));
	}

	[Fact]
	public void Has_AllFlagsPresent_ReturnsTrue() {
		var flags = EntryFlags.IsImage | EntryFlags.ThumbnailError;
		Assert.True(flags.Has(EntryFlags.IsImage | EntryFlags.ThumbnailError));
	}

	[Fact]
	public void Has_PartialFlags_ReturnsFalse() {
		var flags = EntryFlags.IsImage;
		Assert.False(flags.Has(EntryFlags.IsImage | EntryFlags.ThumbnailError));
	}

	[Fact]
	public void Set_AddsFlag() {
		var flags = EntryFlags.IsImage;
		flags.Set(EntryFlags.ThumbnailError);
		Assert.True(flags.Has(EntryFlags.IsImage | EntryFlags.ThumbnailError));
	}

	[Fact]
	public void Set_WithTrue_SetsFlag() {
		var flags = (EntryFlags)0;
		flags.Set(EntryFlags.IsImage, true);
		Assert.True(flags.Has(EntryFlags.IsImage));
	}

	[Fact]
	public void Set_WithFalse_ClearsFlag() {
		var flags = EntryFlags.IsImage | EntryFlags.ThumbnailError;
		flags.Set(EntryFlags.ThumbnailError, false);
		Assert.True(flags.Has(EntryFlags.IsImage));
		Assert.False(flags.Any(EntryFlags.ThumbnailError));
	}

	[Fact]
	public void AllErrors_ContainsExpectedFlags() {
		Assert.True(EntryFlags.AllErrors.Has(EntryFlags.ThumbnailError));
		Assert.True(EntryFlags.AllErrors.Has(EntryFlags.MetadataError));
		Assert.True(EntryFlags.AllErrors.Has(EntryFlags.TooDark));
		Assert.False(EntryFlags.AllErrors.Any(EntryFlags.IsImage));
	}

	[Fact]
	public void SilentAudioTrack_HasDistinctValue() {
		// Regression for issue #719: SilentAudioTrack must be a unique bit so it
		// doesn't collide with NoAudioTrack / AudioFingerprintError in the stored DB.
		Assert.NotEqual(EntryFlags.SilentAudioTrack, EntryFlags.NoAudioTrack);
		Assert.NotEqual(EntryFlags.SilentAudioTrack, EntryFlags.AudioFingerprintError);
		var combined = EntryFlags.NoAudioTrack | EntryFlags.SilentAudioTrack;
		Assert.True(combined.Has(EntryFlags.NoAudioTrack));
		Assert.True(combined.Has(EntryFlags.SilentAudioTrack));
	}
}
