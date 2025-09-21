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
        private double _defaultAspectRatio = 1.5; // Default 3:2 aspect ratio
        private Rect _backgroundBounds; // Actual background image display bounds

        public event EventHandler<PhotoPlacementData> PositionChanged;

        public SimplePhotoPositioner()
        {
            InitializeComponent();
            _placementData = new PhotoPlacementData
            {
                PlacementZones = new List<PhotoPlacementZone>()
            };
            _touchPoints = new Dictionary<int, Point>();
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
                    // Try to load as absolute path first
                    Uri imageUri;
                    if (System.IO.File.Exists(imagePath))
                    {
                        imageUri = new Uri(imagePath, UriKind.Absolute);
                    }
                    else
                    {
                        // Try as relative path
                        imageUri = new Uri(imagePath, UriKind.Relative);
                    }

                    var bitmapImage = new BitmapImage(imageUri);
                    BackgroundImage.Source = bitmapImage;

                    // Calculate aspect ratio from the background image
                    if (bitmapImage.PixelWidth > 0 && bitmapImage.PixelHeight > 0)
                    {
                        _defaultAspectRatio = (double)bitmapImage.PixelWidth / bitmapImage.PixelHeight;

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

            // Calculate how the image fits with Stretch="Uniform"
            var imageAspect = _defaultAspectRatio;
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

            // Center it within the background bounds
            Canvas.SetLeft(PhotoZone, _backgroundBounds.X + (_backgroundBounds.Width - zoneWidth) / 2);
            Canvas.SetTop(PhotoZone, _backgroundBounds.Y + (_backgroundBounds.Height - zoneHeight) / 2);

            // Update slider to match
            _isUpdatingSlider = true;
            SizeSlider.Value = zoneWidth / PositioningCanvas.ActualWidth;
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

            // Calculate relative positions (0-1)
            if (_canvasWidth > 0 && _canvasHeight > 0 && PhotoZone != null)
            {
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
                var zone = data.PlacementZones[0];

                // Apply to UI
                if (_canvasWidth > 0 && _canvasHeight > 0)
                {
                    Canvas.SetLeft(PhotoZone, zone.X * _canvasWidth);
                    Canvas.SetTop(PhotoZone, zone.Y * _canvasHeight);
                    PhotoZone.Width = zone.Width * _canvasWidth;
                    PhotoZone.Height = zone.Height * _canvasHeight;
                }
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

                var newLeft = Canvas.GetLeft(PhotoZone) + deltaX;
                var newTop = Canvas.GetTop(PhotoZone) + deltaY;

                // Keep within background bounds
                newLeft = Math.Max(_backgroundBounds.X, Math.Min(newLeft, _backgroundBounds.X + _backgroundBounds.Width - PhotoZone.Width));
                newTop = Math.Max(_backgroundBounds.Y, Math.Min(newTop, _backgroundBounds.Y + _backgroundBounds.Height - PhotoZone.Height));

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

                var newLeft = Canvas.GetLeft(PhotoZone) + deltaX;
                var newTop = Canvas.GetTop(PhotoZone) + deltaY;

                // Keep within background bounds
                newLeft = Math.Max(_backgroundBounds.X, Math.Min(newLeft, _backgroundBounds.X + _backgroundBounds.Width - PhotoZone.Width));
                newTop = Math.Max(_backgroundBounds.Y, Math.Min(newTop, _backgroundBounds.Y + _backgroundBounds.Height - PhotoZone.Height));

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
                var newWidth = PositioningCanvas.ActualWidth * e.NewValue;
                var newHeight = newWidth / _defaultAspectRatio; // Use stored aspect ratio

                if (LockAspectRatio.IsChecked == true && PhotoZone.Width > 0 && PhotoZone.Height > 0)
                {
                    var aspectRatio = PhotoZone.Width / PhotoZone.Height;
                    newHeight = newWidth / aspectRatio;
                }

                PhotoZone.Width = newWidth;
                PhotoZone.Height = newHeight;

                // Keep within bounds
                var left = Canvas.GetLeft(PhotoZone);
                var top = Canvas.GetTop(PhotoZone);

                // Handle NaN values from Canvas.GetLeft/GetTop
                if (double.IsNaN(left))
                    left = 0;
                if (double.IsNaN(top))
                    top = 0;

                if (left + newWidth > PositioningCanvas.ActualWidth)
                {
                    Canvas.SetLeft(PhotoZone, PositioningCanvas.ActualWidth - newWidth);
                }
                if (top + newHeight > PositioningCanvas.ActualHeight)
                {
                    Canvas.SetTop(PhotoZone, PositioningCanvas.ActualHeight - newHeight);
                }

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