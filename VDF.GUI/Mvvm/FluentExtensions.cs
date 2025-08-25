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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VDF.GUI.Mvvm {
	internal static class FluentExtensions {
		public static T Also<T>(this T value, Action<T> action) {
			action(value);
			return value;
		}

		public static TResult Let<T, TResult>(this T value, Func<T, TResult> func)
			=> func(value);

		public static T? AlsoIfNotNull<T>(this T? value, Action<T> action) where T : class {
			if (value is not null) action(value);
			return value;
		}
	}
}
