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

using VDF.Core.FFTools;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// Covers <see cref="FfmpegEngine.EncodeJpegFromBgra"/>, the encoder behind the
/// GUI's composed thumbnail-strip cache (replaces the old ImageSharp JPEG encode).
/// </summary>
[Collection("Ffmpeg")]
public class JpegEncodeTests {
	readonly FfmpegFixture _fixture;

	public JpegEncodeTests(FfmpegFixture fixture) => _fixture = fixture;

	static byte[] SolidBgra(int width, int height, byte b, byte g, byte r) {
		var buf = new byte[width * height * 4];
		for (int i = 0; i < buf.Length; i += 4) {
			buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = 255;
		}
		return buf;
	}

	static void AssertIsJpeg(byte[]? jpeg) {
		Assert.NotNull(jpeg);
		Assert.True(jpeg!.Length > 4);
		Assert.Equal(0xFF, jpeg[0]); // SOI
		Assert.Equal(0xD8, jpeg[1]);
		Assert.Equal(0xFF, jpeg[^2]); // EOI
		Assert.Equal(0xD9, jpeg[^1]);
	}

	[SkippableFact]
	public void EncodeJpegFromBgra_ProcessMode_ProducesValidJpeg() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;

		AssertIsJpeg(FfmpegEngine.EncodeJpegFromBgra(SolidBgra(64, 48, 30, 60, 200), 64, 48));
	}

	[SkippableFact]
	public void EncodeJpegFromBgra_NativeMode_ProducesValidJpeg() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;

		AssertIsJpeg(FfmpegEngine.EncodeJpegFromBgra(SolidBgra(64, 48, 30, 60, 200), 64, 48));
	}

	[SkippableFact]
	public void EncodeJpegFromBgra_MaxWidth_Downscales() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;

		var full = FfmpegEngine.EncodeJpegFromBgra(SolidBgra(400, 100, 0, 0, 0), 400, 100);
		var scaled = FfmpegEngine.EncodeJpegFromBgra(SolidBgra(400, 100, 0, 0, 0), 400, 100, maxWidth: 50);
		AssertIsJpeg(full);
		AssertIsJpeg(scaled);
		Assert.True(scaled!.Length < full!.Length, "downscaled strip should encode smaller");
	}

	[Fact]
	public void EncodeJpegFromBgra_RejectsInvalidInput() {
		Assert.Null(FfmpegEngine.EncodeJpegFromBgra(null!, 10, 10));
		Assert.Null(FfmpegEngine.EncodeJpegFromBgra(new byte[8], 10, 10)); // too small
		Assert.Null(FfmpegEngine.EncodeJpegFromBgra(new byte[400], 0, 10));
	}
}
