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

using System.Globalization;
using VDF.GUI.Data;

namespace VDF.GUI.Tests {
	public class ResultsBadgeRulesTests {

		[Theory]
		[InlineData(100f, false, false)]
		[InlineData(99f, false, false)]   // high band starts at 99
		[InlineData(98.9f, true, false)]  // just under → mid
		[InlineData(90f, true, false)]    // mid band starts at 90
		[InlineData(89.9f, false, true)]  // just under → low
		[InlineData(0f, false, true)]
		public void SimilarityTiers_MatchTheBands(float similarity, bool expectMid, bool expectLow) {
			Assert.Equal(expectMid, ResultsBadgeRules.IsMidSimilarity(similarity));
			Assert.Equal(expectLow, ResultsBadgeRules.IsLowSimilarity(similarity));
		}

		[Fact]
		public void FormatSimilarity_WholeNumbersDropTheFraction() {
			Assert.Equal("100 %", ResultsBadgeRules.FormatSimilarity(100f, CultureInfo.InvariantCulture));
			// 99.96 rounds to 100.0 → shown as a whole number, not "100.0 %"
			Assert.Equal("100 %", ResultsBadgeRules.FormatSimilarity(99.96f, CultureInfo.InvariantCulture));
		}

		[Fact]
		public void FormatSimilarity_FractionsKeepOneDecimal() {
			Assert.Equal("98.2 %", ResultsBadgeRules.FormatSimilarity(98.2f, CultureInfo.InvariantCulture));
			Assert.Equal("98.2 %", ResultsBadgeRules.FormatSimilarity(98.24f, CultureInfo.InvariantCulture));
		}

		[Fact]
		public void FormatSimilarity_UsesTheGivenCulturesDecimalSeparator() {
			Assert.Equal("98,2 %", ResultsBadgeRules.FormatSimilarity(98.2f, CultureInfo.GetCultureInfo("de-DE")));
		}

		[Theory]
		[InlineData(true, null, "Good")]
		[InlineData(false, null, "Bad")]
		[InlineData(true, "+2.1 MB", "Good")]
		[InlineData(false, "-2.1 MB", "Bad")]
		[InlineData(true, "=", "Equal")]   // equal diff mutes even the best value
		[InlineData(false, "=", "Equal")]
		public void Emphasis_EqualDiffWinsOverBest(bool isBest, string? diff, string expected) {
			Assert.Equal(Enum.Parse<MetricEmphasis>(expected), ResultsBadgeRules.GetEmphasis(isBest, diff));
		}

		[Fact]
		public void IsEqualDiff_OnlyOnTheExactMarker() {
			Assert.True(ResultsBadgeRules.IsEqualDiff("="));
			Assert.False(ResultsBadgeRules.IsEqualDiff(null));
			Assert.False(ResultsBadgeRules.IsEqualDiff(""));
			Assert.False(ResultsBadgeRules.IsEqualDiff("=="));
		}
	}
}
