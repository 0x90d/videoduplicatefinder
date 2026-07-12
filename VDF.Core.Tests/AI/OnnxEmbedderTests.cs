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
using VDF.TestSupport;

namespace VDF.Core.Tests.AI;

/// <summary>
/// Drives the ONNX inference plumbing with the checked-in tiny embedder (same I/O
/// contract as DINOv2, ~300 KB, deterministic). The native onnxruntime comes from
/// this test project's full OnnxRuntime package — no downloads.
/// </summary>
public class OnnxEmbedderTests {

	internal static byte[] SolidColorFrame(byte r, byte g, byte b) {
		var img = new byte[OnnxEmbedder.InputSide * OnnxEmbedder.InputSide * 3];
		for (int i = 0; i < img.Length; i += 3) {
			img[i] = r;
			img[i + 1] = g;
			img[i + 2] = b;
		}
		return img;
	}

	/// <summary>A frame with per-pixel structure so pooling windows differ.</summary>
	internal static byte[] PatternFrame(int seed) {
		var rng = new Random(seed);
		var img = new byte[OnnxEmbedder.InputSide * OnnxEmbedder.InputSide * 3];
		rng.NextBytes(img);
		return img;
	}

	[Fact]
	public void EmbedBatch_ReturnsNormalized384DimVectors() {
		using var embedder = new OnnxEmbedder(TestModels.TinyEmbedderPath);
		float[][] embeddings = embedder.EmbedBatch(new[] { PatternFrame(1), PatternFrame(2), PatternFrame(3) });

		Assert.Equal(3, embeddings.Length);
		foreach (float[] e in embeddings) {
			Assert.Equal(EmbeddingMath.Dimensions, e.Length);
			double norm = Math.Sqrt(e.Sum(x => (double)x * x));
			Assert.Equal(1.0, norm, 3);
		}
	}

	[Fact]
	public void SameFrame_EmbedsIdentically_DifferentFramesDoNot() {
		using var embedder = new OnnxEmbedder(TestModels.TinyEmbedderPath);
		byte[] frame = PatternFrame(7);
		float[][] embeddings = embedder.EmbedBatch(new[] { frame, (byte[])frame.Clone(), PatternFrame(8) });

		Assert.Equal(embeddings[0], embeddings[1]);
		float crossSim = EmbeddingMath.CosineSimilarity(
			EmbeddingMath.QuantizeUnitVector(embeddings[0]),
			EmbeddingMath.QuantizeUnitVector(embeddings[2]));
		Assert.True(crossSim < 0.99f, $"distinct frames should not embed near-identically (cos={crossSim})");
	}

	[Fact]
	public void EmbedBatchQuantized_RoundTripsThroughCosine() {
		using var embedder = new OnnxEmbedder(TestModels.TinyEmbedderPath);
		byte[][] quantized = embedder.EmbedBatchQuantized(new[] { PatternFrame(11), PatternFrame(11) });
		Assert.All(quantized, q => Assert.Equal(EmbeddingMath.Dimensions, q.Length));
		Assert.Equal(1f, EmbeddingMath.CosineSimilarity(quantized[0], quantized[1]), 0.02f);
	}

	[Fact]
	public void EmbedBatch_RejectsWrongFrameSize() {
		using var embedder = new OnnxEmbedder(TestModels.TinyEmbedderPath);
		Assert.Throws<ArgumentException>(() => embedder.EmbedBatch(new[] { new byte[100] }));
	}

	[Fact]
	public async Task EmbeddingPipeline_WritesQuantizedEmbeddingsToStore() {
		var entry = new FileEntry { Folder = @"D:\media" };
		entry.Path = @"D:\media\pipeline.mp4";
		var store = new UnionEmbeddingStore();

		using (var pipeline = new EmbeddingPipeline(TestModels.TinyEmbedderPath, store, CancellationToken.None)) {
			Assert.True(pipeline.WantsEmbedding(entry, 1.0));
			pipeline.SubmitFrame(entry, 1.0, PatternFrame(21));
			pipeline.SubmitFrame(entry, 3.0, PatternFrame(22));
			await pipeline.CompleteAsync();
			Assert.Equal(2, pipeline.EmbeddedCount);
			Assert.False(pipeline.WantsEmbedding(entry, 1.0));
		}

		Assert.Equal(EmbeddingMath.Dimensions, store.GetEmbedding(entry, 1.0)!.Length);
		Assert.Equal(EmbeddingMath.Dimensions, store.GetEmbedding(entry, 3.0)!.Length);
	}
}
