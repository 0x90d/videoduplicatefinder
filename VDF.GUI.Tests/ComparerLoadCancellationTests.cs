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

using System.Reactive.Linq;
using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	// The comparer's loading overlay blocked the whole window with no way out
	// (user report 2026-07-14): no cancel, and closing didn't stop the load.
	// These drive the VM side; the overlay's titlebar clearance is XAML-only.
	public class ComparerLoadCancellationTests {

		static ComparerLoadCancellationTests() {
			// ReactiveUI 21 requires builder initialization before WhenAnyValue (the
			// app does this via UseReactiveUI); run everything on the current thread.
			ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder()
				.WithMainThreadScheduler(System.Reactive.Concurrency.CurrentThreadScheduler.Instance)
				.BuildApp();
		}

		static ThumbnailComparerVM MakeVm(int itemCount) {
			var items = new List<LargeThumbnailDuplicateItem>();
			var groupId = Guid.NewGuid();
			for (int i = 0; i < itemCount; i++) {
				var info = new DuplicateItem {
					Path = $@"Z:\does\not\exist\clip_{i}.mp4",
					GroupId = groupId,
					IsImage = false,
					// No timestamps: LoadThumbnail yields no bitmap without ever
					// touching FFmpeg or Avalonia, so the load "fails" instantly.
				};
				items.Add(new LargeThumbnailDuplicateItem(new DuplicateItemVM(info)));
			}
			return new ThumbnailComparerVM(items);
		}

		[Fact]
		public async Task CancelDuringLoad_ClearsOverlayAndSpinners() {
			var vm = MakeVm(4);
			var load = vm.LoadThumbnailsAsync();
			vm.CancelBackgroundWork();
			await load;
			Assert.False(vm.IsLoadingThumbnails);
			Assert.All(vm.Items, i => Assert.False(i.IsLoadingThumbnail));
		}

		[Fact]
		public async Task CancelCommand_StopsLoad() {
			var vm = MakeVm(2);
			var load = vm.LoadThumbnailsAsync();
			await vm.CancelThumbnailLoadingCommand.Execute();
			await load;
			Assert.False(vm.IsLoadingThumbnails);
			Assert.All(vm.Items, i => Assert.False(i.IsLoadingThumbnail));
		}

		[Fact]
		public void CancelBeforeAnyLoad_IsSafe() {
			var vm = MakeVm(1);
			vm.CancelBackgroundWork(); // nothing in flight - must not throw
			Assert.False(vm.IsLoadingThumbnails);
		}

		// A LargeThumbnailDuplicateItem whose grab never returns, standing in for a
		// hung/glacial FFmpeg decode (network share, corrupt file).
		sealed class StuckItem : LargeThumbnailDuplicateItem {
			readonly ManualResetEventSlim _gate;
			public StuckItem(ManualResetEventSlim gate) : base(new DuplicateItemVM(new DuplicateItem {
				Path = @"Z:\does\not\exist\stuck.mp4",
				GroupId = Guid.NewGuid(),
				IsImage = false,
				ThumbnailTimestamps = new List<TimeSpan> { TimeSpan.FromSeconds(1) },
			})) => _gate = gate;
			public override void LoadThumbnail(CancellationToken ct = default, Action? frameLoaded = null) {
				_gate.Wait(); // simulates FFmpeg never coming back
				IsLoadingThumbnail = false;
			}
		}

		// User report 2026-07-14 (second): Cancel appeared completely dead because the
		// load awaited WhenAll behind an in-flight FFmpeg grab. Cancel must surface
		// even while a grab is stuck.
		[Fact]
		public async Task Cancel_ReturnsEvenWhileAGrabIsStuck() {
			using var gate = new ManualResetEventSlim(false);
			var vm = new ThumbnailComparerVM(new List<LargeThumbnailDuplicateItem> { new StuckItem(gate) });
			var load = vm.LoadThumbnailsAsync();
			vm.CancelBackgroundWork();
			var finished = await Task.WhenAny(load, Task.Delay(5000)) == load;
			gate.Set(); // unblock the stranded worker thread before asserting
			Assert.True(finished, "LoadThumbnailsAsync did not return after cancel while a grab was in flight");
			Assert.False(vm.IsLoadingThumbnails);
		}
	}
}
