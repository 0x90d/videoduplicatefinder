// /*
//     Copyright (C) 2025 0x90d
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

namespace VDF.CLI.Actions {
	public enum Strategy {
		LowestQuality,
		SmallestFile,
		ShortestDuration,
		WorstResolution,
		HundredPercentOnly
	}

	public static class DeletionStrategy {
		/// <summary>
		/// For each duplicate group, returns the files to DELETE (i.e., all but the keeper).
		/// When HundredPercentOnly is used, groups where not all members are 100% similar are skipped entirely.
		/// </summary>
		public static IReadOnlyList<DuplicateItem> SelectForDeletion(IEnumerable<DuplicateItem> duplicates, Strategy strategy) {
			var toDelete = new List<DuplicateItem>();

			var groups = duplicates.GroupBy(d => d.GroupId);

			foreach (var group in groups) {
				var items = group.ToList();
				if (items.Count < 2) continue;

				if (strategy == Strategy.HundredPercentOnly) {
					// Skip groups that are not all 100% similar
					if (items.Any(i => i.Similarity < 100f))
						continue;
				}

				DuplicateItem keeper = PickKeeper(items, strategy);
				toDelete.AddRange(items.Where(i => i != keeper));
			}

			return toDelete;
		}

		static DuplicateItem PickKeeper(List<DuplicateItem> items, Strategy strategy) =>
			strategy switch {
				Strategy.SmallestFile => items.OrderByDescending(i => i.SizeLong).First(),
				Strategy.ShortestDuration => items.OrderByDescending(i => i.Duration).First(),
				Strategy.WorstResolution => items.OrderByDescending(i => i.FrameSizeInt).First(),
				// LowestQuality and HundredPercentOnly: keep the highest overall quality item
				_ => PickHighestQuality(items)
			};

		static DuplicateItem PickHighestQuality(List<DuplicateItem> items) {
			// Prefer items flagged as best by the scan engine; fall back to highest bitrate
			var best = items.FirstOrDefault(i => i.IsBestBitRateKbs)
				?? items.FirstOrDefault(i => i.IsBestFrameSize)
				?? items.OrderByDescending(i => i.BitRateKbs)
					.ThenByDescending(i => i.FrameSizeInt)
					.ThenByDescending(i => i.SizeLong)
					.First();
			return best;
		}
	}
}
