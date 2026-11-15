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

using System.CommandLine;
using VDF.CLI.Commands;
using VDF.Core.FFTools.FFmpegNative;

namespace VDF.CLI.Tests.Commands;

// --native-ffmpeg next to the FFmpeg 8 an older VDF used: the scan must not start and then fail
// with "cannot find FFmpeg libraries", it has to say which version is there, which is needed,
// and how to get on. Swaps the static finder, so the class runs on its own.
[Collection(nameof(NativeFfmpegCheckTests))]
public class NativeFfmpegCheckTests {

	static readonly NativeLibraryMismatch Ffmpeg8 = new(62, 63, Path.Combine(Path.GetTempPath(), "old-vdf", "bin"));

	static async Task<(int Exit, string Error)> RunWithMismatch(NativeLibraryMismatch? mismatch, string commandLine) {
		var findBefore = ScanRunner.FindNativeMismatch;
		var errorBefore = Console.Error;
		var error = new StringWriter();
		ScanRunner.FindNativeMismatch = () => mismatch;
		Console.SetError(error);
		try {
			var root = new RootCommand("vdf-cli") { ScanCommand.Build(), ScanAndCompareCommand.Build() };
			int exit = await root.Parse(commandLine).InvokeAsync();
			return (exit, error.ToString());
		}
		finally {
			Console.SetError(errorBefore);
			ScanRunner.FindNativeMismatch = findBefore;
		}
	}

	[Theory]
	[InlineData("scan")]
	[InlineData("scan-and-compare")]
	public async Task NativeBindingWithAnotherFfmpeg_StopsBeforeTheScanAndSaysWhy(string command) {
		string include = Path.GetTempPath();

		var (exit, error) = await RunWithMismatch(Ffmpeg8, $"{command} --include \"{include}\" --native-ffmpeg");

		Assert.Equal(1, exit);
		Assert.Contains(Ffmpeg8.Folder, error);
		Assert.Contains("FFmpeg 8", error);
		Assert.Contains("FFmpeg 9", error);
		Assert.Contains("--native-ffmpeg", error); // the way out without downloading anything
	}

	[Fact]
	public void WithoutTheNativeBinding_AnotherFfmpegIsNoProblem() {
		var findBefore = ScanRunner.FindNativeMismatch;
		ScanRunner.FindNativeMismatch = () => Ffmpeg8;
		try {
			Assert.Null(ScanRunner.NativeFfmpegProblem(new VDF.Core.Settings { UseNativeFfmpegBinding = false }));
			Assert.NotNull(ScanRunner.NativeFfmpegProblem(new VDF.Core.Settings { UseNativeFfmpegBinding = true }));
		}
		finally { ScanRunner.FindNativeMismatch = findBefore; }
	}

	[Fact]
	public void NothingOfAnotherVersion_IsNoProblem() {
		var findBefore = ScanRunner.FindNativeMismatch;
		ScanRunner.FindNativeMismatch = () => null;
		try {
			Assert.Null(ScanRunner.NativeFfmpegProblem(new VDF.Core.Settings { UseNativeFfmpegBinding = true }));
		}
		finally { ScanRunner.FindNativeMismatch = findBefore; }
	}
}
