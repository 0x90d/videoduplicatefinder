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

namespace VDF.Core.Utils {
	/// <summary>
	/// Remembers the audio partial-clip comparison results of the last completed scan, so a
	/// rescan only compares pairs that involve a new or changed video. A pair's result is a
	/// pure function of both fingerprints, both durations, both paths (folder gate) and the
	/// settings below, so reusing it is exact whenever all of those are unchanged.
	/// A stale file (after a database cleanup or "clear database") is harmless for the same
	/// reason: every entry is looked up by path + fingerprint hash + length + duration, so an
	/// entry that changed or vanished is simply not covered and is compared afresh.
	/// Unlike the visual cache, a save keeps only the files of its own scan: "covered" means
	/// every pair among the covered files was compared, so carrying over a file that was
	/// absent from a scan would make its pairs with files added since look compared.
	/// </summary>
	internal sealed class PartialClipCache {
		/// <summary>
		/// Bump whenever <see cref="ScanEngine.SlidingWindowCompare"/>, the pair prefilters in
		/// <see cref="ScanEngine.ScanForPartialDuplicates"/> or the shared candidate loop change
		/// behaviour: cached results from the old behaviour would otherwise be reused.
		/// </summary>
		internal const int FormatVersion = 1;

		const int Magic = 0x43435056; // "VPCC"
		internal const string FileName = "PartialClipCache.bin";

		internal readonly record struct Identity(string Path, ulong FingerprintHash, int FingerprintLength, long DurationTicks);

		/// <summary>Everything besides the identities that decides which pairs are compared and what counts as a match.</summary>
		internal readonly record struct SettingsKey(float Threshold, double MinRatio, int FolderMatchMode, int SameFolderDepth);

		readonly bool[] covered;
		readonly Dictionary<long, (float sim, int offsetSec)> matches;

		PartialClipCache(bool[] covered, Dictionary<long, (float sim, int offsetSec)> matches) {
			this.covered = covered;
			this.matches = matches;
		}

		/// <summary>Never covers anything: every pair is computed.</summary>
		internal static PartialClipCache Empty(int count) => new(new bool[count], new());

		internal bool IsCovered(int index) => covered[index];

		internal bool TryGetMatch(int sourceIdx, int clipIdx, out (float sim, int offsetSec) match) =>
			matches.TryGetValue(PairKey(sourceIdx, clipIdx), out match);

		static long PairKey(int sourceIdx, int clipIdx) => ((long)sourceIdx << 32) | (uint)clipIdx;

		internal static string FilePath => System.IO.Path.Combine(DatabaseUtils.GetDatabaseFolderPath(), FileName);

		/// <summary>
		/// Deterministic (FNV-1a over the uints): string/HashCode hashes are randomized per
		/// process and would never match across runs.
		/// </summary>
		internal static ulong HashFingerprint(uint[] fingerprint) {
			ulong h = 14695981039346656037UL;
			foreach (uint v in fingerprint) {
				h ^= v;
				h *= 1099511628211UL;
			}
			return h;
		}

		internal static Identity[] BuildIdentities(List<FileEntry> videos) {
			var ids = new Identity[videos.Count];
			for (int i = 0; i < ids.Length; i++) {
				var fp = videos[i].AudioFingerprint!;
				ids[i] = new Identity(videos[i].Path, HashFingerprint(fp), fp.Length, (videos[i].mediaInfo?.Duration ?? TimeSpan.Zero).Ticks);
			}
			return ids;
		}

		/// <summary>
		/// Identities occurring more than once cannot be told apart, so they are never treated
		/// as covered (and never saved): their cached matches would be ambiguous.
		/// </summary>
		static Dictionary<Identity, int> UniqueIdentityIndex(Identity[] ids) {
			var index = new Dictionary<Identity, int>(ids.Length);
			var duplicates = new HashSet<Identity>();
			for (int i = 0; i < ids.Length; i++)
				if (!index.TryAdd(ids[i], i))
					duplicates.Add(ids[i]);
			foreach (var d in duplicates)
				index.Remove(d);
			return index;
		}

		/// <summary>
		/// Reads the cache and maps it onto <paramref name="current"/>. Any problem (missing,
		/// unreadable, other version, other settings) yields an empty cache and a reason.
		/// </summary>
		internal static PartialClipCache Load(string file, SettingsKey key, Identity[] current, out string status) {
			if (!File.Exists(file)) {
				status = "no cache file yet";
				return Empty(current.Length);
			}
			try {
				using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var r = new BinaryReader(stream);
				if (r.ReadInt32() != Magic || r.ReadInt32() != FormatVersion) {
					status = "cache file has a different version";
					return Empty(current.Length);
				}
				var stored = new SettingsKey(r.ReadSingle(), r.ReadDouble(), r.ReadInt32(), r.ReadInt32());
				if (stored != key) {
					status = "partial clip settings changed since the cache was written";
					return Empty(current.Length);
				}

				var currentIndex = UniqueIdentityIndex(current);
				int count = r.ReadInt32();
				// Each identity needs more than one byte, so a count beyond the file size is corrupt.
				if (count < 0 || count > stream.Length)
					throw new InvalidDataException("bad identity count");
				var mapped = new int[count]; // cached identity index -> current index, -1 when gone
				var covered = new bool[current.Length];
				for (int k = 0; k < count; k++) {
					var id = new Identity(r.ReadString(), r.ReadUInt64(), r.ReadInt32(), r.ReadInt64());
					if (currentIndex.TryGetValue(id, out int idx)) {
						mapped[k] = idx;
						covered[idx] = true;
					}
					else
						mapped[k] = -1;
				}

				int matchCount = r.ReadInt32();
				if (matchCount < 0 || matchCount > stream.Length)
					throw new InvalidDataException("bad match count");
				var matches = new Dictionary<long, (float sim, int offsetSec)>();
				for (int m = 0; m < matchCount; m++) {
					int si = r.ReadInt32(), ci = r.ReadInt32();
					float sim = r.ReadSingle();
					int offsetSec = r.ReadInt32();
					if ((uint)si >= (uint)count || (uint)ci >= (uint)count)
						throw new InvalidDataException("bad match index");
					if (mapped[si] >= 0 && mapped[ci] >= 0)
						matches[PairKey(mapped[si], mapped[ci])] = (sim, offsetSec);
				}
				status = "loaded";
				return new PartialClipCache(covered, matches);
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or FormatException) {
				status = "cache file is unreadable";
				return Empty(current.Length);
			}
		}

		/// <summary>
		/// Writes the cache atomically (temp file, then move over the old one). The covered set
		/// is <paramref name="current"/>, the matches are this run's candidate matches before
		/// the visual gate, as indices into <paramref name="current"/>.
		/// </summary>
		internal static void Save(string file, SettingsKey key, Identity[] current,
			IEnumerable<(int sourceIdx, int clipIdx, float sim, int offsetSec)> matches) {
			var unique = UniqueIdentityIndex(current);
			var savedIndex = new int[current.Length]; // current index -> position in the file, -1 when not saved
			var saved = new List<Identity>(unique.Count);
			for (int i = 0; i < current.Length; i++) {
				if (unique.TryGetValue(current[i], out int idx) && idx == i) {
					savedIndex[i] = saved.Count;
					saved.Add(current[i]);
				}
				else
					savedIndex[i] = -1;
			}

			string temp = file + ".tmp";
			try {
				using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
				using (var w = new BinaryWriter(stream)) {
					w.Write(Magic);
					w.Write(FormatVersion);
					w.Write(key.Threshold);
					w.Write(key.MinRatio);
					w.Write(key.FolderMatchMode);
					w.Write(key.SameFolderDepth);
					w.Write(saved.Count);
					foreach (var id in saved) {
						w.Write(id.Path);
						w.Write(id.FingerprintHash);
						w.Write(id.FingerprintLength);
						w.Write(id.DurationTicks);
					}
					var kept = matches.Where(m => savedIndex[m.sourceIdx] >= 0 && savedIndex[m.clipIdx] >= 0).ToList();
					w.Write(kept.Count);
					foreach (var m in kept) {
						w.Write(savedIndex[m.sourceIdx]);
						w.Write(savedIndex[m.clipIdx]);
						w.Write(m.sim);
						w.Write(m.offsetSec);
					}
				}
				File.Move(temp, file, overwrite: true);
			}
			catch (Exception ex) {
				Logger.Instance.Warn($"Partial clip detection: could not save the comparison cache: {ex.Message}");
				try { File.Delete(temp); } catch (Exception) { }
			}
		}
	}
}
