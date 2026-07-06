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

using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Utils;

namespace VDF.GUI.ViewModels {

	/// <summary>One folder row on the Setup screen (included or excluded).</summary>
	public sealed class SetupFolderVM : ReactiveObject {
		public SetupFolderVM(string path, bool isExcluded) {
			Path = path;
			IsExcluded = isExcluded;
			IsNetwork = FolderCountingService.IsNetworkPath(path);
			string? root = null;
			try { root = System.IO.Path.GetPathRoot(path); } catch (Exception) { }
			DriveLabel = IsNetwork ? "＼＼" : string.IsNullOrEmpty(root) ? "＊" : root.TrimEnd('\\', '/');
		}

		public string Path { get; }
		public bool IsExcluded { get; }
		public bool IsNetwork { get; }
		/// <summary>Drive chip: "D:", "＼＼" for UNC, "＊" for patterns.</summary>
		public string DriveLabel { get; }

		string _MetaText = string.Empty;
		public string MetaText {
			get => _MetaText;
			set => this.RaiseAndSetIfChanged(ref _MetaText, value);
		}
		bool _ShowCountNow;
		/// <summary>Network locations are not walked automatically — opt-in link.</summary>
		public bool ShowCountNow {
			get => _ShowCountNow;
			set => this.RaiseAndSetIfChanged(ref _ShowCountNow, value);
		}
		bool _IsCounting;
		public bool IsCounting {
			get => _IsCounting;
			set => this.RaiseAndSetIfChanged(ref _IsCounting, value);
		}
		/// <summary>Instant count from the fingerprint database, before any folder walk.</summary>
		internal int? DbKnownCount;
	}

	public sealed class ScanProfileOptionVM : ReactiveObject {
		public ScanProfileOptionVM(ScanProfile value, string name, string description, string timeHint) {
			Value = value;
			Name = name;
			Description = description;
			TimeHint = timeHint;
		}
		public ScanProfile Value { get; }
		public string Name { get; }
		public string Description { get; }
		public string TimeHint { get; }
		bool _IsActive;
		public bool IsActive {
			get => _IsActive;
			set => this.RaiseAndSetIfChanged(ref _IsActive, value);
		}
	}

	// Setup + Scanning window states (redesign stage 2). Review state is the results view.
	public partial class MainWindowVM : ReactiveObject {

		// ---------- state switching ----------
		public bool IsSetupState => !IsScanning && Duplicates.Count == 0;
		public bool IsScanningState => IsScanning;
		public bool IsReviewState => !IsScanning && Duplicates.Count > 0;

		void RaiseScannerStateChanged() {
			this.RaisePropertyChanged(nameof(IsSetupState));
			this.RaisePropertyChanged(nameof(IsScanningState));
			this.RaisePropertyChanged(nameof(IsReviewState));
		}

		// ---------- welcome strip ----------
		public ReactiveCommand<Unit, Unit> DismissWelcomeStripCommand => ReactiveCommand.Create(() => {
			SettingsFile.Instance.WelcomeStripDismissed = true;
		});

		// ---------- folder list ----------
		public ObservableCollection<SetupFolderVM> SetupFolders { get; } = new();
		readonly FolderCountingService folderCounting = new();
		// Completed walks per folder; survives list rebuilds within the session.
		readonly Dictionary<string, FolderCountProgress> folderCountCache = new(StringComparer.OrdinalIgnoreCase);

		string _SetupFootnote = string.Empty;
		public string SetupFootnote {
			get => _SetupFootnote;
			set => this.RaiseAndSetIfChanged(ref _SetupFootnote, value);
		}
		string _DatabaseInfoText = string.Empty;
		public string DatabaseInfoText {
			get => _DatabaseInfoText;
			set => this.RaiseAndSetIfChanged(ref _DatabaseInfoText, value);
		}

		internal void RebuildSetupFolders() {
			SetupFolders.Clear();
			foreach (var path in SettingsFile.Instance.Includes)
				SetupFolders.Add(new SetupFolderVM(path, isExcluded: false));
			foreach (var path in SettingsFile.Instance.Blacklists)
				SetupFolders.Add(new SetupFolderVM(path, isExcluded: true) {
					MetaText = App.Lang["Setup.Excluded"]
				});

			SetupFootnote = string.Format(App.Lang["Setup.LocationsFootnote"], SettingsFile.Instance.Includes.Count);

			foreach (var folder in SetupFolders.Where(f => !f.IsExcluded))
				StartFolderStats(folder);

			RefreshDatabaseInfo();
		}

		void RefreshDatabaseInfo() {
			Task.Run(() => ScanEngine.DatabaseEntryCount).ContinueWith(t =>
				Dispatcher.UIThread.Post(() => {
					if (t.IsCompletedSuccessfully)
						DatabaseInfoText = string.Format(App.Lang["Setup.DatabaseInfo"], t.Result.ToString("N0"));
				}));
		}

		void StartFolderStats(SetupFolderVM folder) {
			if (folderCountCache.TryGetValue(folder.Path, out var cached)) {
				ApplyFinalCount(folder, cached);
				return;
			}

			// DB-known count is instant and never blocks anything.
			Task.Run(() => ScanEngine.CountDatabaseEntriesUnder(folder.Path)).ContinueWith(t =>
				Dispatcher.UIThread.Post(() => {
					if (!t.IsCompletedSuccessfully) return;
					folder.DbKnownCount = t.Result;
					// Only fill the meta line while nothing better is known yet.
					if (!folder.IsCounting && string.IsNullOrEmpty(folder.MetaText) && t.Result > 0)
						folder.MetaText = string.Format(App.Lang["Setup.MetaKnown"], t.Result.ToString("N0"));
				}));

			if (folder.IsNetwork) {
				folder.MetaText = App.Lang["Setup.NetworkNotCounted"];
				folder.ShowCountNow = true;
				return;
			}
			BeginFolderWalk(folder);
		}

		void BeginFolderWalk(SetupFolderVM folder) {
			folder.ShowCountNow = false;
			folder.IsCounting = true;
			folder.MetaText = string.Format(App.Lang["Setup.CountingSoFar"], 0);
			bool started = folderCounting.StartCounting(folder.Path, progress =>
				Dispatcher.UIThread.Post(() => {
					if (!progress.Completed) {
						folder.MetaText = string.Format(App.Lang["Setup.CountingSoFar"], progress.FileCount.ToString("N0"));
						return;
					}
					folderCountCache[folder.Path] = progress;
					// The rebuilt list may hold a NEW VM for this path by now.
					var target = SetupFolders.FirstOrDefault(f =>
						string.Equals(f.Path, folder.Path, StringComparison.OrdinalIgnoreCase)) ?? folder;
					target.IsCounting = false;
					ApplyFinalCount(target, progress);
				}));
			if (!started)
				folder.IsCounting = folderCounting.IsCounting(folder.Path);
		}

		void ApplyFinalCount(SetupFolderVM folder, FolderCountProgress result) {
			if (result.Failed) {
				folder.MetaText = App.Lang["Setup.CountFailed"];
				return;
			}
			string text = string.Format(App.Lang["Setup.MetaCounted"],
				result.FileCount.ToString("N0"), result.TotalBytes.BytesToString());
			if (folder.DbKnownCount is int known && result.FileCount - known > 0)
				text += " · " + string.Format(App.Lang["Setup.MetaNotScanned"], (result.FileCount - known).ToString("N0"));
			folder.MetaText = text;
		}

		public ReactiveCommand<SetupFolderVM, Unit> CountFolderNowCommand => ReactiveCommand.Create<SetupFolderVM>(folder => {
			if (folder != null && !folder.IsCounting)
				BeginFolderWalk(folder);
		});

		public ReactiveCommand<SetupFolderVM, Unit> RemoveSetupFolderCommand => ReactiveCommand.Create<SetupFolderVM>(folder => {
			if (folder == null) return;
			if (folder.IsExcluded)
				SettingsFile.Instance.Blacklists.Remove(folder.Path);
			else {
				folderCounting.Cancel(folder.Path);
				SettingsFile.Instance.Includes.Remove(folder.Path);
			}
		});

		// ---------- scan profiles ----------
		public ScanProfileOptionVM[] ScanProfileOptions { get; } = {
			new(ScanProfile.ExactAndNear, App.Lang["Profile.Exact.Name"], App.Lang["Profile.Exact.Desc"], App.Lang["Profile.Exact.Time"]),
			new(ScanProfile.EditedAndAltered, App.Lang["Profile.Edited.Name"], App.Lang["Profile.Edited.Desc"], App.Lang["Profile.Edited.Time"]),
			new(ScanProfile.DeepClean, App.Lang["Profile.Deep.Name"], App.Lang["Profile.Deep.Desc"], App.Lang["Profile.Deep.Time"]),
			new(ScanProfile.Custom, App.Lang["Profile.Custom.Name"], App.Lang["Profile.Custom.Desc"], string.Empty),
		};

		internal void RefreshScanProfileSelection() {
			var active = ScanProfileMapper.Detect(SettingsFile.Instance);
			foreach (var option in ScanProfileOptions)
				option.IsActive = option.Value == active;
		}

		public ReactiveCommand<ScanProfileOptionVM, Unit> SelectScanProfileCommand => ReactiveCommand.Create<ScanProfileOptionVM>(option => {
			if (option == null) return;
			ScanProfileMapper.Apply(option.Value, SettingsFile.Instance);
			RefreshScanProfileSelection();
		});

		// ---------- scanning state ----------
		string _ScanStageText = string.Empty;
		/// <summary>Localizable-free stage chip text, e.g. "Scanning files 27/819".</summary>
		public string ScanStageText {
			get => _ScanStageText;
			set => this.RaiseAndSetIfChanged(ref _ScanStageText, value);
		}
		string _ScanCurrentFile = string.Empty;
		public string ScanCurrentFile {
			get => _ScanCurrentFile;
			set => this.RaiseAndSetIfChanged(ref _ScanCurrentFile, value);
		}

		/// <summary>Last few log lines, shown under the scan card.</summary>
		public ObservableCollection<string> LogTail { get; } = new();
		internal const int LogTailLength = 4;
		internal void AppendLogTail(string message) {
			LogTail.Add(message);
			while (LogTail.Count > LogTailLength)
				LogTail.RemoveAt(0);
		}
	}
}
