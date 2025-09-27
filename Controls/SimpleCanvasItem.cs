using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Photobooth.Controls
{
    /// <summary>
    /// Base class for simple canvas items that can be dragged, resized, and selected
    /// </summary>
    public abstract class SimpleCanvasItem : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler SelectionChanged;

        // Dependency properties
        public static readonly DependencyProperty LeftProperty =
            DependencyProperty.Register("Left", typeof(double), typeof(SimpleCanvasItem),
                new PropertyMetadata(0.0, OnPositionChanged));

        public static readonly DependencyProperty TopProperty =
            DependencyProperty.Register("Top", typeof(double), typeof(SimpleCanvasItem),
                new PropertyMetadata(0.0, OnPositionChanged));

        public static readonly DependencyProperty ZIndexProperty =
            DependencyProperty.Register("ZIndex", typeof(int), typeof(SimpleCanvasItem),
                new PropertyMetadata(0, OnZIndexChanged));

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register("IsSelected", typeof(bool), typeof(SimpleCanvasItem),
                new PropertyMetadata(false, OnIsSelectedChanged));

        public static readonly DependencyProperty RotationAngleProperty =
            DependencyProperty.Register("RotationAngle", typeof(double), typeof(SimpleCanvasItem),
                new PropertyMetadata(0.0, OnRotationAngleChanged));

        // Properties
        // Backing fields for direct access during dragging
        private double _left = 0;
        private double _top = 0;

        public double Left
        {
            get => _left;
            set
            {
                if (_isBatchUpdating)
                {
                    _left = value;
                }
                else
                {
                    SetValue(LeftProperty, value);
                }
            }
        }

        public double Top
        {
            get => _top;
            set
            {
                if (_isBatchUpdating)
                {
                    _top = value;
                }
                else
                {
                    SetValue(TopProperty, value);
                }
            }
        }

        public int ZIndex
        {
            get => (int)GetValue(ZIndexProperty);
            set => SetValue(ZIndexProperty, value);
        }

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public double RotationAngle
        {
            get => (double)GetValue(RotationAngleProperty);
            set => SetValue(RotationAngleProperty, value);
        }

        public bool IsAspectRatioLocked
        {
            get => _isAspectRatioLocked;
            set
            {
                if (_isAspectRatioLocked != value)
                {
                    _isAspectRatioLocked = value;
                    if (value && Width > 0 && Height > 0)
                    {
                        _aspectRatio = Width / Height;
                    }
                    OnPropertyChanged(nameof(IsAspectRatioLocked));
                }
            }
        }

        // Protected fields for manipulation
        protected bool _isDragging;
        protected bool _isResizing;
        protected Point _dragStartPoint;
        protected Point _initialPosition;
        protected Size _initialSize;
        protected ResizeHandle _resizeHandle = ResizeHandle.None;
        protected bool _isAspectRatioLocked = false;
        protected double _aspectRatio = 1.0;

        // Performance optimization
        private DateTime _lastHandleUpdate = DateTime.MinValue;
        private const int HANDLE_UPDATE_THROTTLE_MS = 16; // ~60fps max
        private bool _isBatchUpdating = false;
        private bool _pendingHandleUpdate = false;

        // Selection handles
        protected Rectangle[] _selectionHandles;
        protected Ellipse _rotateHandle;
        protected Line _rotateHandleLine;
        protected readonly double HandleSize = 24; // Larger size for better touch
        protected readonly double RotateHandleDistance = 36; // Slightly increased to clear larger handles

        public enum ResizeHandle
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Top,
            Bottom,
            Left,
            Right
        }

        protected SimpleCanvasItem()
        {
            InitializeItem();
        }

        protected virtual void InitializeItem()
        {
            // Set default size
            Width = 100;
            Height = 100;

            // Enable manipulation for touch
            IsManipulationEnabled = true;

            // Create selection handles
            CreateSelectionHandles();
            CreateRotateHandle();

            // Apply rotation transform
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new RotateTransform(0);

            // Wire up mouse events
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            MouseMove += OnMouseMove;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;

            // Wire up touch events
            TouchDown += OnTouchDown;
            TouchUp += OnTouchUp;
            TouchMove += OnTouchMove;
            ManipulationStarting += OnManipulationStarting;
            ManipulationDelta += OnManipulationDelta;
            ManipulationCompleted += OnManipulationCompleted;
        }

        protected virtual void CreateSelectionHandles()
        {
            _selectionHandles = new Rectangle[8];

            for (int i = 0; i < 8; i++)
            {
                var handle = new Rectangle
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Fill = Brushes.DodgerBlue,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = true,
                    Opacity = 0.8,
                    Cursor = GetResizeCursor((ResizeHandle)(i + 1))
                };

                // Add handle events
                handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
                handle.MouseLeftButtonUp += Handle_MouseLeftButtonUp;
                handle.MouseMove += Handle_MouseMove;
                handle.Tag = (ResizeHandle)(i + 1);

                _selectionHandles[i] = handle;
            }
        }

        protected virtual void CreateRotateHandle()
        {
            // Create rotate handle (circle)
            _rotateHandle = new Ellipse
            {
                Width = HandleSize + 4,
                Height = HandleSize + 4,
                Fill = Brushes.Orange,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = true,
                Opacity = 0.9,
                Cursor = Cursors.Hand
            };

            // Add rotation events
            _rotateHandle.MouseLeftButtonDown += RotateHandle_MouseLeftButtonDown;
            _rotateHandle.MouseLeftButtonUp += RotateHandle_MouseLeftButtonUp;
            _rotateHandle.MouseMove += RotateHandle_MouseMove;

            // Create line connecting rotate handle to item
            _rotateHandleLine = new Line
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
        }

        // Rotation handle events
        private bool _isRotating;
        private Point _rotateStartPoint;
        private double _initialRotation;

        protected virtual void RotateHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isRotating = true;
            _rotateStartPoint = e.GetPosition(Parent as UIElement);
            _initialRotation = RotationAngle;

            (_rotateHandle as UIElement)?.CaptureMouse();
            e.Handled = true;
        }

        protected virtual void RotateHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isRotating)
            {
                _isRotating = false;
                (_rotateHandle as UIElement)?.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        protected virtual void RotateHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isRotating && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(Parent as UIElement);

                // Calculate center of item
                var centerX = Left + Width / 2;
                var centerY = Top + Height / 2;

                // Calculate angles
                var angle1 = Math.Atan2(_rotateStartPoint.Y - centerY, _rotateStartPoint.X - centerX);
                var angle2 = Math.Atan2(currentPoint.Y - centerY, currentPoint.X - centerX);

                // Calculate rotation difference in degrees
                var angleDiff = (angle2 - angle1) * 180 / Math.PI;

                // Apply rotation
                RotationAngle = _initialRotation + angleDiff;

                // Throttle handle updates during rotation
                ThrottledUpdateSelectionHandles();

                e.Handled = true;
            }
        }

        protected virtual Cursor GetResizeCursor(ResizeHandle handle)
        {
            switch (handle)
            {
                case ResizeHandle.TopLeft:
                case ResizeHandle.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeHandle.TopRight:
                case ResizeHandle.BottomLeft:
                    return Cursors.SizeNESW;
                case ResizeHandle.Top:
                case ResizeHandle.Bottom:
                    return Cursors.SizeNS;
                case ResizeHandle.Left:
                case ResizeHandle.Right:
                    return Cursors.SizeWE;
                default:
                    return Cursors.Arrow;
            }
        }

        protected virtual void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Don't set IsSelected here - the canvas will handle selection
            // through its Item_MouseLeftButtonDown handler

            // Start dragging immediately
            _isDragging = true;
            _dragStartPoint = e.GetPosition(Parent as UIElement);
            _initialPosition = new Point(Left, Top);

            CaptureMouse();
            // Don't mark as handled so canvas can also handle the event for selection
            // e.Handled = true;
        }

        protected virtual void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();

                // Sync the dependency properties after drag ends
                SetValue(LeftProperty, _left);
                SetValue(TopProperty, _top);

                e.Handled = true;
            }
        }

        protected virtual void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(Parent as UIElement);
                var deltaX = currentPoint.X - _dragStartPoint.X;
                var deltaY = currentPoint.Y - _dragStartPoint.Y;

                // Batch update to avoid multiple property change callbacks
                BeginBatchUpdate();

                var newLeft = _initialPosition.X + deltaX;
                var newTop = _initialPosition.Y + deltaY;

                // Directly update canvas position without going through dependency properties during drag
                if (Parent is Canvas canvas)
                {
                    // Clamp values
                    double cw = !double.IsNaN(canvas.Width) && canvas.Width > 0 ? canvas.Width : canvas.ActualWidth;
                    double ch = !double.IsNaN(canvas.Height) && canvas.Height > 0 ? canvas.Height : canvas.ActualHeight;

                    if (cw > 0 && ch > 0)
                    {
                        double maxLeft = Math.Max(0, cw - Width);
                        double maxTop = Math.Max(0, ch - Height);
                        if (newLeft < 0) newLeft = 0; else if (newLeft > maxLeft) newLeft = maxLeft;
                        if (newTop < 0) newTop = 0; else if (newTop > maxTop) newTop = maxTop;
                    }

                    // Update position directly on canvas for smooth movement
                    Canvas.SetLeft(this, newLeft);
                    Canvas.SetTop(this, newTop);

                    // Update properties without triggering callbacks
                    _left = newLeft;
                    _top = newTop;
                }

                EndBatchUpdate();

                // Throttle handle updates for performance
                ThrottledUpdateSelectionHandles();

                e.Handled = true;
            }
        }

        protected virtual void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isDragging && !_isResizing)
            {
                Cursor = Cursors.SizeAll;
            }
        }

        protected virtual void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDragging && !_isResizing)
            {
                Cursor = Cursors.Arrow;
            }
        }

        // Touch event handlers for better touch support
        protected virtual void OnTouchDown(object sender, TouchEventArgs e)
        {
            // Don't set IsSelected here - the canvas will handle selection
            // through its touch handling

            // Capture the touch
            CaptureTouch(e.TouchDevice);
            // Don't mark as handled so canvas can also handle the event for selection
            // e.Handled = true;
        }

        protected virtual void OnTouchUp(object sender, TouchEventArgs e)
        {
            ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        protected virtual void OnTouchMove(object sender, TouchEventArgs e)
        {
            // Touch movement is handled through manipulation events
            e.Handled = true;
        }

        protected virtual void OnManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = Parent as UIElement;
            _initialPosition = new Point(Left, Top);
            _initialSize = new Size(Width, Height);

            // Allow all manipulations (translate, scale, rotate)
            e.Mode = ManipulationModes.All;
            e.Handled = true;
        }

        protected virtual void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle translation (movement)
            if (e.DeltaManipulation.Translation.X != 0 || e.DeltaManipulation.Translation.Y != 0)
            {
                Left += e.DeltaManipulation.Translation.X;
                Top += e.DeltaManipulation.Translation.Y;
                ClampPositionToCanvas();
            }

            // Handle scaling (pinch to resize)
            if (e.DeltaManipulation.Scale.X != 1 || e.DeltaManipulation.Scale.Y != 1)
            {
                Width = Math.Max(20, Width * e.DeltaManipulation.Scale.X);
                Height = Math.Max(20, Height * e.DeltaManipulation.Scale.Y);
                ClampBoundsToCanvas();
                // Throttle handle updates for performance
                ThrottledUpdateSelectionHandles();
            }

            e.Handled = true;
        }

        protected virtual void OnManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            e.Handled = true;
        }

        protected virtual void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var handle = sender as Rectangle;
            _resizeHandle = (ResizeHandle)handle.Tag;
            _isResizing = true;
            _dragStartPoint = e.GetPosition(Parent as UIElement);
            _initialPosition = new Point(Left, Top);
            _initialSize = new Size(Width, Height);

            handle.CaptureMouse();
            e.Handled = true;
        }

        protected virtual void Handle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                _resizeHandle = ResizeHandle.None;
                (sender as Rectangle)?.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        protected virtual void Handle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(Parent as UIElement);
                var deltaX = currentPoint.X - _dragStartPoint.X;
                var deltaY = currentPoint.Y - _dragStartPoint.Y;

                ResizeItem(deltaX, deltaY);
                // Throttle handle updates for performance
                ThrottledUpdateSelectionHandles();
                e.Handled = true;
            }
        }

        protected virtual void ResizeItem(double deltaX, double deltaY)
        {
            if (_isAspectRatioLocked)
            {
                // When aspect ratio is locked, resize proportionally
                switch (_resizeHandle)
                {
                    case ResizeHandle.TopLeft:
                    case ResizeHandle.TopRight:
                    case ResizeHandle.BottomLeft:
                    case ResizeHandle.BottomRight:
                        // For corner handles, use the larger delta to maintain aspect ratio
                        var avgDelta = (Math.Abs(deltaX) > Math.Abs(deltaY)) ? deltaX : deltaY * _aspectRatio;

                        if (_resizeHandle == ResizeHandle.TopLeft)
                        {
                            var newWidth = Math.Max(20, _initialSize.Width - avgDelta);
                            var newHeight = newWidth / _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                            Left = _initialPosition.X + (_initialSize.Width - newWidth);
                            Top = _initialPosition.Y + (_initialSize.Height - newHeight);
                        }
                        else if (_resizeHandle == ResizeHandle.TopRight)
                        {
                            var newWidth = Math.Max(20, _initialSize.Width + avgDelta);
                            var newHeight = newWidth / _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                            Top = _initialPosition.Y + (_initialSize.Height - newHeight);
                        }
                        else if (_resizeHandle == ResizeHandle.BottomLeft)
                        {
                            var newWidth = Math.Max(20, _initialSize.Width - avgDelta);
                            var newHeight = newWidth / _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                            Left = _initialPosition.X + (_initialSize.Width - newWidth);
                        }
                        else if (_resizeHandle == ResizeHandle.BottomRight)
                        {
                            var newWidth = Math.Max(20, _initialSize.Width + avgDelta);
                            var newHeight = newWidth / _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                        }
                        break;

                    case ResizeHandle.Left:
                    case ResizeHandle.Right:
                        // Resize width and adjust height to maintain ratio
                        if (_resizeHandle == ResizeHandle.Left)
                        {
                            var newWidth = Math.Max(20, _initialSize.Width - deltaX);
                            var newHeight = newWidth / _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                            Left = _initialPosition.X + (_initialSize.Width - newWidth);
                            Top = _initialPosition.Y + (_initialSize.Height - newHeight) / 2;
                        }
                        else
                        {
                            var newWidth = Math.Max(20, _initialSize.Width + deltaX);
                            var newHeight = newWidth / _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                            Top = _initialPosition.Y + (_initialSize.Height - newHeight) / 2;
                        }
                        break;

                    case ResizeHandle.Top:
                    case ResizeHandle.Bottom:
                        // Resize height and adjust width to maintain ratio
                        if (_resizeHandle == ResizeHandle.Top)
                        {
                            var newHeight = Math.Max(20, _initialSize.Height - deltaY);
                            var newWidth = newHeight * _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                            Top = _initialPosition.Y + (_initialSize.Height - newHeight);
                            Left = _initialPosition.X + (_initialSize.Width - newWidth) / 2;
                        }
                        else
                        {
                            var newHeight = Math.Max(20, _initialSize.Height + deltaY);
                            var newWidth = newHeight * _aspectRatio;
                            Width = newWidth;
                            Height = newHeight;
                            Left = _initialPosition.X + (_initialSize.Width - newWidth) / 2;
                        }
                        break;
                }
            }
            else
            {
                // Normal resize without aspect ratio lock
                switch (_resizeHandle)
                {
                    case ResizeHandle.TopLeft:
                        var newWidth = Math.Max(20, _initialSize.Width - deltaX);
                        var newHeight = Math.Max(20, _initialSize.Height - deltaY);
                        Width = newWidth;
                        Height = newHeight;
                        Left = _initialPosition.X + (_initialSize.Width - newWidth);
                        Top = _initialPosition.Y + (_initialSize.Height - newHeight);
                        break;

                    case ResizeHandle.TopRight:
                        Width = Math.Max(20, _initialSize.Width + deltaX);
                        Height = Math.Max(20, _initialSize.Height - deltaY);
                        Top = _initialPosition.Y + (_initialSize.Height - Height);
                        break;

                    case ResizeHandle.BottomLeft:
                        Width = Math.Max(20, _initialSize.Width - deltaX);
                        Height = Math.Max(20, _initialSize.Height + deltaY);
                        Left = _initialPosition.X + (_initialSize.Width - Width);
                        break;

                    case ResizeHandle.BottomRight:
                        Width = Math.Max(20, _initialSize.Width + deltaX);
                        Height = Math.Max(20, _initialSize.Height + deltaY);
                        break;

                    case ResizeHandle.Top:
                        Height = Math.Max(20, _initialSize.Height - deltaY);
                        Top = _initialPosition.Y + (_initialSize.Height - Height);
                        break;

                    case ResizeHandle.Bottom:
                        Height = Math.Max(20, _initialSize.Height + deltaY);
                        break;

                    case ResizeHandle.Left:
                        Width = Math.Max(20, _initialSize.Width - deltaX);
                        Left = _initialPosition.X + (_initialSize.Width - Width);
                        break;

                    case ResizeHandle.Right:
                        Width = Math.Max(20, _initialSize.Width + deltaX);
                        break;
                }
            }
            ClampBoundsToCanvas();
            // Update selection handles after any size/position changes
            UpdateSelectionHandles();
        }

        public virtual void UpdateSelectionHandles()
        {
            if (_selectionHandles == null) return;

            var visibility = IsSelected ? Visibility.Visible : Visibility.Collapsed;

            // Only update visibility if it changed
            if (_selectionHandles[0].Visibility != visibility)
            {
                foreach (var handle in _selectionHandles)
                {
                    handle.Visibility = visibility;
                    // Only set z-index once when visibility changes
                    if (visibility == Visibility.Visible)
                        Panel.SetZIndex(handle, 10000);
                }

                // Update rotate handle visibility
                if (_rotateHandle != null)
                {
                    _rotateHandle.Visibility = visibility;
                    if (visibility == Visibility.Visible)
                        Panel.SetZIndex(_rotateHandle, 10001);
                }
                if (_rotateHandleLine != null)
                {
                    _rotateHandleLine.Visibility = visibility;
                    if (visibility == Visibility.Visible)
                        Panel.SetZIndex(_rotateHandleLine, 9999);
                }
            }

            if (IsSelected)
            {
                PositionSelectionHandles();
            }
        }

        private void ThrottledUpdateSelectionHandles()
        {
            if (_isBatchUpdating)
            {
                _pendingHandleUpdate = true;
                return;
            }

            var now = DateTime.Now;
            if ((now - _lastHandleUpdate).TotalMilliseconds >= HANDLE_UPDATE_THROTTLE_MS)
            {
                UpdateSelectionHandles();
                _lastHandleUpdate = now;
                _pendingHandleUpdate = false;
            }
        }

        private void BeginBatchUpdate()
        {
            _isBatchUpdating = true;
        }

        private void EndBatchUpdate()
        {
            _isBatchUpdating = false;

            if (_pendingHandleUpdate)
            {
                ThrottledUpdateSelectionHandles();
            }
        }

        protected virtual void PositionSelectionHandles()
        {
            if (_selectionHandles == null || !IsSelected) return;

            // Only position handles if they're visible
            if (_selectionHandles[0].Visibility != Visibility.Visible)
                return;

            var halfHandle = HandleSize / 2;

            // Position handles around the item
            Canvas.SetLeft(_selectionHandles[0], Left - halfHandle); // TopLeft
            Canvas.SetTop(_selectionHandles[0], Top - halfHandle);

            Canvas.SetLeft(_selectionHandles[1], Left + Width - halfHandle); // TopRight
            Canvas.SetTop(_selectionHandles[1], Top - halfHandle);

            Canvas.SetLeft(_selectionHandles[2], Left - halfHandle); // BottomLeft
            Canvas.SetTop(_selectionHandles[2], Top + Height - halfHandle);

            Canvas.SetLeft(_selectionHandles[3], Left + Width - halfHandle); // BottomRight
            Canvas.SetTop(_selectionHandles[3], Top + Height - halfHandle);

            Canvas.SetLeft(_selectionHandles[4], Left + Width / 2 - halfHandle); // Top
            Canvas.SetTop(_selectionHandles[4], Top - halfHandle);

            Canvas.SetLeft(_selectionHandles[5], Left + Width / 2 - halfHandle); // Bottom
            Canvas.SetTop(_selectionHandles[5], Top + Height - halfHandle);

            Canvas.SetLeft(_selectionHandles[6], Left - halfHandle); // Left
            Canvas.SetTop(_selectionHandles[6], Top + Height / 2 - halfHandle);

            Canvas.SetLeft(_selectionHandles[7], Left + Width - halfHandle); // Right
            Canvas.SetTop(_selectionHandles[7], Top + Height / 2 - halfHandle);

            // Position rotate handle above the top center
            if (_rotateHandle != null)
            {
                var rotateHandleHalf = (_rotateHandle.Width / 2);
                Canvas.SetLeft(_rotateHandle, Left + Width / 2 - rotateHandleHalf);
                Canvas.SetTop(_rotateHandle, Top - RotateHandleDistance - rotateHandleHalf);
            }

            // Position rotate handle line
            if (_rotateHandleLine != null)
            {
                _rotateHandleLine.X1 = Left + Width / 2;
                _rotateHandleLine.Y1 = Top;
                _rotateHandleLine.X2 = Left + Width / 2;
                _rotateHandleLine.Y2 = Top - RotateHandleDistance;
            }
        }

        public Rectangle[] GetSelectionHandles()
        {
            return _selectionHandles;
        }

        public UIElement[] GetAllHandles()
        {
            var handles = new List<UIElement>();
            if (_selectionHandles != null)
                handles.AddRange(_selectionHandles);
            if (_rotateHandleLine != null)
                handles.Add(_rotateHandleLine);
            if (_rotateHandle != null)
                handles.Add(_rotateHandle);
            return handles.ToArray();
        }

        // Property change notifications
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleCanvasItem item)
            {
                // Update backing fields
                if (e.Property == LeftProperty)
                    item._left = (double)e.NewValue;
                else if (e.Property == TopProperty)
                    item._top = (double)e.NewValue;

                // Only update canvas position if not batch updating (dragging)
                if (!item._isBatchUpdating)
                {
                    item.UpdateCanvasPosition();
                    item.ThrottledUpdateSelectionHandles();
                }
                item.OnPropertyChanged(e.Property.Name);
            }
        }

        // Constrain position inside parent canvas bounds
        protected void ClampPositionToCanvas()
        {
            if (!(Parent is Canvas canvas)) return;
            double cw = !double.IsNaN(canvas.Width) && canvas.Width > 0 ? canvas.Width : canvas.ActualWidth;
            double ch = !double.IsNaN(canvas.Height) && canvas.Height > 0 ? canvas.Height : canvas.ActualHeight;
            if (cw <= 0 || ch <= 0) return;

            double maxLeft = Math.Max(0, cw - Width);
            double maxTop = Math.Max(0, ch - Height);
            if (Left < 0) Left = 0; else if (Left > maxLeft) Left = maxLeft;
            if (Top < 0) Top = 0; else if (Top > maxTop) Top = maxTop;
        }

        // Constrain both size and position to keep fully inside parent canvas
        protected void ClampBoundsToCanvas()
        {
            if (!(Parent is Canvas canvas)) return;
            double cw = !double.IsNaN(canvas.Width) && canvas.Width > 0 ? canvas.Width : canvas.ActualWidth;
            double ch = !double.IsNaN(canvas.Height) && canvas.Height > 0 ? canvas.Height : canvas.ActualHeight;
            if (cw <= 0 || ch <= 0) return;

            if (Width > cw) Width = Math.Max(20, cw);
            if (Height > ch) Height = Math.Max(20, ch);

            if (Left < 0) Left = 0;
            if (Top < 0) Top = 0;
            if (Left + Width > cw) Width = Math.Max(20, cw - Left);
            if (Top + Height > ch) Height = Math.Max(20, ch - Top);
        }

        private static void OnZIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleCanvasItem item)
            {
                Panel.SetZIndex(item, (int)e.NewValue);
                item.OnPropertyChanged("ZIndex");
            }
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleCanvasItem item)
            {
                item.UpdateSelectionHandles();
                item.SelectionChanged?.Invoke(item, EventArgs.Empty);
                item.OnPropertyChanged("IsSelected");
            }
        }

        private static void OnRotationAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleCanvasItem item)
            {
                var angle = (double)e.NewValue;
                if (item.RenderTransform is RotateTransform rotate)
                {
                    rotate.Angle = angle;
                }
                else
                {
                    item.RenderTransform = new RotateTransform(angle);
                }
                item.UpdateSelectionHandles();
                item.OnPropertyChanged("RotationAngle");
            }
        }

        protected virtual void UpdateCanvasPosition()
        {
            if (Parent is Canvas canvas)
            {
                Canvas.SetLeft(this, Left);
                Canvas.SetTop(this, Top);
            }
        }

        // Abstract methods to be implemented by derived classes
        public abstract string GetDisplayName();
        public abstract SimpleCanvasItem Clone();
    }
}
