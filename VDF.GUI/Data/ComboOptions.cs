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
	// These lists were KeyValuePair<string, T> with reflection-bound item templates
	// (x:DataType cannot express closed generic types). Under Native AOT the
	// KeyValuePair property metadata is trimmed and the labels rendered empty, so
	// each list gets a small named type instead, compiled-bindable like everything else.

	public sealed class FileTypeFilterOption {
		public string Name { get; }
		public FileTypeFilter Value { get; }
		public FileTypeFilterOption(string name, FileTypeFilter value) {
			Name = name;
			Value = value;
		}
	}

	/// <summary>One entry of the theme combo: the translated name and the mode it stands for.</summary>
	public sealed class ThemeModeOption {
		public string Name { get; }
		public ThemeMode Value { get; }
		public ThemeModeOption(string name, ThemeMode value) {
			Name = name;
			Value = value;
		}
		public override string ToString() => Name;
	}

	/// <summary>One entry of the interface size combo. Percent 0 follows the system.</summary>
	public sealed class UiScaleOption {
		public string Name { get; }
		public int Percent { get; }
		public UiScaleOption(string name, int percent) {
			Name = name;
			Percent = percent;
		}
		public override string ToString() => Name;
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
