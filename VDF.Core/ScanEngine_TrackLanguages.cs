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

using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Core {
	// Track languages (#899) come from the ffprobe call every scan already makes, but the
	// database caches that probe: entries made before the language field existed never get
	// it on their own. Re-probing a whole library for it would cost one ffprobe per video,
	// so only the files that end up in the results are re-read, once, after the comparison.
	public sealed partial class ScanEngine {

		/// <summary>True when an audio or subtitle stream was probed before languages were stored.</summary>
		internal static bool NeedsTrackLanguages(MediaInfo? info) {
			if (info?.Streams == null)
				return false;
			foreach (var s in info.Streams) {
				if (s.Language == null && (s.CodecType?.Equals("audio", StringComparison.OrdinalIgnoreCase) == true ||
						s.CodecType?.Equals("subtitle", StringComparison.OrdinalIgnoreCase) == true))
					return true;
			}
			return false;
		}

		/// <summary>
		/// Copies the probed languages onto the cached streams by stream index (by position
		/// for entries without one); a cached stream the new probe doesn't have gets
		/// "no tag", so it is not asked again.
		/// </summary>
		internal static void MergeTrackLanguages(MediaInfo cached, MediaInfo probed) {
			for (int i = 0; i < cached.Streams.Length; i++) {
				var s = cached.Streams[i];
				if (s.Language != null)
					continue;
				MediaInfo.StreamInfo? match = null;
				if (s.Index != null) {
					foreach (var p in probed.Streams) {
						if (p.Index == s.Index) {
							match = p;
							break;
						}
					}
				}
				else if (i < probed.Streams.Length)
					match = probed.Streams[i];
				s.Language = match?.Language ?? string.Empty;
			}
		}

		void BackfillTrackLanguages() =>
			BackfillTrackLanguages(
				path => DatabaseUtils.Database.TryGetValue(new FileEntry(path), out var entry) ? entry : null,
				path => FFProbeEngine.GetMediaInfo(path, Settings.ExtendedFFToolsLogging),
				cancelationTokenSource.Token);

		/// <summary>
		/// Re-probes the result videos whose cached media info predates track languages and
		/// updates both the database entry and the result rows. Returns how many files were
		/// re-probed. A probe that fails leaves the entry as it was, to be tried next scan.
		/// </summary>
		internal int BackfillTrackLanguages(Func<string, FileEntry?> findEntry, Func<string, MediaInfo?> probe, CancellationToken cancellationToken) {
			var itemsByEntry = new Dictionary<FileEntry, List<DuplicateItem>>();
			foreach (var item in Duplicates) {
				if (item.IsImage)
					continue;
				var entry = findEntry(item.Path);
				if (entry == null || !NeedsTrackLanguages(entry.mediaInfo) || !File.Exists(entry.Path))
					continue;
				if (!itemsByEntry.TryGetValue(entry, out var items))
					itemsByEntry[entry] = items = new List<DuplicateItem>();
				items.Add(item);
			}
			if (itemsByEntry.Count == 0)
				return 0;

			Logger.Instance.Info($"Reading track languages of {itemsByEntry.Count} result file(s) scanned by an older version");
			InitProgress(itemsByEntry.Count, T("Scan.Stage.TrackLanguages"));
			int probed = 0;
			try {
				Parallel.ForEach(itemsByEntry, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = ParallelDegree }, pair => {
					var (entry, items) = (pair.Key, pair.Value);
					MediaInfo? info = probe(entry.Path);
					if (info != null) {
						MergeTrackLanguages(entry.mediaInfo!, info);
						foreach (var item in items)
							item.ApplyTrackLanguages(entry.mediaInfo!.Streams);
						Interlocked.Increment(ref probed);
					}
					IncrementProgress(entry.Path);
				});
			}
			catch (OperationCanceledException) { }
			return probed;
		}
	}
}
