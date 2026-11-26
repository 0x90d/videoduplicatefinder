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
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;
using VDF.TestSupport;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// The audio fingerprint reads a whole file, which off a network share is most of a scan
/// with new files. With extended FFmpeg logging on, each fingerprint's timing must reach
/// the log; the scan passed a hard-coded false, so that stretch of a scan logged nothing.
/// </summary>
[Collection("Ffmpeg")]
public class AudioFingerprintLoggingTests {
	readonly FfmpegFixture _fixture;

	public AudioFingerprintLoggingTests(FfmpegFixture fixture) => _fixture = fixture;

	[SkippableTheory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Scan_LogsFingerprintTiming_OnlyWithExtendedLogging(bool extendedLogging) {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);

		string scanDir = Directory.CreateTempSubdirectory("vdf-fp-logging-scan").FullName;
		string dbDir = Directory.CreateTempSubdirectory("vdf-fp-logging-db").FullName;
		string fileName = $"fp-logging-{Guid.NewGuid():N}.mp4";
		var messages = new List<string>();
		void OnEntry(LogEntry entry) { lock (messages) messages.Add(entry.Message); }
		using var guard = new FfmpegStaticStateGuard();
		Logger.Instance.LogEntryAdded += OnEntry;
		try {
			Skip.If(!TestVideoGenerator.GenerateH264WithAudio(FfmpegEngine.FFmpegPath, Path.Combine(scanDir, fileName)),
				"Could not generate a test video with audio");

			var engine = new ScanEngine();
			engine.Settings.CustomDatabaseFolder = dbDir;
			engine.Settings.IncludeList.Add(scanDir);
			engine.Settings.UseNativeFfmpegBinding = false;
			engine.Settings.HardwareAccelerationMode = FFHardwareAccelerationMode.none;
			engine.Settings.EnablePartialClipDetection = true;
			engine.Settings.ExtendedFFToolsLogging = extendedLogging;

			var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			engine.ScanDone += (_, _) => done.TrySetResult();
			engine.ScanAborted += (_, _) => done.TrySetException(new InvalidOperationException("Scan aborted unexpectedly."));

			engine.StartSearch(searchAndCompare: true);
			var winner = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(120)));
			Assert.True(winner == done.Task, "Scan did not finish within 120 s.");
			await done.Task;

			bool logged;
			lock (messages)
				logged = messages.Any(m => m.Contains($"[ChromaprintEngine] {fileName}:", StringComparison.Ordinal) &&
					m.Contains("total=", StringComparison.Ordinal));
			Assert.Equal(extendedLogging, logged);
		}
		finally {
			Logger.Instance.LogEntryAdded -= OnEntry;
			// The scan leaves its files in the shared static database, where a later scan test would find them.
			DatabaseUtils.Database.RemoveWhere(e => e.Path.StartsWith(scanDir, StringComparison.Ordinal));
			DatabaseUtils.CustomDatabaseFolder = null;
			DatabaseUtils.InvalidateDatabaseFolder();
			try { Directory.Delete(scanDir, true); } catch { /* best effort */ }
			try { Directory.Delete(dbDir, true); } catch { /* best effort */ }
		}
	}
}
