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

namespace VDF.Core.Tests;

public class FileUtilsTests {
	[Fact]
	public void IsPathFFmpegSafe_Empty_True() =>
		Assert.True(FileUtils.IsPathFFmpegSafe(string.Empty));

	[Fact]
	public void IsPathFFmpegSafe_AsciiPath_True() =>
		Assert.True(FileUtils.IsPathFFmpegSafe(@"C:\videos\clip.mp4"));

	[Fact]
	public void IsPathFFmpegSafe_BmpNonAscii_True() {
		// U+2019 RIGHT SINGLE QUOTATION MARK — the curly apostrophe in "Y'all"
		string path = "C:\\videos\\Y’all.mp4";
		Assert.True(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_ValidSurrogatePair_True() {
		// U+1F49A GREEN HEART, encoded in UTF-16 as the surrogate pair D83D DC9A
		string path = "C:\\videos\\💚.mp4";
		Assert.True(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_TwoAdjacentValidPairs_True() {
		// U+1F49A GREEN HEART followed by U+1FAF6 HEART HANDS
		string path = "C:\\videos\\💚🫶.mp4";
		Assert.True(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_LoneHighSurrogate_False() {
		// U+D83D high surrogate without a following low surrogate
		string path = "C:\\videos\\bad\uD83D.mp4";
		Assert.False(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_LoneLowSurrogate_False() {
		// U+DC9A low surrogate without a preceding high surrogate
		string path = "C:\\videos\\bad\uDC9A.mp4";
		Assert.False(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_HighSurrogateFollowedByNonLow_False() {
		// High surrogate followed by an ASCII letter (not a low surrogate)
		string path = "C:\\videos\\bad\uD83DA.mp4";
		Assert.False(FileUtils.IsPathFFmpegSafe(path));
	}

	[Theory]
	[InlineData("C:\\pics\\photo.jpg")]
	[InlineData("C:\\pics\\photo.JPEG")]
	[InlineData("/home/user/pics/photo.png")]
	[InlineData("photo.webp")]
	[InlineData("scan.heic")]
	public void IsImageFile_ImageExtensions_True(string path) =>
		Assert.True(FileUtils.IsImageFile(path));

	[Theory]
	[InlineData("C:\\videos\\clip.mp4")]
	[InlineData("clip.mkv")]
	[InlineData("noextension")]
	[InlineData("archive.zip")]
	public void IsImageFile_NonImage_False(string path) =>
		Assert.False(FileUtils.IsImageFile(path));
}
