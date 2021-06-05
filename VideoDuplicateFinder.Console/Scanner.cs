using System;
using System.IO;
using System.Reflection;
using DuplicateFinderEngine;

namespace VideoDuplicateFinderConsole {
	class Scanner {
		private readonly ScanEngine engine = new ScanEngine();
		readonly string Outputfolder;
		readonly ConsoleScanSettings consoleScanSettings;
		public Scanner(ConsoleScanSettings settings) {
			consoleScanSettings = settings;

			foreach (var s in settings.IncludeFolders)
				engine.Settings.IncludeList.Add(s);
			foreach (var s in settings.ExcludeFolders)
				engine.Settings.IncludeList.Add(s);
			engine.Settings.IncludeSubDirectories = settings.IsRecursive;
			engine.Settings.IncludeImages = settings.IncludeImages;
#pragma warning disable CS8601, CS8602 // Possible null reference assignment.
			Outputfolder = string.IsNullOrEmpty(settings.OutputFolder) ? Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) : settings.OutputFolder;
#pragma warning restore CS8601, CS8602 // Possible null reference assignment.
			if (settings.Percent.HasValue)
				engine.Settings.Percent = settings.Percent.Value;
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate.
			if (!settings.IsQuiet)
				engine.Progress += Engine_Progress;
			engine.ScanDone += Engine_ScanDone;
			engine.DatabaseCleaned += Engine_DatabaseCleaned;
#pragma warning restore CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate.
		}

		private static void Engine_DatabaseCleaned(object sender, EventArgs e) => Console.Error.WriteLine("~~~~ Database cleanup completed! ~~~~");

		public void StartSearch() => engine.StartSearch();

		public void StartCleanup() {
			engine.CleanupDatabase();
			Environment.Exit(0);
		}

		private void Engine_ScanDone(object sender, EventArgs e) {
			Console.Error.WriteLine();
			Console.Error.WriteLine();
			Console.Error.WriteLine("~~~~ Scan done! ~~~~");
			Console.Error.WriteLine($"Found '{engine.Duplicates.Count:N0}' duplicates");
			if (engine.Duplicates.Count == 0) return;

			if (consoleScanSettings.OutputJson) {
				var jsonOutput = engine.Duplicates.ToJson();
				Console.Write(jsonOutput);
			} else {
				var targetFile = Utils.SafePathCombine(Outputfolder, "output.html");
				engine.Duplicates.ToHtmlTable(targetFile);
				Console.Error.Write("Saved results in: ");
				Console.Error.WriteLine(targetFile);
			}
			
			Environment.Exit(0);
		}
		private static readonly object _MessageLock = new object();
		private static void Engine_Progress(object sender, ScanEngine.OwnScanProgress e) {
			lock (_MessageLock) {
				Console.Error.WriteLine($"## Elapsed {e.Elapsed.TrimMiliseconds()}, remaining ~{e.Remaining.TrimMiliseconds()}, processing {TruncateWithElipsis(e.CurrentFile)}");
			}
		}
		private static string TruncateWithElipsis(string s, int length = 60) => (s.Length > length ? "..." + s.Substring(s.Length - length) : s);
	}
}
