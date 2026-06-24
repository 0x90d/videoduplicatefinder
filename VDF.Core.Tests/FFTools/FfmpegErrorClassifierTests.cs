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

namespace VDF.Core.Tests.FFTools;

/// <summary>
/// Pins the mapping from raw FFmpeg diagnostics onto a coarse cause category and hint. This is
/// what lets a native-binding failure report "your GPU can't decode this" / "the file is corrupt"
/// instead of an opaque av_strerror string, so most issue reports become self-service.
/// </summary>
public class FfmpegErrorClassifierTests {
	[Theory]
	// The exact lines from issue #812 (GTX 960 + 4K HEVC).
	[InlineData("[h264 @ 0x0] Hardware is lacking required capabilities")]
	[InlineData("[h264 @ 0x0] Failed setup for format cuda: hwaccel initialisation returned error")]
	[InlineData("Failed setup for format cuda: hwaccel initialization returned error")]
	[InlineData("No device available for decoder")]
	// AVERROR_EXTERNAL — the generic string the native binding throws on hwaccel failure.
	[InlineData("Generic error in an external library")]
	public void Categorize_HardwareAcceleration(string text) =>
		Assert.Equal(FfmpegErrorCategory.HardwareAcceleration, FfmpegErrorClassifier.Categorize(text));

	[Theory]
	[InlineData("[mov,mp4 @ 0x0] moov atom not found")]
	[InlineData("Invalid data found when processing input")]
	[InlineData("[h264 @ 0x0] Invalid NAL unit size (-1)")]
	[InlineData("Could not find codec parameters for stream 0")]
	[InlineData("[hevc @ 0x0] Error while decoding")]
	public void Categorize_CorruptOrTruncated(string text) =>
		Assert.Equal(FfmpegErrorCategory.CorruptOrTruncated, FfmpegErrorClassifier.Categorize(text));

	[Theory]
	[InlineData("Decoder not found for codec id 12345")]
	[InlineData("Unknown decoder 'foobar'")]
	public void Categorize_UnsupportedCodec(string text) =>
		Assert.Equal(FfmpegErrorCategory.UnsupportedCodec, FfmpegErrorClassifier.Categorize(text));

	[Theory]
	[InlineData("Permission denied")]
	[InlineData("No such file or directory")]
	public void Categorize_FileAccess(string text) =>
		Assert.Equal(FfmpegErrorCategory.FileAccess, FfmpegErrorClassifier.Categorize(text));

	[Theory]
	[InlineData("Unable to load shared library 'avcodec'")]
	[InlineData("The installed FFmpeg major version does not match what the bundled FFmpeg.AutoGen binding expects")]
	public void Categorize_LibraryLoad(string text) =>
		Assert.Equal(FfmpegErrorCategory.LibraryLoad, FfmpegErrorClassifier.Categorize(text));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("some entirely unrecognized message")]
	public void Categorize_Unknown(string? text) =>
		Assert.Equal(FfmpegErrorCategory.Unknown, FfmpegErrorClassifier.Categorize(text));

	[Fact]
	public void Categorize_IsCaseInsensitive() =>
		Assert.Equal(FfmpegErrorCategory.HardwareAcceleration,
			FfmpegErrorClassifier.Categorize("HARDWARE IS LACKING REQUIRED CAPABILITIES"));

	[Fact]
	public void Categorize_SpecificCorruptionBeatsGenericExternalLibrary() {
		// Both needles present; the specific corruption match must win over the weak
		// external-library hardware fallback that is deliberately checked last.
		const string text = "Generic error in an external library; Invalid data found when processing input";
		Assert.Equal(FfmpegErrorCategory.CorruptOrTruncated, FfmpegErrorClassifier.Categorize(text));
	}

	[Fact]
	public void Classify_ReturnsHintForKnownCategory() {
		string? hint = FfmpegErrorClassifier.Classify("Hardware is lacking required capabilities");
		Assert.NotNull(hint);
		Assert.Contains("Hardware acceleration", hint);
	}

	[Fact]
	public void Classify_ReturnsNullForUnknown() =>
		Assert.Null(FfmpegErrorClassifier.Classify("nothing recognizable here"));

	[Fact]
	public void HintFor_UnknownIsNull() =>
		Assert.Null(FfmpegErrorClassifier.HintFor(FfmpegErrorCategory.Unknown));
}
