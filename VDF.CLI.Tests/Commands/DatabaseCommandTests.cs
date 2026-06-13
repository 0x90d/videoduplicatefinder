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

public class DatabaseCommandTests {
	[Fact]
	public void DbCommand_Registers_Export_Subcommand() {
		var cmd = DatabaseCommand.Build();
		var export = cmd.Subcommands.FirstOrDefault(c => c.Name == "export");
		Assert.NotNull(export);
		Assert.Equal("Export the scan database to a JSON file (includes fingerprints, media info, and perceptual hashes).",
			export.Description);
	}

	[Fact]
	public void DbExport_Parses_Output_Option() {
		var cmd = DatabaseCommand.Build();
		var root = new RootCommand { cmd };
		var result = root.Parse("db export -o /tmp/test.json");
		Assert.Equal(0, result.Errors.Count);
	}

	[Fact]
	public void DbExport_Parses_Pretty_Flag() {
		var cmd = DatabaseCommand.Build();
		var root = new RootCommand { cmd };
		var result = root.Parse("db export -o /tmp/test.json --pretty");
		Assert.Equal(0, result.Errors.Count);
	}
}
