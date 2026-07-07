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
using VDF.GUI.Data;

namespace VDF.GUI.Tests;

// The Scanning state's per-drive rows: engine snapshots map onto stable row VMs with a
// byte-weighted fill and a smoothed files/s rate. The clear-on-null rule is load-bearing —
// the fork this feature is modeled on shipped a bug where stale drive rows froze on screen
// through the whole compare phase.
public class ScanDrivesPresenterTests {

	static readonly DateTime T0 = new(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc);

	static ScanDrivesPresenter NewPresenter() => new(() => "files/s");

	static DriveProgress Drive(string root, long totalBytes, long doneBytes, int totalFiles, int doneFiles, bool? isFast = true) =>
		new() { Root = root, TotalBytes = totalBytes, DoneBytes = doneBytes, TotalFiles = totalFiles, DoneFiles = doneFiles, IsFastDrive = isFast };

	[Fact]
	public void FirstUpdate_BuildsRowsWithTypeChipAndNoRate() {
		var presenter = NewPresenter();
		presenter.Update(new[] {
			Drive(@"D:\", 1000, 620, 380, 214, isFast: true),
			Drive(@"E:\", 1000, 140, 439, 63, isFast: false),
		}, T0);

		Assert.True(presenter.HasRows);
		Assert.Equal(2, presenter.Rows.Count);
		Assert.Equal(@"D:\", presenter.Rows[0].Root);
		Assert.Equal("SSD", presenter.Rows[0].TypeLabel);
		Assert.Equal("HDD", presenter.Rows[1].TypeLabel);
		Assert.Equal(0.62, presenter.Rows[0].Fraction, 3);
		// No second sample yet — the stat must not show a made-up rate.
		Assert.Equal("214 / 380", presenter.Rows[0].Stat);
	}

	[Fact]
	public void UnclassifiedDrive_HidesTheTypeChip() {
		var presenter = NewPresenter();
		presenter.Update(new[] { Drive(@"C:\", 10, 0, 5, 0, isFast: null) }, T0);
		Assert.Equal(string.Empty, presenter.Rows[0].TypeLabel);
		Assert.False(presenter.Rows[0].HasType);
	}

	[Fact]
	public void RateAppearsAfterASecondAndIsSmoothed() {
		var presenter = NewPresenter();
		presenter.Update(new[] { Drive(@"D:\", 1000, 0, 380, 0) }, T0);
		presenter.Update(new[] { Drive(@"D:\", 1000, 100, 380, 41) }, T0.AddSeconds(1));
		Assert.Equal("41 / 380 · 41 files/s", presenter.Rows[0].Stat);

		// Next window measures 21 files/s; EMA(0.5) over 41 → 31.
		presenter.Update(new[] { Drive(@"D:\", 1000, 200, 380, 62) }, T0.AddSeconds(2));
		Assert.Equal("62 / 380 · 31 files/s", presenter.Rows[0].Stat);
	}

	[Fact]
	public void SubSecondUpdates_KeepThePreviousRateInsteadOfMeasuringNoise() {
		var presenter = NewPresenter();
		presenter.Update(new[] { Drive(@"D:\", 1000, 0, 380, 0) }, T0);
		presenter.Update(new[] { Drive(@"D:\", 1000, 100, 380, 40) }, T0.AddSeconds(1));
		presenter.Update(new[] { Drive(@"D:\", 1000, 110, 380, 45) }, T0.AddSeconds(1.3));
		Assert.Equal("45 / 380 · 40 files/s", presenter.Rows[0].Stat);
	}

	[Fact]
	public void SameDrives_UpdateRowsInPlace() {
		var presenter = NewPresenter();
		presenter.Update(new[] { Drive(@"D:\", 1000, 100, 380, 40) }, T0);
		ScanDriveRow row = presenter.Rows[0];
		presenter.Update(new[] { Drive(@"D:\", 1000, 500, 380, 200) }, T0.AddSeconds(0.3));
		Assert.Same(row, presenter.Rows[0]);
		Assert.Equal(0.5, row.Fraction, 3);
	}

	[Fact]
	public void NullSnapshot_ClearsRows() {
		// Compare-phase events carry no drive data; stale gather-phase rows must vanish.
		var presenter = NewPresenter();
		presenter.Update(new[] { Drive(@"D:\", 1000, 100, 380, 40) }, T0);
		Assert.True(presenter.HasRows);
		presenter.Update(null, T0.AddSeconds(1));
		Assert.False(presenter.HasRows);
		Assert.Empty(presenter.Rows);
	}

	[Fact]
	public void ChangedDriveSet_RebuildsRows() {
		var presenter = NewPresenter();
		presenter.Update(new[] { Drive(@"D:\", 1000, 100, 380, 40) }, T0);
		presenter.Update(new[] {
			Drive(@"D:\", 1000, 100, 380, 40),
			Drive(@"E:\", 500, 0, 10, 0, isFast: false),
		}, T0.AddSeconds(0.3));
		Assert.Equal(2, presenter.Rows.Count);
		Assert.Equal(@"E:\", presenter.Rows[1].Root);
	}

	[Fact]
	public void ZeroByteTotals_FallBackToFileFraction() {
		var presenter = NewPresenter();
		presenter.Update(new[] { Drive(@"D:\", 0, 0, 10, 5) }, T0);
		Assert.Equal(0.5, presenter.Rows[0].Fraction, 3);
	}
}
