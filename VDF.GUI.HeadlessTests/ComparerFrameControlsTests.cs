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

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using ReactiveUI;
using VDF.Core.ViewModels;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// The comparer's frame step buttons and frame strips only exist once a video's frames are
/// loaded, which takes FFmpeg and a real file: the guards over the comparer never saw them.
/// Here the frames are handed in.
/// </summary>
public class ComparerFrameControlsTests {

	sealed class VideoWithFrames(string name, Guid group) : LargeThumbnailDuplicateItem(new DuplicateItemVM(new DuplicateItem {
		Path = $@"Z:\does\not\exist\{name}", GroupId = group, Similarity = 98.4f, IsImage = false,
		SizeLong = 700_000_000, FrameSize = "1280x720", Duration = TimeSpan.FromSeconds(754),
		ThumbnailTimestamps = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)],
	})) {
		public override void LoadThumbnail(CancellationToken ct = default, Action? frameLoaded = null) {
			var frames = Enumerable.Range(0, 3)
				.Select(_ => (Bitmap)new WriteableBitmap(new PixelSize(64, 36), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
				.ToList();
			SetFrames(frames);
			Thumbnail = frames[0];
			IsLoadingThumbnail = false;
			this.RaisePropertyChanged(nameof(Thumbnail));
		}
	}

	static async Task WithLoadedComparer(ThemeVariant variant, Action<ThumbnailComparer> body) {
		HeadlessUi.Shell(); // the comparer takes the main window as its owner
		var group = Guid.NewGuid();
		var comparer = new ThumbnailComparer([new VideoWithFrames("left.mp4", group), new VideoWithFrames("right.mp4", group)]) {
			RequestedThemeVariant = variant,
		};
		var vm = (ThumbnailComparerVM)comparer.DataContext!;
		comparer.Show();
		try {
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (!(vm.ShowFrameControls && vm.StripA.Count == 3 && !vm.IsLoadingThumbnails) && DateTime.UtcNow < deadline)
				await Task.Delay(20);
			HeadlessUi.Pump();
			Assert.True(vm.ShowFrameControls, "the fixture did not bring up the frame controls");
			body(comparer);
		}
		finally {
			comparer.Hide();
			HeadlessUi.Pump();
		}
	}

	[Fact]
	public Task FrameStepButtonsAndFrameStrips_AreNamed() => HeadlessUi.Run(() =>
		WithLoadedComparer(ThemeVariant.Dark, comparer => {
			AccessibleNameTests.AssertEverythingIsNamed(comparer);

			// And they really were part of what was checked.
			var names = PeerTree.Walk(comparer).Select(n => n.Name).ToList();
			Assert.Contains("Previous frame, left file", names);
			Assert.Contains("Next frame, right file", names);
			Assert.Contains("Frames of the left file", names);
			Assert.Contains("Frame 2", names);
		}));

	[Theory]
	[InlineData("Dark")]
	[InlineData("Light")]
	public Task WithFramesLoaded_TextIsReadable(string theme) => HeadlessUi.Run(() => {
		var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
		return WithLoadedComparer(variant, comparer => {
			var failures = ContrastTests.Measure(comparer, variant);
			Assert.True(failures.Count == 0,
				$"{failures.Count} kind(s) of text below the required contrast in the {theme} theme:\n  " + string.Join("\n  ", failures));
		});
	});
}
