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
// HardLinkUtils on its own. The POSIX side declares link(2), statx(2) (Linux) and the
// 64-bit-inode stat (macOS) by hand instead of going through Mono.Posix, so the struct offsets
// are pinned against what the stat command itself prints. Tests marked POSIX return early on
// Windows, where CI runs; run them on Linux or macOS when touching that code.

using System.Diagnostics;
using System.Globalization;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

public class HardLinkUtilsTests : IDisposable {

	readonly string dir = Path.Combine(Path.GetTempPath(), "vdf-hardlinkutils-" + Guid.NewGuid().ToString("N"));

	public HardLinkUtilsTests() => Directory.CreateDirectory(dir);

	public void Dispose() {
		try { Directory.Delete(dir, recursive: true); }
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	string NewFile(string name) {
		string path = Path.Combine(dir, name);
		File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
		return path;
	}

	[Fact]
	public void AreSameFile_ForAHardLinkAndThePathItself_NotForACopy() {
		string original = NewFile("original.bin");
		string copy = NewFile("copy.bin");
		string link = Path.Combine(dir, "link.bin");
		HardLinkUtils.CreateHardLink(link, original);

		Assert.True(HardLinkUtils.AreSameFile(original, link));
		Assert.True(HardLinkUtils.AreSameFile(link, original));
		Assert.True(HardLinkUtils.AreSameFile(original, original));
		Assert.False(HardLinkUtils.AreSameFile(original, copy));
	}

	[Fact]
	public void AreSameFile_IsFalseWhenEitherPathIsMissing() {
		string file = NewFile("file.bin");
		string missing = Path.Combine(dir, "missing.bin");

		Assert.False(HardLinkUtils.AreSameFile(file, missing));
		Assert.False(HardLinkUtils.AreSameFile(missing, file));
	}

	[Fact]
	public void CreateHardLink_OntoAnExistingFile_ThrowsNamingBothPaths_AndLeavesItAlone() {
		string original = NewFile("original.bin");
		string taken = NewFile("taken.bin");

		var ex = Assert.Throws<IOException>(() => HardLinkUtils.CreateHardLink(taken, original));

		Assert.Contains(taken, ex.Message);
		Assert.Contains(original, ex.Message);
		if (!OperatingSystem.IsWindows())
			Assert.Contains("errno 17", ex.Message); // EEXIST, the same number on Linux and macOS
		Assert.False(HardLinkUtils.AreSameFile(original, taken));
	}

	[Fact]
	public void CreateHardLink_FromAMissingFile_Throws() {
		string link = Path.Combine(dir, "link.bin");

		Assert.Throws<IOException>(() => HardLinkUtils.CreateHardLink(link, Path.Combine(dir, "missing.bin")));
		Assert.False(File.Exists(link));
	}

	[Fact]
	public void PosixFileId_IsTheDeviceAndInodeTheStatCommandPrints() {
		if (OperatingSystem.IsWindows()) return; // POSIX
		string a = NewFile("a.bin");
		string b = NewFile("b.bin");

		foreach (string path in new[] { a, b, dir }) {
			Assert.True(HardLinkUtils.TryGetFileIdPosix(path, out var id), path);
			Assert.Equal(StatCommand(path), id);
		}
	}

	[Fact]
	public void PosixFileId_IsSharedByAHardLink() {
		if (OperatingSystem.IsWindows()) return; // POSIX
		string original = NewFile("original.bin");
		string link = Path.Combine(dir, "link.bin");
		HardLinkUtils.CreateHardLink(link, original);

		Assert.True(HardLinkUtils.TryGetFileIdPosix(original, out var originalId));
		Assert.True(HardLinkUtils.TryGetFileIdPosix(link, out var linkId));
		Assert.Equal(originalId, linkId);
		Assert.Equal(StatCommand(link), linkId);
	}

	[Fact]
	public void PosixFileId_MissingFile_IsFalse() {
		if (OperatingSystem.IsWindows()) return; // POSIX
		Assert.False(HardLinkUtils.TryGetFileIdPosix(Path.Combine(dir, "missing.bin"), out _));
	}

	/// <summary>st_dev and st_ino as stat(1) prints them, following symlinks like the code does.</summary>
	static (ulong Device, ulong Inode) StatCommand(string path) {
		var psi = new ProcessStartInfo("stat") { RedirectStandardOutput = true, UseShellExecute = false };
		// BSD stat (macOS) and GNU coreutils (Linux) spell the format option differently.
		foreach (string arg in OperatingSystem.IsMacOS() ? new[] { "-L", "-f", "%d %i", path } : new[] { "-L", "-c", "%d %i", path })
			psi.ArgumentList.Add(arg);
		using var process = Process.Start(psi)!;
		string output = process.StandardOutput.ReadToEnd().Trim();
		process.WaitForExit();
		Assert.True(process.ExitCode == 0, $"stat {path} failed: {output}");
		string[] parts = output.Split(' ');
		return (ulong.Parse(parts[0], CultureInfo.InvariantCulture), ulong.Parse(parts[1], CultureInfo.InvariantCulture));
	}
}
