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

using VDF.Core;
using VDF.Core.Utils;
using VDF.GUI.Data;

namespace VDF.GUI.Tests {
	public class DatabaseEditorRulesTests {

		static FileEntry Entry(string path, EntryFlags flags = 0, long size = 100,
			DateTime? created = null, TimeSpan? duration = null, long videoBitRate = 0) {
			var entry = new FileEntry {
				Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty,
				FileSize = size,
				DateCreated = created ?? new DateTime(2024, 1, 1),
				Flags = flags,
			};
			entry.Path = path;
			if (duration != null || videoBitRate > 0)
				entry.mediaInfo = new MediaInfo {
					Duration = duration ?? TimeSpan.Zero,
					Streams = new[] {
						new MediaInfo.StreamInfo { CodecType = "video", CodecName = "h264", BitRate = videoBitRate, Width = 1920, Height = 1080 },
					},
				};
			return entry;
		}

		[Fact]
		public void Matches_TypeErrorFlagAndSearchAndTogether() {
			var video = Entry(@"D:\a\clip.mp4");
			var image = Entry(@"D:\a\photo.jpg", EntryFlags.IsImage);
			var broken = Entry(@"D:\a\broken.mp4", EntryFlags.ThumbnailError);
			var excluded = Entry(@"D:\b\old.mp4", EntryFlags.ManuallyExcluded);

			Assert.True(DatabaseEditorRules.Matches(video, new DbFilterState()));
			Assert.False(DatabaseEditorRules.Matches(image, new DbFilterState { Type = DbTypeFilter.Videos }));
			Assert.True(DatabaseEditorRules.Matches(image, new DbFilterState { Type = DbTypeFilter.Images }));
			Assert.True(DatabaseEditorRules.Matches(broken, new DbFilterState { ErrorsOnly = true }));
			Assert.False(DatabaseEditorRules.Matches(video, new DbFilterState { ErrorsOnly = true }));
			Assert.True(DatabaseEditorRules.Matches(excluded, new DbFilterState { Flag = EntryFlags.ManuallyExcluded }));
			Assert.False(DatabaseEditorRules.Matches(video, new DbFilterState { Flag = EntryFlags.ManuallyExcluded }));
			Assert.True(DatabaseEditorRules.Matches(video, new DbFilterState { Search = "CLIP" })); // case-insensitive
			Assert.False(DatabaseEditorRules.Matches(video, new DbFilterState { Search = @"D:\b" }));
			// combined: errors-only AND search must both hold
			Assert.False(DatabaseEditorRules.Matches(broken, new DbFilterState { ErrorsOnly = true, Search = "photo" }));
		}

		[Fact]
		public void ManualExclusionIsNotAnError() {
			var excluded = Entry(@"D:\x.mp4", EntryFlags.ManuallyExcluded);
			Assert.False(DatabaseEditorRules.Matches(excluded, new DbFilterState { ErrorsOnly = true }));
		}

		[Fact]
		public void Sort_SizeLargestFirst_PathTiebreak() {
			var entries = new List<FileEntry> {
				Entry(@"D:\b.mp4", size: 100),
				Entry(@"D:\a.mp4", size: 100),
				Entry(@"D:\c.mp4", size: 900),
			};
			entries.Sort(DatabaseEditorRules.BuildComparison(DbSortMode.Size, null));
			Assert.Equal(new[] { @"D:\c.mp4", @"D:\a.mp4", @"D:\b.mp4" }, entries.Select(e => e.Path));
		}

		[Fact]
		public void Sort_FlagFirstPutsFlaggedEntriesOnTop_ThenBaseMode() {
			var entries = new List<FileEntry> {
				Entry(@"D:\big.mp4", size: 900),
				Entry(@"D:\flagged_small.mp4", EntryFlags.TooDark, size: 10),
				Entry(@"D:\flagged_big.mp4", EntryFlags.TooDark, size: 500),
			};
			entries.Sort(DatabaseEditorRules.BuildComparison(DbSortMode.Size, EntryFlags.TooDark));
			Assert.Equal(new[] { @"D:\flagged_big.mp4", @"D:\flagged_small.mp4", @"D:\big.mp4" },
				entries.Select(e => e.Path));
		}

		[Fact]
		public void Sort_BitrateReadsTheVideoStream() {
			var entries = new List<FileEntry> {
				Entry(@"D:\low.mp4", videoBitRate: 1_000_000),
				Entry(@"D:\high.mp4", videoBitRate: 9_000_000),
				Entry(@"D:\unknown.mp4"),
			};
			entries.Sort(DatabaseEditorRules.BuildComparison(DbSortMode.Bitrate, null));
			Assert.Equal(@"D:\high.mp4", entries[0].Path);
			Assert.Equal(@"D:\unknown.mp4", entries[2].Path);
			Assert.Equal(9000m, DatabaseEditorRules.GetBitRate(entries[0]));
		}

		[Fact]
		public void CountFlags_PerFlagAndAnyErrorTotal() {
			var entries = new[] {
				Entry(@"D:\1.mp4", EntryFlags.ThumbnailError | EntryFlags.TooDark),
				Entry(@"D:\2.mp4", EntryFlags.MetadataError),
				Entry(@"D:\3.mp4", EntryFlags.ManuallyExcluded),
				Entry(@"D:\4.mp4"),
			};
			var (perFlag, errors) = DatabaseEditorRules.CountFlags(entries);
			Assert.Equal(1, perFlag[EntryFlags.ThumbnailError]);
			Assert.Equal(1, perFlag[EntryFlags.MetadataError]);
			Assert.Equal(1, perFlag[EntryFlags.TooDark]);
			Assert.Equal(1, perFlag[EntryFlags.ManuallyExcluded]);
			Assert.Equal(2, errors); // entry 1 counts once despite two error flags
		}

		[Theory]
		[InlineData(EntryFlags.ThumbnailError, "err")]
		[InlineData(EntryFlags.MetadataError, "err")]
		[InlineData(EntryFlags.ManuallyExcluded, "warn")]
		[InlineData(EntryFlags.TooDark, "warn")]
		[InlineData(EntryFlags.IsImage, "neutral")]
		[InlineData(EntryFlags.ReparsePoint, "neutral")]
		public void ChipKind_MapsSeverity(EntryFlags flag, string expected) {
			Assert.Equal(expected, DatabaseEditorRules.ChipKind(flag));
		}
	}
}
