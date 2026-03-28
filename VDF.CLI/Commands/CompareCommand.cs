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
using VDF.CLI.Output;
using VDF.Core;

namespace VDF.CLI.Commands {
	internal static class CompareCommand {
		internal static Command Build() {
			var cmd = new Command("compare", "Compare previously scanned hashes and output duplicate groups. Requires a prior 'scan' run.");

			cmd.Options.Add(SharedOptions.Threshold);
			cmd.Options.Add(SharedOptions.Percent);
			cmd.Options.Add(SharedOptions.Database);
			cmd.Options.Add(SharedOptions.SettingsFile);
			cmd.Options.Add(SharedOptions.Format);
			cmd.Options.Add(SharedOptions.Output);

			cmd.SetAction(async (parseResult, ct) => {
				var engine = new ScanEngine();
				SharedOptions.ApplyToSettings(engine.Settings, parseResult);
				ScanRunner.WireProgress(engine);

				var duplicates = await ScanRunner.RunCompareAsync(engine, ct);

				var format = Enum.TryParse<OutputFormat>(parseResult.GetValue(SharedOptions.Format), true, out var fmt) ? fmt : OutputFormat.Text;
				var outFile = parseResult.GetValue(SharedOptions.Output);
				string output = ResultFormatter.Format(duplicates, format);

				if (outFile != null) {
					File.WriteAllText(outFile.FullName, output);
					Console.Error.WriteLine($"Results written to: {outFile.FullName}");
				}
				else {
					Console.Write(output);
				}
			});

			return cmd;
		}
	}
}
