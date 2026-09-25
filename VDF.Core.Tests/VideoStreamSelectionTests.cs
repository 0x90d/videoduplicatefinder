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
// #905: an mp4 with 720p h264 video and a 1080p mjpeg cover (attached pic) was listed
// as a 1080p mjpeg file, because the metadata columns took the largest video stream.

using System.Text;
using MemoryPack;
using VDF.Core.FFTools;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests;

public class VideoStreamSelectionTests {
	static MediaInfo.StreamInfo Video(string codec, int w, int h, bool attached = false, float fps = 30) =>
		new() { CodecType = "video", CodecName = codec, Width = w, Height = h, IsAttachedPicture = attached, FrameRate = fps };
	static MediaInfo.StreamInfo Audio() => new() { CodecType = "audio", CodecName = "aac", Channels = 2 };

	// The report's ffprobe output, trimmed to what the reader looks at.
	const string ReportJson = """
		{
		  "streams": [
		    { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1280, "height": 720, "r_frame_rate": "60/1",
		      "disposition": { "default": 1, "attached_pic": 0 } },
		    { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "sample_rate": "48000",
		      "disposition": { "default": 1, "attached_pic": 0 } },
		    { "index": 2, "codec_name": "mjpeg", "codec_type": "video", "width": 1920, "height": 1080, "r_frame_rate": "90000/1",
		      "disposition": { "default": 0, "attached_pic": 1 } }
		  ],
		  "format": { "duration": "517.910000" }
		}
		""";

	[Fact]
	public void ProbeOutput_MarksTheCoverAsAttachedPicture() {
		var info = FFProbeJsonReader.Read(Encoding.UTF8.GetBytes(ReportJson), "sample.mp4");

		Assert.False(info.Streams[0].IsAttachedPicture);
		Assert.False(info.Streams[1].IsAttachedPicture);
		Assert.True(info.Streams[2].IsAttachedPicture);
	}

	[Fact]
	public void ResultRow_DescribesTheVideo_NotTheCover() {
		var entry = new FileEntry {
			_Path = Path.Combine(Path.GetTempPath(), "sample.mp4"),
			mediaInfo = FFProbeJsonReader.Read(Encoding.UTF8.GetBytes(ReportJson), "sample.mp4"),
		};

		var item = new DuplicateItem(entry, 0f, Guid.NewGuid(), DuplicateFlags.None);

		Assert.Equal("h264", item.Format);
		Assert.Equal("1280x720", item.FrameSize);
		Assert.Equal(60f, item.Fps);
	}

	[Fact]
	public void AttachedPicture_LosesToTheVideo_WhateverItsSize() {
		var streams = new[] { Video("h264", 1280, 720), Audio(), Video("png", 3000, 3000, attached: true) };
		Assert.Equal(0, DuplicateItem.SelectVideoStream(streams));
	}

	[Fact]
	public void EntriesProbedBeforeTheFlag_RecognizeTheCoverByItsCodec() {
		var streams = new[] { Video("hevc", 1280, 720), Audio(), Video("mjpeg", 1920, 1080, fps: 90000) };
		Assert.Equal(0, DuplicateItem.SelectVideoStream(streams));
	}

	[Fact]
	public void MotionJpegVideo_WithoutAnyOtherVideo_IsTheVideo() {
		var streams = new[] { Video("mjpeg", 640, 480), Audio() };
		Assert.Equal(0, DuplicateItem.SelectVideoStream(streams));
	}

	[Fact]
	public void TwoRealVideoStreams_TheLargerWins_AndTiesGoToTheLowerIndex() {
		Assert.Equal(1, DuplicateItem.SelectVideoStream(new[] { Video("h264", 640, 360), Video("h264", 1920, 1080) }));
		Assert.Equal(0, DuplicateItem.SelectVideoStream(new[] { Video("h264", 1280, 720), Video("hevc", 1280, 720) }));
	}

	[Fact]
	public void OnlyACover_IsStillShown() {
		Assert.Equal(1, DuplicateItem.SelectVideoStream(new[] { Audio(), Video("mjpeg", 500, 500, attached: true) }));
		Assert.Equal(-1, DuplicateItem.SelectVideoStream(new[] { Audio() }));
	}

	[Fact]
	public void AttachedPictureFlag_SurvivesTheDatabase() {
		var info = new MediaInfo { Streams = new[] { Video("h264", 1280, 720), Video("mjpeg", 1920, 1080, attached: true) } };
		var restored = MemoryPackSerializer.Deserialize<MediaInfo>(MemoryPackSerializer.Serialize(info))!;
		Assert.False(restored.Streams[0].IsAttachedPicture);
		Assert.True(restored.Streams[1].IsAttachedPicture);
	}
}

// #907: the results can show the modified date, so the result row has to carry it.
public class DuplicateItemDatesTests {
	[Fact]
	public void DuplicateItem_TakesBothDatesFromTheScan() {
		var entry = new FileEntry {
			_Path = Path.Combine(Path.GetTempPath(), "missing.mp4"),
			DateCreated = new DateTime(2026, 9, 1), DateModified = new DateTime(2014, 6, 15),
		};
		var item = new DuplicateItem(entry, 0f, Guid.NewGuid(), DuplicateFlags.None);
		Assert.Equal(new DateTime(2026, 9, 1), item.DateCreated);
		Assert.Equal(new DateTime(2014, 6, 15), item.DateModified);
	}
}
