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
using System.Runtime.CompilerServices;

namespace VDF.Core {
	[Flags]
	public enum EntryFlags {
		IsImage = 1,
		ManuallyExcluded = 2,
		ThumbnailError = 4,
		MetadataError = 8,
		TooDark = 16,

		AllErrors = ThumbnailError | MetadataError | TooDark
	}

	public static class EntryFlagExtensions {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Any(this EntryFlags f, EntryFlags checkFlags) => (f & checkFlags) > 0;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Has(this EntryFlags f, EntryFlags checkFlags) => (f & checkFlags) == checkFlags;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Set(this ref EntryFlags f, EntryFlags setFlag) => f |= setFlag;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Set(this ref EntryFlags f, EntryFlags setFlag, bool falseToReset) => f = (f & ~setFlag) | (falseToReset ? setFlag : 0);
	}
}
