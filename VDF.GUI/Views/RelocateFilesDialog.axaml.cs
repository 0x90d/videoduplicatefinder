using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using VDF.Core;
using VDF.GUI.ViewModels;

namespace VDF.GUI;

public partial class RelocateFilesDialog : Window
{
	public RelocateFilesDialog() {
		AvaloniaXamlLoader.Load(this);
		DataContext = new RelocateFilesDialogVM(this);
		Owner = ApplicationHelpers.MainWindow;
		if (!VDF.GUI.Data.SettingsFile.Instance.DarkMode)
			RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

		// Drag-and-drop folders onto the From/To boxes and the scan-roots list, mirroring the
		// include/blacklist lists in the main window (same DataFormat.File handling).
		var txtOld = this.FindControl<TextBox>("TxtOldPrefix")!;
		txtOld.AddHandler(DragDrop.DropEvent, DropOldPrefix);
		txtOld.AddHandler(DragDrop.DragOverEvent, DragOverFolder);
		var txtNew = this.FindControl<TextBox>("TxtNewPrefix")!;
		txtNew.AddHandler(DragDrop.DropEvent, DropNewPrefix);
		txtNew.AddHandler(DragDrop.DragOverEvent, DragOverFolder);
		var listRoots = this.FindControl<ListBox>("ListScanRoots")!;
		listRoots.AddHandler(DragDrop.DropEvent, DropScanRoots);
		listRoots.AddHandler(DragDrop.DragOverEvent, DragOverFolder);
	}

	void DragOverFolder(object? sender, DragEventArgs e) {
		// Only accept file/folder drops, as Copy or Link.
		e.DragEffects &= (DragDropEffects.Copy | DragDropEffects.Link);
		if (!e.DataTransfer.Contains(DataFormat.File))
			e.DragEffects = DragDropEffects.None;
	}

	static string? FirstDroppedPath(DragEventArgs e) {
		if (!e.DataTransfer.Contains(DataFormat.File)) return null;
		foreach (var item in e.DataTransfer.GetItems(DataFormat.File) ?? Array.Empty<IDataTransferItem>()) {
			string? local = item.TryGetFile()?.TryGetLocalPath();
			if (!string.IsNullOrEmpty(local)) return local;
		}
		return null;
	}

	// A single dropped folder fills the prefix box. e.Handled stops the TextBox from also
	// inserting the raw path as text.
	void DropOldPrefix(object? sender, DragEventArgs e) {
		if (DataContext is RelocateFilesDialogVM vm && FirstDroppedPath(e) is string p) {
			vm.OldPrefix = p;
			e.Handled = true;
		}
	}

	void DropNewPrefix(object? sender, DragEventArgs e) {
		if (DataContext is RelocateFilesDialogVM vm && FirstDroppedPath(e) is string p) {
			vm.NewPrefix = p;
			e.Handled = true;
		}
	}

	// The scan-roots list takes every dropped folder, skipping duplicates.
	void DropScanRoots(object? sender, DragEventArgs e) {
		if (DataContext is not RelocateFilesDialogVM vm || !e.DataTransfer.Contains(DataFormat.File))
			return;
		foreach (var item in e.DataTransfer.GetItems(DataFormat.File) ?? Array.Empty<IDataTransferItem>()) {
			string? local = item.TryGetFile()?.TryGetLocalPath();
			if (!string.IsNullOrEmpty(local) && !vm.ScanRoots.Contains(local))
				vm.ScanRoots.Add(local);
		}
	}
}
