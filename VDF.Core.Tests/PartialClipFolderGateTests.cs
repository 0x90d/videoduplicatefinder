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

// Regression tests for #870: the partial clip passes ignored the folder match mode, so
// "different folder only" scans still grouped a clip with a source from the same folder.
// The gate now lives in CollectPartialMatchCandidates, which both the audio pass and the
// AI partial pass run their pairs through - the audio pass exercised here covers the
// shared code path for both.
[Collection("DatabaseUtils")] // ScanForPartialDuplicates reads the shared static database
public class PartialClipFolderGateTests : IDisposable {

	readonly List<FileEntry> added = new();

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
	}

	FileEntry Add(string folder, string name, double durationSeconds, uint[] fingerprint) {
		var entry = new FileEntry {
			_Path = folder + "\\" + name,
			Folder = folder,
			FileSize = 1,
			invalid = false,
			IsImage = false,
			AudioFingerprint = fingerprint,
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) },
		};
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	// Distinct, non-zero blocks: an all-zero fingerprint is treated as a silent track and skipped.
	static readonly uint[] SourceFingerprint =
		{ 0x0F0F0F0F, 0x12345678, 0xA5A5A5A5, 0xDEADBEEF, 0xCAFEBABE, 0x11223344, 0x55667788, 0x99AABBCC, 0x0BADF00D, 0xFEEDFACE };

	/// <summary>Blocks 2..4 of the source: an exact sub-window, so the sliding compare matches at 100%.</summary>
	static uint[] ClipFingerprint => SourceFingerprint[2..5];

	// Unique folder names per test run: the shared static database may briefly hold
	// entries of a concurrently disposed test class in this collection.
	static string Folder(string name) => @"C:\vdf-fgate-" + Guid.NewGuid().ToString("N") + "\\" + name;

	static ScanEngine NewEngine(FolderMatchMode mode) {
		var engine = new ScanEngine();
		engine.Settings.EnablePartialClipDetection = true;
		engine.Settings.PartialClipRequireVisualMatch = false; // no frame decoding in this test
		engine.Settings.FolderMatchMode = mode;
		engine.ElapsedTimer.Start();
		return engine;
	}

	[Fact]
	public void DifferentFolderOnly_IgnoresSameFolderClip() {
		var engine = NewEngine(FolderMatchMode.DifferentFolderOnly);
		string folder = Folder("both");
		Add(folder, "source.mp4", 100, SourceFingerprint);
		Add(folder, "clip.mp4", 30, ClipFingerprint);

		engine.ScanForPartialDuplicates();

		Assert.Empty(engine.Duplicates);
	}

	[Fact]
	public void DifferentFolderOnly_StillFindsCrossFolderClip() {
		var engine = NewEngine(FolderMatchMode.DifferentFolderOnly);
		Add(Folder("sources"), "source.mp4", 100, SourceFingerprint);
		var clip = Add(Folder("clips"), "clip.mp4", 30, ClipFingerprint);

		engine.ScanForPartialDuplicates();

		var clipResult = Assert.Single(engine.Duplicates, d => d.Path == clip.Path);
		Assert.True(clipResult.Flags.HasFlag(DuplicateFlags.PartialClip));
		Assert.Equal(2, engine.Duplicates.Count);
	}

	[Fact]
	public void SameFolderOnly_IgnoresCrossFolderClip() {
		var engine = NewEngine(FolderMatchMode.SameFolderOnly);
		Add(Folder("sources"), "source.mp4", 100, SourceFingerprint);
		Add(Folder("clips"), "clip.mp4", 30, ClipFingerprint);

		engine.ScanForPartialDuplicates();

		Assert.Empty(engine.Duplicates);
	}

	[Fact]
	public void SameFolderOnly_StillFindsSameFolderClip() {
		var engine = NewEngine(FolderMatchMode.SameFolderOnly);
		string folder = Folder("both");
		Add(folder, "source.mp4", 100, SourceFingerprint);
		var clip = Add(folder, "clip.mp4", 30, ClipFingerprint);

		engine.ScanForPartialDuplicates();

		var clipResult = Assert.Single(engine.Duplicates, d => d.Path == clip.Path);
		Assert.True(clipResult.Flags.HasFlag(DuplicateFlags.PartialClip));
	}

	[Fact]
	public void FolderGate_RespectsTheConfiguredDepth() {
		// Depth 2: "...\show\season1" vs "...\other\season1" differ one level up, so
		// they count as different folders even though the leaf segment matches.
		var engine = NewEngine(FolderMatchMode.DifferentFolderOnly);
		engine.Settings.SameFolderDepth = 2;
		string root = @"C:\vdf-fgate-" + Guid.NewGuid().ToString("N");
		Add(root + @"\show\season1", "source.mp4", 100, SourceFingerprint);
		var clip = Add(root + @"\other\season1", "clip.mp4", 30, ClipFingerprint);

		engine.ScanForPartialDuplicates();

		Assert.Single(engine.Duplicates, d => d.Path == clip.Path);
	}
}
