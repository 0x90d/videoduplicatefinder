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
			this.GetPropertyChangedObservable(ZoomProperty).Subscribe(_ => OnZoomChanged());
			this.GetPropertyChangedObservable(OffsetXProperty).Subscribe(_ => ApplyTransform());
			this.GetPropertyChangedObservable(OffsetYProperty).Subscribe(_ => ApplyTransform());
		}

		private void OnZoomChanged() {
			// A programmatic zoom (the Zoom-In/Out/Fit toolbar commands set Zoom directly) must
			// re-clamp the pan offsets too: an offset that was valid at the previous zoom can
			// push the now-differently-scaled content out of the viewport, leaving blank space.
			// The wheel and drag handlers already clamp as they compute offsets; this covers
			// every other write to Zoom.
			Point clamped = ClampOffsetsToViewport(OffsetX, OffsetY, Math.Clamp(Zoom, MinZoom, MaxZoom));
			if (clamped.X != OffsetX) OffsetX = clamped.X;
			if (clamped.Y != OffsetY) OffsetY = clamped.Y;
			ApplyTransform();
		}

		private void AttachTransform() {
			if (Content is Visual v) {
				_group ??= new TransformGroup { Children = { _scale, _translate } };
				// Avalonia defaults RenderTransformOrigin to the center. All offset math
				// here is expressed from the content's top-left corner, so the default
				// origin adds a hidden second translation that makes repeated wheel
				// zoom drift down/right.
				v.RenderTransformOrigin = RelativePoint.TopLeft;
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

		// Avalonia converts the pointer position through the content's inverse render
		// transform, giving the exact content pixel under the cursor at any zoom/offset.
		private Point GetContentPosition(PointerEventArgs e) =>
			Content is Visual contentVisual ? e.GetPosition(contentVisual) : e.GetPosition(this);

		internal static double ClampOffsetToViewport(double offset, double viewportLength, double contentLength, double zoom) {
			if (viewportLength <= 0 || contentLength <= 0 || !double.IsFinite(offset) || !double.IsFinite(zoom))
				return offset;

			double scaledLength = contentLength * zoom;
			// Content smaller than the viewport is centered instead of pannable.
			if (scaledLength <= viewportLength)
				return (viewportLength - scaledLength) / 2d;

			return Math.Clamp(offset, viewportLength - scaledLength, 0d);
		}

		private Point ClampOffsetsToViewport(double offsetX, double offsetY, double zoom) {
			if (Content is not Visual contentVisual)
				return new Point(offsetX, offsetY);
			return new Point(
				ClampOffsetToViewport(offsetX, Bounds.Width, contentVisual.Bounds.Width, zoom),
				ClampOffsetToViewport(offsetY, Bounds.Height, contentVisual.Bounds.Height, zoom));
		}

		private bool IsNearDivider(PointerEventArgs e) {
			Point contentPoint = GetContentPosition(e);
			// The grab margin is a screen-space distance; translate it into content
			// space so the divider stays equally grabbable at any zoom level.
			double contentGrabDistance = SwipeGrabDistance / Math.Max(Math.Abs(Zoom), 0.0001);
			if (SwipeVertical) {
				var divY = SwipeTopOffset + SwipeDisplayHeight * SwipeRatio;
				return Math.Abs(contentPoint.Y - divY) <= contentGrabDistance;
			}
			else {
				var divX = SwipeLeftOffset + SwipeDisplayWidth * SwipeRatio;
				return Math.Abs(contentPoint.X - divX) <= contentGrabDistance;
			}
		}

		private void OnWheel(object? s, PointerWheelEventArgs e) {
			var oldZoom = Math.Clamp(Zoom, MinZoom, MaxZoom);
			var factor = e.Delta.Y > 0 ? 1.1 : (1.0 / 1.1);
			var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
			if (Math.Abs(newZoom - oldZoom) < 0.000001d) {
				e.Handled = true;
				return;
			}

			// Keep the content pixel under the cursor stationary while zooming.
			Point contentPoint = GetContentPosition(e);
			Point viewportPoint = e.GetPosition(this);
			Point offsets = ClampOffsetsToViewport(
				viewportPoint.X - contentPoint.X * newZoom,
				viewportPoint.Y - contentPoint.Y * newZoom,
				newZoom);

			Zoom = newZoom;
			OffsetX = offsets.X;
			OffsetY = offsets.Y;
			e.Handled = true;
		}

		private void OnPressed(object? s, PointerPressedEventArgs e) {
			var props = e.GetCurrentPoint(this).Properties;

			if (IsAnySwipe && props.IsLeftButtonPressed && IsNearDivider(e)) {
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
				Point offsets = ClampOffsetsToViewport(_startX + d.X, _startY + d.Y, Math.Clamp(Zoom, MinZoom, MaxZoom));
				OffsetX = offsets.X;
				OffsetY = offsets.Y;
				e.Handled = true;
				return;
			}

			if (IsAnySwipe) {
				var near = IsNearDivider(e);
				Cursor = near
					? new Cursor(SwipeVertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast)
					: Cursor.Default;
			}
		}

		private void UpdateSwipeFromPointer(PointerEventArgs e) {
			Point contentPoint = GetContentPosition(e);
			if (SwipeVertical) {
				var displayH = SwipeDisplayHeight;
				if (displayH <= 0) displayH = Math.Max(Bounds.Height, 1);
				SwipeRatio = Math.Clamp((contentPoint.Y - SwipeTopOffset) / displayH, 0, 1);
			}
			else {
				var displayW = SwipeDisplayWidth;
				if (displayW <= 0) displayW = Math.Max(Bounds.Width, 1);
				SwipeRatio = Math.Clamp((contentPoint.X - SwipeLeftOffset) / displayW, 0, 1);
			}
		}
	}
}
