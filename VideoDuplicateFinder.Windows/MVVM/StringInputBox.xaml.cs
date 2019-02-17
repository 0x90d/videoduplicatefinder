namespace VideoDuplicateFinderWindows {
	/// <summary>
	/// Interaction logic for StringInputBox.xaml
	/// </summary>
	public partial class StringInputBox {
		public StringInputBox() {
			InitializeComponent();
			DataContext = this;
			Loaded += StringInputBox_Loaded;
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
