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

	/// <summary>
	/// Encodes a normalised 12-element chroma vector as a 32-bit fingerprint integer
	/// using 32 fixed pairwise comparisons over the chroma bins.
	///
	/// The 32 feature pairs are:
	///   bits  0–11: adjacent semitone pairs  (i, (i+1) % 12)
	///   bits 12–23: minor-third pairs        (i, (i+3) % 12)
	///   bits 24–31: tritone pairs            (i, (i+6) % 12) for i in 0–7
	///
	/// Bit i = 1 when chroma[pair.a] > chroma[pair.b], else 0.
	/// Same audio → same bits; similar audio → low Hamming distance.
	/// </summary>
	internal static class FingerprintCalculator {
		private static readonly (int A, int B)[] s_pairs = BuildPairs();

		private static (int, int)[] BuildPairs() {
			var pairs = new List<(int, int)>(32);
			for (int i = 0; i < 12; i++) pairs.Add((i, (i + 1) % 12));  // adjacent
			for (int i = 0; i < 12; i++) pairs.Add((i, (i + 3) % 12));  // minor third
			for (int i = 0; i < 8; i++)  pairs.Add((i, (i + 6) % 12));  // tritone
			return pairs.ToArray();
		}

		/// <summary>Returns a 32-bit fingerprint for one chroma frame.</summary>
		internal static uint Compute(double[] chroma) {
			uint fp = 0u;
			for (int i = 0; i < 32; i++) {
				if (chroma[s_pairs[i].A] > chroma[s_pairs[i].B])
					fp |= 1u << i;
			}
			return fp;
		}

		/// <summary>
		/// Aggregates a list of per-frame fingerprints into a single 32-bit value
		/// using bitwise majority vote: for each bit position, the output bit is 1
		/// if more than half of the input frames had that bit set.
		/// </summary>
		internal static uint AggregateMajorityVote(List<uint> fingerprints) {
			if (fingerprints.Count == 0) return 0u;
			int threshold = fingerprints.Count / 2 + 1;
			uint result = 0u;
			for (int bit = 0; bit < 32; bit++) {
				uint mask = 1u << bit;
				int count = 0;
				for (int i = 0; i < fingerprints.Count; i++)
					if ((fingerprints[i] & mask) != 0)
						count++;
				if (count >= threshold)
					result |= mask;
			}
			return result;
		}
	}
}
