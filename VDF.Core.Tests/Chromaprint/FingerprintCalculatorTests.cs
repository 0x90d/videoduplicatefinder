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

public class FingerprintCalculatorTests {
	[Fact]
	public void Compute_AllZeroChroma_ReturnsZero() {
		double[] chroma = new double[12]; // all zeros — no bin is greater than any other
		uint result = FingerprintCalculator.Compute(chroma);
		Assert.Equal(0u, result);
	}

	[Fact]
	public void Compute_AllEqualChroma_ReturnsZero() {
		double[] chroma = new double[12];
		Array.Fill(chroma, 0.5);
		uint result = FingerprintCalculator.Compute(chroma);
		Assert.Equal(0u, result);
	}

	[Fact]
	public void Compute_KnownChroma_CorrectBits() {
		// Set bin 0 higher than all others — should set bits where bin 0 is compared against others
		double[] chroma = new double[12];
		chroma[0] = 1.0;
		// All other bins are 0, so:
		// Adjacent pairs: (0,1) -> bit 0 set (0 > 1? yes, 1.0 > 0.0)
		// Minor third: (0,3) -> bit 12 set
		// Tritone: (0,6) -> bit 24 set
		uint result = FingerprintCalculator.Compute(chroma);
		Assert.True((result & (1u << 0)) != 0, "Bit 0 should be set: chroma[0] > chroma[1]");
		Assert.True((result & (1u << 12)) != 0, "Bit 12 should be set: chroma[0] > chroma[3]");
		Assert.True((result & (1u << 24)) != 0, "Bit 24 should be set: chroma[0] > chroma[6]");
	}

	[Fact]
	public void Compute_SameInput_SameOutput() {
		double[] chroma = { 0.1, 0.5, 0.3, 0.8, 0.2, 0.6, 0.4, 0.7, 0.9, 0.05, 0.15, 0.45 };
		uint result1 = FingerprintCalculator.Compute(chroma);
		uint result2 = FingerprintCalculator.Compute(chroma);
		Assert.Equal(result1, result2);
	}

	[Fact]
	public void AggregateMajorityVote_EmptyList_ReturnsZero() {
		var list = new List<uint>();
		uint result = FingerprintCalculator.AggregateMajorityVote(list);
		Assert.Equal(0u, result);
	}

	[Fact]
	public void AggregateMajorityVote_SingleItem_ReturnsSame() {
		var list = new List<uint> { 0xDEADBEEF };
		uint result = FingerprintCalculator.AggregateMajorityVote(list);
		Assert.Equal(0xDEADBEEFu, result);
	}

	[Fact]
	public void AggregateMajorityVote_AllSame_ReturnsSame() {
		var list = new List<uint> { 0xFF00FF00, 0xFF00FF00, 0xFF00FF00 };
		uint result = FingerprintCalculator.AggregateMajorityVote(list);
		Assert.Equal(0xFF00FF00u, result);
	}

	[Fact]
	public void AggregateMajorityVote_MajoritySetsBit() {
		// 3 out of 5 have bit 0 set -> threshold = 5/2+1 = 3, so bit should be set
		var list = new List<uint> { 1, 1, 1, 0, 0 };
		uint result = FingerprintCalculator.AggregateMajorityVote(list);
		Assert.True((result & 1u) != 0, "Bit 0 should be set (3 of 5 have it)");
	}

	[Fact]
	public void AggregateMajorityVote_ExactHalf_NotSet() {
		// 2 out of 4 have bit 0 set -> threshold = 4/2+1 = 3, so bit should NOT be set
		var list = new List<uint> { 1, 1, 0, 0 };
		uint result = FingerprintCalculator.AggregateMajorityVote(list);
		Assert.True((result & 1u) == 0, "Bit 0 should NOT be set (only 2 of 4, threshold is 3)");
	}
}
