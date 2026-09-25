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
// #926: the metadata comparison window's table, read on demand.

using VDF.Core.Utils;
using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	public class MetadataCompareVMTests {
		static DuplicateItemVM Item(string name) => new() { ItemInfo = new DuplicateItem { Path = @"D:\clips\" + name } };

		static MetadataField C(string name, string value) => new(new(MetadataSectionKind.Container), name, value);
		static MetadataField A(string name, string value) => new(new(MetadataSectionKind.Stream, 1, "audio"), name, value);

		static readonly List<MetadataField> Wrong = new() {
			C("creation_time", "2023-08-15T12:34:56Z"), C("com.apple.quicktime.creationdate", "2023-08-15T12:34:56"), A("language", "und"),
		};
		static readonly List<MetadataField> Right = new() {
			C("creation_time", "2023-08-15T12:34:56Z"), C("com.apple.quicktime.creationdate", "2023-08-15T14:34:56+0200"), A("language", "und"),
		};

		static MetadataCompareVM Loaded(params IReadOnlyList<MetadataField>?[] files) {
			var vm = new MetadataCompareVM(files.Select((_, i) => Item($"clip{i}.mp4")).ToList());
			vm.Apply(files);
			return vm;
		}

		[Fact]
		public void OnlyDifferences_ByDefault_ShowsTheDifferingDateUnderItsSection() {
			var vm = Loaded(Wrong, Right, Wrong);

			Assert.True(vm.OnlyDifferences);
			Assert.Equal("1 of 3 fields differ", vm.SummaryText);
			Assert.Equal("Container", Assert.IsType<MetadataSectionRow>(vm.Rows[0]).Title);
			var row = Assert.IsType<MetadataValueRow>(Assert.Single(vm.Rows.Skip(1)));
			Assert.Equal("com.apple.quicktime.creationdate", row.Name);
			Assert.Equal(new[] { false, true, false }, row.Cells.Select(c => c.IsOdd));
			Assert.Equal("2023-08-15T14:34:56+0200", row.Cells[1].Text);
		}

		[Fact]
		public void AllFields_ShowEverySectionAndField() {
			var vm = Loaded(Wrong, Right, Wrong);
			vm.OnlyDifferences = false;

			var titles = vm.Rows.OfType<MetadataSectionRow>().Select(r => r.Title);
			Assert.Equal(new[] { "Container", "Stream 1: audio" }, titles);
			Assert.Equal(3, vm.Rows.OfType<MetadataValueRow>().Count());
			Assert.False(vm.AllEqual);
		}

		[Fact]
		public void RowsAreSpokenWithEveryFileAndWhichOneDiffers() {
			var vm = Loaded(Wrong, Right);
			var row = vm.Rows.OfType<MetadataValueRow>().Single();

			Assert.Equal("com.apple.quicktime.creationdate, clip0.mp4 2023-08-15T12:34:56 differs, clip1.mp4 2023-08-15T14:34:56+0200 differs",
				row.AccessibleName);
		}

		[Fact]
		public void MissingValuesAndUnreadableFiles_SayWhy() {
			var tagged = new List<MetadataField> { C("location", "+48.1+011.5/") };
			var vm = Loaded(tagged, tagged, new List<MetadataField>(), null);

			var row = vm.Rows.OfType<MetadataValueRow>().Single();
			Assert.Equal("(not set)", row.Cells[2].Text);
			Assert.True(row.Cells[2].IsMissing);
			Assert.True(row.Cells[2].IsOdd);
			Assert.Equal("file not available", row.Cells[3].Text);
			Assert.False(row.Cells[3].IsOdd);
			Assert.Equal(new[] { false, false, false, true }, vm.Files.Select(f => f.IsUnavailable));
		}

		[Fact]
		public void IdenticalTags_SayEverythingIsEqual_NoTagsSaySo() {
			var same = Loaded(Wrong, Wrong);
			Assert.True(same.AllEqual);
			Assert.Empty(same.Rows);
			same.OnlyDifferences = false;
			Assert.False(same.AllEqual);
			Assert.NotEmpty(same.Rows);

			var none = Loaded(new List<MetadataField>(), new List<MetadataField>());
			Assert.True(none.HasNoMetadata);
			Assert.False(none.AllEqual);
		}

		[Fact]
		public void Texts_ComeFromTheGivenTranslation() {
			var texts = MetadataCompareTexts.Default with { Summary = "{0} von {1} Feldern unterscheiden sich", CheckFile = "{0} auswählen", Container = "Container (de)" };
			var vm = new MetadataCompareVM(new[] { Item("a.mp4"), Item("b.mp4") }, texts);
			vm.Apply(new[] { Wrong, Right });

			Assert.Equal("1 von 3 Feldern unterscheiden sich", vm.SummaryText);
			Assert.Equal("a.mp4 auswählen", vm.Files[0].CheckName);
			Assert.Equal("Container (de)", ((MetadataSectionRow)vm.Rows[0]).Title);
		}

		[Fact]
		public async Task Load_ReadsEachFileOnce_OffTheCallingThread() {
			var items = new[] { Item("a.mp4"), Item("b.mp4") };
			var reads = new System.Collections.Concurrent.ConcurrentBag<string>();
			var vm = new MetadataCompareVM(items, reader: path => { reads.Add(path); return path.EndsWith("a.mp4") ? Wrong : Right; });

			await vm.LoadAsync();
			await vm.LoadAsync(); // a second call (window re-opened) reads nothing again

			Assert.Equal(2, reads.Count);
			Assert.False(vm.IsLoading);
			Assert.Single(vm.Rows.OfType<MetadataValueRow>());
		}

		// Ticking a file in the window is ticking it in the results list: the same object.
		[Fact]
		public void FileColumns_AreTheResultItems() {
			var a = Item("a.mp4");
			var vm = new MetadataCompareVM(new[] { a });
			vm.Files[0].Item.Checked = true;
			Assert.True(a.Checked);
		}
	}
}
