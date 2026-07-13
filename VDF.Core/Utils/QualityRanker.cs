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
			// Optional near-tie test: when it returns true for two values, they count as
			// tied and the NEXT criterion decides. Without it only exact equality ties,
			// which made near-continuous values (duration in ticks, bitrate in kbps)
			// effectively always decisive - a file 200ms longer beat one with double the
			// resolution (#839).
			public Func<IComparable, IComparable, bool>? NearTie { get; }
			public Criterion(string name, Func<T, IComparable> accessor, bool videoOnly, bool ascending = false,
				Func<IComparable, IComparable, bool>? nearTie = null) {
				Name = name;
				Accessor = accessor;
				VideoOnly = videoOnly;
				Ascending = ascending;
				NearTie = nearTie;
			}
			internal bool Ties(IComparable a, IComparable b) => NearTie?.Invoke(a, b) ?? Equals(a, b);
		}

		// Picks the item to keep from `items` by walking `criteria` in priority order.
		// Each criterion is "higher value wins" unless Ascending is set; on a (near-)tie,
		// the next criterion runs only against the items that tied - never against the
		// original full list. Walking stops as soon as a criterion produces a unique
		// winner, or when all criteria are exhausted (the best item of the last applied
		// criterion wins). `isImage(keep)` skips video-only criteria for image items.
		public static T PickKeeper<T>(IList<T> items, IEnumerable<Criterion<T>> criteria, Func<T, bool> isImage) =>
			PickKeeperWithReason(items, criteria, isImage).Keeper;

		/// <summary>
		/// Like PickKeeper, but also reports WHICH criterion produced the unique winner
		/// (null when the candidates stayed effectively tied through every criterion).
		/// Feeds the BEST badge tooltip (#839).
		/// </summary>
		public static (T Keeper, Criterion<T>? DecidedBy) PickKeeperWithReason<T>(IList<T> items, IEnumerable<Criterion<T>> criteria, Func<T, bool> isImage) {
			if (items.Count == 0)
				throw new ArgumentException("Items must not be empty.", nameof(items));

			IList<T> candidates = items;
			T keep = candidates[0];

			foreach (var criterion in criteria) {
				if (candidates.Count <= 1)
					break;
				if (criterion.VideoOnly && isImage(keep))
					continue;

				keep = criterion.Ascending
					? candidates.OrderBy(criterion.Accessor).First()
					: candidates.OrderByDescending(criterion.Accessor).First();
				var keepValue = criterion.Accessor(keep);
				var tied = candidates.Where(d => criterion.Ties(criterion.Accessor(d), keepValue)).ToList();
				if (tied.Count <= 1)
					return (keep, criterion);
				candidates = tied;
			}

			return (keep, null);
		}
	}
}
