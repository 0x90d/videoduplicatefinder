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

using VDF.Core.Chromaprint.FFT;

namespace VDF.Core.Tests.Chromaprint;

public class FftServiceTests {
	[Fact]
	public void Forward_DcSignal_EnergyInBinZero() {
		// Constant signal of 1.0 -> all energy in bin 0
		int n = 8;
		double[] re = new double[n];
		double[] im = new double[n];
		Array.Fill(re, 1.0);

		FftService.Forward(re, im);

		// Bin 0 should have magnitude = n (unnormalized DFT of constant signal)
		Assert.Equal(n, re[0], precision: 10);
		Assert.Equal(0.0, im[0], precision: 10);

		// All other bins should be ~0
		for (int k = 1; k < n; k++) {
			Assert.True(Math.Abs(re[k]) < 1e-10, $"re[{k}] = {re[k]} should be ~0");
			Assert.True(Math.Abs(im[k]) < 1e-10, $"im[{k}] = {im[k]} should be ~0");
		}
	}

	[Fact]
	public void Forward_KnownLength4_MatchesHandCalculation() {
		// DFT of [1, 0, -1, 0]:
		// X[0] = 1 + 0 + (-1) + 0 = 0
		// X[1] = 1 + 0*(-j) + (-1)*(-1) + 0*(j) = 1 + 1 = 2
		// X[2] = 1 + 0*(-1) + (-1)*(1) + 0*(-1) = 0
		// X[3] = 1 + 0*(j) + (-1)*(-1) + 0*(-j) = 2
		double[] re = { 1, 0, -1, 0 };
		double[] im = { 0, 0, 0, 0 };

		FftService.Forward(re, im);

		Assert.Equal(0.0, re[0], precision: 10);
		Assert.Equal(2.0, re[1], precision: 10);
		Assert.Equal(0.0, re[2], precision: 10);
		Assert.Equal(2.0, re[3], precision: 10);

		Assert.Equal(0.0, im[0], precision: 10);
		Assert.Equal(0.0, im[1], precision: 10);
		Assert.Equal(0.0, im[2], precision: 10);
		Assert.Equal(0.0, im[3], precision: 10);
	}

	[Fact]
	public void Forward_PureSineWave_PeakAtExpectedBin() {
		// A pure cosine wave at bin k should produce energy only at bins k and N-k
		int n = 16;
		int targetBin = 3;
		double[] re = new double[n];
		double[] im = new double[n];

		for (int i = 0; i < n; i++)
			re[i] = Math.Cos(2.0 * Math.PI * targetBin * i / n);

		FftService.Forward(re, im);

		// Find the bin with maximum magnitude (excluding DC)
		double maxMag = 0;
		int maxBin = 0;
		for (int k = 1; k < n; k++) {
			double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
			if (mag > maxMag) {
				maxMag = mag;
				maxBin = k;
			}
		}

		Assert.Equal(targetBin, maxBin);
	}

	[Fact]
	public void Forward_Parseval_EnergyPreserved() {
		// Parseval's theorem: sum |x[n]|^2 = (1/N) * sum |X[k]|^2
		int n = 8;
		double[] re = { 1, 2, 3, 4, 5, 6, 7, 8 };
		double[] im = new double[n];

		// Time-domain energy
		double timeEnergy = 0;
		for (int i = 0; i < n; i++)
			timeEnergy += re[i] * re[i];

		FftService.Forward(re, im);

		// Frequency-domain energy
		double freqEnergy = 0;
		for (int k = 0; k < n; k++)
			freqEnergy += re[k] * re[k] + im[k] * im[k];
		freqEnergy /= n;

		Assert.Equal(timeEnergy, freqEnergy, precision: 8);
	}
}
