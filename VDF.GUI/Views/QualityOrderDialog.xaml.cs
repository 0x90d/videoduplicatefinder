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
			VDF.GUI.Utils.Appearance.Attach(this);
		}

		public QualityOrderVM ViewModel => (QualityOrderVM)DataContext!;

		void Ok_Click(object? sender, RoutedEventArgs e) {
			Close(ViewModel.CriteriaOrder.Select(o => o.Key).ToList());
		}

		void Cancel_Click(object? sender, RoutedEventArgs e) {
			Close();
		}
	}
}
