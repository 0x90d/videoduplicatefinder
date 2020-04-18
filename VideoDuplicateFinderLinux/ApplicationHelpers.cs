using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace VideoDuplicateFinderLinux {
	static class ApplicationHelpers {
		public static IClassicDesktopStyleApplicationLifetime CurrentApplicationLifetime =>
			(IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
		public static MainWindow MainWindow =>
			(MainWindow)((IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime).MainWindow;
		public static MainWindowVM MainWindowDataContext =>
			(MainWindowVM)MainWindow.DataContext;

	}
}
