// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

		public FileTypeFilterOption[] TypeFilters { get; } = {
			new FileTypeFilterOption("All", FileTypeFilter.All),
			new FileTypeFilterOption("Videos", FileTypeFilter.Videos),
			new FileTypeFilterOption("Images", FileTypeFilter.Images),
		};

		FileTypeFilterOption _FileType;

		public FileTypeFilterOption FileType {
			get => _FileType;
			set {
				if (value.Name == _FileType.Name) return;
				_FileType = value;
				this.RaisePropertyChanged(nameof(FileType));
				RefreshResultsView();
			}
		}
		bool _FilterGroupsWithCheckedItems;
		public bool FilterGroupsWithCheckedItems {
			get => _FilterGroupsWithCheckedItems;
			set {
				if (value == _FilterGroupsWithCheckedItems) return;
				this.RaiseAndSetIfChanged(ref _FilterGroupsWithCheckedItems, value);
				RefreshResultsView();
			}
		}

		private HashSet<Guid> _groupsWithPathHit = new();
		void RebuildSearchPathIndex() {
			var needle = FilterByPath;
			if (string.IsNullOrEmpty(needle)) { _groupsWithPathHit.Clear(); return; }

			_groupsWithPathHit = Duplicates
				.Where(d => PathMatchesFilter(d.ItemInfo.Path, needle))
				.Select(d => d.ItemInfo.GroupId)
				.ToHashSet();
		}

		/// <summary>
		/// Substring match by default; when the needle contains * or ? it is treated
		/// as a wildcard pattern instead (unanchored, so "*season?\ep*" works without
		/// the user having to wrap it in stars themselves).
		/// </summary>
		internal static bool PathMatchesFilter(string path, string needle) {
			if (needle.IndexOfAny(['*', '?']) < 0)
				return path.Contains(needle, StringComparison.OrdinalIgnoreCase);
			string pattern = needle;
			if (!pattern.StartsWith('*')) pattern = "*" + pattern;
			if (!pattern.EndsWith('*')) pattern += "*";
			return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, path);
		}

		string _FilterByPath = string.Empty;
		public string FilterByPath {
			get => _FilterByPath;
			set {
				if (value == _FilterByPath) return;
				_FilterByPath = value;
				this.RaisePropertyChanged(nameof(FilterByPath));
			}
		}
		int _FilterSimilarityFrom = 0;
		public int FilterSimilarityFrom {
			get => _FilterSimilarityFrom;
			set {
				if (value == _FilterSimilarityFrom) return;
				this.RaiseAndSetIfChanged(ref _FilterSimilarityFrom, value);
				RefreshResultsView();
			}
		}
		int _FilterSimilarityTo = 100;
		public int FilterSimilarityTo {
			get => _FilterSimilarityTo;
			set {
				if (value == _FilterSimilarityTo) return;
				this.RaiseAndSetIfChanged(ref _FilterSimilarityTo, value);
				RefreshResultsView();
			}
		}

		/// <summary>The results filter; the view exposes it as always-active toolbar chips.</summary>
		internal bool DuplicatesFilterCore(DuplicateItemVM data) {
			bool ok = true;
			if (!string.IsNullOrEmpty(FilterByPath)) {
				ok = PathMatchesFilter(data.ItemInfo.Path, FilterByPath)
					 || _groupsWithPathHit.Contains(data.ItemInfo.GroupId);
			}

			if (ok && FileType.Value != FileTypeFilter.All)
				ok = FileType.Value == FileTypeFilter.Images ? data.ItemInfo.IsImage : !data.ItemInfo.IsImage;

			if (ok)
				ok = data.ItemInfo.Similarity >= FilterSimilarityFrom && data.ItemInfo.Similarity <= FilterSimilarityTo;

			if (ok && FilterGroupsWithCheckedItems)
				ok = GroupHasCheckedItems(data.ItemInfo.GroupId);

			data.IsVisibleInFilter = ok;
			return ok;
		}
	}
}
