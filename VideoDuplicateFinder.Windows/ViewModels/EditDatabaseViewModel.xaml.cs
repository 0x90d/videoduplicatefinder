using System.Collections.Generic;
using System.Windows.Controls;
using DuplicateFinderEngine.Data;
using VideoDuplicateFinderWindows.MVVM;

namespace VideoDuplicateFinder.Windows.ViewModels {
	/// <summary>
	/// Interaction logic for EditDatabaseViewModel.xaml
	/// </summary>
	public partial class EditDatabaseViewModel : UserControl {
		public EditDatabaseViewModel() {
			InitializeComponent();
			DataContext = new EditDatabaseVM();
		}
	
	}
	class EditDatabaseVM : ViewModelBase {
		public List<VideoFileEntry>? Database { get; private set; }
		public DelegateCommand LoadDatabaseCommand => new DelegateCommand(a => {
			Database = DuplicateFinderEngine.DatabaseHelper.LoadDatabaseAsList();
			OnPropertyChanged(nameof(Database));
		});
		public DelegateCommand SaveDatabaseCommand => new DelegateCommand(a => {
			 DuplicateFinderEngine.DatabaseHelper.SaveDatabase(Database);
		}, a => Database != null);
	}
}
