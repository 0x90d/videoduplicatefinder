using System.Collections.Generic;

namespace VideoDuplicateFinderConsole {
	class ConsoleScanSettings {
		public bool IsRecursive;
		public bool IncludeImages;
		public float? Percent;
		public string OutputFolder;
		public bool OutputJson;
		public bool IsQuiet;
		public bool CleanupDatabase;
		public readonly List<string> IncludeFolders = new List<string>();
		public readonly List<string> ExcludeFolders = new List<string>();
	}
}
