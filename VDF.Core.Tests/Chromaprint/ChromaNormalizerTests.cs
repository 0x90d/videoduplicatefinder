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

using VDF.Core.Chromaprint.Pipeline;

namespace VDF.Core.Tests.Chromaprint;

public class ChromaNormalizerTests {
	[Fact]
	public void Normalize_ZeroVector_RemainsZero() {
		double[] chroma = new double[12];
		ChromaNormalizer.Normalize(chroma);
		Assert.All(chroma, v => Assert.Equal(0.0, v));
	}

	[Fact]
	public void Normalize_KnownVector_CorrectL2() {
		// [3, 4, 0, 0, ...] -> norm = 5 -> [0.6, 0.8, 0, 0, ...]
		double[] chroma = new double[12];
		chroma[0] = 3.0;
		chroma[1] = 4.0;
		ChromaNormalizer.Normalize(chroma);
		Assert.Equal(0.6, chroma[0], precision: 10);
		Assert.Equal(0.8, chroma[1], precision: 10);
		for (int i = 2; i < 12; i++)
			Assert.Equal(0.0, chroma[i]);
	}

	[Fact]
	public void Normalize_UnitVector_Unchanged() {
		double[] chroma = new double[12];
		chroma[0] = 1.0; // already unit vector
		ChromaNormalizer.Normalize(chroma);
		Assert.Equal(1.0, chroma[0], precision: 10);
		for (int i = 1; i < 12; i++)
			Assert.Equal(0.0, chroma[i]);
	}

	[Fact]
	public void Normalize_AllEqual_ProducesUniformVector() {
		double[] chroma = new double[12];
		Array.Fill(chroma, 1.0);
		ChromaNormalizer.Normalize(chroma);

		// After normalization, L2 norm should be 1.0
		double sumSq = 0;
		for (int i = 0; i < 12; i++)
			sumSq += chroma[i] * chroma[i];
		Assert.Equal(1.0, Math.Sqrt(sumSq), precision: 10);

		// All values should be equal
		double expected = 1.0 / Math.Sqrt(12.0);
		Assert.All(chroma, v => Assert.Equal(expected, v, precision: 10));
	}
}
