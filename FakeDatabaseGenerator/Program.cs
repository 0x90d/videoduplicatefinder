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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//
using System.Globalization;
using ProtoBuf;
using VDF.Core;

namespace FakeDatabaseGenerator {
	internal static class Program {
		private const int DefaultEntries = 50_000;
		private const int DefaultSeed = 42;
		private const int DefaultThumbnailCount = 1;
		private const double DefaultMinDurationSeconds = 5;
		private const double DefaultMaxDurationSeconds = 3600;
		private const int DefaultDuplicateGroups = 0;
		private const int DefaultDuplicateGroupSize = 2;
		private const int GrayBytesSize = 32 * 32;

		public static int Main(string[] args) {
			var options = ParseArgs(args);
			if (options.ShowHelp) {
				PrintHelp();
				return 0;
			}

			var random = new Random(options.Seed);
			var positions = BuildPositions(options.ThumbnailCount);
			var entries = new HashSet<FileEntry>();

			for (int i = 0; i < options.Entries; i++) {
				entries.Add(CreateEntry(random, options, positions, i));
			}

			int nextIndex = options.Entries;
			for (int group = 0; group < options.DuplicateGroups; group++) {
				var reference = CreateEntry(random, options, positions, nextIndex++);
				entries.Add(reference);
				for (int copy = 1; copy < options.DuplicateGroupSize; copy++) {
					var cloned = CloneEntry(reference, nextIndex++);
					entries.Add(cloned);
				}
			}

			var wrapper = new DatabaseWrapper {
				Version = 2,
				Entries = entries
			};

			using var stream = File.Create(options.OutputPath);
			Serializer.Serialize(stream, wrapper);

			Console.WriteLine($"Wrote {entries.Count:N0} entries to {options.OutputPath}");
			Console.WriteLine("Remember to keep ThumbnailCount consistent with the benchmark settings.");
			return 0;
		}

		private static FileEntry CreateEntry(Random random, Options options, List<double> positions, int index) {
			double durationSeconds = options.MinDurationSeconds +
				random.NextDouble() * (options.MaxDurationSeconds - options.MinDurationSeconds);
			var entry = new FileEntry {
				Path = Path.Combine(options.PathPrefix, $"fake_{index:D6}.mp4"),
				FileSize = random.Next(5_000_000, 250_000_000),
				DateCreated = DateTime.UtcNow.AddMinutes(-index),
				DateModified = DateTime.UtcNow.AddMinutes(-index),
				mediaInfo = BuildMediaInfo(durationSeconds, random)
			};

			foreach (double position in positions) {
				double key = entry.mediaInfo!.Duration.TotalSeconds * position;
				entry.grayBytes[key] = RandomGrayBytes(random);
			}

			return entry;
		}

		private static FileEntry CloneEntry(FileEntry reference, int index) {
			var clone = new FileEntry {
				Path = Path.Combine(Path.GetDirectoryName(reference.Path) ?? string.Empty, $"fake_dup_{index:D6}.mp4"),
				FileSize = reference.FileSize,
				DateCreated = reference.DateCreated,
				DateModified = reference.DateModified,
				mediaInfo = reference.mediaInfo,
				Flags = reference.Flags
			};

			foreach (var kvp in reference.grayBytes) {
				var copy = new byte[kvp.Value!.Length];
				Array.Copy(kvp.Value, copy, copy.Length);
				clone.grayBytes[kvp.Key] = copy;
			}

			foreach (var kvp in reference.PHashes) {
				clone.PHashes[kvp.Key] = kvp.Value;
			}

			return clone;
		}

		private static MediaInfo BuildMediaInfo(double durationSeconds, Random random) {
			return new MediaInfo {
				Duration = TimeSpan.FromSeconds(durationSeconds),
				Streams = new[] {
			new MediaInfo.StreamInfo {
				Index = "0",
				CodecName = "h264",
				CodecLongName = "Fake H.264",
				CodecType = "video",
				PixelFormat = "yuv420p",
				Width = random.Next(640, 3840),
				Height = random.Next(360, 2160),
				SampleRate = 0,
				ChannelLayout = string.Empty,
				BitRate = random.Next(1_000_000, 20_000_000),
				FrameRate = (float)(24 + random.NextDouble() * 36),
				Channels = 0
			}
		}
			};
		}

		private static byte[] RandomGrayBytes(Random random) {
			var bytes = new byte[GrayBytesSize];
			random.NextBytes(bytes);
			return bytes;
		}

		private static List<double> BuildPositions(int thumbnailCount) {
			var positions = new List<double>(thumbnailCount);
			double positionCounter = 0;
			for (int i = 0; i < thumbnailCount; i++) {
				positionCounter += 1.0d / (thumbnailCount + 1);
				positions.Add(positionCounter);
			}
			return positions;
		}

		private static Options ParseArgs(string[] args) {
			var options = new Options {
				Entries = DefaultEntries,
				Seed = DefaultSeed,
				ThumbnailCount = DefaultThumbnailCount,
				MinDurationSeconds = DefaultMinDurationSeconds,
				MaxDurationSeconds = DefaultMaxDurationSeconds,
				DuplicateGroups = DefaultDuplicateGroups,
				DuplicateGroupSize = DefaultDuplicateGroupSize,
				OutputPath = Path.GetFullPath("ScannedFiles.db"),
				PathPrefix = Path.GetFullPath("fake_files")
			};

			for (int i = 0; i < args.Length; i++) {
				string arg = args[i];
				switch (arg) {
				case "--help":
				case "-h":
					options.ShowHelp = true;
					break;
				case "--entries":
					options.Entries = ParseInt(args, ref i, arg);
					break;
				case "--seed":
					options.Seed = ParseInt(args, ref i, arg);
					break;
				case "--thumbnails":
					options.ThumbnailCount = Math.Max(1, ParseInt(args, ref i, arg));
					break;
				case "--min-duration":
					options.MinDurationSeconds = ParseDouble(args, ref i, arg);
					break;
				case "--max-duration":
					options.MaxDurationSeconds = ParseDouble(args, ref i, arg);
					break;
				case "--duplicate-groups":
					options.DuplicateGroups = Math.Max(0, ParseInt(args, ref i, arg));
					break;
				case "--duplicate-group-size":
					options.DuplicateGroupSize = Math.Max(2, ParseInt(args, ref i, arg));
					break;
				case "--output":
					options.OutputPath = Path.GetFullPath(ParseString(args, ref i, arg));
					break;
				case "--path-prefix":
					options.PathPrefix = Path.GetFullPath(ParseString(args, ref i, arg));
					break;
				default:
					throw new ArgumentException($"Unknown argument: {arg}");
				}
			}

			if (options.MaxDurationSeconds < options.MinDurationSeconds) {
				throw new ArgumentException("max-duration must be >= min-duration.");
			}

			return options;
		}

		private static int ParseInt(string[] args, ref int index, string name) {
			if (index + 1 >= args.Length)
				throw new ArgumentException($"Missing value for {name}");
			index++;
			return int.Parse(args[index], CultureInfo.InvariantCulture);
		}

		private static double ParseDouble(string[] args, ref int index, string name) {
			if (index + 1 >= args.Length)
				throw new ArgumentException($"Missing value for {name}");
			index++;
			return double.Parse(args[index], CultureInfo.InvariantCulture);
		}

		private static string ParseString(string[] args, ref int index, string name) {
			if (index + 1 >= args.Length)
				throw new ArgumentException($"Missing value for {name}");
			index++;
			return args[index];
		}

		private static void PrintHelp() {
			Console.WriteLine("FakeDatabaseGenerator");
			Console.WriteLine("Usage:");
			Console.WriteLine("  dotnet run --project tools/FakeDatabaseGenerator/FakeDatabaseGenerator.csproj -- [options]");
			Console.WriteLine();
			Console.WriteLine("Options:");
			Console.WriteLine($"  --entries <int>             Number of entries (default {DefaultEntries})");
			Console.WriteLine($"  --seed <int>                Random seed (default {DefaultSeed})");
			Console.WriteLine($"  --thumbnails <int>          Thumbnail count (default {DefaultThumbnailCount})");
			Console.WriteLine($"  --min-duration <seconds>    Min duration in seconds (default {DefaultMinDurationSeconds})");
			Console.WriteLine($"  --max-duration <seconds>    Max duration in seconds (default {DefaultMaxDurationSeconds})");
			Console.WriteLine($"  --duplicate-groups <int>    Number of duplicate groups (default {DefaultDuplicateGroups})");
			Console.WriteLine($"  --duplicate-group-size <int> Duplicate group size (default {DefaultDuplicateGroupSize})");
			Console.WriteLine("  --output <path>             Output database path (default ./ScannedFiles.db)");
			Console.WriteLine("  --path-prefix <path>        Fake file path prefix (default ./fake_files)");
			Console.WriteLine("  --help, -h                  Show this help");
		}

		private sealed class Options {
			public int Entries { get; set; }
			public int Seed { get; set; }
			public int ThumbnailCount { get; set; }
			public double MinDurationSeconds { get; set; }
			public double MaxDurationSeconds { get; set; }
			public int DuplicateGroups { get; set; }
			public int DuplicateGroupSize { get; set; }
			public string OutputPath { get; set; } = string.Empty;
			public string PathPrefix { get; set; } = string.Empty;
			public bool ShowHelp { get; set; }
		}
	}
}
