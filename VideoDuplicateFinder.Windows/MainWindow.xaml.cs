using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.ObjectModel;
using VideoDuplicateFinderWindows.Data;

namespace VideoDuplicateFinderWindows
{
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            ((MainWindowVM)DataContext).host = this;
            ((MainWindowVM)DataContext).LoadSettings();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ((MainWindowVM) DataContext).SaveSettings();
        }

		private void TreeViewDuplicates_PreviewKeyDown(object sender, KeyEventArgs e) {
			
				if (e.Key == Key.Space || e.Key == Key.Enter && TreeViewDuplicates.SelectedItem != null) {
					((DuplicateItemViewModel)TreeViewDuplicates.SelectedItem).Checked = !((DuplicateItemViewModel)TreeViewDuplicates.SelectedItem).Checked;
			}
		}

		private void FolderListBox_Drop(object sender, System.Windows.DragEventArgs e) {
			var existing = (ObservableCollection<string>)((ListBox)sender).ItemsSource;
			if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
			var folders = (string[])e.Data.GetData(DataFormats.FileDrop);
			if (folders == null) return;
			for (int i = 0; i < folders.Length; i++) {
				var ws = new IWshRuntimeLibrary.WshShell();
				try {
					var sc = (IWshRuntimeLibrary.IWshShortcut)ws.CreateShortcut(folders[i]);
					folders[i] = sc.TargetPath;
				}
				catch { }
			}
			folders.Where(f => (File.GetAttributes(f) & FileAttributes.Directory) > 0)
				.Where(f => !existing.Contains(f))
				.ToList().ForEach(f => existing.Add(f));
		}
	}
}
