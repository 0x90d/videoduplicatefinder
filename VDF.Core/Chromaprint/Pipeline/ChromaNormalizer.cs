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
// Derived from AcoustID.NET by wo80 (https://github.com/wo80/AcoustID.NET), LGPL 2.1.

namespace VDF.Core.Chromaprint.Pipeline {

	/// <summary>L2-normalises a 12-element chroma vector in-place.</summary>
	internal static class ChromaNormalizer {
		private const double Epsilon = 1e-10;

		internal static void Normalize(double[] chroma) {
			double sumSq = 0.0;
			for (int i = 0; i < 12; i++)
				sumSq += chroma[i] * chroma[i];

			if (sumSq < Epsilon) {
				Array.Clear(chroma, 0, 12);
				return;
			}

			double invNorm = 1.0 / Math.Sqrt(sumSq);
			for (int i = 0; i < 12; i++)
				chroma[i] *= invNorm;
		}
	}
}
