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
using System.Text;
using VDF.Core.ViewModels;
using VDF.Web.Services;

namespace VDF.Web.Tests.Services;

/// <summary>The Web CSV export, which must read like the GUI's.</summary>
public sealed class ResultsCsvTests {
	static DuplicateItem Item(string name, Guid group) => new() {
		Path = @"C:\vdf-webtests\" + name,
		GroupId = group,
		Fps = 29.97f,
	};

	static string[] Lines(byte[] csv) =>
		Encoding.UTF8.GetString(csv).TrimEnd().Split(Environment.NewLine);

	[Fact]
	public void HeaderMatchesTheGuiExport() {
		string[] lines = Lines(ResultsCsv.Build([], new HashSet<DuplicateItem>()));

		Assert.Equal("GroupId,Path,SizeBytes,Duration,Resolution,Fps,BitrateKbs,AudioFormat,AudioSampleRate,Similarity,DateCreated,IsImage,Checked",
			Assert.Single(lines).TrimStart('\uFEFF'));
	}

	[Fact]
	public void CheckedIsTrueOrFalseLikeTheGui() {
		Guid group = Guid.NewGuid();
		var a = Item("a.mp4", group);
		var b = Item("b.mp4", group);

		string[] lines = Lines(ResultsCsv.Build([a, b], new HashSet<DuplicateItem> { b }));

		Assert.EndsWith(",False", lines[1]);
		Assert.EndsWith(",True", lines[2]);
	}

	[Fact]
	public void GroupMembersAreAdjacentAndPathsAreEscaped() {
		Guid g1 = Guid.NewGuid();
		Guid g2 = Guid.NewGuid();
		var items = new[] { Item("a1.mp4", g1), Item("b1.mp4", g2), Item("a2, \"cut\".mp4", g1) };

		string[] lines = Lines(ResultsCsv.Build(items, new HashSet<DuplicateItem>()));

		Assert.Contains("a1.mp4", lines[1]);
		Assert.Contains(@"""C:\vdf-webtests\a2, """"cut"""".mp4""", lines[2]);
		Assert.Contains("b1.mp4", lines[3]);
	}

	[Fact]
	public void StartsWithABomAndIgnoresTheServerCulture() {
		var saved = CultureInfo.CurrentCulture;
		try {
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");
			byte[] csv = ResultsCsv.Build([Item("a.mp4", Guid.NewGuid())], new HashSet<DuplicateItem>());

			Assert.Equal(Encoding.UTF8.GetPreamble(), csv[..3]);
			Assert.Contains(",29.97,", Lines(csv)[1]);
		}
		finally {
			CultureInfo.CurrentCulture = saved;
		}
	}
}
