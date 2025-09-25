// /*
//     Copyright (C) 2025 0x90d
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
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VDF.Core.pHash {
	internal static class PHashCompare {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static int Hamming(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

		/// <summary>
		/// “if ~X% match, it is a duplicate”
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <param name="percent"></param>
		/// <param name="strict">true → floor (at least X%); strict=false → round (approximately X%)</param>
		/// <returns></returns>
		/// <exception cref="ArgumentOutOfRangeException"></exception>
		public static bool IsDuplicateByPercent(ulong a, ulong b, out float similarity, double percent = 0.90,  bool strict = true) {
			if (percent < 0 || percent > 1) throw new ArgumentOutOfRangeException(nameof(percent));
			int d = Hamming(a, b);
			similarity = 1f - (d / 64f);
			double bits = (1.0 - percent) * 64.0;
			int maxBits = strict ? (int)Math.Floor(bits) : (int)Math.Round(bits);
			return d <= maxBits;
		}
	}
}
