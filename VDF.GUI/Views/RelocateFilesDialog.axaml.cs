using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
	}

}
