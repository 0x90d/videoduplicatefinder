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
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	public class ResultsScrollAnchorTests {

		static DuplicateItemVM Item(Guid group, string path, long size = 100) => new() {
			ItemInfo = new DuplicateItem {
				GroupId = group,
				Path = path,
				SizeLong = size,
				Similarity = 100f,
				DateCreated = new DateTime(2024, 1, 1),
				Duration = TimeSpan.FromMinutes(1),
			}
		};

		static ResultsBuildResult Build(IEnumerable<DuplicateItemVM> items, IReadOnlySet<Guid>? collapsed = null) =>
			ResultsListBuilder.Build(new ResultsBuildRequest {
				Items = items.ToList(),
				IsTombstone = _ => false,
				IsOffline = _ => false,
				CollapsedGroups = collapsed,
			});

		static List<Guid> Order(ResultsBuildResult r) => r.Groups.ConvertAll(g => g.GroupId);

		[Fact]
		public void NullAnchor_OrEmptyList_ReturnsNull() {
			var g = Guid.NewGuid();
			var built = Build(new[] { Item(g, "a"), Item(g, "b") });
			Assert.Null(ResultsScrollAnchor.FindRestoreTarget(null, Order(built), built.Rows));
			Assert.Null(ResultsScrollAnchor.FindRestoreTarget(built.Rows[1], Order(built), new List<object>()));
		}

		[Fact]
		public void SurvivingItem_ReturnsItsNewRow() {
			var g1 = Guid.NewGuid();
			var g2 = Guid.NewGuid();
			var items = new[] { Item(g1, "a1", 500), Item(g1, "a2"), Item(g2, "b1", 300), Item(g2, "b2") };
			var before = Build(items);
			var anchor = before.Rows.OfType<ResultsItemRow>().First(r => r.Item.ItemInfo.Path == "b1");

			var after = Build(items); // same content, all row objects replaced
			var target = ResultsScrollAnchor.FindRestoreTarget(anchor, Order(before), after.Rows);

			var row = Assert.IsType<ResultsItemRow>(target);
			Assert.Same(anchor.Item, row.Item);
			Assert.NotSame(anchor, row);
		}

		[Fact]
		public void DeletedItem_GroupSurvives_ReturnsGroupHeader() {
			var g = Guid.NewGuid();
			var a = Item(g, "a", 500);
			var b = Item(g, "b");
			var c = Item(g, "c");
			var before = Build(new[] { a, b, c });
			var anchor = before.Rows.OfType<ResultsItemRow>().First(r => r.Item == b);

			var after = Build(new[] { a, c });
			var target = ResultsScrollAnchor.FindRestoreTarget(anchor, Order(before), after.Rows);

			Assert.Equal(g, Assert.IsType<ResultsGroupHeader>(target).GroupId);
		}

		[Fact]
		public void CollapsedGroup_AnchorItemRowGone_ReturnsItsHeader() {
			var g = Guid.NewGuid();
			var items = new[] { Item(g, "a", 500), Item(g, "b") };
			var before = Build(items);
			var anchor = before.Rows.OfType<ResultsItemRow>().First();

			var after = Build(items, collapsed: new HashSet<Guid> { g });
			var target = ResultsScrollAnchor.FindRestoreTarget(anchor, Order(before), after.Rows);

			Assert.Equal(g, Assert.IsType<ResultsGroupHeader>(target).GroupId);
		}

		[Fact]
		public void DeletedGroup_ReturnsNextSurvivingGroup_InOldDisplayOrder() {
			// Wasted-space descending: g1 (300) before g2 (200) before g3 (100).
			var g1 = Guid.NewGuid();
			var g2 = Guid.NewGuid();
			var g3 = Guid.NewGuid();
			var grp1 = new[] { Item(g1, "a1", 1000), Item(g1, "a2", 300) };
			var grp2 = new[] { Item(g2, "b1", 600), Item(g2, "b2", 200) };
			var grp3 = new[] { Item(g3, "c1", 200), Item(g3, "c2", 100) };
			var before = Build(grp1.Concat(grp2).Concat(grp3));
			var anchor = before.Groups.First(h => h.GroupId == g2);

			var after = Build(grp1.Concat(grp3)); // g2 fully deleted
			var target = ResultsScrollAnchor.FindRestoreTarget(anchor, Order(before), after.Rows);

			Assert.Equal(g3, Assert.IsType<ResultsGroupHeader>(target).GroupId);
		}

		[Fact]
		public void DeletedLastGroup_FallsBackToPreviousSurvivingGroup() {
			var g1 = Guid.NewGuid();
			var g2 = Guid.NewGuid();
			var grp1 = new[] { Item(g1, "a1", 1000), Item(g1, "a2", 100) };
			var grp2 = new[] { Item(g2, "b1", 600), Item(g2, "b2", 100) };
			var before = Build(grp1.Concat(grp2));
			var anchor = before.Groups.First(h => h.GroupId == g2);

			var after = Build(grp1);
			var target = ResultsScrollAnchor.FindRestoreTarget(anchor, Order(before), after.Rows);

			Assert.Equal(g1, Assert.IsType<ResultsGroupHeader>(target).GroupId);
		}

		[Fact]
		public void DetailsRowAnchor_MapsToItsItem() {
			var g = Guid.NewGuid();
			var items = new[] { Item(g, "a", 500), Item(g, "b") };
			var before = Build(items);
			var itemRow = before.Rows.OfType<ResultsItemRow>().First(r => r.Item.ItemInfo.Path == "b");
			var anchor = new ResultsDetailsRow(itemRow);

			var after = Build(items);
			var target = ResultsScrollAnchor.FindRestoreTarget(anchor, Order(before), after.Rows);

			Assert.Same(itemRow.Item, Assert.IsType<ResultsItemRow>(target).Item);
		}

		[Fact]
		public void UnknownAnchorGroup_ReturnsNull() {
			var g = Guid.NewGuid();
			var built = Build(new[] { Item(g, "a", 500), Item(g, "b") });
			// Anchor from a list state that no longer exists at all (e.g. stale reference
			// whose group id never appears in the old order) — leave the scroll alone.
			var foreign = Build(new[] { Item(Guid.NewGuid(), "x", 500), Item(Guid.NewGuid(), "x2") });
			var target = ResultsScrollAnchor.FindRestoreTarget(foreign.Rows[0], Order(built), built.Rows);
			Assert.Null(target);
		}
	}
}
