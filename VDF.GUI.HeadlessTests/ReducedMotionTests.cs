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

using Avalonia.Controls;
using VDF.GUI.Utils;

namespace VDF.GUI.HeadlessTests;

/// <summary>
/// The thumbnail placeholders pulse for as long as thumbnails load, all of them at once.
/// Users who asked their system for less motion get them standing still.
/// </summary>
public class ReducedMotionTests {

	static async Task<List<double>> OpacitiesOverTime(Window window, Border shimmer) {
		var seen = new List<double>();
		for (int i = 0; i < 12; i++) {
			await Task.Delay(40);
			Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
			seen.Add(Math.Round(shimmer.Opacity, 3));
		}
		window.Close();
		return seen;
	}

	static (Window, Border) ShowShimmer(bool reduceMotion) {
		var shimmer = new Border { Width = 120, Height = 60 };
		shimmer.Classes.Add("skeleton-shimmer");
		var window = new Window { Width = 300, Height = 200, Content = shimmer };
		window.Classes.Set("reduce-motion", reduceMotion);
		window.Show();
		HeadlessUi.Pump();
		return (window, shimmer);
	}

	[Fact]
	public Task Shimmer_Pulses_ByDefault() => HeadlessUi.Run(async () => {
		var (window, shimmer) = ShowShimmer(reduceMotion: false);

		var seen = await OpacitiesOverTime(window, shimmer);

		Assert.True(seen.Distinct().Count() > 3, "expected the opacity to move, saw " + string.Join(" ", seen));
	});

	[Fact]
	public Task Shimmer_StandsStill_WhenTheUserAskedForLessMotion() => HeadlessUi.Run(async () => {
		var (window, shimmer) = ShowShimmer(reduceMotion: true);

		var seen = await OpacitiesOverTime(window, shimmer);

		// Still recognizable as a placeholder, which a fully opaque block would not be.
		Assert.All(seen, opacity => Assert.Equal(0.5, opacity));
	});

	[Fact]
	public Task EveryWindow_FollowsTheSystem_AndTheSetting_Live() => HeadlessUi.Run(() => {
		var (window, _) = HeadlessUi.Shell();
		bool settingBefore = Data.SettingsFile.Instance.AlwaysReduceMotion;
		var dialog = new Views.AboutWindow();
		dialog.Show();
		HeadlessUi.Pump();
		try {
			Data.SettingsFile.Instance.AlwaysReduceMotion = false;
			Appearance.SetSystemAnimations(true);
			Assert.False(window.Classes.Contains("reduce-motion"));
			Assert.False(dialog.Classes.Contains("reduce-motion"));

			// The user switches animations off in the system while VDF runs.
			Appearance.SetSystemAnimations(false);
			Assert.True(window.Classes.Contains("reduce-motion"));
			Assert.True(dialog.Classes.Contains("reduce-motion"));

			// The system allows them, VDF is told not to move anyway.
			Appearance.SetSystemAnimations(true);
			Data.SettingsFile.Instance.AlwaysReduceMotion = true;
			Assert.True(window.Classes.Contains("reduce-motion"));
			Assert.True(dialog.Classes.Contains("reduce-motion"));
		}
		finally {
			dialog.Hide();
			Data.SettingsFile.Instance.AlwaysReduceMotion = settingBefore;
			Appearance.SetSystemAnimations(null);
			HeadlessUi.Pump();
		}
	});

	[Theory]
	[InlineData(false, true, false)]
	[InlineData(false, false, true)]  // the system says no animations: followed
	[InlineData(false, null, false)]  // no answer from the system: animate as before
	[InlineData(true, true, true)]    // the setting only ever asks for less motion
	[InlineData(true, null, true)]
	public void Motion_IsReduced_WhenTheSystemOrTheUserSaysSo(bool always, bool? animationsEnabled, bool expected) =>
		Assert.Equal(expected, Appearance.ResolveReduceMotion(always, animationsEnabled));
}
