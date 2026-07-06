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
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public partial class DuplicateResultsView : UserControl {
		public DuplicateResultsView() {
			AvaloniaXamlLoader.Load(this);
			DataContextChanged += (_, _) => WireViewModel();
			WireViewModel();
		}

		ListBox ResultsListControl => this.FindControl<ListBox>("ResultsList")!;

		/// <summary>The control keyboard shortcuts are attached to (see ApplyKeyboardShortcuts).</summary>
		internal ListBox ShortcutTarget => ResultsListControl;

		MainWindowVM? ViewModel => DataContext as MainWindowVM;

		void WireViewModel() {
			if (ViewModel is not MainWindowVM vm) return;
			vm.NewResultsSelectionProvider = () =>
				ResultsListControl.SelectedItems?.OfType<ResultsItemRow>().Select(r => r.Item).ToList() ?? new();
			vm.NewResultsSelectAndScrollTo = row => {
				ResultsListControl.SelectedItems?.Clear();
				ResultsListControl.SelectedItem = row;
				ResultsListControl.ScrollIntoView(row);
			};
		}

		// Group headers are rendered inside the same ListBox as file rows; they must never
		// count as "selected duplicates", so any header that sneaks into the selection
		// (marquee/range selection) is dropped again immediately.
		void OnResultsSelectionChanged(object? sender, SelectionChangedEventArgs e) {
			var selected = ResultsListControl.SelectedItems;
			if (selected == null) return;
			for (int i = selected.Count - 1; i >= 0; i--)
				if (selected[i] is ResultsGroupHeader)
					selected.RemoveAt(i);
		}

		// The DataGrid selected rows on right-click; ListBox doesn't. Mirror that behavior
		// so the context menu acts on the row under the cursor.
		void OnResultsPointerPressed(object? sender, PointerPressedEventArgs e) {
			if (!e.GetCurrentPoint(ResultsListControl).Properties.IsRightButtonPressed) return;
			if (e.Source is not Control source) return;
			var container = source.FindAncestorOfType<ListBoxItem>(includeSelf: true);
			if (container?.DataContext is not ResultsItemRow row) return;
			if (ResultsListControl.SelectedItems?.Contains(row) == true) return;
			ResultsListControl.SelectedItems?.Clear();
			ResultsListControl.SelectedItem = row;
		}

		void OnThumbnailDoubleTapped(object? sender, TappedEventArgs e) {
			ViewModel?.ThumbnailDoubleClickCommand.Execute().Subscribe();
			e.Handled = true;
		}

		// Click on the path line copies the full path (locked design decision 5).
		async void OnPathPointerPressed(object? sender, PointerPressedEventArgs e) {
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
			if ((sender as Control)?.DataContext is not ResultsItemRow row) return;
			if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
				await clipboard.SetTextAsync(row.Item.ItemInfo.Path);
		}

		void OnPreviewGripDragDelta(object? sender, VectorEventArgs e) {
			SettingsFile.Instance.ResultsPreviewWidth += e.Vector.X;
		}

		// Hover-diff: same contract as the classic grid — Tag carries the metric name.
		void OnMetricPointerEntered(object? sender, PointerEventArgs e) {
			if (sender is Border { Tag: string metric, DataContext: ResultsItemRow row })
				ViewModel?.SetHoveredMetric(row.Item, metric);
		}

		void OnMetricPointerExited(object? sender, PointerEventArgs e) {
			if (sender is Border { DataContext: ResultsItemRow row })
				ViewModel?.ClearHoveredMetric(row.Item);
		}
	}
}
