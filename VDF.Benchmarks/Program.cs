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

using BenchmarkDotNet.Running;
using VDF.Benchmarks.Scenarios;

namespace VDF.Benchmarks;

internal static class Program {
	static int Main(string[] args) {
		// Direct phase-timing probe (bypasses BDN so we can split open vs decode cost).
		if (args.Length > 0 && args[0] == "--probe-decoder-reuse")
			return DecoderReuseProbe.Run(args);

		// BenchmarkSwitcher routes CLI args (--filter, --list, --job, --exporters …)
		// to BDN. With no args, prints the menu of available benchmarks.
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		return 0;
	}
}
