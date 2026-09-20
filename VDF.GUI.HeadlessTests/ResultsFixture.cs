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

namespace VDF.GUI.HeadlessTests;

/// <summary>A view model holding two duplicate groups of two videos each, list already built.</summary>
static class ResultsFixture {
	public static MainWindowVM CreatePopulatedViewModel() {
		var vm = new MainWindowVM();
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();
		Add(vm, @"D:\Videos\Holiday\beach_2019_final.mp4", first, 100f, 1_900_000_000, 1920, best: true);
		Add(vm, @"D:\Videos\Holiday\copy\beach_2019_final (1).mp4", first, 98.4f, 700_000_000, 1280, best: false);
		Add(vm, @"E:\Archive\Old\wedding.mkv", second, 100f, 3_100_000_000, 3840, best: true);
		Add(vm, @"E:\Archive\Old\wedding_small.mp4", second, 91.2f, 400_000_000, 854, best: false);
		vm.RebuildResultsList();
		return vm;
	}

	static void Add(MainWindowVM vm, string path, Guid group, float similarity, long size, int width, bool best) {
		int height = width * 9 / 16;
		vm.Duplicates.Add(new DuplicateItemVM(new DuplicateItem {
			Path = path,
			GroupId = group,
			Similarity = similarity,
			SizeLong = size,
			Folder = Path.GetDirectoryName(path)!,
			Duration = TimeSpan.FromSeconds(754),
			FrameSize = $"{width}x{height}",
			FrameSizeInt = width * height,
			Format = "h264",
			AudioFormat = "aac",
			AudioChannel = "stereo",
			Fps = 29.97f,
			BitRateKbs = 4200,
			AudioBitRateKbs = 192,
			DateCreated = new DateTime(2024, 5, 1),
			IsBestSize = best,
			IsBestFrameSize = best,
			IsBestBitRateKbs = best,
			IsBestDuration = true,
		}));
	}
}
