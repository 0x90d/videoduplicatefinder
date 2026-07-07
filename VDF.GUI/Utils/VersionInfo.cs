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

using System;
using System.Linq;
using System.Reflection;

namespace VDF.GUI.Utils {
	// Build/version identity for the About box, the window titlebar and the diagnostics
	// report. Releases all publish to the single "4.1.x" tag, so the git commit baked in
	// at build time (see Directory.Build.props) is the meaningful build identifier.
	public static class VersionInfo {
		static readonly Assembly Asm = Assembly.GetEntryAssembly() ?? typeof(VersionInfo).Assembly;

		// Numeric assembly version, e.g. "4.1.0".
		public static string Version { get; } = Asm.GetName().Version is { } v
			? $"{v.Major}.{v.Minor}.{v.Build}"
			: "4.1.0";

		// Short git commit, e.g. "a225a45", or "unknown" when built without git.
		public static string CommitId { get; } = Metadata("CommitId") ?? "unknown";

		// Build date "yyyy-MM-dd", or empty when unavailable.
		public static string BuildDate { get; } = Metadata("BuildDate") ?? string.Empty;

		static bool HasCommit => CommitId.Length > 0 && CommitId != "unknown";

		// Compact form for the titlebar, e.g. "4.1.0 (a225a45)".
		public static string ShortDisplay => HasCommit ? $"{Version} ({CommitId})" : Version;

		// Verbose form for the About box, e.g. "4.1.0 (build a225a45, 2026-07-01)".
		public static string LongDisplay {
			get {
				if (!HasCommit)
					return Version;
				return string.IsNullOrEmpty(BuildDate)
					? $"{Version} (build {CommitId})"
					: $"{Version} (build {CommitId}, {BuildDate})";
			}
		}

		static string? Metadata(string key) {
			var value = Asm.GetCustomAttributes<AssemblyMetadataAttribute>()
				.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal))?.Value?.Trim();
			return string.IsNullOrEmpty(value) ? null : value;
		}
	}
}
