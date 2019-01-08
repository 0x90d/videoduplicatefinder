using System.Collections.Generic;

namespace DuplicateFinderEngine {
	public class Settings {
		public HashSet<string> IncludeList { get; } = new HashSet<string>();
		public HashSet<string> BlackList { get; } = new HashSet<string>();

		public bool IgnoreSystemFolders;

		public bool IgnoreHiddenFolders;

		public bool IgnoreReadOnlyFolders;

		public bool IncludeSubDirectories = true;
		public bool IncludeImages = true;

		public byte Threshhold = 5;
		public float Percent = 96f;

		public int ThumbnailCount = 1;
	}
}
