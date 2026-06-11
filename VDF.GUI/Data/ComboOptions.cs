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

using Avalonia.Collections;

namespace VDF.GUI.Data {
	// These lists were KeyValuePair<string, T> with reflection-bound item templates
	// (x:DataType cannot express closed generic types). Under Native AOT the
	// KeyValuePair property metadata is trimmed and the labels rendered empty, so
	// each list gets a small named type instead, compiled-bindable like everything else.

	public sealed class SortOrderOption {
		public string Name { get; }
		public DataGridSortDescription? Sort { get; }
		public SortOrderOption(string name, DataGridSortDescription? sort) {
			Name = name;
			Sort = sort;
		}
	}

	public sealed class FileTypeFilterOption {
		public string Name { get; }
		public FileTypeFilter Value { get; }
		public FileTypeFilterOption(string name, FileTypeFilter value) {
			Name = name;
			Value = value;
		}
	}

	public sealed class ThumbnailDoubleClickOption {
		public string Name { get; }
		public ThumbnailDoubleClickAction Value { get; }
		public ThumbnailDoubleClickOption(string name, ThumbnailDoubleClickAction value) {
			Name = name;
			Value = value;
		}
	}
}
