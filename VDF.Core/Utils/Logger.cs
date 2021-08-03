// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Concurrent;
using System.Text;

namespace VDF.Core.Utils {
	public sealed class Logger {
		static Logger instance;
		public static Logger Instance => instance ??= new Logger();
		public event EventHandler LogItemAdded;

		public void ClearLog() => LogEntries.Clear();
		public override string? ToString() {
			var sb = new StringBuilder();
			foreach (var item in LogEntries) {
				sb.AppendLine("---------------");
				sb.AppendLine(item.ToString());
			}
			return sb.ToString();
		}
		public void Info(string text) {
			LogEntries.Add(new LogItem { DateTime = DateTime.Now.ToString("HH:mm:ss"), Message = text });
			LogItemAdded?.Invoke(null, new EventArgs());
		}
		public ConcurrentBag<LogItem> LogEntries { get; internal set; } = new ConcurrentBag<LogItem>();
	}

	public sealed class LogItem {
		public string? DateTime { get; set; }
		public string? Message { get; set; }
		public override string? ToString() => DateTime + '\t' + Message;
	}
}
