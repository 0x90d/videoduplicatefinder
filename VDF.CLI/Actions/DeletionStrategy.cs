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

using System.CommandLine.Parsing;
using VDF.Core.ViewModels;

namespace VDF.CLI.Actions {
	public enum Strategy {
		LowestQuality,
		SmallestFile,
		ShortestDuration,
		WorstResolution,
		HundredPercentOnly
	}

	/// <summary>
	/// Parses the <see cref="Strategy"/> enum from the kebab-case names shown in
	/// --help (e.g. "lowest-quality", "100-percent-only"). The default enum binder
	/// only accepts the PascalCase member names, so the documented forms were
	/// rejected; this normalises away case, hyphens and underscores and also maps
	/// "100" to the "Hundred" prefix.
	/// </summary>
	public static class StrategyParser {
		/// <summary>The canonical, documented option values.</summary>
		public static readonly IReadOnlyList<string> Names = new[] {
			"lowest-quality", "smallest-file", "shortest-duration", "worst-resolution", "100-percent-only"
		};

		public static bool TryParse(string? value, out Strategy strategy) {
			strategy = Normalize(value) switch {
				"lowestquality" => Strategy.LowestQuality,
				"smallestfile" => Strategy.SmallestFile,
				"shortestduration" => Strategy.ShortestDuration,
				"worstresolution" => Strategy.WorstResolution,
				"100percentonly" or "hundredpercentonly" => Strategy.HundredPercentOnly,
				_ => (Strategy)(-1)
			};
			return Enum.IsDefined(strategy);
		}

		/// <summary>CustomParser for the required <c>Option&lt;Strategy&gt;</c> (mark --strategy).</summary>
		public static Strategy Parse(ArgumentResult result) => ParseToken(result) ?? default;

		/// <summary>CustomParser for the optional <c>Option&lt;Strategy?&gt;</c> (scan-and-compare --action).</summary>
		public static Strategy? ParseNullable(ArgumentResult result) => ParseToken(result);

		static Strategy? ParseToken(ArgumentResult result) {
			string? token = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
			if (TryParse(token, out var strategy))
				return strategy;
			result.AddError($"Invalid strategy '{token}'. Valid values: {string.Join(", ", Names)}.");
			return null;
		}

		static string Normalize(string? value) =>
			(value ?? string.Empty).Replace("-", "").Replace("_", "").ToLowerInvariant();
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
