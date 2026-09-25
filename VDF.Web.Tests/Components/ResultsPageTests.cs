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

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VDF.Core;
using VDF.Core.ViewModels;
using VDF.Web.Services;
using VDF.Web.Tests.Services;

namespace VDF.Web.Tests.Components;

/// <summary>Renders the real Results page over seeded duplicates.</summary>
[Collection("WebSettingsOverride")]
public sealed class ResultsPageTests : BunitContext {
	readonly ScanService scan;

	public ResultsPageTests() {
		WebSettingsService.TestOverrideSettingsPath =
			Path.Combine(Path.GetTempPath(), $"VDF.WebTests.{Guid.NewGuid():N}.json");
		var webSettings = new WebSettingsService();
		scan = new ScanService(webSettings);
		Services.AddSingleton(webSettings);
		Services.AddSingleton(scan);
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	protected override void Dispose(bool disposing) {
		base.Dispose(disposing);
		scan.Dispose();
		WebSettingsService.TestOverrideSettingsPath = null;
	}

	DuplicateItem Seed(string name, Guid group, DuplicateFlags flags = DuplicateFlags.None, float difference = 0.02f) {
		var item = new DuplicateItem(ScanServiceTests.MakeEntry(name), difference, group, flags) {
			PartialClipOffset = flags.HasFlag(DuplicateFlags.PartialClip) ? TimeSpan.FromSeconds(42) : TimeSpan.Zero,
			Duration = TimeSpan.FromSeconds(60), // the stream-less test entry leaves it at zero
		};
		scan.Engine.Duplicates.Add(item);
		return item;
	}

	IRenderedComponent<VDF.Web.Components.Pages.Results> RenderPage() =>
		Render<VDF.Web.Components.Pages.Results>();

	[Fact]
	public void NoDuplicates_ShowsTheEmptyStateInsteadOfTheToolbar() {
		var page = RenderPage();

		Assert.Contains("No duplicates found", page.Markup);
		Assert.Empty(page.FindAll(".results-toolbar"));
	}

	[Fact]
	public void Duplicates_RenderGroupedWithCountsAndCards() {
		Guid groupA = Guid.NewGuid();
		Guid groupB = Guid.NewGuid();
		Seed("a1.mp4", groupA);
		Seed("a2.mp4", groupA);
		Seed("b1.mp4", groupB);
		Seed("b2.mp4", groupB);
		Seed("b3.mp4", groupB);

		var page = RenderPage();

		Assert.Equal(2, page.FindAll(".dup-group").Count);
		Assert.Equal(5, page.FindAll(".dup-card").Count);
		Assert.Contains("2 group(s), 5 file(s)", page.Markup);
	}

	[Fact]
	public void AiAndPartialClipMatches_ShowTheirBadges() {
		Guid group = Guid.NewGuid();
		Seed("source.mp4", group);
		Seed("union-ai.mp4", group, DuplicateFlags.AiMatched);
		Seed("clip.mp4", group, DuplicateFlags.PartialClip | DuplicateFlags.AiMatched);

		var page = RenderPage();

		// Two AI-matched items, one of them additionally a partial clip with its offset.
		Assert.Equal(2, page.FindAll(".badge-ai-matched").Count);
		var partialBadge = Assert.Single(page.FindAll(".badge-partial-clip"));
		Assert.Contains("partial clip", partialBadge.TextContent);
		Assert.Contains("@ 00:00:42", partialBadge.TextContent);
	}

	// #899: a card lists the audio and subtitle languages, and only when there are any.
	[Fact]
	public void VideoCards_ShowTrackLanguages_WhenTheFileHasThem() {
		Guid group = Guid.NewGuid();
		var tagged = Seed("tagged.mkv", group);
		tagged.AudioLanguages = "GER, ENG";
		tagged.SubtitleLanguages = "GER";
		Seed("plain.mp4", group);

		var page = RenderPage();

		string LabelsOf(string name) => string.Join("|", page.FindAll(".dup-card")
			.Single(c => c.TextContent.Contains(name)).QuerySelectorAll(".meta-row")
			.Select(r => r.TextContent.Trim().Replace("\n", " ")));
		var taggedCard = page.FindAll(".dup-card").Single(c => c.TextContent.Contains("tagged.mkv"));
		var rows = taggedCard.QuerySelectorAll(".meta-row")
			.ToDictionary(r => r.QuerySelector(".meta-label")!.TextContent, r => r.QuerySelector(".meta-value")!.TextContent.Trim());
		Assert.Equal("GER, ENG", rows["Audio"]);
		Assert.Equal("GER", rows["Subs"]);
		Assert.DoesNotContain("Audio", LabelsOf("plain.mp4"));
		Assert.DoesNotContain("Subs", LabelsOf("plain.mp4"));
	}

	// === #926: Compare metadata ===

	static readonly Func<string, IReadOnlyList<VDF.Core.Utils.MetadataField>?> FakeTags = path => {
		VDF.Core.Utils.MetadataField C(string name, string value) => new(new(VDF.Core.Utils.MetadataSectionKind.Container), name, value);
		if (path.Contains("gone")) return null;
		string date = path.Contains("right") ? "2023-08-15T14:34:56+0200" : "2023-08-15T12:34:56";
		return new[] { C("creation_time", "2023-08-15T12:34:56Z"), C("com.apple.quicktime.creationdate", date) };
	};

	IRenderedComponent<VDF.Web.Components.Pages.Results> OpenMetadataOfFirstGroup() {
		var page = RenderPage();
		page.Find(".metadata-btn").Click();
		page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".metadata-table")));
		return page;
	}

	[Fact]
	public void Metadata_MarksTheOneCopyWithTheOtherDate() {
		MetadataLookup.Reader = FakeTags;
		try {
			Guid group = Guid.NewGuid();
			Seed("wrong1.mov", group);
			Seed("right.mov", group);
			Seed("wrong2.mov", group);

			var page = OpenMetadataOfFirstGroup();

			Assert.Contains("1 of 2 fields differ", page.Find(".metadata-summary").TextContent);
			var row = Assert.Single(page.FindAll(".metadata-table tbody tr"), r => r.QuerySelector("th[scope=row]") != null);
			Assert.Equal("com.apple.quicktime.creationdate", row.QuerySelector("th")!.TextContent);
			var odd = Assert.Single(row.QuerySelectorAll("td.metadata-odd"));
			Assert.Contains("2023-08-15T14:34:56+0200", odd.TextContent);
			Assert.Contains("(differs)", odd.TextContent); // said to screen readers, not only colored
			Assert.Equal("Container", page.Find(".metadata-section th").TextContent);

			// Untick the filter: the shared creation_time shows too, unmarked.
			page.Find(".metadata-toolbar input").Change(false);
			Assert.Equal(2, page.FindAll(".metadata-table th[scope=row]").Count);
			Assert.Single(page.FindAll("td.metadata-odd"));
		}
		finally { MetadataLookup.Reader = VDF.Core.Utils.FileMetadata.Read; }
	}

	[Fact]
	public void Metadata_ColumnCheckbox_SelectsTheFileOnThePage_AndEscCloses() {
		MetadataLookup.Reader = FakeTags;
		try {
			Guid group = Guid.NewGuid();
			Seed("wrong1.mov", group);
			Seed("right.mov", group);
			Seed("gone.mov", group);

			var page = OpenMetadataOfFirstGroup();

			var gone = page.FindAll(".metadata-table thead th").Single(th => th.TextContent.Contains("gone.mov"));
			Assert.Contains("file not available", gone.TextContent);
			page.FindAll(".metadata-table thead input").Single(i => i.GetAttribute("aria-label") == "Select wrong1.mov").Change(true);
			Assert.Contains("1 selected", page.Markup);
			Assert.Contains("selected", page.FindAll(".dup-card").Single(c => c.TextContent.Contains("wrong1.mov")).ClassName);

			page.Find("#metadata-modal").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
			Assert.Empty(page.FindAll("#metadata-modal"));
		}
		finally { MetadataLookup.Reader = VDF.Core.Utils.FileMetadata.Read; }
	}

	[Fact]
	public void Metadata_OpensFromTheCardMenu() {
		MetadataLookup.Reader = FakeTags;
		try {
			Guid group = Guid.NewGuid();
			Seed("wrong1.mov", group);
			Seed("right.mov", group);
			Seed("wrong2.mov", group);
			var page = RenderPage();

			page.FindAll(".dup-card").Single(c => c.TextContent.Contains("right.mov")).ContextMenu();
			page.FindAll(".ctx-menu button").Single(b => b.TextContent == "Compare metadata").Click();

			page.WaitForAssertion(() => Assert.Single(page.FindAll("td.metadata-odd")));
			Assert.Empty(page.FindAll(".ctx-menu"));
		}
		finally { MetadataLookup.Reader = VDF.Core.Utils.FileMetadata.Read; }
	}

	// === Frames: which moment of a file is shown ===
	// Seeded entries are 60 seconds long.

	static string[] PaneFrames(IRenderedComponent<VDF.Web.Components.Pages.Results> page) =>
		page.FindAll(".compare-pane-img img").Select(i => i.GetAttribute("src")!).ToArray();

	[Fact]
	public void Cards_AskForTheMiddleSampledFrame() {
		// Used to be left to the server, which always answered with 10 percent of the file:
		// a frame the scan never looked at.
		scan.Settings.ThumbnailCount = 3;
		Guid group = Guid.NewGuid();
		Seed("a.mp4", group);
		Seed("b.mp4", group);

		var page = RenderPage();

		Assert.All(page.FindAll(".card-thumb img"), img => {
			string url = img.GetAttribute("data-src")!;
			Assert.EndsWith("&w=480&q=85&t=30.00", url);
			// Written into the markup as "&amp;w=", the URL reached the browser undecoded:
			// the server saw parameters named "amp;w" and "amp;q" and ignored them.
			Assert.DoesNotContain("amp;", url);
		});
	}

	[Fact]
	public void Compare_StepsThroughTheSampledFrames() {
		scan.Settings.ThumbnailCount = 3;
		Guid group = Guid.NewGuid();
		Seed("a.mp4", group);
		Seed("b.mp4", group);
		var page = RenderPage();

		page.Find(".compare-btn").Click();

		// Opens on the middle one of the three sampled frames (25/50/75 percent).
		Assert.Equal("Frame 2 / 3", page.Find(".compare-frame-count").TextContent);
		Assert.All(PaneFrames(page), src => Assert.EndsWith("&t=30.00", src));
		Assert.All(page.FindAll(".compare-frame-time"), t => Assert.Equal("00:00:30", t.TextContent));

		page.Find(".compare-frame-next").Click();

		Assert.Equal("Frame 3 / 3", page.Find(".compare-frame-count").TextContent);
		Assert.All(PaneFrames(page), src => Assert.EndsWith("&t=45.00", src));
		Assert.True(page.Find(".compare-frame-next").HasAttribute("disabled"));

		page.Find(".compare-frame-prev").Click();
		page.Find(".compare-frame-prev").Click();

		Assert.Equal("Frame 1 / 3", page.Find(".compare-frame-count").TextContent);
		Assert.All(PaneFrames(page), src => Assert.EndsWith("&t=15.00", src));
		Assert.True(page.Find(".compare-frame-prev").HasAttribute("disabled"));
	}

	[Fact]
	public void Compare_ArrowKeysStepAndEscapeCloses() {
		scan.Settings.ThumbnailCount = 3;
		Guid group = Guid.NewGuid();
		Seed("a.mp4", group);
		Seed("b.mp4", group);
		var page = RenderPage();
		page.Find(".compare-btn").Click();

		page.Find("#compare-modal").KeyDown("ArrowRight");
		Assert.Equal("Frame 3 / 3", page.Find(".compare-frame-count").TextContent);

		// At the end: stays there instead of running out of the list.
		page.Find("#compare-modal").KeyDown("ArrowRight");
		Assert.Equal("Frame 3 / 3", page.Find(".compare-frame-count").TextContent);

		page.Find("#compare-modal").KeyDown("ArrowLeft");
		Assert.Equal("Frame 2 / 3", page.Find(".compare-frame-count").TextContent);

		page.Find("#compare-modal").KeyDown("Escape");
		Assert.Empty(page.FindAll("#compare-modal"));
	}

	[Fact]
	public void Compare_SwipeMode_StepsTheSameFrames() {
		scan.Settings.ThumbnailCount = 3;
		Guid group = Guid.NewGuid();
		Seed("a.mp4", group);
		Seed("b.mp4", group);
		var page = RenderPage();
		page.Find(".compare-btn").Click();
		page.FindAll(".compare-mode-tabs button").Single(b => b.TextContent == "Swipe").Click();

		page.Find(".compare-frame-next").Click();

		Assert.EndsWith("&t=45.00", page.Find(".compare-swipe-img-a img").GetAttribute("src"));
		Assert.EndsWith("&t=45.00", page.Find(".compare-swipe-img-b img").GetAttribute("src"));
		Assert.Equal("A 00:00:45", page.Find(".compare-swipe-label.label-a").TextContent);
	}

	[Fact]
	public void Compare_WithOneSampledFrame_HasNothingToStepThrough() {
		scan.Settings.ThumbnailCount = 1;
		Guid group = Guid.NewGuid();
		Seed("a.mp4", group);
		Seed("b.mp4", group);
		var page = RenderPage();

		page.Find(".compare-btn").Click();

		Assert.Empty(page.FindAll(".compare-frames"));
		Assert.All(PaneFrames(page), src => Assert.EndsWith("&t=30.00", src));
	}

	[Fact]
	public void Compare_PartialClip_ShowsTheSameMomentOfClipAndSource() {
		// A 60 second "clip" found 42 seconds into its source. Both files showed their own
		// 10 percent before, two unrelated moments, so a correct match looked wrong.
		scan.Settings.ThumbnailCount = 1;
		Guid group = Guid.NewGuid();
		var source = Seed("source.mp4", group, difference: 0f);
		source.Duration = TimeSpan.FromSeconds(600);
		Seed("clip.mp4", group, DuplicateFlags.PartialClip);
		var page = RenderPage();

		page.Find(".compare-btn").Click();

		// Sorted by similarity: the source (100 percent) is A, the clip is B.
		Assert.Equal("Frame 2 / 3", page.Find(".compare-frame-count").TextContent);
		var frames = PaneFrames(page);
		Assert.Contains("source.mp4", frames[0]);
		Assert.EndsWith("&t=72.00", frames[0]); // 42 + 30
		Assert.EndsWith("&t=30.00", frames[1]);

		page.Find(".compare-frame-next").Click();

		frames = PaneFrames(page);
		Assert.EndsWith("&t=87.00", frames[0]); // 42 + 45
		Assert.EndsWith("&t=45.00", frames[1]);
	}

	[Fact]
	public void UnflaggedItems_ShowNoBadges() {
		Guid group = Guid.NewGuid();
		Seed("plain1.mp4", group);
		Seed("plain2.mp4", group);

		var page = RenderPage();

		Assert.Empty(page.FindAll(".badge-ai-matched"));
		Assert.Empty(page.FindAll(".badge-partial-clip"));
	}

	// === CSV export ===

	[Fact]
	public void ExportCsv_MarksWhatIsSelectedInThisPage() {
		// The export used to be a stateless endpoint with no way to know the selection,
		// which lives in the page (one per tab), so the CSV had no Checked column at all.
		Guid group = Guid.NewGuid();
		Seed("keep.mp4", group);
		Seed("drop.mp4", group);
		string? csv = null;
		JSInterop.SetupVoid("vdf.downloadStream", inv => {
			Assert.Equal("vdf-results.csv", inv.Arguments[0]);
			var stream = ((Microsoft.JSInterop.DotNetStreamReference)inv.Arguments[1]!).Stream;
			csv = new StreamReader(stream).ReadToEnd();
			return true;
		}).SetVoidResult();

		var page = RenderPage();
		page.FindAll(".dup-card").Single(c => c.TextContent.Contains("drop.mp4"))
			.QuerySelector(".card-check input")!.Change(true);
		page.FindAll("button").Single(b => b.TextContent == "Export CSV").Click();

		Assert.NotNull(csv);
		string[] lines = csv!.TrimEnd().Split(Environment.NewLine);
		Assert.EndsWith(",IsImage,Checked,AudioLanguages,SubtitleLanguages", lines[0]);
		Assert.Equal("True", ResultsCsvTests.Field(lines[0], Assert.Single(lines, l => l.Contains("drop.mp4")), "Checked"));
		Assert.Equal("False", ResultsCsvTests.Field(lines[0], Assert.Single(lines, l => l.Contains("keep.mp4")), "Checked"));
	}
}
