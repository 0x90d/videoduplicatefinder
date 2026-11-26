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
using VDF.Core.FFTools;

namespace VDF.Core.Utils {
	/// <summary>
	/// Remembers the partial-clip visual check's similarity per pair between scans, so a rescan
	/// only decodes frames for pairs that involve a new or changed video. The audio pass hands
	/// the check nearly the same candidates every scan, and decoding them off a network share
	/// took most of a rescan.
	/// A pair's similarity is a pure function of both files, the clip's offset, how the frames
	/// were decoded and the settings below, so a stored value is reused only when all of those
	/// match. The similarity (not pass/fail) is stored, so changing the visual threshold needs
	/// no decoding. Only results with every sampled frame decoded on both sides are stored: a
	/// missing frame (timeout, read error) makes the result depend on the moment.
	/// Persistence follows DenseEmbeddingStore: concurrent puts, periodic checkpoints that never
	/// prune, a final save that drops pairs with a file that left the scan database or that this
	/// scan saw changed. Pairs of files outside the current scan are kept: they are keyed by both
	/// files' identities, so a scan of another folder set must not cost the first set its results.
	/// </summary>
	internal sealed class PartialClipVisualCache {
		/// <summary>
		/// Bump whenever the similarity of a pair changes for the same inputs: the sample times
		/// (<see cref="ScanEngine.PartialClipVisualSampleTimes"/>), the comparison in
		/// <see cref="ScanEngine.VerifyPartialClipVisually"/>, the gray-frame extraction
		/// (<see cref="FfmpegEngine.GetGrayFrames"/> and the gray path of GetThumbnail),
		/// <see cref="GrayBytesUtils.PercentageDifference"/> or the 32x32 pHash.
		/// </summary>
		internal const int FormatVersion = 1;

		const int Magic = 0x43565056; // "VPVC"
		internal const string FileName = "PartialClipVisualCache.bin";

		/// <summary>
		/// The audio identity plus size and modification time: the frames come from the video
		/// stream, which can change while the audio fingerprint stays the same.
		/// </summary>
		internal readonly record struct Identity(PartialClipCache.Identity Audio, long FileSize, long ModifiedTicks);

		/// <summary>Everything besides the two files and the offset that the similarity depends on.</summary>
		internal readonly record struct SettingsKey(bool UsePHashing, string DecoderBuild);

		internal readonly record struct PairKey(Identity Source, Identity Clip, int OffsetSec, FfmpegEngine.GrayFrameSource Decoder);

		readonly ConcurrentDictionary<PairKey, float> results;
		readonly object saveLock = new();

		PartialClipVisualCache(ConcurrentDictionary<PairKey, float> results) => this.results = results;

		internal static PartialClipVisualCache Empty() => new(new());

		internal int Count => results.Count;

		internal static string FilePath => System.IO.Path.Combine(DatabaseUtils.GetDatabaseFolderPath(), FileName);

		internal static Identity[] BuildIdentities(List<FileEntry> videos, PartialClipCache.Identity[] audio) {
			var ids = new Identity[videos.Count];
			for (int i = 0; i < ids.Length; i++)
				ids[i] = new Identity(audio[i], videos[i].FileSize, videos[i].DateModified.Ticks);
			return ids;
		}

		internal bool TryGet(PairKey key, out float visualSim) => results.TryGetValue(key, out visualSim);

		internal void Put(PairKey key, float visualSim) => results[key] = visualSim;

		/// <summary>
		/// Reads the cache. Any problem (missing, unreadable, other version, other settings)
		/// yields an empty cache and a reason.
		/// </summary>
		internal static PartialClipVisualCache Load(string file, SettingsKey key, out string status) {
			if (!File.Exists(file)) {
				status = "no cache file yet";
				return Empty();
			}
			try {
				using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
				using var r = new BinaryReader(stream);
				if (r.ReadInt32() != Magic || r.ReadInt32() != FormatVersion) {
					status = "cache file has a different version";
					return Empty();
				}
				var stored = new SettingsKey(r.ReadBoolean(), r.ReadString());
				if (stored != key) {
					status = stored.DecoderBuild != key.DecoderBuild
						? "FFmpeg changed since the cache was written"
						: "the visual comparison setting changed since the cache was written";
					return Empty();
				}

				int idCount = r.ReadInt32();
				// Each identity needs more than one byte, so a count beyond the file size is corrupt.
				if (idCount < 0 || idCount > stream.Length)
					throw new InvalidDataException("bad identity count");
				var ids = new Identity[idCount];
				for (int k = 0; k < idCount; k++)
					ids[k] = new Identity(new PartialClipCache.Identity(r.ReadString(), r.ReadUInt64(), r.ReadInt32(), r.ReadInt64()), r.ReadInt64(), r.ReadInt64());

				int count = r.ReadInt32();
				if (count < 0 || count > stream.Length)
					throw new InvalidDataException("bad result count");
				var results = new ConcurrentDictionary<PairKey, float>();
				for (int k = 0; k < count; k++) {
					int si = r.ReadInt32(), ci = r.ReadInt32(), offsetSec = r.ReadInt32();
					byte decoder = r.ReadByte();
					float sim = r.ReadSingle();
					if ((uint)si >= (uint)idCount || (uint)ci >= (uint)idCount || decoder > (byte)FfmpegEngine.GrayFrameSource.Process)
						throw new InvalidDataException("bad result entry");
					results[new PairKey(ids[si], ids[ci], offsetSec, (FfmpegEngine.GrayFrameSource)decoder)] = sim;
				}
				status = "loaded";
				return new PartialClipVisualCache(results);
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or FormatException) {
				status = "cache file is unreadable";
				return Empty();
			}
		}

		/// <summary>
		/// Writes the cache atomically (temp file, then move over the old one). With
		/// <paramref name="keepOnly"/> (database paths, compared with the set's own comparer),
		/// first drops every pair one of whose files is not in it; checkpoints pass null so an
		/// interrupted run never throws results away. <paramref name="current"/> (this run's files by
		/// path) also drops pairs stored for an earlier version of a file this run saw changed: they
		/// can never match again, and a file that is re-tagged or remuxed in place would keep adding them.
		/// </summary>
		internal void Save(string file, SettingsKey key, IReadOnlySet<string>? keepOnly,
			IReadOnlyDictionary<string, Identity>? current = null) {
			string temp = file + ".tmp";
			try {
				lock (saveLock) {
					if (keepOnly != null)
						foreach (var stale in results.Keys.Where(k => !keepOnly.Contains(k.Source.Audio.Path) || !keepOnly.Contains(k.Clip.Audio.Path)).ToList())
							results.TryRemove(stale, out _);
					if (current != null) {
						bool Outdated(Identity id) => current.TryGetValue(id.Audio.Path, out var now) && now != id;
						foreach (var stale in results.Keys.Where(k => Outdated(k.Source) || Outdated(k.Clip)).ToList())
							results.TryRemove(stale, out _);
					}
					var entries = results.ToArray();
					var idIndex = new Dictionary<Identity, int>();
					var ids = new List<Identity>();
					int IndexOf(Identity id) {
						if (!idIndex.TryGetValue(id, out int i)) {
							i = ids.Count;
							idIndex[id] = i;
							ids.Add(id);
						}
						return i;
					}
					var rows = entries.Select(e => (si: IndexOf(e.Key.Source), ci: IndexOf(e.Key.Clip), e.Key.OffsetSec, e.Key.Decoder, sim: e.Value)).ToList();

					using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
					using (var w = new BinaryWriter(stream)) {
						w.Write(Magic);
						w.Write(FormatVersion);
						w.Write(key.UsePHashing);
						w.Write(key.DecoderBuild);
						w.Write(ids.Count);
						foreach (var id in ids) {
							w.Write(id.Audio.Path);
							w.Write(id.Audio.FingerprintHash);
							w.Write(id.Audio.FingerprintLength);
							w.Write(id.Audio.DurationTicks);
							w.Write(id.FileSize);
							w.Write(id.ModifiedTicks);
						}
						w.Write(rows.Count);
						foreach (var row in rows) {
							w.Write(row.si);
							w.Write(row.ci);
							w.Write(row.OffsetSec);
							w.Write((byte)row.Decoder);
							w.Write(row.sim);
						}
					}
					File.Move(temp, file, overwrite: true);
				}
			}
			catch (Exception ex) {
				Logger.Instance.Warn($"Partial clip detection: could not save the visual check cache: {ex.Message}");
				try { File.Delete(temp); } catch (Exception) { }
			}
		}
	}
}
