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

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VDF.Core.ViewModels;

namespace VDF.CLI.Output {
	public enum OutputFormat { Text, Json, Csv }

	public static class ResultFormatter {
		static readonly JsonSerializerOptions JsonOptions = new() {
			WriteIndented = true,
			Converters = { new JsonStringEnumConverter() }
		};

		public static string Format(IEnumerable<DuplicateItem> duplicates, OutputFormat format) =>
			format switch {
				OutputFormat.Json => FormatJson(duplicates),
				OutputFormat.Csv => FormatCsv(duplicates),
				_ => FormatText(duplicates)
			};

		static string FormatJson(IEnumerable<DuplicateItem> duplicates) {
			var groups = duplicates
				.GroupBy(d => d.GroupId)
				.Select(g => new {
					GroupId = g.Key,
					Items = g.OrderByDescending(d => d.Similarity).ToList()
				});
			return JsonSerializer.Serialize(groups, JsonOptions);
		}

		static string FormatCsv(IEnumerable<DuplicateItem> duplicates) {
			var sb = new StringBuilder();
			sb.AppendLine("GroupId,Similarity,Path,Size,Duration,FrameSize,Format,Fps,BitRateKbs,AudioFormat,DateCreated,IsImage,Flags,PartialClipOffset");
			foreach (var d in duplicates.OrderBy(d => d.GroupId).ThenByDescending(d => d.Similarity)) {
				sb.AppendLine(string.Join(",",
					d.GroupId,
					d.Similarity.ToString("F1"),
					CsvEscape(d.Path),
					d.SizeLong,
					d.Duration.TotalSeconds.ToString("F3"),
					CsvEscape(d.FrameSize ?? string.Empty),
					CsvEscape(d.Format ?? string.Empty),
					d.Fps.ToString("F2"),
					d.BitRateKbs,
					CsvEscape(d.AudioFormat ?? string.Empty),
					d.DateCreated.ToString("O"),
					d.IsImage,
					d.Flags,
					d.PartialClipOffset.TotalSeconds.ToString("F0")
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
					sb.AppendLine($"  {item.Similarity,6:F1}%  {item.Path}{best}{partial}");
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
