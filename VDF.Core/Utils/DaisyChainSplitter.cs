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
using System.Numerics;

namespace VDF.Core.Utils {

	/// <summary>
	/// Pairwise similarity of one duplicate group, stored as the upper triangle of an
	/// n x n bit matrix: pair (i, j) with i &lt; j lives in row i at bit (j - i - 1). Every
	/// row starts on its own 64-bit word so the rows can be filled by different threads
	/// without touching a shared word.
	/// <para>
	/// Replaces the <c>bool[n, n]</c> the daisy-chain validation used to allocate: that
	/// costs n² bytes, and the NativeAOT runtime the release builds ship on refuses any
	/// multi-dimensional array above 2³¹ elements outright (Array.NativeAot.cs,
	/// NewMultiDimArray) - a group of 46,341+ members threw OutOfMemoryException no
	/// matter how much RAM the machine had (#901, a 320k-image library whose images
	/// were broadly similar at the configured threshold, on a 1 TB node). The triangle
	/// costs n²/16 bytes: 132 MB for that group instead of the 2.1 GB it could not have.
	/// </para>
	/// </summary>
	internal sealed class PairBitMatrix {
		readonly ulong[] words;
		readonly long[] rowStart; // word offset of row i (rows 0 .. n-2)
		public int Count { get; }

		PairBitMatrix(int n, ulong[] words, long[] rowStart) {
			Count = n;
			this.words = words;
			this.rowStart = rowStart;
		}

		static long WordsForRow(int n, int i) => ((long)(n - 1 - i) + 63) / 64;

		/// <summary>Total words the triangle for <paramref name="n"/> members needs.</summary>
		public static long WordCount(int n) {
			long total = 0;
			for (int i = 0; i < n - 1; i++)
				total += WordsForRow(n, i);
			return total;
		}

		/// <summary>Bytes the matrix for <paramref name="n"/> members would occupy.</summary>
		public static long EstimateBytes(int n) => WordCount(n) * sizeof(ulong);

		/// <summary>
		/// Whether the matrix can be represented as a single managed array at all
		/// (element count below <see cref="Array.MaxLength"/>); false from roughly
		/// 520,000 members upward.
		/// </summary>
		public static bool CanAllocate(int n) => WordCount(n) <= Array.MaxLength;

		/// <summary>
		/// Builds the matrix by asking <paramref name="isSimilar"/> for every pair once.
		/// Rows are filled in parallel; <paramref name="onRowDone"/> (if given) runs once
		/// per completed row, from the worker threads, for progress reporting.
		/// </summary>
		public static PairBitMatrix Build(int n, Func<int, int, bool> isSimilar, int parallelism, CancellationToken cancellationToken, Action? onRowDone = null) {
			if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
			var rowStart = new long[Math.Max(0, n - 1)];
			long total = 0;
			for (int i = 0; i < n - 1; i++) {
				rowStart[i] = total;
				total += WordsForRow(n, i);
			}
			if (total > Array.MaxLength)
				throw new OutOfMemoryException($"A pair matrix for {n:N0} members needs {total:N0} words, more than a single array can hold.");
			var words = new ulong[total];
			var matrix = new PairBitMatrix(n, words, rowStart);
			if (n < 2) return matrix;

			// Row i has n-1-i pairs: the early rows are the long ones. Static
			// range partitioning would hand one worker all the long rows, so let the
			// scheduler hand out rows dynamically (the default Parallel.For partitioner
			// does, in small chunks).
			var options = new ParallelOptions {
				CancellationToken = cancellationToken,
				MaxDegreeOfParallelism = parallelism > 0 ? parallelism : -1,
			};
			Parallel.For(0, n - 1, options, i => {
				long start = rowStart[i];
				for (int j = i + 1; j < n; j++) {
					if (isSimilar(i, j)) {
						int bit = j - i - 1;
						words[start + (bit >> 6)] |= 1UL << (bit & 63);
					}
				}
				onRowDone?.Invoke();
			});
			return matrix;
		}

		/// <summary>Whether members <paramref name="a"/> and <paramref name="b"/> are similar (a member is not similar to itself).</summary>
		public bool this[int a, int b] {
			get {
				if (a == b) return false;
				if (a > b) (a, b) = (b, a);
				int bit = b - a - 1;
				return (words[rowStart[a] + (bit >> 6)] & (1UL << (bit & 63))) != 0;
			}
		}

		/// <summary>
		/// Number of similar partners of every member, across the whole group.
		/// Row bits are popcounted; the column half (partners with a smaller index)
		/// is walked bit by bit, rows in parallel.
		/// </summary>
		public int[] Degrees(int parallelism, CancellationToken cancellationToken) {
			int n = Count;
			var degree = new int[n];
			if (n < 2) return degree;
			var options = new ParallelOptions {
				CancellationToken = cancellationToken,
				MaxDegreeOfParallelism = parallelism > 0 ? parallelism : -1,
			};
			Parallel.For(0, n, options, i => {
				int d = 0;
				if (i < n - 1) {
					long start = rowStart[i];
					long count = WordsForRow(n, i);
					for (long w = 0; w < count; w++)
						d += BitOperations.PopCount(words[start + w]);
				}
				for (int j = 0; j < i; j++)
					if (this[j, i]) d++;
				degree[i] = d;
			});
			return degree;
		}
	}

	/// <summary>Outcome of validating one duplicate group (member indices 0 .. n-1).</summary>
	internal sealed class DaisyChainSplitResult {
		/// <summary>The group was too large for its pair matrix and was left exactly as found.</summary>
		public bool Skipped { get; init; }
		/// <summary>The validation pruned at least one member; false means the group stays as it was.</summary>
		public bool Changed { get; init; }
		/// <summary>Bytes the pair matrix needed (or would have needed).</summary>
		public long MatrixBytes { get; init; }
		/// <summary>The groups to keep, each with at least two members. Unchanged groups report their single original group here.</summary>
		public List<int[]> Groups { get; init; } = new();
		/// <summary>Members that fit in no group and leave the duplicate list.</summary>
		public List<int> Removed { get; init; } = new();
	}

	/// <summary>
	/// Breaks apart "daisy chains": groups whose transitive merging collected members
	/// that are not actually similar to each other. Builds the pairwise similarity
	/// graph, iteratively prunes the member similar to fewer than half of the others,
	/// then re-clusters the pruned members among themselves (connected components,
	/// each majority-pruned in turn). Members that fit nowhere are removed.
	/// </summary>
	internal static class DaisyChainSplitter {

		/// <summary>
		/// Default ceiling for one group's pair matrix: 1 GiB (about 131,000 members)
		/// or an eighth of the memory available to the process, whichever is larger.
		/// Groups above it are reported <see cref="DaisyChainSplitResult.Skipped"/>
		/// rather than validated - a group that size means the threshold admits most of
		/// the library, and the validation would cost as much as the scan itself.
		/// </summary>
		public static long DefaultMatrixBudgetBytes {
			get {
				const long floor = 1L << 30;
				long available;
				try { available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; }
				catch { available = 0; }
				return Math.Max(floor, available / 8);
			}
		}

		public static DaisyChainSplitResult Split(int n, Func<int, int, bool> isSimilar, int parallelism, long matrixBudgetBytes, CancellationToken cancellationToken, Action? onRowDone = null) {
			if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
			long bytes = PairBitMatrix.EstimateBytes(n);
			if (bytes > matrixBudgetBytes || !PairBitMatrix.CanAllocate(n))
				return new DaisyChainSplitResult { Skipped = true, MatrixBytes = bytes, Groups = { Enumerable.Range(0, n).ToArray() } };

			var matrix = PairBitMatrix.Build(n, isSimilar, parallelism, cancellationToken, onRowDone);
			int[] degrees = matrix.Degrees(parallelism, cancellationToken);

			var all = new int[n];
			for (int i = 0; i < n; i++) all[i] = i;
			var (core, pruned) = PruneByMajority(all, degrees, matrix, cancellationToken);

			if (pruned.Count == 0)
				return new DaisyChainSplitResult { Changed = false, MatrixBytes = bytes, Groups = { all } };

			var result = new DaisyChainSplitResult { Changed = true, MatrixBytes = bytes };
			if (core.Count >= 2)
				result.Groups.Add(core.ToArray());
			else
				result.Removed.AddRange(core); // core collapsed to a single item

			// Re-cluster the pruned members among themselves: connected components of
			// the similarity graph restricted to the pruned set, each validated with the
			// same majority rule. Pruned-again members fit nowhere and are removed.
			var isPruned = new bool[n];
			foreach (int p in pruned) isPruned[p] = true;
			var visited = new bool[n];
			var queue = new Queue<int>();
			foreach (int seed in pruned) {
				if (visited[seed]) continue;
				cancellationToken.ThrowIfCancellationRequested();
				var component = new List<int>();
				queue.Enqueue(seed);
				visited[seed] = true;
				while (queue.Count > 0) {
					int cur = queue.Dequeue();
					component.Add(cur);
					foreach (int other in pruned) {
						if (!visited[other] && matrix[cur, other]) {
							visited[other] = true;
							queue.Enqueue(other);
						}
					}
				}

				if (component.Count < 2) {
					result.Removed.Add(component[0]);
					continue;
				}

				var subDegrees = new int[component.Count];
				for (int a = 0; a < component.Count; a++)
					for (int b = a + 1; b < component.Count; b++)
						if (matrix[component[a], component[b]]) { subDegrees[a]++; subDegrees[b]++; }
				var (kept, dropped) = PruneByMajority(component.ToArray(), subDegrees, matrix, cancellationToken);
				result.Removed.AddRange(dropped);
				if (kept.Count >= 2)
					result.Groups.Add(kept.ToArray());
				else
					result.Removed.AddRange(kept);
			}
			return result;
		}

		/// <summary>
		/// Repeatedly removes the least-connected member (first one in <paramref name="nodes"/>
		/// order on ties) while it is similar to fewer than half of the other remaining
		/// members. <paramref name="degrees"/> holds each node's similar-partner count
		/// within <paramref name="nodes"/> and is updated in place as members leave, so the
		/// whole pass costs O(n²) instead of the O(n³) a recount per round would.
		/// </summary>
		static (List<int> kept, List<int> pruned) PruneByMajority(int[] nodes, int[] degrees, PairBitMatrix matrix, CancellationToken cancellationToken) {
			int count = nodes.Length;
			var alive = new bool[count];
			Array.Fill(alive, true);
			int aliveCount = count;
			var pruned = new List<int>();

			while (aliveCount >= 2) {
				int worst = -1;
				int worstDegree = int.MaxValue;
				for (int k = 0; k < count; k++) {
					if (alive[k] && degrees[k] < worstDegree) {
						worstDegree = degrees[k];
						worst = k;
					}
				}
				// Required: similar to at least half of the OTHER remaining members,
				// i.e. ceil((aliveCount - 1) / 2) == aliveCount / 2.
				int required = aliveCount / 2;
				if (worstDegree >= required)
					break;
				cancellationToken.ThrowIfCancellationRequested();
				alive[worst] = false;
				aliveCount--;
				pruned.Add(nodes[worst]);
				int removed = nodes[worst];
				for (int k = 0; k < count; k++)
					if (alive[k] && matrix[removed, nodes[k]])
						degrees[k]--;
			}

			var kept = new List<int>(aliveCount);
			for (int k = 0; k < count; k++)
				if (alive[k]) kept.Add(nodes[k]);
			return (kept, pruned);
		}
	}
}
