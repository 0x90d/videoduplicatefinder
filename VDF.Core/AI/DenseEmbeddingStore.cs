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

using System.Linq;
using System.Text;
using VDF.Core.Utils;

namespace VDF.Core.AI {
	/// <summary>
	/// Sidecar cache for the dense keyframe embeddings the visual partial-duplicate pass
	/// uses (~25 KB per video). Deliberately NOT part of the main scan database: the data
	/// is bulky, only needed while that pass runs, and fully recomputable. Records are
	/// validated by file size + mtime, so an edited file re-extracts. Plain length-prefixed
	/// binary — no serializer dependency, trivially Native-AOT-safe.
	/// </summary>
	sealed class DenseEmbeddingStore {
		internal sealed record DenseRecord(long FileSize, long MTimeUtcTicks, float IntervalSeconds, byte[][] Frames);

		static ReadOnlySpan<byte> Magic => "VDFAI001"u8;
		const int MaxSaneFrameCount = 100_000;

		readonly Dictionary<string, DenseRecord> records = new(
			CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		readonly object writeLock = new();

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
					for (int i = 0; i < frameCount; i++)
						frames[i] = reader.ReadBytes(EmbeddingMath.Dimensions);
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

		internal void Put(string file, DenseRecord record) {
			lock (writeLock)
				records[file] = record;
		}

		/// <summary>
		/// Persists the cache, pruning records for files no longer in the scan set so the
		/// sidecar cannot grow without bound. Best-effort: failure only costs recompute time.
		/// </summary>
		internal void Save(IReadOnlySet<string>? keepOnly) {
			string path = StorePath;
			string tempPath = path + ".tmp";
			try {
				lock (writeLock) {
					if (keepOnly != null)
						foreach (string stale in records.Keys.Where(k => !keepOnly.Contains(k)).ToList())
							records.Remove(stale);
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
								if (frame.Length != EmbeddingMath.Dimensions)
									throw new InvalidOperationException($"Unexpected embedding length {frame.Length}");
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
