// /*
//     Copyright (C) 2025 0x90d
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

using System.Text.Json;
using VDF.CLI.Actions;
using VDF.Core.ViewModels;

namespace VDF.CLI.Tests.Actions;

public class DeletionStrategyTests {
	static DuplicateItem MakeItem(Guid groupId, long size, float similarity = 100f,
		double durationSeconds = 60, int frameSizeInt = 1920, decimal bitRateKbs = 5000,
		bool isBestBitRate = false, bool isBestFrameSize = false) {
		// Use JSON deserialization to set private-setter properties via [JsonInclude]
		var json = JsonSerializer.Serialize(new {
			GroupId = groupId,
			SizeLong = size,
			Similarity = similarity,
			Duration = TimeSpan.FromSeconds(durationSeconds),
			FrameSizeInt = frameSizeInt,
			BitRateKbs = bitRateKbs,
			IsBestBitRateKbs = isBestBitRate,
			IsBestFrameSize = isBestFrameSize,
			Path = $"/test/{size}.mp4",
			Folder = "/test",
			IsImage = false,
			Flags = 0,
			Fps = 30.0f,
			DateCreated = DateTime.UtcNow,
			HdrFormat = "",
			PartialClipOffset = TimeSpan.Zero,
			ThumbnailTimestamps = new List<TimeSpan>()
		});
		return JsonSerializer.Deserialize<DuplicateItem>(json)!;
	}

	static readonly Guid Group1 = Guid.NewGuid();
	static readonly Guid Group2 = Guid.NewGuid();

	[Fact]
	public void SelectForDeletion_SmallestFile_KeepsLargestFile() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000),
			MakeItem(Group1, size: 5000),
			MakeItem(Group1, size: 3000),
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.SmallestFile);

		Assert.Equal(2, toDelete.Count);
		Assert.DoesNotContain(toDelete, d => d.SizeLong == 5000);
		Assert.Contains(toDelete, d => d.SizeLong == 1000);
		Assert.Contains(toDelete, d => d.SizeLong == 3000);
	}

	[Fact]
	public void SelectForDeletion_ShortestDuration_KeepsLongestDuration() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000, durationSeconds: 30),
			MakeItem(Group1, size: 1000, durationSeconds: 120),
			MakeItem(Group1, size: 1000, durationSeconds: 60),
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.ShortestDuration);

		Assert.Equal(2, toDelete.Count);
		Assert.DoesNotContain(toDelete, d => d.Duration == TimeSpan.FromSeconds(120));
	}

	[Fact]
	public void SelectForDeletion_WorstResolution_KeepsHighestResolution() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000, frameSizeInt: 1280),
			MakeItem(Group1, size: 1000, frameSizeInt: 3840),
			MakeItem(Group1, size: 1000, frameSizeInt: 1920),
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.WorstResolution);

		Assert.Equal(2, toDelete.Count);
		Assert.DoesNotContain(toDelete, d => d.FrameSizeInt == 3840);
	}

	[Fact]
	public void SelectForDeletion_SingleItemGroup_NothingToDelete() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000),
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.SmallestFile);

		Assert.Empty(toDelete);
	}

	[Fact]
	public void SelectForDeletion_HundredPercentOnly_SkipsMixedSimilarity() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000, similarity: 100f),
			MakeItem(Group1, size: 5000, similarity: 95f), // not 100%
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.HundredPercentOnly);

		Assert.Empty(toDelete);
	}

	[Fact]
	public void SelectForDeletion_HundredPercentOnly_IncludesAllHundredPercent() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000, similarity: 100f),
			MakeItem(Group1, size: 5000, similarity: 100f),
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.HundredPercentOnly);

		Assert.Single(toDelete);
		Assert.Equal(1000, toDelete[0].SizeLong); // smaller file deleted, larger kept
	}

	[Fact]
	public void SelectForDeletion_LowestQuality_KeepsBestBitRate() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000, isBestBitRate: true),
			MakeItem(Group1, size: 5000, isBestBitRate: false),
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.LowestQuality);

		Assert.Single(toDelete);
		Assert.Equal(5000, toDelete[0].SizeLong); // the non-best item is deleted
	}

	[Fact]
	public void SelectForDeletion_MultipleGroups_ProcessesSeparately() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, size: 1000),
			MakeItem(Group1, size: 5000),
			MakeItem(Group2, size: 2000),
			MakeItem(Group2, size: 8000),
		};

		var toDelete = DeletionStrategy.SelectForDeletion(items, Strategy.SmallestFile);

		Assert.Equal(2, toDelete.Count);
		Assert.Contains(toDelete, d => d.SizeLong == 1000);
		Assert.Contains(toDelete, d => d.SizeLong == 2000);
	}
}
