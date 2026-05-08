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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VDF.Core.Utils {
	/// <summary>
	/// Persistence for the user-managed "not a match" group blacklist. Reads accept
	/// both the v1 envelope format and the legacy unversioned raw-array format
	/// produced by older builds. Writes always emit v1.
	/// </summary>
	public static class BlacklistStore {
		public const int CurrentVersion = 1;

		public sealed class Envelope {
			[JsonPropertyName("version")]
			public int Version { get; set; } = CurrentVersion;
			[JsonPropertyName("groups")]
			public List<HashSet<string>> Groups { get; set; } = new();
		}

		/// <summary>
		/// Loads the blacklist, returning an empty list if the file is missing,
		/// empty, or unreadable. A corrupt file is renamed aside (with a timestamp
		/// suffix) so the app can keep starting and the user can inspect it later.
		/// </summary>
		public static List<HashSet<string>> Load(string path, Action<string>? logWarning = null) {
			if (!File.Exists(path)) return new();
			try {
				if (new FileInfo(path).Length == 0) return new();
				using var stream = File.OpenRead(path);
				using var doc = JsonDocument.Parse(stream);
				return Parse(doc.RootElement);
			}
			catch (Exception ex) {
				QuarantineCorrupt(path, ex, logWarning);
				return new();
			}
		}

		public static Task SaveAsync(string path, List<HashSet<string>> groups, CancellationToken ct = default) {
			var envelope = new Envelope { Version = CurrentVersion, Groups = groups };
			return AtomicJsonWriter.WriteAsync(path, envelope, options: null, ct);
		}

		internal static List<HashSet<string>> Parse(JsonElement root) {
			List<HashSet<string>> groups;

			// v0: legacy raw array of arrays.
			if (root.ValueKind == JsonValueKind.Array) {
				groups = root.Deserialize<List<HashSet<string>>>() ?? new();
			}
			// v1+: { "version": N, "groups": [[...], [...]] }
			else if (root.ValueKind == JsonValueKind.Object &&
				root.TryGetProperty("groups", out var groupsEl) &&
				groupsEl.ValueKind == JsonValueKind.Array) {
				groups = groupsEl.Deserialize<List<HashSet<string>>>() ?? new();
			}
			else {
				throw new JsonException("Unknown BlacklistedGroups.json format");
			}

			// JSON deserialization uses the default (ordinal) comparer; rebuild each
			// set with the platform-appropriate path comparer so a different-cased
			// re-scan still matches.
			for (int i = 0; i < groups.Count; i++)
				groups[i] = new HashSet<string>(groups[i], PathComparer.ForCurrentPlatform);
			return groups;
		}

		static void QuarantineCorrupt(string path, Exception ex, Action<string>? logWarning) {
			try {
				string suffix = ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
				string corruptPath = path + suffix;
				int n = 1;
				while (File.Exists(corruptPath)) {
					corruptPath = path + suffix + "-" + n++;
				}
				File.Move(path, corruptPath);
				logWarning?.Invoke($"BlacklistedGroups.json was unreadable and has been moved aside to {corruptPath}: {ex.Message}");
			}
			catch (Exception moveEx) {
				logWarning?.Invoke($"BlacklistedGroups.json was unreadable and could not be moved aside: {moveEx.Message} (original error: {ex.Message})");
			}
		}
	}
}
