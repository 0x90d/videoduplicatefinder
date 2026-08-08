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

using System.Collections.Concurrent;

namespace VDF.Core.AI {
	/// <summary>
	/// Pool of exact-size 224x224 RGB24 frame buffers (150,528 bytes - above the 85 KB
	/// large-object threshold). Both AI passes move one such buffer per sampled frame,
	/// and allocating them fresh churned the Large Object Heap at hundreds of MB per
	/// second, ballooning the process to several times its live set (#878; measured
	/// 4.4 GB peak for 8 dense-sweep workers where pooled streaming needs ~50 MB).
	/// Buffers are exact-size on purpose - ArrayPool's rounded-up arrays would break
	/// every Length-dependent consumer (dark-frame check, duplicate compare).
	/// Rented buffers come back with stale content; renters must fill them fully.
	/// Returning a foreign array is safe: wrong sizes are ignored, exact-size ones
	/// simply join the pool. Production code uses <see cref="Shared"/>.
	/// </summary>
	internal sealed class FramePool {
		public const int FrameBytes = OnnxEmbedder.InputSide * OnnxEmbedder.InputSide * 3;

		public static readonly FramePool Shared = new();

		readonly ConcurrentBag<byte[]> pool = new();
		readonly int maxPooled;
		int pooledCount;

		// ~79 MB default cap. Worst-case in-flight: the union pipeline's bounded queue
		// (256) plus a batch per dense worker; beyond the cap, returns fall to the GC.
		internal FramePool(int maxPooled = 512) => this.maxPooled = maxPooled;

		/// <summary>Number of buffers currently held by the pool (test/diagnostic aid).</summary>
		internal int PooledCount => Volatile.Read(ref pooledCount);

		public byte[] Rent() {
			if (pool.TryTake(out byte[]? buffer)) {
				Interlocked.Decrement(ref pooledCount);
				return buffer;
			}
			return new byte[FrameBytes];
		}

		public void Return(byte[]? buffer) {
			if (buffer == null || buffer.Length != FrameBytes)
				return; // not one of ours (or a padded foreign array) - let the GC have it
			if (Interlocked.Increment(ref pooledCount) > maxPooled) {
				Interlocked.Decrement(ref pooledCount);
				return;
			}
			pool.Add(buffer);
		}
	}
}
