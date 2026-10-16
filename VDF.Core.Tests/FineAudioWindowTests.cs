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

using VDF.Core.Chromaprint;

namespace VDF.Core.Tests;

public class FineAudioWindowTests {
	static uint[] RandomWords(int length, int seed) {
		var random = new Random(seed);
		return Enumerable.Range(0, length).Select(_ => (uint)random.NextInt64(0, 1L << 32)).ToArray();
	}

	[Theory]
	[InlineData(79)]
	[InlineData(80)]
	[InlineData(81)]
	[InlineData(82)]
	[InlineData(83)]
	[InlineData(84)]
	[InlineData(85)]
	[InlineData(86)]
	public void ExactSubset_AllSourcePhasesAndTailRemainders_KeepCorrectOffset(int length) {
		uint[] clip = RandomWords(length, 935);
		for (int phase = 0; phase < 7; phase++) {
			uint[] source = RandomWords(length + 30, 1735);
			int offset = 7 + phase;
			clip.CopyTo(source, offset);
			var found = ScanEngine.CompareFineAudioWindows(clip, source, .8f);
			Assert.Equal(1f, found.similarity);
			Assert.Equal(offset, found.offsetBlocks);
		}
	}

	[Fact]
	public void PartialFinalWindow_IsScoredRatherThanIgnored() {
		uint[] clip = RandomWords(82, 935), source = RandomWords(190, 1735);
		clip.CopyTo(source, 0);
		source[clip.Length - 1] ^= uint.MaxValue;
		clip.CopyTo(source, 100);
		var found = ScanEngine.CompareFineAudioWindows(clip, source);
		Assert.Equal(1f, found.similarity);
		Assert.Equal(100, found.offsetBlocks);
	}

	[Fact]
	public void SampledPrefixMatch_CannotExtendPastTheSource() {
		uint[] clip = RandomWords(83, 935), source = RandomWords(100, 1735);
		for (int i = 0; i < clip.Length; i += 7) source[20 + i] = clip[i];
		var found = ScanEngine.CompareFineAudioWindows(clip, source);
		Assert.True(found.similarity < .8f);
		Assert.InRange(found.offsetBlocks, 0, source.Length - clip.Length);
	}

	[Fact]
	public void EqualScoresAcrossPhases_ChooseEarliestOffset() {
		uint[] clip = Enumerable.Repeat(0x12345678u, 82).ToArray();
		uint[] source = Enumerable.Repeat(0x12345678u, 120).ToArray();
		Array.Fill(source, uint.MaxValue, 0, 3);
		Assert.Equal(3, ScanEngine.CompareFineAudioWindows(clip, source).offsetBlocks);
	}

	[Fact]
	public void DiagnosticBelowThreshold_RetainsBestScoreAndOffset() {
		uint[] clip = RandomWords(82, 935), source = RandomWords(120, 1735);
		clip.CopyTo(source, 13);
		for (int i = 13; i < 13 + clip.Length; i++) source[i] ^= 1;
		var actual = ScanEngine.CompareFineAudioWindows(clip, source);
		Assert.Equal(31f / 32, actual.similarity);
		Assert.Equal(13, actual.offsetBlocks);
		Assert.Equal(0, ScanEngine.CompareFineAudioWindows(clip, source, .99f).similarity);
	}

	[Fact]
	public void EqualNonperfectScoresAcrossPhases_ChooseEarliestOffset() {
		uint[] clip = RandomWords(82, 935), source = RandomWords(210, 1735);
		clip.CopyTo(source, 3); clip.CopyTo(source, 100);
		source[3 + clip.Length - 1] ^= 1;
		source[100 + clip.Length - 1] ^= 1;
		var found = ScanEngine.CompareFineAudioWindows(clip, source);
		Assert.Equal(3, found.offsetBlocks);
	}

	[Fact]
	public void ExactlyAtThreshold_IsNotPrunedByFloatRounding() {
		uint[] clip = RandomWords(94, 935), source = RandomWords(120, 1735);
		clip.CopyTo(source, 10);
		for (int i = 0; i < 12; i++) source[10 + i * 7] ^= 0xFF;
		var found = ScanEngine.CompareFineAudioWindows(clip, source, .8f);
		Assert.Equal(.8f, found.similarity);
		Assert.Equal(10, found.offsetBlocks);
	}
}
