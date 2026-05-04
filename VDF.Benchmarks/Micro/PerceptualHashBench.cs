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

using BenchmarkDotNet.Attributes;
using VDF.Core.pHash;

namespace VDF.Benchmarks.Micro;

[MemoryDiagnoser]
public class PerceptualHashBench {
	byte[] _gray = null!;

	[GlobalSetup]
	public void Setup() {
		_gray = new byte[32 * 32];
		// Deterministic, non-uniform pixel data so the median split touches both branches.
		var rng = new Random(42);
		rng.NextBytes(_gray);
	}

	/// <summary>
	/// Baseline for the current naive 32x32 DCT. After optimization to compute only
	/// the 8x8 low-frequency coefficients we actually use, we expect this to drop
	/// substantially (likely 5-10x).
	/// </summary>
	[Benchmark]
	public ulong ComputePHash() => PerceptualHash.ComputePHashFromGray32x32(_gray);
}
