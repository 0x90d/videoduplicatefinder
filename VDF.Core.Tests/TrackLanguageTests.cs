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
// #899: the results show the language of every audio and subtitle track.

using System.Text;
using MemoryPack;
using VDF.Core.FFTools;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests;

public class TrackLanguageTests {
	// ffprobe output of a real mkv (German + English audio, German SubRip subtitles),
	// trimmed to what the reader looks at; the tags are nested exactly as ffprobe prints them.
	const string MkvJson = """
		{
		  "streams": [
		    { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 800, "r_frame_rate": "24000/1001",
		      "disposition": { "default": 1, "forced": 0, "attached_pic": 0 },
		      "tags": { "BPS": "4829326", "DURATION": "01:43:34.792000000" } },
		    { "index": 1, "codec_name": "ac3", "codec_type": "audio", "sample_rate": "48000", "channels": 6, "channel_layout": "5.1(side)",
		      "disposition": { "default": 1, "forced": 0, "attached_pic": 0 },
		      "tags": { "language": "ger", "BPS": "448000" } },
		    { "index": 2, "codec_name": "dts", "codec_type": "audio", "sample_rate": "48000", "channels": 6, "channel_layout": "5.1(side)",
		      "disposition": { "default": 0, "forced": 0, "attached_pic": 0 },
		      "tags": { "language": "eng", "BPS": "1509000" } },
		    { "index": 3, "codec_name": "subrip", "codec_type": "subtitle", "r_frame_rate": "0/0",
		      "disposition": { "default": 1, "forced": 0, "attached_pic": 0 },
		      "tags": { "language": "ger", "BPS": "0" } }
		  ],
		  "format": { "duration": "1:43:34.816000", "bit_rate": "6111476",
		    "tags": { "encoder": "libebml v1.3.1 + libmatroska v1.4.2", "creation_time": "2016-07-20T15:21:03.000000Z" } }
		}
		""";

	// An mp4 as phones and many encoders write it: "und" audio, a mov_text track in "deu".
	const string Mp4Json = """
		{
		  "streams": [
		    { "index": 0, "codec_name": "hevc", "codec_type": "video", "width": 1920, "height": 1080, "r_frame_rate": "30/1",
		      "tags": { "language": "und", "handler_name": "VideoHandler" } },
		    { "index": 1, "codec_name": "aac", "codec_type": "audio", "sample_rate": "48000", "channels": 2,
		      "tags": { "language": "und", "handler_name": "SoundHandler" } },
		    { "index": 2, "codec_name": "mov_text", "codec_type": "subtitle",
		      "tags": { "language": "deu" } },
		    { "index": 3, "codec_name": "mov_text", "codec_type": "subtitle" }
		  ],
		  "format": { "duration": "12.000000" }
		}
		""";

	static MediaInfo Read(string json) => FFProbeJsonReader.Read(Encoding.UTF8.GetBytes(json), "sample");

	static DuplicateItem Item(MediaInfo info) => new(new FileEntry {
		_Path = Path.Combine(Path.GetTempPath(), "sample.mkv"),
		mediaInfo = info,
	}, 0f, Guid.NewGuid(), DuplicateFlags.None);

	[Fact]
	public void Reader_TakesTheLanguageOutOfTheNestedTags() {
		var info = Read(MkvJson);
		Assert.Equal(new[] { "", "ger", "eng", "ger" }, info.Streams.Select(s => s.Language));
	}

	[Fact]
	public void Reader_TreatsUndAsNoTag_AndAMissingTagAsEmptyNotNull() {
		var info = Read(Mp4Json);
		Assert.Equal(new[] { "", "", "deu", "" }, info.Streams.Select(s => s.Language));
	}

	[Fact]
	public void ResultRow_ListsEveryAudioAndSubtitleTrack() {
		var item = Item(Read(MkvJson));
		Assert.Equal("GER, ENG", item.AudioLanguages);
		Assert.Equal("GER", item.SubtitleLanguages);
	}

	[Fact]
	public void UntaggedAudio_StaysEmpty_UntaggedSubtitles_ReadAsQuestionMarks() {
		var item = Item(Read(Mp4Json));
		Assert.Equal(string.Empty, item.AudioLanguages);
		// "deu" reads as GER, the same as the mkv's "ger": copies must not seem to differ.
		Assert.Equal("GER, ?", item.SubtitleLanguages);
	}

	[Fact]
	public void UntaggedAudio_NextToATaggedOne_ReadsAsQuestionMark() {
		var info = new MediaInfo {
			Streams = new[] {
				new MediaInfo.StreamInfo { Index = "0", CodecType = "video", CodecName = "h264", Width = 640, Height = 360, Language = "" },
				new MediaInfo.StreamInfo { Index = "1", CodecType = "audio", CodecName = "aac", Channels = 2, Language = "fra" },
				new MediaInfo.StreamInfo { Index = "2", CodecType = "audio", CodecName = "aac", Channels = 2, Language = "" },
			}
		};
		var item = Item(info);
		Assert.Equal("FRE, ?", item.AudioLanguages);
		Assert.Equal(string.Empty, item.SubtitleLanguages);
	}

	[Theory]
	[InlineData("ger", "GER")]
	[InlineData("deu", "GER")]
	[InlineData("FRA", "FRE")]
	[InlineData("zho", "CHI")]
	[InlineData("eng", "ENG")]
	[InlineData("en", "EN")]
	[InlineData(" und ", "")]
	[InlineData("", "")]
	[InlineData(null, "")]
	public void DisplayLanguage_UnifiesTheTwoIsoCodes(string? tag, string expected) =>
		Assert.Equal(expected, DuplicateItem.DisplayLanguage(tag));

	[Fact]
	public void Language_SurvivesTheDatabase_IncludingNullForOlderEntries() {
		var info = new MediaInfo {
			Streams = new[] {
				new MediaInfo.StreamInfo { CodecType = "audio", Language = "ger" },
				new MediaInfo.StreamInfo { CodecType = "audio", Language = "" },
				new MediaInfo.StreamInfo { CodecType = "audio", Language = null },
			}
		};
		var restored = MemoryPackSerializer.Deserialize<MediaInfo>(MemoryPackSerializer.Serialize(info))!;
		Assert.Equal("ger", restored.Streams[0].Language);
		Assert.Equal(string.Empty, restored.Streams[1].Language);
		Assert.Null(restored.Streams[2].Language);
	}

	[Fact]
	public void NeedsTrackLanguages_OnlyForAudioOrSubtitleStreamsProbedBeforeTheField() {
		var fresh = Read(MkvJson);
		Assert.False(ScanEngine.NeedsTrackLanguages(fresh));

		var videoOnlyOld = new MediaInfo { Streams = new[] { new MediaInfo.StreamInfo { CodecType = "video" } } };
		Assert.False(ScanEngine.NeedsTrackLanguages(videoOnlyOld));

		var old = new MediaInfo { Streams = new[] { new MediaInfo.StreamInfo { CodecType = "video" }, new MediaInfo.StreamInfo { CodecType = "subtitle" } } };
		Assert.True(ScanEngine.NeedsTrackLanguages(old));
		Assert.False(ScanEngine.NeedsTrackLanguages(null));
	}

	[Fact]
	public void Merge_MatchesByIndex_OrByPositionWithoutOne_AndMarksMissingStreamsAsAsked() {
		var probed = Read(MkvJson);

		var byIndex = new MediaInfo {
			Streams = new[] {
				new MediaInfo.StreamInfo { Index = "3", CodecType = "subtitle" },
				new MediaInfo.StreamInfo { Index = "1", CodecType = "audio" },
				new MediaInfo.StreamInfo { Index = "9", CodecType = "audio" },
			}
		};
		ScanEngine.MergeTrackLanguages(byIndex, probed);
		Assert.Equal(new[] { "ger", "ger", "" }, byIndex.Streams.Select(s => s.Language));

		// Entries migrated from the legacy protobuf database carry no stream index.
		var legacy = new MediaInfo {
			Streams = new[] {
				new MediaInfo.StreamInfo { CodecType = "video" },
				new MediaInfo.StreamInfo { CodecType = "audio" },
				new MediaInfo.StreamInfo { CodecType = "audio" },
			}
		};
		ScanEngine.MergeTrackLanguages(legacy, probed);
		Assert.Equal(new[] { "", "ger", "eng" }, legacy.Streams.Select(s => s.Language));
	}

	// Existing databases: every entry there was probed before languages existed. The scan's
	// compare phase re-reads exactly the result videos that lack them, once, and the rows show them.
	[Fact]
	public void Backfill_ReprobesOnlyOldResultVideos_AndUpdatesEntryAndRows() {
		string dir = Directory.CreateTempSubdirectory("vdf-langs-").FullName;
		try {
			FileEntry Entry(string name, MediaInfo info, bool isImage = false) {
				string path = Path.Combine(dir, name);
				File.WriteAllBytes(path, new byte[] { 1 });
				return new FileEntry { _Path = path, Folder = dir, mediaInfo = info, IsImage = isImage };
			}
			MediaInfo Old() => new() {
				Streams = new[] {
					new MediaInfo.StreamInfo { Index = "0", CodecType = "video", CodecName = "h264", Width = 1920, Height = 800 },
					new MediaInfo.StreamInfo { Index = "1", CodecType = "audio", CodecName = "ac3", Channels = 6 },
					new MediaInfo.StreamInfo { Index = "2", CodecType = "audio", CodecName = "dts", Channels = 6 },
					new MediaInfo.StreamInfo { Index = "3", CodecType = "subtitle", CodecName = "subrip" },
				}
			};
			var old = Entry("old.mkv", Old());
			var failing = Entry("failing.mkv", Old());
			var fresh = Entry("fresh.mkv", Read(MkvJson));
			var gone = Entry("gone.mkv", Old());
			File.Delete(gone.Path);
			var entries = new[] { old, failing, fresh, gone }.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);

			var group = Guid.NewGuid();
			var engine = new ScanEngine();
			foreach (var e in entries.Values)
				engine.Duplicates.Add(new DuplicateItem(e, 0f, group, DuplicateFlags.None));
			var probedPaths = new List<string>();

			int count = engine.BackfillTrackLanguages(
				path => entries.GetValueOrDefault(path),
				path => {
					lock (probedPaths) probedPaths.Add(path);
					return path == failing.Path ? null : Read(MkvJson);
				},
				CancellationToken.None);

			Assert.Equal(1, count);
			Assert.Equal(new[] { failing.Path, old.Path }.Order(), probedPaths.Order());
			Assert.Equal(new[] { "", "ger", "eng", "ger" }, old.mediaInfo!.Streams.Select(s => s.Language));
			var oldRow = engine.Duplicates.Single(d => d.Path == old.Path);
			Assert.Equal("GER, ENG", oldRow.AudioLanguages);
			Assert.Equal("GER", oldRow.SubtitleLanguages);
			// A failed probe leaves the entry unmarked, so the next scan tries again.
			Assert.True(ScanEngine.NeedsTrackLanguages(failing.mediaInfo));

			// The second compare has nothing left but the failure.
			probedPaths.Clear();
			engine.BackfillTrackLanguages(path => entries.GetValueOrDefault(path), path => {
				lock (probedPaths) probedPaths.Add(path);
				return null;
			}, CancellationToken.None);
			Assert.Equal(new[] { failing.Path }, probedPaths);
		}
		finally {
			Directory.Delete(dir, recursive: true);
		}
	}
}
