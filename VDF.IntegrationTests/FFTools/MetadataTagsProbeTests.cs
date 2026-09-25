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
using VDF.Core;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>#926: the real ffprobe hands every container and stream tag to the metadata comparison.</summary>
[Collection("Ffmpeg")]
public sealed class MetadataTagsProbeTests : IDisposable {
	readonly FfmpegFixture _fixture;
	readonly string dir = Path.Combine(Path.GetTempPath(), $"vdf_metadata_{Guid.NewGuid():N}");

	public MetadataTagsProbeTests(FfmpegFixture fixture) {
		_fixture = fixture;
		Directory.CreateDirectory(dir);
	}

	public void Dispose() {
		try { Directory.Delete(dir, true); } catch { }
	}

	string Make(string name, string creationDate) {
		string output = Path.Combine(dir, name);
		var psi = new ProcessStartInfo(FfmpegEngine.FFmpegPath) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
		foreach (string a in new[] {
			"-hide_banner", "-loglevel", "error", "-y",
			"-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1",
			"-f", "lavfi", "-i", "sine=frequency=440:duration=1",
			"-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac",
			"-movflags", "use_metadata_tags",
			"-metadata", "creation_time=2023-08-15T12:34:56Z",
			"-metadata", $"com.apple.quicktime.creationdate={creationDate}",
			"-metadata:s:a:0", "language=ger",
			output })
			psi.ArgumentList.Add(a);
		using var p = Process.Start(psi)!;
		string err = p.StandardError.ReadToEnd();
		p.WaitForExit();
		Assert.True(p.ExitCode == 0, $"ffmpeg failed: {err}");
		return output;
	}

	[SkippableFact]
	public void Mp4Copies_TheOneWithTheOtherDate_IsTheOddOne() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!ScanEngine.FFprobeExists, "ffprobe not found");

		string wrong1 = Make("a.mp4", "2023-08-15T12:34:56");
		string right = Make("b.mp4", "2023-08-15T14:34:56+0200");
		string wrong2 = Make("c.mp4", "2023-08-15T12:34:56");

		var files = new[] { wrong1, right, wrong2 }.Select(FileMetadata.Read).ToList();
		Assert.All(files, Assert.NotNull);
		Assert.Contains(files[0]!, f => f.Section.Kind == MetadataSectionKind.Container && f.Name == "creation_time");
		Assert.Contains(files[0]!, f => f.Section.Kind == MetadataSectionKind.Stream && f.Section.StreamType == "audio" && f.Name == "language" && f.Value == "ger");

		var rows = MetadataComparison.Build(files);
		var date = rows.Single(r => r.Name == "com.apple.quicktime.creationdate");
		Assert.Equal("2023-08-15T14:34:56+0200", date.Values[1]);
		Assert.Equal(new[] { false, true, false }, date.IsOdd);
		Assert.False(rows.Single(r => r.Section.Kind == MetadataSectionKind.Container && r.Name == "creation_time").Differs);
	}
}
