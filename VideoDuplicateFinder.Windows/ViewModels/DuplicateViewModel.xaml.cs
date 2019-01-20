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
				System.Diagnostics.Process.Start("explorer.exe", $"/select, \"{((DuplicateItemViewModel)DataContext).Path}\"");
			}
			catch (Exception exception) {
				MessageBox.Show(exception.Message, VideoDuplicateFinder.Windows.Properties.Resources.Error, MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

	}
}
