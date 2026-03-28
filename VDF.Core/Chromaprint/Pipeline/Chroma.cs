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

using VDF.Core.Chromaprint.FFT;

namespace VDF.Core.Chromaprint.Pipeline {

	/// <summary>
	/// Converts a windowed PCM frame into a 12-element chroma (pitch class) energy vector
	/// by computing an FFT and mapping each frequency bin to one of the 12 semitones.
	/// </summary>
	internal sealed class Chroma {
		internal const int FrameSize = 4096;
		internal const int SampleRate = 11025;

		// Frequency range matching the standard Chromaprint algorithm
		private const double MinFreq = 27.5;   // A0
		private const double MaxFreq = 3520.0; // A7

		private static readonly double[] s_hannWindow = BuildHannWindow(FrameSize);

		// Maps each FFT bin index to a chroma class (0–11), or -1 to skip.
		private static readonly int[] s_chromaMap = BuildChromaMap();

		private readonly double[] _re = new double[FrameSize];
		private readonly double[] _im = new double[FrameSize];

		private static double[] BuildHannWindow(int n) {
			var w = new double[n];
			double factor = 2.0 * Math.PI / (n - 1);
			for (int i = 0; i < n; i++)
				w[i] = 0.5 * (1.0 - Math.Cos(i * factor));
			return w;
		}

		private static int[] BuildChromaMap() {
			int bins = FrameSize / 2 + 1;
			var map = new int[bins];
			Array.Fill(map, -1);
			for (int i = 1; i < bins; i++) {
				double freq = (double)i * SampleRate / FrameSize;
				if (freq < MinFreq || freq > MaxFreq)
					continue;
				// Number of semitones above A0 (27.5 Hz)
				double note = 12.0 * Math.Log2(freq / MinFreq);
				int c = (int)note % 12;
				if (c < 0) c += 12;
				map[i] = c;
			}
			return map;
		}

		/// <summary>
		/// Applies a Hann window to <paramref name="samples"/>, runs an FFT, and
		/// accumulates the energy of each bin into the corresponding chroma bin.
		/// </summary>
		/// <param name="samples">Exactly <see cref="FrameSize"/> normalised samples in [-1, 1].</param>
		/// <param name="chroma">Output buffer of length 12 — must be zeroed by caller.</param>
		internal void Compute(ReadOnlySpan<double> samples, double[] chroma) {
			// Apply Hann window and copy into re[]
			for (int i = 0; i < FrameSize; i++) {
				_re[i] = samples[i] * s_hannWindow[i];
				_im[i] = 0.0;
			}

			FftService.Forward(_re, _im);

			// Accumulate squared magnitude per chroma bin (skip DC and Nyquist)
			int bins = FrameSize / 2 + 1;
			for (int i = 1; i < bins - 1; i++) {
				int c = s_chromaMap[i];
				if (c < 0) continue;
				chroma[c] += _re[i] * _re[i] + _im[i] * _im[i];
			}
		}
	}
}
