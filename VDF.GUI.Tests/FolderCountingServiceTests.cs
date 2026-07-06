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

using VDF.GUI.Utils;

namespace VDF.GUI.Tests {
	public class FolderCountingServiceTests : IDisposable {
		readonly string root;

		public FolderCountingServiceTests() {
			root = Path.Combine(Path.GetTempPath(), "vdf-count-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
		}

		public void Dispose() {
			try { Directory.Delete(root, recursive: true); } catch (Exception) { }
		}

		void WriteFile(string relative, int bytes = 10) {
			string path = Path.Combine(root, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllBytes(path, new byte[bytes]);
		}

		static FolderCountProgress WaitForCompletion(FolderCountingService service, string path) {
			FolderCountProgress? final = null;
			using var done = new System.Threading.ManualResetEventSlim();
			Assert.True(service.StartCounting(path, p => {
				if (p.Completed) {
					final = p;
					done.Set();
				}
			}));
			Assert.True(done.Wait(TimeSpan.FromSeconds(15)), "counting did not finish in time");
			return final!;
		}

		[Fact]
		public void CountsOnlyMediaFiles_Recursively_WithSizes() {
			WriteFile("a.mp4", 100);
			WriteFile("b.jpg", 50);
			WriteFile("notes.txt", 999);          // not media
			WriteFile(@"sub\deeper\c.mkv", 25);

			var result = WaitForCompletion(new FolderCountingService(), root);

			Assert.Equal(3, result.FileCount);
			Assert.Equal(175, result.TotalBytes);
			Assert.False(result.Failed);
		}

		[Fact]
		public void EmptyFolder_CompletesWithZero() {
			var result = WaitForCompletion(new FolderCountingService(), root);
			Assert.Equal(0, result.FileCount);
			Assert.False(result.Failed);
		}

		[Fact]
		public void DuplicateRequests_AreDeduplicated() {
			// Enough files that the first walk is still running when the second starts.
			for (int i = 0; i < 200; i++)
				WriteFile($@"many\file{i}.mp4");

			var service = new FolderCountingService();
			using var done = new System.Threading.ManualResetEventSlim();
			bool first = service.StartCounting(root, p => { if (p.Completed) done.Set(); });
			bool second = service.StartCounting(root, _ => { });

			Assert.True(first);
			Assert.False(second);
			Assert.True(done.Wait(TimeSpan.FromSeconds(15)));

			// After completion the folder can be counted again.
			Assert.True(service.StartCounting(root, _ => { }));
			service.CancelAll();
		}

		[Fact]
		public void Cancel_StopsWithoutCompletionReport() {
			for (int i = 0; i < 500; i++)
				WriteFile($@"big\file{i}.mp4");

			var service = new FolderCountingService(TimeSpan.Zero);
			using var progressed = new System.Threading.ManualResetEventSlim();
			bool completed = false;
			service.StartCounting(root, p => {
				if (p.Completed) completed = true;
				else progressed.Set();
			});
			Assert.True(progressed.Wait(TimeSpan.FromSeconds(15)), "no progress before cancel");
			service.Cancel(root);

			// Give the walk time to notice the cancellation and unwind.
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while (service.IsCounting(root) && sw.Elapsed < TimeSpan.FromSeconds(10))
				Thread.Sleep(20);
			Assert.False(service.IsCounting(root));
			Assert.False(completed);
		}

		[Fact]
		public void MissingFolder_ReportsFailedCompletion() {
			var result = WaitForCompletion(new FolderCountingService(),
				Path.Combine(root, "does-not-exist"));
			Assert.True(result.Failed);
		}

		[Theory]
		[InlineData(@"\\NAS\media\Movies", true)]
		[InlineData("//nas/share", true)]
		[InlineData(@"C:\Users\Public", false)]
		[InlineData("", false)]
		public void IsNetworkPath_DetectsUncPaths(string path, bool expected) {
			Assert.Equal(expected, FolderCountingService.IsNetworkPath(path));
		}
	}
}
