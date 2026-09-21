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
}
