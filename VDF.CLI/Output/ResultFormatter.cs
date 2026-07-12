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

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VDF.Core.ViewModels;

namespace VDF.CLI.Output {
	public enum OutputFormat { Text, Json, Csv }

	public static class ResultFormatter {
		public static string Format(IEnumerable<DuplicateItem> duplicates, OutputFormat format) =>
			format switch {
				OutputFormat.Json => FormatJson(duplicates),
				OutputFormat.Csv => FormatCsv(duplicates),
				_ => FormatText(duplicates)
			};

		static string FormatJson(IEnumerable<DuplicateItem> duplicates) {
			var groups = duplicates
				.GroupBy(d => d.GroupId)
				.Select(g => new DuplicateGroup {
					GroupId = g.Key,
					Items = g.OrderByDescending(d => d.Similarity).ToList()
				})
				.ToList();
			return JsonSerializer.Serialize(groups, CliJsonContext.Default.ListDuplicateGroup);
		}

		static string FormatCsv(IEnumerable<DuplicateItem> duplicates) {
			// All numbers invariant: on a comma-decimal locale (de-DE) culture-formatted
			// values like "60,000" inject extra CSV columns and shift every field after them.
			var inv = CultureInfo.InvariantCulture;
			var sb = new StringBuilder();
			sb.AppendLine("GroupId,Similarity,Path,Size,Duration,FrameSize,Format,Fps,BitRateKbs,AudioFormat,DateCreated,IsImage,Flags,PartialClipOffset");
			foreach (var d in duplicates.OrderBy(d => d.GroupId).ThenByDescending(d => d.Similarity)) {
				sb.AppendLine(string.Join(",",
					d.GroupId,
					d.Similarity.ToString("F1", inv),
					CsvEscape(d.Path),
					d.SizeLong,
					d.Duration.TotalSeconds.ToString("F3", inv),
					CsvEscape(d.FrameSize ?? string.Empty),
					CsvEscape(d.Format ?? string.Empty),
					d.Fps.ToString("F2", inv),
					d.BitRateKbs.ToString(inv),
					CsvEscape(d.AudioFormat ?? string.Empty),
					d.DateCreated.ToString("O"),
					d.IsImage,
					// Multi-bit values render as "PartialClip, AiMatched" — the comma must
					// not become an extra CSV column.
					CsvEscape(d.Flags.ToString()),
					d.PartialClipOffset.TotalSeconds.ToString("F0", inv)
				));
			}
			return sb.ToString();
		}

		static string FormatText(IEnumerable<DuplicateItem> duplicates) {
			var sb = new StringBuilder();
			var groups = duplicates
				.GroupBy(d => d.GroupId)
				.OrderBy(g => g.Key)
				.ToList();

			sb.AppendLine($"Found {groups.Count} duplicate group(s), {duplicates.Count()} total file(s).");
			sb.AppendLine();

			int groupNum = 1;
			foreach (var group in groups) {
				sb.AppendLine($"Group {groupNum++} ({group.Count()} files):");
				foreach (var item in group.OrderByDescending(d => d.Similarity)) {
					string best = item.IsBestBitRateKbs || item.IsBestFrameSize ? " [best]" : string.Empty;
					string partial = item.PartialClipOffsetDisplay.Length > 0 ? $" [partial clip {item.PartialClipOffsetDisplay}]" : string.Empty;
					// AI-union pairs are the lower-confidence matches — users need to see
					// which pairing came from the AI pass before acting on it.
					string ai = item.Flags.HasFlag(VDF.Core.DuplicateFlags.AiMatched) ? " [AI]" : string.Empty;
					sb.AppendLine($"  {item.Similarity,6:F1}%  {item.Path}{best}{partial}{ai}");
					if (!item.IsImage) {
						string details = $"         {item.FrameSize ?? "?"}, {item.Format ?? "?"}, {item.Fps:F2} fps, {item.BitRateKbs} kbps, {item.Duration:hh\\:mm\\:ss}";
						sb.AppendLine(details);
					}
					else {
						sb.AppendLine($"         {item.FrameSize ?? "?"}, {item.Format ?? "?"}");
					}
				}
				sb.AppendLine();
			}
			return sb.ToString();
		}

		static string CsvEscape(string value) {
			if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
				return $"\"{value.Replace("\"", "\"\"")}\"";
			return value;
		}
	}
}
