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

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using DynamicData;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	public class ThumbnailComparer : Window {
		//Designer need this
		public ThumbnailComparer() => InitializeComponent();
		public ThumbnailComparer(List<LargeThumbnailDuplicateItem> duplicateItemVMs)
			: this(duplicateItemVMs, null, null, null) { }
		public ThumbnailComparer(
			List<LargeThumbnailDuplicateItem> duplicateItemVMs,
			Guid? currentGroupId,
			Func<Guid, bool, (Guid GroupId, List<LargeThumbnailDuplicateItem> Items)?>? groupNavigator,
			Func<Guid, (int Index, int Total)?>? groupPosition = null) {
			DataContext = new ThumbnailComparerVM(duplicateItemVMs, currentGroupId, groupNavigator, groupPosition);
			InitializeComponent();
			Owner = ApplicationHelpers.MainWindow;
			this.Loaded += ThumbnailComparer_Loaded;
			this.Opened += ThumbnailComparer_Opened;
			this.Closing += ThumbnailComparer_Closing;
			// Culling keys are tunnel-handled so buttons/sliders never swallow them
			// (locked decision 10: A/D keep a side, Space next pair, arrows step frames, Z zoom).
			AddHandler(KeyDownEvent, OnCullingKeyDown, RoutingStrategies.Tunnel);

			if (SettingsFile.Instance.UseMica &&
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
				Environment.OSVersion.Version.Build >= 22000) {
				Background = null;
				TransparencyLevelHint = new List<WindowTransparencyLevel> { WindowTransparencyLevel.Mica };
				// Avalonia 12: ExtendClientAreaChromeHints was removed; WindowDecorations.Full
				// (system chrome) is the default, matching the old PreferSystemChrome behavior.
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
				_ = vm.LoadThumbnailsAsync();

				void TrackCanvas(string name) {
					var canvas = this.FindControl<Grid>(name);
					if (canvas != null) {
						canvas.GetObservable(Layoutable.BoundsProperty).Subscribe(b => {
							if (canvas.IsVisible) {
								vm.ViewportWidth = b.Width;
								vm.ViewportHeight = b.Height;
							}
						});
						canvas.GetObservable(Visual.IsVisibleProperty).Subscribe(_ => {
							if (canvas.IsVisible) {
								vm.ViewportWidth = canvas.Bounds.Width;
								vm.ViewportHeight = canvas.Bounds.Height;
							}
						});
					}
				}

				TrackCanvas("CompareCanvas");
				TrackCanvas("StackedCanvas");
			}
		}

		private void ThumbnailComparer_Closing(object? sender, System.ComponentModel.CancelEventArgs e) {
			// A close mid-load must stop the thumbnail/frame extraction work — the
			// window is gone, nothing should keep FFmpeg grinding in the background.
			if (DataContext is ThumbnailComparerVM vm)
				vm.CancelBackgroundWork();
			SaveWindowPlacement();
		}

		// Titlebar "✕ Close comparer" (mockup); checked items flow back like any close.
		void OnCloseComparerClick(object? sender, RoutedEventArgs e) => Close();

		void OnCullingKeyDown(object? sender, KeyEventArgs e) {
			if (DataContext is not ThumbnailComparerVM vm) return;
			if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
			// An open dropdown or a text input keeps its keys.
			var focused = FocusManager?.GetFocusedElement() as Control;
			if (focused is TextBox) return;
			var combo = focused as ComboBox ?? focused?.FindLogicalAncestorOfType<ComboBox>();
			if (combo?.IsDropDownOpen == true) return;

			bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
			switch (e.Key) {
				case Key.A:
					vm.KeepLeftCommand.Execute().Subscribe();
					e.Handled = true;
					break;
				case Key.D:
					vm.KeepRightCommand.Execute().Subscribe();
					e.Handled = true;
					break;
				case Key.N:
					vm.NotAMatchCommand.Execute().Subscribe();
					e.Handled = true;
					break;
				case Key.Space:
					vm.SkipPairCommand.Execute().Subscribe();
					e.Handled = true;
					break;
				case Key.Z:
					vm.ToggleZoomCommand.Execute().Subscribe();
					e.Handled = true;
					break;
				case Key.Left:
					// Plain: previous aligned position on BOTH panes; Shift: fine-step both by one frame.
					(shift ? vm.StepBothMinusCommand : vm.PrevBaseCommand).Execute().Subscribe();
					e.Handled = true;
					break;
				case Key.Right:
					(shift ? vm.StepBothPlusCommand : vm.NextBaseCommand).Execute().Subscribe();
					e.Handled = true;
					break;
			}
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
