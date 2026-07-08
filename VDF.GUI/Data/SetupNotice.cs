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

namespace VDF.GUI.Data {

	/// <summary>
	/// Decides the Setup screen's "no duplicates found" banner. When a scan completes with
	/// an empty result set the window drops back to the Setup screen, which is otherwise
	/// identical to the never-scanned state — a user returning to the app after a while
	/// cannot tell a scan actually ran and matched nothing. The banner makes that outcome
	/// explicit. Kept out of MainWindowVM (which needs the Avalonia runtime) so the decision
	/// stays unit-testable.
	/// </summary>
	public static class SetupNotice {
		/// <summary>
		/// Whether the banner should be raised after a scan reports done. Only a genuinely
		/// completed scan reaches this — a stopped or failed scan raises ScanAborted instead —
		/// so an empty result set here means the scan finished and matched nothing.
		/// </summary>
		public static bool ShowAfterScanDone(int duplicateCount) => duplicateCount == 0;
	}
}
