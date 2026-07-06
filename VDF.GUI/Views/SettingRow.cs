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

using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace VDF.GUI.Views {
	/// <summary>
	/// One option row on the settings page: title + always-visible description on the
	/// left, the control on the right (locked design decision 9). Its template lives in
	/// SettingsView.xaml. Title/description double as the row's search text.
	/// </summary>
	public class SettingRow : ContentControl {

		/// <summary>Trailing colons are stripped so the old locale labels can be reused as titles.</summary>
		public static readonly StyledProperty<string?> TitleProperty =
			AvaloniaProperty.Register<SettingRow, string?>(nameof(Title),
				coerce: (_, value) => value?.TrimEnd().TrimEnd(':', '：').TrimEnd());
		public static readonly StyledProperty<string?> DescriptionProperty =
			AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));
		/// <summary>Extra warn-colored line under the description (e.g. "re-extracts every video").</summary>
		public static readonly StyledProperty<string?> WarningProperty =
			AvaloniaProperty.Register<SettingRow, string?>(nameof(Warning));
		/// <summary>Additional search keywords that are not part of the visible texts.</summary>
		public static readonly StyledProperty<string?> SearchTagsProperty =
			AvaloniaProperty.Register<SettingRow, string?>(nameof(SearchTags));
		/// <summary>The hairline under the row; cleared on the last visible row of a section.</summary>
		public static readonly StyledProperty<bool> ShowSeparatorProperty =
			AvaloniaProperty.Register<SettingRow, bool>(nameof(ShowSeparator), true);

		public string? Title {
			get => GetValue(TitleProperty);
			set => SetValue(TitleProperty, value);
		}
		public string? Description {
			get => GetValue(DescriptionProperty);
			set => SetValue(DescriptionProperty, value);
		}
		public string? Warning {
			get => GetValue(WarningProperty);
			set => SetValue(WarningProperty, value);
		}
		public string? SearchTags {
			get => GetValue(SearchTagsProperty);
			set => SetValue(SearchTagsProperty, value);
		}
		public bool ShowSeparator {
			get => GetValue(ShowSeparatorProperty);
			set => SetValue(ShowSeparatorProperty, value);
		}

		internal string BuildSearchText() =>
			string.Join(' ', new[] { Title, Description, Warning, SearchTags }
				.Where(s => !string.IsNullOrWhiteSpace(s)));
	}

	/// <summary>
	/// Marks a non-row content block (folder lists, shortcut editor, test page) as
	/// searchable: the block hides during a search unless the query matches this text
	/// or any static text found inside the block.
	/// </summary>
	public static class SettingsSearchMeta {
		public static readonly AttachedProperty<string?> TextProperty =
			AvaloniaProperty.RegisterAttached<Control, string?>("Text", typeof(SettingsSearchMeta));

		public static string? GetText(Control control) => control.GetValue(TextProperty);
		public static void SetText(Control control, string? value) => control.SetValue(TextProperty, value);
	}
}
