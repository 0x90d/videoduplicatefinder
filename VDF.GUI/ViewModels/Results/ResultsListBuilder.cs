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
using VDF.Core.Utils;

namespace VDF.GUI.ViewModels {

	/// <summary>Inputs of one flattened-list build. Only <see cref="Items"/> is required.</summary>
	public sealed record ResultsBuildRequest {
		public required IReadOnlyList<DuplicateItemVM> Items { get; init; }
		/// <summary>Per-item visibility (the results filter). Null shows everything.</summary>
		public Func<DuplicateItemVM, bool>? Filter { get; init; }
		public ResultsSortMode SortMode { get; init; } = ResultsSortMode.WastedSpace;
		public bool SortDescending { get; init; } = true;
		/// <summary>Groups whose member rows are omitted (header only).</summary>
		public IReadOnlySet<Guid>? CollapsedGroups { get; init; }
		/// <summary>Items whose details panel is expanded (a details row follows their row).</summary>
		public IReadOnlySet<DuplicateItemVM>? ExpandedDetails { get; init; }
		/// <summary>
		/// Picks the member the BEST badge goes to, plus the badge's tooltip text
		/// (which criterion decided, #839). Null: no badges.
		/// </summary>
		public Func<IReadOnlyList<DuplicateItemVM>, (DuplicateItemVM? Best, string? Tooltip)>? PickBest { get; init; }
		/// <summary>Tombstone test, replaceable for tests. Defaults to <see cref="DuplicateItemVM.IsTombstone"/>.</summary>
		public Func<DuplicateItemVM, bool>? IsTombstone { get; init; }
		/// <summary>Offline test, replaceable for tests. Defaults to <see cref="DuplicateItemVM.IsOffline"/>.</summary>
		public Func<DuplicateItemVM, bool>? IsOffline { get; init; }
		public GroupSummaryFormats Formats { get; init; } = GroupSummaryFormats.Default;
	}

	public sealed class ResultsBuildResult {
		public required List<object> Rows { get; init; }
		public required List<ResultsGroupHeader> Groups { get; init; }
		/// <summary>Any visible member is a partial clip — drives the Clip offset column.</summary>
		public bool HasPartialClips { get; init; }
	}

	/// <summary>
	/// Pure builder that turns the flat duplicates collection into the list the results view
	/// renders: group headers followed by their member rows, filtered and sorted. No UI types,
	/// no side effects — everything here is unit-testable.
	/// </summary>
	public static class ResultsListBuilder {

		public static ResultsBuildResult Build(ResultsBuildRequest request) {
			Func<DuplicateItemVM, bool> filter = request.Filter ?? (_ => true);
			Func<DuplicateItemVM, bool> isTombstone = request.IsTombstone ?? (d => d.IsTombstone);
			Func<DuplicateItemVM, bool> isOffline = request.IsOffline ?? (d => d.IsOffline);

			// Group in first-appearance order so ties keep a stable, predictable order.
			var groupsById = new Dictionary<Guid, List<DuplicateItemVM>>();
			var groupOrder = new List<Guid>();
			foreach (var item in request.Items) {
				if (!filter(item)) continue;
				if (!groupsById.TryGetValue(item.ItemInfo.GroupId, out var members)) {
					groupsById[item.ItemInfo.GroupId] = members = new List<DuplicateItemVM>();
					groupOrder.Add(item.ItemInfo.GroupId);
				}
				members.Add(item);
			}

			var headers = new List<ResultsGroupHeader>(groupOrder.Count);
			foreach (var gid in groupOrder) {
				var members = groupsById[gid];
				SortMembers(members, request.SortMode, request.SortDescending);

				long total = 0, largest = 0;
				float simMin = float.MaxValue, simMax = float.MinValue;
				int onDisk = 0;
				bool hasTombstone = false, hasOffline = false, hasChecked = false;
				foreach (var m in members) {
					long size = Math.Max(0, m.ItemInfo.SizeLong);
					total += size;
					if (size > largest) largest = size;
					if (m.ItemInfo.Similarity < simMin) simMin = m.ItemInfo.Similarity;
					if (m.ItemInfo.Similarity > simMax) simMax = m.ItemInfo.Similarity;
					bool tomb = isTombstone(m);
					bool off = !tomb && isOffline(m);
					hasTombstone |= tomb;
					hasOffline |= off;
					if (!tomb && !off) onDisk++;
					hasChecked |= m.Checked;
				}

				var rows = members.Select(m => new ResultsItemRow(m)).ToList();
				var header = new ResultsGroupHeader {
					GroupId = gid,
					Rows = rows,
					TotalBytes = total,
					WastedBytes = total - largest,
					SimilarityMin = simMin == float.MaxValue ? 0 : simMin,
					SimilarityMax = simMax == float.MinValue ? 0 : simMax,
					HasTombstone = hasTombstone,
					HasOffline = hasOffline,
					OnDiskCount = onDisk,
					HasCheckedItems = hasChecked,
					IsCollapsed = request.CollapsedGroups?.Contains(gid) == true,
				};
				foreach (var row in rows)
					row.Group = header;

				if (request.PickBest != null && members.Count >= 2) {
					var (best, tooltip) = request.PickBest(members);
					if (best != null)
						foreach (var row in rows) {
							row.IsBest = ReferenceEquals(row.Item, best);
							row.BestTooltip = row.IsBest ? tooltip : null;
						}
				}

				// HDR chip highlight: green only when this member's format outranks
				// another member of the SAME group (mixed dynamic ranges).
				int minHdrRank = int.MaxValue;
				foreach (var m in members)
					if (m.ItemInfo.HdrFormatRank < minHdrRank) minHdrRank = m.ItemInfo.HdrFormatRank;
				foreach (var row in rows)
					row.HdrIsUpgrade = row.Item.ItemInfo.HdrFormatRank > minHdrRank;

				headers.Add(header);
			}

			SortGroups(headers, request.SortMode, request.SortDescending);

			bool hasPartialClips = false;
			var flat = new List<object>();
			for (int i = 0; i < headers.Count; i++) {
				var header = headers[i];
				header.GroupNumber = i + 1;
				header.Title = string.Format(request.Formats.GroupTitle, header.GroupNumber);
				header.Summary = BuildSummary(header, request.Formats);
				flat.Add(header);
				foreach (var row in header.Rows) {
					hasPartialClips |= row.Item.ItemInfo.Flags.HasFlag(Core.DuplicateFlags.PartialClip);
					if (!header.IsCollapsed) {
						flat.Add(row);
						if (request.ExpandedDetails?.Contains(row.Item) == true)
							flat.Add(new ResultsDetailsRow(row));
					}
				}
			}

			return new ResultsBuildResult { Rows = flat, Groups = headers, HasPartialClips = hasPartialClips };
		}

		/// <summary>
		/// Composes the header info line, e.g. "3 files · 1.9 GB · save up to 1.2 GB" or
		/// "2 files · 1 on disk · previously deleted content" for tombstone groups.
		/// </summary>
		internal static string BuildSummary(ResultsGroupHeader header, GroupSummaryFormats formats) {
			var parts = new List<string> {
				header.FileCount == 1 ? formats.SingleFile : string.Format(formats.Files, header.FileCount),
				header.TotalBytes.BytesToString()
			};
			if (header.WastedBytes > 0)
				parts.Add(string.Format(formats.SaveUpTo, header.WastedBytes.BytesToString()));
			if (header.OnDiskCount < header.FileCount)
				parts.Add(string.Format(formats.OnDisk, header.OnDiskCount));
			if (header.HasTombstone)
				parts.Add(formats.PreviouslyDeleted);
			return string.Join(" · ", parts);
		}

		static void SortMembers(List<DuplicateItemVM> members, ResultsSortMode mode, bool descending) {
			Comparison<DuplicateItemVM>? comparison = mode switch {
				ResultsSortMode.WastedSpace or ResultsSortMode.TotalSize or ResultsSortMode.LargestFile =>
					(a, b) => a.ItemInfo.SizeLong.CompareTo(b.ItemInfo.SizeLong),
				ResultsSortMode.Similarity => (a, b) => a.ItemInfo.Similarity.CompareTo(b.ItemInfo.Similarity),
				ResultsSortMode.DateCreated => (a, b) => a.ItemInfo.DateCreated.CompareTo(b.ItemInfo.DateCreated),
				ResultsSortMode.Duration => (a, b) => a.ItemInfo.Duration.CompareTo(b.ItemInfo.Duration),
				ResultsSortMode.FolderPath => (a, b) => string.Compare(a.ItemInfo.Path, b.ItemInfo.Path, StringComparison.OrdinalIgnoreCase),
				// FileCount / GroupsWithCheckedItems have no meaningful member dimension.
				_ => null,
			};
			if (comparison == null) return;
			if (descending) {
				var inner = comparison;
				comparison = (a, b) => inner(b, a);
			}
			// List.Sort is unstable; a manual insertion-style stable sort is overkill here,
			// so sort an index-decorated copy to keep equal members in original order.
			var decorated = members.Select((m, i) => (m, i)).ToList();
			decorated.Sort((x, y) => {
				int c = comparison(x.m, y.m);
				return c != 0 ? c : x.i.CompareTo(y.i);
			});
			for (int i = 0; i < decorated.Count; i++)
				members[i] = decorated[i].m;
		}

		static void SortGroups(List<ResultsGroupHeader> headers, ResultsSortMode mode, bool descending) {
			Comparison<ResultsGroupHeader> comparison = mode switch {
				ResultsSortMode.WastedSpace => (a, b) => a.WastedBytes.CompareTo(b.WastedBytes),
				ResultsSortMode.TotalSize => (a, b) => a.TotalBytes.CompareTo(b.TotalBytes),
				ResultsSortMode.LargestFile => (a, b) => MaxSize(a).CompareTo(MaxSize(b)),
				ResultsSortMode.FileCount => (a, b) => a.FileCount.CompareTo(b.FileCount),
				ResultsSortMode.Similarity => (a, b) => a.SimilarityMax.CompareTo(b.SimilarityMax),
				ResultsSortMode.DateCreated => (a, b) => MaxDate(a).CompareTo(MaxDate(b)),
				ResultsSortMode.Duration => (a, b) => MaxDuration(a).CompareTo(MaxDuration(b)),
				ResultsSortMode.FolderPath => (a, b) => string.Compare(FirstPath(a), FirstPath(b), StringComparison.OrdinalIgnoreCase),
				ResultsSortMode.GroupsWithCheckedItems => (a, b) => a.HasCheckedItems.CompareTo(b.HasCheckedItems),
				_ => (a, b) => 0,
			};
			if (descending) {
				var inner = comparison;
				comparison = (a, b) => inner(b, a);
			}
			var decorated = headers.Select((h, i) => (h, i)).ToList();
			decorated.Sort((x, y) => {
				int c = comparison(x.h, y.h);
				return c != 0 ? c : x.i.CompareTo(y.i);
			});
			for (int i = 0; i < decorated.Count; i++)
				headers[i] = decorated[i].h;
		}

		static long MaxSize(ResultsGroupHeader h) {
			long max = 0;
			foreach (var row in h.Rows)
				if (row.Item.ItemInfo.SizeLong > max) max = row.Item.ItemInfo.SizeLong;
			return max;
		}
		static DateTime MaxDate(ResultsGroupHeader h) {
			DateTime max = DateTime.MinValue;
			foreach (var row in h.Rows)
				if (row.Item.ItemInfo.DateCreated > max) max = row.Item.ItemInfo.DateCreated;
			return max;
		}
		static TimeSpan MaxDuration(ResultsGroupHeader h) {
			TimeSpan max = TimeSpan.Zero;
			foreach (var row in h.Rows)
				if (row.Item.ItemInfo.Duration > max) max = row.Item.ItemInfo.Duration;
			return max;
		}
		static string FirstPath(ResultsGroupHeader h) => h.Rows.Count > 0 ? h.Rows[0].Item.ItemInfo.Path : string.Empty;
	}
}
