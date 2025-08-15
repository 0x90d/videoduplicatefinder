// Copyright (C) 2024 0x90d
// UI Dialog for selecting quality order for duplicate selection

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
