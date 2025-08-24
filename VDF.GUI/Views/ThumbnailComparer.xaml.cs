// /*
//     Copyright (C) 2025 0x90d
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

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public class ThumbnailComparer : Window {
		//Designer need this
		public ThumbnailComparer() => InitializeComponent();
		public ThumbnailComparer(List<LargeThumbnailDuplicateItem> duplicateItemVMs) {
			DataContext = new ThumbnailComparerVM(duplicateItemVMs);
			InitializeComponent();
			Owner = ApplicationHelpers.MainWindow;
			this.Loaded += ThumbnailComparer_Loaded;

			if (SettingsFile.Instance.UseMica &&
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
				Environment.OSVersion.Version.Build >= 22000) {
				Background = null;
				TransparencyLevelHint = new List<WindowTransparencyLevel> { WindowTransparencyLevel.Mica };
				ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
				if (SettingsFile.Instance.DarkMode)
					this.FindControl<ExperimentalAcrylicBorder>("ExperimentalAcrylicBorderBackgroundBlack")!.IsVisible = true;
				else
					this.FindControl<ExperimentalAcrylicBorder>("ExperimentalAcrylicBorderBackgroundWhite")!.IsVisible = true;
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				this.FindControl<TextBlock>("TextBlockWindowTitle")!.IsVisible = false;
			}
			if (!VDF.GUI.Data.SettingsFile.Instance.DarkMode)
				RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
		}
		void InitializeComponent() => AvaloniaXamlLoader.Load(this);

		private void ThumbnailComparer_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
			if (DataContext != null)
				((ThumbnailComparerVM)DataContext).LoadThumbnails();
		}
	}
}
