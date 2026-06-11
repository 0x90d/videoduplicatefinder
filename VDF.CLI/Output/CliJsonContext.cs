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
using VDF.Core.ViewModels;

namespace VDF.CLI.Output {
	/// <summary>
	/// Group shape written by the JSON results output and read back by the
	/// 'mark' command. One shared type keeps the two sides in lockstep.
	/// </summary>
	public sealed class DuplicateGroup {
		public Guid GroupId { get; set; }
		public List<DuplicateItem> Items { get; set; } = new();
	}

	/// <summary>
	/// Source-generated JSON metadata for the CLI's results format. Replaces
	/// reflection-based serialization (and the non-generic JsonStringEnumConverter)
	/// so the CLI can be published with Native AOT.
	/// </summary>
	[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
	[JsonSerializable(typeof(List<DuplicateGroup>))]
	internal partial class CliJsonContext : JsonSerializerContext { }
}
