// /*
//     Copyright (C) 2025 0x90d
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
using System.Globalization;

namespace VDF.Core.Utils {
	public static class Extensions {
		public static TimeSpan StopGetElapsedAndRestart(this Stopwatch stopwatch) {
			stopwatch.Stop();
			var elapsed = stopwatch.Elapsed;
			stopwatch.Restart();
			return elapsed;
		}
		public static TimeSpan TrimMiliseconds(this TimeSpan ts) => new(ts.Days, ts.Hours, ts.Minutes, ts.Seconds);

		static readonly string[] SizeSuffixes = { " B", " KB", " MB", " GB", " TB", " PB", " EB" };
		public static string BytesToString(this long byteCount) {
			if (byteCount == 0)
				return "0 B";

			int place = 0;
			double value = byteCount;

			while (value >= 1024 && place < SizeSuffixes.Length - 1) {
				value /= 1024;
				place++;
			}

			return string.Create(CultureInfo.InvariantCulture, $"{value:0.0}{SizeSuffixes[place]}");
		}
		public static long BytesToMegaBytes(this long byteCount) => (long)((byteCount / 1024f) / 1024f);
	}
}
