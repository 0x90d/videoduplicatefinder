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
// #899: the results show the language of every audio and subtitle track.
using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // reads the shared static database
public class ResultLookupTests {
	/// <summary>
	/// A result whose file was moved or deleted while the compare ran (a long partial-clip pass
	/// on a network share) aborted the scan at its last step, the track-language backfill.
	/// </summary>
	[Fact]
	public void FindDatabaseEntry_ForAResultWhoseFileIsGone_DoesNotThrow() {
		string gone = Path.Combine(Path.GetTempPath(), $"vdf-gone-{Guid.NewGuid():N}.mp4");
		var entry = new FileEntry { _Path = gone, Folder = Path.GetDirectoryName(gone)! };
		DatabaseUtils.Database.Add(entry);
		try {
			Assert.Same(entry, ScanEngine.FindDatabaseEntry(gone));
			Assert.Null(ScanEngine.FindDatabaseEntry(gone + ".other"));
		}
		finally {
			DatabaseUtils.Database.Remove(entry);
		}
	}
}
