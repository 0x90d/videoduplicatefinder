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
using System.Text.Json;
using VDF.CLI.Actions;
using VDF.Core.ViewModels;

namespace VDF.CLI.Commands {
	internal static class MarkCommand {
		internal static Command Build() {
			var cmd = new Command("mark",
				"Read a JSON results file and mark files for deletion based on a strategy.\n\n" +
				"WARNING: Automatic deletion is not recommended. Always use --dry-run first to review what would be deleted.");

			var inputOpt = new Option<FileInfo>("--input", "-i") {
				Description = "Path to a JSON results file produced by scan-and-compare --format json.",
				Required = true
			};
			var strategyOpt = new Option<Strategy>("--strategy") {
				Description = "Selection strategy: lowest-quality, smallest-file, shortest-duration, worst-resolution, 100-percent-only.",
				Required = true
			};
			var dryRunOpt = new Option<bool>("--dry-run") {
				Description = "Print which files would be deleted without deleting anything (default).",
				DefaultValueFactory = _ => true
			};
			var deleteOpt = new Option<bool>("--delete") {
				Description = "Move files to the system recycle bin / trash."
			};
			var deletePermanentOpt = new Option<bool>("--delete-permanent") {
				Description = "Permanently delete files. WARNING: irreversible."
			};

			cmd.Options.Add(inputOpt);
			cmd.Options.Add(strategyOpt);
			cmd.Options.Add(dryRunOpt);
			cmd.Options.Add(deleteOpt);
			cmd.Options.Add(deletePermanentOpt);

			cmd.SetAction(async (parseResult, ct) => {
				var inputFile = parseResult.GetValue(inputOpt)!;
				if (!inputFile.Exists) {
					Console.Error.WriteLine($"Error: input file not found: {inputFile.FullName}");
					return;
				}

				List<DuplicateItem>? duplicates;
				try {
					await using var stream = inputFile.OpenRead();
					// The JSON is an array of groups, each with an Items array
					var groups = await JsonSerializer.DeserializeAsync<List<DuplicateGroup>>(stream, cancellationToken: ct);
					duplicates = groups?.SelectMany(g => g.Items).ToList();
				}
				catch (Exception ex) {
					Console.Error.WriteLine($"Error reading results file: {ex.Message}");
					return;
				}

				if (duplicates == null || duplicates.Count == 0) {
					Console.Error.WriteLine("No duplicates found in the results file.");
					return;
				}

				var strategy = parseResult.GetValue(strategyOpt);
				var marked = DeletionStrategy.SelectForDeletion(duplicates, strategy);

				bool doPermanent = parseResult.GetValue(deletePermanentOpt);
				bool doDelete = parseResult.GetValue(deleteOpt) || doPermanent;
				bool dryRun = !doDelete || parseResult.GetValue(dryRunOpt);

				await ExecuteDeletion(marked, dryRun, doPermanent);
			});

			return cmd;
		}

		internal static async Task ExecuteDeletion(IReadOnlyList<DuplicateItem> marked, bool dryRun, bool permanent) {
			if (marked.Count == 0) {
				Console.Error.WriteLine("No files selected for deletion by the chosen strategy.");
				return;
			}

			if (!dryRun) {
				Console.Error.WriteLine();
				Console.Error.WriteLine("WARNING: Automatic deletion is not recommended.");
				Console.Error.WriteLine($"         {marked.Count} file(s) will be {(permanent ? "permanently deleted" : "moved to trash")}.");
				Console.Error.WriteLine("         This action cannot be undone. Proceeding in 3 seconds... (Ctrl+C to abort)");
				await Task.Delay(3000);
			}

			int deleted = 0, failed = 0;
			foreach (var item in marked) {
				if (dryRun) {
					Console.WriteLine($"[dry-run] would delete: {item.Path}");
					continue;
				}

				try {
					if (permanent) {
						File.Delete(item.Path);
					}
					else {
						MoveToTrash(item.Path);
					}
					Console.Error.WriteLine($"Deleted: {item.Path}");
					deleted++;
				}
				catch (Exception ex) {
					Console.Error.WriteLine($"Failed to delete '{item.Path}': {ex.Message}");
					failed++;
				}
			}

			if (!dryRun) {
				Console.Error.WriteLine($"Done. {deleted} deleted, {failed} failed.");
			}
			else {
				Console.Error.WriteLine($"[dry-run] {marked.Count} file(s) would be deleted. Use --delete or --delete-permanent to proceed.");
			}
		}

		static void MoveToTrash(string path) {
			// Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile with recycle option is Windows-only.
			// For cross-platform trash support we use a best-effort approach.
			if (OperatingSystem.IsWindows()) {
				Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
					path,
					Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
					Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
			}
			else if (OperatingSystem.IsLinux()) {
				// XDG trash specification: move to ~/.local/share/Trash/files/
				string trashDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					".local", "share", "Trash", "files");
				Directory.CreateDirectory(trashDir);
				// Skip trash for cross-filesystem files (e.g. network shares) to avoid downloading
				if (!VDF.Core.Utils.FileUtils.IsOnSameFileSystem(path, trashDir)) {
					File.Delete(path);
					return;
				}
				string dest = Path.Combine(trashDir, Path.GetFileName(path));
				// Avoid overwriting existing trash entries
				int n = 1;
				while (File.Exists(dest))
					dest = Path.Combine(trashDir, $"{Path.GetFileNameWithoutExtension(path)}_{n++}{Path.GetExtension(path)}");
				File.Move(path, dest);
			}
			else if (OperatingSystem.IsMacOS()) {
				// macOS: move to ~/.Trash/
				string trashDir = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					".Trash");
				Directory.CreateDirectory(trashDir);
				// Skip trash for cross-volume files to avoid cross-volume copy
				if (!VDF.Core.Utils.FileUtils.IsOnSameFileSystem(path, trashDir)) {
					File.Delete(path);
					return;
				}
				string dest = Path.Combine(trashDir, Path.GetFileName(path));
				int n = 1;
				while (File.Exists(dest))
					dest = Path.Combine(trashDir, $"{Path.GetFileNameWithoutExtension(path)}_{n++}{Path.GetExtension(path)}");
				File.Move(path, dest);
			}
			else {
				// Fallback: permanent delete with a warning
				Console.Error.WriteLine($"Warning: trash not supported on this OS. Permanently deleting: {path}");
				File.Delete(path);
			}
		}

		// Matches the JSON structure written by ResultFormatter
		private class DuplicateGroup {
			public Guid GroupId { get; set; }
			public List<DuplicateItem> Items { get; set; } = new();
		}
	}
}
