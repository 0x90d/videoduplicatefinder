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

using System.Diagnostics;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

// These tests flip the process-wide CoreUtils.IsWindows, which other tests read, so they must
// not overlap with anything. Non-parallel collections run alone, after the parallel ones.
[CollectionDefinition(nameof(SpellingOnlyRenameTests), DisableParallelization = true)]
public class SpellingOnlyRenameCollection { }

/// <summary>
/// A rename done outside VDF that changes only a name's spelling — its case (Finder:
/// movie.mp4 -> Movie.mp4) or its Unicode normalization form (café.mp4 NFD -> NFC) — on a volume
/// that ignores that difference (default APFS, SMB shares) must relink the existing entry.
/// File.Exists still resolves the old spelling there, so without care the rename reads as a copy
/// and the one physical file ends up in the database twice — and is reported as a duplicate of
/// itself. The bug needs an insensitive volume plus exact database keys. NTFS ignores case, so
/// the tests switch the keys to non-Windows behaviour and the case tests also run on Windows CI.
/// </summary>
[Collection(nameof(SpellingOnlyRenameTests))]
public class SpellingOnlyRenameTests : IDisposable {
	const string CafeNfd = "cafe\u0301.mp4";
	const string CafeNfc = "caf\u00e9.mp4";

	readonly string _dir;
	readonly bool _wasWindows;

	public SpellingOnlyRenameTests() {
		_dir = Path.Combine(Path.GetTempPath(), $"vdf-spelling-rename-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_dir);
		DatabaseUtils.Database.Clear();
		_wasWindows = CoreUtils.IsWindows;
		CoreUtils.IsWindows = false;
	}

	public void Dispose() {
		// Clear before restoring: entries hashed with ordinal keys must not outlive the switch back.
		DatabaseUtils.Database.Clear();
		CoreUtils.IsWindows = _wasWindows;
		try { Directory.Delete(_dir, recursive: true); } catch { }
	}

	// Whether `dir` finds a file written as `written` when asked for `lookedUp`. Where it doesn't,
	// the old spelling is gone after a respelling, so it is an ordinary move that File.Exists
	// already detects (see TombstoneRelinkInteractionTests).
	static bool ResolvesAs(string dir, string written, string lookedUp) {
		string probe = Path.Combine(dir, written);
		File.WriteAllBytes(probe, []);
		bool resolves = File.Exists(Path.Combine(dir, lookedUp));
		File.Delete(probe);
		return resolves;
	}

	bool IgnoresCase() => ResolvesAs(_dir, "probe", "PROBE");

	// APFS and SMB resolve either form; NTFS and ext4 treat them as different names.
	bool IgnoresNormalization() => ResolvesAs(_dir, CafeNfd, CafeNfc);

	// A folder where a.mp4 and A.mp4 are distinct files, or null where none can be made: Linux
	// temp is one already; Windows can mark an empty NTFS folder case-sensitive, but only when
	// elevated (as on GitHub's runners); default macOS volumes can't.
	string? CaseSensitiveFolder() {
		string dir = Path.Combine(_dir, "case-sensitive");
		Directory.CreateDirectory(dir);
		if (OperatingSystem.IsWindows()) {
			try {
				using var fsutil = Process.Start(new ProcessStartInfo("fsutil.exe", ["file", "setCaseSensitiveInfo", dir, "enable"]) {
					UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
				});
				fsutil!.WaitForExit(10_000);
			}
			catch {
				return null;
			}
		}
		return ResolvesAs(dir, "probe", "PROBE") ? null : dir;
	}

	string WriteVideo(string name) {
		string path = Path.Combine(_dir, name);
		var content = new byte[200_000];
		for (int i = 0; i < content.Length; i++)
			content[i] = (byte)(i * 17 & 0xFF);
		File.WriteAllBytes(path, content);
		return path;
	}

	static FileEntry AnalysedEntry(string path) {
		var file = new FileInfo(path);
		var entry = new FileEntry {
			_Path = path,
			Folder = Path.GetDirectoryName(path)!,
			FileSize = file.Length,
			DateCreated = file.CreationTimeUtc,
			DateModified = file.LastWriteTimeUtc,
			OsHash = OsHashUtils.TryCompute(path),
		};
		entry.grayBytes[0] = new byte[] { 1, 2, 3 };
		return entry;
	}

	// What BuildFileList does when the scan finds `newPath` missing from the database.
	static bool Relink(string newPath) =>
		new ScanEngine().TryRelinkMovedFile(new FileEntry(newPath), ScanEngine.BuildRelinkIndex());

	// Renames and reports whether the folder now lists the new spelling. Some volumes resolve both
	// spellings yet treat the rename as a no-op (case-sensitive APFS does for NFC/NFD), so the
	// respelled state these tests need can't be produced there.
	static bool Respell(string oldPath, string newPath) {
		File.Move(oldPath, newPath);
		return Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(newPath)!).Contains(newPath);
	}

	[SkippableFact]
	public void CaseOnlyFileRename_IsRelinked() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		string oldPath = WriteVideo("movie.mp4");
		var dbEntry = AnalysedEntry(oldPath);
		DatabaseUtils.Database.Add(dbEntry);
		string newPath = Path.Combine(_dir, "Movie.mp4");
		File.Move(oldPath, newPath);

		Assert.True(Relink(newPath));
		var only = Assert.Single(DatabaseUtils.Database);
		Assert.Equal(newPath, only.Path);
		Assert.NotEmpty(only.grayBytes);
	}

	[SkippableFact]
	public void CaseOnlyFolderRename_IsRelinked() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		Directory.CreateDirectory(Path.Combine(_dir, "clips"));
		string oldPath = WriteVideo(Path.Combine("clips", "movie.mp4"));
		var dbEntry = AnalysedEntry(oldPath);
		DatabaseUtils.Database.Add(dbEntry);
		// Via a temporary name: .NET on Windows may refuse a move between paths equal ignoring case.
		Directory.Move(Path.Combine(_dir, "clips"), Path.Combine(_dir, "clips-tmp"));
		Directory.Move(Path.Combine(_dir, "clips-tmp"), Path.Combine(_dir, "Clips"));
		string newPath = Path.Combine(_dir, "Clips", "movie.mp4");

		Assert.True(Relink(newPath));
		Assert.Equal(newPath, Assert.Single(DatabaseUtils.Database).Path);
	}

	[SkippableFact]
	public void ExistingCaseTwin_IsPruned_KeepingTheSpellingOnDisk() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// A database written before the fix: both spellings of one renamed file are present.
		string oldPath = WriteVideo("movie.mp4");
		var stale = AnalysedEntry(oldPath);
		string newPath = Path.Combine(_dir, "Movie.mp4");
		File.Move(oldPath, newPath);
		var current = AnalysedEntry(newPath);
		DatabaseUtils.Database.Add(stale);
		DatabaseUtils.Database.Add(current);

		Assert.Equal(1, ScanEngine.PruneStaleSpellings());
		Assert.Same(current, Assert.Single(DatabaseUtils.Database));
	}

	[SkippableFact]
	public void AllTwinsStale_KeepsOne_WhichIsThenRelinkedWithItsAnalysis() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// Renamed by case twice under an older build: neither stored spelling is on disk.
		string first = WriteVideo("movie.mp4");
		var a = AnalysedEntry(first);
		var b = AnalysedEntry(first);
		b._Path = Path.Combine(_dir, "MOVIE.mp4");
		string current = Path.Combine(_dir, "Movie.mp4");
		File.Move(first, current);
		DatabaseUtils.Database.Add(a);
		DatabaseUtils.Database.Add(b);

		Assert.Equal(1, ScanEngine.PruneStaleSpellings());
		var kept = Assert.Single(DatabaseUtils.Database);
		Assert.True(Relink(current));
		Assert.Equal(current, Assert.Single(DatabaseUtils.Database).Path);
		Assert.NotEmpty(kept.grayBytes);
	}

	[SkippableFact]
	public void DistinctFilesDifferingOnlyByCase_AreBothKept() {
		string dir = CaseSensitiveFolder() ?? throw new SkipException("needs a case-sensitive folder");
		// Same bytes too, so only the exact-name check stands between them and a wrong relink.
		string lower = WriteVideo(Path.Combine(dir, "a.mp4"));
		string upper = WriteVideo(Path.Combine(dir, "A.mp4"));
		var lowerEntry = AnalysedEntry(lower);
		DatabaseUtils.Database.Add(lowerEntry);

		Assert.False(Relink(upper));
		Assert.Equal(lower, lowerEntry.Path);

		DatabaseUtils.Database.Add(AnalysedEntry(upper));
		Assert.Equal(0, ScanEngine.PruneStaleSpellings());
		Assert.Equal(2, DatabaseUtils.Database.Count);
	}

	[SkippableFact]
	public void UnresolvableOldSpelling_WithTheSameContent_IsPruned() {
		// Some SMB servers stop resolving the old case of a renamed file. A case-sensitive folder
		// gives the same picture: the old spelling is simply missing.
		string dir = CaseSensitiveFolder() ?? throw new SkipException("needs a case-sensitive folder");
		string current = WriteVideo(Path.Combine(dir, "Movie.mp4"));
		var kept = AnalysedEntry(current);
		var old = AnalysedEntry(current);
		old._Path = Path.Combine(dir, "movie.mp4");
		DatabaseUtils.Database.Add(kept);
		DatabaseUtils.Database.Add(old);

		Assert.Equal(1, ScanEngine.PruneStaleSpellings());
		Assert.Same(kept, Assert.Single(DatabaseUtils.Database));
	}

	[SkippableFact]
	public void MissingFileDifferingOnlyByCase_WithOtherContent_IsKept() {
		string dir = CaseSensitiveFolder() ?? throw new SkipException("needs a case-sensitive folder");
		// A deleted a.mp4 that held something else than A.mp4: its record must survive.
		string current = WriteVideo(Path.Combine(dir, "A.mp4"));
		DatabaseUtils.Database.Add(AnalysedEntry(current));
		var deleted = AnalysedEntry(current);
		deleted._Path = Path.Combine(dir, "a.mp4");
		deleted.OsHash = "0000000000000000";
		DatabaseUtils.Database.Add(deleted);

		Assert.Equal(0, ScanEngine.PruneStaleSpellings());
		Assert.Equal(2, DatabaseUtils.Database.Count);
	}

	[SkippableFact]
	public void NormalizationOnlyRename_IsRelinked() {
		Skip.IfNot(IgnoresNormalization(), "needs a normalization-insensitive volume");
		string oldPath = WriteVideo(CafeNfd);
		var dbEntry = AnalysedEntry(oldPath);
		DatabaseUtils.Database.Add(dbEntry);
		string newPath = Path.Combine(_dir, CafeNfc);
		Skip.IfNot(Respell(oldPath, newPath), "the volume keeps the old spelling on rename");

		Assert.True(Relink(newPath));
		var only = Assert.Single(DatabaseUtils.Database);
		Assert.Equal(newPath, only.Path);
		Assert.NotEmpty(only.grayBytes);
	}

	[SkippableFact]
	public void ExistingNormalizationTwin_IsPruned_KeepingTheSpellingOnDisk() {
		Skip.IfNot(IgnoresNormalization(), "needs a normalization-insensitive volume");
		string oldPath = WriteVideo(CafeNfd);
		var stale = AnalysedEntry(oldPath);
		string newPath = Path.Combine(_dir, CafeNfc);
		Skip.IfNot(Respell(oldPath, newPath), "the volume keeps the old spelling on rename");
		var current = AnalysedEntry(newPath);
		DatabaseUtils.Database.Add(stale);
		DatabaseUtils.Database.Add(current);

		Assert.Equal(1, ScanEngine.PruneStaleSpellings());
		Assert.Same(current, Assert.Single(DatabaseUtils.Database));
	}

	[SkippableFact]
	public void WindowsRescan_TakesTheCasingOnDisk() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// Windows keys: the rescan's lookup matches the old spelling instead of missing it.
		CoreUtils.IsWindows = true;
		string oldPath = WriteVideo("movie.mp4");
		var dbEntry = AnalysedEntry(oldPath);
		DatabaseUtils.Database.Add(dbEntry);
		string newPath = Path.Combine(_dir, "Movie.mp4");
		File.Move(oldPath, newPath);
		var fEntry = new FileEntry(newPath);
		dbEntry.DateCreated = fEntry.DateCreated;
		dbEntry.DateModified = fEntry.DateModified;

		Assert.True(DatabaseUtils.Database.TryGetValue(fEntry, out var found));
		ScanEngine.RefreshExistingEntry(fEntry, found!);

		var only = Assert.Single(DatabaseUtils.Database);
		Assert.Same(dbEntry, only);
		Assert.Equal(newPath, only.Path);
		Assert.NotEmpty(only.grayBytes);
	}

	[SkippableFact]
	public void Copy_IsNotMistakenForRespelling() {
		// Differs from the original only by case, and the original still exists: a second file, not
		// a respelling. Only a case-sensitive folder can hold both.
		string dir = CaseSensitiveFolder() ?? throw new SkipException("needs a case-sensitive folder");
		string original = WriteVideo(Path.Combine(dir, "movie.mp4"));
		var dbEntry = AnalysedEntry(original);
		DatabaseUtils.Database.Add(dbEntry);
		string copy = Path.Combine(dir, "Movie.mp4");
		File.Copy(original, copy);

		Assert.False(Relink(copy));
		Assert.Equal(original, dbEntry.Path);
		Assert.Equal(0, ScanEngine.PruneStaleSpellings());
	}

	[SkippableFact]
	public void SmallFileRespelling_IsRelinkedWithoutFingerprint() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// Under 64 KiB there is no OsHash, so only the respelling itself can prove the identity.
		string oldPath = Path.Combine(_dir, "logo.png");
		File.WriteAllBytes(oldPath, new byte[1000]);
		var dbEntry = AnalysedEntry(oldPath);
		Assert.Null(dbEntry.OsHash);
		DatabaseUtils.Database.Add(dbEntry);
		string newPath = Path.Combine(_dir, "Logo.png");
		File.Move(oldPath, newPath);

		Assert.True(Relink(newPath));
		var only = Assert.Single(DatabaseUtils.Database);
		Assert.Same(dbEntry, only);
		Assert.Equal(newPath, only.Path);
		Assert.NotEmpty(only.grayBytes);
	}

	[SkippableFact]
	public void RespelledAndModified_DropsTheOldAnalysis() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		string oldPath = Path.Combine(_dir, "logo.png");
		File.WriteAllBytes(oldPath, new byte[1000]);
		var dbEntry = AnalysedEntry(oldPath);
		DatabaseUtils.Database.Add(dbEntry);
		string newPath = Path.Combine(_dir, "Logo.png");
		File.Move(oldPath, newPath);
		// Same size, different bytes and a later timestamp: the cached analysis no longer applies.
		File.WriteAllBytes(newPath, Enumerable.Repeat((byte)7, 1000).ToArray());
		File.SetLastWriteTimeUtc(newPath, dbEntry.DateModified.AddMinutes(1));

		// Not counted as a relink: the old analysis was dropped, not reused.
		Assert.False(Relink(newPath));
		var only = Assert.Single(DatabaseUtils.Database);
		Assert.NotSame(dbEntry, only);
		Assert.Equal(newPath, only.Path);
		Assert.Empty(only.grayBytes);
	}

	[Fact]
	public void ResolveOnDiskSpelling_TakesTheFolderSpellingOnDisk() {
		string real = Path.Combine(_dir, "Media", "Movies");
		Directory.CreateDirectory(real);
		string typed = Path.Combine(_dir, "media", "movies");

		// Where the typed spelling is a different (missing) folder, it must not be redirected.
		Assert.Equal(IgnoresCase() ? real : typed, FileUtils.ResolveOnDiskSpelling(typed));
		Assert.Equal(real, FileUtils.ResolveOnDiskSpelling(real));
		string missing = Path.Combine(_dir, "nowhere", "movies");
		Assert.Equal(missing, FileUtils.ResolveOnDiskSpelling(missing));
	}

	[SkippableFact]
	public void CompareOnly_RespellsEntriesStoredUnderTheTypedIncludeFolder() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// A database scanned with the include folder typed as "media" while the disk says "Media".
		string real = Path.Combine(_dir, "Media");
		Directory.CreateDirectory(real);
		File.WriteAllBytes(Path.Combine(real, "clip.mp4"), new byte[1000]);
		var stored = AnalysedEntry(Path.Combine(_dir, "media", "clip.mp4"));
		DatabaseUtils.Database.Add(stored);
		// Out of scope even ignoring case: left alone, so offline drives are never read.
		string elsewhere = Path.Combine(_dir, "elsewhere", "clip.mp4");
		DatabaseUtils.Database.Add(new FileEntry { _Path = elsewhere, Folder = Path.GetDirectoryName(elsewhere)! });

		var engine = new ScanEngine();
		engine.Settings.IncludeList.Add(real); // as NormalizeScanPaths spells it

		Assert.Equal(1, engine.RespellEntriesUnderIncludeFolders());
		Assert.Equal(Path.Combine(real, "clip.mp4"), stored.Path);
		Assert.Equal(real, stored.Folder);
		Assert.Same(stored, DatabaseUtils.Database.Single(e => e.Path == stored.Path));
		Assert.Contains(DatabaseUtils.Database, e => e.Path == elsewhere);
	}

	[SkippableFact]
	public void CompareOnly_RespellsDeletedFilesWithoutThrowing() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// Deleted files stay in the database until it is cleaned; only their folder can be respelled.
		string real = Path.Combine(_dir, "Media");
		Directory.CreateDirectory(real);
		string stored = Path.Combine(_dir, "media", "gone.mp4");
		var entry = new FileEntry { _Path = stored, Folder = Path.GetDirectoryName(stored)! };
		DatabaseUtils.Database.Add(entry);

		var engine = new ScanEngine();
		engine.Settings.IncludeList.Add(real);

		Assert.Equal(1, engine.RespellEntriesUnderIncludeFolders());
		Assert.Equal(Path.Combine(real, "gone.mp4"), entry.Path);
	}

	[SkippableFact]
	public void CompareOnly_WithoutSubdirectories_LeavesSubfolderEntriesAlone() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// Left by an earlier recursive scan: out of scope now, and respelling it would list every ancestor on each compare.
		string real = Path.Combine(_dir, "Media");
		Directory.CreateDirectory(Path.Combine(real, "sub"));
		string nested = Path.Combine(_dir, "media", "sub", "clip.mp4");
		DatabaseUtils.Database.Add(new FileEntry { _Path = nested, Folder = Path.GetDirectoryName(nested)! });
		var direct = new FileEntry { _Path = Path.Combine(_dir, "media", "top.mp4"), Folder = Path.Combine(_dir, "media") };
		DatabaseUtils.Database.Add(direct);

		var engine = new ScanEngine();
		engine.Settings.IncludeSubDirectories = false;
		engine.Settings.IncludeList.Add(real);

		Assert.Equal(1, engine.RespellEntriesUnderIncludeFolders());
		Assert.Equal(Path.Combine(real, "top.mp4"), direct.Path);
		Assert.Contains(DatabaseUtils.Database, e => e.Path == nested);
	}

	[SkippableFact]
	public void CompareOnly_LeavesSiblingFoldersSharingAPrefixAlone() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		string real = Path.Combine(_dir, "Media");
		Directory.CreateDirectory(real);
		Directory.CreateDirectory(Path.Combine(_dir, "Media2"));
		File.WriteAllBytes(Path.Combine(_dir, "Media2", "clip.mp4"), new byte[1000]);
		string sibling = Path.Combine(_dir, "media2", "clip.mp4");
		DatabaseUtils.Database.Add(AnalysedEntry(sibling));

		var engine = new ScanEngine();
		engine.Settings.IncludeList.Add(real);

		Assert.Equal(0, engine.RespellEntriesUnderIncludeFolders());
		Assert.Contains(DatabaseUtils.Database, e => e.Path == sibling);
	}

	[SkippableFact]
	public void CompareOnly_WithADatabaseLoadedEarlier_StillComparesEntriesStoredUnderTheTypedIncludeFolder() {
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// The GUI loads the database at startup, so PrepareCompare finds it populated and does not load it.
		string real = Path.Combine(_dir, "Media");
		Directory.CreateDirectory(real);
		string file = Path.Combine(real, "clip.mp4");
		File.WriteAllBytes(file, new byte[1000]);
		var stored = AnalysedEntry(Path.Combine(_dir, "media", "clip.mp4"));
		DatabaseUtils.Database.Add(stored);

		var engine = new ScanEngine();
		engine.Settings.IncludeList.Add(Path.Combine(_dir, "media")); // as typed; NormalizeScanPaths spells it as on disk
		engine.Settings.CustomDatabaseFolder = _dir;
		DatabaseUtils.CustomDatabaseFolder = _dir;
		try {
			engine.PrepareCompare();
		}
		finally {
			DatabaseUtils.CustomDatabaseFolder = null;
			DatabaseUtils.InvalidateDatabaseFolder();
		}

		Assert.Equal(file, stored.Path);
		Assert.False(engine.InvalidEntry(stored, out _, out _));
	}

	[SkippableFact]
	public void ResolveOnDiskSpelling_TakesTheNormalizationFormOnDisk() {
		Skip.IfNot(IgnoresNormalization(), "needs a normalization-insensitive volume");
		string real = Path.Combine(_dir, Path.GetFileNameWithoutExtension(CafeNfd));
		Directory.CreateDirectory(real);
		string typed = Path.Combine(_dir, Path.GetFileNameWithoutExtension(CafeNfc));
		Skip.If(Directory.EnumerateDirectories(_dir).Contains(typed), "the volume stored the typed form, so there is nothing to correct");

		Assert.Equal(real, FileUtils.ResolveOnDiskSpelling(typed));
	}

	[Fact]
	public void IsStaleSpellingOf_FalseWhenFileIsMissing() {
		// Offline drive or deleted file: not a spelling question, leave it to the tombstone logic.
		string a = Path.Combine(_dir, "gone.mp4");
		Assert.False(FileUtils.IsStaleSpellingOf(a, Path.Combine(_dir, "Gone.mp4")));
	}

	[SkippableFact]
	public void UnlistableFolder_IsNotTakenAsRespelling() {
		// A guard the CA1416 platform analyzer understands, which Skip.If is not.
		if (OperatingSystem.IsWindows())
			throw new SkipException("Unix file modes are not available on Windows");
		Skip.IfNot(IgnoresCase(), "needs a case-insensitive volume");
		// Files can be opened but the folder can't be listed (NAS permissions, or macOS withholding
		// network-volume access). An unreadable listing must not read as "the stored name is gone".
		string locked = Path.Combine(_dir, "locked");
		Directory.CreateDirectory(locked);
		string onDisk = WriteVideo(Path.Combine("locked", "movie.mp4"));
		string other = Path.Combine(locked, "MOVIE.mp4");
		var twin = AnalysedEntry(onDisk);
		twin._Path = other;
		DatabaseUtils.Database.Add(AnalysedEntry(onDisk));
		DatabaseUtils.Database.Add(twin);
		File.SetUnixFileMode(locked, UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		try {
			// Running as root, or the volume ignores permissions: nothing to simulate.
			Skip.If(!File.Exists(onDisk) || Directory.EnumerateFileSystemEntries(locked, "*", new EnumerationOptions { IgnoreInaccessible = true }).Any(),
				"the folder can still be listed");

			Assert.False(FileUtils.IsStaleSpellingOf(onDisk, other));
			Assert.False(FileUtils.IsStaleSpellingOf(other, onDisk));
			Assert.Equal(0, ScanEngine.PruneStaleSpellings());
			Assert.Equal(2, DatabaseUtils.Database.Count);
		}
		finally {
			File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}

	[Fact]
	public void InvalidUnicodePath_DoesNotThrow() {
		// A lone surrogate (mangled emoji) makes string.Normalize throw; one such entry must not
		// abort the scan.
		string broken = Path.Combine(_dir, "clip\uD83D.mp4");
		DatabaseUtils.Database.Add(new FileEntry { _Path = broken, Folder = _dir });
		DatabaseUtils.Database.Add(new FileEntry { _Path = Path.Combine(_dir, "Clip\uD83D.mp4"), Folder = _dir });

		Assert.Equal(0, ScanEngine.PruneStaleSpellings());
		Assert.False(FileUtils.IsStaleSpellingOf(broken, Path.Combine(_dir, "CLIP\uD83D.mp4")));
	}
}
