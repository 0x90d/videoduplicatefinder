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

using VDF.Core;
using VDF.Core.FFTools;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests;

/// <summary>
/// A rescan of files whose analysis is already complete must not touch them again. Each
/// visit used to stat the file and write the crash journal around a sampling call that had
/// nothing to do, which on a network share cost minutes for a large unchanged library.
/// </summary>
[Collection("Ffmpeg")]
public class CachedRescanTests {
	readonly FfmpegFixture _fixture;

	public CachedRescanTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableFact]
	public async Task Rescan_OfFullyAnalysedFiles_DoesNotRevisitThem() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(_fixture.H264_8bit == null, "H264 test video not generated");
		Skip.If(_fixture.H264_Different == null, "Different test video not generated");

		string scanDir = Directory.CreateTempSubdirectory("vdf-cached-rescan-scan").FullName;
		string dbDir = Directory.CreateTempSubdirectory("vdf-cached-rescan-db").FullName;
		using var guard = new FfmpegStaticStateGuard();
		try {
			File.Copy(_fixture.H264_8bit!, Path.Combine(scanDir, "a.mp4"));
			File.Copy(_fixture.H264_Different!, Path.Combine(scanDir, "b.mp4"));

			await ScanOnceAsync(scanDir, dbDir);
			foreach (string journal in Directory.GetFiles(dbDir, "scan-inflight-*"))
				File.Delete(journal);

			await ScanOnceAsync(scanDir, dbDir);

			Assert.Empty(Directory.GetFiles(dbDir, "scan-inflight-*"));
		}
		finally {
			VDF.Core.Utils.DatabaseUtils.CustomDatabaseFolder = null;
			VDF.Core.Utils.DatabaseUtils.InvalidateDatabaseFolder();
			try { Directory.Delete(scanDir, true); } catch { /* best effort */ }
			try { Directory.Delete(dbDir, true); } catch { /* best effort */ }
		}
	}

	static async Task ScanOnceAsync(string scanDir, string dbDir) {
		var engine = new ScanEngine();
		engine.Settings.CustomDatabaseFolder = dbDir;
		engine.Settings.IncludeList.Add(scanDir);
		engine.Settings.UseNativeFfmpegBinding = false;
		engine.Settings.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
		engine.Settings.EnablePartialClipDetection = false;
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		engine.BuildingHashesDone += (_, _) => done.TrySetResult();
		engine.ScanAborted += (_, _) => done.TrySetException(new InvalidOperationException("Scan aborted unexpectedly."));

		engine.StartSearch(searchAndCompare: false);
		var winner = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(120)));
		Assert.True(winner == done.Task, "Scan did not finish within 120 s.");
		await done.Task;
	}
}
