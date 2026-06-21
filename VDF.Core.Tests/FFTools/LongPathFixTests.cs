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

using System.Runtime.InteropServices;
using VDF.Core.FFTools;

namespace VDF.Core.Tests.FFTools;

/// <summary>
/// Regression tests for #806: prefixing every path with the extended-length "\\?\"
/// form broke FFmpeg's image2 demuxer (the '?' is a glob metacharacter), so normal
/// still images failed to open. The prefix must only be added when the path actually
/// exceeds MAX_PATH.
/// </summary>
public class LongPathFixTests {
	static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	[Fact]
	public void ShortLocalPath_IsNotPrefixed() {
		// Must be byte-for-byte unchanged so FFmpeg's image2 demuxer never sees a '?'.
		const string p = @"C:\photos\holiday\IMG_1234.jpg";
		Assert.Equal(p, FFToolsUtils.LongPathFix(p));
	}

	[Fact]
	public void AlreadyExtendedPath_IsUnchanged() {
		const string p = @"\\?\C:\photos\IMG_1234.jpg";
		Assert.Equal(p, FFToolsUtils.LongPathFix(p));
	}

	[Fact]
	public void LongLocalPath_GetsExtendedPrefixOnWindows() {
		string p = @"C:\photos\" + new string('a', 300) + ".jpg";
		string result = FFToolsUtils.LongPathFix(p);

		if (IsWindows)
			Assert.Equal(@"\\?\" + p, result);
		else
			Assert.Equal(p, result); // no-op off Windows
	}

	[Fact]
	public void LongUncPath_GetsExtendedUncPrefixOnWindows() {
		string p = @"\\server\share\" + new string('b', 300) + ".jpg";
		string result = FFToolsUtils.LongPathFix(p);

		if (IsWindows)
			Assert.Equal(@"\\?\UNC\server\share\" + new string('b', 300) + ".jpg", result);
		else
			Assert.Equal(p, result); // no-op off Windows
	}
}
