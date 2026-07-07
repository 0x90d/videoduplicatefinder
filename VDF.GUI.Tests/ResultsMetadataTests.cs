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

using System.Globalization;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	/// <summary>Stage-7 row metadata: bitrate formatting, HDR chip highlight, details rows.</summary>
	public class ResultsMetadataTests {

		static DuplicateItemVM Item(Guid group, string path, string hdr = "", long size = 100) =>
			new() {
				ItemInfo = new DuplicateItem {
					GroupId = group,
					Path = path,
					SizeLong = size,
					HdrFormat = hdr,
					DateCreated = new DateTime(2024, 1, 1),
					Duration = TimeSpan.FromMinutes(1),
				}
			};

		static ResultsBuildRequest Request(params DuplicateItemVM[] items) => new() {
			Items = items,
			IsTombstone = _ => false,
			IsOffline = _ => false,
		};

		[Theory]
		[InlineData(0, "")]
		[InlineData(-5, "")]
		[InlineData(192, "192 kb/s")]
		[InlineData(999, "999 kb/s")]
		[InlineData(1000, "1.0 Mb/s")]
		[InlineData(8437, "8.4 Mb/s")]
		public void FormatBitrate_SwitchesUnitsAtOneThousand(decimal kbs, string expected) {
			Assert.Equal(expected, ResultsBadgeRules.FormatBitrate(kbs, CultureInfo.InvariantCulture));
		}

		[Fact]
		public void FormatSampleRate_ReadsAsKhz() {
			Assert.Equal("48 kHz", ResultsBadgeRules.FormatSampleRate(48000, CultureInfo.InvariantCulture));
			Assert.Equal("44.1 kHz", ResultsBadgeRules.FormatSampleRate(44100, CultureInfo.InvariantCulture));
			Assert.Equal("", ResultsBadgeRules.FormatSampleRate(0, CultureInfo.InvariantCulture));
		}

		[Fact]
		public void HdrChip_IsUpgradeOnlyInMixedGroups() {
			Guid mixed = Guid.NewGuid(), uniform = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(mixed, "hdr.mkv", hdr: "HDR10"),
				Item(mixed, "sdr.mp4"),
				Item(uniform, "a.mkv", hdr: "HDR10"),
				Item(uniform, "b.mkv", hdr: "HDR10")));

			var rows = result.Rows.OfType<ResultsItemRow>().ToDictionary(r => r.Item.ItemInfo.Path);
			Assert.True(rows["hdr.mkv"].HdrIsUpgrade);   // beats the SDR member
			Assert.False(rows["sdr.mp4"].HdrIsUpgrade);
			Assert.False(rows["a.mkv"].HdrIsUpgrade);    // uniform group: nothing to win
			Assert.False(rows["b.mkv"].HdrIsUpgrade);
		}

		[Fact]
		public void HdrChip_DolbyVisionBeatsHdr10() {
			Guid g = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(g, "dv.mkv", hdr: "Dolby Vision"),
				Item(g, "hdr10.mkv", hdr: "HDR10")));

			var rows = result.Rows.OfType<ResultsItemRow>().ToDictionary(r => r.Item.ItemInfo.Path);
			Assert.True(rows["dv.mkv"].HdrIsUpgrade);
			Assert.False(rows["hdr10.mkv"].HdrIsUpgrade);
		}

		[Fact]
		public void ExpandedDetails_InsertsDetailsRowAfterItsItem() {
			Guid g = Guid.NewGuid();
			var a = Item(g, "a.mp4");
			var b = Item(g, "b.mp4");
			var result = ResultsListBuilder.Build(Request(a, b) with {
				ExpandedDetails = new HashSet<DuplicateItemVM> { b },
			});

			Assert.Equal(4, result.Rows.Count); // header, a, b, details(b)
			var details = Assert.IsType<ResultsDetailsRow>(result.Rows[3]);
			Assert.Same(b, details.Item);
			Assert.Same(((ResultsItemRow)result.Rows[2]).Item, details.Item);
		}

		[Fact]
		public void ExpandedDetails_CollapsedGroupSuppressesDetailsRows() {
			Guid g = Guid.NewGuid();
			var a = Item(g, "a.mp4");
			var result = ResultsListBuilder.Build(Request(a, Item(g, "b.mp4")) with {
				ExpandedDetails = new HashSet<DuplicateItemVM> { a },
				CollapsedGroups = new HashSet<Guid> { g },
			});

			Assert.Single(result.Rows); // header only
		}

		[Fact]
		public void DetailsText_SkipsMissingFieldsAndAudioForImages() {
			var video = new DuplicateItem {
				Path = @"D:\a.mkv", Format = "HEVC", FrameSize = "3840x2160", Fps = 59.94f,
				Duration = TimeSpan.FromSeconds(251), BitRateKbs = 51500m, HdrFormat = "HLG",
				AudioFormat = "Opus", AudioChannel = "2", AudioSampleRate = 48000, AudioBitRateKbs = 128m,
				SizeLong = 1_000_000, DateCreated = new DateTime(2025, 3, 14),
			};
			string text = ResultsBadgeRules.BuildDetailsText(video);
			Assert.Contains("Video: HEVC", text);
			Assert.Contains("HLG", text);
			Assert.Contains("Audio: Opus", text);

			var image = new DuplicateItem {
				Path = @"D:\a.jpg", Format = "JPEG", FrameSize = "2268x4032", IsImage = true,
				SizeLong = 1_600_000, DateCreated = new DateTime(2021, 4, 28),
			};
			string imageText = ResultsBadgeRules.BuildDetailsText(image);
			Assert.Contains("Image: JPEG", imageText);
			Assert.DoesNotContain("Audio:", imageText);
			Assert.DoesNotContain("fps", imageText);
		}
	}
}
