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

using VDF.Core.Utils;

namespace VDF.Core.Tests;

public class FileUtilsTests {
	[Fact]
	public void IsPathFFmpegSafe_Empty_True() =>
		Assert.True(FileUtils.IsPathFFmpegSafe(string.Empty));

	[Fact]
	public void IsPathFFmpegSafe_AsciiPath_True() =>
		Assert.True(FileUtils.IsPathFFmpegSafe(@"C:\videos\clip.mp4"));

	[Fact]
	public void IsPathFFmpegSafe_BmpNonAscii_True() {
		// U+2019 RIGHT SINGLE QUOTATION MARK — the curly apostrophe in "Y'all"
		string path = "C:\\videos\\Y’all.mp4";
		Assert.True(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_ValidSurrogatePair_True() {
		// U+1F49A GREEN HEART, encoded in UTF-16 as the surrogate pair D83D DC9A
		string path = "C:\\videos\\💚.mp4";
		Assert.True(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_TwoAdjacentValidPairs_True() {
		// U+1F49A GREEN HEART followed by U+1FAF6 HEART HANDS
		string path = "C:\\videos\\💚🫶.mp4";
		Assert.True(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_LoneHighSurrogate_False() {
		// U+D83D high surrogate without a following low surrogate
		string path = "C:\\videos\\bad\uD83D.mp4";
		Assert.False(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_LoneLowSurrogate_False() {
		// U+DC9A low surrogate without a preceding high surrogate
		string path = "C:\\videos\\bad\uDC9A.mp4";
		Assert.False(FileUtils.IsPathFFmpegSafe(path));
	}

	[Fact]
	public void IsPathFFmpegSafe_HighSurrogateFollowedByNonLow_False() {
		// High surrogate followed by an ASCII letter (not a low surrogate)
		string path = "C:\\videos\\bad\uD83DA.mp4";
		Assert.False(FileUtils.IsPathFFmpegSafe(path));
	}

	[Theory]
	[InlineData("C:\\pics\\photo.jpg")]
	[InlineData("C:\\pics\\photo.JPEG")]
	[InlineData("/home/user/pics/photo.png")]
	[InlineData("photo.webp")]
	[InlineData("scan.heic")]
	public void IsImageFile_ImageExtensions_True(string path) =>
		Assert.True(FileUtils.IsImageFile(path));

	[Theory]
	[InlineData("C:\\videos\\clip.mp4")]
	[InlineData("clip.mkv")]
	[InlineData("noextension")]
	[InlineData("archive.zip")]
	public void IsImageFile_NonImage_False(string path) =>
		Assert.False(FileUtils.IsImageFile(path));

	// AVCHD camcorder footage: same MPEG-TS container as .m2ts, camcorders name it
	// .MTS (uppercase on the card). Requested in discussion #766.
	[Theory]
	[InlineData(".mts")]
	[InlineData(".MTS")]
	[InlineData(".m2ts")]
	[InlineData(".ts")]
	public void IsVideoExtension_TransportStreamVariants_True(string extension) =>
		Assert.True(FileUtils.IsVideoExtension(extension));

	[Fact]
	public void GetFilesRecursive_FindsMtsFiles() {
		string dir = Path.Combine(Path.GetTempPath(), $"VDF.Test.{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		try {
			File.WriteAllBytes(Path.Combine(dir, "camcorder.MTS"), new byte[] { 0x47 });
			File.WriteAllBytes(Path.Combine(dir, "notes.txt"), new byte[] { 0x00 });
			var files = FileUtils.GetFilesRecursive(dir, ignoreReadonly: false, ignoreReparsePoints: false,
				recursive: true, includeImages: false, new List<string>(), CancellationToken.None);
			Assert.Equal("camcorder.MTS", Assert.Single(files).Name);
		}
		finally {
			Directory.Delete(dir, recursive: true);
		}
	}

	// ---- Subfolder attribute policy (#876): skips must stay policy-equivalent to the ----
	// ---- old AttributesToSkip behavior now that they are counted and logged instead. ----

	static string NewTempTree() {
		string dir = Path.Combine(Path.GetTempPath(), $"VDF.Test.{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		return dir;
	}

	static void DeleteTempTree(string dir) {
		// Clear ReadOnly/System attributes so cleanup never fails.
		foreach (string sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
			File.SetAttributes(sub, FileAttributes.Directory);
		Directory.Delete(dir, recursive: true);
	}

	static List<FileInfo> Scan(string dir, bool ignoreReadonly = false, bool ignoreReparsePoints = false) =>
		FileUtils.GetFilesRecursive(dir, ignoreReadonly, ignoreReparsePoints,
			recursive: true, includeImages: false, new List<string>(), CancellationToken.None);

	[Fact]
	public void GetFilesRecursive_RecursesIntoNestedSubfolders() {
		string dir = NewTempTree();
		try {
			Directory.CreateDirectory(Path.Combine(dir, "a", "b"));
			File.WriteAllBytes(Path.Combine(dir, "root.mp4"), new byte[] { 1 });
			File.WriteAllBytes(Path.Combine(dir, "a", "mid.mp4"), new byte[] { 1 });
			File.WriteAllBytes(Path.Combine(dir, "a", "b", "deep.mp4"), new byte[] { 1 });
			Assert.Equal(3, Scan(dir).Count);
		}
		finally {
			DeleteTempTree(dir);
		}
	}

	[Fact]
	public void GetFilesRecursive_SystemSubfolderIsSkipped_ButScannedWhenIncludedDirectly() {
		if (!OperatingSystem.IsWindows()) return; // Windows attribute semantics
		string dir = NewTempTree();
		try {
			string sysFolder = Path.Combine(dir, "sys");
			Directory.CreateDirectory(sysFolder);
			File.WriteAllBytes(Path.Combine(sysFolder, "inside.mp4"), new byte[] { 1 });
			File.SetAttributes(sysFolder, FileAttributes.Directory | FileAttributes.System);

			Assert.Empty(Scan(dir));
			// The starting folder's own attributes are deliberately never checked —
			// explicitly included folders are always scanned (matches issue #876 reports:
			// adding the folders individually works).
			Assert.Single(Scan(sysFolder));
		}
		finally {
			DeleteTempTree(dir);
		}
	}

	[Fact]
	public void GetFilesRecursive_ReadOnlySubfolder_SkippedOnlyWhenIgnoreReadonly() {
		if (!OperatingSystem.IsWindows()) return; // ReadOnly on directories is a Windows concept
		string dir = NewTempTree();
		try {
			string roFolder = Path.Combine(dir, "ro");
			Directory.CreateDirectory(roFolder);
			File.WriteAllBytes(Path.Combine(roFolder, "inside.mp4"), new byte[] { 1 });
			File.SetAttributes(roFolder, FileAttributes.Directory | FileAttributes.ReadOnly);

			Assert.Single(Scan(dir, ignoreReadonly: false));
			Assert.Empty(Scan(dir, ignoreReadonly: true));
		}
		finally {
			DeleteTempTree(dir);
		}
	}

	[Fact]
	public void GetFilesRecursive_JunctionSubfolder_SkippedOnlyWhenIgnoreReparsePoints() {
		if (!OperatingSystem.IsWindows()) return; // junction creation
		string dir = NewTempTree();
		string target = NewTempTree();
		try {
			File.WriteAllBytes(Path.Combine(target, "inside.mp4"), new byte[] { 1 });
			string junction = Path.Combine(dir, "link");
			// Junctions need no admin rights, unlike symlinks.
			var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"") {
				CreateNoWindow = true,
				UseShellExecute = false,
			};
			using (var proc = System.Diagnostics.Process.Start(psi)!)
				proc.WaitForExit();
			if (!Directory.Exists(junction)) return; // couldn't create a junction here — nothing to assert

			Assert.Single(Scan(dir, ignoreReparsePoints: false));
			Assert.Empty(Scan(dir, ignoreReparsePoints: true));
		}
		finally {
			// Remove the junction itself first — recursive delete refuses to traverse it.
			string junction = Path.Combine(dir, "link");
			if (Directory.Exists(junction))
				Directory.Delete(junction, recursive: false);
			Directory.Delete(dir, recursive: true);
			DeleteTempTree(target);
		}
	}
}
