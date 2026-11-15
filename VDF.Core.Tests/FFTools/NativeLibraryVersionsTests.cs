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
// The move from FFmpeg 8 to 9: a user who let an older VDF download FFmpeg 8 has avcodec-62 & co.
// next to VDF, while the binding now loads avcodec-63. VDF must say so instead of only "libraries
// not found", and the FFmpeg 9 download must not leave the FFmpeg 8 libraries behind.

using VDF.Core.FFTools;
using VDF.Core.FFTools.FFmpegNative;

namespace VDF.Core.Tests.FFTools;

public class NativeLibraryVersionsTests : IDisposable {

	readonly string root = Path.Combine(Path.GetTempPath(), "vdf-libversions-" + Guid.NewGuid().ToString("N"));

	public NativeLibraryVersionsTests() => Directory.CreateDirectory(root);

	public void Dispose() {
		try { Directory.Delete(root, recursive: true); }
		catch (IOException) { }
	}

	/// <summary>A library file name the way this OS spells it.</summary>
	static string Name(string lib, int major) =>
		OperatingSystem.IsWindows() ? $"{lib}-{major}.dll"
		: OperatingSystem.IsMacOS() ? $"lib{lib}.{major}.dylib"
		: $"lib{lib}.so.{major}";

	// FFmpeg 8's libraries, the ones an older VDF downloaded.
	static readonly (string Lib, int Major)[] Ffmpeg8 = {
		("avcodec", 62), ("avdevice", 62), ("avfilter", 11), ("avformat", 62), ("avutil", 60), ("swresample", 6), ("swscale", 9),
	};

	string Folder(string name, params string[] files) {
		string dir = Path.Combine(root, name);
		Directory.CreateDirectory(dir);
		foreach (string f in files)
			File.WriteAllBytes(Path.Combine(dir, f), new byte[] { 1 });
		return dir;
	}

	[Theory]
	[InlineData("avcodec-62.dll", "avcodec", 62)]
	[InlineData("libavcodec.so.63", "avcodec", 63)]
	[InlineData("libavcodec.62.dylib", "avcodec", 62)]
	[InlineData("swscale-10.dll", "swscale", 10)]
	[InlineData("AVCODEC-63.DLL", "avcodec", 63)]
	[InlineData("postproc-58.dll", "postproc", 58)]
	public void TryParseLibraryFileName_ReadsEveryPlatformsSpelling(string file, string lib, int major) {
		Assert.True(NativeLibraryVersions.TryParseLibraryFileName(file, out string parsedLib, out int parsedMajor));
		Assert.Equal(lib, parsedLib);
		Assert.Equal(major, parsedMajor);
	}

	[Theory]
	[InlineData("libavcodec.so")] // unversioned link, not what the binding loads
	[InlineData("libavcodec.so.62.1.100")] // full version
	[InlineData("libavcodec.dylib")]
	[InlineData("avcodec.dll")]
	[InlineData("libavcodec-62.dll")]
	[InlineData("avcodec-62.dll.bak")]
	[InlineData("avcodec-62.txt")]
	[InlineData("VDF.Core.dll")]
	[InlineData("ffmpeg.exe")]
	public void TryParseLibraryFileName_IgnoresEverythingElse(string file) =>
		Assert.False(NativeLibraryVersions.TryParseLibraryFileName(file, out _, out _));

	[Fact]
	public void FindMismatch_ReportsTheOlderFfmpegAndWhereItIs() {
		string empty = Folder("empty");
		string ff8 = Folder("ff8", Ffmpeg8.Select(l => Name(l.Lib, l.Major)).ToArray());

		var mismatch = NativeLibraryVersions.FindMismatch(new[] { empty, ff8 }, neededAvcodecMajor: 63);

		Assert.NotNull(mismatch);
		Assert.Equal(ff8, mismatch!.Folder);
		Assert.Equal("FFmpeg 8", mismatch.Found);
		Assert.Equal("FFmpeg 9", mismatch.Needed);
		string sentence = FFmpegHelper.DescribeMismatch(mismatch);
		Assert.Contains(ff8, sentence);
		Assert.Contains("FFmpeg 8", sentence);
		Assert.Contains("FFmpeg 9", sentence);
	}

	[Fact]
	public void FindMismatch_TheRightVersionIsNoMismatch() {
		string ff9 = Folder("ff9", Name("avcodec", 63), Name("avutil", 61));
		Assert.Null(NativeLibraryVersions.FindMismatch(new[] { ff9 }, neededAvcodecMajor: 63));
	}

	[Fact]
	public void FindMismatch_FirstFolderWins_AndWithinItTheNewestVersion() {
		string mine = Folder("mine", Name("avcodec", 61), Name("avcodec", 62));
		string system = Folder("system", Name("avcodec", 60));

		var mismatch = NativeLibraryVersions.FindMismatch(new[] { Path.Combine(root, "missing"), mine, system }, neededAvcodecMajor: 63);

		Assert.Equal(mine, mismatch!.Folder);
		Assert.Equal(62, mismatch.FoundAvcodecMajor);
	}

	[Fact]
	public void Mismatch_OfAnUnknownVersion_NamesTheLibraryVersion() =>
		Assert.Equal("libavcodec 64", new NativeLibraryMismatch(64, 63, "x").Found);

	[Fact]
	public void RemoveOtherMajors_DeletesOnlyOtherVersionsOfTheLibrariesTheBindingLoads() {
		string dir = Folder("mixed",
			Name("avcodec", 62), Name("avcodec", 63), Name("avutil", 60), Name("avutil", 61),
			Name("postproc", 58), "ffmpeg.exe", "VDF.Core.dll", "avcodec-62.dll.txt");

		var removed = NativeLibraryVersions.RemoveOtherMajors(dir, new Dictionary<string, int> { ["avcodec"] = 63, ["avutil"] = 61 });

		Assert.Equal(new[] { Name("avcodec", 62), Name("avutil", 60) }.Order(), removed.Order());
		Assert.Equal(
			new[] { Name("avcodec", 63), Name("avutil", 61), Name("postproc", 58), "ffmpeg.exe", "VDF.Core.dll", "avcodec-62.dll.txt" }.Order(),
			Directory.EnumerateFiles(dir).Select(Path.GetFileName).Order());
	}

	[Fact]
	public void AfterTheDownload_OnlyTheBindingsFfmpegIsLeft() {
		// What a successful FFmpeg 9 install leaves in VDF's folder that held FFmpeg 8 before.
		string[] ffmpeg9 = FFmpegHelper.GenerateLibraryFileNames();
		string dir = Folder("bin", Ffmpeg8.Select(l => Name(l.Lib, l.Major)).Concat(ffmpeg9).Append("ffmpeg.exe").ToArray());

		FfmpegDownloader.RemoveOutdatedLibraries(new[] { dir, dir });

		Assert.Equal(ffmpeg9.Append("ffmpeg.exe").Order(), Directory.EnumerateFiles(dir).Select(Path.GetFileName).Order());
	}
}
