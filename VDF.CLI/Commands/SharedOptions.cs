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

using System.CommandLine;
using VDF.Core;
using VDF.Core.FFTools;

namespace VDF.CLI.Commands {
	/// <summary>Reusable option definitions shared across scan/compare commands.</summary>
	internal static class SharedOptions {
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
			DefaultValueFactory = _ => 96f
		};

		internal static readonly Option<int> Parallelism = new("--parallelism") {
			Description = "Maximum degree of parallelism for hashing. Default: 1.",
			DefaultValueFactory = _ => 1
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

		internal static readonly Option<bool> EnablePartialClipDetection = new("--partial-clip-detection") {
			Description = "Enable partial clip detection via audio fingerprinting."
		};

		internal static readonly Option<double> PartialClipMinRatio = new("--partial-clip-min-ratio") {
			Description = "Minimum clip/source duration ratio (0.0–1.0). Default: 0.10.",
			DefaultValueFactory = _ => 0.10
		};

		internal static readonly Option<double> PartialClipSimilarityThreshold = new("--partial-clip-similarity") {
			Description = "Minimum audio fingerprint similarity threshold (0.0–1.0). Default: 0.80.",
			DefaultValueFactory = _ => 0.80
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
			s.MaxDegreeOfParallelism = r.GetValue(Parallelism);
			s.IncludeSubDirectories = !r.GetValue(NoSubdirs);
			s.IncludeImages = r.GetValue(IncludeImages);
			s.UsePHashing = r.GetValue(UsePhash);
			s.UseNativeFfmpegBinding = r.GetValue(NativeFfmpeg);
			s.HardwareAccelerationMode = r.GetValue(HardwareAccel);

			var db = r.GetValue(Database);
			if (db != null) s.CustomDatabaseFolder = db;

			var ffArgs = r.GetValue(CustomFfArgs);
			if (ffArgs != null) s.CustomFFArguments = ffArgs;

			s.EnablePartialClipDetection = r.GetValue(EnablePartialClipDetection);
			s.PartialClipMinRatio = r.GetValue(PartialClipMinRatio);
			s.PartialClipSimilarityThreshold = r.GetValue(PartialClipSimilarityThreshold);
		}

		internal static void AddScanOptions(Command cmd) {
			cmd.Options.Add(Include);
			cmd.Options.Add(Exclude);
			cmd.Options.Add(Threshold);
			cmd.Options.Add(Percent);
			cmd.Options.Add(Parallelism);
			cmd.Options.Add(Database);
			cmd.Options.Add(NoSubdirs);
			cmd.Options.Add(IncludeImages);
			cmd.Options.Add(UsePhash);
			cmd.Options.Add(NativeFfmpeg);
			cmd.Options.Add(HardwareAccel);
			cmd.Options.Add(CustomFfArgs);
			cmd.Options.Add(EnablePartialClipDetection);
			cmd.Options.Add(PartialClipMinRatio);
			cmd.Options.Add(PartialClipSimilarityThreshold);
			cmd.Options.Add(SettingsFile);
			cmd.Options.Add(Format);
			cmd.Options.Add(Output);
		}
	}
}
