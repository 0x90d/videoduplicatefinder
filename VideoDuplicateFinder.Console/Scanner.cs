using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DuplicateFinderEngine;

namespace VideoDuplicateFinderConsole {
	class Scanner {
		private readonly ScanEngine engine = new ScanEngine();
		readonly ConsoleScanSettings ScanSettings;
		readonly string Outputfolder;
		public Scanner(ConsoleScanSettings settings) {
			foreach (var s in settings.IncludeFolders)
				engine.Settings.IncludeList.Add(s);
			foreach (var s in settings.ExcludeFolders)
				engine.Settings.IncludeList.Add(s);
			engine.Settings.IncludeSubDirectories = settings.IsRecursive;
			engine.Settings.IncludeImages = settings.IncludeImages;
			Outputfolder = string.IsNullOrEmpty(settings.OutputFolder) ? Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) : outputfolder;
			if (settings.Percent.HasValue)
				engine.Settings.Percent = settings.Percent.Value;
			if (!settings.IsQuiet)
				engine.Progress += Engine_Progress;
			engine.ScanDone += Engine_ScanDone;
			engine.DatabaseCleaned += Engine_DatabaseCleaned;
			ScanSettings = settings;
		}

		private static void Engine_DatabaseCleaned(object sender, EventArgs e) => Console.WriteLine("~~~~ Database cleanup completed! ~~~~");

		public void StartSearch() => engine.StartSearch();

		public void StartCleanup() => engine.CleanupDatabase();

		private void Engine_ScanDone(object sender, EventArgs e) {
			Console.WriteLine("~~~~ Scan done! ~~~~");
			Console.WriteLine($"Found '{engine.Duplicates.Count}' duplicates");
			if (engine.Duplicates.Count == 0) return;
			var targetFile = Utils.SafePathCombine(Outputfolder, "output.html");
			engine.Duplicates.ToHtmlTable(targetFile);
			Console.Write("Saved results in: ");
			Console.WriteLine(targetFile);
		}
		private static readonly object _MessageLock = new object();
		private static void Engine_Progress(object sender, ScanEngine.OwnScanProgress e) {
			lock (_MessageLock) {
				Console.Write($"Elapsed {e.Elapsed.TrimMiliseconds()}, remaining ~{e.Remaining.TrimMiliseconds()}, processing {TruncateWithElipsis(e.CurrentFile)}");
			}
		}
		private static string TruncateWithElipsis(string s, int length = 60) => (s.Length > length ? "..." + s.Substring(s.Length - length) : s);
	}
}
