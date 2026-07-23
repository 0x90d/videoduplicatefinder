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

using System.Globalization;

namespace VDF.GUI.ViewModels {

	/// <summary>
	/// Header entry of the flattened results list: one per duplicate group, followed by its
	/// <see cref="ResultsItemRow"/> members (unless collapsed). Immutable — the list is
	/// rebuilt whenever grouping-relevant state changes.
	/// </summary>
	public sealed class ResultsGroupHeader {
		public required Guid GroupId { get; init; }
		/// <summary>1-based position in the current sort order, for the "Group N" title.</summary>
		public int GroupNumber { get; internal set; }
		public required IReadOnlyList<ResultsItemRow> Rows { get; init; }
		public int FileCount => Rows.Count;
		/// <summary>Sum of member sizes; missing files count as 0.</summary>
		public long TotalBytes { get; init; }
		/// <summary>Bytes freed if only the largest member were kept.</summary>
		public long WastedBytes { get; init; }
		public float SimilarityMin { get; init; }
		public float SimilarityMax { get; init; }
		/// <summary>Group contains a tombstone: content the user already deleted matched again.</summary>
		public bool HasTombstone { get; init; }
		/// <summary>Group contains a member on an unmounted drive.</summary>
		public bool HasOffline { get; init; }
		/// <summary>Members that are neither tombstones nor offline.</summary>
		public int OnDiskCount { get; init; }
		public bool HasCheckedItems { get; init; }
		/// <summary>Member rows are omitted from the flattened list while collapsed.</summary>
		public bool IsCollapsed { get; init; }
		/// <summary>A member was matched by the grayscale comparison (combined mode, #842).</summary>
		public bool HasGrayscaleMatches { get; init; }
		/// <summary>A member was matched by the pHash comparison (combined mode, #842).</summary>
		public bool HasPHashMatches { get; init; }

		/// <summary>Localized "Group N" title, set by the builder from the active formats.</summary>
		public string Title { get; internal set; } = string.Empty;
		/// <summary>Localized "3 files · 1.9 GB · save up to 1.2 GB" line, set by the builder.</summary>
		public string Summary { get; internal set; } = string.Empty;

		public string SimilarityRangeDisplay {
			get {
				string min = SimilarityMin.ToString("0.#", CultureInfo.CurrentCulture);
				string max = SimilarityMax.ToString("0.#", CultureInfo.CurrentCulture);
				return min == max ? $"{max} %" : $"{min}–{max} %";
			}
		}
	}

	/// <summary>Member entry of the flattened results list, wrapping the shared item VM.</summary>
	public sealed class ResultsItemRow {
		public ResultsItemRow(DuplicateItemVM item) => Item = item;

		public DuplicateItemVM Item { get; }
		public ResultsGroupHeader Group { get; internal set; } = null!;
		/// <summary>The member the quality ranker would keep; shown as the BEST badge.</summary>
		public bool IsBest { get; internal set; }
		/// <summary>BEST badge tooltip: which quality criterion decided (#839).</summary>
		public string? BestTooltip { get; internal set; }
		/// <summary>
		/// This member's HDR format beats at least one other member of the group — only
		/// then does the HDR chip turn green. In uniform groups (all HDR10, or all SDR)
		/// the chip stays neutral: there is nothing to win.
		/// </summary>
		public bool HdrIsUpgrade { get; internal set; }
	}

	/// <summary>
	/// Expanded per-file details panel, inserted directly after its owning row (Tier 2 of
	/// the metadata model: verbose facts that don't rank live here, not in columns).
	/// Text lines are precomputed here so the template binds plain strings.
	/// </summary>
	public sealed class ResultsDetailsRow {
		public ResultsDetailsRow(ResultsItemRow row) {
			Row = row;
			var culture = CultureInfo.CurrentCulture;
			VideoText = Data.ResultsBadgeRules.BuildVideoLine(row.Item.ItemInfo, culture);
			AudioText = Data.ResultsBadgeRules.BuildAudioLine(row.Item.ItemInfo, culture);
			FileText = Data.ResultsBadgeRules.BuildFileLine(row.Item.ItemInfo, culture);
		}
		public ResultsItemRow Row { get; }
		public DuplicateItemVM Item => Row.Item;
		public string VideoText { get; }
		public string AudioText { get; }
		public string FileText { get; }
		public bool HasAudio => AudioText.Length > 0;
		public bool IsImage => Item.ItemInfo.IsImage;
	}

	/// <summary>
	/// Localizable format fragments used by <see cref="ResultsListBuilder"/> for header text.
	/// Defaults are English; the VM substitutes translations at runtime, tests use defaults.
	/// </summary>
	public sealed record GroupSummaryFormats {
		public string GroupTitle { get; init; } = "Group {0}";
		public string Files { get; init; } = "{0} files";
		public string SingleFile { get; init; } = "1 file";
		public string SaveUpTo { get; init; } = "save up to {0}";
		public string OnDisk { get; init; } = "{0} on disk";
		public string PreviouslyDeleted { get; init; } = "previously deleted content";
		public static readonly GroupSummaryFormats Default = new();
	}
}
