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

using MemoryPack;
using VDF.Core.Tests.AI;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")]
public class AudioFingerprintCacheTests {
	static FileEntry Entry() => new() {
		_Path = Path.Combine(Path.GetTempPath(), "vdf-fingerprint-" + Guid.NewGuid().ToString("N") + ".mp4"),
		Folder = Path.GetTempPath(),
		AudioFingerprint = [1, 2, 3],
		AudioFingerprintFrames = [11, 12, 13, 14],
		invalid = false,
	};

	[Fact]
	public void LegacyNonemptyFingerprint_IsUpgradedOnNextPartialScan() {
		var entry = Entry(); entry.AudioFingerprintFrames = null;
		File.WriteAllBytes(entry.Path, []);
		try {
			Assert.True(ScanEngine.NeedsAudioFingerprint(entry, true, false));
			Assert.False(ScanEngine.NeedsAudioFingerprint(entry, false, false));
		}
		finally { File.Delete(entry.Path); }
	}

	[Fact]
	public void MissingLegacyFile_KeepsCachedFingerprintInsteadOfAttemptingUpgrade() {
		var entry = Entry(); entry.AudioFingerprintFrames = null;
		Assert.False(File.Exists(entry.Path));
		Assert.False(ScanEngine.NeedsAudioFingerprint(entry, true, true));
		Assert.Equal(new uint[] { 1, 2, 3 }, entry.AudioFingerprint);
	}

	[Fact]
	public void CurrentFingerprint_IsReused() => Assert.False(ScanEngine.NeedsAudioFingerprint(Entry(), true, true));

	[Fact]
	public void ClearCachedMediaData_DropsFineFeaturesToo() {
		var entry = Entry(); entry.ClearCachedMediaData();
		Assert.Null(entry.AudioFingerprintFrames);
	}

	[Fact]
	public void FineFeatures_RoundTripThroughMemoryPack() {
		var entry = Entry();
		var restored = MemoryPackSerializer.Deserialize<FileEntry>(MemoryPackSerializer.Serialize(entry))!;
		Assert.Equal(entry.AudioFingerprint, restored.AudioFingerprint);
		Assert.Equal(entry.AudioFingerprintFrames, restored.AudioFingerprintFrames);
	}

	[Fact]
	public void OldEmbeddingMember_IsSkippedInsteadOfBecomingAudioFrames() {
		var legacy = new FileEntryEmbeddingsTests.FileEntryWithEmbeddingsMember {
			_Path = Path.Combine(Path.GetTempPath(), "legacy.mp4"),
			AudioFingerprint = [1, 2, 3],
			Embeddings = new() { [0] = [1, 2, 3] }
		};
		var restored = MemoryPackSerializer.Deserialize<FileEntry>(MemoryPackSerializer.Serialize(legacy))!;
		Assert.Equal(legacy.AudioFingerprint, restored.AudioFingerprint);
		Assert.Null(restored.AudioFingerprintFrames);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void LegacyMixedOrVeryShortPair_UsesCoarseFeaturesAndSecondOffsets(bool sourceHasFrames, bool clipHasFrames) {
		var source = Entry(); var clip = Entry();
		source.AudioFingerprint = [uint.MaxValue, uint.MaxValue, 0x12345678, 0xABCDEF12, uint.MaxValue];
		clip.AudioFingerprint = [0x12345678, 0xABCDEF12];
		source.AudioFingerprintFrames = sourceHasFrames ? [1, 2, 3, 4, 5] : null;
		clip.AudioFingerprintFrames = clipHasFrames ? [9, 9] : null;
		var (similarity, offset) = ScanEngine.CompareAudioFingerprints(clip, source);
		Assert.Equal(1f, similarity);
		Assert.Equal(2d, offset);
	}

	[Fact]
	public void NewAudioPayload_StillLoadsIntoFormerEmbeddingSchema() {
		var entry = Entry();
		var oldReader = MemoryPackSerializer.Deserialize<FileEntryEmbeddingsTests.FileEntryWithEmbeddingsMember>(MemoryPackSerializer.Serialize(entry))!;
		Assert.Equal(entry.AudioFingerprint, oldReader.AudioFingerprint);
		Assert.Equal(entry.Path, oldReader._Path);
		Assert.Null(oldReader.Embeddings); // reserved/missing member, not an audio array
	}

	[Fact]
	public void StreamingDatabase_RetainsFineAndLegacyCacheFormats() {
		string folder = Path.Combine(Path.GetTempPath(), "vdf-audio-cache-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		DatabaseUtils.CustomDatabaseFolder = folder; DatabaseUtils.InvalidateDatabaseFolder();
		DatabaseUtils.Database.Clear();
		try {
			var current = Entry();
			var legacy = Entry(); legacy.Path = Path.Combine(folder, "legacy.mp4"); legacy.AudioFingerprintFrames = null;
			File.WriteAllBytes(legacy.Path, []);
			DatabaseUtils.Database.UnionWith([current, legacy]);
			DatabaseUtils.SaveDatabase(); DatabaseUtils.Database.Clear();
			Assert.True(DatabaseUtils.LoadDatabase());
			Assert.Equal(current.AudioFingerprintFrames, DatabaseUtils.Database.Single(e => e.Path == current.Path).AudioFingerprintFrames);
			Assert.Null(DatabaseUtils.Database.Single(e => e.Path == legacy.Path).AudioFingerprintFrames);
			Assert.False(ScanEngine.NeedsAudioFingerprint(DatabaseUtils.Database.Single(e => e.Path == current.Path), true, true));
			Assert.True(ScanEngine.NeedsAudioFingerprint(DatabaseUtils.Database.Single(e => e.Path == legacy.Path), true, false));
		}
		finally {
			DatabaseUtils.Database.Clear(); DatabaseUtils.CustomDatabaseFolder = null; DatabaseUtils.InvalidateDatabaseFolder();
			Directory.Delete(folder, true);
		}
	}
}
