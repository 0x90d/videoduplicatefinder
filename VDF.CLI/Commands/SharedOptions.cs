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

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using VDF.Core;
using VDF.Core.FFTools;

namespace VDF.CLI.Commands {
	/// <summary>Reusable option definitions shared across scan/compare commands.</summary>
	internal static class SharedOptions {

		// System.CommandLine's built-in numeric converters parse with the current culture, so on
		// a comma-decimal locale (e.g. de-DE) "0.8" becomes 8. CLI numeric arguments are
		// conventionally invariant ('.' decimal), so every float/double option parses that way on
		// every host. Tokens.Count == 0 only happens if the parser is invoked without a value;
		// returning the fallback keeps the documented default intact.
		static double ParseInvariantDouble(ArgumentResult result, double fallback) {
			if (result.Tokens.Count == 0)
				return fallback;
			string token = result.Tokens[0].Value;
			if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
				return value;
			result.AddError($"'{token}' is not a valid number (use '.' as the decimal separator, e.g. 0.8).");
			return fallback;
		}

		static float ParseInvariantFloat(ArgumentResult result, float fallback) {
			if (result.Tokens.Count == 0)
				return fallback;
			string token = result.Tokens[0].Value;
			if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
				return value;
			result.AddError($"'{token}' is not a valid number (use '.' as the decimal separator, e.g. 96.5).");
			return fallback;
		}
		internal static readonly Option<string[]> Include = new("--include", "-i") {
			Description = "Directory to include in the scan. Can be specified multiple times.",
			Arity = ArgumentArity.OneOrMore,
			AllowMultipleArgumentsPerToken = false
		};

		internal static readonly Option<string[]> Exclude = new("--exclude", "-e") {
			Description = "Directory to exclude from the scan. Can be specified multiple times.",
			Arity = ArgumentArity.ZeroOrMore,
			AllowMultipleArgumentsPerToken = false
		};

		internal static readonly Option<byte> Threshold = new("--threshold") {
			Description = "Hash difference threshold (0–10, lower = stricter). Default: 5.",
			DefaultValueFactory = _ => (byte)5
		};

		internal static readonly Option<float> Percent = new("--percent") {
			Description = "Minimum similarity percentage to report as duplicate. Default: 96.",
			DefaultValueFactory = _ => 96f,
			CustomParser = r => ParseInvariantFloat(r, 96f)
		};

		internal static readonly Option<int> Parallelism = new("--parallelism") {
			Description = "Maximum degree of parallelism for hashing. Default: 1.",
			DefaultValueFactory = _ => 1
		};

		internal static readonly Option<int> MatchingParallelism = new("--matching-parallelism") {
			Description = "Worker cap for the CPU-bound matching phases (visual compare, partial-clip compare), separate from --parallelism which governs media reads. 0 = automatic CPU-headroom cap. Default: 0.",
			DefaultValueFactory = _ => 0
		};

		internal static readonly Option<string?> Database = new("--db") {
			Description = "Custom folder to store the scan database.",
		};

		internal static readonly Option<bool> NoSubdirs = new("--no-subdirs") {
			Description = "Do not scan subdirectories."
		};

		internal static readonly Option<bool> IncludeImages = new("--include-images") {
			Description = "Include image files in the scan."
		};

		internal static readonly Option<bool> UsePhash = new("--use-phash") {
			Description = "Use perceptual hashing instead of frame sampling."
		};

		internal static readonly Option<double> PhashSampleRatio = new("--phash-sample-ratio") {
			Description = "Minimum fraction (0.01-1.0) of sampled frame positions that must individually pass the pHash similarity threshold for a pair to match. Only used with --use-phash. Default: 0.6.",
			DefaultValueFactory = _ => 0.6,
			CustomParser = r => ParseInvariantDouble(r, 0.6)
		};

		internal static readonly Option<bool> NativeFfmpeg = new("--native-ffmpeg") {
			Description = "Use native FFmpeg bindings instead of the CLI wrapper."
		};

		internal static readonly Option<FFHardwareAccelerationMode> HardwareAccel = new("--hardware-accel") {
			Description = "FFmpeg hardware acceleration mode (none, auto, cuda, vaapi, etc.). Default: none.",
			DefaultValueFactory = _ => FFHardwareAccelerationMode.none
		};

		internal static readonly Option<string?> CustomFfArgs = new("--ff-args") {
			Description = "Additional custom FFmpeg arguments."
		};

		internal static readonly Option<bool> IncludeNonExistingFiles = new("--include-non-existing") {
			Description = "Compare against database entries whose files no longer exist on disk."
		};

		internal static readonly Option<bool> EnablePartialClipDetection = new("--partial-clip-detection") {
			Description = "Enable partial clip detection via audio fingerprinting."
		};

		internal static readonly Option<double> PartialClipMinRatio = new("--partial-clip-min-ratio") {
			Description = "Minimum clip/source duration ratio (0.0–1.0). Default: 0.10.",
			DefaultValueFactory = _ => 0.10,
			CustomParser = r => ParseInvariantDouble(r, 0.10)
		};

		internal static readonly Option<double> PartialClipSimilarityThreshold = new("--partial-clip-similarity") {
			Description = "Minimum audio fingerprint similarity threshold (0.0–1.0). Default: 0.80.",
			DefaultValueFactory = _ => 0.80,
			CustomParser = r => ParseInvariantDouble(r, 0.80)
		};

		internal static readonly Option<bool> PartialClipRequireVisualMatch = new("--partial-clip-require-visual") {
			Description = "Require an on-demand visual frame check on partial-clip matches to filter false positives from videos that share an audio track but differ visually. Default: true.",
			DefaultValueFactory = _ => true
		};

		internal static readonly Option<double> PartialClipVisualThreshold = new("--partial-clip-visual-threshold") {
			Description = "Minimum visual similarity (0.0–1.0) for the partial-clip visual gate. Uses pHash when --use-phash is set, else 32x32 grayscale percent diff. Default: 0.85.",
			DefaultValueFactory = _ => 0.85,
			CustomParser = r => ParseInvariantDouble(r, 0.85)
		};

		internal static readonly Option<int> CheckpointInterval = new("--checkpoint-interval") {
			Description = "Database checkpoint interval in minutes during scanning. 0 = disabled. Default: 5.",
			DefaultValueFactory = _ => 5
		};

		internal static readonly Option<FileInfo?> SettingsFile = new("--settings", "-s") {
			Description = "Path to a VDF settings JSON file. Individual flags override values from this file."
		};

		internal static readonly Option<string> Format = new("--format", "-f") {
			Description = "Output format: text (default), json, csv.",
			DefaultValueFactory = _ => "text"
		};

		internal static readonly Option<FileInfo?> Output = new("--output", "-o") {
			Description = "Write results to a file instead of stdout."
		};

		internal static void ApplyToSettings(Settings s, ParseResult r) {
			var includes = r.GetValue(Include);
			if (includes != null)
				foreach (var p in includes) s.IncludeList.Add(p);

			var excludes = r.GetValue(Exclude);
			if (excludes != null)
				foreach (var p in excludes) s.BlackList.Add(p);

			s.Threshhold = r.GetValue(Threshold);
			s.Percent = r.GetValue(Percent);
			// Commands that don't register --parallelism (e.g. 'compare') get default(int) == 0
			// back from GetValue, and 0 is the one value ParallelOptions rejects (#804). Remap
			// only that sentinel to the documented default of 1; -1 (unbounded) and any positive
			// value the user actually passed are left untouched.
			int parallelism = r.GetValue(Parallelism);
			s.MaxDegreeOfParallelism = parallelism == 0 ? 1 : parallelism;
			// 0 (also what commands that don't register the option get back) = automatic
			// CPU-headroom cap resolved by the engine.
			s.MatchingMaxDegreeOfParallelism = r.GetValue(MatchingParallelism);
			s.IncludeSubDirectories = !r.GetValue(NoSubdirs);
			s.IncludeImages = r.GetValue(IncludeImages);
			s.UsePHashing = r.GetValue(UsePhash);
			// --phash-sample-ratio is only registered on commands that expose pHash tuning;
			// for the rest GetResult is null and we keep the documented default. When it IS
			// present, clamp to [0.01, 1] — GetValue returns the 0.6 DefaultValueFactory when
			// omitted, and an explicit 0 clamps to the 0.01 minimum instead of being mistaken
			// for "unset" (avoids the 0-sentinel overload the --parallelism/#804 remap has).
			if (r.GetResult(PhashSampleRatio) != null)
				s.PHashRequiredMatchingSampleRatio = (float)Math.Clamp(r.GetValue(PhashSampleRatio), 0.01d, 1d);
			else
				s.PHashRequiredMatchingSampleRatio = 0.6f;
			s.UseNativeFfmpegBinding = r.GetValue(NativeFfmpeg);
			s.HardwareAccelerationMode = r.GetValue(HardwareAccel);

			var db = r.GetValue(Database);
			if (db != null) s.CustomDatabaseFolder = db;

			var ffArgs = r.GetValue(CustomFfArgs);
			if (ffArgs != null) s.CustomFFArguments = ffArgs;

			s.DatabaseCheckpointIntervalMinutes = r.GetValue(CheckpointInterval);
			s.IncludeNonExistingFiles = r.GetValue(IncludeNonExistingFiles);
			s.EnablePartialClipDetection = r.GetValue(EnablePartialClipDetection);
			s.PartialClipMinRatio = r.GetValue(PartialClipMinRatio);
			s.PartialClipSimilarityThreshold = r.GetValue(PartialClipSimilarityThreshold);
			s.PartialClipRequireVisualMatch = r.GetValue(PartialClipRequireVisualMatch);
			s.PartialClipVisualThreshold = r.GetValue(PartialClipVisualThreshold);
		}

		internal static void AddScanOptions(Command cmd) {
			cmd.Options.Add(Include);
			cmd.Options.Add(Exclude);
			cmd.Options.Add(Threshold);
			cmd.Options.Add(Percent);
			cmd.Options.Add(Parallelism);
			cmd.Options.Add(MatchingParallelism);
			cmd.Options.Add(Database);
			cmd.Options.Add(NoSubdirs);
			cmd.Options.Add(IncludeImages);
			cmd.Options.Add(UsePhash);
			cmd.Options.Add(PhashSampleRatio);
			cmd.Options.Add(NativeFfmpeg);
			cmd.Options.Add(HardwareAccel);
			cmd.Options.Add(CustomFfArgs);
			cmd.Options.Add(CheckpointInterval);
			cmd.Options.Add(IncludeNonExistingFiles);
			cmd.Options.Add(EnablePartialClipDetection);
			cmd.Options.Add(PartialClipMinRatio);
			cmd.Options.Add(PartialClipSimilarityThreshold);
			cmd.Options.Add(PartialClipRequireVisualMatch);
			cmd.Options.Add(PartialClipVisualThreshold);
			cmd.Options.Add(SettingsFile);
			cmd.Options.Add(Format);
			cmd.Options.Add(Output);
		}
	}
}
