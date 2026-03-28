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
// Retargeted to net9.0; replaced LomontFFT with an in-place radix-2 Cooley-Tukey FFT.

namespace VDF.Core.Chromaprint.FFT {

	/// <summary>
	/// In-place radix-2 DIT Cooley-Tukey FFT operating on a power-of-two length array.
	/// Input length must be a power of two.
	/// After <see cref="Forward"/>, re[k] and im[k] hold the real and imaginary parts
	/// of the k-th frequency bin (not normalized).
	/// </summary>
	internal static class FftService {
		/// <summary>Compute the forward DFT in-place.</summary>
		internal static void Forward(double[] re, double[] im) {
			int n = re.Length;

			// Bit-reversal permutation
			for (int i = 1, j = 0; i < n; i++) {
				int bit = n >> 1;
				for (; (j & bit) != 0; bit >>= 1)
					j ^= bit;
				j ^= bit;
				if (i < j) {
					(re[i], re[j]) = (re[j], re[i]);
					(im[i], im[j]) = (im[j], im[i]);
				}
			}

			// Butterfly stages
			for (int len = 2; len <= n; len <<= 1) {
				double ang = -2.0 * Math.PI / len;
				double wRe = Math.Cos(ang);
				double wIm = Math.Sin(ang);
				for (int i = 0; i < n; i += len) {
					double curRe = 1.0, curIm = 0.0;
					int half = len >> 1;
					for (int j = 0; j < half; j++) {
						double uRe = re[i + j];
						double uIm = im[i + j];
						double vRe = re[i + j + half] * curRe - im[i + j + half] * curIm;
						double vIm = re[i + j + half] * curIm + im[i + j + half] * curRe;
						re[i + j] = uRe + vRe;
						im[i + j] = uIm + vIm;
						re[i + j + half] = uRe - vRe;
						im[i + j + half] = uIm - vIm;
						double tmpRe = curRe * wRe - curIm * wIm;
						curIm = curRe * wIm + curIm * wRe;
						curRe = tmpRe;
					}
				}
			}
		}
	}
}
