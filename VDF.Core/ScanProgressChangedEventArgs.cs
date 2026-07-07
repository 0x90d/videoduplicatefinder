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

using System;

namespace VDF.Core {
	public struct ScanProgressChangedEventArgs {

		public string CurrentFile;
		public int CurrentPosition;
		public int MaxPosition;
		public TimeSpan Elapsed;
		public TimeSpan Remaining;
		/// <summary>Short label describing what's happening to CurrentFile right now (e.g. "probing", "sampling frames", "audio fingerprint"). Empty between files.</summary>
		public string CurrentStage;
		/// <summary>Progress within the current stage (e.g. sample 2 of 5). Both zero when stage progress isn't tracked.</summary>
		public int StageCurrent;
		public int StageMax;
		/// <summary>
		/// Per-drive progress during the analysis phase; null in every other phase (and for
		/// consumers that don't care — CLI/Web render the global numbers only). Drives whose
		/// files are all out of scan scope are omitted.
		/// </summary>
		public DriveProgress[]? Drives;
	}

	/// <summary>One drive's slice of the analysis phase, for per-drive progress display.</summary>
	public struct DriveProgress {
		public string Root;
		public long TotalBytes;
		public long DoneBytes;
		public int TotalFiles;
		public int DoneFiles;
		/// <summary>true = fast (SSD/NVMe), false = slow (HDD/network share), null = not classified (strictly serial scan).</summary>
		public bool? IsFastDrive;
	}
}
