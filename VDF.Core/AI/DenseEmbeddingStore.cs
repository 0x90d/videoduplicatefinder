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
	/// Sidecar cache for the dense keyframe embeddings the visual partial-duplicate pass
	/// uses (~25 KB per video). Deliberately NOT part of the main scan database: the data
	/// is bulky, only needed while that pass runs, and fully recomputable. Records are
	/// validated by file size + mtime, so an edited file re-extracts. Plain length-prefixed
	/// binary — no serializer dependency, trivially Native-AOT-safe. A frame slot may be
	/// EMPTY (length 0): the frame exists on the timeline but was excluded from matching
	/// (too dark, or a duplicate of its predecessor) — the slot keeps the index↔time
	/// mapping intact.
	/// </summary>
	sealed class DenseEmbeddingStore {
		internal sealed record DenseRecord(long FileSize, long MTimeUtcTicks, float IntervalSeconds, byte[][] Frames);

		// VDFAI003: v2 layout with a per-frame validity flag (VDFAI001 had none and
		// never shipped; VDFAI002 is the union store's magic).
		static ReadOnlySpan<byte> Magic => "VDFAI003"u8;
		const int MaxSaneFrameCount = 100_000;

		// Concurrent: the sampling phase's Parallel.For has workers probing (TryGet) while
		// others insert freshly computed records (Put) — a plain Dictionary would race.
		readonly ConcurrentDictionary<string, DenseRecord> records = new(
			CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		readonly object saveLock = new();

		internal static string StorePath =>
			TestOverrideStorePath ??
			FileUtils.SafePathCombine(CoreUtils.ResolveDatabaseFolder(Utils.DatabaseUtils.CustomDatabaseFolder), "DenseEmbeddings.db");

		/// <summary>Test hook: isolates store tests from the real database folder.</summary>
		internal static string? TestOverrideStorePath;

		internal int Count => records.Count;

		internal static DenseEmbeddingStore Load() {
			var store = new DenseEmbeddingStore();
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
					float interval = reader.ReadSingle();
					int frameCount = reader.ReadInt32();
					if (interval <= 0 || frameCount < 0 || frameCount > MaxSaneFrameCount)
						throw new IOException("implausible record header");
					var frames = new byte[frameCount][];
					for (int i = 0; i < frameCount; i++) {
						byte validity = reader.ReadByte();
						if (validity == 0) {
							frames[i] = Array.Empty<byte>();
							continue;
						}
						if (validity != 1)
							throw new IOException("bad frame validity flag");
						frames[i] = reader.ReadBytes(EmbeddingMath.Dimensions);
						// ReadBytes returns a SHORT array at EOF instead of throwing — a file
						// truncated inside a frame must count as corruption, or the poisoned
						// record would be served forever (size/mtime still match) and every
						// later Save would fail on its length check.
						if (frames[i].Length != EmbeddingMath.Dimensions)
							throw new IOException("truncated record");
					}
					store.records[file] = new DenseRecord(size, mtime, interval, frames);
				}
			}
			catch (Exception e) {
				// The cache is recomputable — a torn or corrupt file must never block a scan.
				Logger.Instance.Warn($"Dense embedding cache unreadable, rebuilding ({e.Message}): {path}");
				store.records.Clear();
			}
			return store;
		}

		internal bool TryGet(string file, long fileSize, long mtimeUtcTicks, out DenseRecord record) {
			if (records.TryGetValue(file, out DenseRecord? found) &&
				found.FileSize == fileSize && found.MTimeUtcTicks == mtimeUtcTicks) {
				record = found;
				return true;
			}
			record = null!;
			return false;
		}

		internal void Put(string file, DenseRecord record) =>
			records[file] = record;

		/// <summary>
		/// Persists the cache atomically. <paramref name="keepOnly"/> should be every path
		/// known to the scan database — records are pruned only when their file left the
		/// database entirely, so alternating scans between different libraries (or scans
		/// with an offline drive) never throw cached embeddings away. Best-effort: failure
		/// only costs recompute time.
		/// </summary>
		internal void Save(IReadOnlySet<string>? keepOnly) {
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
						foreach ((string file, DenseRecord record) in records) {
							writer.Write(file);
							writer.Write(record.FileSize);
							writer.Write(record.MTimeUtcTicks);
							writer.Write(record.IntervalSeconds);
							writer.Write(record.Frames.Length);
							foreach (byte[] frame in record.Frames) {
								if (frame.Length == 0) {
									writer.Write((byte)0); // invalid slot (dark/duplicate frame)
									continue;
								}
								if (frame.Length != EmbeddingMath.Dimensions)
									throw new InvalidOperationException($"Unexpected embedding length {frame.Length}");
								writer.Write((byte)1);
								writer.Write(frame);
							}
						}
					}
					File.Move(tempPath, path, overwrite: true);
				}
			}
			catch (Exception e) {
				Logger.Instance.Warn($"Failed to save the dense embedding cache (will recompute next time): {e.Message}");
				try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
			}
		}
	}
}
