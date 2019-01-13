using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using DuplicateFinderEngine.Data;
using ProtoBuf;

namespace DuplicateFinderEngine {
	static class DatabaseHelper {
		public static List<VideoFileEntry> LoadDatabase() {
			var videoFiles = new List<VideoFileEntry>();
			var path = new FileInfo(Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
				"ScannedFiles.db"));
			if (path.Exists && path.Length == 0) //invalid data
			{
				path.Delete();
				return videoFiles;
			}
			if (!path.Exists)
				return videoFiles;
			Logger.Instance.Info(Properties.Resources.FoundPreviouslyScannedFilesImporting);

			var st = Stopwatch.StartNew();
			using (var file = new FileStream(path.FullName, FileMode.Open)) {
				videoFiles = Serializer.Deserialize<List<VideoFileEntry>>(file);
			}
			
			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.PreviouslyScannedFilesImported, videoFiles.Count, st.Elapsed));

			return videoFiles;
		}

		public static void CleanupDatabase(List<VideoFileEntry> videoFiles) {

			var oldCount = videoFiles.Count;
			var st = Stopwatch.StartNew();
			//Cleanup deleted files
			for (int i = videoFiles.Count - 1; i >= 0; i--) {
				if (!File.Exists(videoFiles[i].Path))
					videoFiles.RemoveAt(i);
			}
			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.DatabaseCleanupHasFinished, st.Elapsed, oldCount - videoFiles.Count));

		}

		public static void SaveDatabase(List<VideoFileEntry> videoFiles) {
			Logger.Instance.Info(string.Format(Properties.Resources.SaveScannedFilesToDisk0N0Files, videoFiles.Count));
			using (var stream = new FileStream(Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
				"ScannedFiles.db"), FileMode.OpenOrCreate)) {
				Serializer.Serialize(stream, videoFiles);
			}
		}
	}
}
