// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Linq;
using ReactiveUI;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

		static bool HasTieOn(string lastCriterion, List<DuplicateItemVM> list, DuplicateItemVM keep) => lastCriterion switch {
			"Duration" => list.Count(d => d.ItemInfo.Duration == keep.ItemInfo.Duration) > 1,
			"Resolution" => list.Count(d => d.ItemInfo.FrameSizeInt == keep.ItemInfo.FrameSizeInt) > 1,
			"FPS" => list.Count(d => d.ItemInfo.Fps == keep.ItemInfo.Fps) > 1,
			"Bitrate" => list.Count(d => d.ItemInfo.BitRateKbs == keep.ItemInfo.BitRateKbs) > 1,
			"Audio Bitrate" => list.Count(d => d.ItemInfo.AudioSampleRate == keep.ItemInfo.AudioSampleRate) > 1,
			_ => false
		};

		static DuplicateItemVM ApplyCriterion(string criterion, List<DuplicateItemVM> list) => criterion switch {
			"Duration" => list.OrderByDescending(d => d.ItemInfo.Duration).First(),
			"Resolution" => list.OrderByDescending(d => d.ItemInfo.FrameSizeInt).First(),
			"FPS" => list.OrderByDescending(d => d.ItemInfo.Fps).First(),
			"Bitrate" => list.OrderByDescending(d => d.ItemInfo.BitRateKbs).First(),
			"Audio Bitrate" => list.OrderByDescending(d => d.ItemInfo.AudioSampleRate).First(),
			_ => list[0]
		};
	}

	sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class {
		public static readonly ReferenceEqualityComparer<T> Instance = new();
		public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
		public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
	}
}
