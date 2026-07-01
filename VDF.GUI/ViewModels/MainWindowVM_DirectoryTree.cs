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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using VDF.Core.Utils;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM {
		ObservableCollection<DirectoryTreeNodeVM>? _directoryTreeRoots;

		// Drive-rooted, lazily-expanded folder tree behind the "Directory selection" settings tab.
		// Ticking a node adds its path to SettingsFile.Includes, unticking removes it (and its whole
		// subtree) — the same collection the classic list uses, so the two stay in sync both ways.
		// Built on first access so it costs nothing at startup.
		public ObservableCollection<DirectoryTreeNodeVM> DirectoryTreeRoots {
			get {
				if (_directoryTreeRoots != null)
					return _directoryTreeRoots;

				// Kick off the DB path index once (used for the "unscanned files" count per folder).
				DirectoryTreeNodeVM.DbIndexTask ??= Task.Run(DirectoryTreeNodeVM.LoadDbIndex);

				_directoryTreeRoots = new ObservableCollection<DirectoryTreeNodeVM>();
				var includes = SettingsFile.Instance.Includes;
				foreach (var d in DriveInfo.GetDrives()) {
					bool ready;
					try { ready = d.IsReady; } catch { ready = false; }
					if (!ready) continue;
					_directoryTreeRoots.Add(new DirectoryTreeNodeVM(d.Name, DriveDisplayName(d), includes, isDrive: true, drive: d));
				}
				// Reflect selection changes made through the classic Add/Remove list back onto the tree.
				includes.CollectionChanged += (_, _) => {
					foreach (var n in _directoryTreeRoots!)
						n.RefreshState();
				};
				return _directoryTreeRoots;
			}
		}

		static string DriveDisplayName(DriveInfo d) {
			try {
				string label = d.VolumeLabel;
				return string.IsNullOrWhiteSpace(label) ? d.Name : $"{d.Name} ({label})";
			}
			catch { return d.Name; }
		}
	}

	// One folder (or drive) node. Children are enumerated lazily on first expand; folder size and the
	// "unscanned files" count are computed on a throttled background walk so the UI never blocks.
	public sealed class DirectoryTreeNodeVM : ReactiveObject {
		static readonly StringComparison PathCmp =
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		// Colours (app runs the Dark theme). State drives the icon + name colour so the tree reads at a glance.
		static readonly IBrush IncludedBrush = new SolidColorBrush(Color.Parse("#66BB6A")); // green  = included
		static readonly IBrush PartialBrush  = new SolidColorBrush(Color.Parse("#FFB74D")); // amber  = partially included
		static readonly IBrush FolderBrush   = new SolidColorBrush(Color.Parse("#E6C15A")); // gold   = plain folder
		static readonly IBrush DriveBrush    = new SolidColorBrush(Color.Parse("#64B5F6")); // blue   = drive
		static readonly IBrush DefaultBrush  = new SolidColorBrush(Color.Parse("#DCDCDC")); // light  = plain name

		// Throttle the recursive size/unscanned walks so expanding a drive doesn't hammer the disk.
		static readonly SemaphoreSlim WalkGate = new(2);
		// Set of file paths already in the VDF database, for the per-folder "unscanned" count. null until loaded.
		static volatile HashSet<string>? _dbPaths;
		internal static Task? DbIndexTask;

		readonly ObservableCollection<string> _includes;
		readonly bool _isPlaceholder;
		bool _loaded;

		public string Path { get; }
		public string Name { get; }
		public bool IsDrive { get; }
		public ObservableCollection<DirectoryTreeNodeVM> Children { get; } = new();

		// Placeholder child: gives a not-yet-loaded node its expander arrow without enumerating; swapped
		// for the real children the instant the node expands, so it never actually renders.
		DirectoryTreeNodeVM() {
			_includes = new ObservableCollection<string>();
			_isPlaceholder = true;
			Path = string.Empty;
			Name = string.Empty;
		}

		public DirectoryTreeNodeVM(string path, string name, ObservableCollection<string> includes, bool isDrive, DriveInfo? drive) {
			Path = path;
			Name = name;
			IsDrive = isDrive;
			_includes = includes;

			if (isDrive && drive != null) {
				try {
					long total = drive.TotalSize;
					_sizeText = $"{FormatSize(total - drive.TotalFreeSpace)} / {FormatSize(total)}";
				}
				catch { _sizeText = string.Empty; }
			}
			else {
				_sizeText = "…";
				ComputeFolderStats();   // background, throttled
			}

			if (HasSubDirectories(path))
				Children.Add(new DirectoryTreeNodeVM());
		}

		bool _isExpanded;
		public bool IsExpanded {
			get => _isExpanded;
			set {
				this.RaiseAndSetIfChanged(ref _isExpanded, value);
				if (value)
					LoadChildren();
			}
		}

		// Tri-state selection. Displayed as a normal check / indeterminate fill / empty box.
		//   true  = this folder (or an ancestor) is in the include list  → whole subtree scanned
		//   null  = some descendant folder is included, but not this one → indeterminate
		//   false = nothing here is included
		// A click always toggles the WHOLE subtree (select-all ↔ clear-all), matching how file managers
		// behave — never a confusing three-way cycle.
		public bool? CheckState {
			get {
				if (SelfOrAncestorIncluded()) return true;
				if (DescendantIncluded()) return null;
				return false;
			}
			set {
				if (_isPlaceholder) return;
				if (SelfOrAncestorIncluded())
					ClearSubtree();     // was fully/partly selected → clear this folder and everything under it
				else
					SelectThis();       // was empty/partial       → select this whole folder
				// Includes.CollectionChanged → RefreshState() repaints every node's checkbox and colours.
			}
		}

		public IBrush IconBrush =>
			IsDrive ? DriveBrush :
			CheckState == true ? IncludedBrush :
			CheckState == null ? PartialBrush : FolderBrush;

		public IBrush NameBrush =>
			CheckState == true ? IncludedBrush :
			CheckState == null ? PartialBrush : DefaultBrush;

		public FontWeight NameWeight => CheckState == true ? FontWeight.SemiBold : FontWeight.Normal;

		string _sizeText = string.Empty;
		public string SizeText { get => _sizeText; private set => this.RaiseAndSetIfChanged(ref _sizeText, value); }

		string _missingText = string.Empty;
		public string MissingText {
			get => _missingText;
			private set {
				this.RaiseAndSetIfChanged(ref _missingText, value);
				this.RaisePropertyChanged(nameof(HasMissing));
			}
		}
		public bool HasMissing => _missingText.Length > 0;

		// Repaint this node (and any already-loaded descendants) from the current include list.
		internal void RefreshState() {
			if (_isPlaceholder) return;
			this.RaisePropertyChanged(nameof(CheckState));
			this.RaisePropertyChanged(nameof(IconBrush));
			this.RaisePropertyChanged(nameof(NameBrush));
			this.RaisePropertyChanged(nameof(NameWeight));
			if (_loaded)
				foreach (var c in Children)
					c.RefreshState();
		}

		// ── selection helpers ──────────────────────────────────────────────────────────────────────
		bool SelfOrAncestorIncluded() {
			foreach (var i in _includes)
				if (Eq(i, Path) || IsUnder(Path, i)) return true;
			return false;
		}
		bool DescendantIncluded() {
			foreach (var i in _includes)
				if (IsUnder(i, Path)) return true;
			return false;
		}
		void SelectThis() {
			// Drop now-redundant descendant entries, then add this folder (unless an ancestor already covers it).
			for (int i = _includes.Count - 1; i >= 0; i--)
				if (IsUnder(_includes[i], Path)) _includes.RemoveAt(i);
			if (!SelfOrAncestorIncluded()) _includes.Add(Path);
		}
		void ClearSubtree() {
			// Remove this folder and every included descendant. (Ancestor-wide includes are left alone —
			// carving a single child out of a whole-drive include would need the exclude list.)
			for (int i = _includes.Count - 1; i >= 0; i--) {
				var p = _includes[i];
				if (Eq(p, Path) || IsUnder(p, Path)) _includes.RemoveAt(i);
			}
		}

		// ── lazy children ──────────────────────────────────────────────────────────────────────────
		void LoadChildren() {
			if (_loaded) return;
			_loaded = true;
			Children.Clear();   // drop the placeholder
			try {
				foreach (var dir in Directory.EnumerateDirectories(Path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
					string leaf = System.IO.Path.GetFileName(dir);
					if (string.IsNullOrEmpty(leaf)) leaf = dir;
					Children.Add(new DirectoryTreeNodeVM(dir, leaf, _includes, isDrive: false, drive: null));
				}
			}
			catch { /* access denied / removed drive: leave it childless */ }
		}

		// ── size + unscanned count (background) ─────────────────────────────────────────────────────
		async void ComputeFolderStats() {
			(long size, int missing) result = (0, 0);
			try {
				if (DbIndexTask != null)
					await DbIndexTask.ConfigureAwait(false);
				await WalkGate.WaitAsync().ConfigureAwait(false);
				try {
					result = await Task.Run(() => WalkFolder(Path)).ConfigureAwait(false);
				}
				finally {
					WalkGate.Release();
				}
			}
			catch { return; }
			Dispatcher.UIThread.Post(() => {
				SizeText = FormatSize(result.size);
				MissingText = result.missing > 0
					? string.Format(App.Lang["MainWindow.Settings.DirTree.Unscanned"], result.missing)
					: string.Empty;
			});
		}

		static (long size, int missing) WalkFolder(string path) {
			long size = 0;
			int missing = 0;
			try {
				var opts = new EnumerationOptions {
					RecurseSubdirectories = true,
					IgnoreInaccessible = true,
					AttributesToSkip = FileAttributes.ReparsePoint
				};
				var db = _dbPaths;
				foreach (var fi in new DirectoryInfo(path).EnumerateFiles("*", opts)) {
					size += fi.Length;
					if (db != null && FileUtils.IsMediaExtension(System.IO.Path.GetExtension(fi.Name)) && !db.Contains(fi.FullName))
						missing++;
				}
			}
			catch { /* best-effort */ }
			return (size, missing);
		}

		internal static void LoadDbIndex() {
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try {
				if (DatabaseUtils.Database.Count == 0) {
					DatabaseUtils.CustomDatabaseFolder = SettingsFile.Instance.CustomDatabaseFolder;
					DatabaseUtils.InvalidateDatabaseFolder();
					DatabaseUtils.LoadDatabase();
				}
				foreach (var e in DatabaseUtils.Database)
					set.Add(e.Path);
			}
			catch { /* no DB yet: counts simply won't show */ }
			_dbPaths = set;
		}

		// ── small utils ────────────────────────────────────────────────────────────────────────────
		static bool Eq(string a, string b) => string.Equals(a, b, PathCmp);

		// True if 'candidate' is a strict descendant path of 'ancestor' ("D:\Videos" is under "D:\",
		// but "D:\VideosX" is NOT under "D:\Videos").
		static bool IsUnder(string candidate, string ancestor) {
			if (candidate.Length <= ancestor.Length || !candidate.StartsWith(ancestor, PathCmp))
				return false;
			if (ancestor.EndsWith(System.IO.Path.DirectorySeparatorChar) || ancestor.EndsWith(System.IO.Path.AltDirectorySeparatorChar))
				return true;   // drive roots ("D:\") already end in a separator
			char boundary = candidate[ancestor.Length];
			return boundary == System.IO.Path.DirectorySeparatorChar || boundary == System.IO.Path.AltDirectorySeparatorChar;
		}

		static bool HasSubDirectories(string path) {
			try { return Directory.EnumerateDirectories(path).Any(); }
			catch { return false; }
		}

		static string FormatSize(long bytes) {
			string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
			double s = bytes;
			int i = 0;
			while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
			return i == 0 ? $"{bytes} {units[0]}" : $"{s:0.#} {units[i]}";
		}
	}
}
