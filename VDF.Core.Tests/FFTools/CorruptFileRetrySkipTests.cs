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

using FFmpeg.AutoGen;
using VDF.Core.FFTools;

namespace VDF.Core.Tests.FFTools;

/// <summary>
/// Pins the corrupt-file fast-fail decision (#867): a native batch failure classified as a
/// broken file must not be retried through the FFmpeg process (same libavcodec, same broken
/// bitstream — the retry only stalls the scan for another timeout), but ONLY when the native
/// decode ran in software. Under hardware decode a corrupt-looking error can still be a
/// GPU/driver quirk that the process fallback genuinely rescues.
/// </summary>
public class CorruptFileRetrySkipTests {
	[Fact]
	public void CorruptFailure_SoftwareDecode_SkipsRetry() =>
		Assert.True(FfmpegEngine.ShouldSkipProcessRetryForCorruptFile(
			FfmpegErrorCategory.CorruptOrTruncated, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE));

	[Theory]
	[InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA)]
	[InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2)]
	[InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA)]
	[InlineData(AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI)]
	public void CorruptFailure_HardwareDecode_StillRetries(AVHWDeviceType hwType) =>
		Assert.False(FfmpegEngine.ShouldSkipProcessRetryForCorruptFile(
			FfmpegErrorCategory.CorruptOrTruncated, hwType));

	[Fact]
	public void NonCorruptFailure_StillRetries() {
		foreach (FfmpegErrorCategory category in Enum.GetValues<FfmpegErrorCategory>()) {
			if (category == FfmpegErrorCategory.CorruptOrTruncated)
				continue;
			Assert.False(FfmpegEngine.ShouldSkipProcessRetryForCorruptFile(
				category, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE));
		}
	}
}
