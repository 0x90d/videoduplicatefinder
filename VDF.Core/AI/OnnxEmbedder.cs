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

using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VDF.Core.AI {
	/// <summary>
	/// Wraps the ONNX image-embedding model (DINOv2-small vision transformer). Input is a
	/// raw 224×224 RGB24 frame; output an L2-normalized (optionally int8-quantized)
	/// embedding whose dot product is the frames' cosine similarity. Instances are
	/// created per scan; <see cref="EmbedBatch"/> is called from a single worker thread.
	/// </summary>
	internal sealed class OnnxEmbedder : IDisposable {
		public const int InputSide = 224;
		public const int MaxBatch = 16;
		const int PixelsPerChannel = InputSide * InputSide;

		// ImageNet normalization, the preprocessing DINOv2 was trained with.
		static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
		static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

		readonly InferenceSession session;
		readonly string inputName;
		readonly string outputName;
		readonly bool clsFromHiddenState;

		public OnnxEmbedder(string modelPath) {
			AiComponents.EnsureResolverInstalled();
			var options = new SessionOptions();
			// The embedder shares the machine with the decode workers during hashing;
			// give inference a portion of the cores, not all of them.
			options.IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
			session = new InferenceSession(modelPath, options);
			inputName = session.InputMetadata.Keys.First();
			// DINOv2 exports emit last_hidden_state (CLS token = the image embedding);
			// keep the generic fallbacks so a future model swap keeps working.
			if (session.OutputMetadata.ContainsKey("image_embeds")) outputName = "image_embeds";
			else if (session.OutputMetadata.ContainsKey("pooler_output")) outputName = "pooler_output";
			else { outputName = session.OutputMetadata.Keys.First(); clsFromHiddenState = true; }
		}

		/// <summary>L2-normalized float embeddings, one per input frame (each 224·224·3 RGB24 bytes).</summary>
		internal float[][] EmbedBatch(IReadOnlyList<byte[]> rgbFrames) {
			int batch = rgbFrames.Count;
			if (batch == 0) return Array.Empty<float[]>();
			var tensor = new DenseTensor<float>(new[] { batch, 3, InputSide, InputSide });
			Span<float> buffer = tensor.Buffer.Span;
			for (int k = 0; k < batch; k++) {
				byte[] img = rgbFrames[k];
				if (img.Length != PixelsPerChannel * 3)
					throw new ArgumentException($"Expected {PixelsPerChannel * 3} bytes of RGB24, got {img.Length}.");
				int baseIdx = k * 3 * PixelsPerChannel;
				for (int c = 0; c < 3; c++) {
					float mean = Mean[c] * 255f;
					float invStd = 1f / (Std[c] * 255f);
					int channelBase = baseIdx + c * PixelsPerChannel;
					for (int p = 0; p < PixelsPerChannel; p++)
						buffer[channelBase + p] = (img[p * 3 + c] - mean) * invStd;
				}
			}

			using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
				session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
			var outputTensor = (DenseTensor<float>)results.First(v => v.Name == outputName).AsTensor<float>();
			ReadOnlySpan<int> dims = outputTensor.Dimensions;
			int dim = dims[^1];
			// last_hidden_state is [batch, tokens, dim]; the CLS token (index 0) is the embedding.
			int stride = clsFromHiddenState && dims.Length == 3 ? dims[1] * dim : dim;
			Span<float> output = outputTensor.Buffer.Span;

			var embeddings = new float[batch][];
			for (int k = 0; k < batch; k++) {
				var e = new float[dim];
				output.Slice(k * stride, dim).CopyTo(e);
				Normalize(e);
				embeddings[k] = e;
			}
			return embeddings;
		}

		/// <summary>Embeddings quantized for storage in <see cref="FileEntry.Embeddings"/>.</summary>
		internal byte[][] EmbedBatchQuantized(IReadOnlyList<byte[]> rgbFrames) {
			float[][] floats = EmbedBatch(rgbFrames);
			var quantized = new byte[floats.Length][];
			for (int i = 0; i < floats.Length; i++)
				quantized[i] = EmbeddingMath.QuantizeUnitVector(floats[i]);
			return quantized;
		}

		static void Normalize(float[] v) {
			double sum = 0;
			for (int i = 0; i < v.Length; i++)
				sum += (double)v[i] * v[i];
			float inv = (float)(1.0 / Math.Sqrt(Math.Max(sum, 1e-12)));
			for (int i = 0; i < v.Length; i++)
				v[i] *= inv;
		}

		public void Dispose() => session.Dispose();
	}
}
