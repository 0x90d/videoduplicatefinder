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

namespace VDF.Core.Utils {
	public enum LogSeverity {
		Info,
		Warning,
		Error,
	}

	/// <summary>
	/// One log line. <see cref="IsSessionStart"/> entries mark the beginning of a
	/// logical session (e.g. a scan); their <see cref="Message"/> is the session label.
	/// </summary>
	public readonly record struct LogEntry(DateTime Timestamp, LogSeverity Severity, string Message, bool IsSessionStart = false);

	public sealed class Logger {
		// Eagerly initialized: `instance ??= new()` raced under parallel first access,
		// briefly yielding TWO loggers — a subscriber on the first missed every entry
		// raised through the second.
		static readonly Logger instance = new();
		public static Logger Instance => instance;
		public event LogEventHandler? LogEntryAdded;
		public delegate void LogEventHandler(LogEntry entry);
		static readonly object lockObject = new();
		static readonly Lazy<string> _logFilePath = new(() =>
			Path.Combine(CoreUtils.IsCurrentFolderWritable
				? CoreUtils.CurrentFolder
				: CoreUtils.GetDefaultStateFolder(), "log.txt"));
		public static string LogFilePath => _logFilePath.Value;

		public void Info(string text) => Add(new LogEntry(DateTime.Now, LogSeverity.Info, text));
		public void Warn(string text) => Add(new LogEntry(DateTime.Now, LogSeverity.Warning, text));
		public void Error(string text) => Add(new LogEntry(DateTime.Now, LogSeverity.Error, text));
		/// <summary>Marks the start of a logical session (e.g. a scan) with a short label.</summary>
		public void BeginSession(string label) => Add(new LogEntry(DateTime.Now, LogSeverity.Info, label, IsSessionStart: true));

		void Add(LogEntry entry) {
			LogEntryAdded?.Invoke(entry);
			lock (lockObject) {
				// Best-effort: a reader holding log.txt (tail, editor, antivirus) must
				// never turn a log call into an exception inside the scan engine.
				try {
					using var stream = new FileStream(_logFilePath.Value, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
					using var writer = new StreamWriter(stream);
					writer.WriteLine(FormatFileLine(entry));
				}
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}
		}

		// Info lines keep the historic "HH:mm:ss => text" shape; severity only shows
		// up as a tag when there is something to flag.
		internal static string FormatFileLine(LogEntry entry) => entry switch {
			{ IsSessionStart: true } => $"{Environment.NewLine}{new string('-', 60)} {entry.Message} · {entry.Timestamp:HH:mm:ss} {new string('-', 60)}",
			{ Severity: LogSeverity.Warning } => $"{entry.Timestamp:HH:mm:ss} => [WARNING] {entry.Message}",
			{ Severity: LogSeverity.Error } => $"{entry.Timestamp:HH:mm:ss} => [ERROR] {entry.Message}",
			_ => $"{entry.Timestamp:HH:mm:ss} => {entry.Message}",
		};
	}

}
