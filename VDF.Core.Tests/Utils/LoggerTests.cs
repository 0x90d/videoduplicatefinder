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

public class LoggerTests {

	// The Logger is a process-wide singleton whose event other parallel tests could
	// also raise; tag messages with a unique marker and filter on it.
	static List<LogEntry> Capture(Action<Logger> act, string marker) {
		var captured = new List<LogEntry>();
		void Handler(LogEntry entry) {
			lock (captured)
				if (entry.Message.Contains(marker)) captured.Add(entry);
		}
		Logger.Instance.LogEntryAdded += Handler;
		try {
			act(Logger.Instance);
		}
		finally {
			Logger.Instance.LogEntryAdded -= Handler;
		}
		return captured;
	}

	[Fact]
	public void InfoWarnError_RaiseEntriesWithMatchingSeverity() {
		string marker = Guid.NewGuid().ToString("N");
		var entries = Capture(log => {
			log.Info($"info {marker}");
			log.Warn($"warn {marker}");
			log.Error($"error {marker}");
		}, marker);

		Assert.Equal(3, entries.Count);
		Assert.Equal(LogSeverity.Info, entries[0].Severity);
		Assert.Equal(LogSeverity.Warning, entries[1].Severity);
		Assert.Equal(LogSeverity.Error, entries[2].Severity);
		Assert.All(entries, e => Assert.False(e.IsSessionStart));
		Assert.Equal($"info {marker}", entries[0].Message);
	}

	[Fact]
	public void BeginSession_RaisesSessionStartEntry_WithLabelAsMessage() {
		string marker = Guid.NewGuid().ToString("N");
		var entries = Capture(log => log.BeginSession($"Scan {marker}"), marker);

		var entry = Assert.Single(entries);
		Assert.True(entry.IsSessionStart);
		Assert.Equal($"Scan {marker}", entry.Message);
	}

	[Fact]
	public void Entries_LandInTheLogFile_WithSeverityTags() {
		string marker = Guid.NewGuid().ToString("N");
		Logger.Instance.Info($"file info {marker}");
		Logger.Instance.Warn($"file warn {marker}");
		Logger.Instance.Error($"file error {marker}");
		Logger.Instance.BeginSession($"Scan {marker}");

		// Permissive sharing: parallel tests log through the same singleton and their
		// appends must not be locked out (nor fail this read) while we look.
		string content;
		using (var stream = new FileStream(Logger.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		using (var reader = new StreamReader(stream))
			content = reader.ReadToEnd();
		Assert.Contains($"=> file info {marker}", content);
		Assert.Contains($"=> [WARNING] file warn {marker}", content);
		Assert.Contains($"=> [ERROR] file error {marker}", content);
		Assert.Contains($" Scan {marker} · ", content); // session separator line
	}

	// Info lines keep the historic "HH:mm:ss => text" shape so nothing that greps
	// old log files breaks; only warnings/errors gain a tag.
	[Fact]
	public void FormatFileLine_Shapes() {
		var at = new DateTime(2026, 7, 7, 9, 41, 51);
		Assert.Equal("09:41:51 => hello",
			Logger.FormatFileLine(new LogEntry(at, LogSeverity.Info, "hello")));
		Assert.Equal("09:41:51 => [WARNING] hello",
			Logger.FormatFileLine(new LogEntry(at, LogSeverity.Warning, "hello")));
		Assert.Equal("09:41:51 => [ERROR] hello",
			Logger.FormatFileLine(new LogEntry(at, LogSeverity.Error, "hello")));
		string session = Logger.FormatFileLine(new LogEntry(at, LogSeverity.Info, "Scan", IsSessionStart: true));
		Assert.Contains("Scan · 09:41:51", session);
		Assert.Contains("---", session);
	}
}
