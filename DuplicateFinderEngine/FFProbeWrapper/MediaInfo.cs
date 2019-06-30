using System;
using ProtoBuf;

namespace DuplicateFinderEngine.FFProbeWrapper {
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

		}
	}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
}
