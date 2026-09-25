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

using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// The metadata comparison window (#926) already filled, with every kind of cell: shared,
/// odd, missing and a file that could not be read, so the guards measure all of them.
/// </summary>
static class MetadataCompareSample {
	public static MetadataCompareWindow Window() {
		MetadataField C(string name, string value) => new(new(MetadataSectionKind.Container), name, value);
		var wrong = new List<MetadataField> { C("creation_time", "2023-08-15T12:34:56Z"), C("com.apple.quicktime.creationdate", "2023-08-15T12:34:56"), C("location", "+48.1+011.5/") };
		var right = new List<MetadataField> { C("creation_time", "2023-08-15T12:34:56Z"), C("com.apple.quicktime.creationdate", "2023-08-15T14:34:56+0200") };
		var items = new[] { "IMG_0412.MOV", "IMG_0412 (1).MOV", "IMG_0412 (2).MOV", "IMG_0412 (3).MOV" }
			.Select(name => new DuplicateItemVM(new DuplicateItem { Path = $@"Z:\does\not\exist\{name}" })).ToList();
		var vm = new MetadataCompareVM(items);
		vm.Apply(new IReadOnlyList<MetadataField>?[] { wrong, right, wrong, null });
		return new MetadataCompareWindow(vm);
	}
}
