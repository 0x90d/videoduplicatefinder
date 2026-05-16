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
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// End-to-end coverage for HEIC/HEIF support: ImageSharp cannot decode HEIC, so
/// <see cref="ImageLoader"/> routes it through FFmpeg, and the EXIF-creation-date
/// feature falls back to FFprobe's container creation_time tag.
/// </summary>
[Collection("Ffmpeg")]
public class HeicSupportTests {
	readonly FfmpegFixture _fixture;

	public HeicSupportTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public void ImageLoader_Load_Heic_ProcessMode_DecodesToExpectedDimensions() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.SampleHeic == null, "sample.heic test asset missing");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		using var image = ImageLoader.Load(_fixture.SampleHeic!);

		Assert.Equal(96, image.Width);
		Assert.Equal(72, image.Height);
	}

	[SkippableFact]
	public void ImageLoader_Load_Heic_NativeMode_DecodesToExpectedDimensions() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.SampleHeic == null, "sample.heic test asset missing");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		using var image = ImageLoader.Load(_fixture.SampleHeic!);

		Assert.Equal(96, image.Width);
		Assert.Equal(72, image.Height);
	}

	[SkippableFact]
	public void ExtractThumbnailJpeg_Heic_ReturnsValidJpeg() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.SampleHeic == null, "sample.heic test asset missing");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		var jpeg = FfmpegEngine.ExtractThumbnailJpeg(_fixture.SampleHeic!, TimeSpan.Zero);

		Assert.NotNull(jpeg);
		Assert.True(jpeg!.Length > 2);
		// JPEG SOI marker
		Assert.Equal(0xFF, jpeg[0]);
		Assert.Equal(0xD8, jpeg[1]);
	}

	[SkippableFact]
	public void ImageLoader_Load_NonHeic_BypassesFfmpeg() {
		// A .png path must never go through the FFmpeg branch — ImageSharp decodes it
		// directly even when no FFmpeg is present.
		Assert.False(ImageLoader.RequiresFfmpegDecoding("whatever.png"));
		Assert.True(ImageLoader.RequiresFfmpegDecoding(_fixture.SampleHeic ?? "x.heic"));
	}

	[SkippableFact]
	public void GetCreationTime_FileWithTag_ReturnsStampedDate() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.Mp4WithCreationTime == null, "creation_time fixture not generated");

		var result = FFProbeEngine.GetCreationTime(_fixture.Mp4WithCreationTime!);

		Assert.NotNull(result);
		Assert.Equal(new DateTime(2023, 8, 15, 12, 34, 56, DateTimeKind.Utc), result!.Value);
		Assert.Equal(DateTimeKind.Utc, result.Value.Kind);
	}

	[SkippableFact]
	public void GetCreationTime_FileWithoutTag_ReturnsNull() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.SampleHeic == null, "sample.heic test asset missing");

		// The checked-in HEIC carries no container creation_time tag.
		Assert.Null(FFProbeEngine.GetCreationTime(_fixture.SampleHeic!));
	}
}
