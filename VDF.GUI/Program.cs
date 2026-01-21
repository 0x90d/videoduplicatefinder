// /*
//     Copyright (C) 2025 0x90d
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

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Threading.Tasks;
using System.CommandLine;
using System.Linq;
using Avalonia;
using ReactiveUI.Avalonia;
using VDF.GUI.Utils;

namespace VDF.GUI {
	class Program {
		// Initialization code. Don't use any Avalonia, third-party APIs or any
		// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
		// yet and stuff might break.
		[STAThread]
		public static int Main(string[] args) {

			Option<FileInfo> settingsOption = new("--settings", new[] { "-s" }) {
				Description = "Path to a settings file to load and save."
			};
			RootCommand rootCommand = new("VideoDuplicateFinder settings options");
			rootCommand.Options.Add(settingsOption);

			// This runs ONLY when parsing succeeded and no built-in action (like --help) took over
			rootCommand.SetAction(parseResult =>
			{
				if (parseResult.GetValue(settingsOption) is FileInfo parsedFile) {
					if (parsedFile.Exists) {
						Data.SettingsFile.SetSettingsPath(parsedFile.FullName);
						Console.Out.WriteLine($"Using custom settings file: '{parsedFile.FullName}'");
					}
					else {
						ConsoleAttach.EnsureConsole();
						Console.Error.WriteLine($"Settings file not found: '{parsedFile.FullName}'. Using default settings file.");
					}
				}

				BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
			});
			var parseResult = rootCommand.Parse(args);
			// If help requested OR parse errors -> we want console output
			if (parseResult.Errors.Count > 0 || args.Contains("-h") || args.Contains("--help") || args.Contains("-?")) {
				ConsoleAttach.EnsureConsole();
			}
			return rootCommand.Parse(args).Invoke();
		}

		// Avalonia configuration, don't remove; also used by visual designer.
		public static AppBuilder BuildAvaloniaApp()
			=> AppBuilder.Configure<App>()
				.UsePlatformDetect()
				.With(new X11PlatformOptions {  UseDBusFilePicker = false })
				.UseReactiveUI()
				.RegisterReactiveUIViewsFromEntryAssembly();
	}
}
