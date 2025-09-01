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
//     along with VideoDuplicateFinder.  If not, see <https://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace VDF.GUI.Data {
	public class ZoomPanPresenter : ContentControl {
		public static readonly StyledProperty<double> ZoomProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(Zoom), 1.0);

		public static readonly StyledProperty<double> OffsetXProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(OffsetX), 0.0);

		public static readonly StyledProperty<double> OffsetYProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(OffsetY), 0.0);

		public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
		public double OffsetX { get => GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
		public double OffsetY { get => GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }

		public double MinZoom { get; set; } = 0.1;
		public double MaxZoom { get; set; } = 8.0;
		private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
		private readonly TranslateTransform _translate = new();
		private TransformGroup? _group;

		private bool _panning;
		private Point _pointerStart;
		private double _startX, _startY;

		public ZoomPanPresenter() {
			ClipToBounds = true;
			Background = Brushes.Transparent;

			AddHandler(PointerWheelChangedEvent, OnWheel, handledEventsToo: true);
			AddHandler(PointerPressedEvent, OnPressed, handledEventsToo: true);
			AddHandler(PointerReleasedEvent, OnReleased, handledEventsToo: true);
			AddHandler(PointerMovedEvent, OnMoved, handledEventsToo: true);

			this.GetObservable(ContentProperty).Subscribe(_ => AttachTransform());

			this.GetPropertyChangedObservable(ZoomProperty).Subscribe(_ => ApplyTransform());
			this.GetPropertyChangedObservable(OffsetXProperty).Subscribe(_ => ApplyTransform());
			this.GetPropertyChangedObservable(OffsetYProperty).Subscribe(_ => ApplyTransform());

		}

		private void AttachTransform() {
			if (Content is Visual v) {
				_group ??= new TransformGroup { Children = { _scale, _translate } };
				v.RenderTransform = _group;
				ApplyTransform();
			}
		}

		private void ApplyTransform() {
			_scale.ScaleX = _scale.ScaleY = Math.Clamp(Zoom, MinZoom, MaxZoom);
			_translate.X = OffsetX;
			_translate.Y = OffsetY;
			InvalidateVisual();
		}

		private void OnWheel(object? s, PointerWheelEventArgs e) {
			var oldZoom = Zoom;
			var factor = e.Delta.Y > 0 ? 1.1 : (1.0 / 1.1);
			var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);

			var mouse = e.GetPosition(this);
			var contentX = (mouse.X - OffsetX) / oldZoom;
			var contentY = (mouse.Y - OffsetY) / oldZoom;

			Zoom = newZoom;

			OffsetX = mouse.X - contentX * newZoom;
			OffsetY = mouse.Y - contentY * newZoom;

			e.Handled = true;
		}

		private void OnPressed(object? s, PointerPressedEventArgs e) {
			var props = e.GetCurrentPoint(this).Properties;
			if (props.IsLeftButtonPressed || props.IsRightButtonPressed) {
				_panning = true;
				_pointerStart = e.GetPosition(this);
				_startX = OffsetX;
				_startY = OffsetY;
				e.Pointer.Capture(this);
				e.Handled = true;
			}
		}

		private void OnReleased(object? s, PointerReleasedEventArgs e) {
			if (_panning) {
				_panning = false;
				e.Pointer.Capture(null);
				e.Handled = true;
			}
		}

		private void OnMoved(object? s, PointerEventArgs e) {
			if (!_panning) return;

			var p = e.GetPosition(this);
			var d = p - _pointerStart;
			OffsetX = _startX + d.X;
			OffsetY = _startY + d.Y;
			e.Handled = true;
		}
	}
}
