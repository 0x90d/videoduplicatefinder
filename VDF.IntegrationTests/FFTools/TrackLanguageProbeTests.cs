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
using VDF.Core.ViewModels;
using VDF.IntegrationTests.Fixtures;

namespace VDF.IntegrationTests.FFTools;

/// <summary>
/// #899: the scan's own ffprobe call carries every track's language to the result row,
/// for Matroska (bibliographic codes) and mp4 (terminology codes, "und" for untagged).
/// </summary>
[Collection("Ffmpeg")]
public sealed class TrackLanguageProbeTests : IDisposable {
	readonly FfmpegFixture _fixture;
	readonly string dir = Path.Combine(Path.GetTempPath(), $"vdf_languages_{Guid.NewGuid():N}");

	public TrackLanguageProbeTests(FfmpegFixture fixture) {
		_fixture = fixture;
		Directory.CreateDirectory(dir);
	}

	public void Dispose() {
		try { Directory.Delete(dir, true); } catch { }
	}

	static void RunFfmpeg(params string[] args) {
		var psi = new ProcessStartInfo(FfmpegEngine.FFmpegPath) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
		foreach (string a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args))
			psi.ArgumentList.Add(a);
		using var p = Process.Start(psi)!;
		string err = p.StandardError.ReadToEnd();
		p.WaitForExit();
		Assert.True(p.ExitCode == 0, $"ffmpeg failed: {err}");
	}

	/// <summary>Video, two audio tracks and two subtitle tracks, the languages given per track (null = no tag).</summary>
	string MakeFile(string name, string subtitleCodec, string? audio1, string? audio2, string? sub1, string? sub2) {
		string srt = Path.Combine(dir, "subs.srt");
		File.WriteAllText(srt, "1\n00:00:00,000 --> 00:00:01,000\nHallo\n\n");
		string output = Path.Combine(dir, name);
		var args = new List<string> {
			"-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=2",
			"-f", "lavfi", "-i", "sine=frequency=440:duration=2",
			"-f", "lavfi", "-i", "sine=frequency=880:duration=2",
			"-i", srt, "-i", srt,
			"-map", "0:v", "-map", "1:a", "-map", "2:a", "-map", "3:s", "-map", "4:s",
			"-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-c:s", subtitleCodec,
		};
		void Tag(string stream, string? language) {
			if (language != null) { args.Add($"-metadata:s:{stream}"); args.Add($"language={language}"); }
		}
		Tag("a:0", audio1); Tag("a:1", audio2); Tag("s:0", sub1); Tag("s:1", sub2);
		args.Add(output);
		RunFfmpeg(args.ToArray());
		return output;
	}

	static DuplicateItem RowFor(string file) {
		var info = FFProbeEngine.GetMediaInfo(file, extendedLogging: false);
		Assert.NotNull(info);
		return new DuplicateItem(new FileEntry { _Path = file, mediaInfo = info }, 0f, Guid.NewGuid(), DuplicateFlags.None);
	}

	[SkippableFact]
	public void Matroska_TracksKeepTheirLanguages() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!ScanEngine.FFprobeExists, "ffprobe not found");

		var row = RowFor(MakeFile("tracks.mkv", "srt", "ger", "eng", "ger", null));

		Assert.Equal("GER, ENG", row.AudioLanguages);
		Assert.Equal("GER, ?", row.SubtitleLanguages);
	}

	[SkippableFact]
	public void Mp4_TerminologyCodesAndUnd_ReadLikeTheMatroskaCopy() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!ScanEngine.FFprobeExists, "ffprobe not found");

		var row = RowFor(MakeFile("tracks.mp4", "mov_text", "deu", "und", "fra", "eng"));

		Assert.Equal("GER, ?", row.AudioLanguages);
		Assert.Equal("FRE, ENG", row.SubtitleLanguages);
	}

	[SkippableFact]
	public void UntaggedFile_ShowsNoAudioLanguages_ButItsSubtitleTracks() {
		Skip.If(!_fixture.FfmpegCliAvailable, _fixture.FfmpegNotFoundReason);
		Skip.If(!ScanEngine.FFprobeExists, "ffprobe not found");

		var row = RowFor(MakeFile("untagged.mkv", "srt", null, null, null, null));

		Assert.Equal(string.Empty, row.AudioLanguages);
		Assert.Equal("?, ?", row.SubtitleLanguages);
	}
}
