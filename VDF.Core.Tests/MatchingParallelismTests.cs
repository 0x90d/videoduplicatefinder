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

// The CPU-bound matching phases resolve their worker count independently of
// MaxDegreeOfParallelism (a media-read knob tuned to storage). Auto mode
// (configured <= 0) uses most of the machine while reserving headroom for the
// UI; explicit values are honored up to the logical CPU count.
public class MatchingParallelismTests {
	[Theory]
	[InlineData(0, 1, 1)]
	[InlineData(0, 2, 1)]
	[InlineData(0, 4, 3)]
	[InlineData(0, 8, 6)]
	[InlineData(0, 16, 13)]
	[InlineData(0, 32, 26)]
	[InlineData(-1, 8, 6)]
	public void AutoModeLeavesCpuHeadroom(int configured, int processorCount, int expected) =>
		Assert.Equal(expected, ScanEngine.CalculateMatchingParallelism(configured, processorCount));

	[Theory]
	[InlineData(1, 16, 1)]
	[InlineData(6, 16, 6)]
	[InlineData(99, 16, 16)]
	public void ExplicitValueIsHonoredAndCappedAtCpuCount(int configured, int processorCount, int expected) =>
		Assert.Equal(expected, ScanEngine.CalculateMatchingParallelism(configured, processorCount));

	[Fact]
	public void ResultIsAlwaysAValidParallelOptionsValue() {
		// ParallelOptions.MaxDegreeOfParallelism throws for 0; the resolver must
		// never produce it (the raw setting used to reach ParallelOptions directly,
		// where an explicit 0 crashed the compare phase).
		for (int configured = -2; configured <= 4; configured++)
			for (int cpus = 0; cpus <= 4; cpus++)
				Assert.True(ScanEngine.CalculateMatchingParallelism(configured, cpus) >= 1);
	}
}
