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
/// ScanEngine.RefreshExistingEntry: a rescan of a path already in the database keeps
/// the cached analysis when only the timestamps moved AND the content hash proves the
/// bytes unchanged; any size change or unverifiable hash re-analyzes as before.
/// </summary>
[Collection("DatabaseUtils")] // DatabaseUtils is static — serialize with other classes touching it
public class ModifiedDetectionTests : IDisposable {
	readonly string _dir;
	readonly string _file;

	public ModifiedDetectionTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"vdf-moddetect-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
		_file = Path.Combine(_dir, "video.mp4");
		var content = new byte[200_000];
		for (int i = 0; i < content.Length; i++)
			content[i] = (byte)(i * 31 & 0xFF);
		File.WriteAllBytes(_file, content);
		DatabaseUtils.Database.Clear();
	}

	public void Dispose() {
		DatabaseUtils.Database.Clear();
		try { Directory.Delete(_dir, recursive: true); } catch { }
	}

	// The DB entry as a previous scan would have left it: same path, analysis attached,
	// timestamps that differ from the file's current ones (someone touched/restored it).
	FileEntry MakeAnalyzedDbEntry(FileEntry current, string? osHash) {
		var dbEntry = new FileEntry {
			_Path = current.Path,
			Folder = current.Folder,
			FileSize = current.FileSize,
			DateCreated = current.DateCreated.AddHours(-1),
			DateModified = current.DateModified.AddHours(-1),
			OsHash = osHash,
		};
		dbEntry.grayBytes[0] = new byte[] { 1, 2, 3 }; // marker: cached analysis present
		DatabaseUtils.Database.Add(dbEntry);
		return dbEntry;
	}

	FileEntry EntryInDb(FileEntry key) {
		Assert.True(DatabaseUtils.Database.TryGetValue(key, out var entry));
		return entry!;
	}

	[Fact]
	public void TimestampOnlyChange_VerifiedSameContent_KeepsAnalysis() {
		var fEntry = new FileEntry(_file);
		var dbEntry = MakeAnalyzedDbEntry(fEntry, OsHashUtils.TryCompute(_file));

		ScanEngine.RefreshExistingEntry(fEntry, dbEntry);

		var kept = EntryInDb(fEntry);
		Assert.Same(dbEntry, kept);
		Assert.NotEmpty(kept.grayBytes);
		// Timestamps refreshed so the next scan doesn't re-verify.
		Assert.Equal(fEntry.DateCreated, kept.DateCreated);
		Assert.Equal(fEntry.DateModified, kept.DateModified);
	}

	[Fact]
	public void TimestampOnlyChange_DifferentContent_Reanalyzes() {
		var fEntry = new FileEntry(_file);
		// Stored hash belongs to different bytes -> a genuine same-size content swap.
		var dbEntry = MakeAnalyzedDbEntry(fEntry, "0123456789abcdef");

		ScanEngine.RefreshExistingEntry(fEntry, dbEntry);

		var replaced = EntryInDb(fEntry);
		Assert.Same(fEntry, replaced);
		Assert.Empty(replaced.grayBytes);
	}

	[Fact]
	public void TimestampOnlyChange_UnverifiableHash_Reanalyzes() {
		var fEntry = new FileEntry(_file);
		// Pre-OsHash entry (not yet backfilled): no proof of identity -> conservative re-analyze.
		var dbEntry = MakeAnalyzedDbEntry(fEntry, osHash: null);

		ScanEngine.RefreshExistingEntry(fEntry, dbEntry);

		Assert.Same(fEntry, EntryInDb(fEntry));
	}

	[Fact]
	public void SizeChange_Reanalyzes_EvenWithMatchingDates() {
		var fEntry = new FileEntry(_file);
		var dbEntry = new FileEntry {
			_Path = fEntry.Path,
			Folder = fEntry.Folder,
			FileSize = fEntry.FileSize + 1,
			DateCreated = fEntry.DateCreated,
			DateModified = fEntry.DateModified,
			OsHash = OsHashUtils.TryCompute(_file),
		};
		dbEntry.grayBytes[0] = new byte[] { 1, 2, 3 };
		DatabaseUtils.Database.Add(dbEntry);

		ScanEngine.RefreshExistingEntry(fEntry, dbEntry);

		Assert.Same(fEntry, EntryInDb(fEntry));
	}

	[Fact]
	public void NoChange_LeavesEntryUntouched() {
		var fEntry = new FileEntry(_file);
		var dbEntry = new FileEntry {
			_Path = fEntry.Path,
			Folder = fEntry.Folder,
			FileSize = fEntry.FileSize,
			DateCreated = fEntry.DateCreated,
			DateModified = fEntry.DateModified,
		};
		dbEntry.grayBytes[0] = new byte[] { 1, 2, 3 };
		DatabaseUtils.Database.Add(dbEntry);

		ScanEngine.RefreshExistingEntry(fEntry, dbEntry);

		Assert.Same(dbEntry, EntryInDb(fEntry));
		Assert.NotEmpty(EntryInDb(fEntry).grayBytes);
	}
}
