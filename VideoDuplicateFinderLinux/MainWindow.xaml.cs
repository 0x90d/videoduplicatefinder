using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace VideoDuplicateFinderLinux {
	public class MainWindow : Window {
		public MainWindow() {
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
			Closing += MainWindow_Closing;
			((MainWindowVM)DataContext).LoadSettings();
		}
		

		private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
			((MainWindowVM)DataContext).SaveSettings();
		}

		private void InitializeComponent() {
			AvaloniaXamlLoader.Load(this);
		}
	}
}
