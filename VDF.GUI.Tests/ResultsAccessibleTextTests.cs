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

using System.Globalization;
using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests;

/// <summary>The one sentence a screen reader speaks for a row of the results list.</summary>
public class ResultsAccessibleTextTests {

	static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

	static DuplicateItem Video(string path, float similarity = 98.4f) => new() {
		Path = path, Similarity = similarity, SizeLong = 700_000_000, FrameSize = "1280x720",
		Duration = TimeSpan.FromSeconds(754), GroupId = Guid.NewGuid(),
	};

	[Fact]
	public void Item_SaysFileSimilaritySizeQualityThenFolder() {
		var info = Video(Path.Combine("videos", "copy", "beach (1).mp4"));

		string text = ResultsAccessibleText.DescribeItem(info, isBest: false, isTombstone: false, isOffline: false,
			RowSpeechWords.Default, Invariant);

		Assert.StartsWith("beach (1).mp4, ", text);
		Assert.Contains(info.Size, text);
		Assert.Contains("1280x720", text);
		Assert.Contains("00:12:34", text);
		Assert.EndsWith(Path.Combine("videos", "copy"), text);
		Assert.True(text.IndexOf("1280x720", StringComparison.Ordinal) < text.IndexOf(Path.Combine("videos", "copy"), StringComparison.Ordinal));
	}

	[Fact]
	public void Item_NamesTheBadgesASightedUserSees() {
		var info = Video(Path.Combine("v", "a.mp4"));
		info.Flags |= VDF.Core.DuplicateFlags.AiMatched;

		string text = ResultsAccessibleText.DescribeItem(info, isBest: true, isTombstone: true, isOffline: true,
			RowSpeechWords.Default, Invariant);

		Assert.Contains(", best", text);
		Assert.Contains(", AI match", text);
		Assert.Contains(", already deleted", text);
		Assert.Contains(", offline", text);
	}

	[Fact]
	public void Item_WithoutBadges_MentionsNone() {
		string text = ResultsAccessibleText.DescribeItem(Video(Path.Combine("v", "a.mp4")), false, false, false,
			RowSpeechWords.Default, Invariant);

		Assert.DoesNotContain("best", text);
		Assert.DoesNotContain("offline", text);
		Assert.DoesNotContain("deleted", text);
	}

	[Fact]
	public void Image_HasNoDuration() {
		var info = Video(Path.Combine("v", "photo.jpg"));
		info.IsImage = true;

		string text = ResultsAccessibleText.DescribeItem(info, false, false, false, RowSpeechWords.Default, Invariant);

		Assert.DoesNotContain("00:12:34", text);
	}

	[Fact]
	public void UsesTheTranslatedWords() {
		var words = new RowSpeechWords { Best = "BESTE", Offline = "Offline-Datei" };

		string text = ResultsAccessibleText.DescribeItem(Video(Path.Combine("v", "a.mp4")), true, false, true, words, Invariant);

		Assert.Contains("BESTE", text);
		Assert.Contains("Offline-Datei", text);
	}

	[Fact]
	public void CheckedState_ComesFirst_BecauseItDecidesWhatADeleteRemoves() {
		Assert.Equal("checked, a.mp4, 98 %", ResultsAccessibleText.WithCheckedState("a.mp4, 98 %", true, "checked"));
		Assert.Equal("a.mp4, 98 %", ResultsAccessibleText.WithCheckedState("a.mp4, 98 %", false, "checked"));
	}

	[Fact]
	public void Builder_GivesEveryRowAndGroupASpokenName() {
		var group = Guid.NewGuid();
		var items = new[] { "a.mp4", "b.mp4" }.Select(n => {
			var info = Video(Path.Combine("v", n));
			info.GroupId = group;
			return new DuplicateItemVM(info);
		}).ToList();

		var result = ResultsListBuilder.Build(new ResultsBuildRequest {
			Items = items,
			IsTombstone = _ => false,
			IsOffline = _ => false,
			PickBest = members => (members[0], "sharpest"),
		});

		var header = Assert.Single(result.Groups);
		Assert.StartsWith("Group 1, 2 files", header.AccessibleName);
		var rows = result.Rows.OfType<ResultsItemRow>().ToList();
		Assert.Equal(2, rows.Count);
		Assert.StartsWith("a.mp4, ", rows[0].AccessibleName);
		Assert.Contains(", best", rows[0].AccessibleName);
		Assert.DoesNotContain(", best", rows[1].AccessibleName);
	}
}
