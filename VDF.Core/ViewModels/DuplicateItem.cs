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

using System.Diagnostics;
using System.Text.Json.Serialization;
using VDF.Core.Utils;

namespace VDF.Core.ViewModels {
	[DebuggerDisplay("{" + nameof(Path) + ",nq}")]
	// Serialized members need PUBLIC setters: the GUI's source-generated JSON context
	// lives in another assembly, where [JsonInclude] on a private setter throws
	// InvalidOperationException at deserialization time (broke backup restore, #789).
	public class DuplicateItem : ViewModelBase {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
		public DuplicateItem() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

		public DuplicateItem(FileEntry file, float difference, Guid groupID, DuplicateFlags flags) {
			Path = file.Path;
			Folder = file.Folder;
			GroupId = groupID;
			Flags = flags;
			if (!file.IsImage && file.mediaInfo?.Streams?.Length > 0) {
				Duration = file.mediaInfo.Duration;
				/*
					Stream selection rules:
					See: https://ffmpeg.org/ffmpeg.html#Automatic-stream-selection
					In the absence of any map options[...] It will select that stream based upon the following criteria:
					for video, it is the stream with the highest resolution,
					for audio, it is the stream with the most channels,
					In the case where several streams of the same type rate equally, the stream with the lowest index is chosen.
				*/
				int[] selAudio = { -1, 0 };
				for (int i = file.mediaInfo.Streams.Length - 1; i >= 0; i--) {
					if (file.mediaInfo.Streams[i].CodecType?.Equals("audio", StringComparison.OrdinalIgnoreCase) == true &&
							 file.mediaInfo.Streams[i].Channels >= selAudio[1]) {
						selAudio[0] = i;
						selAudio[1] = file.mediaInfo.Streams[i].Channels;
					}
				}

				int selectedVideo = SelectVideoStream(file.mediaInfo.Streams);
				if (selectedVideo >= 0) {
					int i = selectedVideo;
					Format = file.mediaInfo.Streams[i].CodecName ?? "<Unknown>";
					Fps = file.mediaInfo.Streams[i].FrameRate;
					BitRateKbs = Math.Round((decimal)file.mediaInfo.Streams[i].BitRate / 1000);
					FrameSize = file.mediaInfo.Streams[i].Width + "x" + file.mediaInfo.Streams[i].Height;
					FrameSizeInt = file.mediaInfo.Streams[i].Width + file.mediaInfo.Streams[i].Height;
					HdrFormat = file.mediaInfo.Streams[i].HdrFormat ?? string.Empty;
				}
				if (selAudio[0] >= 0) {
					int i = selAudio[0];
					AudioFormat = file.mediaInfo.Streams[i].CodecName ?? "<Unknown>";
					AudioChannel = file.mediaInfo.Streams[i].ChannelLayout ?? "<Unknown>";
					AudioSampleRate = file.mediaInfo.Streams[i].SampleRate;
					AudioBitRateKbs = Math.Round((decimal)file.mediaInfo.Streams[i].BitRate / 1000);
				}
				ApplyTrackLanguages(file.mediaInfo.Streams);
			}
			else {
				//We have only one stream if its an image
				if (file.mediaInfo?.Streams?.Length > 0) {
					FrameSize = file.mediaInfo.Streams[0].Width + "x" + file.mediaInfo.Streams[0].Height;
					FrameSizeInt = file.mediaInfo.Streams[0].Width + file.mediaInfo.Streams[0].Height;
				}
			}
			var fi = new FileInfo(Path);
			DateCreated = file.DateCreated;
			DateModified = file.DateModified;
			// A missing file (deleted/offline entry included in the comparison) keeps its
			// database-recorded size instead of a -1 sentinel that rendered as "-1.0 B".
			SizeLong = fi.Exists ? fi.Length : file.FileSize;
			if (file.IsImage)
				Format = fi.Extension[1..];
			Similarity = (1f - difference) * 100;
			IsImage = file.IsImage;
		}

		/// <summary>
		/// The video stream the metadata columns describe: the one with the highest resolution
		/// (ties: the lowest index), as FFmpeg picks it, but never embedded cover art while the
		/// file has a real video stream. FFmpeg skips attached pictures in its own selection;
		/// VDF did not, so an mp4 with a 1080p cover over 720p video was listed as a 1080p
		/// mjpeg file (#905). Entries probed before the attached-picture flag existed have it
		/// false, so a still-image codec next to a moving-picture one counts as a cover too.
		/// Returns -1 when there is no video stream.
		/// </summary>
		internal static int SelectVideoStream(MediaInfo.StreamInfo[] streams) {
			static bool IsVideo(MediaInfo.StreamInfo s) => s.CodecType?.Equals("video", StringComparison.OrdinalIgnoreCase) == true;
			bool hasMovingPicture = System.Linq.Enumerable.Any(streams, s => IsVideo(s) && !s.IsAttachedPicture && !IsStillImageCodec(s.CodecName));
			bool IsCover(MediaInfo.StreamInfo s) => s.IsAttachedPicture || (hasMovingPicture && IsStillImageCodec(s.CodecName));

			int best = -1, bestCover = -1;
			long bestPixels = -1, bestCoverPixels = -1;
			for (int i = 0; i < streams.Length; i++) {
				var s = streams[i];
				if (!IsVideo(s)) continue;
				long pixels = (long)s.Width * s.Height;
				if (IsCover(s)) {
					if (pixels > bestCoverPixels) { bestCover = i; bestCoverPixels = pixels; }
				}
				else if (pixels > bestPixels) { best = i; bestPixels = pixels; }
			}
			return best >= 0 ? best : bestCover;
		}

		/// <summary>
		/// Fills <see cref="AudioLanguages"/> and <see cref="SubtitleLanguages"/> from the
		/// streams, one entry per track in file order (#899). A track without a language tag
		/// reads "?". Audio stays empty when no audio track is tagged at all: every home video
		/// would otherwise read "?", and the Format column already says there is audio.
		/// Subtitles are listed even untagged, because having them at all is the information.
		/// </summary>
		internal void ApplyTrackLanguages(MediaInfo.StreamInfo[] streams) {
			AudioLanguages = JoinLanguages(streams, "audio", listUntaggedOnly: false);
			SubtitleLanguages = JoinLanguages(streams, "subtitle", listUntaggedOnly: true);
		}

		static string JoinLanguages(MediaInfo.StreamInfo[] streams, string codecType, bool listUntaggedOnly) {
			var codes = new List<string>();
			bool anyTagged = false;
			foreach (var s in streams) {
				if (s.CodecType?.Equals(codecType, StringComparison.OrdinalIgnoreCase) != true)
					continue;
				string code = DisplayLanguage(s.Language);
				anyTagged |= code.Length > 0;
				codes.Add(code.Length > 0 ? code : "?");
			}
			return anyTagged || listUntaggedOnly ? string.Join(", ", codes) : string.Empty;
		}

		/// <summary>
		/// "GER" for "ger" and "deu" alike: ISO 639-2 has two codes for twenty languages, and
		/// Matroska muxers write the bibliographic one while others write the terminology one,
		/// so two copies of the same film would otherwise seem to differ. Unified on the
		/// bibliographic code because that is what mkvtoolnix and most players show.
		/// </summary>
		internal static string DisplayLanguage(string? tag) {
			if (string.IsNullOrWhiteSpace(tag))
				return string.Empty;
			tag = tag.Trim().ToLowerInvariant();
			if (tag == "und")
				return string.Empty;
			return (TerminologyToBibliographic.TryGetValue(tag, out var bibliographic) ? bibliographic : tag).ToUpperInvariant();
		}

		static readonly Dictionary<string, string> TerminologyToBibliographic = new() {
			["sqi"] = "alb", ["hye"] = "arm", ["eus"] = "baq", ["mya"] = "bur", ["zho"] = "chi",
			["ces"] = "cze", ["nld"] = "dut", ["fra"] = "fre", ["kat"] = "geo", ["deu"] = "ger",
			["ell"] = "gre", ["isl"] = "ice", ["mkd"] = "mac", ["mri"] = "mao", ["msa"] = "may",
			["fas"] = "per", ["ron"] = "rum", ["slk"] = "slo", ["bod"] = "tib", ["cym"] = "wel",
		};

		/// <summary>Codecs FFmpeg uses for embedded pictures. mjpeg is also real (motion JPEG) video, which is why it only counts next to another video stream.</summary>
		static bool IsStillImageCodec(string? codec) => codec is not null && (
			codec.Equals("mjpeg", StringComparison.OrdinalIgnoreCase) ||
			codec.Equals("png", StringComparison.OrdinalIgnoreCase) ||
			codec.Equals("bmp", StringComparison.OrdinalIgnoreCase) ||
			codec.Equals("gif", StringComparison.OrdinalIgnoreCase) ||
			codec.Equals("webp", StringComparison.OrdinalIgnoreCase) ||
			codec.Equals("tiff", StringComparison.OrdinalIgnoreCase));

		public Guid GroupId { get; set; }
		/// <summary>Encoded thumbnails (JPEG bytes; or the shared placeholder bytes when extraction failed).</summary>
		[JsonIgnore]
		public List<byte[]> ImageList { get; private set; } = new List<byte[]>();
		/// <summary>
		/// ThumbnailMaxWidth setting in effect when <see cref="ImageList"/> was extracted.
		/// 0 = unknown (older backups) or placeholder-only. Lets explicit thumbnail reloads
		/// re-extract when the user has since raised the setting (issue #777).
		/// </summary>
		public int ThumbnailWidth { get; set; }
		[JsonInclude]
		public List<TimeSpan> ThumbnailTimestamps { get; set; } = new List<TimeSpan>();
		string _Path = string.Empty;
		public string Path {
			get => _Path;
			set {
				if (_Path == value) return;
				_Path = value;
				OnPropertyChanged(nameof(Path));
			}
		}
		public long SizeLong { get; set; }
		public bool IsBestSize { get; set; }
		public string Size => SizeLong.BytesToString();
		public float Similarity { get; set; }
		public string Folder { get; set; }
		public TimeSpan Duration { get; set; }
		public bool IsBestDuration { get; set; }
		public string? FrameSize { get; set; }
		[JsonInclude]
		public int FrameSizeInt { get; set; }
		public bool IsBestFrameSize { get; set; }
		[JsonInclude]
		public string? Format { get; set; }
		[JsonInclude]
		public string? AudioFormat { get; set; }
		[JsonInclude]
		public string? AudioChannel { get; set; }
		[JsonInclude]
		public int AudioSampleRate { get; set; }
		public bool IsBestAudioSampleRate { get; set; }
		[JsonInclude]
		public decimal AudioBitRateKbs { get; set; }
		public bool IsBestAudioBitRateKbs { get; set; }
		[JsonInclude]
		public decimal BitRateKbs { get; set; }
		public bool IsBestBitRateKbs { get; set; }
		[JsonInclude]
		public string HdrFormat { get; set; } = string.Empty;
		/// <summary>"GER, ENG": language of every audio track, see <see cref="ApplyTrackLanguages"/>.</summary>
		[JsonInclude]
		public string AudioLanguages { get; set; } = string.Empty;
		/// <summary>"GER, ?": language of every subtitle track; empty when the file has none.</summary>
		[JsonInclude]
		public string SubtitleLanguages { get; set; } = string.Empty;
		public bool IsBestHdrFormat { get; set; }
		[JsonIgnore]
		public int FolderDepth {
			get {
				var span = _Path.AsSpan();
				int count = span.Count(System.IO.Path.DirectorySeparatorChar);
				if (System.IO.Path.DirectorySeparatorChar != System.IO.Path.AltDirectorySeparatorChar)
					count += span.Count(System.IO.Path.AltDirectorySeparatorChar);
				return count;
			}
		}
		[JsonIgnore]
		public int HdrFormatRank => HdrFormat switch {
			"Dolby Vision" => 4,
			"HDR10+" => 3,
			"HDR10" => 2,
			"HLG" => 1,
			_ => 0
		};
		[JsonInclude]
		public float Fps { get; set; }
		public bool IsBestFps { get; set; }
		[JsonInclude]
		public DateTime DateCreated { get; set; }
		/// <summary>Last write time. Default for results saved before it was recorded.</summary>
		[JsonInclude]
		public DateTime DateModified { get; set; }
		[JsonInclude]
		public DuplicateFlags Flags { get; set; }

		[JsonInclude]
		public bool IsImage { get; set; }
		/// <summary>
		/// For <see cref="DuplicateFlags.PartialClip"/> items: the time position within the
		/// source (longer) video where this clip begins.  Zero for all other items.
		/// </summary>
		[JsonInclude]
		public TimeSpan PartialClipOffset { get; set; }

		[JsonIgnore]
		public string PartialClipOffsetDisplay =>
			Flags.HasFlag(DuplicateFlags.PartialClip) && PartialClipOffset > TimeSpan.Zero
				? $"@ {PartialClipOffset:hh\\:mm\\:ss}"
				: string.Empty;

		/// <summary>This pair was found only by the AI embedding pass (chip in the results list).</summary>
		[JsonIgnore]
		public bool IsAiMatched => Flags.HasFlag(DuplicateFlags.AiMatched);

		[JsonIgnore]
		public Action? ThumbnailsUpdated;
		public void SetThumbnails(List<byte[]>? images, List<TimeSpan> timeSpans) {
			if (images == null) return;
			ImageList = images;
			ThumbnailTimestamps = timeSpans;
			ThumbnailsUpdated?.Invoke();
		}

	}
}
