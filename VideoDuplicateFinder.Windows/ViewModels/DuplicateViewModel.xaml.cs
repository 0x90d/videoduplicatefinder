using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoDuplicateFinderWindows.Data;

namespace VideoDuplicateFinderWindows.ViewModels {
	public partial class DuplicateViewModel : UserControl {
		public DuplicateViewModel() {
			InitializeComponent();
		}
		private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
			if (e.ClickCount != 2) return;
			try {
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
					FileName = ((DuplicateItemViewModel)DataContext).Path,
					UseShellExecute = true
				});
			}
			catch (Exception exception) {
				MessageBox.Show(exception.Message, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

	}
}
