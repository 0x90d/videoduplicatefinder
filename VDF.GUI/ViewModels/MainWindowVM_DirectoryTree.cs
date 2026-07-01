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
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM {
		ObservableCollection<DirectoryTreeNodeVM>? _directoryTreeRoots;

		// Drive-rooted, lazily-expanded folder tree backing the "Directory selection" settings tab.
		// Ticking a node adds its path to SettingsFile.Includes, unticking removes it — the very same
		// collection the classic list uses, so the two views stay in sync in both directions. Built on
		// first access (when the tab is first shown) so it costs nothing at startup.
		public ObservableCollection<DirectoryTreeNodeVM> DirectoryTreeRoots {
			get {
				if (_directoryTreeRoots != null)
					return _directoryTreeRoots;

				_directoryTreeRoots = new ObservableCollection<DirectoryTreeNodeVM>();
				var includes = SettingsFile.Instance.Includes;
				foreach (var d in DriveInfo.GetDrives()) {
					bool ready;
					try { ready = d.IsReady; } catch { ready = false; }
					if (!ready) continue;
					string name = d.Name;
					try { if (!string.IsNullOrWhiteSpace(d.VolumeLabel)) name = $"{d.Name} ({d.VolumeLabel})"; } catch { /* label unavailable */ }
					_directoryTreeRoots.Add(new DirectoryTreeNodeVM(d.Name, name, includes));
				}
				// Reflect changes made through the classic Add/Remove list back onto the tree's checkmarks.
				includes.CollectionChanged += (_, _) => {
					foreach (var n in _directoryTreeRoots!)
						n.RefreshFromIncludes();
				};
				return _directoryTreeRoots;
			}
		}
	}

	// One folder (or drive) node. Children are enumerated lazily the first time the node is expanded,
	// so the whole disk is never walked up front.
	public sealed class DirectoryTreeNodeVM : ReactiveObject {
		static readonly StringComparison PathCmp =
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		readonly ObservableCollection<string> _includes;
		readonly bool _isPlaceholder;
		bool _loaded;

		public string Path { get; }
		public string Name { get; }
		public ObservableCollection<DirectoryTreeNodeVM> Children { get; } = new();

		// Placeholder child: gives a not-yet-loaded node its expander arrow without enumerating. It's
		// swapped out for the real children the instant the node expands, so it never actually renders.
		DirectoryTreeNodeVM() {
			_includes = new ObservableCollection<string>();
			_isPlaceholder = true;
			Path = string.Empty;
			Name = string.Empty;
		}

		public DirectoryTreeNodeVM(string path, string name, ObservableCollection<string> includes) {
			Path = path;
			Name = name;
			_includes = includes;
			_isChecked = Contains(path);
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

		bool _isChecked;
		public bool IsChecked {
			get => _isChecked;
			set {
				if (_isChecked == value)
					return;
				this.RaiseAndSetIfChanged(ref _isChecked, value);
				if (value) {
					if (!Contains(Path))
						_includes.Add(Path);
				}
				else {
					for (int i = _includes.Count - 1; i >= 0; i--)
						if (string.Equals(_includes[i], Path, PathCmp))
							_includes.RemoveAt(i);
				}
				this.RaisePropertyChanged(nameof(HasIncludedDescendant));
			}
		}

		// A folder deeper in the tree is included even though this folder itself isn't — surfaced as a
		// "(partially included)" hint so a collapsed branch still signals that something inside is picked.
		public bool HasIncludedDescendant =>
			!_isChecked && !_isPlaceholder && _includes.Any(i => IsUnder(i, Path));

		// Refresh this node (and any already-loaded descendants) from the Includes collection without
		// mutating it — used when the classic list changes the selection out from under the tree.
		internal void RefreshFromIncludes() {
			if (_isPlaceholder)
				return;
			bool now = Contains(Path);
			if (now != _isChecked) {
				_isChecked = now;
				this.RaisePropertyChanged(nameof(IsChecked));
			}
			this.RaisePropertyChanged(nameof(HasIncludedDescendant));
			if (_loaded)
				foreach (var c in Children)
					c.RefreshFromIncludes();
		}

		void LoadChildren() {
			if (_loaded)
				return;
			_loaded = true;
			Children.Clear();   // drop the placeholder
			try {
				foreach (var dir in Directory.EnumerateDirectories(Path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
					string leaf = System.IO.Path.GetFileName(dir);
					if (string.IsNullOrEmpty(leaf))
						leaf = dir;
					Children.Add(new DirectoryTreeNodeVM(dir, leaf, _includes));
				}
			}
			catch { /* access denied / removed drive: leave it childless */ }
		}

		bool Contains(string path) {
			foreach (var i in _includes)
				if (string.Equals(i, path, PathCmp))
					return true;
			return false;
		}

		// True if 'candidate' is a strict descendant path of 'ancestor' (ancestor\...\candidate), so
		// "D:\Videos" is under "D:\" but "D:\VideosX" is NOT under "D:\Videos".
		static bool IsUnder(string candidate, string ancestor) {
			if (candidate.Length <= ancestor.Length || !candidate.StartsWith(ancestor, PathCmp))
				return false;
			// Drive roots ("D:\") already end in a separator; deeper folders ("D:\Videos") don't, so the
			// character right after the prefix must itself be a separator to count as a real boundary.
			if (ancestor.EndsWith(System.IO.Path.DirectorySeparatorChar) || ancestor.EndsWith(System.IO.Path.AltDirectorySeparatorChar))
				return true;
			char boundary = candidate[ancestor.Length];
			return boundary == System.IO.Path.DirectorySeparatorChar || boundary == System.IO.Path.AltDirectorySeparatorChar;
		}

		static bool HasSubDirectories(string path) {
			try { return Directory.EnumerateDirectories(path).Any(); }
			catch { return false; }
		}
	}
}
