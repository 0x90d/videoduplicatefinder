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

namespace VDF.Core.Tests;

/// <summary>
/// Regression tests for #821: a scan that dies on an exception must end in
/// ScanAborted instead of leaving the engine (and every frontend watching it)
/// stuck in a scanning state that Stop can no longer get out of.
/// </summary>
[Collection("DatabaseUtils")] // StartSearch touches the static DatabaseUtils — serialize with other classes touching it
public class ScanEngineAbortTests {

	/// <summary>
	/// StartSearch is async void, so a caller can never catch what it throws. Whatever
	/// goes wrong — here either PrepareSearch failing (no FFmpeg on the test machine)
	/// or an event handler blowing up (the injected throw below) — the engine itself
	/// must terminate the scan and raise ScanAborted.
	/// </summary>
	[Fact]
	public async Task StartSearch_OnUnexpectedError_RaisesScanAborted() {
		string dbDir = Directory.CreateTempSubdirectory("vdf-abort-test").FullName;
		try {
			var engine = new ScanEngine();
			engine.Settings.CustomDatabaseFolder = dbDir;
			engine.Settings.IncludeList.Add(dbDir); // empty folder — nothing gets hashed
			engine.FilesEnumerated += (_, _) => throw new InvalidOperationException("injected scan failure");

			var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			engine.ScanAborted += (_, _) => aborted.TrySetResult();

			engine.StartSearch(searchAndCompare: false);

			var winner = await Task.WhenAny(aborted.Task, Task.Delay(TimeSpan.FromSeconds(30)));
			Assert.True(winner == aborted.Task, "ScanAborted was not raised — the failed scan would hang the UI forever.");
		}
		finally {
			VDF.Core.Utils.DatabaseUtils.CustomDatabaseFolder = null;
			VDF.Core.Utils.DatabaseUtils.InvalidateDatabaseFolder();
			try { Directory.Delete(dbDir, true); } catch { /* best effort */ }
		}
	}

	/// <summary>
	/// Stop while no scan task is alive (e.g. it already died on an error) has no
	/// cancellation to observe, so it must raise ScanAborted itself — otherwise a
	/// frontend that still believes a scan is running waits on its busy overlay forever.
	/// </summary>
	[Fact]
	public void Stop_WhenNoScanIsRunning_RaisesScanAborted() {
		var engine = new ScanEngine();
		bool aborted = false;
		engine.ScanAborted += (_, _) => aborted = true;

		engine.Stop();

		Assert.True(aborted);
	}
}
