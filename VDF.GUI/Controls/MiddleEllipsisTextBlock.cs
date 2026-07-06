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
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using VDF.GUI.Utils;

namespace VDF.GUI.Controls {
	/// <summary>
	/// Single-line text that trims in the MIDDLE when space runs out, keeping both the
	/// start (drive/root) and the end (filename) visible — Avalonia's built-in trimming
	/// modes only cut at one end. Used for the path line in the results list.
	/// </summary>
	public class MiddleEllipsisTextBlock : Control {
		public static readonly StyledProperty<string?> TextProperty =
			AvaloniaProperty.Register<MiddleEllipsisTextBlock, string?>(nameof(Text));
		public static readonly StyledProperty<IBrush?> ForegroundProperty =
			TextBlock.ForegroundProperty.AddOwner<MiddleEllipsisTextBlock>();
		public static readonly StyledProperty<double> FontSizeProperty =
			TextBlock.FontSizeProperty.AddOwner<MiddleEllipsisTextBlock>();
		public static readonly StyledProperty<FontFamily> FontFamilyProperty =
			TextBlock.FontFamilyProperty.AddOwner<MiddleEllipsisTextBlock>();

		static MiddleEllipsisTextBlock() {
			AffectsMeasure<MiddleEllipsisTextBlock>(TextProperty, FontSizeProperty, FontFamilyProperty);
			AffectsRender<MiddleEllipsisTextBlock>(TextProperty, ForegroundProperty, FontSizeProperty, FontFamilyProperty);
		}

		public string? Text {
			get => GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}
		public IBrush? Foreground {
			get => GetValue(ForegroundProperty);
			set => SetValue(ForegroundProperty, value);
		}
		public double FontSize {
			get => GetValue(FontSizeProperty);
			set => SetValue(FontSizeProperty, value);
		}
		public FontFamily FontFamily {
			get => GetValue(FontFamilyProperty);
			set => SetValue(FontFamilyProperty, value);
		}

		TextLayout CreateLayout(string text) =>
			new(text, new Typeface(FontFamily), FontSize, Foreground);

		protected override Size MeasureOverride(Size availableSize) {
			string text = Text ?? string.Empty;
			using var layout = CreateLayout(text.Length == 0 ? " " : text);
			return new Size(Math.Min(layout.WidthIncludingTrailingWhitespace, availableSize.Width), layout.Height);
		}

		public override void Render(DrawingContext context) {
			string text = Text ?? string.Empty;
			if (text.Length == 0 || Bounds.Width <= 0)
				return;

			string display = MiddleEllipsis.Trim(text, s => {
				using var probe = CreateLayout(s);
				return probe.WidthIncludingTrailingWhitespace;
			}, Bounds.Width);

			using var layout = CreateLayout(display);
			layout.Draw(context, new Point(0, (Bounds.Height - layout.Height) / 2));
		}
	}
}
