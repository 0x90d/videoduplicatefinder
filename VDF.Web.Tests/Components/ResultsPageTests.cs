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
public sealed class ResultsPageTests : TestContext {
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
		};
		scan.Engine.Duplicates.Add(item);
		return item;
	}

	IRenderedComponent<VDF.Web.Components.Pages.Results> RenderPage() =>
		RenderComponent<VDF.Web.Components.Pages.Results>();

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
