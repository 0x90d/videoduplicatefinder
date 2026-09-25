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

using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	/// <summary>Which row of a rebuilt results list takes over the keyboard focus.</summary>
	public class ResultsFocusKeeperTests {
		static readonly Guid G = Guid.NewGuid();
		static ResultsGroupHeader Header() => new() { GroupId = G, Rows = new List<ResultsItemRow>() };
		static DuplicateItemVM Item(string path) => new() { ItemInfo = new DuplicateItem { Path = path, GroupId = G } };

		[Fact]
		public void TheSameFile_KeepsTheFocus_AcrossNewRowObjects() {
			var a = Item("a"); var b = Item("b");
			var oldRow = new ResultsItemRow(b);
			var newRows = new List<object> { Header(), new ResultsItemRow(a), new ResultsItemRow(b) };

			var target = Assert.IsType<ResultsItemRow>(ResultsFocusKeeper.FindFocusTarget(oldRow, 1, newRows));
			Assert.Same(b, target.Item);
		}

		[Fact]
		public void TheSameGroupHeader_KeepsTheFocus() {
			var header = Header();
			var newHeader = Header();
			Assert.Same(newHeader, ResultsFocusKeeper.FindFocusTarget(header, 0, new List<object> { newHeader }));
		}

		[Fact]
		public void ADeletedFile_PassesTheFocusToWhatNowStandsInItsPlace() {
			var gone = new ResultsItemRow(Item("gone"));
			var next = new ResultsItemRow(Item("next"));
			var newRows = new List<object> { Header(), new ResultsItemRow(Item("first")), next };

			Assert.Same(next, ResultsFocusKeeper.FindFocusTarget(gone, 2, newRows));
			// Past the end of a shorter list: the last row.
			Assert.Same(next, ResultsFocusKeeper.FindFocusTarget(gone, 9, newRows));
		}

		[Fact]
		public void AClosedDetailsPanel_HandsTheFocusBackToItsFile() {
			var item = Item("a");
			var details = new ResultsDetailsRow(new ResultsItemRow(item));
			var fileRow = new ResultsItemRow(item);
			Assert.Same(fileRow, ResultsFocusKeeper.FindFocusTarget(details, 5, new List<object> { Header(), fileRow }));
		}

		[Fact]
		public void NothingFocused_OrAnEmptyList_MovesNothing() {
			Assert.Null(ResultsFocusKeeper.FindFocusTarget(null, -1, new List<object> { Header() }));
			Assert.Null(ResultsFocusKeeper.FindFocusTarget(new ResultsItemRow(Item("a")), 0, new List<object>()));
		}
	}
}
