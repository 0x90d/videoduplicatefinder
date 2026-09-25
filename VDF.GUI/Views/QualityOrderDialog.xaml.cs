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
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public partial class QualityOrderDialog : Window {
		public QualityOrderDialog() {
			AvaloniaXamlLoader.Load(this);
			DataContext = new QualityOrderVM();
			// Tunnel: the list handles Space itself (selection) before a bubbling handler sees it.
			this.FindControl<ListBox>("CriteriaListBox")!.AddHandler(KeyDownEvent, CriteriaListBox_KeyDown, RoutingStrategies.Tunnel);

			Owner = ApplicationHelpers.MainWindow;
			VDF.GUI.Utils.Appearance.Attach(this);
		}

		public QualityOrderVM ViewModel => (QualityOrderVM)DataContext!;

		void Ok_Click(object? sender, RoutedEventArgs e) {
			Close(ViewModel.Result);
		}

		// Space toggles the selected criterion: the boxes take no focus, the list is the one
		// Tab stop. Only Space is handled, never Tab.
		void CriteriaListBox_KeyDown(object? sender, KeyEventArgs e) {
			if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None) return;
			ViewModel.ToggleSelected();
			e.Handled = true;
		}

		void Cancel_Click(object? sender, RoutedEventArgs e) {
			Close();
		}
	}
}
