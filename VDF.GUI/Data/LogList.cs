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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using VDF.Core.Utils;

namespace VDF.GUI.Data {

	public enum LogFilterKind {
		All,
		Info,
		Warnings,
		Errors,
	}

	public abstract class LogRow { }

	/// <summary>Session separator ("Scan · today 09:41") between scan runs.</summary>
	public sealed class LogSessionRow : LogRow {
		public LogSessionRow(string header, DateTime timestamp) {
			Header = header;
			Timestamp = timestamp;
		}
		public string Header { get; }
		public DateTime Timestamp { get; }
	}

	/// <summary>
	/// One visible log line. <see cref="RepeatCount"/> &gt; 1 marks a collapsed run of
	/// similar messages ("× N" chip); <see cref="IsExpanded"/> is true on the first
	/// member of an expanded group (its chip collapses the group again).
	/// </summary>
	public sealed class LogMessageRow : LogRow {
		public LogMessageRow(LogEntry entry, IReadOnlyList<LogEntry> groupEntries, string? groupKey, int repeatCount, bool isExpanded) {
			Entry = entry;
			GroupEntries = groupEntries;
			GroupKey = groupKey;
			RepeatCount = repeatCount;
			IsExpanded = isExpanded;
		}
		public LogEntry Entry { get; }
		/// <summary>All entries this row stands for (itself only unless collapsed).</summary>
		public IReadOnlyList<LogEntry> GroupEntries { get; }
		/// <summary>Stable key of the collapse group; null when the row is not collapsible.</summary>
		public string? GroupKey { get; }
		public int RepeatCount { get; }
		public bool IsExpanded { get; }

		public string Message => Entry.Message;
		public string TimeText => Entry.Timestamp.ToString("HH:mm:ss");
		public bool IsWarning => Entry.Severity == LogSeverity.Warning;
		public bool IsError => Entry.Severity == LogSeverity.Error;
		public bool HasRepeats => RepeatCount > 1;
		public string RepeatText => $"× {RepeatCount}";
		// Multi-line payloads (exception stack traces) render as a single trimmed line;
		// the full text stays available via tooltip and copy.
		public string DisplayMessage {
			get {
				int nl = Message.IndexOfAny(new[] { '\r', '\n' });
				return nl < 0 ? Message : Message[..nl] + " …";
			}
		}
		/// <summary>Hover text: the untrimmed message, size-capped (see LogList.TooltipText).</summary>
		public string TooltipMessage => LogList.TooltipText(Message);
	}

	/// <summary>
	/// One scanning-screen tail line: the collapsed display form plus the full message
	/// for the hover tooltip, so a line clipped by the window width can still be read
	/// in place (#836).
	/// </summary>
	public sealed record LogTailRow(string Display, string Tooltip);

	public sealed class LogListResult {
		internal LogListResult(IReadOnlyList<LogRow> rows, int infoCount, int warningCount, int errorCount) {
			Rows = rows;
			InfoCount = infoCount;
			WarningCount = warningCount;
			ErrorCount = errorCount;
		}
		public IReadOnlyList<LogRow> Rows { get; }
		/// <summary>Counts over ALL message entries — the filter chips show totals, not what the current filter left visible.</summary>
		public int InfoCount { get; }
		public int WarningCount { get; }
		public int ErrorCount { get; }
	}

	/// <summary>
	/// Pure logic behind the redesigned Log view (redesign stage 5): splits the entry
	/// stream into sessions (newest session first, chronological within), collapses
	/// repeated messages into one "× N" row, and applies the severity chip + search box.
	/// </summary>
	public static class LogList {

		/// <summary>
		/// Collapse key: messages that differ only in numbers (timestamps, counts) or
		/// quoted payloads (file paths) count as repeats of the same message. Severity is
		/// part of the key so an info line never merges with a warning that happens to
		/// share its shape.
		/// </summary>
		public static string CollapseKey(LogEntry entry) =>
			((int)entry.Severity).ToString(CultureInfo.InvariantCulture) + "|" + NormalizeMessage(entry.Message);

		internal static string NormalizeMessage(string message) {
			var sb = new StringBuilder(message.Length);
			bool inDigits = false;
			char quote = '\0';
			foreach (char c in message) {
				if (quote != '\0') {           // inside a quoted span: swallow until the closing quote
					if (c == quote) {
						sb.Append('*').Append(quote);
						quote = '\0';
					}
					continue;
				}
				if (c == '\'' || c == '"') {
					quote = c;
					sb.Append(c);
					inDigits = false;
					continue;
				}
				if (char.IsDigit(c)) {
					if (!inDigits) sb.Append('#');
					inDigits = true;
					continue;
				}
				inDigits = false;
				sb.Append(c);
			}
			return sb.ToString();
		}

		/// <summary>"Scan · today 09:41" for same-day sessions, "Scan · 05.07.2026 09:41" otherwise.</summary>
		public static string FormatSessionHeader(string label, DateTime timestamp, DateTime now, string todayWord) =>
			timestamp.Date == now.Date
				? $"{label} · {todayWord} {timestamp:HH:mm}"
				: $"{label} · {timestamp.ToString("d", CultureInfo.CurrentCulture)} {timestamp:HH:mm}";

		public static bool MatchesFilter(LogEntry entry, LogFilterKind filter) => filter switch {
			LogFilterKind.Info => entry.Severity == LogSeverity.Info,
			LogFilterKind.Warnings => entry.Severity == LogSeverity.Warning,
			LogFilterKind.Errors => entry.Severity == LogSeverity.Error,
			_ => true,
		};

		public static LogListResult Build(IReadOnlyList<LogEntry> entries, LogFilterKind filter, string? search,
			IReadOnlySet<string> expandedGroups, DateTime now, string todayWord) {

			int info = 0, warning = 0, error = 0;
			foreach (var entry in entries) {
				if (entry.IsSessionStart) continue;
				switch (entry.Severity) {
					case LogSeverity.Warning: warning++; break;
					case LogSeverity.Error: error++; break;
					default: info++; break;
				}
			}

			// Split into blocks at session starts. Block 0 holds pre-session entries
			// (app startup) and renders without a header.
			var blocks = new List<(LogEntry? Session, List<LogEntry> Entries)> { (null, new List<LogEntry>()) };
			foreach (var entry in entries) {
				if (entry.IsSessionStart)
					blocks.Add((entry, new List<LogEntry>()));
				else
					blocks[^1].Entries.Add(entry);
			}

			bool searching = !string.IsNullOrWhiteSpace(search);
			var rows = new List<LogRow>();

			// Newest session first; rows inside a session stay chronological.
			for (int b = blocks.Count - 1; b >= 0; b--) {
				var (session, blockEntries) = blocks[b];

				var visible = blockEntries.Where(e => MatchesFilter(e, filter)).ToList();
				// A collapse group survives the search when ANY member matches, so
				// searching for one specific file still finds its collapsed row.
				var groups = visible
					.GroupBy(CollapseKey)
					.Where(g => !searching || g.Any(e => e.Message.Contains(search!, StringComparison.OrdinalIgnoreCase)))
					.ToDictionary(g => g.Key, g => g.ToList());

				var blockRows = new List<LogRow>();
				var emittedGroups = new HashSet<string>();
				foreach (var entry in visible) {
					string key = CollapseKey(entry);
					if (!groups.TryGetValue(key, out var members))
						continue; // filtered out by search
					if (members.Count == 1) {
						blockRows.Add(new LogMessageRow(entry, members, null, 1, false));
						continue;
					}
					if (!emittedGroups.Add(key))
						continue; // group already emitted at its first occurrence
					string groupKey = b.ToString(CultureInfo.InvariantCulture) + "|" + key;
					if (expandedGroups.Contains(groupKey)) {
						for (int m = 0; m < members.Count; m++)
							blockRows.Add(new LogMessageRow(members[m], m == 0 ? members : new[] { members[m] },
								groupKey, m == 0 ? members.Count : 1, m == 0));
					}
					else {
						blockRows.Add(new LogMessageRow(entry, members, groupKey, members.Count, false));
					}
				}

				if (blockRows.Count == 0)
					continue;
				if (session is { } s)
					rows.Add(new LogSessionRow(FormatSessionHeader(s.Message, s.Timestamp, now, todayWord), s.Timestamp));
				rows.AddRange(blockRows);
			}

			return new LogListResult(rows, info, warning, error);
		}

		/// <summary>
		/// Single-line form for the scanning screen's live tail (#832): multi-line payloads
		/// (FFmpeg stderr dumps, stack traces) collapse to their first line so one failed
		/// file cannot flood the screen, keeping the classifier's trailing "Hint:" line —
		/// that is the plain-language part a user needs mid-scan. The full text stays
		/// available in the Log window and the log file.
		/// </summary>
		public static string FormatTailLine(LogEntry entry) {
			string message = entry.Message;
			int nl = message.IndexOfAny(new[] { '\r', '\n' });
			if (nl >= 0) {
				string collapsed = message[..nl] + " …";
				int hint = message.LastIndexOf("Hint: ", StringComparison.Ordinal);
				if (hint > nl && message[hint - 1] is '\n' or '\r') {
					string hintText = message[hint..];
					int hintEnd = hintText.IndexOfAny(new[] { '\r', '\n' });
					collapsed += " " + (hintEnd < 0 ? hintText : hintText[..hintEnd]);
				}
				message = collapsed;
			}
			return $"{entry.Timestamp:HH:mm:ss} · {message}";
		}

		public static LogTailRow BuildTailRow(LogEntry entry) =>
			new(FormatTailLine(entry), TooltipText(entry.Message));

		/// <summary>
		/// Tooltip form of a tail line: the untrimmed message, so the remediation part a
		/// clipped line hides ("switching to pro…") is a hover away — but capped so a
		/// hundred lines of FFmpeg stderr cannot cover the whole screen (#836).
		/// </summary>
		internal static string TooltipText(string message) {
			const int MaxChars = 1500;
			return message.Length <= MaxChars ? message : message[..MaxChars] + " …";
		}

		/// <summary>Plain-text form used by copy/save — mirrors the log file's line shape.</summary>
		public static string FormatLine(LogEntry entry) => entry switch {
			{ IsSessionStart: true } => $"---------- {entry.Message} · {entry.Timestamp:HH:mm:ss} ----------",
			{ Severity: LogSeverity.Warning } => $"{entry.Timestamp:HH:mm:ss} => [WARNING] {entry.Message}",
			{ Severity: LogSeverity.Error } => $"{entry.Timestamp:HH:mm:ss} => [ERROR] {entry.Message}",
			_ => $"{entry.Timestamp:HH:mm:ss} => {entry.Message}",
		};
	}
}
