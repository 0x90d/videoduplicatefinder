// /*
//     Copyright (C) 2025 0x90d
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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace VDF.GUI.Data {
	public sealed class CustomSelectionData : ReactiveObject {
		bool _IgnoreGroupsWithSelectedItems = true;
		public bool IgnoreGroupsWithSelectedItems {
			get => _IgnoreGroupsWithSelectedItems;
			set => this.RaiseAndSetIfChanged(ref _IgnoreGroupsWithSelectedItems, value);
		}
		int _FileTypeSelection = 0;
		public int FileTypeSelection {
			get => _FileTypeSelection;
			set => this.RaiseAndSetIfChanged(ref _FileTypeSelection, value);
		}

		int _IdenticalSelection = 0;
		public int IdenticalSelection {
			get => _IdenticalSelection;
			set => this.RaiseAndSetIfChanged(ref _IdenticalSelection, value);
		}

		int _DateTimeSelection = 0;
		public int DateTimeSelection {
			get => _DateTimeSelection;
			set => this.RaiseAndSetIfChanged(ref _DateTimeSelection, value);
		}
		int _MinimumFileSize = 0;
		public int MinimumFileSize {
			get => _MinimumFileSize;
			set => this.RaiseAndSetIfChanged(ref _MinimumFileSize, value);
		}
		int _MaximumFileSize = 999999999;
		public int MaximumFileSize {
			get => _MaximumFileSize;
			set => this.RaiseAndSetIfChanged(ref _MaximumFileSize, value);
		}
		public ObservableCollection<string> PathContains { get; } = new();
		public ObservableCollection<string> PathNotContains { get; } = new();
		int _SimilarityFrom = 0;
		public int SimilarityFrom {
			get => _SimilarityFrom;
			set => this.RaiseAndSetIfChanged(ref _SimilarityFrom, value);
		}
		int _SimilarityTo = 100;
		public int SimilarityTo {
			get => _SimilarityTo;
			set => this.RaiseAndSetIfChanged(ref _SimilarityTo, value);
		}
	}
}
