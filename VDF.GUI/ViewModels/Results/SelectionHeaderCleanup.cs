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

using System.Collections;

namespace VDF.GUI.ViewModels {
	/// <summary>
	/// Drops group headers that sneak into the results selection (marquee/range
	/// selection). Mutating the selection list raises SelectionChanged again
	/// synchronously, so the cleanup re-entered itself with stale indices and
	/// crashed the dispatcher (ArgumentOutOfRangeException storm); the guard
	/// turns nested calls into no-ops.
	/// </summary>
	internal sealed class SelectionHeaderCleanup {
		bool running;

		public void Run(IList? selected) {
			if (running || selected == null) return;
			running = true;
			try {
				for (int i = selected.Count - 1; i >= 0; i--)
					if (i < selected.Count && selected[i] is ResultsGroupHeader)
						selected.RemoveAt(i);
			}
			finally {
				running = false;
			}
		}
	}
}
