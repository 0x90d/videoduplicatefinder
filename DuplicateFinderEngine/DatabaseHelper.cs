using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DuplicateFinderEngine.Data;
using ProtoBuf;

namespace DuplicateFinderEngine {
	static class DatabaseHelper {
		public static Dictionary<string, VideoFileEntry> LoadDatabase() {
			var videoFiles = new Dictionary<string, VideoFileEntry>();
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
				videoFiles = Serializer.Deserialize<List<VideoFileEntry>>(file)
					.ToDictionary(ve => ve.Path, ve => ve);
			}
			
			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.PreviouslyScannedFilesImported, videoFiles.Count, st.Elapsed));

			return videoFiles;
		}

		public static void CleanupDatabase(Dictionary<string, VideoFileEntry> videoFiles) {

			var oldCount = videoFiles.Count;
			var st = Stopwatch.StartNew();
			//Cleanup deleted files
			videoFiles = new Dictionary<string, VideoFileEntry>(videoFiles.Where(kv => File.Exists(kv.Value.Path)));
			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.DatabaseCleanupHasFinished, st.Elapsed, oldCount - videoFiles.Count));

		}
		public static void ExportDatabaseVideosToCSV(Dictionary<string, VideoFileEntry> videoFiles) {

			if (videoFiles.Count == 0)
				videoFiles = LoadDatabase();

			var st = Stopwatch.StartNew();
			DataTable dt = new DataTable();
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


			foreach (VideoFileEntry videoFile in videoFiles.Values) {

				if (videoFile.mediaInfo == null)
					continue;

				int streamIndex = -1;
				for (int i = 0; i < videoFile.mediaInfo.Streams.Length; i++) {
					if (videoFile.mediaInfo.Streams[i].CodecType == "video") {
						streamIndex = i;
						break;
					}
				}

				if (streamIndex == -1)
					continue;

				var mediaInfoStream = videoFile.mediaInfo.Streams[streamIndex];

				dt.Rows.Add(
					Path.GetDirectoryName(videoFile.Path),
					Path.GetFileName(videoFile.Path),
					mediaInfoStream.Width,
					mediaInfoStream.Height,
					Convert.ToInt64(Math.Round(mediaInfoStream.FrameRate)),
					mediaInfoStream.BitRate,
					mediaInfoStream.CodecName,
					mediaInfoStream.CodecLongName,
					videoFile.mediaInfo.Duration.TotalMinutes,
					videoFile.mediaInfo.Duration.ToString());
			}

			StringBuilder sb = new StringBuilder();
			IEnumerable<string> columnNames = dt.Columns.Cast<DataColumn>().Select(column => column.ColumnName);
			sb.AppendLine(string.Join(",", columnNames));
			foreach (DataRow row in dt.Rows) {
				IEnumerable<string> fields = row.ItemArray.Select(field =>
				{
					string s = field.ToString().Replace("\"", "\"\"");
					if (s.Contains(','))
						s = string.Concat("\"", s, "\"");
					return s;
				});
				sb.AppendLine(string.Join(",", fields));
			}

			using (StreamWriter outputFile = new StreamWriter(Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "VideoFilesExport.csv"))) {
				outputFile.WriteLine(sb.ToString());
			}

			st.Stop();
			Logger.Instance.Info(string.Format(Properties.Resources.DatabaseVideosExportToCSVFinished, st.Elapsed));
		}

		public static void SaveDatabase(Dictionary<string, VideoFileEntry> videoFiles) {
			Logger.Instance.Info(string.Format(Properties.Resources.SaveScannedFilesToDisk0N0Files, videoFiles.Count));
			using (var stream = new FileStream(Utils.SafePathCombine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
				"ScannedFiles.db"), FileMode.OpenOrCreate)) {
				Serializer.Serialize(stream, videoFiles.Values.ToList());
			}
		}
	}
}
