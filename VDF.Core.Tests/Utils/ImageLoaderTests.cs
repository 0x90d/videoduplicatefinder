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

namespace VDF.Core.Tests.Utils;

public class ImageLoaderTests {
	[Theory]
	[InlineData(@"C:\photos\IMG_0001.heic")]
	[InlineData(@"C:\photos\IMG_0001.HEIC")]
	[InlineData("/photos/IMG_0001.heif")]
	[InlineData("/photos/IMG_0001.Heif")]
	public void RequiresFfmpegDecoding_HeicHeif_True(string path) =>
		Assert.True(ImageLoader.RequiresFfmpegDecoding(path));

	[Theory]
	[InlineData(@"C:\photos\IMG_0001.jpg")]
	[InlineData(@"C:\photos\IMG_0001.png")]
	[InlineData(@"C:\photos\IMG_0001.webp")]
	[InlineData(@"C:\videos\clip.mp4")]
	[InlineData(@"C:\photos\noextension")]
	public void RequiresFfmpegDecoding_OtherFormats_False(string path) =>
		Assert.False(ImageLoader.RequiresFfmpegDecoding(path));

	[Fact]
	public void HeicHeif_AreRecognisedAsImages() {
		Assert.Contains(".heic", FileUtils.ImageExtensions);
		Assert.Contains(".heif", FileUtils.ImageExtensions);
	}
}
