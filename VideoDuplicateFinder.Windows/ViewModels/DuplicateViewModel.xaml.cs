using System.Windows.Controls;
using System.Windows.Input;
using VideoDuplicateFinderWindows.Data;

namespace VideoDuplicateFinderWindows.ViewModels
{
    public partial class DuplicateViewModel : UserControl
    {
        public DuplicateViewModel()
        {
            InitializeComponent();
        }
        private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
	        if (e.ClickCount == 2) {
		        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
			        FileName = ((DuplicateItemViewModel)DataContext).Path,
			        UseShellExecute = true
		        });
	        }
        }

	}
}
