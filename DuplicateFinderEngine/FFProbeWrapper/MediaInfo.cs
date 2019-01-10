using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.XPath;
using ProtoBuf;

namespace DuplicateFinderEngine.FFProbeWrapper
{
    [ProtoContract]
    [SuppressMessage("ReSharper", "ConvertToAutoPropertyWithPrivateSetter")]
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    public sealed class MediaInfo
    {
	    [ProtoMember(1)]
		public StreamInfo[] Streams { get; }

		[ProtoMember(2)]
		public TimeSpan Duration { get; }

		public MediaInfo() { }
		public MediaInfo(XPathDocument ffProbeResult)
        {
            var xpathNavigator = ffProbeResult.CreateNavigator();
            //Duration
            var durationValue = xpathNavigator.SelectSingleNode("/ffprobe/format/@duration")?.Value;
            if (!string.IsNullOrEmpty(durationValue) && TimeSpan.TryParse(durationValue, out var result))
                Duration = result;
            else
                Duration = TimeSpan.Zero;
            //Streams
            var list = new List<StreamInfo>();
            var xpathNodeIterator = xpathNavigator.Select("/ffprobe/streams/stream/@index");
            while (xpathNodeIterator.MoveNext())
            {
                var xpathNavigator2 = xpathNodeIterator.Current;
                list.Add(new StreamInfo(xpathNavigator, xpathNavigator2.Value));
            }
            Streams = list.ToArray();
        }


        [ProtoContract]
        public class StreamInfo
        {

            private string XPathPrefix => "/ffprobe/streams/stream[@index=\"" + Index + "\"]";
            public StreamInfo() { }
			internal StreamInfo(XPathNavigator xpathNavigator, string _index)
            {
                Index = _index;
                CodecName = xpathNavigator.SelectSingleNode(XPathPrefix + "/@codec_name")?.Value;
                CodecLongName = xpathNavigator.SelectSingleNode(XPathPrefix + "/@codec_long_name")?.Value;
                CodecType = xpathNavigator.SelectSingleNode(XPathPrefix + "/@codec_type")?.Value;
                PixelFormat = xpathNavigator.SelectSingleNode(XPathPrefix + "/@pix_fmt")?.Value;
                Width = ParseInt(xpathNavigator.SelectSingleNode(XPathPrefix + "/@width")?.Value);
                Height = ParseInt(xpathNavigator.SelectSingleNode(XPathPrefix + "/@height")?.Value);
                SampleRate = ParseInt(xpathNavigator.SelectSingleNode(XPathPrefix + "/@sample_rate")?.Value);
                ChannelLayout = xpathNavigator.SelectSingleNode(XPathPrefix + "/@channel_layout")?.Value;
                BitRate = ParseLong(xpathNavigator.SelectSingleNode(XPathPrefix + "/@bit_rate")?.Value);

                var attrValue = xpathNavigator.SelectSingleNode(XPathPrefix + "/@r_frame_rate")?.Value;
                if (string.IsNullOrEmpty(attrValue))
                {
                    FrameRate = -1f;
                }
                else
                {
                    var array = attrValue.Split(new[]
                    {
                        '/'
                    });
                    if (array.Length != 2)
                    {
                        FrameRate = -1f;
                    }
                    else
                    {
                        var num = ParseInt(array[0]);
                        var num2 = ParseInt(array[1]);
                        FrameRate = (num > 0 && num2 > 0) ? num / (float)num2 : -1f;
                    }
                }
            }

            [ProtoMember(1)]
			public string Index { get; }

			[ProtoMember(2)]
			public string CodecName { get; }

			[ProtoMember(3)]
			public string CodecLongName { get; }

			[ProtoMember(4)]
			public string CodecType { get; }

			[ProtoMember(5)]
			public string PixelFormat { get; }

			[ProtoMember(6)]
			public int Width { get; }

			[ProtoMember(7)]
			public int Height { get; }

			[ProtoMember(8)]
			public int SampleRate { get; }

			[ProtoMember(9)]
			public string ChannelLayout { get; }

			[ProtoMember(10)]
			public long BitRate { get; }

			[ProtoMember(11)]
			public float FrameRate { get; }

			private static int ParseInt(string s)
            {
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out var result))
                {
                    return result;
                }
                return -1;
            }
            private static long ParseLong(string s)
            {
                if (!string.IsNullOrEmpty(s) && long.TryParse(s, out var result))
                {
                    return result;
                }
                return 0;
            }
            
        }
    }
}
