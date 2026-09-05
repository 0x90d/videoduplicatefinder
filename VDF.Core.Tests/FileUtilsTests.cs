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

	// Attribute rule for subfolders (#876): only Hidden AND System together marks a folder the
	// OS owns ($RECYCLE.BIN, System Volume Information). System alone is user content: Explorer
	// honours a folder's desktop.ini icon only with the read-only or system attribute, so whole
	// libraries get marked +S. Hidden alone has been scanned since 2021.

	[Fact]
	public void GetFilesRecursive_SystemOnlySubfolder_IsScanned() {
		if (!OperatingSystem.IsWindows()) return; // Windows attribute semantics
		string dir = NewTempTree();
		try {
			string showFolder = Path.Combine(dir, "show");
			Directory.CreateDirectory(showFolder);
			File.WriteAllBytes(Path.Combine(showFolder, "inside.mp4"), new byte[] { 1 });
			File.SetAttributes(showFolder, FileAttributes.Directory | FileAttributes.System);

			Assert.Equal("inside.mp4", Assert.Single(Scan(dir)).Name);
		}
		finally {
			DeleteTempTree(dir);
		}
	}

	[Fact]
	public void GetFilesRecursive_HiddenOnlySubfolder_IsScanned() {
		if (!OperatingSystem.IsWindows()) return;
		string dir = NewTempTree();
		try {
			string hiddenFolder = Path.Combine(dir, "hidden");
			Directory.CreateDirectory(hiddenFolder);
			File.WriteAllBytes(Path.Combine(hiddenFolder, "inside.mp4"), new byte[] { 1 });
			File.SetAttributes(hiddenFolder, FileAttributes.Directory | FileAttributes.Hidden);

			Assert.Single(Scan(dir));
		}
		finally {
			DeleteTempTree(dir);
		}
	}

	[Fact]
	public void GetFilesRecursive_HiddenSystemSubfolder_IsSkippedAndLogged_ButScannedWhenIncludedDirectly() {
		if (!OperatingSystem.IsWindows()) return;
		string dir = NewTempTree();
		var messages = new List<string>();
		void Handler(LogEntry entry) {
			lock (messages)
				if (entry.Message.Contains(dir)) messages.Add(entry.Message);
		}
		Logger.Instance.LogEntryAdded += Handler;
		try {
			string osFolder = Path.Combine(dir, "System Volume Information");
			Directory.CreateDirectory(osFolder);
			File.WriteAllBytes(Path.Combine(osFolder, "inside.mp4"), new byte[] { 1 });
			File.SetAttributes(osFolder, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);

			Assert.Empty(Scan(dir));
			string skipped;
			lock (messages)
				skipped = Assert.Single(messages);
			Assert.Contains("Skipped 1 subfolder(s)", skipped);
			Assert.Contains("1 hidden system folder(s)", skipped);
			// The starting folder's own attributes are never checked: explicitly included
			// folders are always scanned (adding a folder individually always works).
			Assert.Single(Scan(osFolder));
		}
		finally {
			Logger.Instance.LogEntryAdded -= Handler;
			DeleteTempTree(dir);
		}
	}

	[Fact]
	public void GetFilesRecursive_SystemAttributedFile_IsFound() {
		if (!OperatingSystem.IsWindows()) return;
		string dir = NewTempTree();
		string file = Path.Combine(dir, "marked.mp4");
		try {
			File.WriteAllBytes(file, new byte[] { 1 });
			File.SetAttributes(file, FileAttributes.System);

			Assert.Equal("marked.mp4", Assert.Single(Scan(dir)).Name);
		}
		finally {
			if (File.Exists(file))
				File.SetAttributes(file, FileAttributes.Normal);
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
