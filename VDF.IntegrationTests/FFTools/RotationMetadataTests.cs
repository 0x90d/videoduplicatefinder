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

using System.Diagnostics;
using VDF.Core;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// #910: a video with rotation metadata and a re-encoded, physically rotated copy of it
/// matched through the FFmpeg command line, which applies the rotation, but not through the
/// native binding, which ignored it. Every native output now comes out upright.
/// </summary>
[Collection("Ffmpeg")]
public sealed class RotationMetadataTests : IDisposable {
	readonly FfmpegFixture _fixture;
	readonly string dir = Path.Combine(Path.GetTempPath(), $"vdf_rotation_{Guid.NewGuid():N}");

	public RotationMetadataTests(FfmpegFixture fixture) {
		_fixture = fixture;
		Directory.CreateDirectory(dir);
	}

	public void Dispose() {
		try { Directory.Delete(dir, true); } catch { }
	}

	static void RunFfmpeg(params string[] args) {
		var psi = new ProcessStartInfo(FfmpegEngine.FFmpegPath) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
		foreach (string a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args))
			psi.ArgumentList.Add(a);
		using var p = Process.Start(psi)!;
		string err = p.StandardError.ReadToEnd();
		p.WaitForExit();
		Assert.True(p.ExitCode == 0, $"ffmpeg failed: {err}");
	}

	/// <summary>
	/// The reporter's pair: the original with only a rotation tag, and a copy re-encoded by
	/// the command line (which turns the pixels while encoding and drops the tag).
	/// </summary>
	(string Tagged, string Upright) MakePair(int degrees) {
		string tagged = Path.Combine(dir, $"tagged{degrees}.mp4");
		string upright = Path.Combine(dir, $"upright{degrees}.mp4");
		RunFfmpeg("-display_rotation", degrees.ToString(), "-i", _fixture.H264_8bit!, "-c", "copy", tagged);
		RunFfmpeg("-i", tagged, "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p", upright);
		return (tagged, upright);
	}

	static byte[] Gray(string file, bool native) {
		FfmpegEngine.UseNativeBinding = native;
		return FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = file, Position = TimeSpan.FromSeconds(1), GrayScale = 1, SoftwareDecodeOnly = true,
		}, extendedLogging: false)!;
	}

	[SkippableTheory]
	[InlineData(90)]
	[InlineData(180)]
	[InlineData(270)]
	public void TaggedOriginal_MatchesItsUprightCopy_Natively(int degrees) {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		var (tagged, upright) = MakePair(degrees);

		byte[] nativeTagged = Gray(tagged, native: true);
		byte[] nativeUpright = Gray(upright, native: true);
		byte[] processTagged = Gray(tagged, native: false);

		float pair = GrayBytesUtils.PercentageDifference(nativeTagged, nativeUpright);
		float parity = GrayBytesUtils.PercentageDifference(nativeTagged, processTagged);
		Assert.True(pair < 0.05f, $"{degrees}°: tagged vs upright copy differ by {pair:P2} natively");
		Assert.True(parity < 0.05f, $"{degrees}°: native vs command line differ by {parity:P2}");
		// And the tag is not a no-op on this clip: the stored pixels really are turned.
		FfmpegEngine.UseNativeBinding = false;
		byte[] storedPixels = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = _fixture.H264_8bit!, Position = TimeSpan.FromSeconds(1), GrayScale = 1, SoftwareDecodeOnly = true,
		}, extendedLogging: false)!;
		Assert.True(GrayBytesUtils.PercentageDifference(storedPixels, nativeUpright) > 0.05f);
	}

	[SkippableFact]
	public void ScanSamplingAndPartialVerification_AreUprightToo() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.UseNativeBinding = true;
		var (tagged, upright) = MakePair(90);

		// The scan's batch sampling (gray bytes that get hashed).
		FileEntry Sampled(string path) {
			var entry = new FileEntry(path) { mediaInfo = FFProbeEngine.GetMediaInfo(path, extendedLogging: false) };
			Assert.True(FfmpegEngine.GetGrayBytesFromVideo(entry, new List<float> { 0.5f }, 0, extendedLogging: false));
			return entry;
		}
		var a = Sampled(tagged).grayBytes.Values.Single()!;
		var b = Sampled(upright).grayBytes.Values.Single()!;
		Assert.True(GrayBytesUtils.PercentageDifference(a, b) < 0.05f);

		// The partial-clip visual check.
		var frames = FfmpegEngine.GetGrayFrames(tagged, new[] { 1.0 }, extendedLogging: false);
		var uprightFrames = FfmpegEngine.GetGrayFrames(upright, new[] { 1.0 }, extendedLogging: false);
		Assert.True(GrayBytesUtils.PercentageDifference(frames[0]!, uprightFrames[0]!) < 0.05f);
	}

	[SkippableFact]
	public void Thumbnail_OfAQuarterTurn_IsPortrait() {
		Skip.If(!_fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");

		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		FfmpegEngine.UseNativeBinding = true;
		var (tagged, _) = MakePair(90);

		byte[] jpeg = FfmpegEngine.GetThumbnail(new FfmpegSettings {
			File = tagged, Position = TimeSpan.FromSeconds(1), MaxWidth = 160, SoftwareDecodeOnly = true,
		}, extendedLogging: false)!;
		var (width, height) = JpegSize(jpeg);
		// 320x240 stored, 240x320 shown: a portrait thumbnail inside the 160 box.
		Assert.Equal((120, 160), (width, height));
	}

	static (int Width, int Height) JpegSize(byte[] jpeg) {
		for (int i = 2; i + 8 < jpeg.Length;) {
			if (jpeg[i] != 0xFF) { i++; continue; }
			byte marker = jpeg[i + 1];
			int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
			if (marker is 0xC0 or 0xC1 or 0xC2)
				return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6]);
			i += 2 + length;
		}
		throw new InvalidDataException("no SOF marker");
	}
}
