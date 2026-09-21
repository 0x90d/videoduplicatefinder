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

namespace VDF.Core.Tests;

/// <summary>
/// The relative positions frames are sampled at. The scan, the thumbnail reload and the
/// Web UI's frame stepping all read them from <see cref="ScanEngine.BuildSamplePositions"/>;
/// the values are part of the database contract (gray bytes are keyed by them), so a
/// change here re-extracts every library.
/// </summary>
public class SamplePositionsTests {
	[Fact]
	public void OneFrame_IsTheMidpoint() =>
		Assert.Equal(new[] { 0.5f }, ScanEngine.BuildSamplePositions(1));

	[Fact]
	public void ThreeFrames_AreTheQuarters() =>
		Assert.Equal(new[] { 0.25f, 0.5f, 0.75f }, ScanEngine.BuildSamplePositions(3));

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void NoFrames_IsEmptyInsteadOfThrowing(int count) =>
		Assert.Empty(ScanEngine.BuildSamplePositions(count));

	[Fact]
	public void Positions_AreAccumulatedTheWayDatabasesWereKeyed() {
		// Accumulated, not (i + 1) / (n + 1): the two differ in the last float bits, and
		// the gray bytes of existing databases are keyed by duration * accumulated value.
		var positions = ScanEngine.BuildSamplePositions(50);

		Assert.Equal(50, positions.Count);
		float expected = 0f;
		for (int i = 0; i < 50; i++) {
			expected += 1.0F / 51;
			Assert.Equal(expected, positions[i]);
		}
		Assert.True(positions[0] > 0f && positions[^1] < 1f);
	}

	[Fact]
	public void ThumbnailReload_UsesTheSamePositionsAsTheScan() {
		var engine = new ScanEngine();
		engine.Settings.ThumbnailCount = 7;

		engine.EnsureThumbnailPositions();

		Assert.Equal(ScanEngine.BuildSamplePositions(7), engine.positionList);
	}
}
