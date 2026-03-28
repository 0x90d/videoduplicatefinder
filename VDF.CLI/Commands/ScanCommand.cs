// /*
//     Copyright (C) 2025 0x90d
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

using System.CommandLine;
using VDF.Core;

namespace VDF.CLI.Commands {
	internal static class ScanCommand {
		internal static Command Build() {
			var cmd = new Command("scan", "Enumerate files and build hashes, storing results in the database. Run 'compare' afterwards to find duplicates.");
			SharedOptions.AddScanOptions(cmd);

			cmd.SetAction(async (parseResult, ct) => {
				var engine = new ScanEngine();
				SharedOptions.ApplyToSettings(engine.Settings, parseResult);

				if (engine.Settings.IncludeList.Count == 0) {
					Console.Error.WriteLine("Error: at least one --include path is required.");
					return;
				}

				ScanRunner.WireProgress(engine);
				await ScanRunner.RunSearchAsync(engine, ct);
				Console.Error.WriteLine("Scan complete. Run 'compare' to find duplicates.");
			});

			return cmd;
		}
	}
}
