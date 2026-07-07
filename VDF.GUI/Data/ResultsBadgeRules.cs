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

using System;
using System.Globalization;

namespace VDF.GUI.Data {
	/// <summary>
	/// How a metric value (size/duration/resolution) is emphasized in the results list.
	/// </summary>
	enum MetricEmphasis {
		/// <summary>Best value in the group — rendered in the palette's good color.</summary>
		Good,
		/// <summary>Not the best value — rendered in the palette's danger color.</summary>
		Bad,
		/// <summary>Hover-diff shows the values are equal — rendered muted.</summary>
		Equal
	}

	/// <summary>
	/// Pure presentation rules for the results list's similarity chip and metric
	/// coloring. The XAML converters delegate here so the thresholds and formatting
	/// stay unit-testable without an Avalonia runtime.
	/// </summary>
	static class ResultsBadgeRules {
		/// <summary>Mid tier (amber chip): high similarity but not near-identical.</summary>
		internal static bool IsMidSimilarity(float similarity) => similarity < 99f && similarity >= 90f;
		/// <summary>Low tier (red chip): below the mid band.</summary>
		internal static bool IsLowSimilarity(float similarity) => similarity < 90f;

		/// <summary>
		/// Chip label per the mockup: whole percentages drop the fraction ("100 %"),
		/// everything else keeps one decimal ("98.2 %", locale decimal separator).
		/// </summary>
		internal static string FormatSimilarity(float similarity, CultureInfo culture) {
			float rounded = MathF.Round(similarity, 1);
			string format = rounded == MathF.Truncate(rounded) ? "{0:F0} %" : "{0:F1} %";
			return string.Format(culture, format, rounded);
		}

		/// <summary>The hover-diff marker for "both values are equal".</summary>
		internal static bool IsEqualDiff(string? diff) => diff == "=";

		internal static MetricEmphasis GetEmphasis(bool isBest, string? diff) =>
			IsEqualDiff(diff) ? MetricEmphasis.Equal : isBest ? MetricEmphasis.Good : MetricEmphasis.Bad;
	}
}
