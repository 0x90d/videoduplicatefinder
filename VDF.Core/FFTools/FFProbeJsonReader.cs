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

namespace VDF.Core.FFTools {
	static class FFProbeJsonReader {

		enum JsonObjects {
			None,
			Streams,
			Format
		}

		// C# no-alloc optimization that directly wraps the data section of the dll (similar to string constants)
		// https://github.com/dotnet/roslyn/pull/24621
		static ReadOnlySpan<byte> StreamsKeyword => new byte[] { 0x73, 0x74, 0x72, 0x65, 0x61, 0x6D, 0x73 }; // = streams
		static ReadOnlySpan<byte> FormatKeyword => new byte[] { 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74 }; // = format
		static ReadOnlySpan<byte> IndexKeyword => new byte[] { 0x69, 0x6E, 0x64, 0x65, 0x78 }; // = index

		/// <summary>
		/// Parses FFprobe JSON output and returns a new <see cref="MediaInfo"/>
		/// </summary>
		/// <param name="data">JSON output</param>
		/// <param name="file">The file the JSON output format is about</param>
		/// <returns><see cref="MediaInfo"/> containing information from FFprobe output</returns>
		public static MediaInfo Read(byte[] data, string file) {

			var json = new Utf8JsonReader(data, isFinalBlock: false, state: default);


			var streams = new List<Dictionary<string, object>>();
			var format = new Dictionary<string, object>();

			var currentStream = -1;

			var currentObject = JsonObjects.None;
			string? lastKey = null;

			while (json.Read()) {
				JsonTokenType tokenType = json.TokenType;
				ReadOnlySpan<byte> valueSpan = json.ValueSpan;
				switch (tokenType) {
				case JsonTokenType.StartObject:
				case JsonTokenType.EndObject:
				case JsonTokenType.Null:
				case JsonTokenType.StartArray:
				case JsonTokenType.EndArray:
					break;
				case JsonTokenType.PropertyName:
					if (valueSpan.SequenceEqual(StreamsKeyword)) {
						currentObject = JsonObjects.Streams;
						break;
					}
					if (valueSpan.SequenceEqual(FormatKeyword)) {
						currentObject = JsonObjects.Format;
						break;
					}

					if (valueSpan.SequenceEqual(IndexKeyword)) {
						streams.Add(new Dictionary<string, object>());
						currentStream++;
					}

					if (currentObject == JsonObjects.Streams) {
						lastKey = json.GetString();
						if (lastKey != null)
							streams[currentStream].TryAdd(lastKey, new object());
					}
					else if (currentObject == JsonObjects.Format) {
						lastKey = json.GetString();
						if (lastKey != null)
							format.TryAdd(lastKey, new object());
					}
					break;
				case JsonTokenType.String:
					if (currentObject == JsonObjects.Streams && lastKey != null) {
						streams[currentStream][lastKey] = json.GetString()!;
					}
					else if (currentObject == JsonObjects.Format && lastKey != null) {
						format[lastKey] = json.GetString()!;
					}
					break;
				case JsonTokenType.Number:
					if (!json.TryGetInt32(out int valueInteger)) {
#if DEBUG
						System.Diagnostics.Trace.TraceWarning($"JSON number parse error: \"{lastKey}\" = {System.Text.Encoding.UTF8.GetString(valueSpan.ToArray())}, file = {file}");
#endif
						break;
					}

					if (currentObject == JsonObjects.Streams && lastKey != null) {
						streams[currentStream][lastKey] = valueInteger;
					}
					else if (currentObject == JsonObjects.Format && lastKey != null) {
						format[lastKey] = valueInteger;
					}
					break;
				case JsonTokenType.True:
				case JsonTokenType.False:
					bool valueBool = json.GetBoolean();
					if (currentObject == JsonObjects.Streams && lastKey != null) {
						streams[currentStream][lastKey] = valueBool;
					}
					else if (currentObject == JsonObjects.Format && lastKey != null) {
						format[lastKey] = valueBool;
					}
					break;
				default:
					throw new ArgumentException();
				}
			}

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

			var foundBitRate = false;
			for (int i = 0; i < streams.Count; i++) {
				info.Streams[i] = new MediaInfo.StreamInfo();
				if (streams[i].ContainsKey("bit_rate") && long.TryParse((string)streams[i]["bit_rate"], out var bitrate)) {
					foundBitRate = true;
					info.Streams[i].BitRate = bitrate;
				}
				if (streams[i].ContainsKey("width"))
					info.Streams[i].Width = (int)streams[i]["width"];
				if (streams[i].ContainsKey("height"))
					info.Streams[i].Height = (int)streams[i]["height"];
				if (streams[i].ContainsKey("codec_name"))
					info.Streams[i].CodecName = (string)streams[i]["codec_name"];
				if (streams[i].ContainsKey("codec_long_name"))
					info.Streams[i].CodecLongName = (string)streams[i]["codec_long_name"];
				if (streams[i].ContainsKey("codec_type"))
					info.Streams[i].CodecType = (string)streams[i]["codec_type"];
				if (streams[i].ContainsKey("channel_layout"))
					info.Streams[i].ChannelLayout = (string)streams[i]["channel_layout"];
				if (streams[i].ContainsKey("channels"))
					info.Streams[i].Channels = (int)streams[i]["channels"];

				if (streams[i].ContainsKey("pix_fmt"))
					info.Streams[i].PixelFormat = (string)streams[i]["pix_fmt"];
				if (streams[i].ContainsKey("sample_rate") && int.TryParse((string)streams[i]["sample_rate"], out var sample_rate))
					info.Streams[i].SampleRate = sample_rate;
				if (streams[i].ContainsKey("index"))
					info.Streams[i].Index = ((int)streams[i]["index"]).ToString();

				if (streams[i].ContainsKey("r_frame_rate")) {
					var stringFrameRate = (string)streams[i]["r_frame_rate"];
					if (stringFrameRate.Contains('/')) {
						var split = stringFrameRate.Split('/');
						if (split.Length == 2 && int.TryParse(split[0], out var firstRate) && int.TryParse(split[1], out var secondRate))
							info.Streams[i].FrameRate = (firstRate > 0 && secondRate > 0) ? firstRate / (float)secondRate : -1f;
					}
				}

			}
			//Workaround if video stream bitrate is not set but in format
			if (!foundBitRate && info.Streams.Length > 0 && format.ContainsKey("bit_rate") && long.TryParse((string)format["bit_rate"], out var formatBitrate))
				info.Streams[0].BitRate = formatBitrate;

			return info;
		}
	}
}
