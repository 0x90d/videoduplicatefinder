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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using DynamicData;
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
			this.Opened += ThumbnailComparer_Opened;
			this.Closing += ThumbnailComparer_Closing;

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

		private void ThumbnailComparer_Opened(object? sender, EventArgs e) => ApplySavedWindowPlacement();

		private void ThumbnailComparer_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
			if (DataContext is ThumbnailComparerVM vm) {
				vm.LoadThumbnailsAsync();
				var canvas = this.FindControl<Grid>("CompareCanvas");
				if (canvas != null) {
					var b = canvas.Bounds;
					vm.ViewportWidth = b.Width;
					vm.ViewportHeight = b.Height;

					canvas.GetObservable(Layoutable.BoundsProperty).Subscribe(b => {
						vm.ViewportWidth = b.Width;
						vm.ViewportHeight = b.Height;
					});

					canvas.GetObservable(Visual.IsVisibleProperty).Subscribe(visible => {
						var cb = canvas.Bounds;
						vm.ViewportWidth = cb.Width;
						vm.ViewportHeight = cb.Height;
					});
				}
			}
		}

		private void ThumbnailComparer_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
			SaveWindowPlacement();
		}

		private void ApplySavedWindowPlacement() {
			var settings = SettingsFile.Instance;
			var screens = Screens;
			var targetScreen = ResolveScreen(screens, settings.ThumbnailComparerWindowScreenIndex);

			if (settings.ThumbnailComparerWindowWidth is double savedWidth && savedWidth > 0) {
				Width = savedWidth;
			}
			if (settings.ThumbnailComparerWindowHeight is double savedHeight && savedHeight > 0) {
				Height = savedHeight;
			}

			if (targetScreen != null) {
				var workingArea = targetScreen.WorkingArea;
				var cappedWidth = Math.Min(Width, workingArea.Width);
				var cappedHeight = Math.Min(Height, workingArea.Height);
				if (cappedWidth > 0) {
					Width = cappedWidth;
				}
				if (cappedHeight > 0) {
					Height = cappedHeight;
				}

				if (settings.ThumbnailComparerWindowPositionX.HasValue && settings.ThumbnailComparerWindowPositionY.HasValue) {
					var desiredWidth = Width;
					var desiredHeight = Height;
					var maxX = workingArea.Right - (int)Math.Ceiling(desiredWidth);
					var maxY = workingArea.Bottom - (int)Math.Ceiling(desiredHeight);
					if (maxX < workingArea.X) {
						maxX = workingArea.X;
					}
					if (maxY < workingArea.Y) {
						maxY = workingArea.Y;
					}

					var clampedX = Math.Clamp((int)Math.Round(settings.ThumbnailComparerWindowPositionX.Value), workingArea.X, maxX);
					var clampedY = Math.Clamp((int)Math.Round(settings.ThumbnailComparerWindowPositionY.Value), workingArea.Y, maxY);
					Position = new PixelPoint(clampedX, clampedY);
					WindowStartupLocation = WindowStartupLocation.Manual;
					return;
				}
			}

			WindowStartupLocation = WindowStartupLocation.CenterScreen;
		}

		private void SaveWindowPlacement() {
			var settings = SettingsFile.Instance;
			settings.ThumbnailComparerWindowWidth = Width;
			settings.ThumbnailComparerWindowHeight = Height;

			if (Screens != null) {
				var centerPoint = new PixelPoint(
					Position.X + (int)Math.Round(Width / 2),
					Position.Y + (int)Math.Round(Height / 2));
				var screen = Screens.ScreenFromPoint(centerPoint) ?? Screens.Primary;
				var screenIndex = screen != null ? Screens.All.IndexOf(screen) : -1;
				if (screenIndex >= 0) {
					settings.ThumbnailComparerWindowScreenIndex = screenIndex;
					settings.ThumbnailComparerWindowPositionX = Position.X;
					settings.ThumbnailComparerWindowPositionY = Position.Y;
				}
				else {
					settings.ThumbnailComparerWindowScreenIndex = null;
					settings.ThumbnailComparerWindowPositionX = null;
					settings.ThumbnailComparerWindowPositionY = null;
				}
			}
			else {
				settings.ThumbnailComparerWindowScreenIndex = null;
				settings.ThumbnailComparerWindowPositionX = null;
				settings.ThumbnailComparerWindowPositionY = null;
			}

			SettingsFile.SaveSettings();
		}

		private static Screen? ResolveScreen(Screens? screens, int? screenIndex) {
			if (screens == null) {
				return null;
			}
			if (screenIndex is int index && index >= 0 && index < screens.All.Count) {
				return screens.All[index];
			}
			return screens.Primary;
		}
	}
}
