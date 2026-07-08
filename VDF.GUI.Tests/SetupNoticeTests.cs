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

namespace VDF.GUI.Tests;

// A completed scan that matched nothing returns to the Setup screen; the banner tells
// that outcome apart from the never-scanned state.
public class SetupNoticeTests {

	[Fact]
	public void EmptyResult_ShowsNotice() {
		Assert.True(SetupNotice.ShowAfterScanDone(0));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(1000)]
	public void FoundDuplicates_HidesNotice(int duplicateCount) {
		Assert.False(SetupNotice.ShowAfterScanDone(duplicateCount));
	}
}
