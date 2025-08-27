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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VDF.GUI.Data {
	static class TempExtractionManager {
		static readonly List<TempDir> _live = new();

		public static TempDir Register(TempDir d) { lock (_live) _live.Add(d); return d; }

		public static void DisposeAll() {
			lock (_live) {
				foreach (var d in _live) { try { d.Dispose(); } catch { } }
				_live.Clear();
			}
		}
	}
	internal sealed class TempDir : IDisposable, IAsyncDisposable {
		public string Path { get; }
		bool _deleted;

		public TempDir(string? prefix = null) {
			string customTempDirectory = Environment.GetEnvironmentVariable("VDF_TMP_DIR") ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(customTempDirectory)) {
				try {
					if (!System.IO.Directory.Exists(customTempDirectory)) {
						System.IO.Directory.CreateDirectory(customTempDirectory);
					}
					Path = System.IO.Path.Combine(customTempDirectory, (prefix ?? "VDF-") + Guid.NewGuid().ToString("N"));
					System.IO.Directory.CreateDirectory(Path);
					return;
				}
				catch { }
			}
			Path = Directory.CreateTempSubdirectory(prefix ?? "VDF-").FullName;
		}

		public void Dispose() => TryDelete();
		public ValueTask DisposeAsync() { TryDelete(); return ValueTask.CompletedTask; }

		void TryDelete() {
			if (_deleted) return;
			try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
			catch { /* ignore: locked or already gone */ }
			_deleted = true;
		}
	}
}
