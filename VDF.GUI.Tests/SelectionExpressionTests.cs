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

using VDF.Core.ViewModels;
using VDF.GUI.Utils;

namespace VDF.GUI.Tests {
	/// <summary>
	/// Custom-selection expression compilation (#844: in the AOT release builds even
	/// string.Contains was unresolvable because the trimmer had dropped the reflection
	/// metadata DynamicExpresso needs; SelectionExpression now roots every exposed type).
	/// These tests pin the expression surface the Expression Builder documents.
	/// </summary>
	public class SelectionExpressionTests {

		static DuplicateItem Item(string path = @"D:\videos\S01E02.mkv", string format = "jpg",
				long size = 5000, int minutes = 20, bool isImage = false) => new() {
			Path = path,
			Format = format,
			SizeLong = size,
			Duration = TimeSpan.FromMinutes(minutes),
			IsImage = isImage,
		};

		[Theory]
		// The exact expressions from #844:
		[InlineData("item.Format.Contains(\"jpg\")", true)]
		[InlineData("item.Path.Contains(\"jpg\")", false)]
		[InlineData("Regex.IsMatch(item.Path, \"jpg\")", false)]
		[InlineData("item.Format == \"jpg\"", true)]
		// The examples shown in the Expression Builder dialog:
		[InlineData("item.IsImage && item.SizeLong > 3000", false)]
		[InlineData("item.Path.Contains(\"videos\")", true)]
		[InlineData("item.Duration.Minutes > 15", true)]
		[InlineData("Regex.IsMatch(item.Path, \"S\\\\d+E\\\\d+\")", true)]
		// Other referenced types keep working:
		[InlineData("Math.Abs(item.SizeLong) >= 5000", true)]
		// Note: an int argument ("FromMinutes(15)") is ambiguous since .NET 10 added
		// the FromMinutes(long) overload next to FromMinutes(double).
		[InlineData("item.Duration > TimeSpan.FromMinutes(15.0)", true)]
		public void DocumentedExpressions_CompileAndEvaluate(string expression, bool expected) {
			var predicate = SelectionExpression.Compile(expression);
			Assert.Equal(expected, predicate(Item()));
		}

		[Fact]
		public void Assignment_IsStillAParseError_NotAWrite() {
			var item = Item();
			Assert.ThrowsAny<Exception>(() => SelectionExpression.Compile("item.IsBestSize = true"));
			Assert.False(item.IsBestSize);
		}

		[Fact]
		public void DangerousTypes_RemainUnavailable() {
			// PrimitiveTypes only: no Convert/Activator/Type registration.
			Assert.ThrowsAny<Exception>(() => SelectionExpression.Compile("Convert.ToInt32(item.Path) > 0"));
			Assert.ThrowsAny<Exception>(() => SelectionExpression.Compile("Activator.CreateInstance(null) != null"));
		}
	}

	/// <summary>
	/// #850: the compile-and-check pipeline behind the Expression Builder OK button is
	/// shared with the saved-preset menu. The pure partition step decides which matches
	/// are checked unconditionally and which whole-group matches await the ask-once
	/// policy (checking a full group would mark the whole group for deletion).
	/// </summary>
	public class PartitionExpressionMatchesTests {

		static ViewModels.DuplicateItemVM Vm(string path, long size) => new() {
			ItemInfo = new DuplicateItem { Path = path, SizeLong = size }
		};

		[Fact]
		public void PartialGroups_AreCheckedDirectly_FullGroupsAreSeparated() {
			var partialGroup = new List<ViewModels.DuplicateItemVM> { Vm("keep", 100), Vm("big1", 900) };
			var fullGroup = new List<ViewModels.DuplicateItemVM> { Vm("big2", 700), Vm("big3", 800) };
			var noMatchGroup = new List<ViewModels.DuplicateItemVM> { Vm("small1", 10), Vm("small2", 20) };

			var (partial, full) = ViewModels.MainWindowVM.PartitionExpressionMatches(
				new[] { partialGroup, fullGroup, noMatchGroup },
				item => item.SizeLong > 500);

			Assert.Equal(new[] { "big1" }, partial.Select(d => d.ItemInfo.Path));
			var fullPaths = Assert.Single(full).Select(d => d.ItemInfo.Path).ToArray();
			Assert.Equal(new[] { "big2", "big3" }, fullPaths);
		}

		[Fact]
		public void CompiledExpression_DrivesThePartition() {
			// End to end through SelectionExpression.Compile, the same entry the preset
			// menu uses (keeps the #844 AOT roots on the shared path).
			var group = new List<ViewModels.DuplicateItemVM> { Vm(@"D:\a.mkv", 5000), Vm(@"D:\b.mkv", 100) };
			var interpreter = SelectionExpression.Compile("item.SizeLong > 3000");

			var (partial, full) = ViewModels.MainWindowVM.PartitionExpressionMatches(
				new[] { group }, interpreter);

			Assert.Equal(new[] { @"D:\a.mkv" }, partial.Select(d => d.ItemInfo.Path));
			Assert.Empty(full);
		}
	}
}
