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

namespace VDF.Core.Utils {
	/// <summary>
	/// Gate for a periodic best-effort save called from inside a parallel worker loop: it runs
	/// at most once per interval and never on two threads at once. A worker that finds a save
	/// already in flight keeps working instead of queueing behind it, so checkpointing costs
	/// one thread rather than stalling the phase.
	/// </summary>
	/// <remarks>
	/// Exists because phases that produce expensive, recomputable data (the AI pass's keyframe
	/// embeddings) used to persist it only at the very end — hours of work thrown away by a
	/// crash or by the kill that ends a run the user believes is hung (#865).
	/// </remarks>
	internal sealed class PeriodicCheckpoint {
		readonly TimeSpan interval;
		readonly Func<DateTime> utcNow;
		DateTime lastRunUtc;
		int busy;

		/// <param name="interval">Zero or negative disables checkpointing entirely.</param>
		/// <param name="utcNow">Clock seam for tests; defaults to the real one.</param>
		internal PeriodicCheckpoint(TimeSpan interval, Func<DateTime>? utcNow = null) {
			this.interval = interval;
			this.utcNow = utcNow ?? (() => DateTime.UtcNow);
			lastRunUtc = this.utcNow();
		}

		/// <summary>
		/// Runs <paramref name="save"/> when the interval has elapsed and no other thread is
		/// saving; returns whether it ran. The clock restarts when the save FINISHES, so a
		/// write that takes longer than the interval cannot immediately re-trigger.
		/// </summary>
		internal bool TryRun(Action save) {
			if (interval <= TimeSpan.Zero)
				return false;
			if (utcNow() - lastRunUtc < interval)
				return false;
			if (Interlocked.Exchange(ref busy, 1) == 1)
				return false;
			try {
				save();
				return true;
			}
			finally {
				lastRunUtc = utcNow();
				Interlocked.Exchange(ref busy, 0);
			}
		}
	}
}
