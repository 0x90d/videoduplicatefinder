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

using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using VDF.Core.Utils;

namespace VDF.Core.AI {
	/// <summary>
	/// Sidecar cache for the union pass's neural embeddings (384 bytes per sampled
	/// position, keyed like <see cref="FileEntry.grayBytes"/>). Deliberately NOT part of
	/// the main scan database: at library scale the data is bulky, only needed while AI
	/// matching is enabled, and fully recomputable — keeping it out of FileEntry means
	/// non-AI users pay nothing for it and a database from any VDF version loads
	/// unchanged. Records self-validate by file size + mtime, so an edited file simply
	/// re-embeds. Thread-safe: parallel decode workers probe while the single embedding
	/// worker writes.
	/// </summary>
	sealed class UnionEmbeddingStore {
		sealed class FileRecord {
			public FileRecord(long fileSize, long mtimeUtcTicks) {
				FileSize = fileSize;
				MTimeUtcTicks = mtimeUtcTicks;
			}
			public readonly long FileSize;
			public readonly long MTimeUtcTicks;
			public readonly ConcurrentDictionary<double, byte[]> Positions = new();
		}

		static ReadOnlySpan<byte> Magic => "VDFAI002"u8;
		const int MaxSanePositionCount = 10_000;

		readonly ConcurrentDictionary<string, FileRecord> records = new(
			CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		readonly object saveLock = new();
		volatile bool dirty;

		internal static string StorePath =>
			TestOverrideStorePath ??
			FileUtils.SafePathCombine(CoreUtils.ResolveDatabaseFolder(Utils.DatabaseUtils.CustomDatabaseFolder), "UnionEmbeddings.db");

		/// <summary>Test hook: isolates store tests from the real database folder.</summary>
		internal static string? TestOverrideStorePath;

		internal int Count => records.Count;

		internal bool HasEmbedding(FileEntry entry, double positionKey) =>
			TryGetCurrent(entry)?.Positions.ContainsKey(positionKey) == true;

		internal byte[]? GetEmbedding(FileEntry entry, double positionKey) =>
			TryGetCurrent(entry) is { } record && record.Positions.TryGetValue(positionKey, out byte[]? embedding)
				? embedding
				: null;

		internal void Put(FileEntry entry, double positionKey, byte[] embedding) {
			long size = entry.FileSize;
			long mtime = entry.DateModified.Ticks;
			FileRecord record = records.AddOrUpdate(entry.Path,
				_ => new FileRecord(size, mtime),
				(_, existing) => existing.FileSize == size && existing.MTimeUtcTicks == mtime
					? existing
					: new FileRecord(size, mtime)); // file changed since the record was written — start fresh
			record.Positions[positionKey] = embedding;
			dirty = true;
		}

		FileRecord? TryGetCurrent(FileEntry entry) =>
			records.TryGetValue(entry.Path, out FileRecord? record) &&
			record.FileSize == entry.FileSize && record.MTimeUtcTicks == entry.DateModified.Ticks
				? record
				: null;

		internal static UnionEmbeddingStore Load() {
			var store = new UnionEmbeddingStore();
			string path = StorePath;
			if (!File.Exists(path))
				return store;
			try {
				using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
				using var reader = new BinaryReader(stream, Encoding.UTF8);
				Span<byte> magic = stackalloc byte[Magic.Length];
				if (reader.Read(magic) != Magic.Length || !magic.SequenceEqual(Magic))
					throw new IOException("bad magic");
				while (stream.Position < stream.Length) {
					string file = reader.ReadString();
					long size = reader.ReadInt64();
					long mtime = reader.ReadInt64();
					int positionCount = reader.ReadInt32();
					if (positionCount < 0 || positionCount > MaxSanePositionCount)
						throw new IOException("implausible record header");
					var record = new FileRecord(size, mtime);
					for (int i = 0; i < positionCount; i++) {
						double key = reader.ReadDouble();
						byte[] embedding = reader.ReadBytes(EmbeddingMath.Dimensions);
						// ReadBytes returns a SHORT array at EOF instead of throwing — a file
						// truncated inside an embedding must count as corruption, or the
						// poisoned record would be served forever (size/mtime still match).
						if (embedding.Length != EmbeddingMath.Dimensions)
							throw new IOException("truncated record");
						record.Positions[key] = embedding;
					}
					store.records[file] = record;
				}
			}
			catch (Exception e) {
				// The cache is recomputable — a torn or corrupt file must never block a scan.
				Logger.Instance.Warn($"AI embedding cache unreadable, rebuilding ({e.Message}): {path}");
				store.records.Clear();
			}
			return store;
		}

		/// <summary>
		/// Persists the cache atomically. <paramref name="keepOnly"/> should be every path
		/// known to the scan database — records are pruned only when their file left the
		/// database entirely, so alternating scans between different libraries (or scans
		/// with an offline drive) never throw cached embeddings away. Best-effort: failure
		/// only costs recompute time. No-op when nothing was added since load.
		/// </summary>
		internal void Save(IReadOnlySet<string>? keepOnly) {
			if (!dirty)
				return;
			string path = StorePath;
			string tempPath = path + ".tmp";
			try {
				lock (saveLock) {
					if (keepOnly != null)
						foreach (string stale in records.Keys.Where(k => !keepOnly.Contains(k)).ToList())
							records.TryRemove(stale, out _);
					using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
					using (var writer = new BinaryWriter(stream, Encoding.UTF8)) {
						writer.Write(Magic);
						foreach ((string file, FileRecord record) in records) {
							// Snapshot the positions: the count must match what gets written.
							var positions = record.Positions.ToArray();
							writer.Write(file);
							writer.Write(record.FileSize);
							writer.Write(record.MTimeUtcTicks);
							writer.Write(positions.Length);
							foreach ((double key, byte[] embedding) in positions) {
								if (embedding.Length != EmbeddingMath.Dimensions)
									throw new InvalidOperationException($"Unexpected embedding length {embedding.Length}");
								writer.Write(key);
								writer.Write(embedding);
							}
						}
					}
					File.Move(tempPath, path, overwrite: true);
					dirty = false;
				}
			}
			catch (Exception e) {
				Logger.Instance.Warn($"Failed to save the AI embedding cache (will recompute next time): {e.Message}");
				try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
			}
		}
	}
}
