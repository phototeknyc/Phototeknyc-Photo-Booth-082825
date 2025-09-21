using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Photobooth.Models;
using Photobooth.Services;

namespace Photobooth.Controls
{
    /// <summary>
    /// Control for configuring photo placement zones on backgrounds
    /// </summary>
    public partial class PhotoPositioningOverlay : UserControl
    {
        #region Private Fields

        private PhotoPlacementData _placementData;
        private string _backgroundPath;
        private ObservableCollection<PhotoPlacementZone> _zones;
        private PhotoPlacementZone _selectedZone;
        private ResizablePhotoZone _selectedZoneControl;
        private bool _isUpdatingSliders = false;
        private double _aspectRatio = 1.5; // Default 3:2 aspect ratio

        #endregion

        #region Events

        public event EventHandler<PhotoPlacementData> ConfigurationSaved;
        public event EventHandler Closed;

        #endregion

        #region Constructor

        public PhotoPositioningOverlay()
        {
            InitializeComponent();
            _zones = new ObservableCollection<PhotoPlacementZone>();
            ZonesList.ItemsSource = _zones;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize the overlay with background and existing placement data
        /// </summary>
        public void Initialize(string backgroundPath, PhotoPlacementData existingData = null)
        {
            _backgroundPath = backgroundPath;
            _placementData = existingData ?? new PhotoPlacementData();

            // Load background image
            if (!string.IsNullOrEmpty(backgroundPath))
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(backgroundPath));
                    BackgroundImage.Source = bitmap;
                    NoBackgroundText.Visibility = Visibility.Collapsed;

                    // Store background dimensions
                    _placementData.BackgroundWidth = bitmap.PixelWidth;
                    _placementData.BackgroundHeight = bitmap.PixelHeight;

                    // Set aspect ratio based on image
                    if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                    {
                        _aspectRatio = (double)bitmap.PixelWidth / bitmap.PixelHeight;
                    }
                }
                catch (Exception ex)
                {
                    DebugService.LogError($"Failed to load background image: {ex.Message}");
                    NoBackgroundText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                NoBackgroundText.Visibility = Visibility.Visible;
            }

            // Clear previous zones
            _zones.Clear();
            PositioningCanvas.Children.Clear();

            // Load existing zones or create a single default zone
            if (_placementData.PlacementZones != null && _placementData.PlacementZones.Count > 0)
            {
                foreach (var zone in _placementData.PlacementZones)
                {
                    // Validate zone values
                    if (double.IsNaN(zone.X) || double.IsInfinity(zone.X)) zone.X = 0.25;
                    if (double.IsNaN(zone.Y) || double.IsInfinity(zone.Y)) zone.Y = 0.25;
                    if (double.IsNaN(zone.Width) || double.IsInfinity(zone.Width) || zone.Width <= 0) zone.Width = 0.5;
                    if (double.IsNaN(zone.Height) || double.IsInfinity(zone.Height) || zone.Height <= 0) zone.Height = 0.5;

                    _zones.Add(zone);
                    CreateZoneControl(zone);
                }
            }
            else
            {
                // Always create a single default zone for simplicity
                AddDefaultZone();
            }

            // Auto-select the first zone
            if (_zones.Count > 0)
            {
                SelectZone(_zones[0]);
            }
        }

        #endregion

        #region Zone Management

        private void AddDefaultZone()
        {
            var zone = new PhotoPlacementZone
            {
                Name = "Photo 1",
                X = 0.25,
                Y = 0.25,
                Width = 0.5,
                Height = 0.5 / _aspectRatio,
                PhotoIndex = 0
            };
            _zones.Add(zone);
            CreateZoneControl(zone);
        }

        private void AddZone_Click(object sender, RoutedEventArgs e)
        {
            var photoIndex = _zones.Count;
            var zone = new PhotoPlacementZone
            {
                Name = $"Photo {photoIndex + 1}",
                X = 0.1 + (photoIndex * 0.1),
                Y = 0.1 + (photoIndex * 0.1),
                Width = 0.3,
                Height = 0.3 / _aspectRatio,
                PhotoIndex = photoIndex
            };
            _zones.Add(zone);
            CreateZoneControl(zone);
        }

        private void RemoveZone_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedZone != null && _selectedZoneControl != null)
            {
                _zones.Remove(_selectedZone);
                PositioningCanvas.Children.Remove(_selectedZoneControl);
                _selectedZone = null;
                _selectedZoneControl = null;
                UpdateControlStates();
            }
        }

        private void ZonesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var zone = ZonesList.SelectedItem as PhotoPlacementZone;
            SelectZone(zone);
        }

        private void SelectZone(PhotoPlacementZone zone)
        {
            // Deselect previous
            if (_selectedZoneControl != null)
            {
                _selectedZoneControl.IsSelected = false;
            }

            _selectedZone = zone;

            // Find and select new zone control
            if (zone != null)
            {
                foreach (var child in PositioningCanvas.Children)
                {
                    if (child is ResizablePhotoZone zoneControl && zoneControl.Zone == zone)
                    {
                        _selectedZoneControl = zoneControl;
                        _selectedZoneControl.IsSelected = true;
                        ZonesList.SelectedItem = zone;
                        UpdateSliders();
                        break;
                    }
                }
            }
            else
            {
                _selectedZoneControl = null;
            }

            UpdateControlStates();
        }

        private void UpdateControlStates()
        {
            bool hasSelection = _selectedZone != null;
            RemoveZoneButton.IsEnabled = hasSelection;
            XSlider.IsEnabled = hasSelection;
            YSlider.IsEnabled = hasSelection;
            WidthSlider.IsEnabled = hasSelection;
            HeightSlider.IsEnabled = hasSelection;
        }

        private void UpdateSliders()
        {
            if (_selectedZone == null) return;

            _isUpdatingSliders = true;

            // Ensure values are valid numbers, default to sensible values if not
            XSlider.Value = double.IsNaN(_selectedZone.X) || double.IsInfinity(_selectedZone.X) ? 0.25 : _selectedZone.X;
            YSlider.Value = double.IsNaN(_selectedZone.Y) || double.IsInfinity(_selectedZone.Y) ? 0.25 : _selectedZone.Y;
            WidthSlider.Value = double.IsNaN(_selectedZone.Width) || double.IsInfinity(_selectedZone.Width) || _selectedZone.Width <= 0 ? 0.5 : _selectedZone.Width;
            HeightSlider.Value = double.IsNaN(_selectedZone.Height) || double.IsInfinity(_selectedZone.Height) || _selectedZone.Height <= 0 ? 0.3 : _selectedZone.Height;

            _isUpdatingSliders = false;
        }

        #endregion

        #region Zone Control Creation

        private void CreateZoneControl(PhotoPlacementZone zone)
        {
            var zoneControl = new ResizablePhotoZone
            {
                Zone = zone,
                AspectRatioLocked = AspectRatioCheckBox.IsChecked == true
            };

            zoneControl.Selected += (s, e) => SelectZone(zone);
            zoneControl.PositionChanged += (s, e) => UpdateSliders();

            PositioningCanvas.Children.Add(zoneControl);
            UpdateZonePosition(zoneControl);
        }

        private void UpdateZonePosition(ResizablePhotoZone zoneControl)
        {
            if (BackgroundImage.ActualWidth > 0 && BackgroundImage.ActualHeight > 0 && zoneControl != null && zoneControl.Zone != null)
            {
                var zone = zoneControl.Zone;

                // Validate and set safe defaults if needed
                if (double.IsNaN(zone.X) || double.IsInfinity(zone.X)) zone.X = 0.25;
                if (double.IsNaN(zone.Y) || double.IsInfinity(zone.Y)) zone.Y = 0.25;
                if (double.IsNaN(zone.Width) || double.IsInfinity(zone.Width) || zone.Width <= 0) zone.Width = 0.5;
                if (double.IsNaN(zone.Height) || double.IsInfinity(zone.Height) || zone.Height <= 0) zone.Height = 0.3;

                // Ensure values are within bounds
                zone.X = Math.Max(0, Math.Min(1, zone.X));
                zone.Y = Math.Max(0, Math.Min(1, zone.Y));
                zone.Width = Math.Max(0.05, Math.Min(1, zone.Width));
                zone.Height = Math.Max(0.05, Math.Min(1, zone.Height));

                Canvas.SetLeft(zoneControl, zone.X * BackgroundImage.ActualWidth);
                Canvas.SetTop(zoneControl, zone.Y * BackgroundImage.ActualHeight);
                zoneControl.Width = zone.Width * BackgroundImage.ActualWidth;
                zoneControl.Height = zone.Height * BackgroundImage.ActualHeight;
            }
        }

        #endregion

        #region Slider Events

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders || _selectedZone == null || _selectedZoneControl == null) return;

            _selectedZone.X = XSlider.Value;
            _selectedZone.Y = YSlider.Value;
            UpdateZonePosition(_selectedZoneControl);
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSliders || _selectedZone == null || _selectedZoneControl == null) return;

            if (AspectRatioCheckBox.IsChecked == true)
            {
                // Maintain aspect ratio
                if (sender == WidthSlider)
                {
                    _selectedZone.Width = WidthSlider.Value;
                    _selectedZone.Height = WidthSlider.Value / _aspectRatio;
                    _isUpdatingSliders = true;
                    HeightSlider.Value = _selectedZone.Height;
                    _isUpdatingSliders = false;
                }
                else
                {
                    _selectedZone.Height = HeightSlider.Value;
                    _selectedZone.Width = HeightSlider.Value * _aspectRatio;
                    _isUpdatingSliders = true;
                    WidthSlider.Value = _selectedZone.Width;
                    _isUpdatingSliders = false;
                }
            }
            else
            {
                _selectedZone.Width = WidthSlider.Value;
                _selectedZone.Height = HeightSlider.Value;
            }

            UpdateZonePosition(_selectedZoneControl);
        }

        private void AspectRatio_Changed(object sender, RoutedEventArgs e)
        {
            bool isLocked = AspectRatioCheckBox.IsChecked == true;

            foreach (var child in PositioningCanvas.Children)
            {
                if (child is ResizablePhotoZone zoneControl)
                {
                    zoneControl.AspectRatioLocked = isLocked;
                }
            }

            // Adjust current selection if needed
            if (isLocked && _selectedZone != null)
            {
                _selectedZone.Height = _selectedZone.Width / _aspectRatio;
                UpdateSliders();
                UpdateZonePosition(_selectedZoneControl);
            }
        }

        #endregion

        #region Preset Buttons

        private void SinglePhotoPreset_Click(object sender, RoutedEventArgs e)
        {
            // Clear existing zones
            _zones.Clear();
            PositioningCanvas.Children.Clear();

            // Add single centered photo
            var zone = new PhotoPlacementZone
            {
                Name = "Photo 1",
                X = 0.2,
                Y = 0.2,
                Width = 0.6,
                Height = 0.6 / _aspectRatio,
                PhotoIndex = 0
            };
            _zones.Add(zone);
            CreateZoneControl(zone);
        }

        private void TwoPhotosPreset_Click(object sender, RoutedEventArgs e)
        {
            // Clear existing zones
            _zones.Clear();
            PositioningCanvas.Children.Clear();

            // Add two photos side by side
            var zone1 = new PhotoPlacementZone
            {
                Name = "Photo 1",
                X = 0.05,
                Y = 0.25,
                Width = 0.4,
                Height = 0.4 / _aspectRatio,
                PhotoIndex = 0
            };
            _zones.Add(zone1);
            CreateZoneControl(zone1);

            var zone2 = new PhotoPlacementZone
            {
                Name = "Photo 2",
                X = 0.55,
                Y = 0.25,
                Width = 0.4,
                Height = 0.4 / _aspectRatio,
                PhotoIndex = 1
            };
            _zones.Add(zone2);
            CreateZoneControl(zone2);
        }

        private void FourPhotosPreset_Click(object sender, RoutedEventArgs e)
        {
            // Clear existing zones
            _zones.Clear();
            PositioningCanvas.Children.Clear();

            // Add four photos in a grid
            double size = 0.35;
            double height = size / _aspectRatio;
            double spacing = 0.1;

            for (int i = 0; i < 4; i++)
            {
                var zone = new PhotoPlacementZone
                {
                    Name = $"Photo {i + 1}",
                    X = spacing + (i % 2) * (size + spacing),
                    Y = spacing + (i / 2) * (height + spacing),
                    Width = size,
                    Height = height,
                    PhotoIndex = i
                };
                _zones.Add(zone);
                CreateZoneControl(zone);
            }
        }

        #endregion

        #region Action Buttons

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Update placement data
            _placementData.PlacementZones = _zones.ToList();
            _placementData.MaintainAspectRatio = AspectRatioCheckBox.IsChecked == true;
            _placementData.DefaultAspectRatio = _aspectRatio;

            // Fire save event
            ConfigurationSaved?.Invoke(this, _placementData);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _zones.Clear();
            PositioningCanvas.Children.Clear();
            AddDefaultZone();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Close()
        {
            Closed?.Invoke(this, EventArgs.Empty);
            this.Visibility = Visibility.Collapsed;
        }

        #endregion
    }

    /// <summary>
    /// Resizable and draggable photo zone control with touch support
    /// </summary>
    public class ResizablePhotoZone : Grid
    {
        #region Fields

        private PhotoPlacementZone _zone;
        private bool _isSelected;
        private bool _isDragging;
        private bool _isResizing;
        private Point _dragStart;
        private Point _initialPosition;
        private Size _initialSize;
        private Dictionary<int, Point> _touchPoints = new Dictionary<int, Point>();
        private Border _mainBorder;
        private Grid _resizeHandlesGrid;

        #endregion

        #region Properties

        public PhotoPlacementZone Zone
        {
            get => _zone;
            set
            {
                _zone = value;
                UpdateAppearance();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                UpdateAppearance();
                ShowResizeHandles(value);
            }
        }

        public bool AspectRatioLocked { get; set; } = true;

        #endregion

        #region Events

        public event EventHandler Selected;
        public event EventHandler PositionChanged;

        #endregion

        #region Constructor

        public ResizablePhotoZone()
        {
            InitializeControl();
        }

        private void InitializeControl()
        {
            // Create main border for the zone
            _mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(80, 76, 175, 80)),
                BorderBrush = new SolidColorBrush(Colors.LimeGreen),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(4)
            };

            // Add label
            var label = new TextBlock
            {
                Text = "Photo Area",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _mainBorder.Child = label;

            // Add main border to grid
            Children.Add(_mainBorder);

            // Create resize handles
            CreateResizeHandles();

            // Enable touch support
            IsManipulationEnabled = true;

            // Mouse events for dragging
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;

            // Touch events
            TouchDown += OnTouchDown;
            TouchMove += OnTouchMove;
            TouchUp += OnTouchUp;

            // Manipulation events for pinch/zoom
            ManipulationStarting += OnManipulationStarting;
            ManipulationDelta += OnManipulationDelta;
            ManipulationCompleted += OnManipulationCompleted;

            Cursor = Cursors.SizeAll;
        }

        private void CreateResizeHandles()
        {
            _resizeHandlesGrid = new Grid();

            // Create corner handles (larger for touch)
            int handleSize = 30; // Larger for touch
            var handleColor = new SolidColorBrush(Colors.White);
            var handleBorder = new SolidColorBrush(Colors.DarkGray);

            // Top-left
            CreateHandle(HorizontalAlignment.Left, VerticalAlignment.Top, handleSize, handleColor, handleBorder, Cursors.SizeNWSE);

            // Top-right
            CreateHandle(HorizontalAlignment.Right, VerticalAlignment.Top, handleSize, handleColor, handleBorder, Cursors.SizeNESW);

            // Bottom-left
            CreateHandle(HorizontalAlignment.Left, VerticalAlignment.Bottom, handleSize, handleColor, handleBorder, Cursors.SizeNESW);

            // Bottom-right
            CreateHandle(HorizontalAlignment.Right, VerticalAlignment.Bottom, handleSize, handleColor, handleBorder, Cursors.SizeNWSE);

            // Edge handles
            CreateHandle(HorizontalAlignment.Center, VerticalAlignment.Top, handleSize, handleColor, handleBorder, Cursors.SizeNS);
            CreateHandle(HorizontalAlignment.Center, VerticalAlignment.Bottom, handleSize, handleColor, handleBorder, Cursors.SizeNS);
            CreateHandle(HorizontalAlignment.Left, VerticalAlignment.Center, handleSize, handleColor, handleBorder, Cursors.SizeWE);
            CreateHandle(HorizontalAlignment.Right, VerticalAlignment.Center, handleSize, handleColor, handleBorder, Cursors.SizeWE);

            Children.Add(_resizeHandlesGrid);
        }

        private void CreateHandle(HorizontalAlignment hAlign, VerticalAlignment vAlign, int size, Brush fill, Brush border, Cursor cursor)
        {
            var handle = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = fill,
                Stroke = border,
                StrokeThickness = 2,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
                Cursor = cursor,
                Margin = new Thickness(-size/2)  // Center the handle on the edge
            };

            // Add shadow effect for visibility
            handle.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 5,
                ShadowDepth = 2,
                Opacity = 0.5
            };

            // Touch and mouse events for resizing
            handle.MouseLeftButtonDown += Handle_MouseDown;
            handle.TouchDown += Handle_TouchDown;

            _resizeHandlesGrid.Children.Add(handle);
        }

        private void ShowResizeHandles(bool show)
        {
            _resizeHandlesGrid.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateAppearance()
        {
            if (_zone != null && _mainBorder != null)
            {
                if (_mainBorder.Child is TextBlock label)
                {
                    label.Text = _zone.Name ?? "Photo Area";
                }

                if (_isSelected)
                {
                    _mainBorder.BorderBrush = new SolidColorBrush(Colors.Yellow);
                    _mainBorder.BorderThickness = new Thickness(4);
                    ShowResizeHandles(true);
                }
                else
                {
                    _mainBorder.BorderBrush = new SolidColorBrush(Colors.LimeGreen);
                    _mainBorder.BorderThickness = new Thickness(3);
                    ShowResizeHandles(false);
                }
            }
        }

        #endregion

        #region Mouse Handling

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _dragStart = e.GetPosition((Canvas)Parent);
                _initialPosition = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
                CaptureMouse();
                Selected?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = (Canvas)Parent;
                var currentPos = e.GetPosition(canvas);
                var deltaX = currentPos.X - _dragStart.X;
                var deltaY = currentPos.Y - _dragStart.Y;

                var newLeft = Math.Max(0, Math.Min(_initialPosition.X + deltaX, canvas.ActualWidth - ActualWidth));
                var newTop = Math.Max(0, Math.Min(_initialPosition.Y + deltaY, canvas.ActualHeight - ActualHeight));

                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);

                // Update zone data
                if (_zone != null && canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
                {
                    _zone.X = newLeft / canvas.ActualWidth;
                    _zone.Y = newTop / canvas.ActualHeight;
                }

                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
            }
        }

        #endregion

        #region Touch Handling

        private void OnTouchDown(object sender, TouchEventArgs e)
        {
            var touchPoint = e.GetTouchPoint(this);
            _touchPoints[e.TouchDevice.Id] = touchPoint.Position;
            CaptureTouch(e.TouchDevice);
            Selected?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void OnTouchMove(object sender, TouchEventArgs e)
        {
            if (_touchPoints.ContainsKey(e.TouchDevice.Id))
            {
                var canvas = Parent as Canvas;
                if (canvas != null)
                {
                    var currentPos = e.GetTouchPoint(canvas).Position;
                    var startPos = _touchPoints[e.TouchDevice.Id];

                    // Calculate movement
                    var deltaX = currentPos.X - startPos.X;
                    var deltaY = currentPos.Y - startPos.Y;

                    // Update position
                    var newLeft = Canvas.GetLeft(this) + deltaX;
                    var newTop = Canvas.GetTop(this) + deltaY;

                    // Constrain to canvas bounds
                    newLeft = Math.Max(0, Math.Min(newLeft, canvas.ActualWidth - ActualWidth));
                    newTop = Math.Max(0, Math.Min(newTop, canvas.ActualHeight - ActualHeight));

                    Canvas.SetLeft(this, newLeft);
                    Canvas.SetTop(this, newTop);

                    // Update zone data
                    if (_zone != null && canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
                    {
                        _zone.X = newLeft / canvas.ActualWidth;
                        _zone.Y = newTop / canvas.ActualHeight;
                    }

                    _touchPoints[e.TouchDevice.Id] = currentPos;
                    PositionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void OnTouchUp(object sender, TouchEventArgs e)
        {
            _touchPoints.Remove(e.TouchDevice.Id);
            ReleaseTouchCapture(e.TouchDevice);
        }

        private void OnManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.ManipulationContainer = Parent as Canvas;
            e.Mode = ManipulationModes.All;
        }

        private void OnManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            var canvas = Parent as Canvas;
            if (canvas == null) return;

            // Handle translation
            var newLeft = Canvas.GetLeft(this) + e.DeltaManipulation.Translation.X;
            var newTop = Canvas.GetTop(this) + e.DeltaManipulation.Translation.Y;

            // Handle scaling
            var scaleX = e.DeltaManipulation.Scale.X;
            var scaleY = e.DeltaManipulation.Scale.Y;

            if (Math.Abs(scaleX - 1.0) > 0.01 || Math.Abs(scaleY - 1.0) > 0.01)
            {
                var newWidth = Width * scaleX;
                var newHeight = Height * scaleY;

                if (AspectRatioLocked)
                {
                    // Maintain aspect ratio
                    var avgScale = (scaleX + scaleY) / 2;
                    newWidth = Width * avgScale;
                    newHeight = Height * avgScale;
                }

                // Apply constraints
                newWidth = Math.Max(50, Math.Min(newWidth, canvas.ActualWidth));
                newHeight = Math.Max(50, Math.Min(newHeight, canvas.ActualHeight));

                Width = newWidth;
                Height = newHeight;

                // Update zone size
                if (_zone != null && canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
                {
                    _zone.Width = newWidth / canvas.ActualWidth;
                    _zone.Height = newHeight / canvas.ActualHeight;
                }
            }

            // Constrain position
            newLeft = Math.Max(0, Math.Min(newLeft, canvas.ActualWidth - Width));
            newTop = Math.Max(0, Math.Min(newTop, canvas.ActualHeight - Height));

            Canvas.SetLeft(this, newLeft);
            Canvas.SetTop(this, newTop);

            // Update zone position
            if (_zone != null && canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
            {
                _zone.X = newLeft / canvas.ActualWidth;
                _zone.Y = newTop / canvas.ActualHeight;
            }

            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            // Manipulation completed
        }

        private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var handle = sender as FrameworkElement;
                if (handle != null)
                {
                    _isResizing = true;
                    _dragStart = e.GetPosition(Parent as Canvas);
                    _initialSize = new Size(Width, Height);
                    _initialPosition = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
                    handle.CaptureMouse();
                    e.Handled = true;
                }
            }
        }

        private void Handle_TouchDown(object sender, TouchEventArgs e)
        {
            var handle = sender as FrameworkElement;
            if (handle != null)
            {
                _isResizing = true;
                var touchPoint = e.GetTouchPoint(Parent as Canvas);
                _dragStart = touchPoint.Position;
                _initialSize = new Size(Width, Height);
                _initialPosition = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
                handle.CaptureTouch(e.TouchDevice);
                e.Handled = true;
            }
        }

        #endregion
    }
}