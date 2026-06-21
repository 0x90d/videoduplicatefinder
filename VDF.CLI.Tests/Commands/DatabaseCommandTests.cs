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
using System.Text.Json;
using VDF.CLI.Commands;
using VDF.Core;
using VDF.Core.Utils;

namespace VDF.CLI.Tests.Commands;

// The end-to-end tests invoke the command actions, which mutate the process-wide
// DatabaseUtils state. They live in one class so xUnit runs them serially, and
// each one resets that state in a finally block.
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
		Assert.Empty(result.Errors);
	}

	[Fact]
	public void DbExport_Parses_Pretty_Flag() {
		var cmd = DatabaseCommand.Build();
		var root = new RootCommand { cmd };
		var result = root.Parse("db export -o /tmp/test.json --pretty");
		Assert.Empty(result.Errors);
	}

	[Fact]
	public void DbExport_MissingOutput_IsParseError() {
		var cmd = DatabaseCommand.Build();
		var root = new RootCommand { cmd };
		var result = root.Parse("db export");
		Assert.NotEmpty(result.Errors);
	}

	[Fact]
	public async Task DbExport_WritesValidJson_AndReturnsZero() {
		string dir = NewTempDir();
		string outFile = Path.Combine(dir, "export.json");
		try {
			SeedDatabase(dir, "alpha-export-entry.mp4", "beta-export-entry.mkv");

			int exit = await InvokeAsync($"db export -o \"{outFile}\" --db \"{dir}\" --pretty");

			Assert.Equal(0, exit);
			Assert.True(File.Exists(outFile));
			string json = File.ReadAllText(outFile);
			// Must be parseable and carry the seeded entries.
			using var doc = JsonDocument.Parse(json);
			Assert.Contains("alpha-export-entry", json);
			Assert.Contains("beta-export-entry", json);
			// --pretty must produce indented output.
			Assert.Contains("\n", json);
		}
		finally {
			ResetDatabaseState();
			TryDelete(dir);
		}
	}

	[Fact]
	public async Task DbExport_EmptyDatabase_ReturnsOne() {
		string dir = NewTempDir();
		string outFile = Path.Combine(dir, "export.json");
		try {
			// Empty folder: no database file, no entries.
			ResetDatabaseState();
			DatabaseUtils.CustomDatabaseFolder = dir;
			DatabaseUtils.InvalidateDatabaseFolder();

			int exit = await InvokeAsync($"db export -o \"{outFile}\" --db \"{dir}\"");

			Assert.Equal(1, exit);
			Assert.False(File.Exists(outFile));
		}
		finally {
			ResetDatabaseState();
			TryDelete(dir);
		}
	}

	[Fact]
	public async Task DbClear_Aborted_ReturnsOne() {
		string dir = NewTempDir();
		var savedIn = Console.In;
		try {
			SeedDatabase(dir, "to-keep.mp4");
			// Simulate a user (or non-interactive pipe) that does not type "yes".
			Console.SetIn(new StringReader("no\n"));

			int exit = await InvokeAsync($"db clear --db \"{dir}\"");

			Assert.Equal(1, exit);
			// Nothing was deleted.
			DatabaseUtils.InvalidateDatabaseFolder();
			Assert.True(DatabaseUtils.LoadDatabase());
			Assert.Single(DatabaseUtils.Database);
		}
		finally {
			Console.SetIn(savedIn);
			ResetDatabaseState();
			TryDelete(dir);
		}
	}

	[Fact]
	public async Task DbClear_WithYes_ClearsAndReturnsZero() {
		string dir = NewTempDir();
		try {
			SeedDatabase(dir, "doomed.mp4");

			int exit = await InvokeAsync($"db clear --yes --db \"{dir}\"");

			Assert.Equal(0, exit);
			DatabaseUtils.InvalidateDatabaseFolder();
			Assert.True(DatabaseUtils.LoadDatabase());
			Assert.Empty(DatabaseUtils.Database);
		}
		finally {
			ResetDatabaseState();
			TryDelete(dir);
		}
	}

	[Fact]
	public async Task DbClean_ReturnsZero() {
		string dir = NewTempDir();
		try {
			SeedDatabase(dir, "present.mp4");

			int exit = await InvokeAsync($"db clean --db \"{dir}\"");

			Assert.Equal(0, exit);
		}
		finally {
			ResetDatabaseState();
			TryDelete(dir);
		}
	}

	static Task<int> InvokeAsync(string commandLine) {
		var root = new RootCommand { DatabaseCommand.Build() };
		return root.Parse(commandLine).InvokeAsync();
	}

	static string NewTempDir() {
		string dir = Path.Combine(Path.GetTempPath(), $"vdf-cli-dbtest-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static void SeedDatabase(string dir, params string[] paths) {
		DatabaseUtils.CustomDatabaseFolder = dir;
		DatabaseUtils.InvalidateDatabaseFolder();
		DatabaseUtils.Database.Clear();
		foreach (var p in paths)
			DatabaseUtils.Database.Add(new FileEntry { Path = p, FileSize = 1234 });
		DatabaseUtils.SaveDatabase();
	}

	static void ResetDatabaseState() {
		DatabaseUtils.Database.Clear();
		DatabaseUtils.CustomDatabaseFolder = null;
		DatabaseUtils.InvalidateDatabaseFolder();
	}

	static void TryDelete(string dir) {
		try { Directory.Delete(dir, recursive: true); } catch { }
	}
}
