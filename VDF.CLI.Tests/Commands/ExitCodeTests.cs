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
using VDF.CLI.Commands;

namespace VDF.CLI.Tests.Commands;

// Verifies that command actions surface failures through the process exit code so
// scripts can detect them, rather than printing an error but exiting 0.
public class ExitCodeTests {
	[Fact]
	public async Task Scan_MissingInclude_ReturnsOne() {
		int exit = await Invoke("scan");
		Assert.Equal(1, exit);
	}

	[Fact]
	public async Task ScanAndCompare_MissingInclude_ReturnsOne() {
		int exit = await Invoke("scan-and-compare");
		Assert.Equal(1, exit);
	}

	[Fact]
	public async Task Mark_MissingInputFile_ReturnsOne() {
		string missing = Path.Combine(Path.GetTempPath(), $"vdf-missing-{Guid.NewGuid():N}.json");
		int exit = await Invoke($"mark -i \"{missing}\" --strategy LowestQuality");
		Assert.Equal(1, exit);
	}

	[Fact]
	public async Task Mark_InvalidJson_ReturnsOne() {
		string file = Path.Combine(Path.GetTempPath(), $"vdf-bad-{Guid.NewGuid():N}.json");
		File.WriteAllText(file, "{ this is not valid json ]");
		try {
			int exit = await Invoke($"mark -i \"{file}\" --strategy LowestQuality");
			Assert.Equal(1, exit);
		}
		finally {
			try { File.Delete(file); } catch { }
		}
	}

	[Fact]
	public async Task Mark_EmptyResults_ReturnsZero() {
		string file = Path.Combine(Path.GetTempPath(), $"vdf-empty-{Guid.NewGuid():N}.json");
		File.WriteAllText(file, "[]");
		try {
			int exit = await Invoke($"mark -i \"{file}\" --strategy LowestQuality");
			Assert.Equal(0, exit);
		}
		finally {
			try { File.Delete(file); } catch { }
		}
	}

	static Task<int> Invoke(string commandLine) {
		var root = new RootCommand("vdf-cli");
		root.Subcommands.Add(ScanAndCompareCommand.Build());
		root.Subcommands.Add(ScanCommand.Build());
		root.Subcommands.Add(CompareCommand.Build());
		root.Subcommands.Add(MarkCommand.Build());
		root.Subcommands.Add(DatabaseCommand.Build());
		return root.Parse(commandLine).InvokeAsync();
	}
}
