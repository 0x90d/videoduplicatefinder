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
// #885: criteria can be switched off. #895: "Size (larger file wins)" for archives.
// #888 / #915: values the whole group shares are shown neutral, and the green size
// follows the size direction in use.

using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	public class QualityCriteriaSelectionTests {
		static readonly List<string> DefaultOrder = new SettingsFile().QualityCriteriaOrder;
		static readonly List<string> DefaultDisabled = new SettingsFile().QualityCriteriaDisabled;

		static DuplicateItemVM Image(string path, long size, int frameSizeInt = 3000, Guid? group = null) => new() {
			ItemInfo = new DuplicateItem { Path = path, SizeLong = size, FrameSizeInt = frameSizeInt, IsImage = true, GroupId = group ?? Guid.Empty }
		};

		static DuplicateItemVM Keep(IEnumerable<string> order, ICollection<string> disabled, params DuplicateItemVM[] items) =>
			QualityRanker.PickKeeper(items, MainWindowVM.ResolveCriteria(order, disabled), d => d.ItemInfo.IsImage);

		[Fact]
		public void Defaults_KeepTheSmallerFile_AsBefore() {
			var png = Image("a.png", 900_000);
			var jpg = Image("a.jpg", 200_000);
			Assert.Same(jpg, Keep(DefaultOrder, DefaultDisabled, png, jpg));
			Assert.False(MainWindowVM.PrefersLargerSize(DefaultOrder, DefaultDisabled));
		}

		[Fact]
		public void SizeLargerOn_AndSmallerOff_KeepsThePng() {
			// The #895 report: a PNG and a JPG of the same picture, the lossless one must stay.
			var png = Image("a.png", 900_000);
			var jpg = Image("a.jpg", 200_000);
			var disabled = new List<string> { "Size" };
			Assert.Same(png, Keep(DefaultOrder, disabled, png, jpg));
			Assert.True(MainWindowVM.PrefersLargerSize(DefaultOrder, disabled));
		}

		[Fact]
		public void SwitchedOffCriteria_AreSkippedWhereverTheyStand() {
			// #885: size ranked last still decided between otherwise equal images.
			var bigger = Image("big.jpg", 5_000_000);
			var smaller = Image("small.jpg", 400_000);
			Assert.Same(smaller, Keep(DefaultOrder, DefaultDisabled, bigger, smaller));

			var names = MainWindowVM.ResolveCriteria(DefaultOrder, new List<string> { "Size", "SizeLarger", "Audio Bitrate" }).Select(c => c.Name).ToList();
			Assert.DoesNotContain("Size", names);
			Assert.DoesNotContain("SizeLarger", names);
			Assert.DoesNotContain("Audio Bitrate", names);
			// Without any size criterion the first of equal candidates stays, the ranking does not guess.
			Assert.Same(bigger, Keep(DefaultOrder, new List<string> { "Size", "SizeLarger" }, bigger, smaller));
		}

		[Fact]
		public void OlderSettingsWithoutTheNewCriterion_AppendItSwitchedOff() {
			var savedBeforeThisChange = new List<string> { "Duration", "Resolution", "Bitrate", "FPS", "Bits per pixel", "Audio Bitrate", "Size" };
			var names = MainWindowVM.ResolveCriteria(savedBeforeThisChange, DefaultDisabled).Select(c => c.Name).ToList();
			Assert.Equal(savedBeforeThisChange, names);
		}

		[Fact]
		public void TheFirstEnabledSizeCriterion_DecidesTheDirection() {
			var both = new List<string> { "Resolution", "SizeLarger", "Size" };
			Assert.True(MainWindowVM.PrefersLargerSize(both, new List<string>()));
			Assert.False(MainWindowVM.PrefersLargerSize(new List<string> { "Resolution", "Size", "SizeLarger" }, new List<string>()));
			Assert.False(MainWindowVM.PrefersLargerSize(both, new List<string> { "Size", "SizeLarger" }));
		}

		[Fact]
		public void SizePreference_MovesTheGreenSize() {
			Guid g = Guid.NewGuid(), h = Guid.NewGuid();
			var small = Image("s", 100, group: g);
			var large = Image("l", 900, group: g);
			var other = Image("o", 50, group: h);
			var other2 = Image("o2", 60, group: h);
			var items = new[] { small, large, other, other2 };

			MainWindowVM.ApplySizePreference(items, preferLarger: true);
			Assert.True(large.ItemInfo.IsBestSize);
			Assert.False(small.ItemInfo.IsBestSize);
			Assert.True(other2.ItemInfo.IsBestSize); // per group
			Assert.False(other.ItemInfo.IsBestSize);

			MainWindowVM.ApplySizePreference(items, preferLarger: false);
			Assert.True(small.ItemInfo.IsBestSize);
			Assert.False(large.ItemInfo.IsBestSize);
		}

		static DuplicateItemVM Video(Guid group, string path, long size, TimeSpan duration, bool bestSize, bool bestDuration) => new() {
			ItemInfo = new DuplicateItem {
				GroupId = group, Path = path, SizeLong = size, Duration = duration, Similarity = 100f,
				IsBestSize = bestSize, IsBestDuration = bestDuration, FrameSizeInt = 2000, IsBestFrameSize = true,
				BitRateKbs = 5000, IsBestBitRateKbs = true,
			}
		};

		[Fact]
		public void ValuesTheWholeGroupShares_AreNeitherGreenNorRed() {
			Guid g = Guid.NewGuid();
			var a = Video(g, "a", 100, TimeSpan.FromMinutes(5), bestSize: true, bestDuration: true);
			var b = Video(g, "b", 900, TimeSpan.FromMinutes(5), bestSize: false, bestDuration: true);
			var result = ResultsListBuilder.Build(new ResultsBuildRequest {
				Items = new[] { a, b }, IsTombstone = _ => false, IsOffline = _ => false,
			});
			var rows = result.Groups[0].Rows;

			// Same duration, resolution and bitrate in both: neutral everywhere.
			Assert.All(rows, r => {
				Assert.True(r.SameDuration && r.SameFrameSize && r.SameBitRate);
				Assert.False(r.DurationHi || r.DurationLo || r.FrameSizeHi || r.FrameSizeLo || r.BitRateHi || r.BitRateLo);
			});
			// Sizes differ: green and red as before.
			var rowA = rows.Single(r => r.Item == a);
			var rowB = rows.Single(r => r.Item == b);
			Assert.True(rowA.SizeHi && !rowA.SizeLo);
			Assert.True(rowB.SizeLo && !rowB.SizeHi);
		}
	}
}
