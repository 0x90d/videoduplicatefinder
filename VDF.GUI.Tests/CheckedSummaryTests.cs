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

using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	/// <summary>Action-bar "N checked from M groups" label (asked for in the #849 thread).</summary>
	public class CheckedSummaryTests {
		const string Plural = "{0} checked from {1} groups";
		const string Single = "{0} checked from 1 group";

		[Fact]
		public void MultipleGroups_UsesPluralFormat() =>
			Assert.Equal("5 checked from 3 groups", MainWindowVM.FormatCheckedSummary(5, 3, Plural, Single));

		[Fact]
		public void SingleGroup_UsesSingularFormat() =>
			Assert.Equal("2 checked from 1 group", MainWindowVM.FormatCheckedSummary(2, 1, Plural, Single));

		[Fact]
		public void NothingChecked_StaysPlural() =>
			// The action bar is hidden at 0 checked, but the text must still format sanely.
			Assert.Equal("0 checked from 0 groups", MainWindowVM.FormatCheckedSummary(0, 0, Plural, Single));

		[Fact]
		public void KoreanWordOrder_GroupCountFirst_IsSupported() =>
			// Locale formats may reorder the placeholders ({1} before {0}).
			Assert.Equal("3개 그룹에서 5개 선택됨", MainWindowVM.FormatCheckedSummary(5, 3, "{1}개 그룹에서 {0}개 선택됨", "1개 그룹에서 {0}개 선택됨"));
	}
}
