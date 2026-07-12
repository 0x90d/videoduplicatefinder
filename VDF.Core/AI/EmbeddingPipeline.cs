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

using VDF.Core.Utils;

namespace VDF.Core.AI {
	/// <summary>
	/// Receives decoded 224×224 RGB24 frames from the hashing phase. Implemented by
	/// <see cref="EmbeddingPipeline"/>; the indirection keeps FfmpegEngine free of any
	/// ONNX Runtime dependency (and lets tests inject a sink without native components).
	/// </summary>
	interface IEmbeddingFrameSink {
		/// <summary>Whether the entry still needs an embedding for this position key.</summary>
		bool WantsEmbedding(FileEntry entry, double positionKey);
		/// <summary>Hands a decoded frame over for embedding. Never throws for data reasons.</summary>
		void SubmitFrame(FileEntry entry, double positionKey, byte[] rgb224);
	}

	/// <summary>
	/// Bounded producer/consumer stage between the (parallel, I/O-bound) frame decoders
	/// and the (serial, CPU-bound) ONNX inference: decode workers submit RGB frames, one
	/// worker thread batches them through <see cref="OnnxEmbedder"/> and writes int8
	/// embeddings into <see cref="FileEntry.Embeddings"/>. On an inference failure the
	/// pipeline faults: remaining frames are drained and discarded (so producers never
	/// block on the bounded queue) and affected entries simply stay without embeddings —
	/// the AI pass abstains for them.
	/// </summary>
	sealed class EmbeddingPipeline : IEmbeddingFrameSink, IDisposable {
		readonly BlockingCollection<(FileEntry entry, double key, byte[] rgb)> queue = new(boundedCapacity: 256);
		readonly OnnxEmbedder embedder;
		readonly CancellationToken token;
		readonly Task worker;
		volatile bool faulted;
		int embeddedCount;

		public EmbeddingPipeline(string modelPath, CancellationToken token) {
			this.token = token;
			embedder = new OnnxEmbedder(modelPath);
			worker = Task.Run(WorkerLoop, CancellationToken.None);
		}

		public int EmbeddedCount => embeddedCount;
		public bool Faulted => faulted;

		public bool WantsEmbedding(FileEntry entry, double positionKey) =>
			!faulted && !entry.Embeddings.ContainsKey(positionKey);

		public void SubmitFrame(FileEntry entry, double positionKey, byte[] rgb224) {
			if (faulted) return;
			try {
				queue.Add((entry, positionKey, rgb224), token);
			}
			catch (OperationCanceledException) { }
			catch (InvalidOperationException) { /* completed while a decoder was mid-submit */ }
		}

		/// <summary>No more frames will arrive; returns when everything queued is embedded.</summary>
		public Task CompleteAsync() {
			queue.CompleteAdding();
			return worker;
		}

		void WorkerLoop() {
			var batchEntries = new List<(FileEntry entry, double key)>(OnnxEmbedder.MaxBatch);
			var batchFrames = new List<byte[]>(OnnxEmbedder.MaxBatch);
			try {
				while (!queue.IsCompleted) {
					batchEntries.Clear();
					batchFrames.Clear();
					(FileEntry entry, double key, byte[] rgb) item;
					try {
						item = queue.Take(token);
					}
					catch (OperationCanceledException) { break; }
					catch (InvalidOperationException) { break; } // completed and empty
					batchEntries.Add((item.entry, item.key));
					batchFrames.Add(item.rgb);
					while (batchFrames.Count < OnnxEmbedder.MaxBatch && queue.TryTake(out item)) {
						batchEntries.Add((item.entry, item.key));
						batchFrames.Add(item.rgb);
					}

					if (faulted) continue; // keep draining, discard

					byte[][] embeddings = embedder.EmbedBatchQuantized(batchFrames);
					for (int i = 0; i < embeddings.Length; i++) {
						batchEntries[i].entry.Embeddings.TryAdd(batchEntries[i].key, embeddings[i]);
						Interlocked.Increment(ref embeddedCount);
					}
				}
			}
			catch (Exception e) {
				// One inference failure must not tear down the scan: fault, then keep
				// draining so bounded Add in the decode workers never blocks forever.
				faulted = true;
				Logger.Instance.Error($"AI embedding stage failed — continuing scan without AI matching for the remaining files: {e}");
				try {
					while (queue.TryTake(out _, Timeout.Infinite)) { }
				}
				catch (InvalidOperationException) { /* completed and empty — done */ }
			}
		}

		public void Dispose() {
			queue.CompleteAdding();
			try { worker.Wait(TimeSpan.FromSeconds(30)); } catch { }
			embedder.Dispose();
			queue.Dispose();
		}
	}
}
