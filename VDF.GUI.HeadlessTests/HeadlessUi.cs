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

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using ReactiveUI.Avalonia;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

// One UI thread and one App for the whole assembly: tests queue onto it, so running
// classes in parallel would only interleave their use of the app's static singletons.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VDF.GUI.HeadlessTests;

/// <summary>AppBuilder source for the headless session: the real <see cref="App"/>, no window system.</summary>
public static class HeadlessEntryPoint {
	public static AppBuilder BuildAvaloniaApp() =>
		AppBuilder.Configure<App>()
			.UseSkia()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
			.UseReactiveUI(_ => { });
}

/// <summary>
/// Runs test bodies on the headless Avalonia UI thread. These tests load the real views,
/// which is why they live apart from VDF.GUI.Tests: UseReactiveUI replaces the process-wide
/// main thread scheduler, and the pure view-model tests must not depend on test order.
/// </summary>
public static class HeadlessUi {
	static readonly Lazy<HeadlessUnitTestSession> session = new(() =>
		HeadlessUnitTestSession.StartNew(typeof(HeadlessEntryPoint), AvaloniaTestIsolationLevel.PerAssembly));
	static bool initialized;

	// A view that blocks the UI thread would otherwise hang the whole run without a word.
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

	public static Task Run(Action body) => session.Value.Dispatch(() => {
		EnsureInitialized();
		body();
	}, CancellationToken.None).WaitAsync(Timeout);

	/// <summary>For tests that have to let time pass: timers only tick while the body awaits.</summary>
	public static Task Run(Func<Task> body) => session.Value.Dispatch(async () => {
		EnsureInitialized();
		await body();
		return true;
	}, CancellationToken.None).WaitAsync(Timeout);

	static void EnsureInitialized() {
		if (initialized) return;
		initialized = true;
		// Names are asserted as text, so pin the language instead of following the OS.
		App.Lang.LoadLanguage("en");
	}

	static MainWindow? shell;

	/// <summary>
	/// The real main window with its view model, shown once and shared: closing it would run
	/// the app's shutdown path. Tests must put back whatever state they change on it.
	/// </summary>
	public static (MainWindow Window, MainWindowVM ViewModel) Shell() {
		if (shell == null) {
			// Views and dialogs reach the window through the desktop lifetime, which a
			// headless session has none of, and its public setter refuses once the app is set up.
			var lifetime = new ClassicDesktopStyleApplicationLifetime();
			typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
				.First(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType))
				.SetValue(Application.Current, lifetime);

			// A database file suppresses the first-run algorithm dialog. It is never parsed:
			// the database is loaded from the lifetime's Startup event, which never fires here.
			string database = Path.Combine(CoreUtils.ResolveDatabaseFolder(SettingsFile.Instance.CustomDatabaseFolder), "ScannedFiles.db");
			bool placeholder = !File.Exists(database);
			if (placeholder) File.WriteAllBytes(database, []);
			shell = new MainWindow { DataContext = new MainWindowVM(), Width = 1300, Height = 950 };
			lifetime.MainWindow = shell;
			if (placeholder) File.Delete(database);

			App.Lang.LoadLanguage("en"); // the window's constructor applies the settings' language
			shell.Show();
			Pump();
		}
		return (shell, (MainWindowVM)shell.DataContext!);
	}

	/// <summary>Hosts a view in a shown window and settles layout, templates and bindings.</summary>
	public static Window Show(Control content, double width = 1300, double height = 950) {
		var window = new Window { Width = width, Height = height, Content = content };
		window.Show();
		Pump();
		return window;
	}

	public static void Pump() {
		for (int i = 0; i < 8; i++)
			Dispatcher.UIThread.RunJobs();
	}
}
