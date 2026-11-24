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

namespace VDF.Core.Tests;

// The media-read phases outside gathering (partial-clip visual gate, track languages) pass
// this to Parallel.ForEach, where -1 means unlimited rather than the "all CPU cores" the
// setting promises.
public class MediaReadParallelismTests {
	[Theory]
	[InlineData(-1, 12, 12)]
	[InlineData(0, 12, 12)]
	[InlineData(-1, 0, 1)]
	public void AutoModeUsesAllCpuCores(int configured, int processorCount, int expected) =>
		Assert.Equal(expected, ScanEngine.ResolveMediaReadParallelism(configured, processorCount));

	[Theory]
	[InlineData(1, 12, 1)]
	[InlineData(8, 12, 8)]
	[InlineData(32, 12, 32)] // above the core count is allowed: these workers mostly wait on I/O
	public void ExplicitValueIsHonored(int configured, int processorCount, int expected) =>
		Assert.Equal(expected, ScanEngine.ResolveMediaReadParallelism(configured, processorCount));

	[Fact]
	public void EngineNeverPassesUnlimited() {
		var engine = new ScanEngine();
		engine.Settings.MaxDegreeOfParallelism = -1;
		Assert.Equal(Environment.ProcessorCount, engine.ParallelDegree);
	}
}
