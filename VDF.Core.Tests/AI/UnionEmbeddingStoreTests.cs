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

public class UnionEmbeddingStoreTests : IDisposable {
	readonly string tempDir;

	public UnionEmbeddingStoreTests() {
		tempDir = Path.Combine(Path.GetTempPath(), $"vdf_union_store_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		UnionEmbeddingStore.TestOverrideStorePath = Path.Combine(tempDir, "UnionEmbeddings.db");
	}

	public void Dispose() {
		UnionEmbeddingStore.TestOverrideStorePath = null;
		try { Directory.Delete(tempDir, true); } catch { }
	}

	static FileEntry Entry(string name, long fileSize = 1000, long mtimeTicks = 700_000_000_000L) {
		var entry = new FileEntry {
			Folder = @"D:\media",
			FileSize = fileSize,
			DateModified = new DateTime(mtimeTicks, DateTimeKind.Utc),
		};
		entry.Path = $@"D:\media\{name}";
		return entry;
	}

	static byte[] Embedding(int seed) {
		var rng = new Random(seed);
		var e = new byte[EmbeddingMath.Dimensions];
		rng.NextBytes(e);
		return e;
	}

	[Fact]
	public void PutAndGet_RoundTripsThroughSaveAndLoad() {
		var entry = Entry("a.mp4");
		byte[] e1 = Embedding(1);
		byte[] e2 = Embedding(2);

		var store = new UnionEmbeddingStore();
		Assert.False(store.HasEmbedding(entry, 12.5));
		store.Put(entry, 12.5, e1);
		store.Put(entry, 37.5, e2);
		Assert.True(store.HasEmbedding(entry, 12.5));
		store.Save(keepOnly: null);

		var loaded = UnionEmbeddingStore.Load();
		Assert.Equal(1, loaded.Count);
		Assert.Equal(e1, loaded.GetEmbedding(entry, 12.5));
		Assert.Equal(e2, loaded.GetEmbedding(entry, 37.5));
		Assert.Null(loaded.GetEmbedding(entry, 99.9));
	}

	[Fact]
	public void ChangedFile_InvalidatesRecord_AndPutStartsFresh() {
		var original = Entry("b.mp4", fileSize: 1000);
		var store = new UnionEmbeddingStore();
		store.Put(original, 12.5, Embedding(1));

		// Same path, different size/mtime: the cached embedding no longer applies.
		var edited = Entry("b.mp4", fileSize: 2000);
		Assert.False(store.HasEmbedding(edited, 12.5));
		Assert.Null(store.GetEmbedding(edited, 12.5));

		// Writing for the edited file replaces the record instead of mixing versions.
		store.Put(edited, 20.0, Embedding(2));
		Assert.True(store.HasEmbedding(edited, 20.0));
		Assert.False(store.HasEmbedding(edited, 12.5));
		Assert.False(store.HasEmbedding(original, 12.5));
	}

	[Fact]
	public void Save_PrunesOnlyPathsAbsentFromKeepSet() {
		// Keep-set semantics: membership in the database, NOT file existence and NOT the
		// current scan's include list — none of these test paths exist on disk, yet the
		// kept record must survive.
		var keep = Entry("keep.mp4");
		var gone = Entry("gone.mp4");
		var store = new UnionEmbeddingStore();
		store.Put(keep, 12.5, Embedding(1));
		store.Put(gone, 12.5, Embedding(2));

		store.Save(new HashSet<string>(new[] { keep.Path }, StringComparer.OrdinalIgnoreCase));

		var loaded = UnionEmbeddingStore.Load();
		Assert.Equal(1, loaded.Count);
		Assert.NotNull(loaded.GetEmbedding(keep, 12.5));
		Assert.Null(loaded.GetEmbedding(gone, 12.5));
	}

	[Fact]
	public void Save_IsANoOpWhenNothingWasAdded() {
		// A compare-only run loads the store without adding anything — it must not
		// rewrite (or even create) the file.
		var store = UnionEmbeddingStore.Load();
		store.Save(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		Assert.False(File.Exists(UnionEmbeddingStore.StorePath));
	}

	[Fact]
	public void TruncatedStore_RebuildsEmptyAndCanSaveAgain() {
		// Regression: BinaryReader.ReadBytes returns a SHORT array at EOF without
		// throwing. A store truncated inside the last record's embedding bytes must be
		// treated as corrupt (rebuild), not served as a poisoned record that would also
		// make every subsequent Save throw on its length check.
		var entry = Entry("t.mp4");
		var store = new UnionEmbeddingStore();
		store.Put(entry, 12.5, Embedding(1));
		store.Save(keepOnly: null);

		string path = UnionEmbeddingStore.StorePath;
		byte[] bytes = File.ReadAllBytes(path);
		File.WriteAllBytes(path, bytes.AsSpan(0, bytes.Length - EmbeddingMath.Dimensions / 2).ToArray());

		var loaded = UnionEmbeddingStore.Load();
		Assert.Equal(0, loaded.Count);
		Assert.Null(loaded.GetEmbedding(entry, 12.5));

		// And the rebuilt store persists fine afterwards.
		loaded.Put(entry, 12.5, Embedding(3));
		loaded.Save(keepOnly: null);
		Assert.Equal(1, UnionEmbeddingStore.Load().Count);
	}

	[Fact]
	public void CorruptFile_LoadsAsEmptyStore() {
		File.WriteAllBytes(UnionEmbeddingStore.StorePath, new byte[] { 1, 2, 3, 4, 5 });
		Assert.Equal(0, UnionEmbeddingStore.Load().Count);
	}

	[Fact]
	public void ConcurrentProbesAndPuts_DoNotCorruptTheStore() {
		// The scan phase has parallel decode workers probing while the embedding worker
		// writes — hammer that pattern; ConcurrentDictionary semantics must hold.
		var store = new UnionEmbeddingStore();
		var entries = Enumerable.Range(0, 64).Select(i => Entry($"c{i}.mp4")).ToArray();
		byte[] embedding = Embedding(7);

		Parallel.For(0, 64 * 100, k => {
			FileEntry entry = entries[k % entries.Length];
			if (k % 3 == 0)
				store.Put(entry, k % 7, embedding);
			else {
				store.HasEmbedding(entry, k % 7);
				store.GetEmbedding(entry, k % 7);
			}
		});

		Assert.Equal(entries.Length, store.Count);
	}
}
