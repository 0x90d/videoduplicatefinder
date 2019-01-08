using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.XPath;

namespace DuplicateFinderEngine.FFProbeWrapper
{
    [Serializable]
    [SuppressMessage("ReSharper", "ConvertToAutoPropertyWithPrivateSetter")]
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    public sealed class MediaInfo
    {
        private StreamInfo[] streams;
        public StreamInfo[] Streams => streams;

        private TimeSpan duration;
        public TimeSpan Duration => duration;

        
        public MediaInfo(XPathDocument ffProbeResult)
        {
            var xpathNavigator = ffProbeResult.CreateNavigator();
            //Duration
            var durationValue = xpathNavigator.SelectSingleNode("/ffprobe/format/@duration")?.Value;
            if (!string.IsNullOrEmpty(durationValue) && TimeSpan.TryParse(durationValue, out var result))
                duration = result;
            else
                duration = TimeSpan.Zero;
            //Streams
            var list = new List<StreamInfo>();
            var xpathNodeIterator = xpathNavigator.Select("/ffprobe/streams/stream/@index");
            while (xpathNodeIterator.MoveNext())
            {
                var xpathNavigator2 = xpathNodeIterator.Current;
                list.Add(new StreamInfo(xpathNavigator, xpathNavigator2.Value));
            }
            streams = list.ToArray();
        }


        [Serializable]
        [SuppressMessage("ReSharper", "ConvertToAutoPropertyWithPrivateSetter")]
        [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
        public class StreamInfo
        {

            private string XPathPrefix => "/ffprobe/streams/stream[@index=\"" + Index + "\"]";
            internal StreamInfo(XPathNavigator xpathNavigator, string _index)
            {
                index = _index;
                codecName = xpathNavigator.SelectSingleNode(XPathPrefix + "/@codec_name")?.Value;
                codecLongName = xpathNavigator.SelectSingleNode(XPathPrefix + "/@codec_long_name")?.Value;
                codecType = xpathNavigator.SelectSingleNode(XPathPrefix + "/@codec_type")?.Value;
                pixelFormat = xpathNavigator.SelectSingleNode(XPathPrefix + "/@pix_fmt")?.Value;
                width = ParseInt(xpathNavigator.SelectSingleNode(XPathPrefix + "/@width")?.Value);
                height = ParseInt(xpathNavigator.SelectSingleNode(XPathPrefix + "/@height")?.Value);
                sampleRate = ParseInt(xpathNavigator.SelectSingleNode(XPathPrefix + "/@sample_rate")?.Value);
                channelLayout = xpathNavigator.SelectSingleNode(XPathPrefix + "/@channel_layout")?.Value;
                bitRate = ParseLong(xpathNavigator.SelectSingleNode(XPathPrefix + "/@bit_rate")?.Value);

                var attrValue = xpathNavigator.SelectSingleNode(XPathPrefix + "/@r_frame_rate")?.Value;
                if (string.IsNullOrEmpty(attrValue))
                {
                    frameRate = -1f;
                }
                else
                {
                    var array = attrValue.Split(new[]
                    {
                        '/'
                    });
                    if (array.Length != 2)
                    {
                        frameRate = -1f;
                    }
                    else
                    {
                        var num = ParseInt(array[0]);
                        var num2 = ParseInt(array[1]);
                        frameRate = (num > 0 && num2 > 0) ? num / (float)num2 : -1f;
                    }
                }
            }

            private string index;
            public string Index => index;
            private string codecName;
            public string CodecName => codecName;

            private string codecLongName;
            public string CodecLongName => codecLongName;

            private string codecType;
            public string CodecType => codecType;

            private string pixelFormat;
            public string PixelFormat => pixelFormat;

            private int width;
            public int Width => width;

            private int height;
            public int Height => height;

            private int sampleRate;
            public int SampleRate => sampleRate;

            private string channelLayout;
            public string ChannelLayout => channelLayout;

            private long bitRate;
            public long BitRate => bitRate;

            private float frameRate;
            public float FrameRate => frameRate;

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
