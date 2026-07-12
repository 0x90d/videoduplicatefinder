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

using VDF.Core.AI;

namespace VDF.Core.Tests.AI;

public class DenseEmbeddingStoreTests : IDisposable {
	readonly string tempDir;

	public DenseEmbeddingStoreTests() {
		tempDir = Path.Combine(Path.GetTempPath(), $"vdf_dense_store_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		DenseEmbeddingStore.TestOverrideStorePath = Path.Combine(tempDir, "DenseEmbeddings.db");
	}

	public void Dispose() {
		DenseEmbeddingStore.TestOverrideStorePath = null;
		try { Directory.Delete(tempDir, true); } catch { }
	}

	static DenseEmbeddingStore.DenseRecord Record(int seed, int frames = 3) {
		var rng = new Random(seed);
		var data = new byte[frames][];
		for (int i = 0; i < frames; i++)
			data[i] = EmbeddingMath.QuantizeUnitVector(EmbeddingMathTests.RandomUnitVector(rng));
		return new DenseEmbeddingStore.DenseRecord(1000 + seed, 2000 + seed, 15f, data);
	}

	[Fact]
	public void SaveAndLoad_RoundTripsRecords() {
		var store = new DenseEmbeddingStore();
		var record = Record(1);
		store.Put(@"D:\media\a.mp4", record);
		store.Save(keepOnly: null);

		var loaded = DenseEmbeddingStore.Load();
		Assert.True(loaded.TryGet(@"D:\media\a.mp4", record.FileSize, record.MTimeUtcTicks, out var got));
		Assert.Equal(record.IntervalSeconds, got.IntervalSeconds);
		Assert.Equal(record.Frames.Length, got.Frames.Length);
		Assert.Equal(record.Frames[0], got.Frames[0]);
	}

	[Fact]
	public void TryGet_RejectsChangedFile() {
		var store = new DenseEmbeddingStore();
		var record = Record(2);
		store.Put(@"D:\media\b.mp4", record);

		Assert.False(store.TryGet(@"D:\media\b.mp4", record.FileSize + 1, record.MTimeUtcTicks, out _));
		Assert.False(store.TryGet(@"D:\media\b.mp4", record.FileSize, record.MTimeUtcTicks + 1, out _));
	}

	[Fact]
	public void Save_PrunesRecordsOutsideKeepSet() {
		var store = new DenseEmbeddingStore();
		store.Put(@"D:\media\keep.mp4", Record(3));
		store.Put(@"D:\media\stale.mp4", Record(4));
		store.Save(new HashSet<string>(new[] { @"D:\media\keep.mp4" }, StringComparer.OrdinalIgnoreCase));

		var loaded = DenseEmbeddingStore.Load();
		Assert.Equal(1, loaded.Count);
		Assert.True(loaded.TryGet(@"D:\media\keep.mp4", 1003, 2003, out _));
	}

	[Fact]
	public void CorruptFile_LoadsAsEmptyStore() {
		File.WriteAllBytes(DenseEmbeddingStore.StorePath, new byte[] { 1, 2, 3, 4, 5 });
		Assert.Equal(0, DenseEmbeddingStore.Load().Count);
	}

	[Fact]
	public void TruncatedFrames_RebuildAsEmptyStore() {
		// Regression: BinaryReader.ReadBytes returns a SHORT array at EOF without
		// throwing. A store truncated inside the last record's frame bytes must be
		// treated as corrupt (rebuild), not accepted as a poisoned short-frame record
		// that scores ~0 forever and makes every subsequent Save throw on its length check.
		var record = Record(9);
		var store = new DenseEmbeddingStore();
		store.Put(@"D:\media\t.mp4", record);
		store.Save(keepOnly: null);

		string path = DenseEmbeddingStore.StorePath;
		byte[] bytes = File.ReadAllBytes(path);
		File.WriteAllBytes(path, bytes.AsSpan(0, bytes.Length - EmbeddingMath.Dimensions / 2).ToArray());

		var loaded = DenseEmbeddingStore.Load();
		Assert.Equal(0, loaded.Count);

		// And the rebuilt store persists fine afterwards.
		loaded.Put(@"D:\media\t.mp4", record);
		loaded.Save(keepOnly: null);
		Assert.Equal(1, DenseEmbeddingStore.Load().Count);
	}

	[Fact]
	public void MissingFile_LoadsAsEmptyStore() {
		Assert.Equal(0, DenseEmbeddingStore.Load().Count);
	}
}
