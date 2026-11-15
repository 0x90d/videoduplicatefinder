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
// #940: an MKV whose video stream has no BPS tag carries no per-stream bit_rate, and
// ffprobe prints the file's overall rate only into format.bit_rate. The format fallback
// ran only when no stream at all had a bit_rate, so the AC3 track's 448 kb/s suppressed
// it: the result row showed no video bitrate and only the audio line had a value.

using System.Text;
using VDF.Core.FFTools;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests;

public class BitRateFallbackTests {
	// The report's ffprobe output, trimmed to what the reader looks at.
	const string MkvVideoWithoutBitRate = """
		{
		  "streams": [
		    { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080,
		      "r_frame_rate": "24000/1001", "disposition": { "attached_pic": 0 } },
		    { "index": 1, "codec_name": "ac3", "codec_type": "audio", "channels": 6, "channel_layout": "5.1",
		      "sample_rate": "48000", "bit_rate": "448000", "disposition": { "attached_pic": 0 } },
		    { "index": 2, "codec_name": "subrip", "codec_type": "subtitle", "disposition": { "attached_pic": 0 } }
		  ],
		  "format": { "duration": "5000.000000", "bit_rate": "6194264" }
		}
		""";

	static MediaInfo Read(string json) => FFProbeJsonReader.Read(Encoding.UTF8.GetBytes(json), "sample.mkv");

	[Fact]
	public void VideoStreamWithoutItsOwnBitRate_UsesTheContainerBitRate() {
		var info = Read(MkvVideoWithoutBitRate);

		Assert.Equal(6_194_264, info.Streams[0].BitRate);
		// The audio track has a real bit_rate of its own and keeps it.
		Assert.Equal(448_000, info.Streams[1].BitRate);
		Assert.Equal(0, info.Streams[2].BitRate);
	}

	[Fact]
	public void ResultRow_ShowsTheVideoBitRate_NotTheAudioTrack() {
		var entry = new FileEntry {
			_Path = Path.Combine(Path.GetTempPath(), "sample.mkv"),
			mediaInfo = Read(MkvVideoWithoutBitRate),
		};

		var item = new DuplicateItem(entry, 0f, Guid.NewGuid(), DuplicateFlags.None);

		Assert.Equal(6194m, item.BitRateKbs);
		Assert.Equal(448m, item.AudioBitRateKbs);
	}

	[Fact]
	public void AudioFirstContainer_FallsBackOnTheVideoStream_NotTheAudioStream() {
		const string json = """
			{
			  "streams": [
			    { "index": 0, "codec_name": "aac", "codec_type": "audio", "channels": 2, "sample_rate": "48000" },
			    { "index": 1, "codec_name": "hevc", "codec_type": "video", "width": 1280, "height": 720, "r_frame_rate": "25/1" }
			  ],
			  "format": { "duration": "60.000000", "bit_rate": "1000000" }
			}
			""";

		var info = Read(json);

		Assert.Equal(0, info.Streams[0].BitRate);
		Assert.Equal(1_000_000, info.Streams[1].BitRate);
	}

	[Fact]
	public void VideoStreamWithItsOwnBitRate_KeepsIt() {
		const string json = """
			{
			  "streams": [
			    { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1280, "height": 720,
			      "bit_rate": "5000000", "r_frame_rate": "25/1" },
			    { "index": 1, "codec_name": "ac3", "codec_type": "audio", "channels": 2, "bit_rate": "448000" }
			  ],
			  "format": { "duration": "60.000000", "bit_rate": "9000000" }
			}
			""";

		var info = Read(json);

		Assert.Equal(5_000_000, info.Streams[0].BitRate);
		Assert.Equal(448_000, info.Streams[1].BitRate);
	}

	[Fact]
	public void AudioOnlyFile_StillUsesTheContainerBitRate() {
		const string json = """
			{
			  "streams": [
			    { "index": 0, "codec_name": "mp3", "codec_type": "audio", "channels": 2, "sample_rate": "44100" }
			  ],
			  "format": { "duration": "180.000000", "bit_rate": "192000" }
			}
			""";

		var info = Read(json);

		Assert.Equal(192_000, info.Streams[0].BitRate);
	}
}
