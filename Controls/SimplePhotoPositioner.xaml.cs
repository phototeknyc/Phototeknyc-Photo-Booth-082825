using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Photobooth.Models;

namespace Photobooth.Controls
{
    /// <summary>
    /// Ultra-simple photo positioning control - just drag and resize
    /// </summary>
    public partial class SimplePhotoPositioner : UserControl
    {
        private bool _isDragging = false;
        private bool _isResizing = false;
        private Point _clickPosition;
        private Point _startResizePosition;
        private double _startWidth;
        private double _startHeight;
        private PhotoPlacementData _placementData;
        private Dictionary<int, Point> _touchPoints = new Dictionary<int, Point>();
        private double _canvasWidth;
        private double _canvasHeight;
        private bool _isUpdatingSlider = false; // Prevent infinite loop
        private double _defaultAspectRatio = 1.5; // Default 3:2 aspect ratio for photos
        private double _photoAspectRatio = 1.5; // Actual camera photo aspect ratio (set separately)
        private Rect _backgroundBounds; // Actual background image display bounds
        private double _backgroundImageWidth = 0; // Actual background image pixel width
        private double _backgroundImageHeight = 0; // Actual background image pixel height

        public event EventHandler<PhotoPlacementData> PositionChanged;

        public SimplePhotoPositioner()
        {
            InitializeComponent();
            _placementData = new PhotoPlacementData
            {
                MaintainAspectRatio = true, // Always maintain aspect ratio
                DefaultAspectRatio = _defaultAspectRatio,
                PlacementZones = new List<PhotoPlacementZone>()
            };
            _touchPoints = new Dictionary<int, Point>();

            // Always lock aspect ratio for background removal workflow
            this.Loaded += (sender, e) =>
            {
                if (LockAspectRatio != null)
                {
                    LockAspectRatio.IsChecked = true;
                    LockAspectRatio.IsEnabled = false; // Disable to prevent unchecking
                }
            };
        }

        /// <summary>
        /// Set the photo aspect ratio (from camera/captured photos)
        /// </summary>
        public void SetPhotoAspectRatio(double aspectRatio)
        {
            if (aspectRatio > 0)
            {
                _photoAspectRatio = aspectRatio;
                _defaultAspectRatio = aspectRatio;

                // Update photo zone if it exists
                if (PhotoZone != null && _canvasWidth > 0)
                {
                    // Maintain current width, adjust height for new aspect ratio
                    var currentWidth = PhotoZone.Width;
                    PhotoZone.Height = currentWidth / _defaultAspectRatio;
                }

                System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] Photo aspect ratio set to: {aspectRatio:F3}");
            }
        }

        /// <summary>
        /// Set the background image path
        /// </summary>
        public void SetBackground(string imagePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(imagePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] SetBackground called with: {imagePath}");

                    // Try to load as absolute path first
                    Uri imageUri;
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] Loading as absolute path: {imagePath}");
                        imageUri = new Uri(imagePath, UriKind.Absolute);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] File not found, trying as relative path: {imagePath}");
                        // Try as relative path
                        imageUri = new Uri(imagePath, UriKind.Relative);
                    }

                    var bitmapImage = new BitmapImage(imageUri);
                    BackgroundImage.Source = bitmapImage;
                    System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] Background image set successfully");

                    // DO NOT change the photo aspect ratio based on background image!
                    // The photo aspect ratio should come from the camera/captured photos
                    if (bitmapImage.PixelWidth > 0 && bitmapImage.PixelHeight > 0)
                    {
                        // Store the actual background image dimensions
                        _backgroundImageWidth = bitmapImage.PixelWidth;
                        _backgroundImageHeight = bitmapImage.PixelHeight;

                        System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] Background image dimensions: {_backgroundImageWidth}x{_backgroundImageHeight}");

                        // Calculate actual background display bounds
                        CalculateBackgroundBounds();

                        // Set photo zone to proportional size (similar to live view template)
                        // Use 60% of width for photo area (leaving room for UI elements)
                        SetDefaultPhotoZoneSize();
                    }
                    else
                    {
                        // Position the photo zone in center by default
                        UpdateCanvasSize();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load background: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate the actual bounds of the background image display
        /// </summary>
        private void CalculateBackgroundBounds()
        {
            if (BackgroundImage?.Source == null || PositioningCanvas == null)
            {
                _backgroundBounds = new Rect(0, 0, _canvasWidth, _canvasHeight);
                return;
            }

            var imageSource = BackgroundImage.Source as BitmapImage;
            if (imageSource == null)
            {
                _backgroundBounds = new Rect(0, 0, _canvasWidth, _canvasHeight);
                return;
            }

            // Get canvas dimensions
            var canvasWidth = PositioningCanvas.ActualWidth;
            var canvasHeight = PositioningCanvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                _backgroundBounds = new Rect(0, 0, 100, 100);
                return;
            }

            // Calculate how the background image fits with Stretch="Uniform"
            var imageAspect = (double)imageSource.PixelWidth / imageSource.PixelHeight;
            var canvasAspect = canvasWidth / canvasHeight;

            double displayWidth, displayHeight;
            double offsetX = 0, offsetY = 0;

            if (imageAspect > canvasAspect)
            {
                // Image is wider - fit to width
                displayWidth = canvasWidth;
                displayHeight = canvasWidth / imageAspect;
                offsetY = (canvasHeight - displayHeight) / 2;
            }
            else
            {
                // Image is taller - fit to height
                displayHeight = canvasHeight;
                displayWidth = canvasHeight * imageAspect;
                offsetX = (canvasWidth - displayWidth) / 2;
            }

            _backgroundBounds = new Rect(offsetX, offsetY, displayWidth, displayHeight);

            System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] CalculateBackgroundBounds:");
            System.Diagnostics.Debug.WriteLine($"  Canvas: {canvasWidth:F1} x {canvasHeight:F1}");
            System.Diagnostics.Debug.WriteLine($"  Image: {imageSource.PixelWidth} x {imageSource.PixelHeight}");
            System.Diagnostics.Debug.WriteLine($"  Background bounds: X={offsetX:F1}, Y={offsetY:F1}, W={displayWidth:F1}, H={displayHeight:F1}");
        }

        /// <summary>
        /// Set photo zone to default size matching background proportions
        /// </summary>
        private void SetDefaultPhotoZoneSize()
        {
            if (PositioningCanvas == null || PhotoZone == null || SizeSlider == null)
                return;

            if (PositioningCanvas.ActualWidth <= 0 || PositioningCanvas.ActualHeight <= 0)
            {
                // Canvas not ready yet, try again when it's sized
                UpdateCanvasSize();
                return;
            }

            // Calculate zone size as 60% of background width (similar to live view template)
            var zoneWidth = _backgroundBounds.Width * 0.6;
            var zoneHeight = zoneWidth / _defaultAspectRatio;

            // Ensure it fits within the background height
            if (zoneHeight > _backgroundBounds.Height * 0.7)
            {
                zoneHeight = _backgroundBounds.Height * 0.7;
                zoneWidth = zoneHeight * _defaultAspectRatio;
            }

            // Set the zone size
            PhotoZone.Width = zoneWidth;
            PhotoZone.Height = zoneHeight;

            // Center the photo zone in canvas
            var left = (_canvasWidth / 2) - (zoneWidth / 2);
            var top = (_canvasHeight / 2) - (zoneHeight / 2);

            // Ensure it's within bounds
            left = Math.Max(0, Math.Min(left, _canvasWidth - zoneWidth));
            top = Math.Max(0, Math.Min(top, _canvasHeight - zoneHeight));

            Canvas.SetLeft(PhotoZone, left);
            Canvas.SetTop(PhotoZone, top);

            // Update slider to match
            _isUpdatingSlider = true;
            double referenceWidth = _backgroundBounds.Width > 0 ? _backgroundBounds.Width : PositioningCanvas.ActualWidth;
            SizeSlider.Value = zoneWidth / referenceWidth;
            _isUpdatingSlider = false;
        }

        /// <summary>
        /// Get the current placement data
        /// </summary>
        public PhotoPlacementData GetPlacementData()
        {
            // Ensure _placementData and its zones list are initialized
            if (_placementData == null)
            {
                _placementData = new PhotoPlacementData();
            }

            if (_placementData.PlacementZones == null)
            {
                _placementData.PlacementZones = new List<PhotoPlacementZone>();
            }

            if (_placementData.PlacementZones.Count == 0)
            {
                _placementData.PlacementZones.Add(new PhotoPlacementZone());
            }

            var zone = _placementData.PlacementZones[0];

            // Calculate relative positions (0-1) based on the actual background display area
            if (_backgroundBounds.Width > 0 && _backgroundBounds.Height > 0 && PhotoZone != null)
            {
                var left = Canvas.GetLeft(PhotoZone);
                var top = Canvas.GetTop(PhotoZone);

                // Handle NaN values
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                System.Diagnostics.Debug.WriteLine($"[SimplePhotoPositioner] GetPlacementData calculations:");
                System.Diagnostics.Debug.WriteLine($"  PhotoZone position: Left={left:F1}, Top={top:F1}");
                System.Diagnostics.Debug.WriteLine($"  PhotoZone size: Width={PhotoZone.Width:F1}, Height={PhotoZone.Height:F1}");
                System.Diagnostics.Debug.WriteLine($"  Background bounds: X={_backgroundBounds.X:F1}, Y={_backgroundBounds.Y:F1}, W={_backgroundBounds.Width:F1}, H={_backgroundBounds.Height:F1}");

                // Calculate position relative to the background display bounds
                // This accounts for letterboxing when the background doesn't fill the canvas
                zone.X = (left - _backgroundBounds.X) / _backgroundBounds.Width;
                zone.Y = (top - _backgroundBounds.Y) / _backgroundBounds.Height;
                zone.Width = PhotoZone.Width / _backgroundBounds.Width;
                zone.Height = PhotoZone.Height / _backgroundBounds.Height;

                System.Diagnostics.Debug.WriteLine($"  Calculated zone: X={zone.X:F3}, Y={zone.Y:F3}, Width={zone.Width:F3}, Height={zone.Height:F3}");

                // Clamp values to valid range
                zone.X = Math.Max(0, Math.Min(1, zone.X));
                zone.Y = Math.Max(0, Math.Min(1, zone.Y));
                zone.Width = Math.Max(0.1, Math.Min(1, zone.Width));
                zone.Height = Math.Max(0.1, Math.Min(1, zone.Height));

                System.Diagnostics.Debug.WriteLine($"  Clamped zone: X={zone.X:F3}, Y={zone.Y:F3}, Width={zone.Width:F3}, Height={zone.Height:F3}");
            }
            else if (_canvasWidth > 0 && _canvasHeight > 0 && PhotoZone != null)
            {
                // Fallback to canvas dimensions if background bounds not available
                var left = Canvas.GetLeft(PhotoZone);
                var top = Canvas.GetTop(PhotoZone);

                // Handle NaN values
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                zone.X = left / _canvasWidth;
                zone.Y = top / _canvasHeight;
                zone.Width = PhotoZone.Width / _canvasWidth;
                zone.Height = PhotoZone.Height / _canvasHeight;
            }
            else
            {
                // Defaults
                zone.X = 0.25;
                zone.Y = 0.25;
                zone.Width = 0.5;
                zone.Height = 0.3;
            }

            zone.Name = "Photo Area";
            zone.PhotoIndex = 0;
            zone.IsEnabled = true;
            zone.Rotation = 0; // No rotation in simple mode

            // Always maintain aspect ratio for background removal workflow
            _placementData.MaintainAspectRatio = true;
            _placementData.DefaultAspectRatio = _defaultAspectRatio; // Store the aspect ratio

            // Store background dimensions for proper scaling during application
            _placementData.BackgroundWidth = _backgroundImageWidth > 0 ? _backgroundImageWidth : _canvasWidth;
            _placementData.BackgroundHeight = _backgroundImageHeight > 0 ? _backgroundImageHeight : _canvasHeight;

            return _placementData;
        }

        /// <summary>
        /// Set existing placement data
        /// </summary>
        public void SetPlacementData(PhotoPlacementData data)
        {
            if (data != null && data.PlacementZones != null && data.PlacementZones.Count > 0)
            {
                _placementData = data;

                // Use the aspect ratio from the data if available
                if (data.DefaultAspectRatio > 0)
                {
                    _defaultAspectRatio = data.DefaultAspectRatio;
                }

                var zone = data.PlacementZones[0];

                // Apply to UI - use background bounds if available
                double referenceWidth, referenceHeight, offsetX = 0, offsetY = 0;

                if (_backgroundBounds.Width > 0 && _backgroundBounds.Height > 0)
                {
                    // Use actual background display bounds
                    referenceWidth = _backgroundBounds.Width;
                    referenceHeight = _backgroundBounds.Height;
                    offsetX = _backgroundBounds.X;
                    offsetY = _backgroundBounds.Y;
                }
                else if (_canvasWidth > 0 && _canvasHeight > 0)
                {
                    // Fallback to canvas dimensions
                    referenceWidth = _canvasWidth;
                    referenceHeight = _canvasHeight;
                }
                else
                {
                    return; // Can't apply without dimensions
                }

                var left = zone.X * referenceWidth + offsetX;
                var top = zone.Y * referenceHeight + offsetY;
                var width = zone.Width * referenceWidth;
                var height = zone.Height * referenceHeight;

                // Always maintain aspect ratio
                if (data.MaintainAspectRatio != false) // Default to true
                {
                    height = width / _defaultAspectRatio;
                }

                Canvas.SetLeft(PhotoZone, left);
                Canvas.SetTop(PhotoZone, top);
                PhotoZone.Width = width;
                PhotoZone.Height = height;

                // Update slider without triggering event
                _isUpdatingSlider = true;
                SizeSlider.Value = zone.Width;
                _isUpdatingSlider = false;
            }
        }

        #region Mouse Events

        private void PhotoZone_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _clickPosition = e.GetPosition(PositioningCanvas);
                PhotoZone.CaptureMouse();
                e.Handled = true;
            }
        }

        private void PhotoZone_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(PositioningCanvas);
                var deltaX = currentPosition.X - _clickPosition.X;
                var deltaY = currentPosition.Y - _clickPosition.Y;

                var currentLeft = Canvas.GetLeft(PhotoZone);
                var currentTop = Canvas.GetTop(PhotoZone);

                // Handle NaN values
                if (double.IsNaN(currentLeft)) currentLeft = 0;
                if (double.IsNaN(currentTop)) currentTop = 0;

                var newLeft = currentLeft + deltaX;
                var newTop = currentTop + deltaY;

                // Keep within canvas bounds (0, 0, canvasWidth, canvasHeight)
                newLeft = Math.Max(0, Math.Min(newLeft, _canvasWidth - PhotoZone.Width));
                newTop = Math.Max(0, Math.Min(newTop, _canvasHeight - PhotoZone.Height));

                Canvas.SetLeft(PhotoZone, newLeft);
                Canvas.SetTop(PhotoZone, newTop);

                _clickPosition = currentPosition;
                FirePositionChanged();
            }
            else if (_isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                ResizePhotoZone(e.GetPosition(PositioningCanvas));
            }
        }

        private void PhotoZone_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            PhotoZone.ReleaseMouseCapture();
        }

        private void ResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isResizing = true;
                _startResizePosition = e.GetPosition(PositioningCanvas);
                _startWidth = PhotoZone.Width;
                _startHeight = PhotoZone.Height;
                var handle = sender as FrameworkElement;
                handle?.CaptureMouse();
                e.Handled = true;
            }
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                ResizePhotoZone(e.GetPosition(PositioningCanvas));
            }
        }

        private void ResizeHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isResizing = false;
            var handle = sender as FrameworkElement;
            handle?.ReleaseMouseCapture();
        }

        private void ResizePhotoZone(Point currentPosition)
        {
            var deltaX = currentPosition.X - _startResizePosition.X;
            var deltaY = currentPosition.Y - _startResizePosition.Y;

            var newWidth = Math.Max(50, _startWidth + deltaX);
            var newHeight = Math.Max(50, _startHeight + deltaY);

            // Maintain aspect ratio if locked
            if (LockAspectRatio.IsChecked == true)
            {
                var aspectRatio = _startWidth / _startHeight;
                // Use the larger change to maintain aspect ratio
                if (Math.Abs(deltaX) > Math.Abs(deltaY))
                {
                    newHeight = newWidth / aspectRatio;
                }
                else
                {
                    newWidth = newHeight * aspectRatio;
                }
            }

            // Constrain to background bounds
            var currentLeft = Canvas.GetLeft(PhotoZone);
            var currentTop = Canvas.GetTop(PhotoZone);

            var maxWidth = _backgroundBounds.X + _backgroundBounds.Width - currentLeft;
            var maxHeight = _backgroundBounds.Y + _backgroundBounds.Height - currentTop;

            newWidth = Math.Min(newWidth, maxWidth);
            newHeight = Math.Min(newHeight, maxHeight);

            PhotoZone.Width = newWidth;
            PhotoZone.Height = newHeight;

            // Update slider without triggering event
            _isUpdatingSlider = true;
            SizeSlider.Value = newWidth / PositioningCanvas.ActualWidth;
            _isUpdatingSlider = false;

            FirePositionChanged();
        }

        #endregion

        #region Touch Events

        private void PhotoZone_TouchDown(object sender, TouchEventArgs e)
        {
            var touchPoint = e.GetTouchPoint(PositioningCanvas);
            _touchPoints[e.TouchDevice.Id] = touchPoint.Position;
            PhotoZone.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void PhotoZone_TouchMove(object sender, TouchEventArgs e)
        {
            if (_touchPoints.ContainsKey(e.TouchDevice.Id))
            {
                var currentPos = e.GetTouchPoint(PositioningCanvas).Position;
                var startPos = _touchPoints[e.TouchDevice.Id];

                var deltaX = currentPos.X - startPos.X;
                var deltaY = currentPos.Y - startPos.Y;

                var currentLeft = Canvas.GetLeft(PhotoZone);
                var currentTop = Canvas.GetTop(PhotoZone);

                // Handle NaN values
                if (double.IsNaN(currentLeft)) currentLeft = 0;
                if (double.IsNaN(currentTop)) currentTop = 0;

                var newLeft = currentLeft + deltaX;
                var newTop = currentTop + deltaY;

                // Keep within canvas bounds (0, 0, canvasWidth, canvasHeight)
                newLeft = Math.Max(0, Math.Min(newLeft, _canvasWidth - PhotoZone.Width));
                newTop = Math.Max(0, Math.Min(newTop, _canvasHeight - PhotoZone.Height));

                Canvas.SetLeft(PhotoZone, newLeft);
                Canvas.SetTop(PhotoZone, newTop);

                _touchPoints[e.TouchDevice.Id] = currentPos;
                FirePositionChanged();
            }
        }

        private void PhotoZone_TouchUp(object sender, TouchEventArgs e)
        {
            _touchPoints.Remove(e.TouchDevice.Id);
            PhotoZone.ReleaseTouchCapture(e.TouchDevice);
        }

        private void ResizeHandle_TouchDown(object sender, TouchEventArgs e)
        {
            _isResizing = true;
            var touchPoint = e.GetTouchPoint(PositioningCanvas);
            _startResizePosition = touchPoint.Position;
            _startWidth = PhotoZone.Width;
            _startHeight = PhotoZone.Height;
            var handle = sender as FrameworkElement;
            handle?.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void ResizeHandle_TouchMove(object sender, TouchEventArgs e)
        {
            if (_isResizing)
            {
                ResizePhotoZone(e.GetTouchPoint(PositioningCanvas).Position);
            }
        }

        private void ResizeHandle_TouchUp(object sender, TouchEventArgs e)
        {
            _isResizing = false;
            var handle = sender as FrameworkElement;
            handle?.ReleaseTouchCapture(e.TouchDevice);
        }

        private void PhotoZone_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = PositioningCanvas;
            e.Mode = ManipulationModes.All;
        }

        private void PhotoZone_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            // Handle pinch to zoom
            var scaleX = e.DeltaManipulation.Scale.X;
            var scaleY = e.DeltaManipulation.Scale.Y;

            if (Math.Abs(scaleX - 1.0) > 0.01 || Math.Abs(scaleY - 1.0) > 0.01)
            {
                var newWidth = PhotoZone.Width * scaleX;
                var newHeight = PhotoZone.Height * scaleY;

                if (LockAspectRatio.IsChecked == true)
                {
                    var avgScale = (scaleX + scaleY) / 2;
                    newWidth = PhotoZone.Width * avgScale;
                    newHeight = PhotoZone.Height * avgScale;
                }

                // Apply size constraints
                newWidth = Math.Max(50, Math.Min(newWidth, PositioningCanvas.ActualWidth * 0.8));
                newHeight = Math.Max(50, Math.Min(newHeight, PositioningCanvas.ActualHeight * 0.8));

                PhotoZone.Width = newWidth;
                PhotoZone.Height = newHeight;

                // Update slider without triggering event loop
                _isUpdatingSlider = true;
                var sizeRatio = newWidth / PositioningCanvas.ActualWidth;
                SizeSlider.Value = Math.Max(0.2, Math.Min(0.8, sizeRatio));
                _isUpdatingSlider = false;

                FirePositionChanged();
            }
        }

        #endregion

        #region UI Events

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Prevent re-entry to avoid infinite loop
            if (_isUpdatingSlider)
                return;

            // Add comprehensive null checks and ensure canvas has actual size
            if (PhotoZone == null || PositioningCanvas == null || LockAspectRatio == null)
                return;

            if (PositioningCanvas.ActualWidth <= 0 || PositioningCanvas.ActualHeight <= 0)
                return;

            _isUpdatingSlider = true;
            try
            {
                // Use background bounds if available, otherwise use canvas dimensions
                double referenceWidth = _backgroundBounds.Width > 0 ? _backgroundBounds.Width : PositioningCanvas.ActualWidth;

                var newWidth = referenceWidth * e.NewValue;
                var newHeight = newWidth / _defaultAspectRatio; // Always maintain default aspect ratio

                // Constrain to background bounds or canvas size
                double maxWidth = _backgroundBounds.Width > 0 ? _backgroundBounds.Width : _canvasWidth;
                double maxHeight = _backgroundBounds.Height > 0 ? _backgroundBounds.Height : _canvasHeight;

                newWidth = Math.Min(newWidth, maxWidth);
                newHeight = Math.Min(newHeight, maxHeight);

                // If we hit a boundary, recalculate to maintain aspect ratio
                if (LockAspectRatio.IsChecked == true)
                {
                    if (newWidth == _canvasWidth)
                    {
                        newHeight = newWidth / _defaultAspectRatio;
                    }
                    else if (newHeight == _canvasHeight)
                    {
                        newWidth = newHeight * _defaultAspectRatio;
                    }
                }

                PhotoZone.Width = newWidth;
                PhotoZone.Height = newHeight;

                // Keep within bounds
                var left = Canvas.GetLeft(PhotoZone);
                var top = Canvas.GetTop(PhotoZone);

                // Handle NaN values from Canvas.GetLeft/GetTop
                if (double.IsNaN(left))
                    left = (_canvasWidth - newWidth) / 2; // Center if no position
                if (double.IsNaN(top))
                    top = (_canvasHeight - newHeight) / 2; // Center if no position

                // Adjust position if photo goes out of bounds
                if (left + newWidth > _canvasWidth)
                {
                    left = _canvasWidth - newWidth;
                }
                if (top + newHeight > _canvasHeight)
                {
                    top = _canvasHeight - newHeight;
                }

                // Ensure not negative
                left = Math.Max(0, left);
                top = Math.Max(0, top);

                Canvas.SetLeft(PhotoZone, left);
                Canvas.SetTop(PhotoZone, top);

                FirePositionChanged();
            }
            finally
            {
                _isUpdatingSlider = false;
            }
        }

        private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCanvasSize();
        }

        private void UpdateCanvasSize()
        {
            // Add null check for PositioningCanvas
            if (PositioningCanvas == null)
                return;

            _canvasWidth = PositioningCanvas.ActualWidth;
            _canvasHeight = PositioningCanvas.ActualHeight;

            // Recalculate background bounds when canvas size changes
            CalculateBackgroundBounds();

            // Center the photo zone if canvas size changes
            if (_canvasWidth > 0 && _canvasHeight > 0 && PhotoZone != null && SizeSlider != null)
            {
                // Keep relative position
                var data = GetPlacementData();
                if (data != null && data.PlacementZones != null && data.PlacementZones.Count > 0)
                {
                    SetPlacementData(data);
                }
                else
                {
                    // Use default size based on background aspect ratio
                    SetDefaultPhotoZoneSize();
                }
            }
        }

        #endregion

        private void FirePositionChanged()
        {
            PositionChanged?.Invoke(this, GetPlacementData());
        }
    }
}