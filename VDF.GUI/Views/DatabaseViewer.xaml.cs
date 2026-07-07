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
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public class DatabaseViewer : Window {
		readonly ListBox list;
		DatabaseViewerVM VM => (DatabaseViewerVM)DataContext!;

		public DatabaseViewer() {
			InitializeComponent();
			list = this.FindControl<ListBox>("dbList")!;
			var vm = new DatabaseViewerVM {
				SelectionProvider = () => list.SelectedItems?.OfType<DatabaseEntryVM>() ?? Enumerable.Empty<DatabaseEntryVM>(),
			};
			DataContext = vm;
			Owner = ApplicationHelpers.MainWindow;
			Closing += DatabaseViewer_Closing;
			if (!SettingsFile.Instance.DarkMode)
				RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

			var commandMap = new Dictionary<string, ICommand> {
				["DB_DeleteSelectedEntries"] = vm.DeleteSelectedEntries,
			};
			KeyboardShortcutManager.Instance.ApplyBindings(list, commandMap);
			list.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
		}

		private void DatabaseViewer_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
			=> VM.Save();

		void InitializeComponent() => AvaloniaXamlLoader.Load(this);

		public void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
			=> VM.SelectedCount = list.SelectedItems?.Count ?? 0;

		// F2 starts the explicit path edit on the focused row (same as ✎ / double-click)
		void OnListKeyDown(object? sender, KeyEventArgs e) {
			if (e.Key != Key.F2) return;
			if (list.SelectedItems?.OfType<DatabaseEntryVM>().FirstOrDefault() is { } entry) {
				entry.BeginPathEdit();
				e.Handled = true;
			}
		}

		public void OnListDoubleTapped(object? sender, TappedEventArgs e) {
			// Ignore double-taps on interactive children (chips, pencil, the editor itself)
			if (e.Source is Control c && c.FindAncestorOfType<Button>(includeSelf: true) != null) return;
			if (e.Source is Control t && t.FindAncestorOfType<TextBox>(includeSelf: true) != null) return;
			if ((e.Source as Control)?.DataContext is DatabaseEntryVM entry)
				entry.BeginPathEdit();
		}

		// The edit TextBox materializes when IsEditingPath flips — grab focus then.
		public void OnPathEditorAttached(object? sender, VisualTreeAttachmentEventArgs e) {
			if (sender is TextBox box)
				Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
		}

		public void OnPathEditorKeyDown(object? sender, KeyEventArgs e) {
			if (sender is not TextBox box || box.DataContext is not DatabaseEntryVM entry) return;
			if (e.Key == Key.Enter) {
				// Moving focus pushes the pending text through the LostFocus binding,
				// which also fires CommitPathEdit below.
				list.Focus();
				e.Handled = true;
			}
			else if (e.Key == Key.Escape) {
				entry.CancelPathEdit();
				list.Focus();
				e.Handled = true;
			}
		}

		public void OnPathEditorLostFocus(object? sender, RoutedEventArgs e) {
			if (sender is TextBox box && box.DataContext is DatabaseEntryVM { IsEditingPath: true } entry)
				entry.CommitPathEdit();
		}
	}
}
