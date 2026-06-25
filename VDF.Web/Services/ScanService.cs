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

using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels;

namespace VDF.Web.Services {
	public enum ScanState { Idle, Scanning, Comparing, Done, Aborted, Error }

	/// <summary>Outcome of a batch file operation (delete / move / link).</summary>
	public sealed class FileOpResult {
		public int Done;
		public int Failed;
		public long FreedBytes;
		public List<string> Errors { get; } = new();
	}

	public class ScanProgressArgs {
		public string CurrentFile { get; init; } = string.Empty;
		public int Current { get; init; }
		public int Max { get; init; }
		public TimeSpan Elapsed { get; init; }
		public TimeSpan Remaining { get; init; }
		public string CurrentStage { get; init; } = string.Empty;
		public int StageCurrent { get; init; }
		public int StageMax { get; init; }
	}

	/// <summary>
	/// Singleton service that owns the ScanEngine instance and exposes
	/// scan lifecycle operations to Blazor components via events and state.
	/// </summary>
	public sealed class ScanService : IDisposable {
		readonly ScanEngine _engine = new();
		readonly WebSettingsService _settingsService;
		CancellationTokenSource _cts = new();

		public ScanState State { get; private set; } = ScanState.Idle;
		public string? ErrorMessage { get; private set; }
		public ScanProgressArgs? LastProgress { get; private set; }
		/// <summary>Total files hashed (captured when BuildingHashesDone fires).</summary>
		public int FilesHashed { get; private set; }
		public IReadOnlyCollection<DuplicateItem> Duplicates => _engine.Duplicates;
		public Settings Settings => _engine.Settings;

		/// <summary>
		/// Snapshot of the file paths selected in the Results UI, captured right before a CSV
		/// export is triggered. The selection itself lives per-circuit in the Results component;
		/// the stateless /export/csv endpoint reads this to emit the per-row Selected column.
		/// </summary>
		public IReadOnlySet<string> ExportSelection { get; private set; } = EmptySelection();

		static HashSet<string> EmptySelection() =>
			new(CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		/// <summary>Stores the paths selected at export time so the CSV export can mark them.</summary>
		public void SetExportSelection(IEnumerable<string> paths) {
			var set = EmptySelection();
			foreach (var p in paths)
				set.Add(p);
			ExportSelection = set;
		}

		/// <summary>Caches for the thumbnail endpoints — cleared whenever the results change wholesale.</summary>
		public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> HqThumbCache { get; } = new();
		public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> FullThumbCache { get; } = new();

		void ClearThumbnailCaches() {
			HqThumbCache.Clear();
			FullThumbCache.Clear();
		}

		public event Action? StateChanged;

		public ScanService(WebSettingsService settingsService) {
			_settingsService = settingsService;
			settingsService.Load(_engine.Settings);

			_engine.FilesEnumerated += (_, _) => Notify();
			_engine.BuildingHashesDone += (_, _) => {
				FilesHashed = LastProgress?.Max ?? 0;
				LastProgress = null;
				State = ScanState.Comparing;
				Notify();
			};
			_engine.Progress += (_, e) => {
				LastProgress = new ScanProgressArgs {
					CurrentFile = e.CurrentFile,
					Current = e.CurrentPosition,
					Max = e.MaxPosition,
					Elapsed = e.Elapsed,
					Remaining = e.Remaining,
					CurrentStage = e.CurrentStage ?? string.Empty,
					StageCurrent = e.StageCurrent,
					StageMax = e.StageMax,
				};
				Notify();
			};
			_engine.ScanDone += (_, _) => {
				// Skip low-res thumbnail retrieval — WebUI loads HQ thumbnails on demand
				// via the /thumbnail/hq endpoint. This makes results available immediately.
				State = ScanState.Done;
				LastProgress = null;
				Notify();
			};
			_engine.ScanAborted += (_, _) => {
				State = ScanState.Aborted;
				LastProgress = null;
				Notify();
			};
		}

		public void StartScanAndCompare() {
			if (State == ScanState.Scanning || State == ScanState.Comparing) return;
			_cts = new CancellationTokenSource();
			State = ScanState.Scanning;
			ErrorMessage = null;
			LastProgress = null;
			FilesHashed = 0;
			_engine.Duplicates.Clear();
			ExportSelection = EmptySelection();
			ClearThumbnailCaches();
			try {
				_engine.StartSearch();
			}
			catch (Exception ex) {
				SetError(ex);
				return;
			}
			Notify();
		}

		/// <summary>Called from global exception handlers to surface post-await async void exceptions.</summary>
		public void SetError(Exception ex) {
			State = ScanState.Error;
			ErrorMessage = ex.Message;
			LastProgress = null;
			Notify();
		}

		public void Pause() => _engine.Pause();
		public void Resume() => _engine.Resume();

		public void Stop() {
			_engine.Stop();
			_cts.Cancel();
		}

		public bool SaveSettings() => _settingsService.Save(_engine.Settings);

		public void Reset() {
			if (State == ScanState.Scanning || State == ScanState.Comparing) return;
			State = ScanState.Idle;
			ErrorMessage = null;
			LastProgress = null;
			FilesHashed = 0;
			_engine.Duplicates.Clear();
			ExportSelection = EmptySelection();
			ClearThumbnailCaches();
			// Keep IncludeList/BlackList — resetting scan results should not
			// throw away the paths the user configured.
			Notify();
		}

		/// <summary>Removes items from the results list without touching the files on disk.</summary>
		public void RemoveFromResults(IEnumerable<DuplicateItem> items) {
			foreach (var item in items.ToList())
				_engine.Duplicates.Remove(item);
			DropSingletonGroups();
			Notify();
		}

		/// <summary>Drops groups that have shrunk to a single item — a group of one is not a duplicate.</summary>
		void DropSingletonGroups() {
			var keep = _engine.Duplicates
				.GroupBy(d => d.GroupId)
				.Where(g => g.Count() > 1)
				.Select(g => g.Key)
				.ToHashSet();
			foreach (var d in _engine.Duplicates.ToList())
				if (!keep.Contains(d.GroupId))
					_engine.Duplicates.Remove(d);
		}

		// === Batch file operations (delete / move / link) ===

		/// <summary>True while a delete/move/link batch is running.</summary>
		public bool FileOpRunning { get; private set; }
		public string FileOpVerb { get; private set; } = string.Empty;
		public int FileOpCurrent { get; private set; }
		public int FileOpMax { get; private set; }

		bool TryBeginFileOp(string verb, int max) {
			if (FileOpRunning || max == 0) return false;
			FileOpRunning = true;
			FileOpVerb = verb;
			FileOpCurrent = 0;
			FileOpMax = max;
			Notify();
			return true;
		}

		void EndFileOp() {
			FileOpRunning = false;
			FileOpVerb = string.Empty;
			Notify();
		}

		/// <summary>Deletes files from disk and removes them from results and the scan database.</summary>
		public async Task<FileOpResult> DeleteItemsAsync(IEnumerable<DuplicateItem> items, bool permanent) {
			var list = items.ToList();
			var result = new FileOpResult();
			if (!TryBeginFileOp(permanent ? "Deleting" : "Moving to trash", list.Count))
				return result;
			try {
				await Task.Run(() => {
					// Windows: recycle the whole batch in one shell call — one
					// SHFileOperation per file pays the full shell round-trip each time.
					var batchRecycled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					if (!permanent && OperatingSystem.IsWindows()) {
						var existing = list.Where(d => File.Exists(d.Path)).Select(d => d.Path).ToList();
						if (existing.Count > 0) {
							var fs = new FileUtils.SHFILEOPSTRUCT {
								wFunc = FileUtils.FileOperationType.FO_DELETE,
								pFrom = string.Join('\0', existing) + "\0\0",
								fFlags = FileUtils.FileOperationFlags.FOF_ALLOWUNDO |
										 FileUtils.FileOperationFlags.FOF_NOCONFIRMATION |
										 FileUtils.FileOperationFlags.FOF_NOERRORUI |
										 FileUtils.FileOperationFlags.FOF_SILENT
							};
							int shResult = FileUtils.SHFileOperation(ref fs);
							if (shResult != 0)
								Logger.Instance.Info($"SHFileOperation returned {shResult:X} for a batch of {existing.Count} file(s); checking which files were actually recycled.");
							foreach (var p in existing)
								batchRecycled.Add(p);
						}
					}

					var sw = System.Diagnostics.Stopwatch.StartNew();
					foreach (var item in list) {
						try {
							bool exists = File.Exists(item.Path);
							if (!exists) {
								if (batchRecycled.Contains(item.Path))
									result.FreedBytes += Math.Max(0, item.SizeLong);
								// Already gone — still remove the entry and database record.
							}
							else if (permanent) {
								File.Delete(item.Path);
								result.FreedBytes += Math.Max(0, item.SizeLong);
							}
							else if (OperatingSystem.IsWindows()) {
								// The batch ran but this file is still there.
								throw new IOException("the shell did not move the file to the recycle bin");
							}
							else {
								// System trash, falling back to permanent delete (e.g.
								// cross-filesystem files where trashing means a full copy).
								if (!FileUtils.MoveToTrash(item.Path))
									File.Delete(item.Path);
								result.FreedBytes += Math.Max(0, item.SizeLong);
							}
							_engine.Duplicates.Remove(item);
							// Path-only entry — FileEntry(string) stats the file and throws once it's gone.
							ScanEngine.RemoveFromDatabase(new FileEntry { Path = item.Path });
							result.Done++;
						}
						catch (Exception ex) {
							result.Errors.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
							result.Failed++;
						}
						finally {
							FileOpCurrent++;
							if (sw.ElapsedMilliseconds >= 100) { sw.Restart(); Notify(); }
						}
					}
					if (result.Done > 0)
						ScanEngine.SaveDatabase();
					DropSingletonGroups();
				});
			}
			finally { EndFileOp(); }
			return result;
		}

		/// <summary>Moves files to a destination folder and updates the scan database paths.</summary>
		public async Task<FileOpResult> MoveItemsAsync(IEnumerable<DuplicateItem> items, string destinationFolder) {
			var list = items.ToList();
			var result = new FileOpResult();
			try { Directory.CreateDirectory(destinationFolder); }
			catch (Exception ex) {
				result.Errors.Add($"Cannot create destination folder: {ex.Message}");
				return result;
			}
			if (!TryBeginFileOp("Moving", list.Count))
				return result;
			try {
				await Task.Run(() => {
					var sw = System.Diagnostics.Stopwatch.StartNew();
					foreach (var item in list) {
						try {
							string dest = Path.Combine(destinationFolder, Path.GetFileName(item.Path));
							// Avoid overwriting existing files at destination
							int n = 1;
							string ext = Path.GetExtension(dest);
							string nameNoExt = Path.GetFileNameWithoutExtension(dest);
							while (File.Exists(dest))
								dest = Path.Combine(destinationFolder, $"{nameNoExt}_{n++}{ext}");
							File.Move(item.Path, dest);
							if (ScanEngine.GetFromDatabase(item.Path, out var dbEntry) && dbEntry != null)
								ScanEngine.UpdateFilePathInDatabase(dest, dbEntry);
							_engine.Duplicates.Remove(item);
							result.Done++;
						}
						catch (Exception ex) {
							result.Errors.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
							result.Failed++;
						}
						finally {
							FileOpCurrent++;
							if (sw.ElapsedMilliseconds >= 100) { sw.Restart(); Notify(); }
						}
					}
					if (result.Done > 0)
						ScanEngine.SaveDatabase();
					DropSingletonGroups();
				});
			}
			finally { EndFileOp(); }
			return result;
		}

		/// <summary>
		/// Replaces each selected file with a hardlink or symlink to the kept file of its
		/// group (the highest-similarity unselected member that still exists on disk).
		/// </summary>
		public async Task<FileOpResult> CreateLinksAsync(IEnumerable<DuplicateItem> items, bool hardLinks) {
			var list = items.ToList();
			var result = new FileOpResult();
			if (!TryBeginFileOp(hardLinks ? "Creating hardlinks" : "Creating symlinks", list.Count))
				return result;
			try {
				await Task.Run(() => {
					var selected = list.ToHashSet();
					var keeperByGroup = _engine.Duplicates
						.Where(d => !selected.Contains(d))
						.GroupBy(d => d.GroupId)
						.ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.Similarity).FirstOrDefault(d => File.Exists(d.Path)));

					var sw = System.Diagnostics.Stopwatch.StartNew();
					foreach (var item in list) {
						try {
							if (!File.Exists(item.Path)) {
								// Already gone — still remove the entry and database record.
							}
							else {
								keeperByGroup.TryGetValue(item.GroupId, out var keeper);
								if (keeper == null)
									throw new IOException("no unselected file is left in this group to link to");
								long size = Math.Max(0, item.SizeLong);
								// The link target path must be free before the link can be created.
								File.Delete(item.Path);
								if (hardLinks)
									HardLinkUtils.CreateHardLink(item.Path, keeper.Path);
								else
									File.CreateSymbolicLink(item.Path, keeper.Path);
								result.FreedBytes += size;
							}
							_engine.Duplicates.Remove(item);
							ScanEngine.RemoveFromDatabase(new FileEntry { Path = item.Path });
							result.Done++;
						}
						catch (Exception ex) {
							result.Errors.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
							result.Failed++;
						}
						finally {
							FileOpCurrent++;
							if (sw.ElapsedMilliseconds >= 100) { sw.Restart(); Notify(); }
						}
					}
					if (result.Done > 0)
						ScanEngine.SaveDatabase();
					DropSingletonGroups();
				});
			}
			finally { EndFileOp(); }
			return result;
		}

		/// <summary>Removes database entries for files that no longer exist or have errors.</summary>
		public async Task<int> CleanDatabaseAsync() {
			await ScanEngine.LoadDatabase();
			int before = DatabaseEntryCount;
			await Task.Run(() => _engine.CleanupDatabase());
			return before - DatabaseEntryCount;
		}

		/// <summary>Wipes all entries from the scan database.</summary>
		public async Task ClearDatabaseAsync() {
			await ScanEngine.LoadDatabase();
			ScanEngine.ClearDatabase();
			_engine.Duplicates.Clear();
			Notify();
		}

		/// <summary>Number of file entries currently stored in the scan database.</summary>
		public int DatabaseEntryCount => VDF.Core.Utils.DatabaseUtils.Database.Count;

		/// <summary>
		/// Runs the single-pair detection diagnostic with the current settings and
		/// returns the step-by-step report. See <see cref="ScanEngine.TestFilePairAsync"/>.
		/// </summary>
		public Task<string> TestFilePairAsync(string fileA, string fileB) {
			if (State == ScanState.Scanning || State == ScanState.Comparing)
				return Task.FromResult("A scan is currently running. Wait for it to finish before running the file pair test.");
			return _engine.TestFilePairAsync(fileA, fileB);
		}

		void Notify() => StateChanged?.Invoke();

		public void Dispose() => _cts.Dispose();
	}
}
