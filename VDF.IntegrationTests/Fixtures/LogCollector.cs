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

namespace VDF.IntegrationTests.Fixtures;

/// <summary>Collects the messages logged while it is alive.</summary>
sealed class LogCollector : IDisposable {
	readonly List<string> _messages = new();
	public LogCollector() => Logger.Instance.LogEntryAdded += OnEntry;
	void OnEntry(LogEntry entry) { lock (_messages) _messages.Add(entry.Message); }
	public bool Any(string substring) {
		lock (_messages) return _messages.Any(m => m.Contains(substring, StringComparison.Ordinal));
	}
	public void Dispose() => Logger.Instance.LogEntryAdded -= OnEntry;
}
