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

using System.Diagnostics;
using System.Drawing;
using VDF.Core.Utils;

namespace VDF.Core.ViewModels {
	[DebuggerDisplay("{" + nameof(Path) + ",nq}")]
	public class DuplicateItem {
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
				int[] selVideo = { -1, 0 };
				int[] selAudio = { -1, 0 };
				for (int i = file.mediaInfo.Streams.Length - 1; i >= 0; i--) {
					if (file.mediaInfo.Streams[i].CodecType.Equals("video", StringComparison.OrdinalIgnoreCase) &&
						file.mediaInfo.Streams[i].Width * file.mediaInfo.Streams[i].Height >= selVideo[1]) {
						selVideo[0] = i;
						selVideo[1] = file.mediaInfo.Streams[i].Width * file.mediaInfo.Streams[i].Height;
					}
					else if (file.mediaInfo.Streams[i].CodecType.Equals("audio", StringComparison.OrdinalIgnoreCase) &&
							 file.mediaInfo.Streams[i].Channels >= selAudio[1]) {
						selAudio[0] = i;
						selAudio[1] = file.mediaInfo.Streams[i].Channels;
					}
				}

				if (selVideo[0] >= 0) {
					int i = selVideo[0];
					Format = file.mediaInfo.Streams[i].CodecName;
					Fps = file.mediaInfo.Streams[i].FrameRate;
					BitRateKbs = Math.Round((decimal)file.mediaInfo.Streams[i].BitRate / 1000);
					FrameSize = file.mediaInfo.Streams[i].Width + "x" + file.mediaInfo.Streams[i].Height;
					FrameSizeInt = file.mediaInfo.Streams[i].Width + file.mediaInfo.Streams[i].Height;
				}
				if (selAudio[0] >= 0) {
					int i = selAudio[0];
					AudioFormat = file.mediaInfo.Streams[i].CodecName;
					AudioChannel = file.mediaInfo.Streams[i].ChannelLayout;
					AudioSampleRate = file.mediaInfo.Streams[i].SampleRate;
				}

			}
			else {
				//We have only one stream if its an image
				if (file.mediaInfo?.Streams?.Length > 0) {
					FrameSize = file.mediaInfo.Streams[0].Width + "x" + file.mediaInfo.Streams[0].Height;
					FrameSizeInt = file.mediaInfo.Streams[0].Width + file.mediaInfo.Streams[0].Height;
				}
			}
			var fi = new FileInfo(Path);
			DateCreated = fi.CreationTimeUtc;
			SizeLong = fi.Exists ? fi.Length : -1;
			if (file.IsImage)
				Format = fi.Extension[1..];
			Similarity = (1f - difference) * 100;
			IsImage = file.IsImage;
		}

		public Guid GroupId { get; set; }
		public List<Image> ImageList { get; private set; } = new List<Image>();
		public string Path { get; set; }
		public long SizeLong { get; set; }
		public bool IsBestSize { get; set; }
		public string Size => SizeLong.BytesToString();
		public float Similarity { get; set; }
		public string Folder { get; set; }
		public TimeSpan Duration { get; set; }
		public bool IsBestDuration { get; set; }
		public string? FrameSize { get; set; }
		public int FrameSizeInt { get; }
		public bool IsBestFrameSize { get; set; }
		public string? Format { get; }
		public string? AudioFormat { get; }
		public string? AudioChannel { get; }
		public int AudioSampleRate { get; }
		public bool IsBestAudioSampleRate { get; set; }
		public decimal BitRateKbs { get; }
		public bool IsBestBitRateKbs { get; set; }
		public float Fps { get; }
		public bool IsBestFps { get; set; }
		public DateTime DateCreated { get; }
		public DuplicateFlags Flags { get; }

		public bool IsImage { get; }
		public Action? ThumbnailsUpdated;
		public void SetThumbnails(List<Image>? th) {
			if (th == null) return;
			ImageList = th;
			ThumbnailsUpdated?.Invoke();
		}

	}
}
