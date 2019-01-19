using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VideoDuplicateFinderWindows {
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application { }

	public static class Startup {

		static void InstallExceptionHandlers() {
			TaskScheduler.UnobservedTaskException += (s, e) => { ShowException("TaskScheduler.UnobservedTask", e.Exception); e.SetObserved(); };

			if (Debugger.IsAttached) return;

			AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowException("CurrentDomain.Unhandled", e.ExceptionObject as Exception);
			Dispatcher.CurrentDispatcher.UnhandledException += (s, e) => { ShowException("CurrentDispatcher.Unhandled", e.Exception); e.Handled = true; };
		}

		static void ShowException(string type, Exception ex) {
			string msg = ex?.ToString() ?? "Unknown exception";
			MessageBox.Show(msg, $"{type}Exception", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		/// <summary>
		/// Custom Application Entry Point, to catch exceptions in WPF app constructor
		/// </summary>
		[System.STAThreadAttribute()]
		public static void Main() {
			InstallExceptionHandlers();

			var app = new App();
			app.InitializeComponent();
			app.Run();
		}
	}
}
