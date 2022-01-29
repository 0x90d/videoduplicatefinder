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
		internal static HashSet<FileEntry> Database = new();
		internal static string? CustomDatabaseFolder;

		static string CurrentDatabasePath => Directory.Exists(CustomDatabaseFolder)
					? FileUtils.SafePathCombine(CustomDatabaseFolder,
					"ScannedFiles.db")
					: FileUtils.SafePathCombine(CoreUtils.CurrentFolder,
					"ScannedFiles.db");
		static string TempDatabasePath => Directory.Exists(CustomDatabaseFolder)
					? FileUtils.SafePathCombine(CustomDatabaseFolder,
					"ScannedFiles_new.db")
					: FileUtils.SafePathCombine(CoreUtils.CurrentFolder,
					"ScannedFiles_new.db");

		internal static bool LoadDatabase() {
			FileInfo databaseFile = new(TempDatabasePath);
			if (!databaseFile.Exists)
				databaseFile = new(CurrentDatabasePath);

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
					try {
						File.Move(databaseFile.FullName, Path.ChangeExtension(databaseFile.FullName, "_DAMAGED.db"), true);
					}
					catch (Exception) { }
					return false;
				}
			}
			catch (EndOfStreamException) {
				Logger.Instance.Info($"Importing previously scanned files from '{databaseFile.FullName}' has failed.");
				databaseFile.Delete();
				//Could have been the temp database file
				LoadDatabase();
			}

			st.Stop();
			Logger.Instance.Info($"Previously scanned files imported. {Database.Count:N0} files in {st.Elapsed}");
			return true;
		}
		internal static void CleanupDatabase() {
			int oldCount = Database.Count;
			var st = Stopwatch.StartNew();

			Database.RemoveWhere(a => !File.Exists(a.Path) || a.Flags.Any(EntryFlags.MetadataError | EntryFlags.ThumbnailError));

			st.Stop();
			Logger.Instance.Info(
				$"Database cleanup has finished in: {st.Elapsed}, {oldCount - Database.Count} entries have been removed");
			SaveDatabase();
		}
		internal static void SaveDatabase() {
			Logger.Instance.Info($"Save scanned files to disk ({Database.Count:N0} files).");

			FileStream stream = new(TempDatabasePath, FileMode.Create);
			Serializer.Serialize(stream, Database);
			stream.Dispose();
			//Reason: https://github.com/0x90d/videoduplicatefinder/issues/247
			File.Move(TempDatabasePath, CurrentDatabasePath, true);
		}
		internal static void ClearDatabase() {
			Database.Clear();
			SaveDatabase();
		}
		internal static void BlacklistFileEntry(string filePath) {
			if (!Database.TryGetValue(new FileEntry(filePath), out FileEntry? actualValue))
				return;
			actualValue.Flags.Set(EntryFlags.ManuallyExcluded);
		}
		internal static void UpdateFilePath(string newPath, FileEntry dbEntry) {
			Database.Remove(dbEntry);
			dbEntry.Path = newPath;
			Database.Add(dbEntry);
		}
		internal static bool ExportDatabaseToJson(string jsonFile, JsonSerializerOptions options) {
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
		internal static bool ImportDatabaseFromJson(string jsonFile, JsonSerializerOptions options) {
			try {
				using var stream = File.OpenRead(jsonFile);
				Database = JsonSerializer.Deserialize<HashSet<FileEntry>>(stream, options)!;
				stream.Close();
			}
			catch (JsonException e) {
				Logger.Instance.Info($"Failed to deserialize database from json because: {e}");
				return false;
			}
			catch (Exception e) {
				Logger.Instance.Info($"Failed to import database from json because: {e}");
				return false;
			}
			return true;
		}
	}
}
