namespace VideoDuplicateFinderWindows {
	/// <summary>
	/// Interaction logic for StringInputBox.xaml
	/// </summary>
	public partial class StringInputBox {
		public StringInputBox() {
			InitializeComponent();
			DataContext = this;
		}

		public string Message { get; set; }
		public string Value { get; set; }

		private void Button_Click(object sender, System.Windows.RoutedEventArgs e) {
			DialogResult = true;
			Close();
		}
	}
}
