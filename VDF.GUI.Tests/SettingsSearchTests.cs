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

using VDF.GUI.Data;

namespace VDF.GUI.Tests;

public class SettingsSearchTests {

	// ---------- Matches ----------

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Matches_EmptyQuery_MatchesEverything(string? query) {
		Assert.True(SettingsSearch.Matches(query, "anything"));
		Assert.True(SettingsSearch.Matches(query, ""));
		Assert.False(SettingsSearch.IsSearching(query));
	}

	[Theory]
	[InlineData("phash", "Use perceptual hashing (pHash) Much faster comparison on large libraries", true)]
	[InlineData("PHASH", "Use perceptual hashing (pHash)", true)]
	[InlineData("flip", "Compare horizontally flipped Catches mirror-image re-uploads", true)]
	[InlineData("flip", "Similarity threshold How alike the sampled frames", false)]
	public void Matches_SingleTerm_CaseInsensitive(string query, string text, bool expected) =>
		Assert.Equal(expected, SettingsSearch.Matches(query, text));

	[Fact]
	public void Matches_MultipleTerms_AllMustOccur() {
		const string text = "Ignore black / white pixels Letterboxed or bordered copies match their clean originals.";
		Assert.True(SettingsSearch.Matches("black pixels", text));
		Assert.True(SettingsSearch.Matches("pixels black", text)); // order-independent
		Assert.False(SettingsSearch.Matches("black bars", text));  // "bars" absent -> no match
	}

	[Fact]
	public void Matches_TermsMayMatchAcrossTitleAndDescription() {
		// One term from the title, one from the description, concatenated by the caller.
		Assert.True(SettingsSearch.Matches("duration percentage", "Duration tolerance | compared only when their durations differ by less than this percentage"));
	}

	[Fact]
	public void Matches_WhitespaceVariants_AreTolerated() =>
		Assert.True(SettingsSearch.Matches("  black \t pixels \n", "Ignore black pixels"));

	// ---------- Apply ----------

	static readonly IReadOnlyList<SettingsSearchSection> Sections = new[] {
		new SettingsSearchSection("Scanning", "Scanning"),
		new SettingsSearchSection("Appearance", "Appearance"),
		new SettingsSearchSection("Shortcuts", "Keyboard Shortcuts hotkey key binding"),
	};

	static readonly SettingsSearchRow RowPercent = new("percent", "Scanning", "Similarity threshold How alike the sampled frames of two files must be");
	static readonly SettingsSearchRow RowFlip = new("flip", "Scanning", "Compare horizontally flipped Catches mirror-image re-uploads");
	static readonly SettingsSearchRow RowDark = new("dark", "Appearance", "Dark mode");
	static readonly IReadOnlyList<SettingsSearchRow> Rows = new[] { RowPercent, RowFlip, RowDark };

	[Fact]
	public void Apply_NoQuery_ShowsSelectedSectionWithAllRows() {
		var result = SettingsSearch.Apply("", "Appearance", Sections, Rows);

		Assert.False(result.IsSearchMode);
		Assert.Equal(new HashSet<string> { "Appearance" }, result.VisibleSections);
		// Row visibility is per-section in normal mode; all rows stay visible inside their panels.
		Assert.Equal(3, result.VisibleRows.Count);
	}

	[Fact]
	public void Apply_Query_FiltersRowsAcrossAllSections() {
		// Searching must leave the selected section behind: "dark" lives in Appearance
		// but is found while Scanning is selected.
		var result = SettingsSearch.Apply("dark", "Scanning", Sections, Rows);

		Assert.True(result.IsSearchMode);
		Assert.Contains("dark", result.VisibleRows);
		Assert.DoesNotContain("percent", result.VisibleRows);
		Assert.DoesNotContain("flip", result.VisibleRows);
		Assert.Equal(new HashSet<string> { "Appearance" }, result.VisibleSections);
	}

	[Fact]
	public void Apply_Query_MultipleSectionsCanMatch() {
		// "a" occurs in rows of both Scanning and Appearance and in every section label.
		var result = SettingsSearch.Apply("a", "Appearance", Sections, Rows);

		Assert.Equal(3, result.VisibleRows.Count);
		Assert.Equal(new HashSet<string> { "Scanning", "Appearance", "Shortcuts" }, result.VisibleSections);
	}

	[Fact]
	public void Apply_SectionKeywordMatch_ShowsWholeSectionIncludingItsRows() {
		// "hotkey" only occurs in the Shortcuts section's own keywords; that page has no
		// option rows, so the section itself must surface.
		var result = SettingsSearch.Apply("hotkey", "Scanning", Sections, Rows);

		Assert.True(result.IsSearchMode);
		Assert.Contains("Shortcuts", result.VisibleSections);
		Assert.Empty(result.VisibleRows);

		// And a section matched by name shows all of its rows.
		result = SettingsSearch.Apply("scanning", "Appearance", Sections, Rows);
		Assert.Contains("percent", result.VisibleRows);
		Assert.Contains("flip", result.VisibleRows);
		Assert.DoesNotContain("dark", result.VisibleRows);
		Assert.Equal(new HashSet<string> { "Scanning" }, result.VisibleSections);
	}

	[Fact]
	public void Apply_NoMatch_ShowsNothing() {
		var result = SettingsSearch.Apply("zzz-no-such-setting", "Scanning", Sections, Rows);

		Assert.True(result.IsSearchMode);
		Assert.Empty(result.VisibleRows);
		Assert.Empty(result.VisibleSections);
	}
}
