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

/// <summary>
/// The scan-inclusion gate for missing files: excluded by default, included when the user
/// opted into IncludeNonExistingFiles OR when RememberDeletedContent needs tombstones in the
/// comparison. This is the gate that makes the tombstone feature work at all without forcing
/// the IncludeNonExistingFiles setting, so pin all configurations.
/// </summary>
public class MissingFileInclusionTests {
	const string MissingPath = @"C:\__vdf_gate_test__\gone.mp4";

	static ScanEngine MakeEngine() {
		var engine = new ScanEngine();
		engine.Settings.IncludeList.Add(@"C:\__vdf_gate_test__");
		return engine;
	}

	static FileEntry MissingEntry() => new() {
		_Path = MissingPath,
		Folder = @"C:\__vdf_gate_test__",
		FileSize = 1000,
	};

	[Theory]
	[InlineData(false, false, true)]  // default: missing files are excluded
	[InlineData(true, false, false)]  // user's explicit include-non-existing wins
	[InlineData(false, true, false)]  // tombstones participate without flipping the user's setting
	[InlineData(true, true, false)]
	public void MissingFile_Inclusion_FollowsEitherSwitch(bool includeNonExisting, bool rememberDeleted, bool expectExcluded) {
		var engine = MakeEngine();
		engine.Settings.IncludeNonExistingFiles = includeNonExisting;
		engine.Settings.RememberDeletedContent = rememberDeleted;

		bool invalid = engine.InvalidEntry(MissingEntry(), out _, out string? reason);

		Assert.Equal(expectExcluded, invalid);
		if (expectExcluded)
			Assert.Equal("file does not exist", reason);
	}

	[Fact]
	public void ExistingFile_IsValid_RegardlessOfSwitches() {
		var engine = new ScanEngine();
		string self = typeof(MissingFileInclusionTests).Assembly.Location;
		// .dll isn't a media extension, but InvalidEntry doesn't check extensions — build the
		// entry by hand so only the gates under test decide.
		var entry = new FileEntry {
			_Path = self,
			Folder = Path.GetDirectoryName(self)!,
			FileSize = 1000,
		};
		engine.Settings.IncludeList.Add(entry.Folder);

		Assert.False(engine.InvalidEntry(entry, out _, out _));
	}
}
