using System.Windows.Input;
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
	}
}
