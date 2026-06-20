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
using VDF.Core;
using VDF.Core.Utils;

namespace VDF.CLI.Commands {
	internal static class DatabaseCommand {
		internal static Command Build() {
			var cmd = new Command("db", "Database maintenance commands.");
			cmd.Subcommands.Add(BuildClean());
			cmd.Subcommands.Add(BuildClear());
			cmd.Subcommands.Add(BuildExport());
			return cmd;
		}

		static Command BuildClean() {
			var cmd = new Command("clean",
				"Remove database entries for files that no longer exist on disk or have errors.");
			cmd.Options.Add(SharedOptions.Database);

			cmd.SetAction(async (parseResult, ct) => {
				var db = parseResult.GetValue(SharedOptions.Database);
				if (db != null) DatabaseUtils.CustomDatabaseFolder = db;

				await ScanEngine.LoadDatabase();
				int before = DatabaseUtils.Database.Count;
				Console.Error.WriteLine($"Database loaded: {before:N0} entries.");

				DatabaseUtils.CleanupDatabase();
				int removed = before - DatabaseUtils.Database.Count;
				Console.Error.WriteLine($"Cleanup complete: {removed:N0} entries removed, {DatabaseUtils.Database.Count:N0} remaining.");
			});

			return cmd;
		}

		static Command BuildClear() {
			var cmd = new Command("clear",
				"Delete ALL entries from the scan database. This cannot be undone.");
			cmd.Options.Add(SharedOptions.Database);

			var confirmOpt = new Option<bool>("--yes") {
				Description = "Skip the confirmation prompt."
			};
			cmd.Options.Add(confirmOpt);

			cmd.SetAction(async (parseResult, ct) => {
				var db = parseResult.GetValue(SharedOptions.Database);
				if (db != null) DatabaseUtils.CustomDatabaseFolder = db;

				await ScanEngine.LoadDatabase();
				int count = DatabaseUtils.Database.Count;
				Console.Error.WriteLine($"Database loaded: {count:N0} entries.");

				if (count == 0) {
					Console.Error.WriteLine("Database is already empty.");
					return;
				}

				bool confirmed = parseResult.GetValue(confirmOpt);
				if (!confirmed) {
					Console.Error.Write($"WARNING: This will permanently delete all {count:N0} entries. Type 'yes' to confirm: ");
					string? input = Console.ReadLine();
					if (!string.Equals(input?.Trim(), "yes", StringComparison.OrdinalIgnoreCase)) {
						Console.Error.WriteLine("Aborted.");
						return;
					}
				}

				ScanEngine.ClearDatabase();
				Console.Error.WriteLine($"Database cleared. {count:N0} entries removed.");
			});

			return cmd;
		}

		static Command BuildExport() {
			var cmd = new Command("export",
				"Export the scan database to a JSON file (includes fingerprints, media info, and perceptual hashes).");

			var outputOpt = new Option<FileInfo>("--output", "-o") {
				Description = "Path for the exported JSON file.",
				Required = true,
			};
			cmd.Options.Add(outputOpt);

			var prettyOpt = new Option<bool>("--pretty") {
				Description = "Write indented (pretty-printed) JSON."
			};
			cmd.Options.Add(prettyOpt);

			cmd.Options.Add(SharedOptions.Database);

			cmd.SetAction(async (parseResult, ct) => {
				var db = parseResult.GetValue(SharedOptions.Database);
				if (db != null) DatabaseUtils.CustomDatabaseFolder = db;

				await ScanEngine.LoadDatabase();
				int count = DatabaseUtils.Database.Count;
				Console.Error.WriteLine($"Database loaded: {count:N0} entries.");

				if (count == 0) {
					Console.Error.WriteLine("Database is empty. Nothing to export.");
					return 1;
				}

				var outputPath = parseResult.GetValue(outputOpt)!;
				bool pretty = parseResult.GetValue(prettyOpt);

				var options = new System.Text.Json.JsonSerializerOptions {
					IncludeFields = true,
					WriteIndented = pretty,
				};

				bool ok = ScanEngine.ExportDataBaseToJson(outputPath.FullName, options);
				if (ok) {
					var fi = new System.IO.FileInfo(outputPath.FullName);
					Console.Error.WriteLine($"Exported {count:N0} entries to {outputPath.FullName} ({fi.Length / 1_048_576.0:F1} MB).");
					return 0;
				} else {
					Console.Error.WriteLine("Export failed.");
					return 1;
				}
			});

			return cmd;
		}
	}
}
