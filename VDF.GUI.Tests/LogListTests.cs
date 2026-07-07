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
using VDF.GUI.Data;

namespace VDF.GUI.Tests;

public class LogListTests {

	static readonly DateTime T0 = new(2026, 7, 7, 9, 41, 0);
	static readonly IReadOnlySet<string> NoExpanded = new HashSet<string>();

	static LogEntry Info(string msg, int secondsAfter = 0) => new(T0.AddSeconds(secondsAfter), LogSeverity.Info, msg);
	static LogEntry Warn(string msg, int secondsAfter = 0) => new(T0.AddSeconds(secondsAfter), LogSeverity.Warning, msg);
	static LogEntry Error(string msg, int secondsAfter = 0) => new(T0.AddSeconds(secondsAfter), LogSeverity.Error, msg);
	static LogEntry Session(string label, int secondsAfter = 0) => new(T0.AddSeconds(secondsAfter), LogSeverity.Info, label, IsSessionStart: true);

	static LogListResult Build(IReadOnlyList<LogEntry> entries, LogFilterKind filter = LogFilterKind.All,
		string? search = null, IReadOnlySet<string>? expanded = null) =>
		LogList.Build(entries, filter, search, expanded ?? NoExpanded, T0, "today");

	// ---------- CollapseKey / NormalizeMessage ----------

	[Fact]
	public void CollapseKey_SameShape_DifferentNumbersAndPaths_Match() {
		var a = Warn(@"Failed extracting thumbnail at 00:10:13 for 'E:\Archive\clip_0387.mp4', skipping that position.");
		var b = Warn(@"Failed extracting thumbnail at 00:02:07 for 'E:\Other\video 12.mp4', skipping that position.");
		Assert.Equal(LogList.CollapseKey(a), LogList.CollapseKey(b));
	}

	[Fact]
	public void CollapseKey_DifferentSeverity_DoesNotMatch() {
		var a = Warn("Something happened");
		var b = Error("Something happened");
		Assert.NotEqual(LogList.CollapseKey(a), LogList.CollapseKey(b));
	}

	[Fact]
	public void CollapseKey_DifferentText_DoesNotMatch() {
		Assert.NotEqual(
			LogList.CollapseKey(Info("Building file list...")),
			LogList.CollapseKey(Info("Scan done.")));
	}

	[Theory]
	[InlineData("Loaded 3/4 thumbnails", "Loaded #/# thumbnails")]
	[InlineData("at 00:10:13 position", "at #:#:# position")]
	[InlineData("file 'C:\\a 1.mp4' gone", "file '*' gone")]
	[InlineData("no digits here", "no digits here")]
	public void NormalizeMessage_ReplacesVolatileParts(string message, string expected) =>
		Assert.Equal(expected, LogList.NormalizeMessage(message));

	// ---------- counts ----------

	[Fact]
	public void Build_Counts_AreTotals_IndependentOfFilter() {
		var entries = new[] { Info("a"), Warn("b"), Warn("c"), Error("d") };
		var result = Build(entries, LogFilterKind.Errors);
		Assert.Equal(1, result.InfoCount);
		Assert.Equal(2, result.WarningCount);
		Assert.Equal(1, result.ErrorCount);
	}

	[Fact]
	public void Build_SessionStarts_DoNotCount() {
		var result = Build(new[] { Session("Scan"), Info("a") });
		Assert.Equal(1, result.InfoCount);
		Assert.Equal(0, result.WarningCount);
	}

	// ---------- sessions ----------

	[Fact]
	public void Build_NewestSessionFirst_RowsChronologicalWithin() {
		var entries = new[] {
			Session("Scan", 0), Info("first scan line", 1), Info("second scan line", 2),
			Session("Scan", 100), Info("later scan line", 101),
		};
		var result = Build(entries);
		var rows = result.Rows;
		Assert.Equal(5, rows.Count);
		var s1 = Assert.IsType<LogSessionRow>(rows[0]);
		Assert.Equal(T0.AddSeconds(100), s1.Timestamp);           // newest session on top
		Assert.Equal("later scan line", Assert.IsType<LogMessageRow>(rows[1]).Message);
		var s2 = Assert.IsType<LogSessionRow>(rows[2]);
		Assert.Equal(T0, s2.Timestamp);
		Assert.Equal("first scan line", Assert.IsType<LogMessageRow>(rows[3]).Message);
		Assert.Equal("second scan line", Assert.IsType<LogMessageRow>(rows[4]).Message);
	}

	[Fact]
	public void Build_PreSessionEntries_RenderLast_WithoutHeader() {
		var entries = new[] {
			Info("startup line", 0),
			Session("Scan", 10), Info("scan line", 11),
		};
		var rows = Build(entries).Rows;
		Assert.Equal(3, rows.Count);
		Assert.IsType<LogSessionRow>(rows[0]);
		Assert.Equal("scan line", Assert.IsType<LogMessageRow>(rows[1]).Message);
		Assert.Equal("startup line", Assert.IsType<LogMessageRow>(rows[2]).Message);
	}

	[Fact]
	public void Build_SessionWithNoVisibleRows_IsHidden() {
		var entries = new[] {
			Session("Scan", 0), Info("only info", 1),
		};
		var rows = Build(entries, LogFilterKind.Errors).Rows;
		Assert.Empty(rows);
	}

	// ---------- collapse ----------

	[Fact]
	public void Build_RepeatedMessages_CollapseToOneRow_AtFirstOccurrence() {
		var entries = new[] {
			Session("Scan", 0),
			Warn(@"Failed extracting thumbnail at 00:01:00 for 'a.mp4', skipping that position.", 1),
			Info("something else", 2),
			Warn(@"Failed extracting thumbnail at 00:02:00 for 'b.mp4', skipping that position.", 3),
			Warn(@"Failed extracting thumbnail at 00:03:00 for 'c.mp4', skipping that position.", 4),
		};
		var rows = Build(entries).Rows;
		Assert.Equal(3, rows.Count); // session + collapsed warn + info
		var collapsed = Assert.IsType<LogMessageRow>(rows[1]);
		Assert.Equal(3, collapsed.RepeatCount);
		Assert.True(collapsed.HasRepeats);
		Assert.NotNull(collapsed.GroupKey);
		Assert.Contains("'a.mp4'", collapsed.Message); // first occurrence represents the group
		Assert.Equal(3, collapsed.GroupEntries.Count);
		Assert.Equal("something else", Assert.IsType<LogMessageRow>(rows[2]).Message);
	}

	[Fact]
	public void Build_ExpandedGroup_EmitsAllMembers_FirstCarriesTheChip() {
		var entries = new[] {
			Session("Scan", 0),
			Warn("Repeated 1 thing", 1),
			Warn("Repeated 2 thing", 2),
			Warn("Repeated 3 thing", 3),
		};
		var collapsed = Assert.IsType<LogMessageRow>(Build(entries).Rows[1]);
		var expanded = Build(entries, expanded: new HashSet<string> { collapsed.GroupKey! }).Rows;
		Assert.Equal(4, expanded.Count); // session + 3 members
		var first = Assert.IsType<LogMessageRow>(expanded[1]);
		Assert.True(first.IsExpanded);
		Assert.Equal(3, first.RepeatCount);
		var second = Assert.IsType<LogMessageRow>(expanded[2]);
		Assert.False(second.IsExpanded);
		Assert.Equal(1, second.RepeatCount);
		Assert.Equal("Repeated 2 thing", second.Message);
	}

	[Fact]
	public void Build_SameMessageInDifferentSessions_DoesNotMergeAcross() {
		var entries = new[] {
			Session("Scan", 0), Warn("Repeated 1 thing", 1),
			Session("Scan", 100), Warn("Repeated 2 thing", 101),
		};
		var rows = Build(entries).Rows;
		Assert.Equal(4, rows.Count); // two sessions, one plain row each
		Assert.All(rows.OfType<LogMessageRow>(), r => Assert.Equal(1, r.RepeatCount));
	}

	[Fact]
	public void Build_SingleMessage_HasNoGroupKey() {
		var row = Assert.IsType<LogMessageRow>(Build(new[] { Info("solo") }).Rows[0]);
		Assert.Null(row.GroupKey);
		Assert.False(row.HasRepeats);
	}

	// ---------- filter ----------

	[Fact]
	public void Build_SeverityFilter_KeepsOnlyThatSeverity() {
		var entries = new[] { Info("i"), Warn("w"), Error("e") };
		var rows = Build(entries, LogFilterKind.Warnings).Rows;
		var row = Assert.IsType<LogMessageRow>(Assert.Single(rows));
		Assert.Equal("w", row.Message);
		Assert.True(row.IsWarning);
	}

	// ---------- search ----------

	[Fact]
	public void Build_Search_IsCaseInsensitive() {
		var rows = Build(new[] { Info("Database checkpoint"), Info("other") }, search: "DATABASE").Rows;
		Assert.Equal("Database checkpoint", Assert.IsType<LogMessageRow>(Assert.Single(rows)).Message);
	}

	[Fact]
	public void Build_Search_MatchingAnyGroupMember_KeepsCollapsedRow() {
		var entries = new[] {
			Warn("Failed loading image from file: 'a.png'.", 1),
			Warn("Failed loading image from file: 'clip_0387.png'.", 2),
		};
		var rows = Build(entries, search: "clip_0387").Rows;
		var row = Assert.IsType<LogMessageRow>(Assert.Single(rows));
		Assert.Equal(2, row.RepeatCount);
	}

	[Fact]
	public void Build_Search_NoMatch_HidesEverythingIncludingSession() {
		var entries = new[] { Session("Scan"), Info("hello") };
		Assert.Empty(Build(entries, search: "zzz").Rows);
	}

	// ---------- session header / line formatting ----------

	[Fact]
	public void FormatSessionHeader_SameDay_UsesTodayWord() =>
		Assert.Equal("Scan · today 09:41",
			LogList.FormatSessionHeader("Scan", T0, T0.AddHours(2), "today"));

	[Fact]
	public void FormatSessionHeader_OtherDay_UsesDate() {
		string header = LogList.FormatSessionHeader("Scan", T0, T0.AddDays(3), "today");
		Assert.StartsWith("Scan · ", header);
		Assert.DoesNotContain("today", header);
		Assert.EndsWith("09:41", header);
	}

	[Fact]
	public void FormatLine_TagsWarningsAndErrors_InfoStaysBare() {
		Assert.Equal("09:41:00 => plain", LogList.FormatLine(Info("plain")));
		Assert.Equal("09:41:00 => [WARNING] careful", LogList.FormatLine(Warn("careful")));
		Assert.Equal("09:41:00 => [ERROR] broken", LogList.FormatLine(Error("broken")));
		Assert.Contains("Scan", LogList.FormatLine(Session("Scan")));
	}

	// ---------- display ----------

	[Fact]
	public void DisplayMessage_MultilinePayload_ShowsFirstLineOnly() {
		var row = Assert.IsType<LogMessageRow>(Build(new[] { Error("Exception happened\r\n   at Foo.Bar()") }).Rows[0]);
		Assert.Equal("Exception happened …", row.DisplayMessage);
		Assert.Contains("at Foo.Bar()", row.Message);
	}
}
