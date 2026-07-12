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
using VDF.Core.Utils;

namespace VDF.Core.Tests.AI;

/// <summary>
/// The union semantics of CheckIfDuplicate: the classic verdict stays authoritative,
/// the AI pass may only ADD pairs (flagged via the aiMatched out) — never veto, never
/// fire on dark frames, never run on the flipped orientation.
/// </summary>
public class AiUnionMatchingTests {

	const int GraySize = GrayBytesUtils.Side * GrayBytesUtils.Side;

	static byte[] GrayFrame(byte value) {
		var g = new byte[GraySize];
		Array.Fill(g, value);
		return g;
	}

	static byte[] UnitEmbedding(int hotIndex) {
		var v = new float[EmbeddingMath.Dimensions];
		v[hotIndex] = 1f;
		return EmbeddingMath.QuantizeUnitVector(v);
	}

	static FileEntry Entry(string name, byte grayValue, byte[]? embedding, bool embeddingValid = true) {
		var entry = new FileEntry { Folder = @"D:\media" };
		entry.Path = $@"D:\media\{name}";
		entry.compareGray = new[] { GrayFrame(grayValue) };
		entry.compareEmbeddings = new[] { embedding };
		entry.compareEmbeddingValid = new[] { embeddingValid };
		return entry;
	}

	static ScanEngine Engine(bool useAi = true, float aiPercent = 94f, float percent = 96f) =>
		new() { Settings = { UseAiMatching = useAi, AiPercent = aiPercent, Percent = percent, UsePHashing = false } };

	[Fact]
	public void ClassicFail_AiIdenticalEmbeddings_MatchesAsAiPair() {
		var engine = Engine();
		// Gray values 39% apart — far beyond the 96% threshold — but identical embeddings.
		var a = Entry("a.mp4", 100, UnitEmbedding(0));
		var b = Entry("b.mp4", 200, UnitEmbedding(0));

		Assert.True(engine.CheckIfDuplicate(a, null, null, b, out float difference, out bool aiMatched));
		Assert.True(aiMatched);
		Assert.Equal(0f, difference, 0.03f);
	}

	[Fact]
	public void ClassicFail_AiDissimilarEmbeddings_NoMatch() {
		var engine = Engine();
		var a = Entry("a.mp4", 100, UnitEmbedding(0));
		var b = Entry("b.mp4", 200, UnitEmbedding(1)); // orthogonal

		Assert.False(engine.CheckIfDuplicate(a, null, null, b, out _, out bool aiMatched));
		Assert.False(aiMatched);
	}

	[Fact]
	public void ClassicPass_IsNotFlaggedAsAiMatch() {
		var engine = Engine();
		var a = Entry("a.mp4", 128, UnitEmbedding(0));
		var b = Entry("b.mp4", 128, UnitEmbedding(1)); // AI would say no — classic already said yes

		Assert.True(engine.CheckIfDuplicate(a, null, null, b, out float difference, out bool aiMatched));
		Assert.False(aiMatched);
		Assert.Equal(0f, difference, 0.001f);
	}

	[Fact]
	public void AiDisabled_ClassicFail_NoMatch() {
		var engine = Engine(useAi: false);
		var a = Entry("a.mp4", 100, UnitEmbedding(0));
		var b = Entry("b.mp4", 200, UnitEmbedding(0));

		Assert.False(engine.CheckIfDuplicate(a, null, null, b, out _, out bool aiMatched));
		Assert.False(aiMatched);
	}

	[Fact]
	public void BlackFrameGuard_InvalidPositionAbstains() {
		var engine = Engine();
		// Identical embeddings, but one side's frame is flagged too dark — the AI pass
		// must abstain instead of pairing two unrelated dark scenes.
		var a = Entry("a.mp4", 100, UnitEmbedding(0));
		var b = Entry("b.mp4", 200, UnitEmbedding(0), embeddingValid: false);

		Assert.False(engine.CheckIfDuplicate(a, null, null, b, out _, out bool aiMatched));
		Assert.False(aiMatched);
	}

	[Fact]
	public void MissingEmbedding_Abstains() {
		var engine = Engine();
		var a = Entry("a.mp4", 100, UnitEmbedding(0));
		var b = Entry("b.mp4", 200, null); // never embedded (e.g. decode failure)

		Assert.False(engine.CheckIfDuplicate(a, null, null, b, out _, out bool aiMatched));
		Assert.False(aiMatched);
	}

	[Fact]
	public void FlippedOrientation_SkipsAiPass() {
		var engine = Engine();
		var a = Entry("a.mp4", 100, UnitEmbedding(0));
		var b = Entry("b.mp4", 200, UnitEmbedding(0));

		// Passing overrideGray marks the flipped comparison — identical embeddings must NOT fire there.
		Assert.False(engine.CheckIfDuplicate(a, new[] { GrayFrame(100) }, null, b, out _, out bool aiMatched));
		Assert.False(aiMatched);
	}

	[Fact]
	public void Threshold_IsRespected() {
		// Two embeddings sharing one of two hot components → cosine ≈ 0.5.
		var vA = new float[EmbeddingMath.Dimensions];
		vA[0] = 1f;
		var vB = new float[EmbeddingMath.Dimensions];
		vB[0] = vB[1] = 0.7071f;
		byte[] qa = EmbeddingMath.QuantizeUnitVector(vA);
		byte[] qb = EmbeddingMath.QuantizeUnitVector(vB);

		var strict = Engine(aiPercent: 94f);
		Assert.False(strict.CheckIfDuplicate(Entry("a.mp4", 100, qa), null, null, Entry("b.mp4", 200, qb), out _, out _));

		var loose = Engine(aiPercent: 50f);
		Assert.True(loose.CheckIfDuplicate(Entry("a.mp4", 100, qa), null, null, Entry("b.mp4", 200, qb), out _, out bool aiMatched));
		Assert.True(aiMatched);
	}

	[Fact]
	public void ComputeAiSimilarity_AveragesOnlyValidPositions() {
		var a = new FileEntry { Folder = @"D:\media" };
		a.Path = @"D:\media\a.mp4";
		var b = new FileEntry { Folder = @"D:\media" };
		b.Path = @"D:\media\b.mp4";

		byte[] same = UnitEmbedding(0);
		byte[] other = UnitEmbedding(1);
		// Position 0 identical, position 1 orthogonal-but-dark (must be ignored).
		a.compareEmbeddings = new[] { same, same };
		b.compareEmbeddings = new[] { same, other };
		a.compareEmbeddingValid = new[] { true, false };
		b.compareEmbeddingValid = new[] { true, true };

		Assert.Equal(1f, ScanEngine.ComputeAiSimilarity(a, b), 0.02f);
	}

	[Fact]
	public void ComputeAiSimilarity_AbstainsWhenMostPositionsAreInvalid() {
		// The union verdict must not rest on a sliver of the sampled positions: with
		// 4 positions and only 1 valid pair (e.g. a mostly-dark file), one coincidental
		// bright frame pair must not decide the whole file.
		var a = new FileEntry { Folder = @"D:\media" };
		a.Path = @"D:\media\a.mp4";
		var b = new FileEntry { Folder = @"D:\media" };
		b.Path = @"D:\media\b.mp4";

		byte[] same = UnitEmbedding(0);
		a.compareEmbeddings = new[] { same, same, same, same };
		b.compareEmbeddings = new[] { same, same, same, same };
		a.compareEmbeddingValid = new[] { true, false, false, false };
		b.compareEmbeddingValid = new[] { true, true, true, true };

		Assert.Equal(-1f, ScanEngine.ComputeAiSimilarity(a, b));

		// Two of four valid positions meet the quorum.
		a.compareEmbeddingValid = new[] { true, true, false, false };
		Assert.Equal(1f, ScanEngine.ComputeAiSimilarity(a, b), 0.02f);
	}

	[Fact]
	public void TryBuildCompareSnapshot_PopulatesAlignedEmbeddings_AndToleratesMissing() {
		var engine = Engine();
		engine.positionList.Add(0.5f);

		var entry = new FileEntry {
			Folder = @"D:\media",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(100), Streams = Array.Empty<MediaInfo.StreamInfo>() },
		};
		entry.Path = @"D:\media\snap.mp4";
		double key = entry.GetGrayBytesIndex(0.5f, 0);
		entry.grayBytes[key] = GrayFrame(128);
		var store = new UnionEmbeddingStore();
		store.Put(entry, key, UnitEmbedding(0));
		engine.unionEmbeddingStore = store;

		Assert.True(engine.TryBuildCompareSnapshot(entry, usePHashing: false));
		Assert.NotNull(entry.compareEmbeddings);
		Assert.Equal(UnitEmbedding(0), entry.compareEmbeddings![0]);
		Assert.True(entry.compareEmbeddingValid![0]);

		// Missing embedding must not drop the entry from the comparison.
		var noEmbedding = new FileEntry {
			Folder = @"D:\media",
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(100), Streams = Array.Empty<MediaInfo.StreamInfo>() },
		};
		noEmbedding.Path = @"D:\media\bare.mp4";
		noEmbedding.grayBytes[noEmbedding.GetGrayBytesIndex(0.5f, 0)] = GrayFrame(128);

		Assert.True(engine.TryBuildCompareSnapshot(noEmbedding, usePHashing: false));
		Assert.Null(noEmbedding.compareEmbeddings![0]);
	}
}
