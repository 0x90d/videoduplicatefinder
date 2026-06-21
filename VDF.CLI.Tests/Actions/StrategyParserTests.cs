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

using System.CommandLine;
using VDF.CLI.Actions;
using VDF.CLI.Commands;

namespace VDF.CLI.Tests.Actions;

public class StrategyParserTests {
	[Theory]
	// Documented kebab-case forms (these were previously rejected).
	[InlineData("lowest-quality", Strategy.LowestQuality)]
	[InlineData("smallest-file", Strategy.SmallestFile)]
	[InlineData("shortest-duration", Strategy.ShortestDuration)]
	[InlineData("worst-resolution", Strategy.WorstResolution)]
	[InlineData("100-percent-only", Strategy.HundredPercentOnly)]
	// Enum member names and other casings still work.
	[InlineData("LowestQuality", Strategy.LowestQuality)]
	[InlineData("hundredpercentonly", Strategy.HundredPercentOnly)]
	[InlineData("WORST_RESOLUTION", Strategy.WorstResolution)]
	public void TryParse_AcceptsDocumentedAndEnumForms(string input, Strategy expected) {
		Assert.True(StrategyParser.TryParse(input, out var strategy));
		Assert.Equal(expected, strategy);
	}

	[Theory]
	[InlineData("")]
	[InlineData("bogus")]
	[InlineData(null)]
	public void TryParse_RejectsUnknown(string? input) {
		Assert.False(StrategyParser.TryParse(input, out _));
	}

	[Fact]
	public void Mark_AcceptsHyphenatedStrategy_NoParseError() {
		var root = new RootCommand { MarkCommand.Build() };
		var result = root.Parse("mark -i x.json --strategy lowest-quality");
		Assert.Empty(result.Errors);
	}

	[Fact]
	public void Mark_RejectsUnknownStrategy_ParseError() {
		var root = new RootCommand { MarkCommand.Build() };
		var result = root.Parse("mark -i x.json --strategy bogus");
		Assert.NotEmpty(result.Errors);
	}

	[Fact]
	public void ScanAndCompare_AcceptsHyphenatedAction_NoParseError() {
		var root = new RootCommand { ScanAndCompareCommand.Build() };
		var result = root.Parse("scan-and-compare --action 100-percent-only");
		Assert.Empty(result.Errors);
	}
}
