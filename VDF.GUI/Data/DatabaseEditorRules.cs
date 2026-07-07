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

using System;
using System.Collections.Generic;
using System.Linq;
using VDF.Core;

namespace VDF.GUI.Data {

	enum DbTypeFilter { All, Videos, Images }

	enum DbSortMode { Path, Size, Created, Duration, Bitrate }

	/// <summary>Combined filter state of the database editor; all parts AND together.</summary>
	sealed record DbFilterState {
		public DbTypeFilter Type { get; init; } = DbTypeFilter.All;
		/// <summary>One-click "anything broken?" filter (any of the error flags).</summary>
		public bool ErrorsOnly { get; init; }
		/// <summary>Single-flag filter from the Flags menu or a clicked row chip.</summary>
		public EntryFlags? Flag { get; init; }
		public string Search { get; init; } = string.Empty;
	}

	/// <summary>
	/// Pure filter/sort/presentation rules of the database editor — no UI types, fully
	/// unit-testable. The VM delegates here.
	/// </summary>
	static class DatabaseEditorRules {

		internal const EntryFlags ErrorMask = EntryFlags.ThumbnailError | EntryFlags.MetadataError |
			EntryFlags.TooDark | EntryFlags.AudioFingerprintError;

		/// <summary>Flags surfaced as chips/filters, in display order.</summary>
		internal static readonly EntryFlags[] ChipFlags = {
			EntryFlags.ManuallyExcluded,
			EntryFlags.ThumbnailError,
			EntryFlags.MetadataError,
			EntryFlags.TooDark,
			EntryFlags.NoAudioTrack,
			EntryFlags.SilentAudioTrack,
			EntryFlags.ReparsePoint,
			EntryFlags.IsImage,
		};

		/// <summary>Chip severity: errors render red, excluded/too-dark amber, the rest neutral.</summary>
		internal static string ChipKind(EntryFlags flag) => flag switch {
			EntryFlags.ThumbnailError or EntryFlags.MetadataError or EntryFlags.AudioFingerprintError => "err",
			EntryFlags.ManuallyExcluded or EntryFlags.TooDark => "warn",
			_ => "neutral",
		};

		internal static bool Matches(FileEntry entry, DbFilterState filter) {
			bool isImage = entry.Flags.Any(EntryFlags.IsImage);
			if (filter.Type == DbTypeFilter.Videos && isImage) return false;
			if (filter.Type == DbTypeFilter.Images && !isImage) return false;
			if (filter.ErrorsOnly && !entry.Flags.Any(ErrorMask)) return false;
			if (filter.Flag is { } flag && !entry.Flags.Any(flag)) return false;
			if (filter.Search.Length > 0 &&
				!entry.Path.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)) return false;
			return true;
		}

		/// <summary>
		/// Sort comparison. <paramref name="flagFirst"/> replicates the old grid's per-flag
		/// column sort: flagged entries first, then the base mode; base modes sort path
		/// ascending, everything else largest/newest/longest first, path as tiebreaker.
		/// </summary>
		internal static Comparison<FileEntry> BuildComparison(DbSortMode mode, EntryFlags? flagFirst) {
			Comparison<FileEntry> baseComparison = mode switch {
				DbSortMode.Size => (a, b) => b.FileSize.CompareTo(a.FileSize),
				DbSortMode.Created => (a, b) => b.DateCreated.CompareTo(a.DateCreated),
				DbSortMode.Duration => (a, b) => GetDuration(b).CompareTo(GetDuration(a)),
				DbSortMode.Bitrate => (a, b) => GetBitRate(b).CompareTo(GetBitRate(a)),
				_ => (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase),
			};
			if (flagFirst is not { } flag)
				return Tiebreak(baseComparison);
			return Tiebreak((a, b) => {
				int c = b.Flags.Any(flag).CompareTo(a.Flags.Any(flag));
				return c != 0 ? c : baseComparison(a, b);
			});

			Comparison<FileEntry> Tiebreak(Comparison<FileEntry> inner) => (a, b) => {
				int c = inner(a, b);
				return c != 0 ? c : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
			};
		}

		internal static TimeSpan GetDuration(FileEntry entry) => entry.mediaInfo?.Duration ?? TimeSpan.Zero;

		internal static MediaInfo.StreamInfo? GetVideoStream(FileEntry entry) =>
			entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase))
			?? entry.mediaInfo?.Streams?.FirstOrDefault();

		internal static MediaInfo.StreamInfo? GetAudioStream(FileEntry entry) =>
			entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase));

		/// <summary>Video-stream bitrate in kb/s (the probe stores bits/s).</summary>
		internal static decimal GetBitRate(FileEntry entry) {
			long bits = GetVideoStream(entry)?.BitRate ?? 0;
			return bits <= 0 ? 0 : bits / 1000m;
		}

		/// <summary>Per-flag entry counts plus the any-error total, one pass.</summary>
		internal static (Dictionary<EntryFlags, int> PerFlag, int Errors) CountFlags(IEnumerable<FileEntry> entries) {
			var counts = ChipFlags.ToDictionary(f => f, _ => 0);
			int errors = 0;
			foreach (var entry in entries) {
				foreach (var flag in ChipFlags)
					if (entry.Flags.Any(flag))
						counts[flag]++;
				if (entry.Flags.Any(ErrorMask))
					errors++;
			}
			return (counts, errors);
		}
	}
}
