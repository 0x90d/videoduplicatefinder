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
	/// Middle-ellipsis text trimming for long paths: the drive/root prefix and the file/tail
	/// suffix stay visible, the middle collapses to "…". Pure logic — the measuring function
	/// is injected so the algorithm is unit-testable without a text stack.
	/// </summary>
	internal static class MiddleEllipsis {
		internal const char EllipsisChar = '…';

		/// <summary>
		/// Returns <paramref name="text"/> unchanged when it fits into <paramref name="maxWidth"/>,
		/// otherwise the widest "prefix…suffix" combination that fits. The suffix gets the odd
		/// extra character — for paths the tail is the more informative end.
		/// </summary>
		internal static string Trim(string text, Func<string, double> measure, double maxWidth) {
			if (string.IsNullOrEmpty(text) || measure(text) <= maxWidth)
				return text;

			// Binary search the number of kept characters; Compose() is monotonic in width.
			int lo = 1, hi = text.Length - 1, best = 0;
			while (lo <= hi) {
				int mid = lo + (hi - lo) / 2;
				if (measure(Compose(text, mid)) <= maxWidth) {
					best = mid;
					lo = mid + 1;
				}
				else {
					hi = mid - 1;
				}
			}
			return Compose(text, best);
		}

		/// <summary>Builds the candidate string keeping <paramref name="keep"/> characters of the original.</summary>
		internal static string Compose(string text, int keep) {
			if (keep <= 0)
				return EllipsisChar.ToString();
			if (keep >= text.Length)
				return text;
			int front = keep / 2;
			int back = keep - front;
			return string.Concat(text.AsSpan(0, front), EllipsisChar.ToString(), text.AsSpan(text.Length - back));
		}
	}
}
