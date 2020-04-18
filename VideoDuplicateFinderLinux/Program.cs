using Avalonia;
using Avalonia.Logging.Serilog;
using Avalonia.ReactiveUI;

namespace VideoDuplicateFinderLinux {
	class Program {
		static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

		public static AppBuilder BuildAvaloniaApp()
			=> AppBuilder.Configure<App>()
				.UsePlatformDetect().UseReactiveUI()
				.LogToDebug();
	}
}
