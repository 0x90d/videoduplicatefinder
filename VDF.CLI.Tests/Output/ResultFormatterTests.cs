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
using VDF.CLI.Output;
using VDF.Core.ViewModels;

namespace VDF.CLI.Tests.Output;

public class ResultFormatterTests {
	static DuplicateItem MakeItem(Guid groupId, float similarity = 95f, string path = "/test/video.mp4",
		long size = 1024) {
		var json = JsonSerializer.Serialize(new {
			GroupId = groupId,
			SizeLong = size,
			Similarity = similarity,
			Duration = TimeSpan.FromSeconds(60),
			FrameSizeInt = 1920,
			BitRateKbs = 5000m,
			IsBestBitRateKbs = false,
			IsBestFrameSize = false,
			Path = path,
			Folder = "/test",
			IsImage = false,
			Flags = 0,
			Fps = 30.0f,
			Format = "h264",
			FrameSize = "1920x1080",
			DateCreated = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			HdrFormat = "",
			PartialClipOffset = TimeSpan.Zero,
			ThumbnailTimestamps = new List<TimeSpan>()
		});
		return JsonSerializer.Deserialize<DuplicateItem>(json)!;
	}

	static readonly Guid Group1 = Guid.NewGuid();

	[Fact]
	public void Format_Text_ContainsGroupCount() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, path: "/test/a.mp4"),
			MakeItem(Group1, path: "/test/b.mp4"),
		};

		string result = ResultFormatter.Format(items, OutputFormat.Text);

		Assert.Contains("1 duplicate group(s)", result);
		Assert.Contains("2 total file(s)", result);
	}

	[Fact]
	public void Format_Json_ValidJson() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, path: "/test/a.mp4"),
			MakeItem(Group1, path: "/test/b.mp4"),
		};

		string result = ResultFormatter.Format(items, OutputFormat.Json);

		// Should parse without error
		var doc = JsonDocument.Parse(result);
		Assert.NotNull(doc);
	}

	[Fact]
	public void Format_Csv_HasHeader() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1),
		};

		string result = ResultFormatter.Format(items, OutputFormat.Csv);

		string firstLine = result.Split('\n')[0].Trim();
		Assert.Contains("GroupId", firstLine);
		Assert.Contains("Similarity", firstLine);
		Assert.Contains("Path", firstLine);
		Assert.Contains("Size", firstLine);
	}

	[Fact]
	public void Format_Csv_EscapesCommasInPath() {
		var items = new List<DuplicateItem> {
			MakeItem(Group1, path: "/test/my,video.mp4"),
		};

		string result = ResultFormatter.Format(items, OutputFormat.Csv);

		// Path with comma should be quoted
		Assert.Contains("\"/test/my,video.mp4\"", result);
	}

	[Fact]
	public void Format_Text_EmptyInput_NoGroups() {
		var items = new List<DuplicateItem>();

		string result = ResultFormatter.Format(items, OutputFormat.Text);

		Assert.Contains("0 duplicate group(s)", result);
	}

	[Fact]
	public void Format_Csv_EmptyInput_HeaderOnly() {
		var items = new List<DuplicateItem>();

		string result = ResultFormatter.Format(items, OutputFormat.Csv);

		var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		Assert.Single(lines); // header only
	}

	[Fact]
	public void Format_Json_EmptyInput_EmptyArray() {
		var items = new List<DuplicateItem>();

		string result = ResultFormatter.Format(items, OutputFormat.Json);

		Assert.Equal("[]", result.Trim());
	}
}
