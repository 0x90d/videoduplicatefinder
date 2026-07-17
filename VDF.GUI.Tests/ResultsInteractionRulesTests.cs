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

using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	public class ResultsInteractionRulesTests {

		// Regression #849: the path line is the row's largest click target, so the first,
		// merely row-selecting click must never write the clipboard - users lost clipboard
		// content to plain row selection and pasted stale paths into rename dialogs.
		[Fact]
		public void FirstClickOnUnselectedRow_MustNotCopy() =>
			Assert.False(ResultsInteractionRules.ShouldCopyPathOnPointerPress(
				isLeftButton: true, rowWasAlreadySelected: false));

		[Fact]
		public void ClickOnAlreadySelectedRow_Copies() =>
			Assert.True(ResultsInteractionRules.ShouldCopyPathOnPointerPress(
				isLeftButton: true, rowWasAlreadySelected: true));

		[Fact]
		public void NonLeftButton_NeverCopies() {
			Assert.False(ResultsInteractionRules.ShouldCopyPathOnPointerPress(
				isLeftButton: false, rowWasAlreadySelected: true));
			Assert.False(ResultsInteractionRules.ShouldCopyPathOnPointerPress(
				isLeftButton: false, rowWasAlreadySelected: false));
		}

		[Fact]
		public async Task PathCopiedFlash_ShowsThenHides() {
			var vm = new DuplicateItemVM();
			var task = vm.FlashPathCopiedAsync(durationMs: 20);
			Assert.True(vm.PathCopiedFlash);
			await task;
			Assert.False(vm.PathCopiedFlash);
		}

		[Fact]
		public async Task PathCopiedFlash_RapidReCopy_ExtendsInsteadOfCuttingShort() {
			var vm = new DuplicateItemVM();
			var first = vm.FlashPathCopiedAsync(durationMs: 30);
			var second = vm.FlashPathCopiedAsync(durationMs: 200);
			await first;
			// The first flash's timer has expired, but the second copy is still fresh -
			// its badge must not be hidden by the stale timer.
			Assert.True(vm.PathCopiedFlash);
			await second;
			Assert.False(vm.PathCopiedFlash);
		}
	}
}
