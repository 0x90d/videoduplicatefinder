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
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.LogicalTree;

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

		static SettingRow() {
			ContentProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.ApplyAccessibleName());
			TitleProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.ApplyAccessibleName());
			DescriptionProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.ApplyAccessibleName());
			WarningProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.ApplyAccessibleName());
		}

		// The XAML loader assigns Content before it fills a content panel, so at that moment a
		// row holding several controls has no input to name yet.
		protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e) {
			base.OnLoaded(e);
			ApplyAccessibleName();
		}

		/// <summary>
		/// The title and description are plain text next to the control, which a screen reader
		/// does not connect to it: every switch on the page announced as just "button". The row's
		/// control therefore takes the title as its accessible name and the description (plus
		/// warning) as its help text. Set at template priority: above the app-wide defaults in
		/// _Accessibility.xaml, below a name given in XAML, which rows holding several controls
		/// use for the ones after the first.
		/// </summary>
		void ApplyAccessibleName() {
			if (FindLabelTarget() is not { } target) return;
			target.SetValue(AutomationProperties.NameProperty, Title, BindingPriority.Template);
			string help = string.Join(' ', new[] { Description, Warning }.Where(s => !string.IsNullOrWhiteSpace(s)));
			target.SetValue(AutomationProperties.HelpTextProperty, help.Length > 0 ? help : null, BindingPriority.Template);
		}

		/// <summary>The row's input control, or the first one when the content is a panel of several.</summary>
		internal Control? FindLabelTarget() => Content switch {
			Control control when IsInput(control) => control,
			Control control => control.GetLogicalDescendants().OfType<Control>().FirstOrDefault(IsInput),
			_ => null
		};

		// Plain buttons and links are left out: their own content already names them.
		static bool IsInput(Control control) =>
			control is ToggleButton or NumericUpDown or SelectingItemsControl or TextBox or RangeBase or AutoCompleteBox;

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
