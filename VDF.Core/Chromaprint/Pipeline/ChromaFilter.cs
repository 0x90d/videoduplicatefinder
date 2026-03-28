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
// Fix: ring buffer stores values (not array references) — eliminates per-frame allocation.

namespace VDF.Core.Chromaprint.Pipeline {

	/// <summary>
	/// Applies a 5-tap FIR temporal smoothing filter to successive chroma frames.
	/// The filter coefficients [0.25, 0.50, 1.00, 0.50, 0.25] (sum = 2.50) give a
	/// triangular-shaped response that smooths transient noise while preserving pitch.
	/// </summary>
	internal sealed class ChromaFilter {
		private const int FilterSize = 5;
		private const double FilterNorm = 2.50; // sum of coefficients

		private static readonly double[] s_coeff = { 0.25, 0.50, 1.00, 0.50, 0.25 };

		// Ring buffer: stores 12 doubles per slot rather than array references.
		private readonly double[,] _ring = new double[FilterSize, 12];
		private int _head;
		private int _count;

		internal void Reset() {
			Array.Clear(_ring);
			_head = 0;
			_count = 0;
		}

		/// <summary>
		/// Feeds one chroma frame and, once the buffer is full, writes the
		/// filtered result to <paramref name="output"/>.
		/// </summary>
		/// <returns><c>true</c> when output is valid (buffer has been primed).</returns>
		internal bool Feed(double[] input, double[] output) {
			// Copy into ring slot at head
			for (int j = 0; j < 12; j++)
				_ring[_head, j] = input[j];
			_head = (_head + 1) % FilterSize;
			if (_count < FilterSize) {
				_count++;
				if (_count < FilterSize)
					return false;
			}

			// Weighted sum — oldest slot is at _head (the slot we just overwrote wraps around)
			Array.Clear(output, 0, 12);
			for (int i = 0; i < FilterSize; i++) {
				int slot = (_head + i) % FilterSize;
				double w = s_coeff[i];
				for (int j = 0; j < 12; j++)
					output[j] += _ring[slot, j] * w;
			}
			for (int j = 0; j < 12; j++)
				output[j] /= FilterNorm;

			return true;
		}
	}
}
