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

using System.Collections.Concurrent;
using VDF.Core;
using VDF.Core.FFTools;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests;

/// <summary>
/// End-to-end check of the per-drive progress feed: a real scan's progress events must
/// carry drive data during the analysis phase (with honest done/total accounting) and
/// must NOT carry it during the compare phase — stale drive rows freezing through the
/// compare phase was a shipped bug in the fork this feature is modeled on.
/// </summary>
[Collection("Ffmpeg")]
public class ScanProgressPerDriveTests {
	readonly FfmpegFixture _fixture;

	public ScanProgressPerDriveTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public async Task Scan_ReportsPerDriveProgress_OnlyDuringAnalysisPhase() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");
		Skip.If(_fixture.H264_Different == null, "Different test video not generated");

		string scanDir = Directory.CreateTempSubdirectory("vdf-drive-progress-scan").FullName;
		string dbDir = Directory.CreateTempSubdirectory("vdf-drive-progress-db").FullName;
		using var guard = new FfmpegStaticStateGuard();
		try {
			File.Copy(_fixture.H264_8bit!, Path.Combine(scanDir, "a.mp4"));
			File.Copy(_fixture.H264_Different!, Path.Combine(scanDir, "b.mp4"));

			var engine = new ScanEngine();
			engine.Settings.CustomDatabaseFolder = dbDir;
			engine.Settings.IncludeList.Add(scanDir);
			engine.Settings.UseNativeFfmpegBinding = false;
			engine.Settings.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
			engine.Settings.MaxDegreeOfParallelism = 4; // concurrent path: groups get classified

			var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
			engine.Progress += (_, e) => events.Enqueue(e);
			var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			engine.ScanDone += (_, _) => done.TrySetResult();
			engine.ScanAborted += (_, _) => done.TrySetException(new InvalidOperationException("Scan aborted unexpectedly."));

			engine.StartSearch(searchAndCompare: true);
			var winner = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(120)));
			Assert.True(winner == done.Task, "Scan did not finish within 120 s.");
			await done.Task;

			var all = events.ToArray();
			var driveEvents = all.Where(e => e.Drives != null).ToArray();
			Assert.True(driveEvents.Length > 0, "No progress event carried per-drive data during the analysis phase.");

			// The analysis phase's final push must show the scan drive fully done.
			DriveProgress[] final = driveEvents[^1].Drives!;
			DriveProgress scanDrive = Assert.Single(final);
			Assert.StartsWith(scanDrive.Root, scanDir, StringComparison.OrdinalIgnoreCase);
			Assert.Equal(2, scanDrive.TotalFiles);
			Assert.Equal(2, scanDrive.DoneFiles);
			Assert.True(scanDrive.TotalBytes > 0);
			Assert.Equal(scanDrive.TotalBytes, scanDrive.DoneBytes);
			Assert.NotNull(scanDrive.IsFastDrive); // concurrent path classifies every drive

			// Compare-phase events (the tail of the run) must carry no drive data.
			Assert.Null(all[^1].Drives);
		}
		finally {
			VDF.Core.Utils.DatabaseUtils.CustomDatabaseFolder = null;
			VDF.Core.Utils.DatabaseUtils.InvalidateDatabaseFolder();
			try { Directory.Delete(scanDir, true); } catch { /* best effort */ }
			try { Directory.Delete(dbDir, true); } catch { /* best effort */ }
		}
	}
}
