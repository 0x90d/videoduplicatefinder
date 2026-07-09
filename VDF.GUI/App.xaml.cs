// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI {
	public class App : Application {
		public static LanguageService Lang { get; } = new();

		public override void Initialize() {
			Lang.LoadLanguage("en");
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted() {
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				// Crash logging must be wired BEFORE the window is constructed: an exception
				// escaping the MainWindow/MainWindowVM constructors used to terminate the
				// process without leaving any trace in log.txt (#830).
				AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
				Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
				TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
				desktop.MainWindow = new MainWindow {
					DataContext = new MainWindowVM(),
				};
				desktop.ShutdownRequested += OnShutdownRequested;
				desktop.Exit += OnExitCleanup; //fallback
				AppDomain.CurrentDomain.ProcessExit += (_, __) => SafeCleanup();
			}

			base.OnFrameworkInitializationCompleted();
		}
		private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e) => SafeCleanup();
		private void OnExitCleanup(object? sender, ControlledApplicationLifetimeExitEventArgs e) => SafeCleanup();
		private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) {
			try {
				string detail = e.ExceptionObject is Exception ex ? ex.ToString() : e.ExceptionObject?.ToString() ?? "<null>";
				VDF.Core.Utils.Logger.Instance.Error($"FATAL: Unhandled exception (terminating={e.IsTerminating}): {detail}");
			}
			catch { /* never let logging failure mask the original crash */ }
			SafeCleanup();
		}
		private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e) {
			try {
				VDF.Core.Utils.Logger.Instance.Error($"Unhandled dispatcher exception: {e.Exception}");
			}
			catch { /* never let logging failure mask the original error */ }
			// Keep the app alive so the user can save state and the log contains a stack trace.
			e.Handled = true;
		}
		private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
			try {
				VDF.Core.Utils.Logger.Instance.Error($"Unobserved task exception: {e.Exception}");
			}
			catch { /* never let logging failure mask the original error */ }
			e.SetObserved();
		}
		private static void SafeCleanup() {
			try {
				try { VDF.GUI.Utils.ThumbCacheHelpers.Provider?.Dispose(); }
				catch { /* ignore */ }
				VDF.GUI.Utils.ThumbCacheHelpers.Provider = null;

				try { TempExtractionManager.DisposeAll(); }
				catch { /* ignore */ }
			}
			catch { /* last resort */ }
		}
	}
}
