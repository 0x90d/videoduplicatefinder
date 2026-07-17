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

using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	/// <summary>
	/// Per-file disk deletion outcomes. Regression coverage for the forum report where
	/// a 2000-file "Delete (permanent)" removed every entry from the list while leaving
	/// all files on disk: deletions are now verified afterwards, and a file that is not
	/// found on disk is reported as MissingEntryOnly instead of silently passing as
	/// deleted.
	/// </summary>
	public class DiskDeletionTests {
		const string P = @"X:\videos\a.mp4";

		static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

		[Fact]
		public void MissingFile_IsEntryOnly_NotADeletion() {
			bool deleteCalled = false;
			var outcome = DiskDeletion.DeleteOne(P, permanently: true, batchedRecycle: false, batchRecycled: false,
				fileExists: _ => false, deleteFile: _ => deleteCalled = true, moveToTrash: _ => true);

			Assert.Equal(DiskDeletion.Outcome.MissingEntryOnly, outcome);
			Assert.False(deleteCalled);
		}

		[Fact]
		public void MissingFile_AfterBatchRecycle_CountsAsRecycled() {
			var outcome = DiskDeletion.DeleteOne(P, permanently: false, batchedRecycle: true, batchRecycled: true,
				fileExists: _ => false, deleteFile: _ => { }, moveToTrash: _ => true);

			Assert.Equal(DiskDeletion.Outcome.AlreadyRecycled, outcome);
		}

		[Fact]
		public void BatchRecycleLeftFileBehind_Throws() {
			Assert.True(Throws(() => DiskDeletion.DeleteOne(P, permanently: false, batchedRecycle: true, batchRecycled: true,
				fileExists: _ => true, deleteFile: _ => { }, moveToTrash: _ => true)));
		}

		[Fact]
		public void PermanentDelete_RemovesFile() {
			bool exists = true;
			var outcome = DiskDeletion.DeleteOne(P, permanently: true, batchedRecycle: false, batchRecycled: false,
				fileExists: _ => exists, deleteFile: _ => exists = false, moveToTrash: _ => throw new InvalidOperationException("trash must not be used for permanent deletes"));

			Assert.Equal(DiskDeletion.Outcome.Deleted, outcome);
			Assert.False(exists);
		}

		[Fact]
		public void PermanentDelete_ThatLeavesTheFileOnDisk_Throws() {
			// The delete call returns without error but the file is still there
			// (e.g. odd filesystem semantics). This must surface as a failure, not
			// count as freed space.
			bool deleteCalled = false;
			Assert.True(Throws(() => DiskDeletion.DeleteOne(P, permanently: true, batchedRecycle: false, batchRecycled: false,
				fileExists: _ => true, deleteFile: _ => deleteCalled = true, moveToTrash: _ => true)));
			Assert.True(deleteCalled);
		}

		[Fact]
		public void TrashDelete_UsesTrash_WithoutPermanentFallback() {
			bool exists = true, deleteCalled = false;
			var outcome = DiskDeletion.DeleteOne(P, permanently: false, batchedRecycle: false, batchRecycled: false,
				fileExists: _ => exists, deleteFile: _ => deleteCalled = true, moveToTrash: _ => { exists = false; return true; });

			Assert.Equal(DiskDeletion.Outcome.Deleted, outcome);
			Assert.False(deleteCalled);
		}

		[Fact]
		public void TrashUnavailable_FallsBackToPermanentDelete() {
			bool exists = true;
			var outcome = DiskDeletion.DeleteOne(P, permanently: false, batchedRecycle: false, batchRecycled: false,
				fileExists: _ => exists, deleteFile: _ => exists = false, moveToTrash: _ => false);

			Assert.Equal(DiskDeletion.Outcome.Deleted, outcome);
			Assert.False(exists);
		}

		[Fact]
		public void TrashReportsSuccess_ButFileRemains_Throws() {
			Assert.True(Throws(() => DiskDeletion.DeleteOne(P, permanently: false, batchedRecycle: false, batchRecycled: false,
				fileExists: _ => true, deleteFile: _ => { }, moveToTrash: _ => true)));
		}

		// ── RunChunked ──────────────────────────────────────────────────────
		// Regression (#849 report): recycling the whole selection in ONE shell call kept
		// the "Deleting files... 0/N" overlay frozen for the entire operation, because
		// the counting loop only ran after the shell returned. The loop must recycle and
		// process chunk by chunk so progress can advance in between.

		[Fact]
		public void RunChunked_InterleavesRecycleAndProcessing_PerChunk() {
			var items = Enumerable.Range(0, 120).Select(i => $"f{i}").ToList();
			var log = new List<string>();
			DiskDeletion.RunChunked(items, chunkSize: 50, batchedRecycle: true,
				pathOf: s => s, fileExists: _ => true,
				recycleBatch: paths => log.Add($"recycle:{paths.Count}"),
				processOne: (s, _) => log.Add($"process:{s}"));

			// recycle(50), 50x process, recycle(50), 50x process, recycle(20), 20x process
			Assert.Equal("recycle:50", log[0]);
			Assert.Equal("recycle:50", log[51]);
			Assert.Equal("recycle:20", log[102]);
			Assert.Equal(items.Count + 3, log.Count);
			// Every item processed exactly once, in original order.
			Assert.Equal(items, log.Where(l => l.StartsWith("process:")).Select(l => l["process:".Length..]));
		}

		[Fact]
		public void RunChunked_RecyclesOnlyExistingFiles_AndFlagsThem() {
			var items = new List<string> { "gone1", "there1", "gone2", "there2" };
			var recycledPaths = new List<string>();
			var flagged = new Dictionary<string, bool>();
			DiskDeletion.RunChunked(items, chunkSize: 10, batchedRecycle: true,
				pathOf: s => s, fileExists: p => p.StartsWith("there"),
				recycleBatch: recycledPaths.AddRange,
				processOne: (s, wasRecycled) => flagged[s] = wasRecycled);

			Assert.Equal(new[] { "there1", "there2" }, recycledPaths);
			Assert.True(flagged["there1"]);
			Assert.True(flagged["there2"]);
			Assert.False(flagged["gone1"]);
			Assert.False(flagged["gone2"]);
		}

		[Fact]
		public void RunChunked_WithoutBatchedRecycle_NeverTouchesShellOrFileSystem() {
			var items = new List<string> { "a", "b", "c" };
			var processed = new List<string>();
			DiskDeletion.RunChunked(items, chunkSize: 2, batchedRecycle: false,
				pathOf: s => s,
				fileExists: _ => throw new InvalidOperationException("existence must not be probed when batching is off"),
				recycleBatch: _ => throw new InvalidOperationException("shell must not be invoked when batching is off"),
				processOne: (s, wasRecycled) => { Assert.False(wasRecycled); processed.Add(s); });

			Assert.Equal(items, processed);
		}

		[Fact]
		public void RunChunked_AllFilesMissing_SkipsShellCallButStillProcesses() {
			var items = new List<string> { "gone1", "gone2" };
			var processed = new List<string>();
			DiskDeletion.RunChunked(items, chunkSize: 10, batchedRecycle: true,
				pathOf: s => s, fileExists: _ => false,
				recycleBatch: _ => throw new InvalidOperationException("empty chunks must not reach the shell"),
				processOne: (s, wasRecycled) => { Assert.False(wasRecycled); processed.Add(s); });

			Assert.Equal(items, processed);
		}
	}
}
