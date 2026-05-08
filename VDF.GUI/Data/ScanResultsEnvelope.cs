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

using System.Text.Json.Serialization;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Data {
	/// <summary>
	/// Versioned envelope for the scan.json entry inside backup.scanresults / *.zip.
	/// Older builds wrote the raw <see cref="List{DuplicateItemVM}"/> at the document
	/// root; the import path still accepts that legacy shape.
	/// </summary>
	public sealed class ScanResultsEnvelope {
		public const int CurrentVersion = 1;

		[JsonPropertyName("version")]
		public int Version { get; set; } = CurrentVersion;
		[JsonPropertyName("items")]
		public List<DuplicateItemVM> Items { get; set; } = new();
	}
}
