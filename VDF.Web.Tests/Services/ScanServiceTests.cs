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

using VDF.Core;
using VDF.Core.ViewModels;
using VDF.Web.Services;

namespace VDF.Web.Tests.Services;

[Collection("WebSettingsOverride")]
public sealed class ScanServiceTests : IDisposable {
	readonly ScanService scan;

	public ScanServiceTests() {
		// Never read/write the developer's real web-settings.json.
		WebSettingsService.TestOverrideSettingsPath =
			Path.Combine(Path.GetTempPath(), $"VDF.WebTests.{Guid.NewGuid():N}.json");
		scan = new ScanService(new WebSettingsService());
	}

	public void Dispose() {
		scan.Dispose();
		WebSettingsService.TestOverrideSettingsPath = null;
	}

	internal static FileEntry MakeEntry(string name) => new() {
		_Path = @"C:\vdf-webtests\" + name,
		FileSize = 1000,
		invalid = false,
		IsImage = false,
		mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(60) },
	};

	DuplicateItem Seed(string name, Guid group, DuplicateFlags flags = DuplicateFlags.None, float difference = 0.02f) {
		var item = new DuplicateItem(MakeEntry(name), difference, group, flags);
		scan.Engine.Duplicates.Add(item);
		return item;
	}

	[Fact]
	public void RemoveFromResults_DropsRemovedItemsAndResultingSingletonGroups() {
		Guid groupA = Guid.NewGuid();
		Guid groupB = Guid.NewGuid();
		var a1 = Seed("a1.mp4", groupA);
		var a2 = Seed("a2.mp4", groupA);
		var b1 = Seed("b1.mp4", groupB);
		var b2 = Seed("b2.mp4", groupB);
		var b3 = Seed("b3.mp4", groupB);

		scan.RemoveFromResults(new[] { a1, b3 });

		// Group A shrank to one item — a group of one is not a duplicate, so it
		// vanishes entirely; group B keeps its two survivors.
		Assert.DoesNotContain(a1, scan.Duplicates);
		Assert.DoesNotContain(a2, scan.Duplicates);
		Assert.Equal(new[] { b1, b2 }.ToHashSet(), scan.Duplicates.ToHashSet());
	}

	[Fact]
	public void Reset_ClearsResultsButKeepsConfiguredPaths() {
		scan.Settings.IncludeList.Add(@"C:\videos");
		scan.Settings.BlackList.Add(@"C:\videos\skip");
		Seed("x1.mp4", Guid.NewGuid());

		scan.Reset();

		Assert.Empty(scan.Duplicates);
		Assert.Equal(ScanState.Idle, scan.State);
		Assert.Contains(@"C:\videos", scan.Settings.IncludeList);
		Assert.Contains(@"C:\videos\skip", scan.Settings.BlackList);
	}
}
