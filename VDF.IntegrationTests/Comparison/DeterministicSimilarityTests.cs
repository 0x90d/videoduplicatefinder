// /*
//     Copyright (C) 2025 0x90d
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
using VDF.Core.pHash;
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.Comparison;

[Collection("Ffmpeg")]
public class DeterministicSimilarityTests {
	readonly FfmpegFixture _fixture;

	public DeterministicSimilarityTests(FfmpegFixture fixture) => _fixture = fixture;

	byte[]? ExtractGrayBytes(string file) {
		FfmpegEngine.UseNativeBinding = false;
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		return FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = file,
			Position = TimeSpan.FromSeconds(1),
			GrayScale = 1,
		}, extendedLogging: false);
	}

	[SkippableFact]
	public void IdenticalVideos_ProduceZeroDifference() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();

		// Extract graybytes from the same file twice — should be byte-identical
		var bytes1 = ExtractGrayBytes(_fixture.H264_8bit!);
		var bytes2 = ExtractGrayBytes(_fixture.H264_8bit!);

		Assert.NotNull(bytes1);
		Assert.NotNull(bytes2);

		float diff = GrayBytesUtils.PercentageDifference(bytes1, bytes2);
		Assert.Equal(0.0f, diff);
	}

	[SkippableFact]
	public void DifferentVideos_ProduceNonZeroDifference() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");
		Skip.If(_fixture.H264_Different == null, "Different test video not generated");

		using var guard = new FfmpegStaticStateGuard();

		var bytes1 = ExtractGrayBytes(_fixture.H264_8bit!);
		var bytes2 = ExtractGrayBytes(_fixture.H264_Different!);

		Assert.NotNull(bytes1);
		Assert.NotNull(bytes2);

		float diff = GrayBytesUtils.PercentageDifference(bytes1, bytes2);
		Assert.True(diff > 0.05f,
			$"Expected visually different videos to differ by > 5%, got {diff:P2}");
	}

	[SkippableFact]
	public void DifferentVideos_SimilarityScore_IsStableAcrossRuns() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");
		Skip.If(_fixture.H264_Different == null, "Different test video not generated");

		using var guard = new FfmpegStaticStateGuard();

		var bytesA = ExtractGrayBytes(_fixture.H264_8bit!);
		var bytesB = ExtractGrayBytes(_fixture.H264_Different!);
		Assert.NotNull(bytesA);
		Assert.NotNull(bytesB);

		float diff1 = GrayBytesUtils.PercentageDifference(bytesA, bytesB);
		float diff2 = GrayBytesUtils.PercentageDifference(bytesA, bytesB);
		float diff3 = GrayBytesUtils.PercentageDifference(bytesA, bytesB);

		Assert.Equal(diff1, diff2);
		Assert.Equal(diff2, diff3);
	}

	[SkippableFact]
	public void DifferentVideos_PHashSimilarity_IsDeterministic() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");
		Skip.If(_fixture.H264_Different == null, "Different test video not generated");

		using var guard = new FfmpegStaticStateGuard();

		var bytesA = ExtractGrayBytes(_fixture.H264_8bit!);
		var bytesB = ExtractGrayBytes(_fixture.H264_Different!);
		Assert.NotNull(bytesA);
		Assert.NotNull(bytesB);

		ulong hashA = PerceptualHash.ComputePHashFromGray32x32(bytesA);
		ulong hashB = PerceptualHash.ComputePHashFromGray32x32(bytesB);

		PHashCompare.IsDuplicateByPercent(hashA, hashB, out float sim1, 0.5);
		PHashCompare.IsDuplicateByPercent(hashA, hashB, out float sim2, 0.5);

		Assert.Equal(sim1, sim2);
	}

	[SkippableFact]
	public void SameVideo_PHashSimilarity_Is100Percent() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();

		var bytes = ExtractGrayBytes(_fixture.H264_8bit!);
		Assert.NotNull(bytes);

		ulong hash = PerceptualHash.ComputePHashFromGray32x32(bytes);
		PHashCompare.IsDuplicateByPercent(hash, hash, out float similarity, 1.0);

		Assert.Equal(1.0f, similarity);
	}
}
