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
using VDF.Core.Utils;

namespace VDF.Benchmarks.Micro;

[MemoryDiagnoser]
public class GrayBytesBench {
	byte[] _a = null!;
	byte[] _b = null!;

	[GlobalSetup]
	public void Setup() {
		_a = new byte[32 * 32];
		_b = new byte[32 * 32];
		var rng = new Random(42);
		rng.NextBytes(_a);
		rng.NextBytes(_b);
	}

	/// <summary>
	/// Hot path: pairwise distance between two 32x32 gray frames. AVX2/SSE2 SAD reduction.
	/// Watch for the horizontal-sum tail loop being a measurable fraction of the budget
	/// at this size — that's the part the SIMD-tail optimization would target.
	/// </summary>
	[Benchmark]
	public float PercentageDifference() => GrayBytesUtils.PercentageDifference(_a, _b);

	/// <summary>
	/// Alternate compare path that skips black/white pixels. Now AVX2-vectorized like
	/// <see cref="PercentageDifference"/>, but with mask-build + count overhead per block.
	/// Parameterized over the four (ignoreBlack, ignoreWhite) flag combinations:
	/// the (false, false) case short-circuits to <see cref="PercentageDifference"/>
	/// and should match it exactly.
	/// </summary>
	[Benchmark]
	[Arguments(true, true)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(false, false)]
	public float PercentageDifferenceWithoutSpecificPixels(bool ignoreBlack, bool ignoreWhite) =>
		GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(_a, _b, ignoreBlack, ignoreWhite);

	/// <summary>
	/// Horizontal-flip of the 32x32 gray frame for symmetry detection. AVX2 path uses
	/// pshufb on 16B halves with a swap; baseline Array.Copy+Array.Reverse for !AVX2.
	/// </summary>
	[Benchmark]
	public byte[] FlipGrayScale() => GrayBytesUtils.FlipGrayScale(_a);

	/// <summary>
	/// Dark-pixel verifier — currently a scalar countup. Re-runs after every video sample,
	/// included to size whether folding it into the row-copy is worth doing.
	/// </summary>
	[Benchmark]
	public bool VerifyGrayScaleValues() => GrayBytesUtils.VerifyGrayScaleValues(_a);
}
