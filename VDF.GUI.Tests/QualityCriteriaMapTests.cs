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

using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	// The production criteria map + default order, driven by the two scenarios from the
	// #839 report (both screenshots reproduced the same way the BEST badge computes).
	public class QualityCriteriaMapTests {

		// Must match the SettingsFile default. Bitrate deliberately ranks above FPS;
		// "Bits per pixel" (#848) defaults below them so default picks stay stable -
		// its cross-resolution power comes from ranking it above Resolution manually.
		static readonly string[] DefaultOrder = ["Duration", "Resolution", "Bitrate", "FPS", "Bits per pixel", "Audio Bitrate", "Size"];

		static List<QualityRanker.Criterion<DuplicateItemVM>> DefaultCriteria() =>
			DefaultOrder.Select(n => MainWindowVM.QualityCriteriaMap[n]).ToList();

		static DuplicateItemVM Item(string path, double durationSeconds, int frameSizeInt,
			decimal bitrate = 5000, float fps = 30, decimal audioBitrate = 128,
			int sampleRate = 44100, long size = 1000) =>
			new() {
				ItemInfo = new DuplicateItem {
					Path = path,
					Duration = TimeSpan.FromSeconds(durationSeconds),
					FrameSizeInt = frameSizeInt,
					BitRateKbs = bitrate,
					Fps = fps,
					AudioBitRateKbs = audioBitrate,
					AudioSampleRate = sampleRate,
					SizeLong = size,
				}
			};

		// #839, first screenshot: a file 200ms longer must not beat a file with much
		// higher resolution — the durations display identically to the user.
		[Fact]
		public void MillisecondLongerFile_DoesNotBeatHigherResolution() {
			var longer720p = Item("720p-longer", 1200.2, frameSizeInt: 2000);
			var shorter1080p = Item("1080p", 1200.0, frameSizeInt: 3000);

			var (keep, decidedBy) = QualityRanker.PickKeeperWithReason(
				new[] { longer720p, shorter1080p }, DefaultCriteria(), d => d.ItemInfo.IsImage);

			Assert.Same(shorter1080p, keep);
			Assert.Equal("Resolution", decidedBy!.Name);
		}

		// #839, second screenshot: marginally higher FPS must not outrank a much
		// higher video bitrate at equal resolution.
		[Fact]
		public void HigherFps_DoesNotBeatMuchHigherVideoBitrate() {
			var fastLowBitrate = Item("50fps-3000kbs", 1200, frameSizeInt: 3000, bitrate: 3000, fps: 50);
			var slowHighBitrate = Item("25fps-7600kbs", 1200, frameSizeInt: 3000, bitrate: 7600, fps: 25);

			var (keep, decidedBy) = QualityRanker.PickKeeperWithReason(
				new[] { fastLowBitrate, slowHighBitrate }, DefaultCriteria(), d => d.ItemInfo.IsImage);

			Assert.Same(slowHighBitrate, keep);
			Assert.Equal("Bitrate", decidedBy!.Name);
		}

		// A trimmed copy vs. the full video is a real duration difference — the longer
		// file (most content) must still win, tolerance or not.
		[Fact]
		public void TrimmedCopy_StillLosesToFullVideo() {
			var trimmed = Item("trimmed", 300, frameSizeInt: 3000, bitrate: 9000);
			var full = Item("full", 1200, frameSizeInt: 2000, bitrate: 1000);

			var (keep, decidedBy) = QualityRanker.PickKeeperWithReason(
				new[] { trimmed, full }, DefaultCriteria(), d => d.ItemInfo.IsImage);

			Assert.Same(full, keep);
			Assert.Equal("Duration", decidedBy!.Name);
		}

		// The "Audio Bitrate" criterion compared the audio SAMPLE RATE before #839.
		[Fact]
		public void AudioCriterion_ComparesAudioBitrate_NotSampleRate() {
			// Different sample rates but equal audio bitrate: must fall through to the
			// Size tiebreaker (smaller wins) instead of the higher sample rate deciding.
			var bigHighRate = Item("48kHz-big", 1200, frameSizeInt: 3000, sampleRate: 48000, size: 2000);
			var smallLowRate = Item("44kHz-small", 1200, frameSizeInt: 3000, sampleRate: 44100, size: 1000);

			var (keep, decidedBy) = QualityRanker.PickKeeperWithReason(
				new[] { bigHighRate, smallLowRate }, DefaultCriteria(), d => d.ItemInfo.IsImage);
			Assert.Same(smallLowRate, keep);
			Assert.Equal("Size", decidedBy!.Name);

			// A real audio-bitrate difference decides before Size.
			var strongAudio = Item("320kbs", 1200, frameSizeInt: 3000, audioBitrate: 320, size: 2000);
			var weakAudio = Item("128kbs", 1200, frameSizeInt: 3000, audioBitrate: 128, size: 1000);

			(keep, decidedBy) = QualityRanker.PickKeeperWithReason(
				new[] { weakAudio, strongAudio }, DefaultCriteria(), d => d.ItemInfo.IsImage);
			Assert.Same(strongAudio, keep);
			Assert.Equal("Audio Bitrate", decidedBy!.Name);
		}

		// #848: the criterion itself - bitrate normalized by pixel throughput.
		[Fact]
		public void BitsPerPixel_ComputesFromFrameSizeBitrateAndFps() {
			var item = Item("4k", 1200, frameSizeInt: 6000, bitrate: 13900, fps: 25);
			item.ItemInfo.FrameSize = "3840x2160";

			decimal bpp = MainWindowVM.BitsPerPixel(item.ItemInfo);

			// 13,900 kb/s * 1000 / (3840 * 2160 * 25) = ~0.067 bits per pixel
			Assert.InRange(bpp, 0.066m, 0.068m);
		}

		[Theory]
		[InlineData(null, 5000, 25)]   // no frame size
		[InlineData("x", 5000, 25)]    // broken frame size
		[InlineData("1920x1080", 0, 25)]  // no bitrate
		[InlineData("1920x1080", 5000, 0)] // no fps
		public void BitsPerPixel_UnknownComponents_YieldZero(string? frameSize, double bitrate, float fps) {
			var item = Item("x", 1200, frameSizeInt: 3000, bitrate: (decimal)bitrate, fps: fps);
			item.ItemInfo.FrameSize = frameSize;

			Assert.Equal(0m, MainWindowVM.BitsPerPixel(item.ItemInfo));
		}

		// #848's report: a bitrate-starved 4K (13.9 Mb/s over 8.3M pixels) got BEST over
		// a healthy 1080p (12 Mb/s over 2M pixels). With "Bits per pixel" ranked above
		// Resolution, the 1080p wins.
		[Fact]
		public void BitrateStarved4K_LosesTo1080p_WhenBppRanksAboveResolution() {
			var starved4k = Item("4k-starved", 1609, frameSizeInt: 6000, bitrate: 13900, fps: 25);
			starved4k.ItemInfo.FrameSize = "3840x2160";
			var healthy1080p = Item("1080p-healthy", 1597, frameSizeInt: 3000, bitrate: 12000, fps: 25);
			healthy1080p.ItemInfo.FrameSize = "1920x1080";

			var order = new[] { "Duration", "Bits per pixel", "Resolution", "Bitrate", "FPS", "Audio Bitrate", "Size" };
			var criteria = order.Select(n => MainWindowVM.QualityCriteriaMap[n]).ToList();

			var (keep, decidedBy) = QualityRanker.PickKeeperWithReason(
				new[] { starved4k, healthy1080p }, criteria, d => d.ItemInfo.IsImage);

			Assert.Same(healthy1080p, keep);
			Assert.Equal("Bits per pixel", decidedBy!.Name);

			// With the DEFAULT order the 4K still wins on Resolution - adding the
			// criterion must not change existing default picks.
			(keep, decidedBy) = QualityRanker.PickKeeperWithReason(
				new[] { starved4k, healthy1080p }, DefaultCriteria(), d => d.ItemInfo.IsImage);
			Assert.Same(starved4k, keep);
			Assert.Equal("Resolution", decidedBy!.Name);
		}

		[Theory]
		// Duration: within 1 second or 1% is a tie.
		[InlineData("Duration", 1200.0, 1200.9, true)]
		[InlineData("Duration", 3600.0, 3630.0, true)]   // 30s on 1h = 0.83%
		[InlineData("Duration", 300.0, 320.0, false)]
		// FPS: within 0.5 is a tie (29.97 vs 30), a framerate step is not.
		[InlineData("FPS", 29.97, 30.0, true)]
		[InlineData("FPS", 25.0, 50.0, false)]
		// Bitrate: within 5% is a tie.
		[InlineData("Bitrate", 4500.0, 4499.0, true)]
		[InlineData("Bitrate", 4500.0, 4000.0, false)]
		public void NearTieTolerances(string criterion, double a, double b, bool expectTie) {
			var nearTie = MainWindowVM.QualityCriteriaMap[criterion].NearTie!;
			IComparable x = criterion switch {
				"Duration" => TimeSpan.FromSeconds(a),
				"FPS" => (float)a,
				_ => (decimal)a,
			};
			IComparable y = criterion switch {
				"Duration" => TimeSpan.FromSeconds(b),
				"FPS" => (float)b,
				_ => (decimal)b,
			};
			Assert.Equal(expectTie, nearTie(x, y));
			Assert.Equal(expectTie, nearTie(y, x)); // symmetric
		}
	}
}
