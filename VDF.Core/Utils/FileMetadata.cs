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
using System.Text.Json;
using VDF.Core.FFTools;

namespace VDF.Core.Utils {

	public enum MetadataSectionKind { Container, Stream, Exif, Gps }

	/// <summary>Where a tag lives: the container, one stream (by index), or the EXIF/GPS block of a photo.</summary>
	public readonly record struct MetadataSection(MetadataSectionKind Kind, int StreamIndex = -1, string StreamType = "");

	public sealed record MetadataField(MetadataSection Section, string Name, string Value);

	/// <summary>
	/// Every metadata tag of a file, read on demand (#926): nothing of this is stored in the
	/// database, it is only needed while someone looks at one group. Container and stream tags
	/// come from ffprobe (videos: creation_time, com.apple.quicktime.creationdate with its
	/// time zone, make, model, location...), EXIF and GPS from <see cref="ExifReader"/>.
	/// </summary>
	public static class FileMetadata {

		/// <summary>Null when the file does not exist; an empty list when it carries no tags.</summary>
		public static List<MetadataField>? Read(string path) {
			if (!File.Exists(path))
				return null;
			var fields = new List<MetadataField>();
			if (ScanEngine.FFprobeExists && FFProbeEngine.GetTagsJson(path) is { } json)
				fields.AddRange(ParseProbeTags(json));
			foreach (var (isGps, name, value) in ExifReader.ReadAllTags(path))
				fields.Add(new(new(isGps ? MetadataSectionKind.Gps : MetadataSectionKind.Exif), name, value));
			return fields;
		}

		/// <summary>The tags of <c>ffprobe -show_entries format_tags:stream=index,codec_type:stream_tags -of json</c>.</summary>
		internal static List<MetadataField> ParseProbeTags(byte[] json) {
			var fields = new List<MetadataField>();
			try {
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;
				if (root.TryGetProperty("format", out var format))
					AddTags(format, new MetadataSection(MetadataSectionKind.Container), fields);
				if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array) {
					foreach (var stream in streams.EnumerateArray()) {
						int index = stream.TryGetProperty("index", out var i) && i.TryGetInt32(out int n) ? n : -1;
						string type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
						AddTags(stream, new MetadataSection(MetadataSectionKind.Stream, index, type), fields);
					}
				}
			}
			catch (JsonException) { }
			return fields;
		}

		static void AddTags(JsonElement owner, MetadataSection section, List<MetadataField> fields) {
			if (!owner.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
				return;
			foreach (var tag in tags.EnumerateObject()) {
				string value = tag.Value.ValueKind == JsonValueKind.String ? tag.Value.GetString()! : tag.Value.GetRawText();
				fields.Add(new(section, tag.Name, value));
			}
		}
	}

	/// <summary>One field across all compared files.</summary>
	public sealed class MetadataComparisonRow {
		public required MetadataSection Section { get; init; }
		public required string Name { get; init; }
		/// <summary>Per file, in the order given; null where the file doesn't have the field.</summary>
		public required string?[] Values { get; init; }
		/// <summary>Not every file has the same value (a missing field counts as a value).</summary>
		public bool Differs { get; init; }
		/// <summary>
		/// Per file: this value is not the one most files share. When no value is shared by
		/// more files than any other (two files that disagree), every value of a differing row is.
		/// </summary>
		public required bool[] IsOdd { get; init; }
	}

	public static class MetadataComparison {

		/// <summary>
		/// Lines the files' fields up: container first, then the streams by index, then EXIF
		/// and GPS; within a section in the order the files list them. A file that could not
		/// be read (null) is left out of the majority vote and never marked odd.
		/// </summary>
		public static List<MetadataComparisonRow> Build(IReadOnlyList<IReadOnlyList<MetadataField>?> files) {
			var keys = new List<(MetadataSection Section, string Name)>();
			var seen = new HashSet<(MetadataSection, string)>();
			var lookup = new Dictionary<(MetadataSection, string), string>[files.Count];
			for (int f = 0; f < files.Count; f++) {
				lookup[f] = new();
				if (files[f] is not { } fields) continue;
				foreach (var field in fields) {
					var key = (field.Section, field.Name);
					lookup[f].TryAdd(key, field.Value);
					if (seen.Add(key)) keys.Add(key);
				}
			}

			var ordered = keys
				.Select((k, i) => (k, i))
				.OrderBy(x => x.k.Section.Kind)
				.ThenBy(x => x.k.Section.StreamIndex)
				.ThenBy(x => x.i)
				.Select(x => x.k);

			var rows = new List<MetadataComparisonRow>();
			foreach (var key in ordered) {
				var values = new string?[files.Count];
				for (int f = 0; f < files.Count; f++)
					values[f] = lookup[f].TryGetValue(key, out var v) ? v : null;

				var readable = Enumerable.Range(0, files.Count).Where(f => files[f] != null).ToList();
				var counts = readable.GroupBy(f => values[f] ?? "\0missing").Select(g => (Value: g.Key, Count: g.Count()))
					.OrderByDescending(c => c.Count).ToList();
				bool differs = counts.Count > 1;
				string? majority = differs && counts[0].Count > counts[1].Count ? counts[0].Value : null;
				var odd = new bool[files.Count];
				if (differs)
					foreach (int f in readable)
						odd[f] = majority == null || (values[f] ?? "\0missing") != majority;

				rows.Add(new MetadataComparisonRow {
					Section = key.Section, Name = key.Name, Values = values, Differs = differs, IsOdd = odd,
				});
			}
			return rows;
		}
	}
}
