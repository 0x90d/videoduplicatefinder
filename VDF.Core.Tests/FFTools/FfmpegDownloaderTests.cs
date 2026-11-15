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
using VDF.Core.Utils;

namespace VDF.Core.Tests.FFTools;

public class FfmpegDownloaderTests {

	[Theory]
	[InlineData(63, 63, 61, 9)]
	[InlineData(62, 62, 60, 8)]
	[InlineData(61, 61, 59, 7)]
	[InlineData(60, 60, 58, 6)]
	[InlineData(59, 59, 57, 5)]
	[InlineData(58, 58, 56, 4)]
	[InlineData(0, 0, 0, 0)]
	[InlineData(59, 62, 60, 8)] // mixed headers: the newest major wins
	public void MapToFfmpegMajor_MapsLibraryMajorsToFfmpegMajor(int avcodec, int avformat, int avutil, int expected) =>
		Assert.Equal(expected, FfmpegDownloader.MapToFfmpegMajor(avcodec, avformat, avutil));

	[Fact]
	public void TheBindingIsFfmpeg9_AndDownloadsAPublishedTag() {
		// FFmpeg.AutoGen 9 loads avcodec-63 & co.; the downloader must fetch the matching build.
		int major = FfmpegDownloader.MapToFfmpegMajor(
			FFmpeg.AutoGen.ffmpeg.LIBAVCODEC_VERSION_MAJOR, FFmpeg.AutoGen.ffmpeg.LIBAVFORMAT_VERSION_MAJOR, FFmpeg.AutoGen.ffmpeg.LIBAVUTIL_VERSION_MAJOR);
		Assert.Equal(9, major);
		Assert.Equal("9.0", FfmpegDownloader.VersionTagForMajor(major));
	}

	[Theory]
	[InlineData(9, "9.0")]
	[InlineData(8, "8.1")]
	[InlineData(7, "7.1")]
	[InlineData(6, "6.1")]
	[InlineData(5, "5.1")]
	[InlineData(0, "7.1")] // unknown headers fall back to a tag that exists
	public void VersionTagForMajor_MapsToPublishedBtbNTags(int major, string expected) =>
		Assert.Equal(expected, FfmpegDownloader.VersionTagForMajor(major));

	static FfmpegDownloader.DownloadOS ParseOs(string os) => Enum.Parse<FfmpegDownloader.DownloadOS>(os);

	[Theory]
	[InlineData("Windows", Architecture.X64, "win64", "zip")]
	[InlineData("Windows", Architecture.Arm64, "winarm64", "zip")]
	[InlineData("Linux", Architecture.X64, "linux64", "tar.xz")]
	[InlineData("Linux", Architecture.Arm64, "linuxarm64", "tar.xz")]
	public void GetDownloadPlans_BuildsTheExpectedReleaseUrl(string os, Architecture arch, string rid, string ext) {
		var plans = FfmpegDownloader.GetDownloadPlans(ParseOs(os), arch, "9.0");

		var plan = Assert.Single(plans);
		Assert.Equal($"ffmpeg-n9.0-latest-{rid}-gpl-shared-9.0.{ext}", plan.ArchiveFileName);
		Assert.Equal(ext == "zip" ? ArchiveKind.Zip : ArchiveKind.TarXz, plan.ArchiveKind);
		Assert.Contains($"github.com/BtbN/FFmpeg-Builds/releases/download/latest/{plan.ArchiveFileName}", plan.DownloadUrl.AbsoluteUri);
		Assert.Contains("9.0", plan.DisplayName);
	}

	[Theory]
	[InlineData("OSX", Architecture.X64)] // nobody publishes shared macOS builds (yt-dlp: "no MacOS builds")
	[InlineData("OSX", Architecture.Arm64)]
	[InlineData("Windows", Architecture.X86)] // BtbN dropped the 32-bit builds before 8.1
	[InlineData("Linux", Architecture.X86)]
	[InlineData("Linux", Architecture.Arm)] // and armhf
	[InlineData("Windows", Architecture.Arm)]
	public void GetDownloadPlans_ReturnsEmptyWhereNoBuildIsPublished(string os, Architecture arch) =>
		Assert.Empty(FfmpegDownloader.GetDownloadPlans(ParseOs(os), arch, "9.0"));

	[Fact]
	public void GetDownloadPlans_ForCurrentPlatformMatchesWhatIsPublished() {
		// Windows and Linux on x64/arm64 have a build; macOS has none and points to Homebrew.
		if (OperatingSystem.IsMacOS())
			Assert.Empty(FfmpegDownloader.GetDownloadPlans());
		else
			Assert.NotEmpty(FfmpegDownloader.GetDownloadPlans());
	}

	[Fact]
	public void TryParseExpectedChecksum_FindsTheArchiveEntry() {
		const string listing =
			"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip\n" +
			"FEDCBA9876543210fedcba9876543210fedcba9876543210fedcba9876543210  ffmpeg-n8.1-latest-linux64-gpl-shared-8.1.tar.xz\n";

		Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
			FfmpegDownloader.TryParseExpectedChecksum(listing, "ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip"));
		// hash is lower-cased, filename match is case-insensitive
		Assert.Equal("fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
			FfmpegDownloader.TryParseExpectedChecksum(listing, "FFMPEG-N8.1-LATEST-LINUX64-GPL-SHARED-8.1.TAR.XZ"));
	}

	[Fact]
	public void TryParseExpectedChecksum_HandlesCrLfAndMissingEntries() {
		const string listing = "abc123  some-archive.zip\r\nother  another.zip\r\n";
		Assert.Equal("abc123", FfmpegDownloader.TryParseExpectedChecksum(listing, "some-archive.zip"));
		Assert.Null(FfmpegDownloader.TryParseExpectedChecksum(listing, "absent.zip"));
		Assert.Null(FfmpegDownloader.TryParseExpectedChecksum(string.Empty, "some-archive.zip"));
	}
}
