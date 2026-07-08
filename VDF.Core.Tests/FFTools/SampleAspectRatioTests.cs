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

using System.Drawing;
using VDF.Core.FFTools;

namespace VDF.Core.Tests.FFTools;

// Display thumbnails of anamorphic videos must be widened by the sample (pixel)
// aspect ratio or they render squished; broken container metadata must never
// distort the output.
public class SampleAspectRatioTests {
	[Theory]
	[InlineData(720, 576, 16, 11, 1047)]  // PAL DVD 16:9
	[InlineData(720, 576, 12, 11, 785)]   // PAL DVD 4:3
	[InlineData(320, 240, 2, 1, 640)]     // integration fixture
	[InlineData(640, 480, 1, 2, 320)]     // tall pixels narrow the width
	public void AnamorphicWidthIsScaledBySar(int codedW, int codedH, int num, int den, int expectedW) {
		var result = FfmpegEngine.ApplySampleAspectRatio(new Size(codedW, codedH), num, den);
		Assert.Equal(expectedW, result.Width);
		Assert.Equal(codedH, result.Height);
	}

	[Theory]
	[InlineData(1, 1)]    // square pixels
	[InlineData(0, 1)]    // unknown SAR
	[InlineData(1, 0)]    // degenerate
	[InlineData(0, 0)]    // unset
	[InlineData(-4, 3)]   // corrupt
	[InlineData(500000, 1)] // implausible — would overflow the display width
	public void InvalidOrSquareSarLeavesSizeUnchanged(int num, int den) {
		var coded = new Size(720, 576);
		Assert.Equal(coded, FfmpegEngine.ApplySampleAspectRatio(coded, num, den));
	}
}
