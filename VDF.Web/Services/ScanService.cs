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

using VDF.Core;
using VDF.Core.ViewModels;

namespace VDF.Web.Services {
	public enum ScanState { Idle, Scanning, Comparing, RetrievingThumbnails, Done, Aborted, Error }

	public class ScanProgressArgs {
		public string CurrentFile { get; init; } = string.Empty;
		public int Current { get; init; }
		public int Max { get; init; }
		public TimeSpan Elapsed { get; init; }
		public TimeSpan Remaining { get; init; }
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
		/// <summary>Thumbnail retrieval progress.</summary>
		public int ThumbnailCurrent { get; private set; }
		public int ThumbnailMax { get; private set; }
		public IReadOnlyCollection<DuplicateItem> Duplicates => _engine.Duplicates;
		public Settings Settings => _engine.Settings;

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
					Remaining = e.Remaining
				};
				Notify();
			};
			_engine.ScanDone += (_, _) => {
				State = ScanState.RetrievingThumbnails;
				LastProgress = null;
				ThumbnailCurrent = 0;
				ThumbnailMax = _engine.Duplicates.Count;
				Notify();
				try { _engine.RetrieveThumbnails(); }
				catch (Exception ex) { SetError(ex); }
			};
			_engine.ThumbnailProgress += (current, max) => {
				ThumbnailCurrent = current;
				ThumbnailMax = max;
				Notify();
			};
			_engine.ThumbnailsRetrieved += (_, _) => {
				ThumbnailCurrent = ThumbnailMax;
				State = ScanState.Done;
				Notify();
			};
			_engine.ScanAborted += (_, _) => {
				State = ScanState.Aborted;
				LastProgress = null;
				Notify();
			};
		}

		public void StartScanAndCompare() {
			if (State == ScanState.Scanning || State == ScanState.Comparing || State == ScanState.RetrievingThumbnails) return;
			_cts = new CancellationTokenSource();
			State = ScanState.Scanning;
			ErrorMessage = null;
			LastProgress = null;
			FilesHashed = 0;
			ThumbnailCurrent = 0;
			ThumbnailMax = 0;
			_engine.Duplicates.Clear();
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
			if (State == ScanState.Scanning || State == ScanState.Comparing || State == ScanState.RetrievingThumbnails) return;
			State = ScanState.Idle;
			ErrorMessage = null;
			LastProgress = null;
			FilesHashed = 0;
			ThumbnailCurrent = 0;
			ThumbnailMax = 0;
			_engine.Duplicates.Clear();
			_engine.Settings.IncludeList.Clear();
			_engine.Settings.BlackList.Clear();
			Notify();
		}

		/// <summary>Removes items from the results list without touching the files on disk.</summary>
		public void RemoveFromResults(IEnumerable<DuplicateItem> items) {
			foreach (var item in items.ToList())
				_engine.Duplicates.Remove(item);
			Notify();
		}

		/// <summary>Deletes files from disk and removes them from results.</summary>
		public (int Deleted, int Failed, List<string> Errors) DeleteItems(IEnumerable<DuplicateItem> items, bool permanent) {
			int deleted = 0, failed = 0;
			var errors = new List<string>();
			foreach (var item in items.ToList()) {
				try {
					if (permanent)
						File.Delete(item.Path);
					else
						MoveToTrash(item.Path);
					_engine.Duplicates.Remove(item);
					deleted++;
				}
				catch (Exception ex) {
					errors.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
					failed++;
				}
			}
			Notify();
			return (deleted, failed, errors);
		}

		/// <summary>Moves files to a destination folder.</summary>
		public (int Moved, int Failed, List<string> Errors) MoveItems(IEnumerable<DuplicateItem> items, string destinationFolder) {
			int moved = 0, failed = 0;
			var errors = new List<string>();
			try { Directory.CreateDirectory(destinationFolder); }
			catch (Exception ex) { return (0, 0, new List<string> { $"Cannot create destination folder: {ex.Message}" }); }

			foreach (var item in items.ToList()) {
				try {
					string dest = Path.Combine(destinationFolder, Path.GetFileName(item.Path));
					// Avoid overwriting existing files at destination
					int n = 1;
					string ext = Path.GetExtension(dest);
					string nameNoExt = Path.GetFileNameWithoutExtension(dest);
					while (File.Exists(dest))
						dest = Path.Combine(destinationFolder, $"{nameNoExt}_{n++}{ext}");
					File.Move(item.Path, dest);
					_engine.Duplicates.Remove(item);
					moved++;
				}
				catch (Exception ex) {
					errors.Add($"{Path.GetFileName(item.Path)}: {ex.Message}");
					failed++;
				}
			}
			Notify();
			return (moved, failed, errors);
		}

		static void MoveToTrash(string path) {
			if (OperatingSystem.IsWindows()) {
				Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
					path,
					Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
					Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
			}
			else if (OperatingSystem.IsLinux()) {
				string trashDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					".local", "share", "Trash", "files");
				Directory.CreateDirectory(trashDir);
				// Skip trash for cross-filesystem files (e.g. network shares) to avoid downloading
				if (!VDF.Core.Utils.FileUtils.IsOnSameFileSystem(path, trashDir)) {
					File.Delete(path);
					return;
				}
				string dest = UniqueDestPath(trashDir, path);
				File.Move(path, dest);
			}
			else if (OperatingSystem.IsMacOS()) {
				string trashDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
				Directory.CreateDirectory(trashDir);
				// Skip trash for cross-volume files to avoid cross-volume copy
				if (!VDF.Core.Utils.FileUtils.IsOnSameFileSystem(path, trashDir)) {
					File.Delete(path);
					return;
				}
				string dest = UniqueDestPath(trashDir, path);
				File.Move(path, dest);
			}
			else {
				File.Delete(path);
			}
		}

		static string UniqueDestPath(string folder, string originalPath) {
			string fileName = Path.GetFileNameWithoutExtension(originalPath);
			string ext = Path.GetExtension(originalPath);
			string dest = Path.Combine(folder, Path.GetFileName(originalPath));
			int n = 1;
			while (File.Exists(dest))
				dest = Path.Combine(folder, $"{fileName}_{n++}{ext}");
			return dest;
		}

		void Notify() => StateChanged?.Invoke();

		public void Dispose() => _cts.Dispose();
	}
}
