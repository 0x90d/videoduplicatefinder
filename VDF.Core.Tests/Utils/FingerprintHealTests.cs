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

namespace VDF.Core.Tests.Utils;

/// <summary>
/// Heal-on-load for entries poisoned by the pre-fa902d3 Stop-cancellation bug
/// (AudioFingerprintError + empty fingerprint). The heal must run exactly once per
/// database: flagged entries get one retry, genuinely broken files re-flag on the
/// next scan and then stay flagged.
/// </summary>
[Collection("DatabaseUtils")] // DatabaseUtils is static — serialize with other classes touching it
public class FingerprintHealTests : IDisposable {
	readonly string _dir;

	public FingerprintHealTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"vdf-fpheal-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
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

	string MarkerPath => Path.Combine(_dir, "ScannedFiles.fpheal1");

	// Entries are built by hand: the FileInfo-based ctor requires an existing file, and
	// the heal must not touch the filesystem anyway (a File.Exists per entry would spin
	// up sleeping drives on every load of a million-entry database).
	static FileEntry MakeEntry(string path, EntryFlags flags, uint[]? fingerprint) => new() {
		_Path = path,
		Folder = System.IO.Path.GetDirectoryName(path)!,
		FileSize = 1000,
		Flags = flags,
		AudioFingerprint = fingerprint,
	};

	// Persists the current in-memory database WITHOUT counting as "heal persisted":
	// SaveDatabase writes the heal marker whenever an earlier load ran the heal pass
	// (possibly in a previous test — DatabaseUtils is static), so drop the marker to
	// put the on-disk state back to "not yet healed".
	void SaveAsUnhealed() {
		DatabaseUtils.SaveDatabase();
		File.Delete(MarkerPath);
	}

	FileEntry Get(string pathSuffix) =>
		DatabaseUtils.Database.Single(e => e.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase));

	[Fact]
	public void Heal_ClearsPoisonedEntries_LeavesLegitimateStatesAlone() {
		DatabaseUtils.Database.Add(MakeEntry(@"C:\t\poisoned.mp4", EntryFlags.AudioFingerprintError, Array.Empty<uint>()));
		DatabaseUtils.Database.Add(MakeEntry(@"C:\t\noaudio.mp4", EntryFlags.NoAudioTrack, Array.Empty<uint>()));
		DatabaseUtils.Database.Add(MakeEntry(@"C:\t\silent.mp4", EntryFlags.SilentAudioTrack, Array.Empty<uint>()));
		DatabaseUtils.Database.Add(MakeEntry(@"C:\t\good.mp4", 0, new uint[] { 1, 2, 3 }));
		// Odd legacy state: error flag but a real fingerprint — flag goes, data stays.
		DatabaseUtils.Database.Add(MakeEntry(@"C:\t\flagged-with-data.mp4", EntryFlags.AudioFingerprintError, new uint[] { 7, 8 }));
		SaveAsUnhealed();
		DatabaseUtils.Database.Clear();

		Assert.True(DatabaseUtils.LoadDatabase());

		var poisoned = Get("poisoned.mp4");
		Assert.False(poisoned.Flags.Has(EntryFlags.AudioFingerprintError));
		Assert.Null(poisoned.AudioFingerprint); // back to "not yet extracted" -> next scan retries

		var noAudio = Get("noaudio.mp4");
		Assert.True(noAudio.Flags.Has(EntryFlags.NoAudioTrack));
		Assert.Equal(Array.Empty<uint>(), noAudio.AudioFingerprint); // legitimate empty stays

		var silent = Get("silent.mp4");
		Assert.True(silent.Flags.Has(EntryFlags.SilentAudioTrack));
		Assert.Equal(Array.Empty<uint>(), silent.AudioFingerprint);

		Assert.Equal(new uint[] { 1, 2, 3 }, Get("good.mp4").AudioFingerprint);

		var flaggedWithData = Get("flagged-with-data.mp4");
		Assert.False(flaggedWithData.Flags.Has(EntryFlags.AudioFingerprintError));
		Assert.Equal(new uint[] { 7, 8 }, flaggedWithData.AudioFingerprint);
	}

	[Fact]
	public void Heal_RunsOnlyOnce_ReflaggedEntriesStayFlagged() {
		DatabaseUtils.Database.Add(MakeEntry(@"C:\t\broken.mp4", EntryFlags.AudioFingerprintError, Array.Empty<uint>()));
		SaveAsUnhealed();
		DatabaseUtils.Database.Clear();

		// First load heals; the marker is only a pending intent until a save persists it.
		Assert.True(DatabaseUtils.LoadDatabase());
		Assert.False(Get("broken.mp4").Flags.Has(EntryFlags.AudioFingerprintError));
		Assert.False(File.Exists(MarkerPath));

		// The retry genuinely fails again (this file really is broken) and the scan saves.
		var entry = Get("broken.mp4");
		entry.Flags.Set(EntryFlags.AudioFingerprintError);
		entry.AudioFingerprint = Array.Empty<uint>();
		DatabaseUtils.SaveDatabase();
		Assert.True(File.Exists(MarkerPath));

		// From now on the flag sticks: no second heal.
		DatabaseUtils.Database.Clear();
		Assert.True(DatabaseUtils.LoadDatabase());
		Assert.True(Get("broken.mp4").Flags.Has(EntryFlags.AudioFingerprintError));
	}

	[Fact]
	public void Heal_ExitWithoutSave_StaysPendingForNextLoad() {
		DatabaseUtils.Database.Add(MakeEntry(@"C:\t\poisoned.mp4", EntryFlags.AudioFingerprintError, Array.Empty<uint>()));
		SaveAsUnhealed();
		DatabaseUtils.Database.Clear();

		// Load + heal, but the "app" exits without saving: on-disk DB still poisoned,
		// and no marker may exist that would claim otherwise.
		Assert.True(DatabaseUtils.LoadDatabase());
		Assert.False(File.Exists(MarkerPath));
		DatabaseUtils.Database.Clear();

		// Next load must heal again.
		Assert.True(DatabaseUtils.LoadDatabase());
		Assert.False(Get("poisoned.mp4").Flags.Has(EntryFlags.AudioFingerprintError));
		Assert.Null(Get("poisoned.mp4").AudioFingerprint);
	}
}
