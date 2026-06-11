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
	/// Source-generated JSON metadata for the GUI's serialized types (settings, language
	/// files, selection presets, cleanup reports). Replaces reflection-based
	/// System.Text.Json as groundwork for publishing the GUI with Native AOT.
	/// These types were always serialized with default options — no IncludeFields here,
	/// so the wire format is unchanged; field-based types live in
	/// <see cref="GuiJsonFieldsContext"/>.
	/// </summary>
	[JsonSerializable(typeof(SettingsFile))]
	[JsonSerializable(typeof(Dictionary<string, string>))]
	[JsonSerializable(typeof(CustomSelectionData))]
	[JsonSerializable(typeof(CleanupDryRunReport))]
	internal partial class GuiJsonContext : JsonSerializerContext { }

	/// <summary>
	/// Counterpart of <see cref="GuiJsonContext"/> for the call sites that always passed
	/// IncludeFields=true: scan-results backups/exports and the thumbnail pack index
	/// (whose dictionary values are tuples, serialized through their Item1/Item2 fields).
	/// </summary>
	[JsonSourceGenerationOptions(IncludeFields = true)]
	[JsonSerializable(typeof(ScanResultsEnvelope))]
	[JsonSerializable(typeof(List<DuplicateItemVM>))]
	[JsonSerializable(typeof(Dictionary<string, ValueTuple<long, int>>), TypeInfoPropertyName = "ThumbPackIndex")]
	internal partial class GuiJsonFieldsContext : JsonSerializerContext { }
}
