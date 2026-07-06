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

using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	/// <summary>
	/// The settings page (redesign stage 3): left section nav, option rows with
	/// always-visible descriptions, profile banner and cross-section search. All
	/// filter DECISIONS live in <see cref="SettingsSearch"/>; this class only maps
	/// them onto control visibility.
	/// </summary>
	public partial class SettingsView : UserControl {

		sealed record SectionInfo(Control Panel, TextBlock? Caption, string Id, string Label);

		readonly List<Border> navItems = new();
		readonly List<SectionInfo> sections = new();
		readonly List<TextBlock> subCaptions = new();
		readonly List<SettingsSearchSection> searchSections = new();
		readonly List<SettingsSearchRow> searchRows = new();
		// The seconds floor/cap rows of the duration group, folded behind "more".
		readonly HashSet<SettingRow> collapsedExtraRows = new();
		bool durationMoreExpanded;
		bool indexBuilt;
		string selectedSectionId = "Scanning";
		MainWindowVM? vm;

		public SettingsView() {
			AvaloniaXamlLoader.Load(this);

			var includes = this.FindControl<ListBox>("ListboxIncludelist")!;
			includes.AddHandler(DragDrop.DropEvent, (_, e) => DropFolders(e, SettingsFile.Instance.Includes));
			includes.AddHandler(DragDrop.DragOverEvent, OnDragOver);
			var blacklists = this.FindControl<ListBox>("ListboxBlacklist")!;
			blacklists.AddHandler(DragDrop.DropEvent, (_, e) => DropFolders(e, SettingsFile.Instance.Blacklists));
			blacklists.AddHandler(DragDrop.DragOverEvent, OnDragOver);

			DataContextChanged += (_, __) => HookViewModel();
			Loaded += (_, __) => {
				BuildIndex();
				UpdateVisibility();
			};
		}

		void HookViewModel() {
			if (vm != null)
				vm.PropertyChanged -= ViewModel_PropertyChanged;
			vm = DataContext as MainWindowVM;
			if (vm != null)
				vm.PropertyChanged += ViewModel_PropertyChanged;
		}

		void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
			if (e.PropertyName == nameof(MainWindowVM.SettingsSearchQuery))
				UpdateVisibility();
		}

		void BuildIndex() {
			if (indexBuilt) return;
			indexBuilt = true;

			foreach (var item in this.FindControl<StackPanel>("NavPanel")!.Children.OfType<Border>())
				navItems.Add(item);

			collapsedExtraRows.Add(this.FindControl<SettingRow>("RowDurationMin")!);
			collapsedExtraRows.Add(this.FindControl<SettingRow>("RowDurationMax")!);

			foreach (var panel in this.FindControl<StackPanel>("SectionsHost")!.Children.OfType<StackPanel>()) {
				if (panel.Tag is not string id) continue;
				var caption = panel.Children.OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("sectioncaption"));
				string label = navItems.FirstOrDefault(n => (string?)n.Tag == id)?.Child is TextBlock navText
					? navText.Text ?? id : id;
				sections.Add(new SectionInfo(panel, caption, id, label));
				// The section itself is found by its nav label only; rows and tagged
				// blocks carry their own text.
				searchSections.Add(new SettingsSearchSection(id, label));

				foreach (var descendant in panel.GetLogicalDescendants().OfType<Control>()) {
					if (descendant is TextBlock tb && tb.Classes.Contains("subcaption"))
						subCaptions.Add(tb);
					if (descendant is SettingRow row)
						searchRows.Add(new SettingsSearchRow(row, id, row.BuildSearchText()));
					else if (SettingsSearchMeta.GetText(descendant) is string meta)
						searchRows.Add(new SettingsSearchRow(descendant, id, BuildBlockSearchText(descendant, meta)));
				}
			}
		}

		/// <summary>A tagged block is searched by its keywords plus every static text inside it.</summary>
		static string BuildBlockSearchText(Control block, string meta) =>
			string.Join(' ', block.GetLogicalDescendants().OfType<TextBlock>()
				.Select(t => t.Text)
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Prepend(meta));

		void UpdateVisibility() {
			if (!indexBuilt) return;
			string? query = vm?.SettingsSearchQuery;
			var result = SettingsSearch.Apply(query, selectedSectionId, searchSections, searchRows);
			bool searching = result.IsSearchMode;

			foreach (var section in sections) {
				bool visible = result.VisibleSections.Contains(section.Id);
				section.Panel.IsVisible = visible;
				if (section.Caption != null)
					section.Caption.IsVisible = searching && visible;
			}

			foreach (var row in searchRows) {
				bool visible = result.VisibleRows.Contains(row.Handle);
				var control = (Control)row.Handle;
				if (control is SettingRow settingRow && collapsedExtraRows.Contains(settingRow))
					visible &= searching || durationMoreExpanded;
				control.IsVisible = visible;
			}

			// Group captions only make sense on the full section page.
			foreach (var caption in subCaptions)
				caption.IsVisible = !searching;

			// Hairline under every visible row except the last of its section (mockup).
			foreach (var group in searchRows.Where(r => r.Handle is SettingRow row && row.IsVisible)
					.GroupBy(r => r.SectionId)) {
				SettingRow? last = null;
				foreach (var entry in group) {
					var row = (SettingRow)entry.Handle;
					row.ShowSeparator = true;
					last = row;
				}
				if (last != null)
					last.ShowSeparator = false;
			}

			this.FindControl<TextBlock>("NoResultsText")!.IsVisible = searching && result.VisibleSections.Count == 0;
			this.FindControl<TextBlock>("HeaderTitle")!.Text = searching
				? App.Lang["Settings.SearchResults"]
				: sections.FirstOrDefault(s => s.Id == selectedSectionId)?.Label;

			foreach (var item in navItems)
				item.Classes.Set("on", !searching && (string?)item.Tag == selectedSectionId);
		}

		void OnNavItemPressed(object? sender, PointerPressedEventArgs e) {
			if ((sender as Border)?.Tag is not string id) return;
			selectedSectionId = id;
			if (vm != null && SettingsSearch.IsSearching(vm.SettingsSearchQuery))
				vm.SettingsSearchQuery = string.Empty; // triggers UpdateVisibility
			UpdateVisibility();
		}

		void OnDurationMoreClick(object? sender, RoutedEventArgs e) {
			durationMoreExpanded = !durationMoreExpanded;
			this.FindControl<Button>("DurationMoreLink")!.Content =
				App.Lang[durationMoreExpanded ? "Settings.Less" : "Settings.More"];
			UpdateVisibility();
		}

		void Thumbnails_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
			if (ApplicationHelpers.MainWindow != null && ApplicationHelpers.MainWindowDataContext != null)
				ApplicationHelpers.MainWindowDataContext.Thumbnails_ValueChanged(sender, e);
		}

		static void OnDragOver(object? sender, DragEventArgs e) {
			e.DragEffects &= DragDropEffects.Copy | DragDropEffects.Link;
			if (!e.DataTransfer.Contains(DataFormat.File))
				e.DragEffects = DragDropEffects.None;
		}

		static void DropFolders(DragEventArgs e, System.Collections.ObjectModel.ObservableCollection<string> target) {
			if (!e.DataTransfer.Contains(DataFormat.File)) return;
			foreach (var item in e.DataTransfer.GetItems(DataFormat.File) ?? Array.Empty<IDataTransferItem>()) {
				string? path = item.TryGetFile()?.TryGetLocalPath();
				if (!string.IsNullOrEmpty(path) && !target.Contains(path))
					target.Add(path);
			}
		}
	}
}
