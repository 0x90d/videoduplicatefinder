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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

using System.Net;

namespace VDF.Web.Services {
	/// <summary>
	/// Parses the VDF_TRUSTED_PROXIES / VDF_TRUSTED_PROXY_NETWORKS environment
	/// variables into forwarded-header trust lists. Deliberately lenient: an
	/// invalid entry produces a warning and is skipped — a typo in an env var
	/// must never crash-loop the container and lock the user out of the UI.
	/// </summary>
	internal static class TrustedProxyParser {
		internal sealed record Result(
			List<IPAddress> Proxies,
			List<IPNetwork> Networks,
			List<string> Warnings);

		internal static Result Parse(string? proxyList, string? networkList) {
			var result = new Result(new(), new(), new());

			foreach (string value in SplitList(proxyList)) {
				if (IPAddress.TryParse(value, out IPAddress? proxy))
					result.Proxies.Add(proxy);
				else
					result.Warnings.Add($"Ignoring invalid IP address in VDF_TRUSTED_PROXIES: '{value}'");
			}

			foreach (string value in SplitList(networkList)) {
				if (IPNetwork.TryParse(value, out IPNetwork network))
					result.Networks.Add(network);
				else
					result.Warnings.Add($"Ignoring invalid CIDR network in VDF_TRUSTED_PROXY_NETWORKS: '{value}' (expected e.g. 172.16.0.0/12)");
			}

			return result;
		}

		internal static string[] SplitList(string? value) =>
			(value ?? string.Empty).Split(
				[',', ';', ' '],
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}
}
