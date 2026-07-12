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

/// <summary>
/// The offset-consistency matcher behind visual partial detection: several frame hits
/// must agree on ONE time offset — high per-frame similarity alone is not enough
/// (calibrated on real footage where same-scene noise reaches the hit threshold).
/// </summary>
public class AiPartialMatcherTests {

	static byte[][] RandomEmbeddings(int count, int seed) {
		var rng = new Random(seed);
		var frames = new byte[count][];
		for (int i = 0; i < count; i++)
			frames[i] = EmbeddingMath.QuantizeUnitVector(EmbeddingMathTests.RandomUnitVector(rng));
		return frames;
	}

	[Fact]
	public void ClipCutFromSource_IsFoundAtCorrectOffset() {
		byte[][] sourceFrames = RandomEmbeddings(40, 42);
		// Clip = source content from frame 12 onward (60 s at 5 s cadence), 15 frames long.
		byte[][] clipFrames = sourceFrames.Skip(12).Take(15).ToArray();
		var source = new DenseEmbeddingStore.DenseRecord(0, 0, 5f, sourceFrames);
		var clip = new DenseEmbeddingStore.DenseRecord(0, 0, 5f, clipFrames);

		Assert.True(ScanEngine.TryMatchDenseFrames(source, clip, hitThreshold: 0.89f, out float sim, out int offset));
		Assert.Equal(1f, sim, 0.03f);
		Assert.Equal(60, offset);
	}

	[Fact]
	public void UnrelatedContent_DoesNotMatch() {
		var source = new DenseEmbeddingStore.DenseRecord(0, 0, 5f, RandomEmbeddings(40, 1));
		var clip = new DenseEmbeddingStore.DenseRecord(0, 0, 5f, RandomEmbeddings(15, 2));

		Assert.False(ScanEngine.TryMatchDenseFrames(source, clip, hitThreshold: 0.89f, out _, out _));
	}

	[Fact]
	public void FewerHitsThanQuorum_DoesNotMatch() {
		byte[][] sourceFrames = RandomEmbeddings(40, 3);
		// Only 3 matching frames — one below the required 4 consistent hits.
		byte[][] clipFrames = sourceFrames.Skip(10).Take(3).ToArray();
		var source = new DenseEmbeddingStore.DenseRecord(0, 0, 5f, sourceFrames);
		var clip = new DenseEmbeddingStore.DenseRecord(0, 0, 5f, clipFrames);

		Assert.False(ScanEngine.TryMatchDenseFrames(source, clip, hitThreshold: 0.89f, out _, out _));
	}

	[Fact]
	public void InconsistentOffsets_DoNotMatch() {
		byte[][] sourceFrames = RandomEmbeddings(60, 4);
		// Clip frames present in the source but scattered — no single offset explains them.
		byte[][] clipFrames = { sourceFrames[2], sourceFrames[40], sourceFrames[11], sourceFrames[55], sourceFrames[25], sourceFrames[33] };
		var source = new DenseEmbeddingStore.DenseRecord(0, 0, 15f, sourceFrames);
		var clip = new DenseEmbeddingStore.DenseRecord(0, 0, 15f, clipFrames);

		Assert.False(ScanEngine.TryMatchDenseFrames(source, clip, hitThreshold: 0.89f, out _, out _));
	}

	[Fact]
	public void MixedIntervals_StillAlignByTime() {
		// Source sampled at 15 s, clip at 5 s — the offset math works in seconds, not indices.
		byte[][] sourceFrames = RandomEmbeddings(40, 5);
		// Clip covers source frames 8..12 (= 120 s..180 s): clip frame k at 5k s equals
		// source content when 5k ≡ 0 (mod 15) relative to the 120 s start.
		var clipFrames = new byte[13][];
		var rng = new Random(99);
		for (int k = 0; k < clipFrames.Length; k++) {
			clipFrames[k] = (5 * k) % 15 == 0
				? sourceFrames[8 + (5 * k) / 15]
				: EmbeddingMath.QuantizeUnitVector(EmbeddingMathTests.RandomUnitVector(rng));
		}
		var source = new DenseEmbeddingStore.DenseRecord(0, 0, 15f, sourceFrames);
		var clip = new DenseEmbeddingStore.DenseRecord(0, 0, 5f, clipFrames);

		Assert.True(ScanEngine.TryMatchDenseFrames(source, clip, hitThreshold: 0.89f, out _, out int offset));
		Assert.Equal(120, offset);
	}
}
