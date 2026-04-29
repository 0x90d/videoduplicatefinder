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

namespace VDF.Core.Tests;

public class DurationToleranceTests {
	[Fact]
	public void Percent_OnlyPercent_ReturnsPercentSeconds() {
		var s = new Settings { PercentDurationDifference = 1d };
		Assert.Equal(36d, s.GetDurationToleranceSeconds(3600d), precision: 6);
		Assert.Equal(0.6d, s.GetDurationToleranceSeconds(60d), precision: 6);
	}

	[Fact]
	public void Percent_WithMinSeconds_RaisesToFloor() {
		var s = new Settings { PercentDurationDifference = 1d, DurationDifferenceMinSeconds = 5d };
		// 1% of 60s = 0.6s — Min=5 raises it.
		Assert.Equal(5d, s.GetDurationToleranceSeconds(60d), precision: 6);
		// 1% of 3600s = 36s — Min=5 has no effect.
		Assert.Equal(36d, s.GetDurationToleranceSeconds(3600d), precision: 6);
	}

	[Fact]
	public void Percent_WithMaxSeconds_CapsToCeiling() {
		var s = new Settings { PercentDurationDifference = 1d, DurationDifferenceMaxSeconds = 10d };
		// 1% of 3600s = 36s — Max=10 caps it.
		Assert.Equal(10d, s.GetDurationToleranceSeconds(3600d), precision: 6);
		// 1% of 60s = 0.6s — Max has no effect.
		Assert.Equal(0.6d, s.GetDurationToleranceSeconds(60d), precision: 6);
	}

	[Fact]
	public void Percent_WithMinAndMax_ClampsBetween() {
		var s = new Settings {
			PercentDurationDifference = 1d,
			DurationDifferenceMinSeconds = 5d,
			DurationDifferenceMaxSeconds = 10d,
		};
		Assert.Equal(5d, s.GetDurationToleranceSeconds(60d), precision: 6);     // 0.6 → 5
		Assert.Equal(7d, s.GetDurationToleranceSeconds(700d), precision: 6);    // 7 stays
		Assert.Equal(10d, s.GetDurationToleranceSeconds(3600d), precision: 6);  // 36 → 10
	}

	[Fact]
	public void PercentZero_AllZero_ReturnsZero() {
		var s = new Settings { PercentDurationDifference = 0d };
		Assert.Equal(0d, s.GetDurationToleranceSeconds(3600d));
	}

	// Regression for issue #730: Percent=0 with Max>0 must yield a flat seconds tolerance,
	// not be clamped down to 0 by Math.Min(percentSeconds, Max).
	[Fact]
	public void PercentZero_WithMaxOnly_UsesMaxAsFlatTolerance() {
		var s = new Settings { PercentDurationDifference = 0d, DurationDifferenceMaxSeconds = 15d };
		Assert.Equal(15d, s.GetDurationToleranceSeconds(3600d), precision: 6);
		Assert.Equal(15d, s.GetDurationToleranceSeconds(60d), precision: 6);
	}

	[Fact]
	public void PercentZero_WithMinOnly_UsesMinAsFlatTolerance() {
		var s = new Settings { PercentDurationDifference = 0d, DurationDifferenceMinSeconds = 8d };
		Assert.Equal(8d, s.GetDurationToleranceSeconds(3600d), precision: 6);
	}

	[Fact]
	public void PercentZero_WithMinAndMax_UsesLargerBound() {
		var s = new Settings {
			PercentDurationDifference = 0d,
			DurationDifferenceMinSeconds = 5d,
			DurationDifferenceMaxSeconds = 15d,
		};
		// With percent disabled, both bounds act as flat tolerances; pick the most permissive.
		Assert.Equal(15d, s.GetDurationToleranceSeconds(3600d), precision: 6);
	}
}
