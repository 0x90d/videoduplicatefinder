namespace VideoDuplicateFinderWindows {
	/// <summary>
	/// Interaction logic for StringInputBox.xaml
	/// </summary>
	public partial class StringInputBox {
		public StringInputBox() {
			InitializeComponent();
			DataContext = this;
			Loaded += StringInputBox_Loaded;
			TextBoxInput.PreviewKeyDown += TextBoxInput_PreviewKeyDown;
		}

		private void TextBoxInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
			if (e.Key == System.Windows.Input.Key.Enter) {
				e.Handled = true;
				Button_Click(null, null);
			}
			else if (e.Key == System.Windows.Input.Key.Escape) {
				e.Handled = true;
				DialogResult = false;
				Close();
			}
		}

		private void StringInputBox_Loaded(object sender, System.Windows.RoutedEventArgs e) {
			Loaded -= StringInputBox_Loaded;
			TextBoxInput.Focus();
			TextBoxInput.SelectAll();
		}

		public string Message { get; set; }
		public string Value { get; set; }

		private void Button_Click(object sender, System.Windows.RoutedEventArgs e) {
			DialogResult = true;
			Close();
		}
	}
}
