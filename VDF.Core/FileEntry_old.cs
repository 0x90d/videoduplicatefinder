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

using ProtoBuf;

namespace VDF.Core {


	[ProtoContract]
	public class FileEntry_old {
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
		protected FileEntry_old() { }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
		[ProtoMember(1)]
		public string Path { get; set; }
		[ProtoMember(4)]
		public MediaInfo? mediaInfo;
		[ProtoMember(5)]
		public EntryFlags Flags;
	}
}
