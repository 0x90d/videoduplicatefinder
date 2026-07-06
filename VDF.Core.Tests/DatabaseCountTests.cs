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

// The database is a shared static; unique GUID-based folder prefixes keep these
// assertions independent and Dispose removes only what this class added — but the
// HashSet itself is not thread-safe, so serialize with the other DB-touching classes.
[Collection("DatabaseUtils")]
public class DatabaseCountTests : IDisposable {
	readonly string root = @"C:\vdf-count-" + Guid.NewGuid().ToString("N");
	readonly List<FileEntry> added = new();

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
	}

	FileEntry Add(string relativePath) {
		var entry = new FileEntry {
			_Path = Path.Combine(root, relativePath),
			Folder = Path.GetDirectoryName(Path.Combine(root, relativePath))!,
			FileSize = 1,
		};
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	[Fact]
	public void CountsOnlyEntriesUnderTheFolder() {
		Add("a.mp4");
		Add(@"sub\b.mp4");
		var sibling = new FileEntry {
			// Sibling folder that merely starts with the same text must not match.
			_Path = root + @"X\d.mp4",
			Folder = root + "X",
			FileSize = 1,
		};
		DatabaseUtils.Database.Add(sibling);
		added.Add(sibling);

		Assert.Equal(2, ScanEngine.CountDatabaseEntriesUnder(root));
		Assert.Equal(2, ScanEngine.CountDatabaseEntriesUnder(root + @"\"));
		Assert.Equal(1, ScanEngine.CountDatabaseEntriesUnder(root + "X"));
	}

	[Fact]
	public void EmptyOrUnknownFolder_CountsZero() {
		Add("a.mp4");

		Assert.Equal(0, ScanEngine.CountDatabaseEntriesUnder(@"D:\vdf-count-elsewhere-" + Guid.NewGuid().ToString("N")));
		Assert.Equal(0, ScanEngine.CountDatabaseEntriesUnder(""));
	}
}
