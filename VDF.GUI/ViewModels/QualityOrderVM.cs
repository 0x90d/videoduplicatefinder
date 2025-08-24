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
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;

namespace VDF.GUI.ViewModels {
	public class QualityOrderVM : ReactiveObject {
		public ObservableCollection<string> CriteriaOrder { get; }

		public ReactiveCommand<int, Unit> MoveUpCommand { get; }
		public ReactiveCommand<int, Unit> MoveDownCommand { get; }

		public QualityOrderVM() {
			CriteriaOrder = new ObservableCollection<string>(ApplicationHelpers.MainWindowDataContext.QualityCriteriaOrder);
			MoveUpCommand = ReactiveCommand.Create<int>(MoveUp);
			MoveDownCommand = ReactiveCommand.Create<int>(MoveDown);
			_selectedItem = CriteriaOrder[0];
		}
		private string _selectedItem;
		public string SelectedItem {
			get => _selectedItem;
			set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
		}
		public void MoveUp(int index) {
			if (index <= 0) return;
			var item = CriteriaOrder[index];
			CriteriaOrder.Move(index, index - 1);
			SelectedItem = item;
		}

		public void MoveDown(int index) {
			if (index >= 0 && index < CriteriaOrder.Count - 1) {
				var item = CriteriaOrder[index];
				CriteriaOrder.Move(index, index + 1);
				SelectedItem = item; 
			}
		}
	}
}
