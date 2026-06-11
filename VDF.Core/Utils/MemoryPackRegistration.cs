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

using MemoryPack;

namespace VDF.Core.Utils {
	static class MemoryPackRegistration {
		/// <summary>
		/// Registers the generated formatters explicitly. MemoryPack's lazy fallback
		/// locates a type's RegisterFormatter method via reflection, and under Native
		/// AOT trimming that method can be removed ("can not found RegisterFormatter",
		/// seen in the AOT GUI). Register&lt;T&gt;() is constrained to
		/// IMemoryPackFormatterRegister, so these calls bind statically and root the
		/// formatters at compile time. Called from DatabaseUtils' static constructor —
		/// the only place MemoryPack serialization happens.
		/// </summary>
		internal static void Register() {
			MemoryPackFormatterProvider.Register<DatabaseWrapper>();
			MemoryPackFormatterProvider.Register<FileEntry>();
			MemoryPackFormatterProvider.Register<MediaInfo>();
			MemoryPackFormatterProvider.Register<MediaInfo.StreamInfo>();
		}
	}
}
