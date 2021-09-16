// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using System.Text.Json;
using ProtoBuf;

namespace VDF.Core.Utils {
	static class DatabaseUtils {
		public static HashSet<FileEntry> Database = new();
		public static bool LoadDatabase() {
			var databaseFile = new FileInfo(FileUtils.SafePathCombine(CoreUtils.CurrentFolder, "ScannedFiles.db"));
			if (databaseFile.Exists && databaseFile.Length == 0) //invalid data
			{
				databaseFile.Delete();
				return true;
			}
			if (!databaseFile.Exists)
				return true;

			Logger.Instance.Info("Found previously scanned files, importing...");
			var st = Stopwatch.StartNew();
			try {
				using var file = new FileStream(databaseFile.FullName, FileMode.Open);
				Database = Serializer.Deserialize<HashSet<FileEntry>>(file);
			}
			catch (ProtoException) {
				//This could be an older database
				try {
					using var file = new FileStream(databaseFile.FullName, FileMode.Open);
					var oldDatabase = Serializer.Deserialize<List<FileEntry_old>>(file);
					Database = new();
					foreach (var item in oldDatabase) {
						Database.Add(new FileEntry(item.Path) {
							Flags = item.Flags,
							mediaInfo = item.mediaInfo,
							grayBytes = new()
						});
					}
				}
				catch (ProtoException ex) {
					Logger.Instance.Info($"Importing previously scanned files has failed because of: {ex}");
					st.Stop();
					return false;
				}
			}

			st.Stop();
			Logger.Instance.Info($"Previously scanned files imported. {Database.Count:N0} files in {st.Elapsed}");
			return true;
		}
		public static void CleanupDatabase() {
			int oldCount = Database.Count;
			var st = Stopwatch.StartNew();

			Database.RemoveWhere(a => !File.Exists(a.Path) || a.Flags.Any(EntryFlags.MetadataError | EntryFlags.ThumbnailError));

			st.Stop();
			Logger.Instance.Info(
				$"Database cleanup has finished in: {st.Elapsed}, {oldCount - Database.Count} entries have been removed");
			SaveDatabase();
		}
		public static void SaveDatabase() {
			Logger.Instance.Info($"Save scanned files to disk ({Database.Count:N0} files).");
			using var stream = new FileStream(FileUtils.SafePathCombine(CoreUtils.CurrentFolder,
				"ScannedFiles.db"), FileMode.Create);
			Serializer.Serialize(stream, Database);
		}
		public static void ClearDatabase() => Database.Clear();

		public static void BlacklistFileEntry(string filePath) {
			if (!Database.TryGetValue(new FileEntry(filePath), out FileEntry? actualValue))
				return;
			actualValue.Flags.Set(EntryFlags.ManuallyExcluded);
		}

		public static bool ExportDatabaseToJson(string jsonFile, JsonSerializerOptions options) {
			try {
				using var stream = File.OpenWrite(jsonFile);
				JsonSerializer.Serialize(stream, Database, options);
				stream.Close();
			}
			catch (JsonException e) {
				Logger.Instance.Info($"Failed to serialize database to json because: {e}");
				return false;
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed to export database to json because: {e}");
				return false;
			}
			return true;
		}
	}
}
