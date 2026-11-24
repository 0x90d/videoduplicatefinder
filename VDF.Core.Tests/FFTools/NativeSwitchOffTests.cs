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

using VDF.Core.FFTools;

namespace VDF.Core.Tests.FFTools;

[CollectionDefinition(nameof(NativeSwitchOffTests), DisableParallelization = true)]
public class NativeSwitchOffCollection { }

/// <summary>
/// Native FFmpeg switches itself off for the rest of a scan only on evidence that the binding
/// itself is broken: failures that could be the binding's, from files not already known to
/// fail, before native has decoded anything.
/// </summary>
[Collection(nameof(NativeSwitchOffTests))] // the switch-off state is static
public class NativeSwitchOffTests : IDisposable {
	readonly bool _useNativeBinding = FfmpegEngine.UseNativeBinding;

	public NativeSwitchOffTests() => FfmpegEngine.UseNativeBinding = true; // resets the scan's state

	public void Dispose() => FfmpegEngine.UseNativeBinding = _useNativeBinding;

	static void Fail(int times, FfmpegErrorCategory category, bool fileFailedBefore = false) {
		for (int i = 0; i < times; i++)
			FfmpegEngine.RecordNativeFailure($"file{i}.mp4", new Exception("decode failed"), category, fileFailedBefore);
	}

	// Categories by name: FfmpegErrorCategory is internal, which public test parameters cannot use.
	[Theory]
	[InlineData("Unknown")]
	[InlineData("LibraryLoad")]
	[InlineData("HardwareAcceleration")]
	public void FailuresThatCanBeTheBindings_SwitchItOff(string categoryName) {
		var category = Enum.Parse<FfmpegErrorCategory>(categoryName);
		Fail(4, category);
		Assert.False(FfmpegEngine.NativeDisabledForSession);
		Fail(1, category);
		Assert.True(FfmpegEngine.NativeDisabledForSession);
	}

	[Theory]
	[InlineData("CorruptOrTruncated")]
	[InlineData("FileAccess")]
	[InlineData("UnsupportedCodec")]
	public void FailuresOfTheFile_DoNotCount(string categoryName) {
		var category = Enum.Parse<FfmpegErrorCategory>(categoryName);
		Fail(50, category);
		Assert.False(FfmpegEngine.NativeDisabledForSession);
	}

	[Fact]
	public void FilesThatFailedBefore_DoNotCount() {
		Fail(50, FfmpegErrorCategory.Unknown, fileFailedBefore: true);
		Assert.False(FfmpegEngine.NativeDisabledForSession);
	}

	[Fact]
	public void AfterTheFirstSuccess_NothingCounts() {
		Assert.False(FfmpegEngine.CountsTowardsSwitchOff(FfmpegErrorCategory.Unknown, fileFailedBefore: false, nativeHasSucceeded: true));
		Assert.True(FfmpegEngine.CountsTowardsSwitchOff(FfmpegErrorCategory.Unknown, fileFailedBefore: false, nativeHasSucceeded: false));
	}

	[Fact]
	public void ANewScanStartsAgain() {
		Fail(5, FfmpegErrorCategory.Unknown);
		Assert.True(FfmpegEngine.NativeDisabledForSession);
		FfmpegEngine.UseNativeBinding = true;
		Assert.False(FfmpegEngine.NativeDisabledForSession);
	}
}
