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

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using VDF.GUI.Data;
using VDF.GUI.Utils;

namespace VDF.GUI.Views {
	public class AboutWindow : Window {
		const string ProjectUrl = "https://github.com/0x90d/videoduplicatefinder";
		const string ReleasesUrl = ProjectUrl + "/releases";

		// Designer needs a parameterless ctor.
		public AboutWindow() {
			InitializeComponent();

			if (!SettingsFile.Instance.DarkMode)
				RequestedThemeVariant = ThemeVariant.Light;
			if (ApplicationHelpers.MainWindow != null)
				Icon = ApplicationHelpers.MainWindow.Icon;

			Title = App.Lang["About.Title"];
			SetText("VersionText", VersionInfo.LongDisplay);
			SetText("EnvText",
				$"{RuntimeInformation.FrameworkDescription}\n" +
				$"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
			SetText("LicenseText", App.Lang["About.License"]);
			SetContent("ProjectPageButton", App.Lang["About.ProjectPage"]);
			SetContent("LatestReleaseButton", App.Lang["MainWindow.Menu.LatestRelease"]);
			SetContent("CopyButton", App.Lang["About.Copy"]);
			SetContent("OkButton", App.Lang["Dialog.OK"]);
		}

		void SetText(string name, string value) {
			var tb = this.FindControl<TextBlock>(name);
			if (tb != null)
				tb.Text = value;
		}

		void SetContent(string name, string value) {
			var btn = this.FindControl<Button>(name);
			if (btn != null)
				btn.Content = value;
		}

		void OnProjectPage(object? sender, RoutedEventArgs e) => OpenUrl(ProjectUrl);

		void OnLatestRelease(object? sender, RoutedEventArgs e) => OpenUrl(ReleasesUrl);

		static void OpenUrl(string url) {
			try {
				Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
			}
			catch { /* opening a browser is best-effort */ }
		}

		void OnCopy(object? sender, RoutedEventArgs e) {
			string report =
				$"Video Duplicate Finder {VersionInfo.LongDisplay}\n" +
				$"{RuntimeInformation.FrameworkDescription}\n" +
				$"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
			Clipboard?.SetTextAsync(report);
		}

		void OnOk(object? sender, RoutedEventArgs e) => Close();

		private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
	}
}
