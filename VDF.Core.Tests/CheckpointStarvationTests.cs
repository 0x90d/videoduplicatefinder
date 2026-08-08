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

using VDF.Core.Utils;

namespace VDF.Core.Tests;

/// <summary>
/// Regression tests for #868: the periodic database checkpoint only lived in the
/// file-counter path, whose push shared a throttle timestamp with mid-file stage
/// reports. Stage reports arrive constantly during analysis, so the counter push
/// was throttled away and checkpoints could starve for an entire scan - a crash
/// then lost hours of progress. Any pushed progress report must attempt a
/// checkpoint (PR #883).
/// </summary>
[Collection("DatabaseUtils")] // TryDatabaseCheckpoint saves the static DatabaseUtils state
public class CheckpointStarvationTests {

	[Fact]
	public void StageReports_DoNotStarveDatabaseCheckpoints() {
		string dbDir = Directory.CreateTempSubdirectory("vdf-checkpoint-starvation").FullName;
		try {
			DatabaseUtils.CustomDatabaseFolder = dbDir;
			DatabaseUtils.InvalidateDatabaseFolder();

			var engine = new ScanEngine();
			engine.Settings.DatabaseCheckpointIntervalMinutes = 1;
			engine.InitProgress(1000, "");
			// The checkpoint interval has long elapsed by the time the reports below arrive.
			engine.lastCheckpointTime = DateTime.UtcNow - TimeSpan.FromMinutes(10);

			// The cadence that starved checkpoints: a mid-file stage report refreshes the
			// shared throttle, so the file-counter push right after it is throttled away.
			engine.ReportProgress("stalling.mp4", "sampling", 2, 5);
			engine.IncrementProgress("stalling.mp4");

			Assert.True(File.Exists(Path.Combine(dbDir, "ScannedFiles.db")),
				"No database checkpoint was written - stage reports starved TryDatabaseCheckpoint again (#868).");
		}
		finally {
			DatabaseUtils.CustomDatabaseFolder = null;
			DatabaseUtils.InvalidateDatabaseFolder();
			try { Directory.Delete(dbDir, true); } catch { /* best effort */ }
		}
	}
}
