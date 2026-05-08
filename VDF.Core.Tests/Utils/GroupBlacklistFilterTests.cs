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

using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class GroupBlacklistFilterTests {
	static readonly Guid G1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
	static readonly Guid G2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
	static readonly Guid G3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

	static (Guid, string) Item(Guid g, string p) => (g, p);

	[Fact]
	public void EmptyBlacklist_ReturnsEmpty() {
		var items = new[] { Item(G1, "a"), Item(G1, "b") };
		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, new List<HashSet<string>>());
		Assert.Empty(result);
	}

	[Fact]
	public void NullBlacklist_ReturnsEmpty() {
		var items = new[] { Item(G1, "a"), Item(G1, "b") };
		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, null!);
		Assert.Empty(result);
	}

	[Fact]
	public void NoItems_ReturnsEmpty() {
		var blacklist = new List<HashSet<string>> { new() { "a", "b" } };
		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(Array.Empty<(Guid, string)>(), blacklist);
		Assert.Empty(result);
	}

	[Fact]
	public void ExactMatch_GroupIsBlacklisted() {
		var items = new[] { Item(G1, "a"), Item(G1, "b"), Item(G1, "c") };
		var blacklist = new List<HashSet<string>> { new() { "a", "b", "c" } };

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Single(result);
		Assert.Contains(G1, result);
	}

	[Fact]
	public void RemainingPathsAreSubset_GroupIsBlacklisted() {
		// Original blacklisted group was {a,b,c}. After deletion only {a,b} remain
		// in the current scan; the remaining group should still be filtered out.
		var items = new[] { Item(G1, "a"), Item(G1, "b") };
		var blacklist = new List<HashSet<string>> { new() { "a", "b", "c" } };

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Contains(G1, result);
	}

	[Fact]
	public void ExtraPathNotInBlacklist_GroupIsNotBlacklisted() {
		// Group has an extra file that the user never marked. The user only said
		// "{a,b,c} aren't matches" — this group also contains {d}, so it should
		// not be silently filtered.
		var items = new[] { Item(G1, "a"), Item(G1, "b"), Item(G1, "c"), Item(G1, "d") };
		var blacklist = new List<HashSet<string>> { new() { "a", "b", "c" } };

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Empty(result);
	}

	[Fact]
	public void MultipleBlacklistEntries_AnyCoveringEntryFilters() {
		var items = new[] { Item(G1, "x"), Item(G1, "y") };
		var blacklist = new List<HashSet<string>> {
			new() { "a", "b", "c" },
			new() { "x", "y", "z" }
		};

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Contains(G1, result);
	}

	[Fact]
	public void SharedPath_DoesNotLeakAcrossGroups() {
		// Two groups happen to share path "a" but are otherwise distinct. Only G1
		// is fully covered by the blacklist; G2 has an unblacklisted path "z".
		var items = new[] {
			Item(G1, "a"), Item(G1, "b"),
			Item(G2, "a"), Item(G2, "z")
		};
		var blacklist = new List<HashSet<string>> { new() { "a", "b" } };

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Contains(G1, result);
		Assert.DoesNotContain(G2, result);
	}

	[Fact]
	public void MultipleGroups_OnlyCoveredOnesFiltered() {
		var items = new[] {
			Item(G1, "a"), Item(G1, "b"),
			Item(G2, "c"), Item(G2, "d"),
			Item(G3, "e"), Item(G3, "f")
		};
		var blacklist = new List<HashSet<string>> {
			new() { "a", "b" },
			new() { "e", "f" }
		};

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Equal(2, result.Count);
		Assert.Contains(G1, result);
		Assert.Contains(G3, result);
		Assert.DoesNotContain(G2, result);
	}

	[Fact]
	public void PathMatching_RespectsBlacklistSetComparer_CaseInsensitive() {
		// When the blacklist set uses a case-insensitive comparer (as production
		// callers configure on Windows/macOS via PathComparer.ForCurrentPlatform),
		// a re-scan that produces differently-cased paths must still be filtered.
		var items = new[] { Item(G1, "A"), Item(G1, "B") };
		var blacklist = new List<HashSet<string>> {
			new(StringComparer.OrdinalIgnoreCase) { "a", "b" }
		};

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Contains(G1, result);
	}

	[Fact]
	public void PathMatching_RespectsBlacklistSetComparer_CaseSensitive() {
		// When the blacklist set uses an ordinal comparer (as on Linux), casing
		// differences must NOT match. Pins this code path so a future blanket
		// "always case-insensitive" change is a deliberate, visible decision.
		var items = new[] { Item(G1, "A"), Item(G1, "B") };
		var blacklist = new List<HashSet<string>> {
			new(StringComparer.Ordinal) { "a", "b" }
		};

		var result = GroupBlacklistFilter.ComputeBlacklistedGroupIds(items, blacklist);

		Assert.Empty(result);
	}
}
