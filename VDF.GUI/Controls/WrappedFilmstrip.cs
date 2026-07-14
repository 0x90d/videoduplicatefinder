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
using Avalonia.Media.Imaging;
using VDF.GUI.Utils;

namespace VDF.GUI.Controls {
	/// <summary>
	/// Renders the composite preview frame by frame, re-wrapped to the control's actual
	/// width: frames shrink with the column like the classic strip and stay on one line
	/// (#847), wrapping into more rows only when they would drop below the readability
	/// floor (#834). The composite stays one cached bitmap;
	/// FrameCount/GridColumns describe its storage grid and each frame is drawn from
	/// its cell (WrappedPreviewLayout decides the on-screen arrangement). FrameCount
	/// &lt;= 1 — including results saved before the grid info existed — draws the whole
	/// composite as a single image; frames are never upscaled past their extracted
	/// size (#787).
	/// </summary>
	public class WrappedFilmstrip : Control {
		public static readonly StyledProperty<Bitmap?> SourceProperty =
			AvaloniaProperty.Register<WrappedFilmstrip, Bitmap?>(nameof(Source));
		public static readonly StyledProperty<int> FrameCountProperty =
			AvaloniaProperty.Register<WrappedFilmstrip, int>(nameof(FrameCount));
		public static readonly StyledProperty<int> GridColumnsProperty =
			AvaloniaProperty.Register<WrappedFilmstrip, int>(nameof(GridColumns));
		public static readonly StyledProperty<bool> CompactProperty =
			AvaloniaProperty.Register<WrappedFilmstrip, bool>(nameof(Compact));

		static WrappedFilmstrip() {
			AffectsMeasure<WrappedFilmstrip>(SourceProperty, FrameCountProperty, GridColumnsProperty, CompactProperty);
			AffectsRender<WrappedFilmstrip>(SourceProperty, FrameCountProperty, GridColumnsProperty, CompactProperty);
		}

		public Bitmap? Source {
			get => GetValue(SourceProperty);
			set => SetValue(SourceProperty, value);
		}
		public int FrameCount {
			get => GetValue(FrameCountProperty);
			set => SetValue(FrameCountProperty, value);
		}
		public int GridColumns {
			get => GetValue(GridColumnsProperty);
			set => SetValue(GridColumnsProperty, value);
		}
		public bool Compact {
			get => GetValue(CompactProperty);
			set => SetValue(CompactProperty, value);
		}

		(int frames, int columns, Size cell) StorageGrid(Bitmap source) {
			int frames = FrameCount, columns = GridColumns;
			if (frames <= 1 || columns < 1) {
				frames = 1;
				columns = 1;
			}
			columns = Math.Min(columns, frames);
			int rows = ThumbnailGridLayout.Rows(frames, columns);
			return (frames, columns, new Size(source.Size.Width / columns, source.Size.Height / rows));
		}

		protected override Size MeasureOverride(Size availableSize) {
			var source = Source;
			if (source == null || source.Size.Width <= 0 || source.Size.Height <= 0)
				return default;
			var (frames, _, cell) = StorageGrid(source);
			var layout = WrappedPreviewLayout.Compute(availableSize.Width, Compact, cell.Width, cell.Height, frames);
			return new Size(Math.Min(layout.TotalWidth, availableSize.Width), layout.TotalHeight);
		}

		public override void Render(DrawingContext context) {
			var source = Source;
			if (source == null || source.Size.Width <= 0 || source.Size.Height <= 0 || Bounds.Width <= 0)
				return;
			// Transparent fill so the gaps between frames still hit-test — the
			// double-tap that opens the comparer must work anywhere on the preview.
			context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
			var (frames, columns, cell) = StorageGrid(source);
			// Same input width as MeasureOverride (the arranged width only ever equals
			// or shrinks to the layout's own TotalWidth, which re-picks identically).
			var layout = WrappedPreviewLayout.Compute(Bounds.Width, Compact, cell.Width, cell.Height, frames);
			if (layout.FrameWidth <= 0 || layout.FrameHeight <= 0)
				return;
			for (int i = 0; i < frames; i++) {
				var sourceRect = new Rect((i % columns) * cell.Width, (i / columns) * cell.Height, cell.Width, cell.Height);
				var destRect = new Rect(
					(i % layout.FramesPerRow) * (layout.FrameWidth + WrappedPreviewLayout.Gap),
					(i / layout.FramesPerRow) * (layout.FrameHeight + WrappedPreviewLayout.Gap),
					layout.FrameWidth, layout.FrameHeight);
				context.DrawImage(source, sourceRect, destRect);
			}
		}
	}
}
