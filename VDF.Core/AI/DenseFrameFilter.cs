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

namespace VDF.Core.AI {
	/// <summary>
	/// Streaming per-frame usability decision for the dense AI pass. Excluded: dark
	/// frames (they embed near-identically regardless of content — the union pass's
	/// black-frame guard, applied here) and frames byte-identical to their predecessor
	/// (the fps filter's round=up duplicates the previous keyframe across gaps, and
	/// identical frames would multiply one coincidental hit into a full evidence
	/// quorum). Replaces the whole-file SelectUsableDenseFrames so frames no longer
	/// need to be held in memory together (#878); keeps its own copy of the previous
	/// frame, so callers are free to recycle their buffers after each call.
	/// </summary>
	internal sealed class DenseFrameFilter {
		byte[]? previous;

		public bool IsUsable(byte[] frame) {
			bool usable = GrayBytesUtils.VerifyRgbFrameValues(frame) &&
				!(previous != null && frame.AsSpan().SequenceEqual(previous));
			// The duplicate compare is against the previous RAW frame, usable or not.
			if (previous == null || previous.Length != frame.Length)
				previous = new byte[frame.Length];
			frame.CopyTo(previous, 0);
			return usable;
		}
	}
}
