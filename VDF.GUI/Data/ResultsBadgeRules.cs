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
using System.Text;
using VDF.Core.Utils;

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

		/// <summary>
		/// Bitrate cell text: ≥ 1000 kb/s reads as Mb/s with one decimal, below stays
		/// in kb/s; zero/unknown renders empty so image rows and unprobed files stay clean.
		/// </summary>
		internal static string FormatBitrate(decimal kbs, CultureInfo culture) {
			if (kbs <= 0) return string.Empty;
			return kbs >= 1000m
				? string.Format(culture, "{0:0.0} Mb/s", kbs / 1000m)
				: string.Format(culture, "{0:0} kb/s", kbs);
		}

		internal static MetricEmphasis GetEmphasis(bool isBest, string? diff) =>
			IsEqualDiff(diff) ? MetricEmphasis.Equal : isBest ? MetricEmphasis.Good : MetricEmphasis.Bad;

		/// <summary>"48 kHz" style sample-rate text; empty when unknown.</summary>
		internal static string FormatSampleRate(int hz, CultureInfo culture) =>
			hz <= 0 ? string.Empty : string.Format(culture, "{0:0.#} kHz", hz / 1000.0);

		/// <summary>"HEVC · 3840×2160 · 59.94 fps · 00:04:11 · 51.5 Mb/s · HLG" — only fields that exist.</summary>
		internal static string BuildVideoLine(VDF.Core.ViewModels.DuplicateItem item, CultureInfo culture) => JoinParts(" · ",
			item.Format,
			item.FrameSize,
			item.IsImage || item.Fps <= 0 ? null : string.Format(culture, "{0:0.###} fps", item.Fps),
			item.IsImage || item.Duration <= TimeSpan.Zero ? null : string.Format(culture, "{0:hh\\:mm\\:ss}", item.Duration),
			FormatBitrate(item.BitRateKbs, culture),
			string.IsNullOrEmpty(item.HdrFormat) ? null : item.HdrFormat);

		/// <summary>"AAC · 2.0 · 48 kHz · 192 kb/s" — empty for files without audio.</summary>
		internal static string BuildAudioLine(VDF.Core.ViewModels.DuplicateItem item, CultureInfo culture) => JoinParts(" · ",
			item.AudioFormat,
			item.AudioChannel,
			FormatSampleRate(item.AudioSampleRate, culture),
			FormatBitrate(item.AudioBitRateKbs, culture));

		/// <summary>"712 MB · 28.04.2024 18:03" (size, creation date).</summary>
		internal static string BuildFileLine(VDF.Core.ViewModels.DuplicateItem item, CultureInfo culture) =>
			item.SizeLong.BytesToString() + " · " + item.DateCreated.ToString("g", culture);

		/// <summary>
		/// Plain-text summary behind the details panel's Copy button. Joins only the
		/// lines that exist, so image rows don't produce empty audio lines.
		/// </summary>
		internal static string BuildDetailsText(VDF.Core.ViewModels.DuplicateItem item) {
			var culture = CultureInfo.CurrentCulture;
			var sb = new StringBuilder();
			sb.AppendLine(item.Path);
			var video = BuildVideoLine(item, culture);
			if (video.Length > 0)
				sb.AppendLine((item.IsImage ? "Image: " : "Video: ") + video);
			var audio = BuildAudioLine(item, culture);
			if (audio.Length > 0)
				sb.AppendLine("Audio: " + audio);
			sb.Append("File: ").Append(BuildFileLine(item, culture));
			return sb.ToString();
		}

		internal static string JoinParts(string separator, params string?[] parts) {
			var sb = new StringBuilder();
			foreach (var part in parts) {
				if (string.IsNullOrWhiteSpace(part)) continue;
				if (sb.Length > 0) sb.Append(separator);
				sb.Append(part);
			}
			return sb.ToString();
		}
	}
}
