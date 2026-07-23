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
	public class ResultsListBuilderTests {

		static DuplicateItemVM Item(Guid group, string path, long size = 100,
			float similarity = 100f, DateTime? created = null, TimeSpan? duration = null,
			bool isChecked = false) {
			var vm = new DuplicateItemVM {
				ItemInfo = new DuplicateItem {
					GroupId = group,
					Path = path,
					SizeLong = size,
					Similarity = similarity,
					DateCreated = created ?? new DateTime(2024, 1, 1),
					Duration = duration ?? TimeSpan.FromMinutes(1),
				}
			};
			vm.Checked = isChecked;
			return vm;
		}

		static ResultsBuildRequest Request(params DuplicateItemVM[] items) => new() {
			Items = items,
			// The default seams consult the scan engine's database state; tests run
			// without a database, so pin them to deterministic values.
			IsTombstone = _ => false,
			IsOffline = _ => false,
		};

		[Fact]
		public void Build_FlattensGroupsIntoHeaderPlusRows() {
			Guid g1 = Guid.NewGuid(), g2 = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(g1, "a1", size: 500), Item(g1, "a2", size: 100),
				Item(g2, "b1", size: 300), Item(g2, "b2", size: 300), Item(g2, "b3", size: 300)));

			Assert.Equal(2, result.Groups.Count);
			Assert.Equal(7, result.Rows.Count); // 2 headers + 5 rows
			var header1 = Assert.IsType<ResultsGroupHeader>(result.Rows[0]);
			Assert.All(result.Rows.Skip(1).Take(header1.FileCount),
				r => Assert.Same(header1, Assert.IsType<ResultsItemRow>(r).Group));
			Assert.Equal(1, result.Groups[0].GroupNumber);
			Assert.Equal(2, result.Groups[1].GroupNumber);
			Assert.Equal("Group 1", result.Groups[0].Title);
		}

		[Fact]
		public void WastedSpace_IsTotalMinusLargest_AndSortsDescendingByDefault() {
			Guid small = Guid.NewGuid(), big = Guid.NewGuid();
			// small group wastes 100, big group wastes 600
			var result = ResultsListBuilder.Build(Request(
				Item(small, "s1", size: 200), Item(small, "s2", size: 100),
				Item(big, "b1", size: 700), Item(big, "b2", size: 600)));

			Assert.Equal(big, result.Groups[0].GroupId);
			Assert.Equal(600, result.Groups[0].WastedBytes);
			Assert.Equal(100, result.Groups[1].WastedBytes);
		}

		[Fact]
		public void SizeModes_OrderMembersBySizeDescending() {
			Guid g = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(g, "mid", size: 200), Item(g, "big", size: 900), Item(g, "small", size: 50)));

			var paths = result.Groups[0].Rows.Select(r => r.Item.ItemInfo.Path).ToArray();
			Assert.Equal(new[] { "big", "mid", "small" }, paths);
		}

		[Fact]
		public void Ascending_FlipsGroupAndMemberOrder() {
			Guid small = Guid.NewGuid(), big = Guid.NewGuid();
			// big wastes 100, small wastes 50 — ascending puts small first
			var result = ResultsListBuilder.Build(Request(
				Item(big, "b1", size: 700), Item(big, "b2", size: 100),
				Item(small, "s1", size: 200), Item(small, "s2", size: 50)) with {
				SortDescending = false
			});

			Assert.Equal(small, result.Groups[0].GroupId);
			Assert.Equal(new[] { "b2", "b1" },
				result.Groups[1].Rows.Select(r => r.Item.ItemInfo.Path).ToArray());
		}

		[Fact]
		public void FileCountMode_SortsByCount_MembersKeepOriginalOrder() {
			Guid two = Guid.NewGuid(), three = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(two, "t1", size: 900), Item(two, "t2", size: 5),
				Item(three, "x3", size: 1), Item(three, "x1", size: 3), Item(three, "x2", size: 2)) with {
				SortMode = ResultsSortMode.FileCount
			});

			Assert.Equal(three, result.Groups[0].GroupId);
			Assert.Equal(new[] { "x3", "x1", "x2" },
				result.Groups[0].Rows.Select(r => r.Item.ItemInfo.Path).ToArray());
		}

		[Fact]
		public void SimilarityMode_UsesMaxMemberSimilarity() {
			Guid a = Guid.NewGuid(), b = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(a, "a1", similarity: 90f), Item(a, "a2", similarity: 95f),
				Item(b, "b1", similarity: 99f), Item(b, "b2", similarity: 80f)) with {
				SortMode = ResultsSortMode.Similarity
			});

			Assert.Equal(b, result.Groups[0].GroupId);
		}

		[Fact]
		public void DateCreatedMode_SortsGroupsByNewestMember() {
			Guid older = Guid.NewGuid(), newer = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(older, "o1", created: new DateTime(2020, 1, 1)),
				Item(older, "o2", created: new DateTime(2021, 1, 1)),
				Item(newer, "n1", created: new DateTime(2019, 1, 1)),
				Item(newer, "n2", created: new DateTime(2025, 6, 1))) with {
				SortMode = ResultsSortMode.DateCreated
			});

			Assert.Equal(newer, result.Groups[0].GroupId);
		}

		[Fact]
		public void FolderPathMode_AscendingSortsAlphabetically() {
			Guid z = Guid.NewGuid(), a = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(z, @"Z:\zebra.mp4"), Item(z, @"Z:\zebra2.mp4"),
				Item(a, @"A:\apple.mp4"), Item(a, @"A:\apple2.mp4")) with {
				SortMode = ResultsSortMode.FolderPath,
				SortDescending = false
			});

			Assert.Equal(a, result.Groups[0].GroupId);
		}

		[Fact]
		public void GroupsWithCheckedItemsMode_PutsCheckedGroupsFirst() {
			Guid plain = Guid.NewGuid(), checkedGroup = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(plain, "p1"), Item(plain, "p2"),
				Item(checkedGroup, "c1", isChecked: true), Item(checkedGroup, "c2")) with {
				SortMode = ResultsSortMode.GroupsWithCheckedItems
			});

			Assert.Equal(checkedGroup, result.Groups[0].GroupId);
			Assert.True(result.Groups[0].HasCheckedItems);
			Assert.False(result.Groups[1].HasCheckedItems);
		}

		[Fact]
		public void Filter_HidesMembers_DropsEmptyGroups_Renumbers() {
			Guid kept = Guid.NewGuid(), dropped = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(dropped, "hidden1"), Item(dropped, "hidden2"),
				Item(kept, "visible1", size: 500), Item(kept, "visible2"), Item(kept, "hidden3")) with {
				Filter = d => d.ItemInfo.Path.StartsWith("visible")
			});

			var group = Assert.Single(result.Groups);
			Assert.Equal(kept, group.GroupId);
			Assert.Equal(2, group.FileCount);
			Assert.Equal(1, group.GroupNumber);
			Assert.Equal(3, result.Rows.Count);
		}

		[Fact]
		public void CollapsedGroup_KeepsHeaderOmitsRows() {
			Guid open = Guid.NewGuid(), closed = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(open, "o1", size: 900), Item(open, "o2"),
				Item(closed, "c1"), Item(closed, "c2")) with {
				CollapsedGroups = new HashSet<Guid> { closed }
			});

			Assert.Equal(2, result.Groups.Count);
			var closedHeader = result.Groups.Single(g => g.GroupId == closed);
			Assert.True(closedHeader.IsCollapsed);
			Assert.Equal(2, closedHeader.FileCount); // stats still cover hidden members
			// flat list: open header + 2 rows + closed header, nothing after it
			Assert.Equal(4, result.Rows.Count);
			Assert.Same(closedHeader, result.Rows[^1]);
		}

		[Fact]
		public void PickBest_MarksExactlyTheReturnedItem_AndCarriesTooltip() {
			Guid g = Guid.NewGuid();
			var winner = Item(g, "winner", size: 900);
			var result = ResultsListBuilder.Build(Request(
				Item(g, "loser", size: 100), winner) with {
				PickBest = members => (members.First(m => m.ItemInfo.Path == "winner"), "decided by Resolution")
			});

			Assert.Equal(new[] { true, false },
				result.Groups[0].Rows.Select(r => r.IsBest).ToArray());
			var bestRow = result.Groups[0].Rows.Single(r => r.IsBest);
			Assert.Same(winner, bestRow.Item);
			// The badge tooltip lands on the best row only (#839).
			Assert.Equal("decided by Resolution", bestRow.BestTooltip);
			Assert.Null(result.Groups[0].Rows.Single(r => !r.IsBest).BestTooltip);
		}

		[Fact]
		public void PickBest_SkipsSingletonGroups() {
			Guid g = Guid.NewGuid();
			int calls = 0;
			// Singleton groups no longer render at all (#858), so PickBest must not run.
			var result = ResultsListBuilder.Build(Request(Item(g, "only")) with {
				PickBest = members => { calls++; return (members[0], null); }
			});

			Assert.Equal(0, calls);
			Assert.Empty(result.Groups);
		}

		[Fact]
		public void Stats_NegativeSizesCountAsZero() {
			Guid g = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(g, "gone", size: -1), Item(g, "there", size: 500)));

			Assert.Equal(500, result.Groups[0].TotalBytes);
			Assert.Equal(0, result.Groups[0].WastedBytes);
		}

		[Fact]
		public void Stats_TombstoneAndOfflineSeamsFeedOnDiskCount() {
			Guid g = Guid.NewGuid();
			var tomb = Item(g, "tomb");
			var off = Item(g, "offline");
			var live = Item(g, "live");
			var result = ResultsListBuilder.Build(Request(tomb, off, live) with {
				IsTombstone = d => ReferenceEquals(d, tomb),
				IsOffline = d => ReferenceEquals(d, off),
			});

			var header = result.Groups[0];
			Assert.True(header.HasTombstone);
			Assert.True(header.HasOffline);
			Assert.Equal(1, header.OnDiskCount);
		}

		[Fact]
		public void SimilarityRange_CollapsesWhenMinEqualsMax() {
			Guid g = Guid.NewGuid();
			var equal = ResultsListBuilder.Build(Request(
				Item(g, "a", similarity: 100f), Item(g, "b", similarity: 100f)));
			Assert.Equal("100 %", equal.Groups[0].SimilarityRangeDisplay);

			Guid g2 = Guid.NewGuid();
			var range = ResultsListBuilder.Build(Request(
				Item(g2, "a", similarity: 98.2f), Item(g2, "b", similarity: 100f)));
			Assert.Contains("–", range.Groups[0].SimilarityRangeDisplay);
		}

		[Fact]
		public void Summary_NormalGroup_HasCountSizeAndSavings() {
			Guid g = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(g, "a", size: 700L * 1024 * 1024), Item(g, "b", size: 300L * 1024 * 1024)));

			string summary = result.Groups[0].Summary;
			Assert.StartsWith("2 files · ", summary);
			Assert.Contains("save up to", summary);
			Assert.DoesNotContain("on disk", summary);
			Assert.DoesNotContain("previously deleted", summary);
		}

		[Fact]
		public void Summary_TombstoneGroup_SaysOnDiskAndPreviouslyDeleted() {
			Guid g = Guid.NewGuid();
			var tomb = Item(g, "tomb", size: 300);
			var live = Item(g, "live", size: 300);
			var result = ResultsListBuilder.Build(Request(tomb, live) with {
				IsTombstone = d => ReferenceEquals(d, tomb),
			});

			string summary = result.Groups[0].Summary;
			Assert.Contains("1 on disk", summary);
			Assert.Contains("previously deleted content", summary);
		}

		[Fact]
		public void Summary_NoWaste_OmitsSavings() {
			Guid g = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(g, "gone1", size: -1), Item(g, "gone2", size: 0)));

			Assert.DoesNotContain("save up to", result.Groups[0].Summary);
		}

		[Fact]
		public void EqualSortKeys_KeepFirstAppearanceOrder() {
			Guid first = Guid.NewGuid(), second = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(first, "f1", size: 100), Item(first, "f2", size: 100),
				Item(second, "s1", size: 100), Item(second, "s2", size: 100)));

			Assert.Equal(first, result.Groups[0].GroupId);
			Assert.Equal(second, result.Groups[1].GroupId);
		}

		[Fact]
		public void HasPartialClips_TrueWhenAnyMemberIsFlagged() {
			Guid g = Guid.NewGuid();
			var clip = Item(g, "clip");
			clip.ItemInfo.Flags = VDF.Core.DuplicateFlags.PartialClip;
			var result = ResultsListBuilder.Build(Request(Item(g, "source"), clip));

			Assert.True(result.HasPartialClips);
		}

		[Fact]
		public void HasPartialClips_FalseWithoutFlaggedMembers() {
			Guid g = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(Item(g, "a"), Item(g, "b")));

			Assert.False(result.HasPartialClips);
		}

		[Fact]
		public void HasPartialClips_SeesMembersOfCollapsedGroups() {
			Guid g = Guid.NewGuid();
			var clip = Item(g, "clip");
			clip.ItemInfo.Flags = VDF.Core.DuplicateFlags.PartialClip;
			var result = ResultsListBuilder.Build(Request(Item(g, "source"), clip) with {
				CollapsedGroups = new HashSet<Guid> { g }
			});

			Assert.True(result.HasPartialClips);
		}

		[Fact]
		public void CustomFormats_AreUsedForTitleAndSummary() {
			Guid g = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(g, "a", size: 500), Item(g, "b", size: 100)) with {
				Formats = new GroupSummaryFormats {
					GroupTitle = "Gruppe {0}",
					Files = "{0} Dateien",
					SaveUpTo = "spart bis zu {0}",
				}
			});

			Assert.Equal("Gruppe 1", result.Groups[0].Title);
			Assert.Contains("2 Dateien", result.Groups[0].Summary);
			Assert.Contains("spart bis zu", result.Groups[0].Summary);
		}

		// Regression #858: a similarity filter of 100-100 kept only the member carrying
		// exactly 100 (e.g. a later-joined member, or a partial-clip source which is
		// pinned at 100) and rendered a meaningless "group of one". A group must keep
		// at least two visible members or disappear entirely.
		[Fact]
		public void GroupsReducedToOneVisibleMember_AreHiddenEntirely() {
			Guid mixed = Guid.NewGuid(), exact = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(mixed, "m1", similarity: 100f), Item(mixed, "m2", similarity: 99.5f),
				Item(exact, "e1", similarity: 100f), Item(exact, "e2", similarity: 100f)) with {
				Filter = d => d.ItemInfo.Similarity >= 100f
			});

			var group = Assert.Single(result.Groups);
			Assert.Equal(exact, group.GroupId);
			Assert.Equal(3, result.Rows.Count); // 1 header + 2 rows, nothing from 'mixed'
		}

		[Fact]
		public void SingletonGroupsInInput_AreNotRendered() {
			Guid pair = Guid.NewGuid();
			var result = ResultsListBuilder.Build(Request(
				Item(Guid.NewGuid(), "orphan"),
				Item(pair, "a"), Item(pair, "b")));

			var group = Assert.Single(result.Groups);
			Assert.Equal(pair, group.GroupId);
			Assert.DoesNotContain(result.Rows.OfType<ResultsItemRow>(),
				r => r.Item.ItemInfo.Path == "orphan");
		}

		[Fact]
		public void CachedPathStatus_ClassifiesLikeTheEngine() {
			// missing + drive ready = tombstone; missing + drive gone = offline;
			// existing = neither. Separate instances because readiness is cached per root.
			var (tomb, off) = ResultsListBuilder.CreateCachedPathStatus(_ => false, _ => true);
			var item = Item(Guid.NewGuid(), Path.GetFullPath("missing.mp4"));
			Assert.True(tomb(item));
			Assert.False(off(item));

			var (tomb2, off2) = ResultsListBuilder.CreateCachedPathStatus(_ => false, _ => false);
			Assert.False(tomb2(item));
			Assert.True(off2(item));

			var (tomb3, off3) = ResultsListBuilder.CreateCachedPathStatus(_ => true, _ => throw new InvalidOperationException("existing files must not probe the drive"));
			Assert.False(tomb3(item));
			Assert.False(off3(item));
		}

		// Regression (Linux forum report): every rebuild ran File.Exists twice and a
		// DriveInfo query once PER MISSING FILE on the UI thread — thousands of
		// filesystem probes per list change on big scans. One existence probe per path,
		// one readiness probe per volume root.
		[Fact]
		public void CachedPathStatus_ProbesOncePerPath_AndOncePerRoot() {
			int existsCalls = 0, driveCalls = 0;
			// driveReady false => tombstone is false and Build falls through to isOffline,
			// so BOTH predicates run per item and must share the caches.
			var (tomb, off) = ResultsListBuilder.CreateCachedPathStatus(
				_ => { existsCalls++; return false; },
				_ => { driveCalls++; return false; });

			// Same call pattern as Build: isTombstone first, isOffline only when needed.
			var items = new[] {
				Item(Guid.NewGuid(), Path.GetFullPath("m1.mp4")),
				Item(Guid.NewGuid(), Path.GetFullPath("m2.mp4")),
				Item(Guid.NewGuid(), Path.GetFullPath("m3.mp4")),
			};
			foreach (var i in items) {
				bool t = tomb(i);
				if (!t) _ = off(i);
			}

			Assert.Equal(items.Length, existsCalls);
			Assert.Equal(1, driveCalls); // all paths share one volume root
		}

		// #846: resolution sort came back from v4.0. Same metric as the quality
		// criterion: FrameSizeInt (width + height).
		[Fact]
		public void ResolutionMode_OrdersMembersAndGroupsByFrameSize() {
			Guid uhd = Guid.NewGuid(), hd = Guid.NewGuid();
			var u1 = Item(uhd, "u-720p"); u1.ItemInfo.FrameSizeInt = 1280 + 720;
			var u2 = Item(uhd, "u-4k"); u2.ItemInfo.FrameSizeInt = 3840 + 2160;
			var h1 = Item(hd, "h-1080p"); h1.ItemInfo.FrameSizeInt = 1920 + 1080;
			var h2 = Item(hd, "h-720p"); h2.ItemInfo.FrameSizeInt = 1280 + 720;

			var result = ResultsListBuilder.Build(Request(u1, u2, h1, h2) with {
				SortMode = ResultsSortMode.Resolution
			});

			// Groups by highest member resolution, members descending within the group.
			Assert.Equal(uhd, result.Groups[0].GroupId);
			Assert.Equal(new[] { "u-4k", "u-720p" },
				result.Groups[0].Rows.Select(r => r.Item.ItemInfo.Path).ToArray());
			Assert.Equal(new[] { "h-1080p", "h-720p" },
				result.Groups[1].Rows.Select(r => r.Item.ItemInfo.Path).ToArray());
		}

		// #846: "BEST first" moves the badge carrier to the top of its group; the rest
		// keep the sort order. Off by default.
		[Fact]
		public void BestFirst_MovesTheBestRowToTheTop_OthersKeepSortOrder() {
			Guid g = Guid.NewGuid();
			var big = Item(g, "big", size: 900);
			var mid = Item(g, "mid", size: 200);
			var small = Item(g, "small", size: 50);

			var request = Request(mid, big, small) with {
				PickBest = members => (members.First(m => m.ItemInfo.Path == "small"), "tip"),
				BestFirst = true,
			};
			var result = ResultsListBuilder.Build(request);

			Assert.Equal(new[] { "small", "big", "mid" },
				result.Groups[0].Rows.Select(r => r.Item.ItemInfo.Path).ToArray());
			Assert.True(result.Groups[0].Rows[0].IsBest);

			// Without the toggle the size sort stands untouched.
			var plain = ResultsListBuilder.Build(request with { BestFirst = false });
			Assert.Equal(new[] { "big", "mid", "small" },
				plain.Groups[0].Rows.Select(r => r.Item.ItemInfo.Path).ToArray());
		}

		// #842: combined-mode algorithm flags aggregate to group-level badges.
		[Fact]
		public void CombinedModeFlags_AggregateToGroupBadges() {
			Guid combined = Guid.NewGuid(), plain = Guid.NewGuid();
			var carrier = Item(combined, "found-by-both");
			carrier.ItemInfo.Flags = VDF.Core.DuplicateFlags.GrayscaleMatched | VDF.Core.DuplicateFlags.PHashMatched;
			var partner = Item(combined, "partner");
			var p1 = Item(plain, "p1");
			var p2 = Item(plain, "p2");

			var result = ResultsListBuilder.Build(Request(carrier, partner, p1, p2));

			var combinedHeader = result.Groups.Single(h => h.GroupId == combined);
			Assert.True(combinedHeader.HasGrayscaleMatches);
			Assert.True(combinedHeader.HasPHashMatches);
			var plainHeader = result.Groups.Single(h => h.GroupId == plain);
			Assert.False(plainHeader.HasGrayscaleMatches);
			Assert.False(plainHeader.HasPHashMatches);
		}

		[Fact]
		public void BestFirst_FlattenedRowsFollowTheReorderedGroup() {
			Guid g = Guid.NewGuid();
			var a = Item(g, "a", size: 900);
			var b = Item(g, "b", size: 50);

			var result = ResultsListBuilder.Build(Request(a, b) with {
				PickBest = members => (members.First(m => m.ItemInfo.Path == "b"), null),
				BestFirst = true,
			});

			Assert.IsType<ResultsGroupHeader>(result.Rows[0]);
			Assert.Equal("b", Assert.IsType<ResultsItemRow>(result.Rows[1]).Item.ItemInfo.Path);
			Assert.Equal("a", Assert.IsType<ResultsItemRow>(result.Rows[2]).Item.ItemInfo.Path);
		}
	}
}
