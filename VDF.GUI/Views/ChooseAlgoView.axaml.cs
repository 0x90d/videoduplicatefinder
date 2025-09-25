using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using VDF.GUI.Data;

namespace VDF.GUI.Views;

public partial class ChooseAlgoView : Window
{
    public ChooseAlgoView()
    {
        InitializeComponent();
		Owner = ApplicationHelpers.MainWindow;



		if (!SettingsFile.Instance.DarkMode)
			RequestedThemeVariant = ThemeVariant.Light;
	}

    private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
		this.Close();
    }
}
