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

		static readonly string[] suf = { " B", " KB", " MB", " GB", " TB", " PB", " EB" };
		public static string BytesToString(this long byteCount) {
			if (byteCount == 0)
				return "0" + suf[0];
			var bytes = Math.Abs(byteCount);
			var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
			var num = Math.Round(bytes / Math.Pow(1024, place), 1);
			return (Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture) + suf[place];
		}
	}
}
