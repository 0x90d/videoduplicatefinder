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
using ProtoBuf;

namespace VDF.Core {

#pragma warning disable CS8618 // Non-nullable field is uninitialized.
	[ProtoContract]
	public sealed class MediaInfo {
		[ProtoMember(1)]
		public StreamInfo[] Streams { get; set; }

		[ProtoMember(2)]
		public TimeSpan Duration { get; set; }

		[ProtoContract]
		public class StreamInfo {

			[ProtoMember(1)]
			public string Index { get; set; }

			[ProtoMember(2)]
			public string CodecName { get; set; }

			[ProtoMember(3)]
			public string CodecLongName { get; set; }

			[ProtoMember(4)]
			public string CodecType { get; set; }

			[ProtoMember(5)]
			public string PixelFormat { get; set; }

			[ProtoMember(6)]
			public int Width { get; set; }

			[ProtoMember(7)]
			public int Height { get; set; }

			[ProtoMember(8)]
			public int SampleRate { get; set; }

			[ProtoMember(9)]
			public string ChannelLayout { get; set; }

			[ProtoMember(10)]
			public long BitRate { get; set; }

			[ProtoMember(11)]
			public float FrameRate { get; set; }

			[ProtoMember(12)]
			public int Channels { get; set; }
		}
	}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
}
