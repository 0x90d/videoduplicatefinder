using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using VDF.GUI.Data;
using VDF.GUI.Utils;

namespace VDF.GUI.Views;

public partial class ChooseAlgoView : Window {
	public static readonly StyledProperty<int> StepIndexProperty =
		AvaloniaProperty.Register<ChooseAlgoView, int>(nameof(StepIndex));

	public int StepIndex {
		get => GetValue(StepIndexProperty);
		set => SetValue(StepIndexProperty, value);
	}

	private Button? backButton;
	private Button? skipButton;
	private Button? nextButton;
	private Button? finishButton;
	private ListBox? includesListBox;

	public ChooseAlgoView() {
		InitializeComponent();
		DataContext = this;
		backButton = this.FindControl<Button>("ButtonBack");
		skipButton = this.FindControl<Button>("ButtonSkip");
		nextButton = this.FindControl<Button>("ButtonNext");
		finishButton = this.FindControl<Button>("ButtonFinish");
		includesListBox = this.FindControl<ListBox>("ListboxOnboardingIncludes");
		Owner = ApplicationHelpers.MainWindow;


		if (!SettingsFile.Instance.DarkMode)
			RequestedThemeVariant = ThemeVariant.Light;
		if (includesListBox != null) {
			includesListBox.AddHandler(DragDrop.DropEvent, DropInclude);
			includesListBox.AddHandler(DragDrop.DragOverEvent, DragOver);
		}

		PropertyChanged += ChooseAlgoView_PropertyChanged;
		UpdateStepControls();
	}
	private void ChooseAlgoView_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e) {
		if (e.Property == StepIndexProperty)
			UpdateStepControls();
	}

	private void UpdateStepControls() {
		if (backButton == null || skipButton == null || nextButton == null || finishButton == null)
			return;

		backButton.IsVisible = StepIndex > 0;
		skipButton.IsVisible = StepIndex > 0;
		nextButton.IsVisible = StepIndex < 2;
		finishButton.IsVisible = StepIndex >= 2;
	}

	private void DragOver(object? sender, DragEventArgs e) {
		e.DragEffects &= (DragDropEffects.Copy | DragDropEffects.Link);
		if (!e.DataTransfer.Contains(DataFormat.File))
			e.DragEffects = DragDropEffects.None;
	}

	private void DropInclude(object? sender, DragEventArgs e) {
		if (!e.DataTransfer.Contains(DataFormat.File))
			return;

		foreach (var path in e.DataTransfer.GetItems(DataFormat.File) ?? Array.Empty<IDataTransferItem>()) {
			IStorageItem? fold = path.TryGetFile();
			if (fold == null)
				continue;

			string? localPath = fold.TryGetLocalPath();
			if (!string.IsNullOrEmpty(localPath) && !SettingsFile.Instance.Includes.Contains(localPath))
				SettingsFile.Instance.Includes.Add(localPath);
		}
	}

	private async void AddFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
		var options = new FolderPickerOpenOptions {
			AllowMultiple = true,
			Title = App.Lang["Dialog.SelectFolder"]
		};

		var paths = await PickerDialogUtils.OpenDialogPicker(options);
		if (paths == null)
			return;

		foreach (var path in paths) {
			if (!SettingsFile.Instance.Includes.Contains(path))
				SettingsFile.Instance.Includes.Add(path);
		}
	}

	private void RemoveFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
		if (includesListBox?.SelectedItems == null || includesListBox.SelectedItems.Count == 0)
			return;

		var toRemove = includesListBox.SelectedItems.Cast<string>().ToList();
		foreach (var item in toRemove)
			SettingsFile.Instance.Includes.Remove(item);
	}

	private void Back_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
		StepIndex = Math.Max(0, StepIndex - 1);
	}

	private void Next_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
		StepIndex = Math.Min(2, StepIndex + 1);
	}

	private void Skip_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
		Close();
	}

	private void Finish_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
		Close();
	}
}
