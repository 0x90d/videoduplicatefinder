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

namespace VDF.Core.Utils {
	/// <summary>
	/// Source-generated JSON metadata for VDF.Core's serialized types. Replaces
	/// reflection-based System.Text.Json so the CLI can be published with Native AOT
	/// (reflection serialization is disabled there) — and it is faster everywhere else.
	/// IncludeFields matches the existing wire format: Settings, FileEntry and friends
	/// are field-heavy, and all existing call sites already passed IncludeFields=true.
	/// </summary>
	[JsonSourceGenerationOptions(IncludeFields = true, PropertyNameCaseInsensitive = true)]
	[JsonSerializable(typeof(Dictionary<string, string>))]
	[JsonSerializable(typeof(DatabaseWrapper))]
	[JsonSerializable(typeof(Settings))]
	[JsonSerializable(typeof(BlacklistStore.Envelope))]
	public partial class CoreJsonContext : JsonSerializerContext { }
}
