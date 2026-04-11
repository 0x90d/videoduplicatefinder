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
using VDF.CLI.Commands;

var root = new RootCommand("vdf-cli — Video Duplicate Finder command-line interface");
root.Subcommands.Add(ScanAndCompareCommand.Build());
root.Subcommands.Add(ScanCommand.Build());
root.Subcommands.Add(CompareCommand.Build());
root.Subcommands.Add(MarkCommand.Build());
root.Subcommands.Add(DatabaseCommand.Build());
return await root.Parse(args).InvokeAsync();
