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

namespace VDF.Core.Tests;

// The drive-presence heuristic is what tells an intentional delete (keep the fingerprint as a
// tombstone) apart from a temporarily unmounted drive (leave it alone). Getting it wrong would
// mass-mislabel an unplugged USB as deleted, so pin the three cases down.
public class TombstoneHelpersTests {
	[Fact]
	public void MountedDrive_MissingFile_IsTombstone() {
		if (!OperatingSystem.IsWindows())
			return; // drive-letter semantics; PR CI runs Windows only
		// C:\ is always mounted in the test environment; the file underneath does not exist.
		string missing = @"C:\__vdf_no_such_dir__\__nope__.mp4";
		Assert.True(ScanEngine.IsDriveReady(missing));
		Assert.True(ScanEngine.PathIsTombstone(missing));
		Assert.False(ScanEngine.PathIsOffline(missing));
	}

	[Fact]
	public void UnreachableUncRoot_IsOffline_NeverTombstone() {
		// An unresolvable UNC root can't be probed -> conservative "offline", never a tombstone.
		string unc = @"\\vdf-no-such-server\share\x.mp4";
		Assert.False(ScanEngine.IsDriveReady(unc));
		Assert.True(ScanEngine.PathIsOffline(unc));
		Assert.False(ScanEngine.PathIsTombstone(unc));
	}

	[Fact]
	public void ExistingFile_IsNeitherTombstoneNorOffline() {
		string self = typeof(TombstoneHelpersTests).Assembly.Location;
		Assert.True(File.Exists(self));
		Assert.False(ScanEngine.PathIsTombstone(self));
		Assert.False(ScanEngine.PathIsOffline(self));
	}
}
