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

using System.Linq;

namespace VDF.Core.Utils {
	public static class QualityRanker {
		public sealed class Criterion<T> {
			public string Name { get; }
			public Func<T, IComparable> Accessor { get; }
			public bool VideoOnly { get; }
			// When true, the lower value wins instead of the higher. Used for criteria
			// where "less is better" - e.g. file size as a disk-space tiebreaker once
			// every quality signal has already tied.
			public bool Ascending { get; }
			public Criterion(string name, Func<T, IComparable> accessor, bool videoOnly, bool ascending = false) {
				Name = name;
				Accessor = accessor;
				VideoOnly = videoOnly;
				Ascending = ascending;
			}
		}

		// Picks the item to keep from `items` by walking `criteria` in priority order.
		// Each criterion is "higher value wins" unless Ascending is set; on a tie, the
		// next criterion runs only against the items that tied - never against the
		// original full list. Walking stops as soon as a criterion produces a unique
		// winner, or when all criteria are exhausted (the first remaining tied item wins).
		// `isImage(keep)` skips video-only criteria for image items.
		public static T PickKeeper<T>(IList<T> items, IEnumerable<Criterion<T>> criteria, Func<T, bool> isImage) {
			if (items.Count == 0)
				throw new ArgumentException("Items must not be empty.", nameof(items));

			IList<T> candidates = items;
			T keep = candidates[0];
			bool anyApplied = false;
			Criterion<T>? last = null;

			foreach (var criterion in criteria) {
				if (criterion.VideoOnly && isImage(keep))
					continue;

				if (anyApplied) {
					var keepValue = last!.Accessor(keep);
					var tied = candidates.Where(d => Equals(last.Accessor(d), keepValue)).ToList();
					if (tied.Count <= 1) break;
					candidates = tied;
				}

				keep = criterion.Ascending
					? candidates.OrderBy(criterion.Accessor).First()
					: candidates.OrderByDescending(criterion.Accessor).First();
				anyApplied = true;
				last = criterion;
			}

			return keep;
		}
	}
}
