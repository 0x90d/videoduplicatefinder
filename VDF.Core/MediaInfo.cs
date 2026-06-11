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

using System;
using MemoryPack;

namespace VDF.Core {

#pragma warning disable CS8618 // Non-nullable field is uninitialized.
	[MemoryPackable(GenerateType.VersionTolerant)]
	public sealed partial class MediaInfo {
		[MemoryPackOrder(0)]
		public StreamInfo[] Streams { get; set; }

		[MemoryPackOrder(1)]
		public TimeSpan Duration { get; set; }

		[MemoryPackable(GenerateType.VersionTolerant)]
		public partial class StreamInfo {

			[MemoryPackOrder(0)]
			public string Index { get; set; }

			[MemoryPackOrder(1)]
			public string CodecName { get; set; }

			[MemoryPackOrder(2)]
			public string CodecLongName { get; set; }

			[MemoryPackOrder(3)]
			public string CodecType { get; set; }

			[MemoryPackOrder(4)]
			public string PixelFormat { get; set; }

			[MemoryPackOrder(5)]
			public int Width { get; set; }

			[MemoryPackOrder(6)]
			public int Height { get; set; }

			[MemoryPackOrder(7)]
			public int SampleRate { get; set; }

			[MemoryPackOrder(8)]
			public string ChannelLayout { get; set; }

			[MemoryPackOrder(9)]
			public long BitRate { get; set; }

			[MemoryPackOrder(10)]
			public float FrameRate { get; set; }

			[MemoryPackOrder(11)]
			public int Channels { get; set; }
			[MemoryPackOrder(12)]
			public string HdrFormat { get; set; }
		}
	}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
}
