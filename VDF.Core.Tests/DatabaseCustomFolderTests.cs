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
using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // redirects the process-wide database folder — never run parallel to other DB tests
public class DatabaseCustomFolderTests {

	// Regression for the startup path: LoadDatabase(customFolder) must make the custom
	// folder the active database location — before the fix only the first scan did, so
	// every pre-scan consumer read the default-location database.
	[Fact]
	public async Task LoadDatabaseWithCustomFolder_MakesItTheActiveLocation() {
		string custom = Path.Combine(Path.GetTempPath(), "vdf-dbfolder-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(custom);
		try {
			Assert.True(await ScanEngine.LoadDatabase(custom));
			ScanEngine.SaveDatabase();
			Assert.True(File.Exists(Path.Combine(custom, "ScannedFiles.db")),
				"database was not written to the custom folder");
		}
		finally {
			// restore the default location for the rest of the suite
			DatabaseUtils.CustomDatabaseFolder = null;
			DatabaseUtils.InvalidateDatabaseFolder();
			try { Directory.Delete(custom, recursive: true); } catch (Exception) { }
		}
	}
}
