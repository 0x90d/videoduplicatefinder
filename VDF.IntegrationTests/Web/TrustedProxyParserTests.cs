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

using System.Net;
using VDF.Web.Services;

namespace VDF.IntegrationTests.Web;

// The reverse-proxy trust lists come from env vars in a container. Parsing must
// be lenient — an invalid entry is skipped with a warning, never an exception:
// a typo must not crash-loop the container and lock the user out of the web UI.
public class TrustedProxyParserTests {
	[Fact]
	public void NoEnvironmentVariables_YieldsEmptyTrustAndNoWarnings() {
		var result = TrustedProxyParser.Parse(null, null);
		Assert.Empty(result.Proxies);
		Assert.Empty(result.Networks);
		Assert.Empty(result.Warnings);
	}

	[Fact]
	public void ValidEntriesAreParsed() {
		var result = TrustedProxyParser.Parse("172.17.0.1, ::1", "172.16.0.0/12;10.0.0.0/8");
		Assert.Equal(new[] { IPAddress.Parse("172.17.0.1"), IPAddress.IPv6Loopback }, result.Proxies);
		Assert.Equal(2, result.Networks.Count);
		Assert.Contains(IPNetwork.Parse("172.16.0.0/12"), result.Networks);
		Assert.Empty(result.Warnings);
	}

	[Fact]
	public void InvalidEntriesWarnAndAreSkipped_ValidOnesSurvive() {
		var result = TrustedProxyParser.Parse("not-an-ip, 192.168.1.1", "999.0.0.0/8 172.16.0.0/12");
		Assert.Single(result.Proxies);
		Assert.Single(result.Networks);
		Assert.Equal(2, result.Warnings.Count);
		Assert.Contains(result.Warnings, w => w.Contains("not-an-ip"));
		Assert.Contains(result.Warnings, w => w.Contains("999.0.0.0/8"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	[InlineData(",;, ")]
	public void BlankInput_YieldsNothing(string input) {
		var result = TrustedProxyParser.Parse(input, input);
		Assert.Empty(result.Proxies);
		Assert.Empty(result.Networks);
		Assert.Empty(result.Warnings);
	}
}
