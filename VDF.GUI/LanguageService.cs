// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ReactiveUI;

namespace VDF.GUI {
	public class LanguageService : ReactiveObject {
		private Dictionary<string, string> _translations = new();
		private string _currentLanguage = "en";

		public string CurrentLanguage {
			get => _currentLanguage;
			set {
				if (EqualityComparer<string>.Default.Equals(_currentLanguage, value))
					return;
				this.RaiseAndSetIfChanged(ref _currentLanguage, value);
				LoadLanguage(value);
			}
		}

		public void LoadLanguage(string langCode) {
			var path = Path.Combine("Assets", "Locales", $"{langCode}.json");
			if (File.Exists(path)) {
				var json = File.ReadAllText(path);
				_translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
				this.RaisePropertyChanged("Item[]");
			}
		}

		public string this[string key] => _translations.TryGetValue(key, out var val) ? val : key;
	}
}
