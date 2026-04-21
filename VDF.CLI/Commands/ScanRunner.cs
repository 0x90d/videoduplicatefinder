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

using System.Text.Json;
using VDF.Core;
using VDF.Core.ViewModels;

namespace VDF.CLI.Commands {
	/// <summary>Drives ScanEngine operations and bridges the async-void/event API to awaitable Tasks.</summary>
	internal static class ScanRunner {
		/// <summary>Runs StartSearch() then StartCompare() (the full pipeline).</summary>
		internal static async Task<HashSet<DuplicateItem>> RunScanAndCompareAsync(ScanEngine engine, CancellationToken ct) {
			await RunSearchAsync(engine, ct);
			return await RunCompareAsync(engine, ct);
		}

		/// <summary>Runs StartSearch() only (enumerate files and build hashes).</summary>
		internal static async Task RunSearchAsync(ScanEngine engine, CancellationToken ct) {
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			engine.BuildingHashesDone += OnDone;
			engine.ScanAborted += OnAborted;
			ct.Register(() => { engine.Stop(); tcs.TrySetCanceled(); });

			engine.StartSearch();
			await tcs.Task;

			engine.BuildingHashesDone -= OnDone;
			engine.ScanAborted -= OnAborted;

			void OnDone(object? s, EventArgs e) => tcs.TrySetResult();
			void OnAborted(object? s, EventArgs e) => tcs.TrySetCanceled();
		}

		/// <summary>Runs StartCompare() only (assumes database already populated by a prior scan).</summary>
		internal static async Task<HashSet<DuplicateItem>> RunCompareAsync(ScanEngine engine, CancellationToken ct) {
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			engine.ScanDone += OnDone;
			engine.ScanAborted += OnAborted;
			ct.Register(() => { engine.Stop(); tcs.TrySetCanceled(); });

			engine.StartCompare();
			await tcs.Task;

			engine.ScanDone -= OnDone;
			engine.ScanAborted -= OnAborted;

			return engine.Duplicates;

			void OnDone(object? s, EventArgs e) => tcs.TrySetResult();
			void OnAborted(object? s, EventArgs e) => tcs.TrySetCanceled();
		}

		internal static void WireProgress(ScanEngine engine) {
			engine.FilesEnumerated += (_, _) =>
				Console.Error.WriteLine("[scan] File enumeration complete.");

			engine.BuildingHashesDone += (_, _) =>
				Console.Error.WriteLine("[scan] Hashing complete.");

			engine.Progress += (_, e) => {
				int pct = e.MaxPosition > 0 ? (int)(100L * e.CurrentPosition / e.MaxPosition) : 0;
				string eta = e.Remaining == TimeSpan.Zero ? "..." : e.Remaining.ToString(@"m\mss\s");
				string stage = string.IsNullOrEmpty(e.CurrentStage)
					? string.Empty
					: e.StageMax > 0 ? $"  ({e.CurrentStage} {e.StageCurrent}/{e.StageMax})" : $"  ({e.CurrentStage})";
				Console.Error.Write($"\r[{pct,3}%] {e.CurrentPosition}/{e.MaxPosition}  ETA {eta}  {TruncatePath(e.CurrentFile, 60)}{stage}    ");
			};

			engine.ScanDone += (_, _) => {
				Console.Error.WriteLine();
				Console.Error.WriteLine("[scan] Comparison complete.");
			};

			engine.ScanAborted += (_, _) => {
				Console.Error.WriteLine();
				Console.Error.WriteLine("[scan] Aborted.");
			};
		}

		internal static Settings LoadOrCreateSettings(FileInfo? settingsFile) {
			if (settingsFile == null || !settingsFile.Exists)
				return new Settings();

			try {
				var json = File.ReadAllText(settingsFile.FullName);
				return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
			}
			catch (Exception ex) {
				Console.Error.WriteLine($"Warning: could not load settings file '{settingsFile.FullName}': {ex.Message}");
				return new Settings();
			}
		}

		static string TruncatePath(string path, int maxLen) {
			if (path.Length <= maxLen) return path;
			return "..." + path[^(maxLen - 3)..];
		}
	}
}
