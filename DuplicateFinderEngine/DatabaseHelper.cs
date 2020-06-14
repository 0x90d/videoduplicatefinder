using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DuplicateFinderEngine.Data;
using ProtoBuf;

namespace DuplicateFinderEngine {
	public static class DatabaseHelper {
		private static string _databaseFile = Utils.SafePathCombine(FileHelper.DataDirectory, "ScannedFiles.db");

		public static Dictionary<string, FileEntry> LoadDatabase() {
			var videoFiles = new Dictionary<string, FileEntry>();
			var path = new FileInfo(_databaseFile);
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
				videoFiles = Serializer.Deserialize<List<FileEntry>>(file)
					.ToDictionary(ve => ve.Path, ve => ve);
			}

			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.PreviouslyScannedFilesImported, videoFiles.Count, st.Elapsed));

			return videoFiles;
		}
		public static List<FileEntry> LoadDatabaseAsList() {
			var videoFiles = new List<FileEntry>();
			var path = new FileInfo(_databaseFile);
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
				videoFiles = Serializer.Deserialize<List<FileEntry>>(file);
			}

			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.PreviouslyScannedFilesImported, videoFiles.Count, st.Elapsed));

			return videoFiles;
		}

		public static Dictionary<string, FileEntry> CleanupDatabase(Dictionary<string, FileEntry> videoFiles) {

			var oldCount = videoFiles.Count;
			var st = Stopwatch.StartNew();

			videoFiles = new Dictionary<string, FileEntry>(videoFiles.Where(kv =>
				File.Exists(kv.Value.Path) &&
				!kv.Value.Flags.Any(EntryFlags.MetadataError | EntryFlags.ThumbnailError)));
			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.DatabaseCleanupHasFinished, st.Elapsed, oldCount - videoFiles.Count));
			return videoFiles;

		}
		public static void ExportDatabaseToCSV(IEnumerable<FileEntry> videoFiles) {

			var st = Stopwatch.StartNew();
			using var dt = new DataTable();
			dt.Columns.Add("Directory", typeof(string));
			dt.Columns.Add("FileName", typeof(string));
			dt.Columns.Add("Width", typeof(int));
			dt.Columns.Add("Height", typeof(int));
			dt.Columns.Add("FrameRate", typeof(long));
			dt.Columns.Add("BitRate", typeof(long));
			dt.Columns.Add("CodecName", typeof(string));
			dt.Columns.Add("CodecLongName", typeof(string));
			dt.Columns.Add("DurationMinutes", typeof(double));
			dt.Columns.Add("Duration", typeof(string));
			dt.Columns.Add("IsImage", typeof(bool));
			dt.Columns.Add("Excluded", typeof(bool));
			dt.Columns.Add("Errors", typeof(string));


			foreach (var videoFile in videoFiles) {

				var mediaInfoStream = videoFile.mediaInfo?.Streams?.FirstOrDefault(s => s.CodecType == "video");

				dt.Rows.Add(
					Path.GetDirectoryName(videoFile.Path),
					Path.GetFileName(videoFile.Path),
					mediaInfoStream?.Width,
					mediaInfoStream?.Height,
					Convert.ToInt64(Math.Round(mediaInfoStream?.FrameRate ?? 0)),
					mediaInfoStream?.BitRate,
					mediaInfoStream?.CodecName,
					mediaInfoStream?.CodecLongName,
					videoFile.mediaInfo?.Duration.TotalMinutes ?? 0,
					videoFile.mediaInfo?.Duration.ToString(),
					videoFile.IsImage,
					videoFile.Flags.Has(EntryFlags.ManuallyExcluded),
					(videoFile.Flags & EntryFlags.AllErrors).ToString());
			}

			var sb = new StringBuilder();
			var columnNames = dt.Columns.Cast<DataColumn>().Select(column => column.ColumnName);
			sb.AppendLine(string.Join(",", columnNames));
			foreach (DataRow? row in dt.Rows) {
				if (row == null) return;
				var fields = row.ItemArray.Select(field => {
					string s = field?.ToString().Replace("\"", "\"\"") ?? string.Empty;
					if (s.Contains(','))
						s = string.Concat("\"", s, "\"");
					return s;
				});
				sb.AppendLine(string.Join(",", fields));
			}

			using (var outputFile = new StreamWriter(Utils.SafePathCombine(FileHelper.CurrentDirectory, "VideoFilesExport.csv"))) {
				outputFile.WriteLine(sb.ToString());
			}

			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.DatabaseVideosExportToCSVFinished, st.Elapsed));
		}

		public static void SaveDatabase(Dictionary<string, FileEntry> videoFiles) => SaveDatabase(videoFiles.Values.ToList());
		public static void SaveDatabase(List<FileEntry> videoFiles) {
			Logger.Instance.Info(string.Format(Properties.Resources.SaveScannedFilesToDisk0N0Files, videoFiles.Count));
			Directory.CreateDirectory(FileHelper.DataDirectory);
			using var stream = new FileStream(_databaseFile, FileMode.Create);
			Serializer.Serialize(stream, videoFiles);
		}
	}
}
