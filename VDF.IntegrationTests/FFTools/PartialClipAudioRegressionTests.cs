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

using MemoryPack;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using VDF.Core;
using VDF.Core.Chromaprint;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;
using VDF.TestSupport;

namespace VDF.IntegrationTests.FFTools;

[Collection("Ffmpeg")]
public class PartialClipAudioRegressionTests(FfmpegFixture fixture) {
	string MakeVideo(string name, ReadOnlySpan<short> samples, string color = "gray", double? movingOffset = null, string audioCodec = "copy") {
		string wave = Path.Combine(fixture.TempDir, name + ".wav");
		string video = Path.Combine(fixture.TempDir, name + ".mkv");
		PartialClipAudioFixture.WriteWave(wave, samples);
		string pattern = movingOffset.HasValue
			? "color=c=gray:s=160x96:r=25,geq=lum='30+180*mod(T+" + movingOffset.Value.ToString(CultureInfo.InvariantCulture) + ",1)':cb=128:cr=128"
			: $"color=c={color}:s=160x96:r=4";
		var psi = new ProcessStartInfo(FfmpegEngine.FFmpegPath) { RedirectStandardError = true, UseShellExecute = false };
		foreach (var arg in new[] { "-v", "error", "-nostdin", "-y", "-f", "lavfi", "-i", pattern,
			"-i", wave, "-map", "0:v", "-map", "1:a", "-t", (samples.Length / 11025d).ToString(CultureInfo.InvariantCulture),
			"-c:v", "ffv1", "-c:a", audioCodec, video }) psi.ArgumentList.Add(arg);
		using var process = Process.Start(psi)!;
		var error = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(30_000)) { process.Kill(); Assert.Fail("FFmpeg fixture generation timed out"); }
		Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
		return video;
	}

	static ScanEngine Engine(string folder, double threshold = .8, bool native = false) {
		var engine = new ScanEngine {
			Settings = new Settings {
				EnablePartialClipDetection = true,
				PartialClipSimilarityThreshold = threshold,
				PartialClipRequireVisualMatch = true,
				UseNativeFfmpegBinding = native,
				HardwareAccelerationMode = FFHardwareAccelerationMode.none,
				ThumbnailCount = 2,
				Percent = 98,
				PercentDurationDifference = 4
			}
		};
		engine.Settings.IncludeList.Add(folder);
		engine.ElapsedTimer.Start();
		return engine;
	}

	[SkippableFact]
	public async Task PairDiagnostic_BelowThreshold_ReportsActualBestScoreAndOffset() {
		Skip.If(!fixture.FfmpegCliAvailable, fixture.FfmpegNotFoundReason);
		using var guard = new FfmpegStaticStateGuard();
		short[] pcm = PartialClipAudioFixture.Generate(16);
		string source = MakeVideo("diagnostic-source", pcm);
		string clip = MakeVideo("diagnostic-clip", pcm.AsSpan(2 * 11025, 8 * 11025));
		string report = await Engine(fixture.TempDir, .999).TestFilePairAsync(source, clip);
		var match = Regex.Match(report, @"Best audio match: ([0-9.]+)% at offset ([0-9.]+)s");
		Assert.True(match.Success, report);
		double score = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
		double offset = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
		Assert.InRange(score, 80, 99.9);
		Assert.InRange(offset, 1.9, 2.1);
		Assert.Contains("FAIL — the audio similarity is below the configured threshold.", report);
	}

	[SkippableTheory]
	[InlineData(2.5, false)]
	[InlineData(5.5, false)]
	[InlineData(2.5, true)]
	public async Task PairDiagnostic_AndPartialScan_FindSampleExactFractionalClip(double offset, bool native) {
		Skip.If(!fixture.FfmpegCliAvailable, fixture.FfmpegNotFoundReason);
		using var guard = new FfmpegStaticStateGuard();
		short[] pcm = PartialClipAudioFixture.Generate(16);
		Skip.If(native && !fixture.NativeBindingAvailable, "FFmpeg native libraries not available");
		string tag = offset.ToString(CultureInfo.InvariantCulture) + native;
		string source = MakeVideo("fractional-source-" + tag, pcm);
		string clip = MakeVideo("fractional-clip-" + tag, pcm.AsSpan((int)(offset * 11025), 8 * 11025));
		var engine = Engine(fixture.TempDir, native: native);
		string report = await engine.TestFilePairAsync(source, clip);
		Assert.Contains("PASS — the shorter video would be detected as a partial clip", report);
		Assert.Contains("Visual confirmation at the matched offset: 100.0%", report);
		try {
			foreach (string file in new[] { source, clip }) {
				var entry = new FileEntry(file) { mediaInfo = FFProbeEngine.GetMediaInfo(file, false), invalid = false };
				ScanEngine.ExtractAudioFingerprint(entry);
				var restored = MemoryPackSerializer.Deserialize<FileEntry>(MemoryPackSerializer.Serialize(entry))!;
				restored.invalid = false;
				Assert.False(ScanEngine.NeedsAudioFingerprint(restored, true, true));
				DatabaseUtils.Database.Add(restored);
			}
			engine.ScanForPartialDuplicates();
			var found = Assert.Single(engine.Duplicates, d => d.Path == clip);
			Assert.InRange(found.PartialClipOffset.TotalSeconds, offset - ChromaContext.FrameDurationSeconds, offset + ChromaContext.FrameDurationSeconds);
		}
		finally { DatabaseUtils.Database.RemoveWhere(e => e.Path == source || e.Path == clip); }
	}

	[SkippableFact]
	public async Task MatchingAudio_WithDifferentVideo_StillFailsVisualConfirmation() {
		Skip.If(!fixture.FfmpegCliAvailable, fixture.FfmpegNotFoundReason);
		using var guard = new FfmpegStaticStateGuard();
		short[] pcm = PartialClipAudioFixture.Generate(16);
		string source = MakeVideo("visual-source", pcm);
		string clip = MakeVideo("visual-different", pcm.AsSpan((int)(2.5 * 11025), 8 * 11025), "blue");
		string report = await Engine(fixture.TempDir).TestFilePairAsync(source, clip);
		Assert.Contains("FAIL — the audio matches but the frames", report);
		Assert.DoesNotContain("PASS — the shorter video would be detected", report);
	}

	[SkippableFact]
	public async Task MovingVideo_ConfirmsFractionalOffsetAndRejectsRoundedOffset() {
		Skip.If(!fixture.FfmpegCliAvailable, fixture.FfmpegNotFoundReason);
		using var guard = new FfmpegStaticStateGuard();
		short[] pcm = PartialClipAudioFixture.Generate(16);
		string source = MakeVideo("moving-source", pcm, movingOffset: 0);
		string clip = MakeVideo("moving-clip", pcm.AsSpan((int)(2.5 * 11025), 8 * 11025), movingOffset: 2.5);
		var engine = Engine(fixture.TempDir);
		string report = await engine.TestFilePairAsync(source, clip);
		Assert.Contains("PASS — the shorter video would be detected", report);
		var a = new FileEntry(source) { mediaInfo = FFProbeEngine.GetMediaInfo(source, false) };
		var b = new FileEntry(clip) { mediaInfo = FFProbeEngine.GetMediaInfo(clip, false) };
		Assert.False(engine.VerifyPartialClipVisually(a, b, 2d, out _));
		Assert.True(engine.VerifyPartialClipVisually(a, b, 2.5, out _));
	}

	[SkippableFact]
	public async Task ReencodedAudio_WithFractionalCut_RemainsDetected() {
		Skip.If(!fixture.FfmpegCliAvailable, fixture.FfmpegNotFoundReason);
		using var guard = new FfmpegStaticStateGuard();
		short[] pcm = PartialClipAudioFixture.Generate(32);
		string source = MakeVideo("aac-source", pcm);
		string clip = MakeVideo("aac-clip", pcm.AsSpan((int)(5.5 * 11025), 12 * 11025), audioCodec: "aac");
		string report = await Engine(fixture.TempDir).TestFilePairAsync(source, clip);
		Assert.Contains("PASS — the shorter video would be detected", report);
	}

	[SkippableFact]
	public void ShortSilentAudio_WithNoCompleteFineWindow_IsStillExcluded() {
		Skip.If(!fixture.FfmpegCliAvailable, fixture.FfmpegNotFoundReason);
		using var guard = new FfmpegStaticStateGuard();
		FfmpegEngine.UseNativeBinding = false;
		string path = MakeVideo("short-silent", new short[(int)(1.5 * 11025)]);
		var entry = new FileEntry(path);
		ScanEngine.ExtractAudioFingerprint(entry);
		Assert.True(entry.Flags.Has(EntryFlags.SilentAudioTrack));
		Assert.Empty(entry.AudioFingerprint!);
	}
}
