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

using VDF.GUI.Utils;

namespace VDF.GUI.Tests;

/// <summary>
/// What of a scan and of the busy curtain is said to a screen reader. The screen repaints
/// several times a second; the rules here are what keeps that from being read out.
/// </summary>
public class ScreenReaderAnnouncementsTests {

	static readonly DateTime T0 = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

	sealed class Scan {
		readonly ScanProgressAnnouncer announcer = new();
		public DateTime Now = T0;
		public readonly List<string> Said = new();

		public string? At(double seconds, string stage, long position, long max, string remaining = "5m, 3s") {
			Now = T0.AddSeconds(seconds);
			string? text = announcer.Next(stage, position, max, remaining, Now, "Scanning", "{0} percent, about {1} left");
			if (text != null) Said.Add(text);
			return text;
		}

		public void Reset() => announcer.Reset();
	}

	[Fact]
	public void Scan_FileAnalysis_IsAnnouncedOnceUnderItsOwnName() {
		var scan = new Scan();

		// The analysis phase opens with an empty label; after that the label is whatever a
		// worker did last and flips with every snapshot.
		Assert.Equal("Scanning", scan.At(0, "", 0, 1000));
		scan.At(1, "probing metadata", 3, 1000);
		scan.At(7, "sampling frames", 20, 1000);
		scan.At(14, "audio fingerprint", 41, 1000);
		scan.At(21, "sampling frames", 60, 1000);

		Assert.Equal(["Scanning"], scan.Said);
	}

	[Fact]
	public void Scan_Progress_IsSaidInStepsOfTenPercent_WithTheRemainingTime() {
		var scan = new Scan();
		scan.At(0, "", 0, 1000);

		Assert.Null(scan.At(30, "sampling frames", 99, 1000));
		Assert.Equal("10 percent, about 4m, 30s left", scan.At(31, "sampling frames", 104, 1000, "4m, 30s"));
		Assert.Null(scan.At(60, "sampling frames", 190, 1000)); // same ten
		Assert.Equal("23 percent, about 4m left", scan.At(61, "probing metadata", 230, 1000, "4m"));
	}

	[Fact]
	public void Scan_Progress_OnAFastScan_IsSpacedOut() {
		var scan = new Scan();
		scan.At(0, "", 0, 100);

		// Ten percent a second would be ten sentences queued up behind each other.
		for (int second = 1; second <= 9; second++)
			scan.At(second, "sampling frames", second * 10, 100);
		Assert.Equal(["Scanning"], scan.Said);

		Assert.Equal("95 percent, about 1s left", scan.At(ScanProgressAnnouncer.ProgressGap.TotalSeconds + 1, "sampling frames", 95, 100, "1s"));
	}

	[Fact]
	public void Scan_CompleteOrEmpty_SaysNoPercentage() {
		var scan = new Scan();
		scan.At(0, "", 0, 1000);

		Assert.Null(scan.At(100, "sampling frames", 1000, 1000)); // "scan complete" follows anyway
		Assert.Null(scan.At(200, "sampling frames", 5000, 1000));

		var empty = new Scan();
		Assert.Equal("comparing duplicates", empty.At(0, "comparing duplicates", 0, 0));
		Assert.Null(empty.At(60, "comparing duplicates", 0, 0));
	}

	[Fact]
	public void Scan_EveryPhase_IsAnnouncedByItsLabel_AndStartsItsPercentagesOver() {
		var scan = new Scan();
		scan.At(0, "", 0, 1000);
		scan.At(40, "sampling frames", 900, 1000);

		// A new phase shows in the counters starting over, and carries a label of its own.
		Assert.Equal("comparing duplicates", scan.At(60, "comparing duplicates", 0, 400));
		Assert.Equal("50 percent, about 5m, 3s left", scan.At(90, "comparing duplicates", 200, 400));

		Assert.Equal(["Scanning", "90 percent, about 5m, 3s left", "comparing duplicates", "50 percent, about 5m, 3s left"], scan.Said);
	}

	[Fact]
	public void Scan_PhasesOfTheSameLength_AreToldApartByTheirLabel() {
		var scan = new Scan();
		scan.At(0, "computing AI embeddings", 0, 1);

		// The single-step AI phases: same maximum, position still 0.
		Assert.Equal("AI partial: preparing", scan.At(10, "AI partial: preparing", 0, 1));
	}

	[Fact]
	public void Scan_PhasesThatFollowWithinASecond_DoNotTalkOverEachOther() {
		var scan = new Scan();
		scan.At(0, "AI partial: preparing", 0, 1);

		Assert.Null(scan.At(0.4, "AI partial: sampling keyframes", 0, 50));
		Assert.Null(scan.At(0.8, "AI partial: saving keyframe cache", 0, 1));
		// The one that is current once there is room to speak, not a backlog of all of them.
		Assert.Equal("AI partial: saving keyframe cache", scan.At(ScanProgressAnnouncer.StageGap.TotalSeconds + 0.1, "AI partial: saving keyframe cache", 0, 1));

		Assert.Equal(["AI partial: preparing", "AI partial: saving keyframe cache"], scan.Said);
	}

	[Fact]
	public void Scan_Reset_MakesTheNextScanSpeakFromTheStart() {
		var scan = new Scan();
		scan.At(0, "", 0, 1000);
		scan.At(30, "sampling frames", 500, 1000);

		scan.Reset();

		// Same library, same maximum: without the reset nothing would mark the new scan.
		Assert.Equal("Scanning", scan.At(3600, "", 0, 1000));
	}

	[Fact]
	public void Busy_TextIsSaidWhenTheCurtainComesUp_ThenOnlyEverySoOften() {
		var busy = new BusyAnnouncer();

		Assert.Equal("Deleting files... 0/200", busy.Next(true, "Deleting files... 0/200", T0));
		Assert.Null(busy.Next(true, "Deleting files... 1/200", T0.AddSeconds(0.1)));
		Assert.Null(busy.Next(true, "Deleting files... 90/200", T0.AddSeconds(9)));
		Assert.Equal("Deleting files... 101/200", busy.Next(true, "Deleting files... 101/200", T0 + BusyAnnouncer.Gap));
	}

	[Fact]
	public void Busy_EveryCurtain_SpeaksAtOnce() {
		var busy = new BusyAnnouncer();
		busy.Next(true, "Saving scan results to disk...", T0);
		Assert.Null(busy.Next(false, "Saving scan results to disk...", T0.AddSeconds(1)));

		Assert.Equal("Cleaning database...", busy.Next(true, "Cleaning database...", T0.AddSeconds(2)));
	}

	[Fact]
	public void Busy_WithoutText_WaitsForTheFirstOne() {
		var busy = new BusyAnnouncer();

		Assert.Null(busy.Next(true, "", T0));
		Assert.Equal("Loading database...", busy.Next(true, "Loading database...", T0.AddSeconds(0.2)));
	}

	[Fact]
	public void Busy_NoCurtain_SaysNothing() {
		var busy = new BusyAnnouncer();

		// While a scan runs the curtain stays down and the scanning view speaks for itself.
		Assert.Null(busy.Next(false, "Stopping all scan threads...", T0));
	}
}
