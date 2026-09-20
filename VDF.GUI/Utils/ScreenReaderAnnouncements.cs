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

namespace VDF.GUI.Utils {
	/// <summary>
	/// Decides what of a running scan is worth saying to a screen reader. The scanning view
	/// repaints several times a second; spoken, that would bury the user. What a sighted
	/// user takes from it in passing is which phase runs and roughly how far it is, so that
	/// is what gets said: every phase once, and the progress in steps of ten percent with a
	/// pause in between. Pure, the caller passes the clock.
	/// </summary>
	internal sealed class ScanProgressAnnouncer {
		internal static readonly TimeSpan StageGap = TimeSpan.FromSeconds(5);
		internal static readonly TimeSpan ProgressGap = TimeSpan.FromSeconds(20);

		long lastPosition = -1;
		long lastMax = -1;
		bool analysisPhase;
		string phaseLabel = string.Empty;
		string? pendingStage;
		int spokenDecile;
		DateTime lastSpoken = DateTime.MinValue;

		public void Reset() {
			lastPosition = lastMax = -1;
			analysisPhase = false;
			phaseLabel = string.Empty;
			pendingStage = null;
			spokenDecile = 0;
			lastSpoken = DateTime.MinValue;
		}

		/// <param name="stage">The engine's stage label of this snapshot, empty when it has none.</param>
		/// <param name="analysisLabel">What to call the file analysis phase, which has no label of its own.</param>
		/// <param name="progressFormat">Format with {0} = percent and {1} = <paramref name="remaining"/>.</param>
		/// <returns>The text to announce, or null.</returns>
		public string? Next(string? stage, long position, long max, string remaining, DateTime now,
							string analysisLabel, string progressFormat) {
			stage ??= string.Empty;
			// Every phase starts its counters over; that, not the label, marks a new phase.
			// During file analysis the label is whatever a worker did last (probing, sampling,
			// fingerprinting) and flips with every snapshot: the phase opens with an empty
			// label, and its labels are never announced.
			bool restarted = max != lastMax || position < lastPosition;
			lastMax = max;
			lastPosition = position;
			if (restarted) {
				analysisPhase = stage.Length == 0;
				StartPhase(analysisPhase ? analysisLabel : stage);
			}
			else if (!analysisPhase && stage.Length > 0 && stage != phaseLabel) {
				// Two phases of the same length in a row (the single-step AI phases).
				StartPhase(stage);
			}

			if (pendingStage != null) {
				// Short phases follow each other within a second: the latest one waits its
				// turn instead of talking over the one before.
				if (now - lastSpoken < StageGap) return null;
				string text = pendingStage;
				pendingStage = null;
				lastSpoken = now;
				return text;
			}

			if (max <= 0) return null;
			int percent = (int)(Math.Clamp(position, 0, max) * 100 / max);
			if (percent >= 100 || percent / 10 <= spokenDecile) return null;
			if (now - lastSpoken < ProgressGap) return null;
			spokenDecile = percent / 10;
			lastSpoken = now;
			return string.Format(progressFormat, percent, remaining);
		}

		void StartPhase(string label) {
			phaseLabel = label;
			pendingStage = label.Length == 0 ? null : label;
			spokenDecile = 0;
		}
	}

	/// <summary>
	/// What the busy curtain says: its text when it comes up, and after that the text only
	/// every so often, because "Deleting files... 5/200" changes with every file.
	/// </summary>
	internal sealed class BusyAnnouncer {
		internal static readonly TimeSpan Gap = TimeSpan.FromSeconds(10);

		bool wasVisible;
		bool spokeForThisCurtain;
		DateTime lastSpoken = DateTime.MinValue;

		/// <returns>The text to announce, or null.</returns>
		public string? Next(bool curtainVisible, string? text, DateTime now) {
			if (!curtainVisible) {
				wasVisible = false;
				return null;
			}
			if (!wasVisible) {
				wasVisible = true;
				spokeForThisCurtain = false;
			}
			if (string.IsNullOrWhiteSpace(text)) return null;
			// The text may be set before or after the curtain comes up; either way its first
			// text is said at once.
			if (spokeForThisCurtain && now - lastSpoken < Gap) return null;
			spokeForThisCurtain = true;
			lastSpoken = now;
			return text;
		}
	}
}
