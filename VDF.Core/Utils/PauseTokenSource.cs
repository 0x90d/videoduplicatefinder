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
	/// Implementation of PauseTokenSource pattern based on the blog post: 
	/// http://blogs.msdn.com/b/pfxteam/archive/2013/01/13/cooperatively-pausing-async-methods.aspx 
	/// </summary>
	public sealed class PauseTokenSource {
		// Set = running, reset = paused. Workers block on the event instead of
		// polling IsPaused in a sleep loop, so resuming wakes them immediately
		// and a paused scan burns no CPU.
		readonly ManualResetEventSlim resumeEvent = new(initialState: true);

		public bool IsPaused {
			get => !resumeEvent.IsSet;
			set {
				if (value)
					resumeEvent.Reset();
				else
					resumeEvent.Set();
			}
		}

		/// <summary>Blocks until the source is resumed; no-op when not paused.
		/// Throws <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is canceled while waiting.</summary>
		public void WaitWhilePaused(CancellationToken cancellationToken = default) =>
			resumeEvent.Wait(cancellationToken);
	}
}
