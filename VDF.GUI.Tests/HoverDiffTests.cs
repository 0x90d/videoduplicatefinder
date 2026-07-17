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

using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	/// <summary>
	/// Hover-diff computation. Regression coverage for the #849 report: with metrics
	/// tied across the whole group, every row showed the literal word BEST instead of
	/// its value, which conveys nothing and reads as corrupted cells.
	/// </summary>
	public class HoverDiffTests {
		const string Best = "BEST";

		static DuplicateItemVM Item(TimeSpan? duration = null, int frameSizeInt = 0, long size = 0,
				bool bestDuration = false, bool bestFrameSize = false, bool bestSize = false) => new() {
			ItemInfo = new DuplicateItem {
				GroupId = Guid.Empty,
				Path = Guid.NewGuid().ToString(),
				Duration = duration ?? TimeSpan.Zero,
				FrameSizeInt = frameSizeInt,
				SizeLong = size,
				IsBestDuration = bestDuration,
				IsBestFrameSize = bestFrameSize,
				IsBestSize = bestSize,
			}
		};

		[Fact]
		public void AllTied_ShowsNothing_InsteadOfBestOnEveryRow() {
			var a = Item(duration: TimeSpan.FromMinutes(2), bestDuration: true);
			var b = Item(duration: TimeSpan.FromMinutes(2), bestDuration: true);

			MainWindowVM.ApplyHoverDiffs(new[] { a, b }, "duration", Best);

			Assert.Null(a.DurationDiff);
			Assert.Null(b.DurationDiff);
		}

		[Fact]
		public void DistinctValues_BestGetsLabel_OtherGetsDelta() {
			var best = Item(duration: TimeSpan.FromMinutes(2), bestDuration: true);
			var worse = Item(duration: TimeSpan.FromSeconds(107));

			MainWindowVM.ApplyHoverDiffs(new[] { best, worse }, "duration", Best);

			Assert.Equal(Best, best.DurationDiff);
			Assert.Equal("-13s", worse.DurationDiff);
		}

		[Fact]
		public void TieOnOneMetric_DoesNotSuppressAnother() {
			// Same resolution but different sizes: hovering the cell activates both
			// metrics — framesize must stay silent while size still diffs.
			var a = Item(frameSizeInt: 1920 * 1080, size: 1000, bestFrameSize: true, bestSize: true);
			var b = Item(frameSizeInt: 1920 * 1080, size: 800, bestFrameSize: true);

			MainWindowVM.ApplyHoverDiffs(new[] { a, b }, "framesize", Best);
			MainWindowVM.ApplyHoverDiffs(new[] { a, b }, "size", Best);

			Assert.Null(a.FrameSizeDiff);
			Assert.Null(b.FrameSizeDiff);
			Assert.Equal(Best, a.SizeDiff);
			Assert.Equal("-20%", b.SizeDiff);
		}

		[Fact]
		public void AllTiedOn_CoversEveryHoverableMetric() {
			var a = Item(duration: TimeSpan.FromMinutes(1), frameSizeInt: 100, size: 5);
			var b = Item(duration: TimeSpan.FromMinutes(1), frameSizeInt: 100, size: 5);
			var items = new[] { a, b };

			foreach (string metric in new[] { "duration", "framesize", "size", "fps", "bitrate", "audiosamplerate", "audiobitrate" })
				Assert.True(MainWindowVM.AllTiedOn(items, metric), metric);

			b.ItemInfo.Fps = 30f;
			Assert.False(MainWindowVM.AllTiedOn(items, "fps"));
		}
	}
}
