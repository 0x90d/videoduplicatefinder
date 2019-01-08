using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DuplicateFinderEngine;

namespace VideoDuplicateFinderConsole {
	class Scanner {
		private readonly ScanEngine engine = new ScanEngine();
		readonly string Outputfolder;
		public Scanner(List<string> include, List<string> exclude, bool recursive, string outputfolder,
			bool quiet, float? percent) {
			foreach (var s in include)
				engine.Settings.IncludeList.Add(s);
			foreach (var s in exclude)
				engine.Settings.IncludeList.Add(s);
			engine.Settings.IncludeSubDirectories = recursive;
			Outputfolder = string.IsNullOrEmpty(outputfolder) ? Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) : outputfolder;
			if (percent.HasValue)
				engine.Settings.Percent = percent.Value;
			if (!quiet)
				engine.Progress += Engine_Progress;
			engine.ScanDone += Engine_ScanDone;
		}

		public void StartSearch() {
			engine.StartSearch();
		}

		private void Engine_ScanDone(object sender, EventArgs e) {
			ConsoleHelpers.WriteLineColored("~~~~ Scan done! ~~~~", ConsoleColor.Green);
			Console.WriteLine($"Found '{engine.Duplicates.Count}' duplicates");
			if (engine.Duplicates.Count == 0) return;
			var targetFile = Utils.SafePathCombine(Outputfolder, "output.html");
			engine.Duplicates.ToHtmlTable(targetFile);
			Console.Write("Saved results in: ");
			ConsoleHelpers.WriteLineColored(targetFile, ConsoleColor.Cyan);
		}
		private static readonly object _MessageLock = new object();
		private static void Engine_Progress(object sender, ScanEngine.OwnScanProgress e) {
			lock (_MessageLock) {
				Console.Write("Elapsed ");
				ConsoleHelpers.WriteColored(e.Elapsed.TrimMiliseconds().ToString(), ConsoleColor.Yellow);
				Console.Write(", remaining ~");
				ConsoleHelpers.WriteColored(e.Remaining.TrimMiliseconds().ToString(), ConsoleColor.Yellow);
				Console.Write(", processing ");
				ConsoleHelpers.WriteLineColored(TruncateWithElipsis(e.CurrentFile), ConsoleColor.Magenta);
			}
		}
		private static string TruncateWithElipsis(string s, int length = 60) => (s.Length > length ? "..." + s.Substring(s.Length - length) : s);
	}
}
