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

namespace VDF.Core.Tests;

/// <summary>
/// The core locale files carry the scan's log lines and stage labels. A key missing from a
/// translation surfaces in the UI (and the log) as the raw key string. VDF.GUI.Tests guards
/// the GUI's locale folder the same way; this covers VDF.Core's.
/// </summary>
public class CoreLocaleParityTests {

	static string LocalesFolder() {
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VDF.Core", "Assets", "Locales", "en.json")))
			dir = dir.Parent;
		Assert.NotNull(dir);
		return Path.Combine(dir!.FullName, "VDF.Core", "Assets", "Locales");
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
		foreach (var file in Directory.GetFiles(LocalesFolder(), "*.json")) {
			using var doc = JsonDocument.Parse(File.ReadAllText(file));
			var empty = doc.RootElement.EnumerateObject()
				.Where(p => string.IsNullOrWhiteSpace(p.Value.GetString()))
				.Select(p => p.Name).ToList();
			Assert.True(empty.Count == 0, $"{Path.GetFileName(file)}: empty values for [{string.Join(", ", empty)}]");
		}
	}

	/// <summary>
	/// The engine resolves stage labels through LanguageService at runtime; a typo'd key would
	/// otherwise only show up as raw text in a user's status bar.
	/// </summary>
	[Theory]
	[InlineData("Scan.Stage.PartialCompare", "comparing partial clips")]
	[InlineData("Scan.Stage.PartialVisualVerify", "verifying partial clips")]
	public void EnglishStageLabels_ResolveThroughLanguageService(string key, string expected) =>
		Assert.Equal(expected, LanguageService.Instance.Get("en", key));
}
