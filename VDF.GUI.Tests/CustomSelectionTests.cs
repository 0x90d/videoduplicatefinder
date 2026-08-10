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

using System.Diagnostics;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	/// <summary>
	/// Custom Selection planner (#864). Covers the three bugs the issue surfaced: the
	/// quadratic re-enumeration that froze the GUI for hours on six-digit result lists,
	/// the keeper that got checked along with everything else since the 2024 single-match
	/// fix (the reference item was inserted twice), and Windows path patterns silently
	/// matching nothing because MatchesSimpleExpression treats '\' as an escape character.
	/// </summary>
	public class CustomSelectionTests {

		static DuplicateItemVM Item(Guid group, string path, long size = 100 * 1024 * 1024,
				DateTime? created = null, float similarity = 100f, bool isImage = false,
				bool @checked = false, TimeSpan? duration = null, int frameSizeInt = 0) => new() {
			Checked = @checked,
			ItemInfo = new DuplicateItem {
				GroupId = group,
				Path = path,
				SizeLong = size,
				DateCreated = created ?? new DateTime(2020, 1, 1),
				Similarity = similarity,
				IsImage = isImage,
				Duration = duration ?? TimeSpan.FromMinutes(2),
				FrameSizeInt = frameSizeInt,
			}
		};

		static CustomSelectionData Data(Action<CustomSelectionData>? setup = null) {
			var data = new CustomSelectionData { IgnoreGroupsWithCheckedItems = false };
			setup?.Invoke(data);
			return data;
		}

		// ---- keeper regression (the reference item used to end up checked too) ----

		[Fact]
		public void FullGroupMatched_KeepsFirstUnchecked_ChecksTheRest() {
			var g = Guid.NewGuid();
			var a = Item(g, @"D:\Media\a.mp4");
			var b = Item(g, @"D:\Media\b.mp4");
			var c = Item(g, @"D:\Media\c.mp4");

			var plan = MainWindowVM.ComputeCustomSelection(new[] { a, b, c }, Data());

			Assert.Equal(new[] { a }, plan.Keepers);
			Assert.Equal(new[] { b, c }, plan.ToCheck);
		}

		[Fact]
		public void NoItemAppearsAsBothKeeperAndChecked() {
			var g1 = Guid.NewGuid();
			var g2 = Guid.NewGuid();
			var items = new[] {
				Item(g1, @"D:\Media\a.mp4"), Item(g1, @"D:\Media\b.mp4"),
				Item(g2, @"D:\Media\c.mp4"), Item(g2, @"D:\Media\d.mp4"),
			};

			var plan = MainWindowVM.ComputeCustomSelection(items, Data());

			Assert.Empty(plan.Keepers.Intersect(plan.ToCheck));
		}

		// ---- the 2024 single-match fix must survive ----

		[Fact]
		public void OnlyOneGroupMemberMatches_ThatMemberGetsChecked() {
			var g = Guid.NewGuid();
			var inPath = Item(g, @"D:\Wipe\a.mp4");
			var outside = Item(g, @"E:\Keep\a.mp4");

			var plan = MainWindowVM.ComputeCustomSelection(new[] { inPath, outside },
				Data(d => d.PathContains.Add(@"*D:\Wipe*")));

			Assert.Empty(plan.Keepers);
			Assert.Equal(new[] { inPath }, plan.ToCheck);
		}

		[Fact]
		public void PartialGroupMatch_ChecksAllMatched_UnmatchedAreTheSurvivors() {
			var g = Guid.NewGuid();
			var in1 = Item(g, @"D:\Wipe\a.mp4");
			var in2 = Item(g, @"D:\Wipe\b.mp4");
			var outside = Item(g, @"E:\Keep\a.mp4");

			var plan = MainWindowVM.ComputeCustomSelection(new[] { in1, in2, outside },
				Data(d => d.PathContains.Add(@"*D:\Wipe*")));

			Assert.Empty(plan.Keepers);
			Assert.Equal(new[] { in1, in2 }, plan.ToCheck);
		}

		// ---- Windows path patterns (backslash escape regression) ----

		[Fact]
		public void PathContains_WithSingleBackslashes_MatchesWindowsPaths() {
			var g = Guid.NewGuid();
			var inPath = Item(g, @"D:\Media\Archiv\file.mp4");
			var outside = Item(g, @"E:\Other\file.mp4");

			var plan = MainWindowVM.ComputeCustomSelection(new[] { inPath, outside },
				Data(d => d.PathContains.Add(@"*D:\Media\Archiv*")));

			Assert.Equal(new[] { inPath }, plan.ToCheck);
		}

		[Fact]
		public void PathNotContains_WithSingleBackslashes_Excludes() {
			var g = Guid.NewGuid();
			var protectedItem = Item(g, @"D:\Keep\file.mp4");
			var other = Item(g, @"E:\Other\file.mp4");

			var plan = MainWindowVM.ComputeCustomSelection(new[] { protectedItem, other },
				Data(d => d.PathNotContains.Add(@"*D:\Keep*")));

			Assert.Equal(new[] { other }, plan.ToCheck);
			Assert.Empty(plan.Keepers);
		}

		[Fact]
		public void PathFilter_WildcardNeedle_MatchesAcrossSeparators() {
			Assert.True(MainWindowVM.PathMatchesFilter(@"D:\Shows\season1\episode2.mkv", @"season?\ep*"));
			Assert.False(MainWindowVM.PathMatchesFilter(@"D:\Shows\seasonX\other.mkv", @"season?\ep*"));
		}

		// ---- criteria filters ----

		[Fact]
		public void SizeSimilarityAndFileType_FilterMatching() {
			var g = Guid.NewGuid();
			var big = Item(g, @"D:\a.mp4", size: 500L * 1024 * 1024);
			var small = Item(g, @"D:\b.mp4", size: 1L * 1024 * 1024);
			var lowSim = Item(g, @"D:\c.mp4", size: 500L * 1024 * 1024, similarity: 50f);
			var image = Item(g, @"D:\d.jpg", size: 500L * 1024 * 1024, isImage: true);

			var plan = MainWindowVM.ComputeCustomSelection(new[] { big, small, lowSim, image },
				Data(d => {
					d.MinimumFileSize = 10;      // MB - excludes small
					d.SimilarityFrom = 90;       // excludes lowSim
					d.FileTypeSelection = 1;     // videos only - excludes image
				}));

			// Subset of the group matched, so everything matched gets checked.
			Assert.Equal(new[] { big }, plan.ToCheck);
			Assert.Empty(plan.Keepers);
		}

		[Fact]
		public void IgnoreGroupsWithCheckedItems_SkipsTheWholeGroup() {
			var g1 = Guid.NewGuid();
			var g2 = Guid.NewGuid();
			var alreadyChecked = Item(g1, @"D:\a.mp4", @checked: true);
			var sibling = Item(g1, @"D:\b.mp4");
			var c = Item(g2, @"D:\c.mp4");
			var d2 = Item(g2, @"D:\d.mp4");

			var plan = MainWindowVM.ComputeCustomSelection(new[] { alreadyChecked, sibling, c, d2 },
				Data(d => d.IgnoreGroupsWithCheckedItems = true));

			Assert.Equal(new[] { c }, plan.Keepers);
			Assert.Equal(new[] { d2 }, plan.ToCheck);
		}

		// ---- date rules ----

		[Fact]
		public void DateRuleNewest_KeepsTheOldest() {
			var g = Guid.NewGuid();
			var oldest = Item(g, @"D:\a.mp4", created: new DateTime(2018, 1, 1));
			var newer = Item(g, @"D:\b.mp4", created: new DateTime(2022, 1, 1));
			var newest = Item(g, @"D:\c.mp4", created: new DateTime(2024, 1, 1));

			var plan = MainWindowVM.ComputeCustomSelection(new[] { newer, oldest, newest },
				Data(d => d.DateTimeSelection = 1));

			Assert.Equal(new[] { oldest }, plan.Keepers);
			Assert.Equal(new[] { newer, newest }, plan.ToCheck);
		}

		[Fact]
		public void DateRuleOldest_KeepsTheNewest_EvenOnPartialMatch() {
			var g = Guid.NewGuid();
			var oldMatched = Item(g, @"D:\Wipe\a.mp4", created: new DateTime(2018, 1, 1));
			var newMatched = Item(g, @"D:\Wipe\b.mp4", created: new DateTime(2024, 1, 1));
			var outside = Item(g, @"E:\Keep\a.mp4", created: new DateTime(2026, 1, 1));

			var plan = MainWindowVM.ComputeCustomSelection(new[] { oldMatched, newMatched, outside },
				Data(d => {
					d.DateTimeSelection = 2;
					d.PathContains.Add(@"*D:\Wipe*");
				}));

			Assert.Equal(new[] { newMatched }, plan.Keepers);
			Assert.Equal(new[] { oldMatched }, plan.ToCheck);
		}

		// ---- identity modes ----

		[Fact]
		public void IdenticalOnly_KeepsOneOfTheCluster_LeavesDifferentMemberAlone() {
			var g = Guid.NewGuid();
			var a = Item(g, @"D:\a.mp4", duration: TimeSpan.FromMinutes(2), frameSizeInt: 1920 * 1080);
			var clone = Item(g, @"D:\a-copy.mp4", duration: TimeSpan.FromMinutes(2), frameSizeInt: 1920 * 1080);
			var different = Item(g, @"D:\other.mp4", duration: TimeSpan.FromMinutes(3), frameSizeInt: 1280 * 720);

			var plan = MainWindowVM.ComputeCustomSelection(new[] { a, clone, different },
				Data(d => d.IdenticalSelection = 1));

			Assert.Equal(new[] { a }, plan.Keepers);
			Assert.Equal(new[] { clone }, plan.ToCheck);
			Assert.DoesNotContain(different, plan.ToCheck);
		}

		[Fact]
		public void NotIdentical_StaysWithinTheGroup() {
			var g1 = Guid.NewGuid();
			var g2 = Guid.NewGuid();
			var a = Item(g1, @"D:\a.mp4", duration: TimeSpan.FromMinutes(2));
			var aVariant = Item(g1, @"D:\a-lq.mp4", duration: TimeSpan.FromMinutes(3));
			var b = Item(g2, @"D:\b.mp4", duration: TimeSpan.FromMinutes(5));
			var bVariant = Item(g2, @"D:\b-lq.mp4", duration: TimeSpan.FromMinutes(6));

			var plan = MainWindowVM.ComputeCustomSelection(new[] { a, aVariant, b, bVariant },
				Data(d => d.IdenticalSelection = 3));

			// One keeper per group; nothing from group 2 rides along with group 1.
			Assert.Equal(new[] { a, b }, plan.Keepers);
			Assert.Equal(new[] { aVariant, bVariant }, plan.ToCheck);
		}

		// ---- the classic per-group commands' shared engine ----

		[Fact]
		public void ForEachGroupCluster_FirstClusterFormingMemberWins_OneClusterPerGroup() {
			var g = Guid.NewGuid();
			// "unique" forms no EqualsFull cluster; the engine must move on to b and
			// process its clone cluster - and then stop for this group.
			var unique = Item(g, @"D:\unique.mp4", duration: TimeSpan.FromMinutes(9));
			var b = Item(g, @"D:\b.mp4", duration: TimeSpan.FromMinutes(2));
			var bClone = Item(g, @"D:\b-copy.mp4", duration: TimeSpan.FromMinutes(2));

			var seen = new List<(DuplicateItemVM First, List<DuplicateItemVM> Cluster)>();
			MainWindowVM.ForEachGroupCluster(new[] { unique, b, bClone },
				(d, first) => d.EqualsFull(first),
				(first, cluster) => seen.Add((first, cluster)));

			var call = Assert.Single(seen);
			Assert.Same(b, call.First);
			Assert.Equal(new[] { bClone }, call.Cluster);
		}

		[Fact]
		public void ForEachGroupCluster_ExcludesFilterHiddenCandidates() {
			var g = Guid.NewGuid();
			var a = Item(g, @"D:\a.mp4");
			var hidden = Item(g, @"D:\b.mp4");
			hidden.IsVisibleInFilter = false;
			var visible = Item(g, @"D:\c.mp4");

			var seen = new List<List<DuplicateItemVM>>();
			MainWindowVM.ForEachGroupCluster(new[] { a, hidden, visible },
				(d, first) => d.EqualsButQuality(first),
				(first, cluster) => seen.Add(cluster));

			var cluster = Assert.Single(seen);
			Assert.Equal(new[] { visible }, cluster);
		}

		// ---- the #864 scale guard: quadratic behavior would take hours here ----

		[Fact]
		public void ReporterScale_184kItems_74kGroups_CompletesQuickly() {
			const int itemCount = 184_019;
			const int groupCount = 74_459;
			var groups = new Guid[groupCount];
			for (int i = 0; i < groupCount; i++)
				groups[i] = Guid.NewGuid();
			var items = new List<DuplicateItemVM>(itemCount);
			for (int i = 0; i < itemCount; i++)
				items.Add(Item(groups[i % groupCount],
					$@"D:\Media\Archiv\Kategorie{i % 500}\Aufnahme_{i:D7}_1080p.mp4",
					created: new DateTime(2020, 1, 1).AddMinutes(i)));

			var sw = Stopwatch.StartNew();
			var plan = MainWindowVM.ComputeCustomSelection(items,
				Data(d => d.PathContains.Add(@"*D:\Media\Archiv*")));
			sw.Stop();

			// Every group matched fully: one keeper each, the rest checked.
			Assert.Equal(groupCount, plan.Keepers.Count);
			Assert.Equal(itemCount - groupCount, plan.ToCheck.Count);
			// The old shape needed ~3 hours for this input; leave generous CI headroom.
			Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
				$"Custom selection took {sw.Elapsed} for {itemCount} items - quadratic behavior is back");
		}
	}
}
