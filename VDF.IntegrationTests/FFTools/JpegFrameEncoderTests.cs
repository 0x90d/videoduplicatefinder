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
using VDF.Core.FFTools.FFmpegNative;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// Exercises <see cref="JpegFrameEncoder"/> DIRECTLY (decode -> convert -> encode), with no
/// process-mode fallback. The higher-level GetThumbnail / EncodeJpegFromBgra paths catch
/// native failures and fall back to the FFmpeg process, so a test through them passes even
/// when native encoding is completely broken — which is exactly how the encoder shipped
/// broken in 4.0: avcodec_send_frame rejected every frame with EINVAL because the copied
/// AVFrame's extended_data pointed at the source frame's data array (issues #793/#795).
/// </summary>
[Collection("Ffmpeg")]
public unsafe class JpegFrameEncoderTests {
	readonly FfmpegFixture _fixture;
	public JpegFrameEncoderTests(FfmpegFixture fixture) => _fixture = fixture;

	// Even and odd destination dimensions (odd would also surface chroma-subsampling issues).
	[SkippableTheory]
	[InlineData(100, 56)]
	[InlineData(100, 43)]
	[InlineData(99, 56)]
	public void Encode_FromDecodedFrame_ProducesValidJpeg(int destW, int destH) {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = true;

		using var decoder = new VideoStreamDecoder(_fixture.H264_8bit!);
		Assert.True(decoder.TryDecodeFrame(out var src, TimeSpan.FromSeconds(1)), "decode failed");

		using var converter = new VideoFrameConverter(
			new System.Drawing.Size(src.width, src.height), decoder.PixelFormat,
			new System.Drawing.Size(destW, destH), AVPixelFormat.AV_PIX_FMT_YUVJ420P,
			VideoFrameConverter.ScaleQuality.Bicubic, bitExact: false);

		AVFrame converted = converter.Convert(src);

		// Direct encode — throws on failure (no process-mode fallback to mask it).
		byte[] jpeg = JpegFrameEncoder.Encode(converted, quality: 85);

		Assert.True(jpeg.Length > 4, $"JPEG too small ({jpeg.Length} bytes)");
		Assert.True(jpeg[0] == 0xFF && jpeg[1] == 0xD8, "missing JPEG SOI marker");
		Assert.True(jpeg[^2] == 0xFF && jpeg[^1] == 0xD9, "missing JPEG EOI marker");
	}
}
