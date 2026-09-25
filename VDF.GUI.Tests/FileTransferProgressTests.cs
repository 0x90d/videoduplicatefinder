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
// #879: moving files showed an animated bar and nothing else. File.Move across volumes
// and File.Copy block until the whole file is done, so there was nothing to report.

using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;
using GuiFileUtils = VDF.GUI.Utils.FileUtils;

namespace VDF.GUI.Tests {
	public sealed class FileTransferProgressTests : IDisposable {
		readonly string root = Path.Combine(Path.GetTempPath(), "vdf-transfer-" + Guid.NewGuid().ToString("N"));
		readonly string sourceDir, targetDir;

		public FileTransferProgressTests() {
			sourceDir = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
			targetDir = Path.Combine(root, "dst");
		}

		public void Dispose() {
			try { Directory.Delete(root, true); } catch { }
		}

		string MakeFile(string name, int bytes) {
			string path = Path.Combine(sourceDir, name);
			var data = new byte[bytes];
			new Random(bytes).NextBytes(data);
			File.WriteAllBytes(path, data);
			File.SetLastWriteTimeUtc(path, new DateTime(2019, 3, 4, 5, 6, 7, DateTimeKind.Utc));
			return path;
		}

		static DuplicateItemVM Item(string path) => new() {
			ItemInfo = new DuplicateItem { Path = path, SizeLong = new FileInfo(path).Length, GroupId = Guid.Empty }
		};

		[Fact]
		public void CrossVolumeMove_ReportsBytesAsTheyAreCopied_AndKeepsContentAndTimestamps() {
			string source = MakeFile("big.mp4", 5 * 1024 * 1024 + 123);
			byte[] original = File.ReadAllBytes(source);
			Directory.CreateDirectory(targetDir);
			string target = Path.Combine(targetDir, "big.mp4");
			var reports = new List<long>();

			GuiFileUtils.TransferFile(source, target, move: true, overwrite: false, reports.Add, isSameVolume: (_, _) => false);

			Assert.False(File.Exists(source));
			Assert.Equal(original, File.ReadAllBytes(target));
			Assert.Equal(new DateTime(2019, 3, 4, 5, 6, 7, DateTimeKind.Utc), File.GetLastWriteTimeUtc(target));
			// Several reports during the one file, rising to its full length.
			Assert.True(reports.Count >= 5, $"only {reports.Count} progress report(s)");
			Assert.Equal(reports.OrderBy(r => r), reports);
			Assert.Equal(original.Length, reports[^1]);
		}

		[Fact]
		public void FailedCopy_RemovesThePartialTarget_AndKeepsTheSource() {
			string source = MakeFile("clip.mp4", 3 * 1024 * 1024);
			Directory.CreateDirectory(targetDir);
			string target = Path.Combine(targetDir, "clip.mp4");

			Assert.Throws<IOException>(() => GuiFileUtils.TransferFile(source, target, move: true, overwrite: false,
				_ => throw new IOException("disk full"), isSameVolume: (_, _) => false));

			Assert.True(File.Exists(source));
			Assert.False(File.Exists(target));
		}

		[Fact]
		public void SameVolumeMove_StaysARename() {
			string source = MakeFile("same.mp4", 1000);
			Directory.CreateDirectory(targetDir);
			string target = Path.Combine(targetDir, "same.mp4");
			int reports = 0;

			GuiFileUtils.TransferFile(source, target, move: true, overwrite: false, _ => reports++, isSameVolume: (_, _) => true);

			Assert.False(File.Exists(source));
			Assert.True(File.Exists(target));
			Assert.Equal(0, reports);
		}

		[Fact]
		public void ReadOnlySource_IsMovedAndStaysReadOnly() {
			string source = MakeFile("ro.mp4", 1000);
			File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);
			Directory.CreateDirectory(targetDir);
			string target = Path.Combine(targetDir, "ro.mp4");

			GuiFileUtils.TransferFile(source, target, move: true, overwrite: false, null, isSameVolume: (_, _) => false);

			Assert.False(File.Exists(source));
			Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly));
			File.SetAttributes(target, FileAttributes.Normal);
		}

		[Fact]
		public void CopyFile_ReportsTheByteFractionAcrossFiles_EndingAtOne() {
			var items = new List<DuplicateItemVM> {
				Item(MakeFile("a.mp4", 3 * 1024 * 1024)),
				Item(MakeFile("b.mp4", 1024)),
			};
			var renames = new List<(DuplicateItemVM, string)>();
			var reports = new List<(int done, int total, double fraction)>();

			int errors = GuiFileUtils.CopyFile(items, targetDir, true, false, renames, (d, t, f) => reports.Add((d, t, f)));

			Assert.Equal(0, errors);
			Assert.Equal(2, renames.Count);
			Assert.All(reports, r => Assert.Equal(2, r.total));
			Assert.Equal(reports.Select(r => r.fraction).OrderBy(f => f), reports.Select(r => r.fraction));
			Assert.Equal((2, 2, 1.0), reports[^1]);
			// The first file is nearly all of the bytes: finishing it is nearly all of the bar.
			var afterFirst = reports.First(r => r.done == 1);
			Assert.True(afterFirst.fraction > 0.99);
		}

		[Theory]
		[InlineData("/mnt/usb/videos/a.mp4", "/mnt/usb")]
		[InlineData("/home/me/a.mp4", "/")]
		[InlineData("/mnt/usbstick/a.mp4", "/")] // a prefix of the name is not a mount
		[InlineData("/mnt/usb", "/mnt/usb")]
		public void MountPointOf_PicksTheLongestContainingMount(string path, string expected) =>
			Assert.Equal(expected, GuiFileUtils.MountPointOf(path, new[] { "/", "/mnt/usb", "/home/me/nothere" }));
	}
}
