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

using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	// CompareMode persists numerically in Settings.json; a settings file written by a
	// build that temporarily had extra modes can carry an out-of-range value, which
	// would leave every Is* mode flag false and the canvas blank.
	public class ComparerModeSanitizeTests {

		static ComparerModeSanitizeTests() {
			// Same recipe as ComparerLoadCancellationTests; guarded because whichever
			// test class runs first already initialized ReactiveUI for the process.
			try {
				ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder()
					.WithMainThreadScheduler(System.Reactive.Concurrency.CurrentThreadScheduler.Instance)
					.BuildApp();
			}
			catch { }
		}

		[Fact]
		public void OutOfRangePersistedMode_FallsBackToSideBySide() {
			var previous = SettingsFile.Instance.ThumbnailComparerMode;
			try {
				SettingsFile.Instance.ThumbnailComparerMode = (CompareMode)5;
				var vm = new ThumbnailComparerVM(new List<LargeThumbnailDuplicateItem> {
					new(new DuplicateItemVM(new DuplicateItem {
						Path = @"Z:\does\not\exist\clip.mp4",
						GroupId = Guid.NewGuid(),
						IsImage = false,
					})),
				});
				Assert.Equal(CompareMode.SideBySide, vm.SelectedCompareMode);
			}
			finally {
				SettingsFile.Instance.ThumbnailComparerMode = previous;
			}
		}
	}
}
