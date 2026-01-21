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

using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace VDF.Core {
	public sealed class LanguageService {
		static readonly Lazy<LanguageService> LazyInstance = new(() => new LanguageService());
		public static LanguageService Instance => LazyInstance.Value;

		readonly object gate = new();
		Dictionary<string, string> translations = new();
		string currentLanguage = string.Empty;

		public string Get(string languageCode, string key, params object[] args) {
			EnsureLanguage(languageCode);
			var value = translations.TryGetValue(key, out var translated) ? translated : key;
			return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
		}

		void EnsureLanguage(string? languageCode) {
			var normalized = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim();
			lock (gate) {
				if (normalized == currentLanguage && translations.Count > 0)
					return;
				LoadLanguage(normalized);
			}
		}

		void LoadLanguage(string languageCode) {
			var assembly = Assembly.GetExecutingAssembly();
			var resourceName = $"VDF.Core.Assets.Locales.{languageCode}.json";
			using var stream = assembly.GetManifestResourceStream(resourceName);
			if (stream == null) {
				if (languageCode != "en") {
					LoadLanguage("en");
					return;
				}
				translations = new Dictionary<string, string>();
				currentLanguage = languageCode;
				return;
			}
			using var reader = new StreamReader(stream);
			var json = reader.ReadToEnd();
			translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
			currentLanguage = languageCode;
		}
	}
}
