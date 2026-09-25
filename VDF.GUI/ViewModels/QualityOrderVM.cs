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
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;

namespace VDF.GUI.ViewModels {
	// Key is the stored settings value ("Size"), Display the localized list entry
	// ("Size (smaller file wins)") - they must stay separate so renaming a label
	// never invalidates saved QualityCriteriaOrder settings.
	public sealed class QualityCriterionOption : ReactiveObject {
		public QualityCriterionOption(string key, string display, bool isEnabled) {
			Key = key;
			Display = display;
			_isEnabled = isEnabled;
		}
		public string Key { get; }
		public string Display { get; }
		bool _isEnabled;
		/// <summary>Unchecked criteria are skipped by the ranking (#885).</summary>
		public bool IsEnabled {
			get => _isEnabled;
			set {
				this.RaiseAndSetIfChanged(ref _isEnabled, value);
				this.RaisePropertyChanged(nameof(AccessibleName));
			}
		}
		/// <summary>The list item's name: the criterion and whether it is used, since the box inside takes no focus.</summary>
		public string AccessibleName => $"{Display}, {App.Lang[IsEnabled ? "QualityOrderDialog.Used" : "QualityOrderDialog.Ignored"]}";
		public override string ToString() => Display;
	}

	/// <summary>What the dialog hands back: the order, and the criteria switched off.</summary>
	public sealed record QualityOrderResult(List<string> Order, List<string> Disabled);

	public class QualityOrderVM : ReactiveObject {
		public ObservableCollection<QualityCriterionOption> CriteriaOrder { get; }

		public ReactiveCommand<int, Unit> MoveUpCommand { get; }
		public ReactiveCommand<int, Unit> MoveDownCommand { get; }

		public QualityOrderVM() {
			var saved = ApplicationHelpers.MainWindowDataContext.QualityCriteriaOrder;
			var disabled = Data.SettingsFile.Instance.QualityCriteriaDisabled;
			var merged = new List<string>(saved.Where(MainWindowVM.QualityCriteriaMap.ContainsKey));
			foreach (var name in MainWindowVM.QualityCriteriaMap.Keys)
				if (!merged.Contains(name))
					merged.Add(name);
			CriteriaOrder = new ObservableCollection<QualityCriterionOption>(merged.Select(key => ToOption(key, !disabled.Contains(key))));
			MoveUpCommand = ReactiveCommand.Create<int>(MoveUp);
			MoveDownCommand = ReactiveCommand.Create<int>(MoveDown);
			_selectedItem = CriteriaOrder[0];
		}

		static QualityCriterionOption ToOption(string key, bool isEnabled) {
			var lookupKey = $"QualityCriteria.{key}";
			var display = App.Lang[lookupKey];
			return new(key, display == lookupKey ? key : display, isEnabled);
		}

		public QualityOrderResult Result => new(
			CriteriaOrder.Select(o => o.Key).ToList(),
			CriteriaOrder.Where(o => !o.IsEnabled).Select(o => o.Key).ToList());

		public void ToggleSelected() {
			if (SelectedItem != null)
				SelectedItem.IsEnabled = !SelectedItem.IsEnabled;
		}

		QualityCriterionOption _selectedItem;
		public QualityCriterionOption SelectedItem {
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
