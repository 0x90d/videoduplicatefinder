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
using VDF.Core.Utils;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

		internal static readonly Dictionary<string, QualityRanker.Criterion<DuplicateItemVM>> QualityCriteriaMap = new() {
			["Duration"] = new("Duration", d => d.ItemInfo.Duration, videoOnly: true),
			["Resolution"] = new("Resolution", d => d.ItemInfo.FrameSizeInt, videoOnly: false),
			["FPS"] = new("FPS", d => d.ItemInfo.Fps, videoOnly: true),
			["Bitrate"] = new("Bitrate", d => d.ItemInfo.BitRateKbs, videoOnly: true),
			["Audio Bitrate"] = new("Audio Bitrate", d => d.ItemInfo.AudioSampleRate, videoOnly: true),
			["Size"] = new("Size", d => d.ItemInfo.SizeLong, videoOnly: false),
		};

		// Yields criteria in the user's chosen order, then appends any map entries the
		// user's saved list doesn't include. This lets newly added criteria (e.g. Size)
		// take effect as a final tiebreaker for users with pre-existing settings,
		// without overwriting their explicit ordering.
		static IEnumerable<QualityRanker.Criterion<DuplicateItemVM>> ResolveCriteria(IEnumerable<string> names) {
			var seen = new HashSet<string>();
			foreach (var name in names)
				if (QualityCriteriaMap.TryGetValue(name, out var c) && seen.Add(name))
					yield return c;
			foreach (var kv in QualityCriteriaMap)
				if (!seen.Contains(kv.Key))
					yield return kv.Value;
		}
	}

	sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class {
		public static readonly ReferenceEqualityComparer<T> Instance = new();
		public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
		public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
	}
}
