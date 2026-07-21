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

namespace VDF.Core.Tests.Utils;

// ScanCrashJournal holds static state (the journal folder) that PrepareSearch also
// writes — serialize with the other classes touching scan-engine statics.
[Collection("DatabaseUtils")]
public class ScanCrashJournalTests : IDisposable {
	readonly string tempDir;

	public ScanCrashJournalTests() {
		tempDir = Path.Combine(Path.GetTempPath(), "vdf-journal-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDir);
		ScanCrashJournal.Initialize(tempDir);
	}

	public void Dispose() {
		ScanCrashJournal.Initialize(null);
		try {
			Directory.Delete(tempDir, true);
		}
		catch { }
	}

	string[] BreadcrumbFiles() => Directory.GetFiles(tempDir, "scan-inflight-*.txt");

	[Fact]
	public void Begin_WritesBreadcrumb_End_BlanksIt() {
		ScanCrashJournal.Begin(ScanCrashJournal.PhaseAudio, @"C:\videos\a.mp4");

		string[] files = BreadcrumbFiles();
		Assert.Single(files);
		Assert.Equal(@"audio|C:\videos\a.mp4", File.ReadAllText(files[0]));

		ScanCrashJournal.End();
		Assert.Equal(string.Empty, File.ReadAllText(files[0]));
	}

	[Fact]
	public void CollectLeftovers_ReturnsSuspects_AndRemovesAllBreadcrumbs() {
		// Simulate the state a crash leaves behind: two threads mid-file, one thread idle.
		File.WriteAllText(Path.Combine(tempDir, "scan-inflight-11.txt"),
			ScanCrashJournal.FormatLine(ScanCrashJournal.PhaseSampling, @"C:\videos\poison.mp4"));
		File.WriteAllText(Path.Combine(tempDir, "scan-inflight-12.txt"),
			ScanCrashJournal.FormatLine(ScanCrashJournal.PhaseAudio, @"C:\videos\other.mp4"));
		File.WriteAllText(Path.Combine(tempDir, "scan-inflight-13.txt"), string.Empty);

		List<ScanCrashJournal.Suspect> suspects = ScanCrashJournal.CollectLeftovers();

		Assert.Equal(2, suspects.Count);
		Assert.Contains(new ScanCrashJournal.Suspect(ScanCrashJournal.PhaseSampling, @"C:\videos\poison.mp4"), suspects);
		Assert.Contains(new ScanCrashJournal.Suspect(ScanCrashJournal.PhaseAudio, @"C:\videos\other.mp4"), suspects);
		Assert.Empty(BreadcrumbFiles());
	}

	[Fact]
	public void CompletedScan_LeavesNoSuspects() {
		ScanCrashJournal.Begin(ScanCrashJournal.PhaseSampling, @"C:\videos\a.mp4");
		ScanCrashJournal.End();

		Assert.Empty(ScanCrashJournal.CollectLeftovers());
	}

	[Fact]
	public void Uninitialized_BeginIsNoOp() {
		ScanCrashJournal.Initialize(null);
		ScanCrashJournal.Begin(ScanCrashJournal.PhaseSampling, @"C:\videos\a.mp4");
		ScanCrashJournal.Initialize(tempDir);

		Assert.Empty(BreadcrumbFiles());
	}

	[Theory]
	[InlineData("sampling|C:\\videos\\a.mp4", true, "sampling", "C:\\videos\\a.mp4")]
	[InlineData("audio|/mnt/media/pipe|name.mp4", true, "audio", "/mnt/media/pipe|name.mp4")] // split at FIRST pipe only
	[InlineData("no-separator", false, "", "")]
	[InlineData("|path-only", false, "", "")]
	[InlineData("phase-only|", false, "", "")]
	public void TryParseLine_Cases(string line, bool expectedOk, string expectedPhase, string expectedPath) {
		bool ok = ScanCrashJournal.TryParseLine(line, out string phase, out string path);
		Assert.Equal(expectedOk, ok);
		if (expectedOk) {
			Assert.Equal(expectedPhase, phase);
			Assert.Equal(expectedPath, path);
		}
	}
}
