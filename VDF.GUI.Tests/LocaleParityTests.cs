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

using System.Text.Json;

namespace VDF.GUI.Tests;

/// <summary>
/// Every locale file must carry exactly the keys en.json has — a missing key surfaces
/// in the UI as the raw key string. Guards the bulk key additions of the UI redesign.
/// </summary>
public class LocaleParityTests {

	static string LocalesFolder() {
		// Walk up from the test bin folder to the repo root.
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VDF.GUI", "Assets", "Locales", "en.json")))
			dir = dir.Parent;
		Assert.NotNull(dir);
		return Path.Combine(dir!.FullName, "VDF.GUI", "Assets", "Locales");
	}

	static HashSet<string> LoadKeys(string file) {
		using var doc = JsonDocument.Parse(File.ReadAllText(file));
		return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
	}

	[Fact]
	public void AllLocales_HaveExactlyTheEnglishKeySet() {
		string folder = LocalesFolder();
		var english = LoadKeys(Path.Combine(folder, "en.json"));
		Assert.NotEmpty(english);

		var locales = Directory.GetFiles(folder, "*.json")
			.Where(f => !string.Equals(Path.GetFileName(f), "en.json", StringComparison.OrdinalIgnoreCase))
			.ToList();
		Assert.NotEmpty(locales);

		foreach (var locale in locales) {
			var keys = LoadKeys(locale);
			var missing = english.Except(keys).OrderBy(k => k).ToList();
			var extra = keys.Except(english).OrderBy(k => k).ToList();
			Assert.True(missing.Count == 0 && extra.Count == 0,
				$"{Path.GetFileName(locale)}: missing [{string.Join(", ", missing)}], extra [{string.Join(", ", extra)}]");
		}
	}

	[Fact]
	public void AllLocales_HaveNoEmptyValues() {
		string folder = LocalesFolder();
		foreach (var file in Directory.GetFiles(folder, "*.json")) {
			using var doc = JsonDocument.Parse(File.ReadAllText(file));
			var empty = doc.RootElement.EnumerateObject()
				.Where(p => string.IsNullOrWhiteSpace(p.Value.GetString()))
				.Select(p => p.Name).ToList();
			Assert.True(empty.Count == 0, $"{Path.GetFileName(file)}: empty values for [{string.Join(", ", empty)}]");
		}
	}
}
