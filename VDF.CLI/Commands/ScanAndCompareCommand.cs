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
using VDF.CLI.Actions;
using VDF.CLI.Output;
using VDF.Core;

namespace VDF.CLI.Commands {
	internal static class ScanAndCompareCommand {
		internal static Command Build() {
			var cmd = new Command("scan-and-compare", "Enumerate files, build hashes, and find duplicates in one step.");
			SharedOptions.AddScanOptions(cmd);

			var actionOpt = new Option<Strategy?>("--action") {
				Description = "Automatically mark files for deletion using a strategy (see 'mark --help'). Implies --dry-run unless --delete or --delete-permanent is also specified."
			};
			var dryRunOpt = new Option<bool>("--dry-run") {
				Description = "Print which files would be deleted without deleting anything. Default when --action is specified."
			};
			var deleteOpt = new Option<bool>("--delete") {
				Description = "Move marked files to the system recycle bin / trash."
			};
			var deletePermanentOpt = new Option<bool>("--delete-permanent") {
				Description = "Permanently delete marked files. WARNING: irreversible."
			};

			cmd.Options.Add(actionOpt);
			cmd.Options.Add(dryRunOpt);
			cmd.Options.Add(deleteOpt);
			cmd.Options.Add(deletePermanentOpt);

			cmd.SetAction(async (parseResult, ct) => {
				var engine = new ScanEngine();
				var settings = ScanRunner.LoadOrCreateSettings(parseResult.GetValue(SharedOptions.SettingsFile));
				// Copy loaded settings into engine settings
				CopySettings(settings, engine.Settings);
				SharedOptions.ApplyToSettings(engine.Settings, parseResult);

				if (engine.Settings.IncludeList.Count == 0) {
					Console.Error.WriteLine("Error: at least one --include path is required.");
					return;
				}

				ScanRunner.WireProgress(engine);

				var duplicates = await ScanRunner.RunScanAndCompareAsync(engine, ct);

				var format = Enum.TryParse<OutputFormat>(parseResult.GetValue(SharedOptions.Format), true, out var fmt) ? fmt : OutputFormat.Text;
				var outFile = parseResult.GetValue(SharedOptions.Output);

				var strategy = parseResult.GetValue(actionOpt);
				if (strategy.HasValue) {
					var marked = DeletionStrategy.SelectForDeletion(duplicates, strategy.Value);
					bool doPermanent = parseResult.GetValue(deletePermanentOpt);
					bool doDelete = parseResult.GetValue(deleteOpt) || doPermanent;
					bool dryRun = !doDelete || parseResult.GetValue(dryRunOpt);
					await MarkCommand.ExecuteDeletion(marked, dryRun, doPermanent);
				}

				string output = ResultFormatter.Format(duplicates, format);
				WriteOutput(output, outFile);
			});

			return cmd;
		}

		static void CopySettings(Core.Settings source, Core.Settings dest) {
			foreach (var p in source.IncludeList) dest.IncludeList.Add(p);
			foreach (var p in source.BlackList) dest.BlackList.Add(p);
			dest.Threshhold = source.Threshhold;
			dest.Percent = source.Percent;
			dest.MaxDegreeOfParallelism = source.MaxDegreeOfParallelism;
			dest.IncludeSubDirectories = source.IncludeSubDirectories;
			dest.IncludeImages = source.IncludeImages;
			dest.UsePHashing = source.UsePHashing;
			dest.UseNativeFfmpegBinding = source.UseNativeFfmpegBinding;
			dest.HardwareAccelerationMode = source.HardwareAccelerationMode;
			dest.CustomFFArguments = source.CustomFFArguments;
			dest.CustomDatabaseFolder = source.CustomDatabaseFolder;
			dest.EnablePartialClipDetection = source.EnablePartialClipDetection;
			dest.PartialClipMinRatio = source.PartialClipMinRatio;
			dest.PartialClipSimilarityThreshold = source.PartialClipSimilarityThreshold;
		}

		static void WriteOutput(string content, FileInfo? outFile) {
			if (outFile != null) {
				File.WriteAllText(outFile.FullName, content);
				Console.Error.WriteLine($"Results written to: {outFile.FullName}");
			}
			else {
				Console.Write(content);
			}
		}
	}
}
