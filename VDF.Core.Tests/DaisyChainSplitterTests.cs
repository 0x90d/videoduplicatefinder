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
// #901: the daisy-chain validation allocated a bool[n, n] per duplicate group. A
// 320k-image library compared at 80% produced a group of 46,341+ members, for which
// that array exceeds 2^31 elements; the NativeAOT runtime of the release builds refuses
// such a multi-dimensional array outright (CoreCLR would have allocated the 2 GB), so
// the scan aborted with OutOfMemoryException on a 1 TB machine. The validation now runs
// on a bit-packed pair matrix (DaisyChainSplitter). These tests pin (1) that the new
// code gives exactly the old algorithm's answer, using a port of the old bool[,] code
// as the oracle, (2) the size that used to crash, (3) the budget/cancel/progress seams.

using VDF.Core.Utils;

namespace VDF.Core.Tests;

public class DaisyChainSplitterTests {

	// ---- Oracle: the pre-#901 algorithm verbatim, on a bool[,] ----------------------

	static (List<HashSet<int>> groups, HashSet<int> removed, bool changed) ReferenceSplit(bool[,] similar) {
		int n = similar.GetLength(0);
		var groups = new List<HashSet<int>>();
		var removed = new HashSet<int>();

		var active = new List<int>(Enumerable.Range(0, n));
		var pruned = new List<int>();
		bool changed = true;
		while (changed && active.Count >= 2) {
			changed = false;
			int worstIdx = -1;
			int worstConnections = int.MaxValue;
			for (int ai = 0; ai < active.Count; ai++) {
				int idx = active[ai];
				int connections = 0;
				for (int aj = 0; aj < active.Count; aj++)
					if (ai != aj && similar[idx, active[aj]]) connections++;
				if (connections < worstConnections) { worstConnections = connections; worstIdx = ai; }
			}
			int requiredConnections = (active.Count - 1 + 1) / 2;
			if (worstConnections < requiredConnections) {
				pruned.Add(active[worstIdx]);
				active.RemoveAt(worstIdx);
				changed = true;
			}
		}
		if (pruned.Count == 0)
			return (new List<HashSet<int>> { new(Enumerable.Range(0, n)) }, removed, false);

		if (active.Count >= 2) groups.Add(new HashSet<int>(active));
		else foreach (int idx in active) removed.Add(idx);

		var visited = new HashSet<int>();
		foreach (int seed in pruned) {
			if (visited.Contains(seed)) continue;
			var component = new List<int>();
			var queue = new Queue<int>();
			queue.Enqueue(seed);
			visited.Add(seed);
			while (queue.Count > 0) {
				int cur = queue.Dequeue();
				component.Add(cur);
				foreach (int other in pruned)
					if (!visited.Contains(other) && similar[cur, other]) { visited.Add(other); queue.Enqueue(other); }
			}
			if (component.Count >= 2) {
				var subActive = new List<int>(component);
				bool subChanged = true;
				while (subChanged && subActive.Count >= 2) {
					subChanged = false;
					int subWorstIdx = -1, subWorstConn = int.MaxValue;
					for (int ai = 0; ai < subActive.Count; ai++) {
						int idx = subActive[ai];
						int conn = 0;
						for (int aj = 0; aj < subActive.Count; aj++)
							if (ai != aj && similar[idx, subActive[aj]]) conn++;
						if (conn < subWorstConn) { subWorstConn = conn; subWorstIdx = ai; }
					}
					int subRequired = (subActive.Count - 1 + 1) / 2;
					if (subWorstConn < subRequired) {
						removed.Add(subActive[subWorstIdx]);
						subActive.RemoveAt(subWorstIdx);
						subChanged = true;
					}
				}
				if (subActive.Count >= 2) groups.Add(new HashSet<int>(subActive));
				else foreach (int idx in subActive) removed.Add(idx);
			}
			else
				removed.Add(component[0]);
		}
		return (groups, removed, true);
	}

	static bool[,] RandomGraph(Random rng, int n, double density) {
		var m = new bool[n, n];
		for (int i = 0; i < n; i++) {
			m[i, i] = true;
			for (int j = i + 1; j < n; j++)
				m[i, j] = m[j, i] = rng.NextDouble() < density;
		}
		return m;
	}

	/// <summary>Two members each appearing in exactly one group, plus a dense clique and a chain of cliques: the shapes the validation exists for.</summary>
	static bool[,] StructuredGraph(Random rng, int n) {
		var m = new bool[n, n];
		int cliques = Math.Max(1, rng.Next(1, 5));
		int size = Math.Max(1, n / cliques);
		for (int i = 0; i < n; i++) {
			m[i, i] = true;
			for (int j = i + 1; j < n; j++) {
				bool sameClique = i / size == j / size;
				bool bridge = j == i + 1 && !sameClique && rng.NextDouble() < 0.7;
				bool noise = rng.NextDouble() < 0.03;
				m[i, j] = m[j, i] = (sameClique && rng.NextDouble() < 0.9) || bridge || noise;
			}
		}
		return m;
	}

	static void AssertSameOutcome(bool[,] similar, int parallelism) {
		int n = similar.GetLength(0);
		var (refGroups, refRemoved, refChanged) = ReferenceSplit(similar);
		var result = DaisyChainSplitter.Split(n, (i, j) => similar[i, j], parallelism, long.MaxValue, CancellationToken.None);

		Assert.False(result.Skipped);
		Assert.Equal(refChanged, result.Changed);
		Assert.Equal(refRemoved, result.Removed.ToHashSet());
		var actualGroups = result.Groups.Select(g => g.ToHashSet()).ToList();
		Assert.Equal(refGroups.Count, actualGroups.Count);
		foreach (var g in refGroups)
			Assert.Contains(actualGroups, a => a.SetEquals(g));
		// Every member lands in exactly one place.
		var all = result.Groups.SelectMany(g => g).Concat(result.Removed).ToList();
		Assert.Equal(n, all.Count);
		Assert.Equal(n, all.Distinct().Count());
	}

	[Theory]
	[InlineData(1, 0.1)]
	[InlineData(2, 0.3)]
	[InlineData(3, 0.5)]
	[InlineData(4, 0.7)]
	[InlineData(5, 0.9)]
	public void MatchesReferenceAlgorithm_OnRandomGraphs(int seedBase, double density) {
		for (int seed = 0; seed < 120; seed++) {
			var rng = new Random(seedBase * 1000 + seed);
			int n = rng.Next(3, 48);
			AssertSameOutcome(RandomGraph(rng, n, density), parallelism: 1 + seed % 4);
		}
	}

	[Fact]
	public void MatchesReferenceAlgorithm_OnCliqueChains() {
		for (int seed = 0; seed < 300; seed++) {
			var rng = new Random(7000 + seed);
			int n = rng.Next(3, 90);
			AssertSameOutcome(StructuredGraph(rng, n), parallelism: 1 + seed % 3);
		}
	}

	[Fact]
	public void MatchesReferenceAlgorithm_AcrossWordBoundaries() {
		// Rows longer than 64/128 bits exercise the multi-word row layout.
		for (int seed = 0; seed < 20; seed++) {
			var rng = new Random(9000 + seed);
			AssertSameOutcome(RandomGraph(rng, 64 + rng.Next(0, 140), 0.5), parallelism: 4);
		}
	}

	[Fact]
	public void Star_PrunesLeavesAndKeepsCoreOfThree() {
		// Hub 0 similar to everyone, leaves similar to nothing else: the simplest chain.
		const int n = 4;
		var m = new bool[n, n];
		for (int i = 0; i < n; i++) { m[i, i] = true; m[0, i] = m[i, 0] = true; }
		var result = DaisyChainSplitter.Split(n, (i, j) => m[i, j], 2, long.MaxValue, CancellationToken.None);
		Assert.True(result.Changed);
		Assert.Single(result.Groups);
		Assert.Equal(new[] { 0, 2, 3 }, result.Groups[0]);
		Assert.Equal(new[] { 1 }, result.Removed);
	}

	[Fact]
	public void FullClique_IsUnchanged() {
		var result = DaisyChainSplitter.Split(5, (i, j) => true, 2, long.MaxValue, CancellationToken.None);
		Assert.False(result.Changed);
		Assert.False(result.Skipped);
		Assert.Equal(new[] { 0, 1, 2, 3, 4 }, Assert.Single(result.Groups));
		Assert.Empty(result.Removed);
	}

	// ---- #901: the size that used to throw -------------------------------------------

	[Fact]
	public void GroupBeyondMultiDimArrayLimit_IsValidatedWithoutAllocatingNSquared() {
		// 46,341² > int.MaxValue: `new bool[n, n]` threw OutOfMemoryException here on
		// the AOT release builds regardless of RAM. The bit triangle needs ~134 MB.
		const int n = 46_341;
		long bytes = PairBitMatrix.EstimateBytes(n);
		Assert.InRange(bytes, 120L << 20, 140L << 20);
		Assert.True(PairBitMatrix.CanAllocate(n));

		int rows = 0;
		var result = DaisyChainSplitter.Split(n, (i, j) => true, Environment.ProcessorCount, long.MaxValue, CancellationToken.None,
			onRowDone: () => Interlocked.Increment(ref rows));
		Assert.False(result.Skipped);
		Assert.False(result.Changed);
		Assert.Equal(n, Assert.Single(result.Groups).Length);
		Assert.Equal(n - 1, rows);
	}

	[Fact]
	public void PairBitMatrix_StoresEveryPairOnce() {
		const int n = 203; // rows spanning 1..4 words
		var rng = new Random(42);
		var expected = RandomGraph(rng, n, 0.4);
		var m = PairBitMatrix.Build(n, (i, j) => expected[i, j], 3, CancellationToken.None);
		for (int i = 0; i < n; i++)
			for (int j = 0; j < n; j++)
				Assert.Equal(i != j && expected[i, j], m[i, j]);
		var degrees = m.Degrees(3, CancellationToken.None);
		for (int i = 0; i < n; i++)
			Assert.Equal(Enumerable.Range(0, n).Count(j => j != i && expected[i, j]), degrees[i]);
	}

	[Fact]
	public void PairBitMatrix_SizeEstimateIsTriangular() {
		Assert.Equal(0, PairBitMatrix.EstimateBytes(0));
		Assert.Equal(0, PairBitMatrix.EstimateBytes(1));
		Assert.Equal(8, PairBitMatrix.EstimateBytes(2));      // one row, one word
		Assert.Equal(8 * 64, PairBitMatrix.EstimateBytes(65)); // 64 rows of one word each
		Assert.Equal(8 * 66, PairBitMatrix.EstimateBytes(66)); // 65 rows, row 0 now needs two words
		Assert.True(PairBitMatrix.CanAllocate(131_000));
		Assert.False(PairBitMatrix.CanAllocate(600_000));
	}

	// ---- Budget, cancellation, progress --------------------------------------------

	[Fact]
	public void OverBudget_SkipsValidationAndKeepsGroupIntact() {
		int calls = 0;
		var result = DaisyChainSplitter.Split(10, (i, j) => { calls++; return false; }, 1, matrixBudgetBytes: 0, CancellationToken.None);
		Assert.True(result.Skipped);
		Assert.False(result.Changed);
		Assert.Equal(0, calls);
		Assert.Equal(Enumerable.Range(0, 10).ToArray(), Assert.Single(result.Groups));
		Assert.Empty(result.Removed);
		Assert.True(result.MatrixBytes > 0);
	}

	[Fact]
	public void DefaultBudget_CoversTheReportedGroupAndHasAFloor() {
		Assert.True(DaisyChainSplitter.DefaultMatrixBudgetBytes >= 1L << 30);
		Assert.True(PairBitMatrix.EstimateBytes(46_341) < DaisyChainSplitter.DefaultMatrixBudgetBytes);
		Assert.True(PairBitMatrix.EstimateBytes(131_000) < DaisyChainSplitter.DefaultMatrixBudgetBytes);
	}

	[Fact]
	public void CancelledToken_StopsTheFill() {
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		Assert.Throws<OperationCanceledException>(() =>
			DaisyChainSplitter.Split(50, (i, j) => true, 2, long.MaxValue, cts.Token));
	}

	[Fact]
	public void ProgressCallback_FiresOncePerRow() {
		int rows = 0;
		DaisyChainSplitter.Split(37, (i, j) => (i + j) % 3 == 0, 4, long.MaxValue, CancellationToken.None, () => Interlocked.Increment(ref rows));
		Assert.Equal(36, rows);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public void TinyGroups_AreUnchanged(int n) {
		var result = DaisyChainSplitter.Split(n, (i, j) => false, 2, long.MaxValue, CancellationToken.None);
		Assert.False(result.Changed);
		Assert.Equal(n, Assert.Single(result.Groups).Length);
	}
}
