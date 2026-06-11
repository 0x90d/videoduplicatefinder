// /*
//     Copyright (C) 2026 0x90d
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
using MemoryPack;

namespace VDF.Core.Utils {
	static class DatabaseUtils {
		// New databases are MemoryPack payloads behind this magic header; files without
		// it are protobuf-net databases from 3.x / early 4.x, decoded by
		// LegacyDatabaseReader and migrated to the new format on the next save.
		static ReadOnlySpan<byte> FormatMagic => "VDFDB001"u8;

		internal static HashSet<FileEntry> Database => DbWrapper.Entries;
		internal static int DbVersion => DbWrapper.Version;
		static DatabaseWrapper DbWrapper = new();
		internal static string? CustomDatabaseFolder;
		static string? _resolvedDatabaseFolder;

		static string ResolveDatabaseFolder() => CoreUtils.ResolveDatabaseFolder(CustomDatabaseFolder);

		internal static void InvalidateDatabaseFolder() => _resolvedDatabaseFolder = null;

		static string DatabaseFolder => _resolvedDatabaseFolder ??= ResolveDatabaseFolder();

		static string CurrentDatabasePath => FileUtils.SafePathCombine(DatabaseFolder, "ScannedFiles.db");
		static string TempDatabasePath => FileUtils.SafePathCombine(DatabaseFolder, "ScannedFiles_new.db");

		internal static bool LoadDatabase() {
			FileInfo databaseFile = new(TempDatabasePath);
			if (!databaseFile.Exists)
				databaseFile = new(CurrentDatabasePath);

			if (databaseFile.Exists && databaseFile.Length == 0) //invalid data
			{
				databaseFile.Delete();
				MigrateImageHashesIfNeeded();
				return true;
			}
			if (!databaseFile.Exists) {
				MigrateImageHashesIfNeeded();
				return true;
			}

			Logger.Instance.Info("Found previously scanned files, importing...");
			var st = Stopwatch.StartNew();
			try {
				using var file = new FileStream(databaseFile.FullName, FileMode.Open, FileAccess.Read);
				Span<byte> header = stackalloc byte[8];
				int headerRead = file.Read(header);
				if (headerRead == FormatMagic.Length && header.SequenceEqual(FormatMagic)) {
					DbWrapper = MemoryPackSerializer.DeserializeAsync<DatabaseWrapper>(file)
						.AsTask().GetAwaiter().GetResult() ?? new DatabaseWrapper();
				}
				else {
					// Legacy protobuf-net database (3.x / early 4.x).
					file.Position = 0;
					byte[] raw = new byte[file.Length];
					file.ReadExactly(raw);
					DbWrapper = LegacyDatabaseReader.Read(raw);
					Logger.Instance.Info("Legacy database format detected — it will be stored in the new format on the next save.");
				}
			}
			catch (Exception ex) {
				st.Stop();
				// A broken temp file (e.g. a crash mid-save) must not block startup:
				// drop it and retry with the real database file.
				if (databaseFile.FullName == new FileInfo(TempDatabasePath).FullName) {
					Logger.Instance.Info($"Importing previously scanned files from '{databaseFile.FullName}' has failed; retrying with the main database file.");
					try { databaseFile.Delete(); } catch (Exception) { }
					return LoadDatabase();
				}
				Logger.Instance.Info($"Importing previously scanned files has failed because of: {ex}");
				try {
					File.Move(databaseFile.FullName, Path.ChangeExtension(databaseFile.FullName, "_DAMAGED.db"), true);
				}
				catch (Exception) { }
				return false;
			}

			st.Stop();
			Logger.Instance.Info($"Previously scanned files imported. {Database.Count:N0} files in {st.Elapsed}");
			MigrateImageHashesIfNeeded();
			return true;
		}

		/// <summary>
		/// One-time migration: image gray bytes/pHashes produced by the old ImageSharp
		/// pipeline are not comparable with the FFmpeg pipeline (different luma weights
		/// and resampler), so clear them and let the next scan recompute. Cheap — images
		/// re-hash at one decode per file. Videos are unaffected (always FFmpeg-hashed).
		/// </summary>
		internal const int CurrentImageHashPipeline = 1;
		static void MigrateImageHashesIfNeeded() {
			if (DbWrapper.ImageHashPipeline >= CurrentImageHashPipeline)
				return;
			int cleared = 0;
			foreach (FileEntry entry in DbWrapper.Entries) {
				if (!entry.IsImage)
					continue;
				if (entry.grayBytes.Count > 0 || entry.PHashes.Count > 0 || entry.Flags.Has(EntryFlags.TooDark)) {
					entry.grayBytes.Clear();
					entry.PHashes.Clear();
					entry.Flags.Set(EntryFlags.TooDark, false);
					cleared++;
				}
			}
			DbWrapper.ImageHashPipeline = CurrentImageHashPipeline;
			if (cleared > 0)
				Logger.Instance.Info($"Image hash migration: cleared cached hashes of {cleared:N0} image(s) — they will be re-hashed with the FFmpeg pipeline on the next scan.");
		}
		internal static void Create16x16Database() {
			DbWrapper.Version = 1;
			SaveDatabase();
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

			using (FileStream stream = new(TempDatabasePath, FileMode.Create)) {
				stream.Write(FormatMagic);
				MemoryPackSerializer.SerializeAsync(stream, DbWrapper).AsTask().GetAwaiter().GetResult();
			}
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
		/// <summary>
		/// Layers the source-generated metadata over caller-supplied options (callers
		/// control formatting like WriteIndented). Keeps serialization AOT-safe without
		/// changing the public API.
		/// </summary>
		static JsonSerializerOptions WithCoreContext(JsonSerializerOptions options) =>
			new(options) { TypeInfoResolver = CoreJsonContext.Default };

		internal static bool ExportDatabaseToJson(string jsonFile, JsonSerializerOptions options) {
			try {
				// File.Create, not OpenWrite: overwriting a previously larger export with
				// OpenWrite leaves trailing garbage that breaks re-import.
				using var stream = File.Create(jsonFile);
				JsonSerializer.Serialize(stream, DbWrapper, WithCoreContext(options));
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
				DbWrapper = JsonSerializer.Deserialize<DatabaseWrapper>(stream, WithCoreContext(options))!;
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
