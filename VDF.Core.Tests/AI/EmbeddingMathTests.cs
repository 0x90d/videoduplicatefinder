// /*
//     Copyright (C) 2026 0x90d
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

using VDF.Core.AI;

namespace VDF.Core.Tests.AI;

public class EmbeddingMathTests {

	internal static float[] RandomUnitVector(Random rng, int dim = EmbeddingMath.Dimensions) {
		var v = new float[dim];
		double sum = 0;
		for (int i = 0; i < dim; i++) {
			v[i] = (float)(rng.NextDouble() * 2 - 1);
			sum += (double)v[i] * v[i];
		}
		float inv = (float)(1.0 / Math.Sqrt(sum));
		for (int i = 0; i < dim; i++) v[i] *= inv;
		return v;
	}

	static float FloatCosine(float[] a, float[] b) {
		double dot = 0;
		for (int i = 0; i < a.Length; i++) dot += (double)a[i] * b[i];
		return (float)dot;
	}

	[Fact]
	public void QuantizedSelfSimilarity_IsOne() {
		var rng = new Random(1);
		for (int t = 0; t < 20; t++) {
			byte[] q = EmbeddingMath.QuantizeUnitVector(RandomUnitVector(rng));
			Assert.Equal(1f, EmbeddingMath.CosineSimilarity(q, q), 0.02f);
		}
	}

	[Fact]
	public void QuantizedCosine_MatchesFloatCosine_WithinTolerance() {
		var rng = new Random(2);
		for (int t = 0; t < 50; t++) {
			float[] a = RandomUnitVector(rng);
			float[] b = RandomUnitVector(rng);
			float expected = FloatCosine(a, b);
			float actual = EmbeddingMath.CosineSimilarity(
				EmbeddingMath.QuantizeUnitVector(a), EmbeddingMath.QuantizeUnitVector(b));
			Assert.Equal(expected, actual, 0.02f);
		}
	}

	[Fact]
	public void SimdPath_MatchesScalarReference() {
		var rng = new Random(3);
		// 384 exercises full SIMD lanes; 13 exercises the scalar tail exclusively.
		foreach (int dim in new[] { EmbeddingMath.Dimensions, 13 }) {
			for (int t = 0; t < 25; t++) {
				byte[] a = EmbeddingMath.QuantizeUnitVector(RandomUnitVector(rng, dim));
				byte[] b = EmbeddingMath.QuantizeUnitVector(RandomUnitVector(rng, dim));
				Assert.Equal(EmbeddingMath.CosineSimilarityScalar(a, b), EmbeddingMath.CosineSimilarity(a, b), 5);
			}
		}
	}

	[Fact]
	public void Quantize_ClampsOutOfRangeComponents() {
		byte[] q = EmbeddingMath.QuantizeUnitVector(new[] { 2f, -2f, 0f, 1f, -1f });
		Assert.Equal(127, (sbyte)q[0]);
		Assert.Equal(-127, (sbyte)q[1]);
		Assert.Equal(0, (sbyte)q[2]);
		Assert.Equal(127, (sbyte)q[3]);
		Assert.Equal(-127, (sbyte)q[4]);
	}

	[Fact]
	public void OrthogonalVectors_HaveNearZeroSimilarity() {
		var a = new float[EmbeddingMath.Dimensions];
		var b = new float[EmbeddingMath.Dimensions];
		a[0] = 1f;
		b[1] = 1f;
		Assert.Equal(0f, EmbeddingMath.CosineSimilarity(
			EmbeddingMath.QuantizeUnitVector(a), EmbeddingMath.QuantizeUnitVector(b)), 0.001f);
	}
}
