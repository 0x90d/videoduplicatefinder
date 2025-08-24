using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public partial class QualityOrderDialog : Window {
		public QualityOrderDialog() {
			AvaloniaXamlLoader.Load(this);
			DataContext = new QualityOrderVM();

			Owner = ApplicationHelpers.MainWindow;
			if (!VDF.GUI.Data.SettingsFile.Instance.DarkMode)
				RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
		}

		public QualityOrderVM ViewModel => (QualityOrderVM)DataContext!;

		private void Ok_Click(object? sender, RoutedEventArgs e) {
			Close(ViewModel.CriteriaOrder.ToList());
		}
		private void Cancel_Click(object? sender, RoutedEventArgs e) {
			Close();
		}
	}
}
