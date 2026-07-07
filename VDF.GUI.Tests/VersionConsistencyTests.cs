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

using System.Text.RegularExpressions;

using VDF.GUI.Utils;

namespace VDF.GUI.Tests;

/// <summary>
/// The version the titlebar/About box show (VersionPrefix in Directory.Build.props) must
/// match the GitHub release tag builds are published to. Guards against #826, where the
/// release line moved to 4.1.x but the app still reported itself as 4.0.0.
/// </summary>
public class VersionConsistencyTests {

	static string RepoRoot() {
		// Walk up from the test bin folder to the repo root.
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
			dir = dir.Parent;
		Assert.NotNull(dir);
		return dir!.FullName;
	}

	static string ReleaseTagMajorMinor() {
		string yml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "releases.yml"));
		var match = Regex.Match(yml, @"tag_name:\s*(\d+)\.(\d+)\.x");
		Assert.True(match.Success, "releases.yml: no 'tag_name: <major>.<minor>.x' found");
		return $"{match.Groups[1].Value}.{match.Groups[2].Value}";
	}

	[Fact]
	public void VersionPrefix_MatchesReleaseTag() {
		string props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
		var match = Regex.Match(props, @"<VersionPrefix>(\d+)\.(\d+)\.\d+</VersionPrefix>");
		Assert.True(match.Success, "Directory.Build.props: no <VersionPrefix> found");
		Assert.Equal(ReleaseTagMajorMinor(), $"{match.Groups[1].Value}.{match.Groups[2].Value}");
	}

	[Fact]
	public void BuiltAssemblyVersion_MatchesReleaseTag() {
		// VersionInfo.Version prefers the entry assembly, which under the test host is
		// testhost rather than the GUI — check the VDF.GUI assembly directly instead.
		var v = typeof(VersionInfo).Assembly.GetName().Version;
		Assert.NotNull(v);
		Assert.Equal(ReleaseTagMajorMinor(), $"{v!.Major}.{v.Minor}");
	}
}
