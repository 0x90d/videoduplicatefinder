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
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Views {
	/// <summary>Every metadata tag of a group's files side by side (#926).</summary>
	public partial class MetadataCompareWindow : Window {
		public MetadataCompareWindow() {
			AvaloniaXamlLoader.Load(this);
			Owner = ApplicationHelpers.MainWindow;
			VDF.GUI.Utils.Appearance.Attach(this);
		}

		/// <summary>Shows the files' tags; reading starts once the window is open.</summary>
		public MetadataCompareWindow(MetadataCompareVM vm) : this() {
			DataContext = vm;
			Opened += async (_, _) => await vm.LoadAsync();
		}

		/// <summary>Texts of the comparison in the current UI language.</summary>
		public static MetadataCompareTexts LocalizedTexts() => new() {
			Container = App.Lang["MetadataCompare.Container"],
			Stream = App.Lang["MetadataCompare.Stream"],
			Exif = App.Lang["MetadataCompare.Exif"],
			Gps = App.Lang["MetadataCompare.Gps"],
			NotSet = App.Lang["MetadataCompare.NotSet"],
			Unavailable = App.Lang["MetadataCompare.Unavailable"],
			Differs = App.Lang["MetadataCompare.Differs"],
			CheckFile = App.Lang["MetadataCompare.CheckFile"],
			Summary = App.Lang["MetadataCompare.Summary"],
		};

		void Close_Click(object? sender, RoutedEventArgs e) => Close();
	}
}
