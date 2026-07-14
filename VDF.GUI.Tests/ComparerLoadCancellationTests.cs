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
	}
}
