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

using System.Text.Json;
using System.Text.Json.Serialization;
using VDF.Core.ViewModels;

namespace VDF.Core.Tests.ViewModels;

// Mirrors the GUI's scan-results backup setup: a source-generated context with
// IncludeFields that lives in a DIFFERENT assembly than DuplicateItem. [JsonInclude]
// on a private setter compiles fine in this configuration but throws
// InvalidOperationException at deserialization time, which broke every backup
// restore and import in 4.0 (#789). Guards against re-privatizing those setters.
[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(DuplicateItem))]
internal partial class CrossAssemblyTestContext : JsonSerializerContext { }

public class DuplicateItemSerializationTests {
	[Fact]
	public void DuplicateItem_RoundTrips_Through_CrossAssembly_SourceGenContext() {
		var item = new DuplicateItem {
			GroupId = Guid.NewGuid(),
			Path = @"C:\media\a.mp4",
			Folder = @"C:\media",
			SizeLong = 12345,
			Similarity = 98.5f,
			Duration = TimeSpan.FromSeconds(61),
			DateCreated = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc),
			IsImage = false,
			Format = "h264",
			AudioFormat = "aac",
			AudioChannel = "stereo",
			AudioSampleRate = 48000,
			AudioBitRateKbs = 192,
			BitRateKbs = 4500,
			FrameSizeInt = 1920 + 1080,
			FrameSize = "1920x1080",
			Fps = 23.976f,
			HdrFormat = "HDR10",
			Flags = DuplicateFlags.Flipped,
			ThumbnailTimestamps = new List<TimeSpan> { TimeSpan.FromSeconds(6) },
			PartialClipOffset = TimeSpan.FromSeconds(3),
		};

		string json = JsonSerializer.Serialize(item, CrossAssemblyTestContext.Default.DuplicateItem);
		var restored = JsonSerializer.Deserialize(json, CrossAssemblyTestContext.Default.DuplicateItem);

		Assert.NotNull(restored);
		Assert.Equal(item.GroupId, restored.GroupId);
		Assert.Equal(item.Path, restored.Path);
		Assert.Equal(item.SizeLong, restored.SizeLong);
		Assert.Equal(item.Similarity, restored.Similarity);
		Assert.Equal(item.Duration, restored.Duration);
		Assert.Equal(item.DateCreated, restored.DateCreated);
		Assert.Equal(item.Format, restored.Format);
		Assert.Equal(item.AudioFormat, restored.AudioFormat);
		Assert.Equal(item.AudioChannel, restored.AudioChannel);
		Assert.Equal(item.AudioSampleRate, restored.AudioSampleRate);
		Assert.Equal(item.AudioBitRateKbs, restored.AudioBitRateKbs);
		Assert.Equal(item.BitRateKbs, restored.BitRateKbs);
		Assert.Equal(item.FrameSizeInt, restored.FrameSizeInt);
		Assert.Equal(item.Fps, restored.Fps);
		Assert.Equal(item.HdrFormat, restored.HdrFormat);
		Assert.Equal(item.Flags, restored.Flags);
		Assert.Equal(item.IsImage, restored.IsImage);
		Assert.Equal(item.ThumbnailTimestamps, restored.ThumbnailTimestamps);
		Assert.Equal(item.PartialClipOffset, restored.PartialClipOffset);
	}
}
