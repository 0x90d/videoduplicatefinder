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

using VDF.Core.Utils;

namespace VDF.Core.Tests;

/// <summary>
/// A MOVED file must be relinked to its existing analysis, never mistaken for deleted
/// content: after the relink the entry's path is the new location, so it can no longer
/// read as a tombstone ("already deleted") even with RememberDeletedContent on.
/// </summary>
[Collection("DatabaseUtils")] // DatabaseUtils is static — serialize with other classes touching it
public class TombstoneRelinkInteractionTests : IDisposable {
	readonly string _dir;

	public TombstoneRelinkInteractionTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"vdf-relink-ts-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
		DatabaseUtils.Database.Clear();
	}

	public void Dispose() {
		DatabaseUtils.Database.Clear();
		try { Directory.Delete(_dir, recursive: true); } catch { }
	}

	[Fact]
	public void MovedFile_IsRelinked_NotTombstoned() {
		// The "old" location: analyzed entry whose file has since been moved away.
		string oldPath = Path.Combine(_dir, "old-location.mp4");
		string newPath = Path.Combine(_dir, "new-location.mp4");
		var content = new byte[200_000];
		for (int i = 0; i < content.Length; i++)
			content[i] = (byte)(i * 17 & 0xFF);
		File.WriteAllBytes(newPath, content); // file exists only at the NEW path

		var dbEntry = new FileEntry {
			_Path = oldPath,
			Folder = _dir,
			FileSize = new FileInfo(newPath).Length,
			OsHash = OsHashUtils.TryCompute(newPath), // same content -> same hash
		};
		dbEntry.grayBytes[0] = new byte[] { 1, 2, 3 };
		DatabaseUtils.Database.Add(dbEntry);

		// Before the relink, the stale entry reads as a tombstone (file gone, drive mounted).
		if (OperatingSystem.IsWindows())
			Assert.True(ScanEngine.PathIsTombstone(oldPath));

		// BuildFileList encounters the file at its new path and relinks.
		var engine = new ScanEngine();
		var relinkBySize = new Dictionary<long, List<FileEntry>> {
			[dbEntry.FileSize] = new List<FileEntry> { dbEntry }
		};
		Assert.True(engine.TryRelinkMovedFile(new FileEntry(newPath), relinkBySize));

		// The entry now lives at the new path with its analysis intact — and is no tombstone.
		Assert.Equal(newPath, dbEntry.Path);
		Assert.NotEmpty(dbEntry.grayBytes);
		Assert.False(ScanEngine.PathIsTombstone(dbEntry.Path));
		Assert.False(ScanEngine.PathIsOffline(dbEntry.Path));
		// And no stale entry remains at the old path that could grow a badge.
		Assert.DoesNotContain(DatabaseUtils.Database, e => e.Path == oldPath);
	}
}
