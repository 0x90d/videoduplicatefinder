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

namespace VDF.GUI.Data {

	/// <summary>A settings section as the search sees it: nav id plus its own searchable
	/// text (nav label and extra keywords for pages that are not made of option rows).</summary>
	public sealed record SettingsSearchSection(string Id, string SearchText);

	/// <summary>One option row: the owning section and the text a query is matched
	/// against (title + description + tags). Handle is whatever the view uses per row.</summary>
	public sealed record SettingsSearchRow(object Handle, string SectionId, string SearchText);

	public sealed class SettingsSearchResult {
		internal SettingsSearchResult(bool isSearchMode, IReadOnlySet<object> visibleRows, IReadOnlySet<string> visibleSections) {
			IsSearchMode = isSearchMode;
			VisibleRows = visibleRows;
			VisibleSections = visibleSections;
		}
		/// <summary>False = normal navigation: only the selected section shows, with all its rows.</summary>
		public bool IsSearchMode { get; }
		public IReadOnlySet<object> VisibleRows { get; }
		public IReadOnlySet<string> VisibleSections { get; }
	}

	/// <summary>
	/// Pure filter logic behind the settings search box (redesign stage 3). A query
	/// matches option rows across ALL sections at once; a section whose own label or
	/// keywords match is shown whole. The view only applies the returned visibility.
	/// </summary>
	public static class SettingsSearch {

		public static bool IsSearching(string? query) => !string.IsNullOrWhiteSpace(query);

		/// <summary>Every whitespace-separated term must occur somewhere in the text.</summary>
		public static bool Matches(string? query, string searchText) =>
			!IsSearching(query) ||
			query!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
				.All(term => searchText.Contains(term, StringComparison.OrdinalIgnoreCase));

		public static SettingsSearchResult Apply(string? query, string selectedSectionId,
			IReadOnlyList<SettingsSearchSection> sections, IReadOnlyList<SettingsSearchRow> rows) {

			if (!IsSearching(query))
				return new SettingsSearchResult(false,
					rows.Select(r => r.Handle).ToHashSet(),
					new HashSet<string>(StringComparer.Ordinal) { selectedSectionId });

			var matchedSections = sections.Where(s => Matches(query, s.SearchText))
				.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
			var visibleRows = new HashSet<object>();
			var visibleSections = new HashSet<string>(matchedSections, StringComparer.Ordinal);
			foreach (var row in rows) {
				if (!matchedSections.Contains(row.SectionId) && !Matches(query, row.SearchText))
					continue;
				visibleRows.Add(row.Handle);
				visibleSections.Add(row.SectionId);
			}
			return new SettingsSearchResult(true, visibleRows, visibleSections);
		}
	}
}
