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
		public static readonly StyledProperty<bool> SwipeModeProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, bool>(nameof(SwipeMode), false);
		public static readonly StyledProperty<bool> SwipeVerticalProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, bool>(nameof(SwipeVertical), false);
		public static readonly StyledProperty<double> SwipeRatioProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(SwipeRatio), 0.5);
		public static readonly StyledProperty<double> SwipeLeftOffsetProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(SwipeLeftOffset), 0.0);
		public static readonly StyledProperty<double> SwipeDisplayWidthProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(SwipeDisplayWidth), 0.0);
		public static readonly StyledProperty<double> SwipeTopOffsetProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(SwipeTopOffset), 0.0);
		public static readonly StyledProperty<double> SwipeDisplayHeightProperty =
			AvaloniaProperty.Register<ZoomPanPresenter, double>(nameof(SwipeDisplayHeight), 0.0);

		public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
		public double OffsetX { get => GetValue(OffsetXProperty); set => SetValue(OffsetXProperty, value); }
		public double OffsetY { get => GetValue(OffsetYProperty); set => SetValue(OffsetYProperty, value); }
		public bool SwipeMode { get => GetValue(SwipeModeProperty); set => SetValue(SwipeModeProperty, value); }
		public bool SwipeVertical { get => GetValue(SwipeVerticalProperty); set => SetValue(SwipeVerticalProperty, value); }
		public double SwipeRatio { get => GetValue(SwipeRatioProperty); set => SetValue(SwipeRatioProperty, value); }
		public double SwipeLeftOffset { get => GetValue(SwipeLeftOffsetProperty); set => SetValue(SwipeLeftOffsetProperty, value); }
		public double SwipeDisplayWidth { get => GetValue(SwipeDisplayWidthProperty); set => SetValue(SwipeDisplayWidthProperty, value); }
		public double SwipeTopOffset { get => GetValue(SwipeTopOffsetProperty); set => SetValue(SwipeTopOffsetProperty, value); }
		public double SwipeDisplayHeight { get => GetValue(SwipeDisplayHeightProperty); set => SetValue(SwipeDisplayHeightProperty, value); }

		public double MinZoom { get; set; } = 0.1;
		public double MaxZoom { get; set; } = 8.0;
		private readonly ScaleTransform _scale = new() { ScaleX = 1, ScaleY = 1 };
		private readonly TranslateTransform _translate = new();
		private TransformGroup? _group;

		private bool _panning;
		private bool _swiping;
		private Point _pointerStart;
		private double _startX, _startY;

		const double SwipeGrabDistance = 20;

		bool IsAnySwipe => SwipeMode || SwipeVertical;

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

		private double GetDividerViewportPos(Point viewportPoint) {
			if (SwipeVertical) {
				var contentY = SwipeTopOffset + SwipeDisplayHeight * SwipeRatio;
				return contentY * Zoom + OffsetY;
			}
			else {
				var contentX = SwipeLeftOffset + SwipeDisplayWidth * SwipeRatio;
				return contentX * Zoom + OffsetX;
			}
		}

		private bool IsNearDivider(Point viewportPoint) {
			if (SwipeVertical) {
				var divY = SwipeTopOffset + SwipeDisplayHeight * SwipeRatio;
				var divViewportY = divY * Zoom + OffsetY;
				return Math.Abs(viewportPoint.Y - divViewportY) <= SwipeGrabDistance;
			}
			else {
				var divX = SwipeLeftOffset + SwipeDisplayWidth * SwipeRatio;
				var divViewportX = divX * Zoom + OffsetX;
				return Math.Abs(viewportPoint.X - divViewportX) <= SwipeGrabDistance;
			}
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

			if (IsAnySwipe && props.IsLeftButtonPressed && IsNearDivider(e.GetPosition(this))) {
				_swiping = true;
				UpdateSwipeFromPointer(e);
				e.Pointer.Capture(this);
				e.Handled = true;
				return;
			}

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
			if (_swiping) {
				_swiping = false;
				e.Pointer.Capture(null);
				e.Handled = true;
				return;
			}
			if (_panning) {
				_panning = false;
				e.Pointer.Capture(null);
				e.Handled = true;
			}
		}

		private void OnMoved(object? s, PointerEventArgs e) {
			if (_swiping) {
				UpdateSwipeFromPointer(e);
				e.Handled = true;
				return;
			}
			if (_panning) {
				var p = e.GetPosition(this);
				var d = p - _pointerStart;
				OffsetX = _startX + d.X;
				OffsetY = _startY + d.Y;
				e.Handled = true;
				return;
			}

			if (IsAnySwipe) {
				var near = IsNearDivider(e.GetPosition(this));
				Cursor = near
					? new Cursor(SwipeVertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast)
					: Cursor.Default;
			}
		}

		private void UpdateSwipeFromPointer(PointerEventArgs e) {
			var p = e.GetPosition(this);
			if (SwipeVertical) {
				var contentY = (p.Y - OffsetY) / Zoom;
				var displayH = SwipeDisplayHeight;
				if (displayH <= 0) displayH = Math.Max(Bounds.Height, 1);
				SwipeRatio = Math.Clamp((contentY - SwipeTopOffset) / displayH, 0, 1);
			}
			else {
				var contentX = (p.X - OffsetX) / Zoom;
				var displayW = SwipeDisplayWidth;
				if (displayW <= 0) displayW = Math.Max(Bounds.Width, 1);
				SwipeRatio = Math.Clamp((contentX - SwipeLeftOffset) / displayW, 0, 1);
			}
		}
	}
}
