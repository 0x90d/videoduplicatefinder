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
using System.Linq;
using Avalonia;
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
			vm.ResultsAnchorProvider = TopmostVisibleRow;
			vm.ResultsScrollToRow = ScrollRowToTop;
		}

		/// <summary>Row whose realized container is topmost in the viewport (partially visible counts).</summary>
		object? TopmostVisibleRow() {
			if (resultsScrollViewer == null) return null;
			object? best = null;
			double bestTop = double.MaxValue;
			foreach (var container in ResultsListControl.GetRealizedContainers()) {
				if (container.TranslatePoint(new Point(0, 0), resultsScrollViewer) is not { } p) continue;
				if (p.Y + container.Bounds.Height <= 0) continue; // fully above the viewport
				if (p.Y < bestTop) {
					bestTop = p.Y;
					best = container.DataContext;
				}
			}
			return best;
		}

		/// <summary>
		/// Scrolls the row to the TOP of the viewport once the rebuilt list has a layout.
		/// ScrollIntoView alone only guarantees visibility (the row lands at whichever
		/// edge is closer), which still reads as "shuffled to a random place".
		/// </summary>
		void ScrollRowToTop(object row) {
			Avalonia.Threading.Dispatcher.UIThread.Post(() => {
				int index = ResultsListControl.Items.IndexOf(row);
				if (index < 0) return;
				ResultsListControl.ScrollIntoView(index);
				// ScrollIntoView realized the container; align its top edge after layout.
				Avalonia.Threading.Dispatcher.UIThread.Post(() => {
					if (resultsScrollViewer == null) return;
					var container = ResultsListControl.ContainerFromIndex(index);
					if (container?.TranslatePoint(new Point(0, 0), resultsScrollViewer) is not { } p) return;
					resultsScrollViewer.Offset = new Vector(
						resultsScrollViewer.Offset.X,
						Math.Max(0, resultsScrollViewer.Offset.Y + p.Y));
				}, Avalonia.Threading.DispatcherPriority.Loaded);
			}, Avalonia.Threading.DispatcherPriority.Loaded);
		}

		// Group headers are rendered inside the same ListBox as file rows; they must never
		// count as "selected duplicates", so any header that sneaks into the selection
		// (marquee/range selection) is dropped again immediately.
		readonly SelectionHeaderCleanup selectionHeaderCleanup = new();
		void OnResultsSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
			selectionHeaderCleanup.Run(ResultsListControl.SelectedItems);

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

		ScrollViewer? resultsScrollViewer;
		bool headerInsetHooked;

		// The header strip sits outside the list's scroll viewport, so whenever the
		// vertical scrollbar reserves width the right-docked row cells end left of
		// their headers (#837). Keep the header's usable width in lockstep with the
		// viewport instead of guessing a scrollbar width.
		void OnResultsListTemplateApplied(object? sender, TemplateAppliedEventArgs e) {
			resultsScrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
			if (resultsScrollViewer == null) return;
			resultsScrollViewer.PropertyChanged += (_, args) => {
				if (args.Property == ScrollViewer.ViewportProperty)
					SyncHeaderInset();
			};
			if (!headerInsetHooked && this.FindControl<Border>("ColumnHeaderStrip") is { } header) {
				headerInsetHooked = true;
				header.PropertyChanged += (_, args) => {
					if (args.Property == BoundsProperty)
						SyncHeaderInset();
				};
			}
			SyncHeaderInset();
		}

		void SyncHeaderInset() {
			if (resultsScrollViewer == null) return;
			var header = this.FindControl<Border>("ColumnHeaderStrip");
			var columns = this.FindControl<DockPanel>("HeaderColumns");
			if (header == null || columns == null) return;
			double viewport = resultsScrollViewer.Viewport.Width;
			if (viewport <= 0) return;
			// Header strip and ListBox share the same outer width and the same 6px
			// horizontal padding (strip padding vs. row Border padding), so whatever
			// outer width the viewport does NOT get is exactly the scroll chrome.
			double inset = Math.Max(0, header.Bounds.Width - viewport);
			if (Math.Abs(columns.Margin.Right - inset) > 0.5)
				columns.Margin = new Thickness(0, 0, inset, 0);
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
