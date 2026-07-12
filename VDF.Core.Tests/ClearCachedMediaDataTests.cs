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

using VDF.Core;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

public class ClearCachedMediaDataTests {

	static FileEntry Populated() {
		var entry = new FileEntry {
			Folder = @"D:\media",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromMinutes(2), Streams = Array.Empty<MediaInfo.StreamInfo>() },
			FileSize = 1234,
			DateCreated = new DateTime(2024, 1, 1),
			OsHash = "abc123",
			AudioFingerprint = new uint[] { 1, 2, 3 },
		};
		entry.Path = @"D:\media\clip.mp4";
		entry.grayBytes[0.25] = new byte[] { 1, 2 };
		entry.PHashes[0.25] = 42UL;
		entry.Flags.Set(EntryFlags.ThumbnailError | EntryFlags.MetadataError | EntryFlags.TooDark |
			EntryFlags.NoAudioTrack | EntryFlags.AudioFingerprintError | EntryFlags.SilentAudioTrack);
		return entry;
	}

	[Fact]
	public void ClearsDerivedDataAndErrorFlags() {
		var entry = Populated();
		entry.ClearCachedMediaData();

		Assert.Null(entry.mediaInfo);
		Assert.Empty(entry.grayBytes);
		Assert.Empty(entry.PHashes);
		Assert.Null(entry.AudioFingerprint);
		Assert.False(entry.HasThubmanilError);
		Assert.False(entry.HasMetadataError);
		Assert.False(entry.IsTooDark);
		Assert.False(entry.Flags.Any(EntryFlags.NoAudioTrack | EntryFlags.AudioFingerprintError | EntryFlags.SilentAudioTrack));
	}

	[Fact]
	public void KeepsIdentityDataAndManualFlags() {
		var entry = Populated();
		entry.Flags.Set(EntryFlags.ManuallyExcluded | EntryFlags.IsImage | EntryFlags.ReparsePoint | EntryFlags.ReparsePointChecked);
		entry.ClearCachedMediaData();

		Assert.Equal(@"D:\media\clip.mp4", entry.Path);
		Assert.Equal(1234, entry.FileSize);
		Assert.Equal("abc123", entry.OsHash);
		Assert.True(entry.IsManuallyExcluded);
		Assert.True(entry.Flags.Any(EntryFlags.IsImage));
		Assert.True(entry.Flags.Any(EntryFlags.ReparsePoint));
		Assert.True(entry.Flags.Any(EntryFlags.ReparsePointChecked));
	}
}
