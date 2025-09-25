// /*
//     Copyright (C) 2025 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Diagnostics;
using System.Text.Json;
using ProtoBuf;

namespace VDF.Core.Utils {
	static class DatabaseUtils {
		internal static HashSet<FileEntry> Database => DbWrapper.Entries;
		internal static int DbVersion => DbWrapper.Version;
		static DatabaseWrapper DbWrapper = new();
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
				DbWrapper = Serializer.Deserialize<DatabaseWrapper>(file);
			}
			catch (ProtoException ex) {
				if (UpgradeDatabase(databaseFile.FullName)) {
					Logger.Instance.Info("Database has been upgraded to the new format.");
					return true;
				}
				Logger.Instance.Info($"Importing previously scanned files has failed because of: {ex}");
				st.Stop();
				try {
					File.Move(databaseFile.FullName, Path.ChangeExtension(databaseFile.FullName, "_DAMAGED.db"), true);
				}
				catch (Exception) { }
				return false;
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
		internal static void Create16x16Database() {
			DbWrapper.Version = 1;
			SaveDatabase();
		}
		static bool UpgradeDatabase(string file) {

			try {
				using var fs = new FileStream(file, FileMode.Open);
				DbWrapper.Entries = Serializer.Deserialize<HashSet<FileEntry>>(fs);
				DbWrapper.Version = 1;
				return true;
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Upgrading database has failed because of: {ex}");
			}
			return false;
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
			Serializer.Serialize(stream, DbWrapper);
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
				JsonSerializer.Serialize(stream, DbWrapper, options);
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
				DbWrapper = JsonSerializer.Deserialize<DatabaseWrapper>(stream, options)!;
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
