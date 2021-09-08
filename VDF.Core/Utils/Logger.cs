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

namespace VDF.Core.Utils {
	public sealed class Logger {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		static Logger instance;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		public static Logger Instance => instance ??= new Logger();
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		public event LogEventHandler LogItemAdded;
		public delegate void LogEventHandler(string foo);
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		static readonly object lockObject = new();
		public void Info(string text) {
			LogItemAdded?.Invoke($"{DateTime.Now:HH:mm:ss} => {text}");
			lock (lockObject) {
				File.AppendAllText(Path.Combine(CoreUtils.CurrentFolder, "log.txt"), $"{DateTime.Now:HH:mm:ss} => {text}{Environment.NewLine}");
			}
		}
		public void InsertSeparator(char separatorChar) {
			LogItemAdded?.Invoke($"{Environment.NewLine}{new String(separatorChar, 150)}{Environment.NewLine}");
		}
	}

}
