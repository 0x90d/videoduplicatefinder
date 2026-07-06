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
/// Cleanup semantics around RememberDeletedContent: with the feature OFF, cleanup behaves
/// exactly as before (every missing/error entry removed); with it ON, tombstones (missing
/// file + comparable data on a mounted drive) and offline-drive entries survive while
/// error-flagged entries and data-less ghosts still go. PruneGhostEntries removes only
/// ghosts regardless of the setting.
/// </summary>
[Collection("DatabaseUtils")] // DatabaseUtils is static — serialize with other classes touching it
public class TombstoneCleanupTests : IDisposable {
	readonly string _dir;
	readonly string _existingFile;

	public TombstoneCleanupTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"vdf-tombstone-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
		_existingFile = Path.Combine(_dir, "existing.mp4");
		File.WriteAllBytes(_existingFile, new byte[] { 1, 2, 3 });
		DatabaseUtils.CustomDatabaseFolder = _dir;
		DatabaseUtils.InvalidateDatabaseFolder();
		DatabaseUtils.Database.Clear();
	}

	public void Dispose() {
		DatabaseUtils.CustomDatabaseFolder = null;
		DatabaseUtils.InvalidateDatabaseFolder();
		DatabaseUtils.Database.Clear();
		try { Directory.Delete(_dir, recursive: true); } catch { }
	}

	static FileEntry MakeEntry(string path, EntryFlags flags = 0, bool withGrayBytes = false, uint[]? fingerprint = null) {
		var e = new FileEntry {
			_Path = path,
			Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty,
			FileSize = 1000,
			Flags = flags,
			AudioFingerprint = fingerprint,
		};
		if (withGrayBytes)
			e.grayBytes[0] = new byte[] { 1, 2, 3 };
		return e;
	}

	bool InDb(string path) => DatabaseUtils.Database.Any(e => e.Path == path);

	const string MissingTombstone = @"C:\__vdf_ts_test__\tombstone.mp4"; // mounted drive, file gone, has data
	const string MissingGhost = @"C:\__vdf_ts_test__\ghost.mp4";         // mounted drive, file gone, no data
	const string OfflineNoData = @"\\vdf-no-such-server\share\off.mp4";  // unreachable root, no data

	void SeedDatabase() {
		DatabaseUtils.Database.Add(MakeEntry(_existingFile, withGrayBytes: true));
		DatabaseUtils.Database.Add(MakeEntry(MissingTombstone, withGrayBytes: true));
		DatabaseUtils.Database.Add(MakeEntry(MissingGhost));
		DatabaseUtils.Database.Add(MakeEntry(OfflineNoData));
		DatabaseUtils.Database.Add(MakeEntry(_existingFile + ".err", EntryFlags.ThumbnailError, withGrayBytes: true));
	}

	[Fact]
	public void Cleanup_FeatureOff_RemovesEveryMissingOrErrorEntry() {
		if (!OperatingSystem.IsWindows()) return; // drive-letter semantics
		SeedDatabase();

		DatabaseUtils.CleanupDatabase(preserveDeletedContentMemory: false);

		Assert.True(InDb(_existingFile));
		Assert.False(InDb(MissingTombstone)); // pre-feature behavior: missing = gone
		Assert.False(InDb(MissingGhost));
		Assert.False(InDb(OfflineNoData));
		Assert.False(InDb(_existingFile + ".err"));
	}

	[Fact]
	public void Cleanup_FeatureOn_KeepsTombstonesAndOffline_RemovesGhostsAndErrors() {
		if (!OperatingSystem.IsWindows()) return;
		SeedDatabase();

		DatabaseUtils.CleanupDatabase(preserveDeletedContentMemory: true);

		Assert.True(InDb(_existingFile));
		Assert.True(InDb(MissingTombstone));  // re-download memory survives cleanup
		Assert.True(InDb(OfflineNoData));     // unplugged drive is not a deletion
		Assert.False(InDb(MissingGhost));     // no data -> can never match -> dead weight
		Assert.False(InDb(_existingFile + ".err"));
	}

	[Fact]
	public void PruneGhostEntries_RemovesOnlyGhosts() {
		if (!OperatingSystem.IsWindows()) return;
		SeedDatabase();
		// An empty (no-audio-track) fingerprint still counts as "has data" — conservative keep.
		string missingWithEmptyFp = @"C:\__vdf_ts_test__\emptyfp.mp4";
		DatabaseUtils.Database.Add(MakeEntry(missingWithEmptyFp, fingerprint: Array.Empty<uint>()));

		Assert.Equal(1, ScanEngine.CountGhostEntries());
		int pruned = ScanEngine.PruneGhostEntries();

		Assert.Equal(1, pruned);
		Assert.False(InDb(MissingGhost));
		Assert.True(InDb(MissingTombstone));
		Assert.True(InDb(OfflineNoData));
		Assert.True(InDb(missingWithEmptyFp));
		Assert.True(InDb(_existingFile));
		Assert.True(InDb(_existingFile + ".err")); // has frame data -> not a ghost; flags are cleanup's business, not prune's
	}
}
