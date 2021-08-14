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
using System.Collections.Generic;
using System.Text.Json;
using VDF.Core.Utils;
using System.Text.Json.Serialization;

using DSO = System.Collections.Generic.Dictionary<string, object>; 
using LO  = System.Collections.Generic.List <object>; 

namespace VDF.Core.FFTools {

#if true
// ---------------------------------------------------------------------------------
// V1) JSON -> JsonSerializer.Deserialize -> JsonMediaInfo class -> MediaInfo class
// ---------------------------------------------------------------------------------

	static class FFProbeJsonReader {

		public class JsonMediaInfo
		{
			#pragma warning disable CS0649 // Field is never assigned to, and will always have its default
			public Format? format;
			public List<Stream>? streams;

			public class Format {
				public string? bit_rate;
				public string? duration;
			}
			public class Stream	{
				public string? bit_rate;
				public int width;
				public int height;
				public string? codec_name;
				public string? codec_long_name;
				public string? codec_type;
				public string? channel_layout;
				public string? pix_fmt;
				public string? sample_rate;
				public int index;
				public string? r_frame_rate;
				public Disposition? disposition;
			}
			public class Disposition {
				[JsonPropertyName("default")]
				public int default_;
			}
			#pragma warning restore CS0649 // Field is never assigned to, and will always have its default
		}

		static JsonSerializerOptions serializerOptions = new JsonSerializerOptions
		{
			IncludeFields = true,
		};

		/// <summary>
		/// Parses FFprobe JSON output and returns a new <see cref="MediaInfo"/>
		/// </summary>
		/// <param name="data">JSON output</param>
		/// <param name="file">The file the JSON output format is about</param>
		/// <returns><see cref="MediaInfo"/> containing information from FFprobe output</returns>
		public static MediaInfo Read(byte[] data, string file) {;

			var json = new Utf8JsonReader(data);
            var jsonInfo = JsonSerializer.Deserialize<JsonMediaInfo>(ref json, serializerOptions);

			var info = new MediaInfo {
				Streams = new MediaInfo.StreamInfo[jsonInfo?.streams?.Count ?? 0]
			};

			if (jsonInfo != null && jsonInfo.streams != null) {
				if (jsonInfo.format?.duration != null && TimeSpan.TryParse(jsonInfo.format.duration, out var duration))
					/*
					* Trim miliseconds here as we would have done it later anyway.
					* Reasons are:
					* - More user friendly
					* - Allows an improved check against equality
					* Cons are:
					* - Not 100% accurate if you consider a difference of e.g. 2 miliseconds makes a duplicate no longer a duplicate
					* - Breaking change at the moment of implementation as it doesn't apply to already scanned files
					*/
					info.Duration = duration.TrimMiliseconds();

				long.TryParse(jsonInfo.format?.bit_rate, out var formatBitrate);
		
				for (int i = 0; i < info.Streams.Count(); i++) {
					var stream = jsonInfo.streams[i];
					info.Streams[i] = new MediaInfo.StreamInfo();
					if (stream.bit_rate != null && long.TryParse(stream.bit_rate , out var bitrate))
						info.Streams[i].BitRate = bitrate;
					else if (stream.codec_type == "video")
						info.Streams[i].BitRate = formatBitrate;
						//Workaround if video stream bitrate is not set but in format

					info.Streams[i].Width 			= stream.width;
					info.Streams[i].Height 			= stream.height;
					info.Streams[i].CodecName 		= stream.codec_name ?? "";
					info.Streams[i].CodecLongName 	= stream.codec_long_name ?? "";
					info.Streams[i].CodecType 		= stream.codec_type ?? "";
					info.Streams[i].ChannelLayout 	= stream.channel_layout ?? "";
					info.Streams[i].PixelFormat 	= stream.pix_fmt ?? "";
					info.Streams[i].Default			= stream.disposition?.default_ ?? 0;
					info.Streams[i].Index 			= stream.index.ToString();
					
					if (int.TryParse(stream.sample_rate, out var SampleRate))
						info.Streams[i].SampleRate = SampleRate;

					if (stream.r_frame_rate != null) {
						if (stream.r_frame_rate.Contains('/')) {
							var split = stream.r_frame_rate.Split('/');
							if (split.Length == 2 && int.TryParse(split[0], out var firstRate) && int.TryParse(split[1], out var secondRate))
								info.Streams[i].FrameRate = (firstRate > 0 && secondRate > 0) ? firstRate / (float)secondRate : -1f;
						}
					}
				}
			}

			return info;
		}
	}

#else

// ---------------------------------------------------------------------------------
// V2) JSON -> while(){ Read(json); ...} -> Dictionary/List -> MediaInfo class
// ---------------------------------------------------------------------------------

	static class FFProbeJsonReader {

		static object? ReadJson(ref Utf8JsonReader json, UInt32 max_depth = 5)
		{
			if (max_depth == 0)
				return null;
			max_depth--;

			if (json.TokenType == JsonTokenType.None)
				json.Read();

			switch (json.TokenType) {
				case JsonTokenType.Null:
					return null;
				case JsonTokenType.False:
					return false;
				case JsonTokenType.True:
					return true;
				case JsonTokenType.String:
					return json.GetString();
				case JsonTokenType.Number:
					if (json.TryGetInt32(out var i))
						return i;
					if (json.TryGetInt64(out var l))
						return l;
					if (json.TryGetDouble(out var d))
						return d;
					throw new JsonException(string.Format($"Unsupported number {json.ValueSpan.ToString()}"));
				case JsonTokenType.StartArray:
					var list = new List<object>();
					while (json.Read()) {
						switch (json.TokenType) {
							default:
								var obj = ReadJson(ref json, max_depth);
								if (obj != null) {
									list.Add(obj);
									break;
								}
								throw new JsonException();	// null value not allowed
							case JsonTokenType.EndArray:
								return list;
						}
					}
					throw new JsonException();
				case JsonTokenType.StartObject:
					var dict = new Dictionary<string, object>();
					while (json.Read()) {
						switch (json.TokenType) {
							case JsonTokenType.EndObject:
								return dict;
							case JsonTokenType.PropertyName:
								var key = json.GetString();
								json.Read();
								var obj = ReadJson(ref json, max_depth);
								if (key != null && obj != null) {
									dict.Add(key, obj);
									break;
								}
								throw new JsonException();	// null key / value not allowed
							default:
								throw new JsonException();
						}
					}
					throw new JsonException();
				default:
					throw new JsonException(string.Format($"Unknown token {json.TokenType}"));
			}
		}
		

		/// <summary>
		/// Parses FFprobe JSON output and returns a new <see cref="MediaInfo"/>
		/// </summary>
		/// <param name="data">JSON output</param>
		/// <param name="file">The file the JSON output format is about</param>
		/// <returns><see cref="MediaInfo"/> containing information from FFprobe output</returns>
		public static MediaInfo Read(byte[] data, string file) {
			var json = new Utf8JsonReader(data, isFinalBlock: false, state: default);
		
			//json.Read();
			DSO root = (DSO)(ReadJson(ref json) ?? new DSO());
			
			LO  streams = root.ContainsKey("streams") ? (LO)(root["streams"]) : new LO();
			DSO format  = root.ContainsKey("format")  ? (DSO)(root["format"]) : new DSO();

			var info = new MediaInfo {
				Streams = new MediaInfo.StreamInfo[streams.Count]
			};

			if (format.ContainsKey("duration") && TimeSpan.TryParse((string)format["duration"], out var duration))
				/*
				 * Trim miliseconds here as we would have done it later anyway.
				 * Reasons are:
				 * - More user friendly
				 * - Allows an improved check against equality
				 * Cons are:
				 * - Not 100% accurate if you consider a difference of e.g. 2 miliseconds makes a duplicate no longer a duplicate
				 * - Breaking change at the moment of implementation as it doesn't apply to already scanned files
				 */
				info.Duration = duration.TrimMiliseconds();

			long formatBitrate = 0;
			if (format.ContainsKey("bit_rate"))
				long.TryParse((string)format["bit_rate"], out formatBitrate);

			for (int i = 0; i < streams.Count; i++) {
				info.Streams[i] = new MediaInfo.StreamInfo();
				var stream = (DSO)streams[i];

				if (stream.ContainsKey("width"))
					info.Streams[i].Width = (int)stream["width"];
				if (stream.ContainsKey("height"))
					info.Streams[i].Height = (int)stream["height"];
				if (stream.ContainsKey("codec_name"))
					info.Streams[i].CodecName = (string)stream["codec_name"];
				if (stream.ContainsKey("codec_long_name"))
					info.Streams[i].CodecLongName = (string)stream["codec_long_name"];
				if (stream.ContainsKey("codec_type"))
					info.Streams[i].CodecType = (string)stream["codec_type"];
				if (stream.ContainsKey("channel_layout"))
					info.Streams[i].ChannelLayout = (string)stream["channel_layout"];

				if (stream.ContainsKey("pix_fmt"))
					info.Streams[i].PixelFormat = (string)stream["pix_fmt"];
				if (stream.ContainsKey("sample_rate") && int.TryParse((string)stream["sample_rate"], out var sample_rate))
					info.Streams[i].SampleRate = sample_rate;
				if (stream.ContainsKey("index"))
					info.Streams[i].Index = ((int)stream["index"]).ToString();

				if (stream.ContainsKey("bit_rate") && long.TryParse((string)stream["bit_rate"], out var bitrate))
					info.Streams[i].BitRate = bitrate;
				else if (info.Streams[i].CodecType == "video")
					info.Streams[i].BitRate = formatBitrate;
					//Workaround if video stream bitrate is not set but in format

				if (stream.ContainsKey("r_frame_rate")) {
					var stringFrameRate = (string)stream["r_frame_rate"];
					if (stringFrameRate.Contains('/')) {
						var split = stringFrameRate.Split('/');
						if (split.Length == 2 && int.TryParse(split[0], out var firstRate) && int.TryParse(split[1], out var secondRate))
							info.Streams[i].FrameRate = (firstRate > 0 && secondRate > 0) ? firstRate / (float)secondRate : -1f;
					}
				}

				if (stream.ContainsKey("disposition") && stream["disposition"] != null) {
					var disposition = (DSO)stream["disposition"];
					if (disposition.ContainsKey("default"))
						info.Streams[i].Default = ((int)disposition["default"]);
				}
			}

			return info;
		}
	}

#endif

}
