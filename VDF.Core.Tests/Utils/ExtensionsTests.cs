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

using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class ExtensionsTests {
	[Theory]
	[InlineData(0L, "0 B")]
	[InlineData(512L, "512.0 B")]
	[InlineData(1024L, "1.0 KB")]
	[InlineData(1536L, "1.5 KB")]
	[InlineData(1048576L, "1.0 MB")]
	[InlineData(1073741824L, "1.0 GB")]
	[InlineData(1099511627776L, "1.0 TB")]
	public void BytesToString_CorrectFormat(long bytes, string expected) {
		Assert.Equal(expected, bytes.BytesToString());
	}

	[Fact]
	public void BytesToMegaBytes_CorrectConversion() {
		Assert.Equal(1L, (1048576L).BytesToMegaBytes());
		Assert.Equal(0L, (512L).BytesToMegaBytes());
	}

	[Fact]
	public void Format_SecondsOnly() {
		var ts = new TimeSpan(0, 0, 0, 45);
		Assert.Equal("45s", ts.Format());
	}

	[Fact]
	public void Format_MinutesAndSeconds() {
		var ts = new TimeSpan(0, 0, 3, 15);
		Assert.Equal("3m, 15s", ts.Format());
	}

	[Fact]
	public void Format_HoursAndMinutes() {
		var ts = new TimeSpan(0, 2, 30, 0);
		Assert.Equal("2h, 30m", ts.Format());
	}

	[Fact]
	public void Format_HoursOnly_NoMinutes() {
		var ts = new TimeSpan(0, 5, 0, 0);
		Assert.Equal("5h", ts.Format());
	}

	[Fact]
	public void Format_DaysAndHours() {
		var ts = new TimeSpan(2, 3, 0, 0);
		Assert.Equal("2d, 3h", ts.Format());
	}

	[Fact]
	public void Format_DaysOnly_NoHours() {
		var ts = new TimeSpan(7, 0, 0, 0);
		Assert.Equal("7d", ts.Format());
	}

	[Fact]
	public void TrimMiliseconds_RemovesFractionalSeconds() {
		var ts = new TimeSpan(1, 2, 3, 4, 567);
		var trimmed = ts.TrimMiliseconds();
		Assert.Equal(new TimeSpan(1, 2, 3, 4), trimmed);
		Assert.Equal(0, trimmed.Milliseconds);
	}

	[Fact]
	public void Format_ZeroSeconds() {
		var ts = TimeSpan.Zero;
		Assert.Equal("0s", ts.Format());
	}
}
