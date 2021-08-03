// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using Avalonia.Styling;
using System;
using Avalonia.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia;

namespace VDF.GUI.Mvvm {
	/// <summary>
	/// This is taken from Avalonia Xaml Control Gallery
	/// </summary>
	public class FluentWindow : Window, IStyleable {
		Type IStyleable.StyleKey => typeof(Window);

		public FluentWindow() {
			ExtendClientAreaToDecorationsHint = true;
			ExtendClientAreaTitleBarHeightHint = -1;

			TransparencyLevelHint = WindowTransparencyLevel.AcrylicBlur;

			this.GetObservable(WindowStateProperty)
				.Subscribe(x => {
					PseudoClasses.Set(":maximized", x == WindowState.Maximized);
					PseudoClasses.Set(":fullscreen", x == WindowState.FullScreen);
				});

			this.GetObservable(IsExtendedIntoWindowDecorationsProperty)
				.Subscribe(x => {
					if (!x) {
						SystemDecorations = SystemDecorations.Full;
						TransparencyLevelHint = WindowTransparencyLevel.Blur;
					}
				});
		}

		protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
			base.OnApplyTemplate(e);
			ExtendClientAreaChromeHints =
				ExtendClientAreaChromeHints.PreferSystemChrome |
				ExtendClientAreaChromeHints.OSXThickTitleBar;
		}
	}
}
