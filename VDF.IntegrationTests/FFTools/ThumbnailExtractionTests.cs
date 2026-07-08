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

[Collection("Ffmpeg")]
public class ThumbnailExtractionTests {
	readonly FfmpegFixture _fixture;

	public ThumbnailExtractionTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public void GetThumbnail_GrayScale_ReturnsExactly1024Bytes() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_8bit!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 1,
		}, extendedLogging: false);

		Assert.NotNull(result);
		Assert.Equal(1024, result.Length);
	}

	[SkippableFact]
	public void GetThumbnail_Jpeg_ReturnsValidJpeg() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_8bit!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 0,
			Fullsize = 0,
		}, extendedLogging: false);

		Assert.NotNull(result);
		Assert.True(result.Length > 2);
		// JPEG SOI marker
		Assert.Equal(0xFF, result[0]);
		Assert.Equal(0xD8, result[1]);
	}

	[SkippableFact]
	public void GetThumbnail_InvalidFile_ReturnsNull() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = Path.Combine(_fixture.TempDir, "nonexistent.mp4"),
			Position = TimeSpan.FromSeconds(0),
			GrayScale = 1,
		}, extendedLogging: false);

		Assert.Null(result);
	}

	[SkippableTheory]
	[InlineData(true)]
	[InlineData(false)]
	public void GetThumbnail_AnamorphicVideo_JpegHasDisplayDimensions(bool useNative) {
		if (useNative)
			Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		else
			Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_Anamorphic == null, "Anamorphic test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = useNative;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		// Fixture: 320x240 coded raster, SAR 2:1 → 640x240 display. A fullsize
		// thumbnail must come out at display dimensions, not the coded raster
		// (which renders visibly squished for DVD-style anamorphic content).
		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_Anamorphic!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 0,
			Fullsize = 1,
		}, extendedLogging: false);

		Assert.NotNull(result);
		var (width, height) = ReadJpegDimensions(result);
		Assert.Equal(640, width);
		Assert.Equal(240, height);
	}

	[SkippableTheory]
	[InlineData(true)]
	[InlineData(false)]
	public void GetThumbnail_SquarePixelVideo_JpegKeepsCodedDimensions(bool useNative) {
		if (useNative)
			Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		else
			Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = useNative;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		// Square pixels (SAR 1:1 or unset) must pass through unchanged.
		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_8bit!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 0,
			Fullsize = 1,
		}, extendedLogging: false);

		Assert.NotNull(result);
		var (width, height) = ReadJpegDimensions(result);
		Assert.Equal(320, width);
		Assert.Equal(240, height);
	}

	/// <summary>Reads the frame dimensions from a baseline/progressive JPEG's SOF segment.</summary>
	static (int Width, int Height) ReadJpegDimensions(byte[] jpeg) {
		int i = 2; // skip SOI
		while (i + 9 < jpeg.Length) {
			Assert.True(jpeg[i] == 0xFF, $"Expected JPEG marker at offset {i}");
			byte marker = jpeg[i + 1];
			// SOF0..SOF15 carry dimensions (excluding DHT/JPG/DAC pseudo-SOFs).
			if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC) {
				int height = (jpeg[i + 5] << 8) | jpeg[i + 6];
				int width = (jpeg[i + 7] << 8) | jpeg[i + 8];
				return (width, height);
			}
			int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
			i += 2 + length;
		}
		throw new InvalidOperationException("No SOF segment found in JPEG data");
	}

	[SkippableFact]
	public void GetThumbnail_GrayScale_Native_ReturnsExactly1024Bytes() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var result = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_8bit!,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 1,
		}, extendedLogging: false);

		Assert.NotNull(result);
		Assert.Equal(1024, result.Length);
	}
}
