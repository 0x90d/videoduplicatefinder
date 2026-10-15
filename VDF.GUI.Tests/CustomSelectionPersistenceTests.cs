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
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	// #929: preset save and reload both clone CustomSelectionData through this JSON
	// context. A path filter must survive those copies as well as Settings.json.
	public class CustomSelectionPersistenceTests {
		static CustomSelectionData Criteria() {
			var data = new CustomSelectionData {
				IgnoreGroupsWithCheckedItems = false,
				FileTypeSelection = 1,
				IdenticalSelection = 3,
				DateTimeSelection = 2,
				MinimumFileSize = 12,
				MaximumFileSize = 345,
				SimilarityFrom = 91,
				SimilarityTo = 99,
			};
			data.PathContains.Add(@"*D:\Media\Archiv*");
			data.PathContains.Add("*/电影/*");
			data.PathNotContains.Add(@"*D:\Keep*");
			data.PathNotContains.Add("*/backup/*");
			return data;
		}

		static void AssertCriteria(CustomSelectionData data) {
			Assert.Equal(new[] { @"*D:\Media\Archiv*", "*/电影/*" }, data.PathContains);
			Assert.Equal(new[] { @"*D:\Keep*", "*/backup/*" }, data.PathNotContains);
			Assert.False(data.IgnoreGroupsWithCheckedItems);
			Assert.Equal(1, data.FileTypeSelection);
			Assert.Equal(3, data.IdenticalSelection);
			Assert.Equal(2, data.DateTimeSelection);
			Assert.Equal(12, data.MinimumFileSize);
			Assert.Equal(345, data.MaximumFileSize);
			Assert.Equal(91, data.SimilarityFrom);
			Assert.Equal(99, data.SimilarityTo);
		}

		[Fact]
		public void SelectionJsonRoundTrip_PreservesAllCriteria() {
			string json = JsonSerializer.Serialize(Criteria(), GuiJsonContext.Default.CustomSelectionData);
			using var document = JsonDocument.Parse(json);
			Assert.Equal(new[] { @"*D:\Media\Archiv*", "*/电影/*" },
				document.RootElement.GetProperty("PathContains").EnumerateArray().Select(value => value.GetString()));
			Assert.Equal(new[] { @"*D:\Keep*", "*/backup/*" },
				document.RootElement.GetProperty("PathNotContains").EnumerateArray().Select(value => value.GetString()));

			var loaded = JsonSerializer.Deserialize(json, GuiJsonContext.Default.CustomSelectionData)!;

			AssertCriteria(loaded);
		}

		[Fact]
		public void SelectingPreset_RestoresAllCriteriaWithoutSharingEdits() {
			var preset = new CustomSelectionPreset { Name = "Archive cleanup", Data = Criteria() };
			var vm = new CustomSelectionVM(null!);

			vm.SelectedPreset = preset;

			AssertCriteria(vm.Data);
			vm.Data.PathContains.Clear();
			vm.Data.PathNotContains.Add("*/new/*");
			vm.Data.MinimumFileSize = 100;
			AssertCriteria(preset.Data);

			vm.SelectedPreset = preset;
			AssertCriteria(vm.Data);
		}

		[Fact]
		public void SettingsJsonRoundTrip_PreservesSavedPresetCriteria() {
			var settings = new SettingsFile();
			settings.CustomSelectionPresets.Add(new CustomSelectionPreset {
				Name = "Archive cleanup",
				Data = Criteria(),
			});
			string json = JsonSerializer.Serialize(settings, GuiJsonContext.Default.SettingsFile);

			var loaded = JsonSerializer.Deserialize(json, GuiJsonContext.Default.SettingsFile)!;

			var preset = Assert.Single(loaded.CustomSelectionPresets);
			Assert.Equal("Archive cleanup", preset.Name);
			AssertCriteria(preset.Data);
		}

		[Theory]
		[InlineData("{}")]
		[InlineData("{\"PathContains\":[],\"PathNotContains\":[]}")]
		public void SelectionJsonWithoutPathFilters_KeepsDefaults(string json) {
			var loaded = JsonSerializer.Deserialize(json, GuiJsonContext.Default.CustomSelectionData)!;

			Assert.Empty(loaded.PathContains);
			Assert.Empty(loaded.PathNotContains);
			Assert.True(loaded.IgnoreGroupsWithCheckedItems);
			Assert.Equal(999999999, loaded.MaximumFileSize);
			Assert.Equal(100, loaded.SimilarityTo);
		}
	}
}
