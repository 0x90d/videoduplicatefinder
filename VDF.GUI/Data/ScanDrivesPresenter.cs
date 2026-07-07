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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReactiveUI;
using VDF.Core;

namespace VDF.GUI.Data {

	/// <summary>One row of the Scanning state's per-drive panel (mockup .driverow).</summary>
	public sealed class ScanDriveRow : ReactiveObject {
		internal ScanDriveRow(string root, string typeLabel) {
			Root = root;
			TypeLabel = typeLabel;
		}
		public string Root { get; }
		/// <summary>"SSD" / "HDD" chip text; empty when the drive was never classified (serial scan).</summary>
		public string TypeLabel { get; }
		public bool HasType => TypeLabel.Length > 0;

		double fraction;
		/// <summary>Bar fill 0–1. Byte-weighted — a drive with one huge file left is honestly "not nearly done".</summary>
		public double Fraction {
			get => fraction;
			internal set => this.RaiseAndSetIfChanged(ref fraction, value);
		}

		string stat = string.Empty;
		/// <summary>Right-aligned stat: "214 / 380 · 41 files/s" (rate appears once measurable).</summary>
		public string Stat {
			get => stat;
			internal set => this.RaiseAndSetIfChanged(ref stat, value);
		}
	}

	/// <summary>
	/// Maps the engine's per-drive progress snapshots onto stable row view-models and
	/// derives a smoothed files/s rate per drive. Pure logic — the caller is responsible
	/// for invoking <see cref="Update"/> on the UI thread. A null/empty snapshot clears
	/// the rows: the engine only attaches drive data during the analysis phase, and stale
	/// rows lingering through the compare phase was a known bug in the fork this feature
	/// is modeled on.
	/// </summary>
	public sealed class ScanDrivesPresenter : ReactiveObject {
		// Below this, two consecutive progress events measure noise, not throughput.
		static readonly TimeSpan RateSampleInterval = TimeSpan.FromSeconds(1);

		sealed class RateState {
			public DateTime SampleTime;
			public int SampleFiles;
			public double? Rate;
		}

		readonly Func<string> filesPerSecondUnit;
		readonly Dictionary<string, RateState> rates = new(StringComparer.OrdinalIgnoreCase);

		public ScanDrivesPresenter(Func<string> filesPerSecondUnit) => this.filesPerSecondUnit = filesPerSecondUnit;

		public ObservableCollection<ScanDriveRow> Rows { get; } = new();

		bool hasRows;
		public bool HasRows {
			get => hasRows;
			private set => this.RaiseAndSetIfChanged(ref hasRows, value);
		}

		public void Update(DriveProgress[]? drives, DateTime utcNow) {
			if (drives == null || drives.Length == 0) {
				Clear();
				return;
			}
			if (!RowsMatch(drives)) {
				Rows.Clear();
				foreach (DriveProgress drive in drives)
					Rows.Add(new ScanDriveRow(drive.Root, TypeLabelFor(drive.IsFastDrive)));
			}
			for (int i = 0; i < drives.Length; i++) {
				DriveProgress drive = drives[i];
				ScanDriveRow row = Rows[i];
				row.Fraction = FractionFor(drive);
				double? rate = UpdateRate(drive, utcNow);
				row.Stat = rate == null
					? $"{drive.DoneFiles:N0} / {drive.TotalFiles:N0}"
					: $"{drive.DoneFiles:N0} / {drive.TotalFiles:N0} · {Math.Round(rate.Value):N0} {filesPerSecondUnit()}";
			}
			HasRows = true;
		}

		public void Clear() {
			Rows.Clear();
			rates.Clear();
			HasRows = false;
		}

		static string TypeLabelFor(bool? isFast) => isFast switch {
			true => "SSD",
			false => "HDD",
			null => string.Empty,
		};

		static double FractionFor(DriveProgress drive) {
			double fraction = drive.TotalBytes > 0 ? (double)drive.DoneBytes / drive.TotalBytes
							: drive.TotalFiles > 0 ? (double)drive.DoneFiles / drive.TotalFiles
							: 0d;
			return Math.Clamp(fraction, 0d, 1d);
		}

		bool RowsMatch(DriveProgress[] drives) {
			if (Rows.Count != drives.Length)
				return false;
			for (int i = 0; i < drives.Length; i++) {
				if (!string.Equals(Rows[i].Root, drives[i].Root, StringComparison.OrdinalIgnoreCase) ||
					Rows[i].TypeLabel != TypeLabelFor(drives[i].IsFastDrive))
					return false;
			}
			return true;
		}

		double? UpdateRate(DriveProgress drive, DateTime utcNow) {
			if (!rates.TryGetValue(drive.Root, out RateState? state)) {
				rates[drive.Root] = new RateState { SampleTime = utcNow, SampleFiles = drive.DoneFiles };
				return null;
			}
			double elapsedSeconds = (utcNow - state.SampleTime).TotalSeconds;
			if (elapsedSeconds >= RateSampleInterval.TotalSeconds) {
				double instantaneous = Math.Max(0, drive.DoneFiles - state.SampleFiles) / elapsedSeconds;
				state.Rate = state.Rate == null ? instantaneous : 0.5 * instantaneous + 0.5 * state.Rate.Value;
				state.SampleTime = utcNow;
				state.SampleFiles = drive.DoneFiles;
			}
			return state.Rate;
		}
	}
}
