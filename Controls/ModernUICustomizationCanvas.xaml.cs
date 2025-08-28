using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Photobooth.Database;
using Photobooth.Models.UITemplates;

namespace Photobooth.Controls
{
    public partial class ModernUICustomizationCanvas : UserControl
    {
        private UILayoutTemplate _currentLayout;
        private UILayoutProfile _currentProfile;
        private UILayoutDatabase _database;
        private UIElementTemplate _selectedElement;
        private bool _isDragging;
        private Point _dragOffset;
        private List<UIElementControl> _elementControls = new List<UIElementControl>();
        private Stack<UILayoutTemplate> _undoStack = new Stack<UILayoutTemplate>();
        private Stack<UILayoutTemplate> _redoStack = new Stack<UILayoutTemplate>();
        private List<UILayoutProfile> _profiles;

        public ModernUICustomizationCanvas()
        {
            InitializeComponent();
            _database = new UILayoutDatabase();
            _database.InitializePredefinedProfiles();
            InitializeCanvas();
            SetupEventHandlers();
            LoadProfiles();
            LoadDefaultLayout();
        }
        
        private void LoadProfiles()
        {
            try
            {
                _profiles = _database.GetAllProfiles();
                ProfileSelector.ItemsSource = _profiles;
                
                // Select active profile if exists
                var activeProfile = _database.GetActiveProfile();
                if (activeProfile != null)
                {
                    ProfileSelector.SelectedItem = activeProfile;
                    _currentProfile = activeProfile;
                }
                else if (_profiles.Count > 0)
                {
                    ProfileSelector.SelectedIndex = 0;
                    _currentProfile = _profiles[0];
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading profiles: {ex.Message}");
            }
        }

        private void InitializeCanvas()
        {
            // Set initial device size
            UpdateDeviceFrame(1024, 768);
            
            // Enable drag and drop
            DesignCanvas.AllowDrop = true;
            DesignCanvas.DragOver += Canvas_DragOver;
            DesignCanvas.Drop += Canvas_Drop;
        }

        private void SetupEventHandlers()
        {
            // Toolbar buttons
            OrientationToggle.Click += (s, e) => ToggleOrientation();
            UndoBtn.Click += (s, e) => Undo();
            RedoBtn.Click += (s, e) => Redo();
            PreviewBtn.Click += (s, e) => ShowPreview();
            SaveBtn.Click += (s, e) => SaveLayout();

            // Tool buttons
            AddButtonTool.Click += (s, e) => AddElement(ElementType.Button);
            AddTextTool.Click += (s, e) => AddElement(ElementType.Text);
            AddImageTool.Click += (s, e) => AddElement(ElementType.Image);
            AddShapeTool.Click += (s, e) => AddElement(ElementType.Background);

            // Alignment buttons
            AlignLeftBtn.Click += (s, e) => AlignElements("left");
            AlignCenterBtn.Click += (s, e) => AlignElements("center");
            AlignRightBtn.Click += (s, e) => AlignElements("right");

            // Device selector
            DeviceSelector.SelectionChanged += DeviceSelector_SelectionChanged;

            // Canvas mouse events
            DesignCanvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
            DesignCanvas.MouseMove += Canvas_MouseMove;
            DesignCanvas.MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
            DesignCanvas.MouseRightButtonDown += Canvas_MouseRightButtonDown;

            // Keyboard shortcuts
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void LoadDefaultLayout()
        {
            // Try to import from current PhotoboothTouchModern layout first
            var importedLayout = ImportFromPhotoboothTouchModern();
            if (importedLayout != null && importedLayout.Elements.Count > 0)
            {
                _currentLayout = importedLayout;
                RefreshCanvas();
                return;
            }
            
            // Otherwise load default landscape layout or create empty one
            _currentLayout = _database.GetActiveLayout(Orientation.Horizontal) 
                          ?? DefaultTemplates.CreateLandscapeTemplate();
            
            // If still null or no elements, create an empty layout
            if (_currentLayout == null)
            {
                _currentLayout = new UILayoutTemplate
                {
                    Id = "empty-layout",
                    Name = "New Layout",
                    Elements = new List<UIElementTemplate>(),
                    Theme = new UITheme
                    {
                        BackgroundColor = Color.FromRgb(30, 30, 30)
                    }
                };
            }
            
            RefreshCanvas();
        }

        private void RefreshCanvas()
        {
            DesignCanvas.Children.Clear();
            _elementControls.Clear();

            if (_currentLayout == null) return;

            foreach (var element in _currentLayout.Elements.OrderBy(e => e.ZIndex))
            {
                var control = CreateElementControl(element);
                if (control != null)
                {
                    _elementControls.Add(control);
                    DesignCanvas.Children.Add(control);
                    PositionElement(control, element);
                }
            }

            UpdateLayersList();
        }

        private UIElementControl CreateElementControl(UIElementTemplate element)
        {
            var control = new UIElementControl(element);
            
            // Apply visual based on type
            switch (element.Type)
            {
                case ElementType.Button:
                    control.Child = CreateButtonVisual(element);
                    break;
                case ElementType.Text:
                    control.Child = CreateTextVisual(element);
                    break;
                case ElementType.Image:
                    control.Child = CreateImageVisual(element);
                    break;
                case ElementType.Background:
                    control.Child = CreateBackgroundVisual(element);
                    break;
                case ElementType.Camera:
                    control.Child = CreateCameraPreviewVisual(element);
                    break;
                case ElementType.Countdown:
                    control.Child = CreateCountdownVisual(element);
                    break;
            }

            // Make it interactive
            control.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            control.MouseMove += Element_MouseMove;
            control.MouseLeftButtonUp += Element_MouseLeftButtonUp;
            control.MouseEnter += Element_MouseEnter;
            control.MouseLeave += Element_MouseLeave;

            return control;
        }

        private FrameworkElement CreateButtonVisual(UIElementTemplate element)
        {
            var button = new Border
            {
                CornerRadius = new CornerRadius(GetPropertyValue<double>(element, "CornerRadius", 10)),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    GetPropertyValue<string>(element, "BackgroundColor", "#4CAF50"))),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                MinWidth = 100,
                MinHeight = 50
            };

            var content = new TextBlock
            {
                Text = GetPropertyValue<string>(element, "Text", "Button"),
                Foreground = Brushes.White,
                FontSize = GetPropertyValue<double>(element, "FontSize", 16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            button.Child = content;

            // Add hover effect
            button.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = 0.3
            };

            return button;
        }

        private FrameworkElement CreateTextVisual(UIElementTemplate element)
        {
            var textBlock = new TextBlock
            {
                Text = GetPropertyValue<string>(element, "Text", "Text Label"),
                Foreground = Brushes.White,
                FontSize = GetPropertyValue<double>(element, "FontSize", 24),
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Wrap in a border for better visibility
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                Padding = new Thickness(10),
                Child = textBlock
            };
        }

        private FrameworkElement CreateImageVisual(UIElementTemplate element)
        {
            var border = new Border
            {
                Background = new LinearGradientBrush(Color.FromRgb(60, 60, 60), Color.FromRgb(40, 40, 40), 90),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                MinWidth = 100,
                MinHeight = 100
            };

            var text = new TextBlock
            {
                Text = GetPropertyValue<string>(element, "Text", "IMAGE"),
                Foreground = Brushes.White,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = text;
            return border;
        }

        private FrameworkElement CreateBackgroundVisual(UIElementTemplate element)
        {
            var bgColor = GetPropertyValue<string>(element, "BackgroundColor", "#2A2A2A");
            return new Rectangle
            {
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            };
        }

        private FrameworkElement CreateCameraPreviewVisual(UIElementTemplate element)
        {
            var border = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(GetPropertyValue<double>(element, "BorderThickness", 5)),
                CornerRadius = new CornerRadius(GetPropertyValue<double>(element, "CornerRadius", 20)),
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                Width = element.MinSize.Width,
                Height = element.MinSize.Height
            };

            var text = new TextBlock
            {
                Text = "Camera Preview",
                Foreground = Brushes.White,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = text;
            return border;
        }

        private FrameworkElement CreateCountdownVisual(UIElementTemplate element)
        {
            var ellipse = new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Width = element.MinSize.Width,
                Height = element.MinSize.Height
            };

            var grid = new Grid();
            grid.Children.Add(ellipse);

            var text = new TextBlock
            {
                Text = "3",
                Foreground = Brushes.White,
                FontSize = GetPropertyValue<double>(element, "FontSize", 72),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            grid.Children.Add(text);
            return grid;
        }

        private T GetPropertyValue<T>(UIElementTemplate element, string key, T defaultValue)
        {
            if (element.Properties != null && element.Properties.ContainsKey(key))
            {
                var value = element.Properties[key];
                if (value is T typedValue)
                    return typedValue;
                
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private void PositionElement(UIElementControl control, UIElementTemplate element)
        {
            var canvasWidth = DesignCanvas.ActualWidth > 0 ? DesignCanvas.ActualWidth : 984; // 1024 - 40 margin
            var canvasHeight = DesignCanvas.ActualHeight > 0 ? DesignCanvas.ActualHeight : 728; // 768 - 40 margin

            // Calculate position based on anchor and offset
            double x = 0, y = 0;
            
            // If offset values are > 10, treat as pixels, otherwise as percentage
            double offsetX = Math.Abs(element.AnchorOffset.X) > 10 ? element.AnchorOffset.X : canvasWidth * element.AnchorOffset.X / 100;
            double offsetY = Math.Abs(element.AnchorOffset.Y) > 10 ? element.AnchorOffset.Y : canvasHeight * element.AnchorOffset.Y / 100;

            switch (element.Anchor)
            {
                case AnchorPoint.TopLeft:
                    x = offsetX;
                    y = offsetY;
                    break;
                case AnchorPoint.TopCenter:
                    x = canvasWidth / 2 + offsetX;
                    y = offsetY;
                    break;
                case AnchorPoint.TopRight:
                    x = canvasWidth + offsetX;
                    y = offsetY;
                    break;
                case AnchorPoint.Center:
                    x = canvasWidth / 2 + offsetX;
                    y = canvasHeight / 2 + offsetY;
                    break;
                case AnchorPoint.BottomCenter:
                    x = canvasWidth / 2 + offsetX;
                    y = canvasHeight + offsetY;
                    break;
                case AnchorPoint.BottomLeft:
                    x = offsetX;
                    y = canvasHeight + offsetY;
                    break;
                case AnchorPoint.BottomRight:
                    x = canvasWidth + offsetX;
                    y = canvasHeight + offsetY;
                    break;
                case AnchorPoint.MiddleLeft:
                    x = offsetX;
                    y = canvasHeight / 2 + offsetY;
                    break;
                case AnchorPoint.MiddleRight:
                    x = canvasWidth + offsetX;
                    y = canvasHeight / 2 + offsetY;
                    break;
            }

            // Apply size based on mode
            if (element.SizeMode == SizeMode.Relative)
            {
                control.Width = canvasWidth * element.RelativeSize.Width / 100;
                control.Height = canvasHeight * element.RelativeSize.Height / 100;
            }
            else
            {
                control.Width = element.MinSize.Width;
                control.Height = element.MinSize.Height;
            }

            // Center the element on its anchor point
            Canvas.SetLeft(control, x - control.Width / 2);
            Canvas.SetTop(control, y - control.Height / 2);
        }

        private void AddElement(ElementType type)
        {
            SaveUndoState();

            var newElement = new UIElementTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"New {type}",
                Type = type,
                Anchor = AnchorPoint.Center,
                AnchorOffset = new Point(0, 0),
                SizeMode = SizeMode.Fixed,  // Changed to Fixed for initial visibility
                RelativeSize = new Size(20, 10),
                MinSize = new Size(200, 100),  // Increased default size
                MaxSize = new Size(400, 200),
                ZIndex = _currentLayout.Elements.Count,
                Properties = new Dictionary<string, object>(),
                IsVisible = true,
                IsEnabled = true,
                Opacity = 1.0
            };

            // Set type-specific defaults
            switch (type)
            {
                case ElementType.Button:
                    newElement.Properties["Text"] = "Button";
                    newElement.Properties["BackgroundColor"] = "#4CAF50";
                    newElement.Properties["FontSize"] = 18.0;
                    newElement.Properties["CornerRadius"] = 10.0;
                    break;
                case ElementType.Text:
                    newElement.Properties["Text"] = "Text Label";
                    newElement.Properties["FontSize"] = 24.0;
                    break;
                case ElementType.Image:
                    newElement.Properties["Text"] = "Image Placeholder";
                    break;
                case ElementType.Background:
                    newElement.Properties["BackgroundColor"] = "#2A2A2A";
                    break;
            }

            _currentLayout.Elements.Add(newElement);
            RefreshCanvas();

            // Animate new element
            var control = _elementControls.LastOrDefault();
            if (control != null)
            {
                AnimateElementAppear(control);
            }
        }

        private void AnimateElementAppear(UIElementControl control)
        {
            control.Opacity = 0;
            control.RenderTransform = new ScaleTransform(0.5, 0.5, control.Width / 2, control.Height / 2);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            var scaleX = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(300));
            var scaleY = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(300));

            scaleX.EasingFunction = scaleY.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut };

            control.BeginAnimation(OpacityProperty, fadeIn);
            ((ScaleTransform)control.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            ((ScaleTransform)control.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private void Element_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is UIElementControl control)
            {
                // Show hover effect
                control.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(108, 99, 255),
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.6
                };
            }
        }

        private void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDragging && sender is UIElementControl control)
            {
                // Remove hover effect if not selected
                if (_selectedElement == null || _selectedElement.Id != control.Element.Id)
                {
                    control.Effect = null;
                }
            }
        }

        private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElementControl control)
            {
                _selectedElement = control.Element;
                _isDragging = true;
                _dragOffset = e.GetPosition(control);
                control.CaptureMouse();

                ShowElementProperties(control.Element);
                ShowSelectionAdorner(control);

                e.Handled = true;
            }
        }

        private void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && sender is UIElementControl control)
            {
                var position = e.GetPosition(DesignCanvas);
                var newX = position.X - _dragOffset.X;
                var newY = position.Y - _dragOffset.Y;

                // Snap to grid if enabled
                if (true) // Check snap to grid checkbox
                {
                    newX = Math.Round(newX / 10) * 10;
                    newY = Math.Round(newY / 10) * 10;
                }

                Canvas.SetLeft(control, newX);
                Canvas.SetTop(control, newY);

                // Show smart guides
                ShowSmartGuides(control, newX, newY);
            }
        }

        private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is UIElementControl control)
            {
                _isDragging = false;
                control.ReleaseMouseCapture();
                HideSmartGuides();
                SaveUndoState();
            }
        }

        private void ShowSmartGuides(UIElementControl control, double x, double y)
        {
            // Check alignment with other elements
            foreach (var other in _elementControls.Where(c => c != control))
            {
                var otherX = Canvas.GetLeft(other);
                var otherY = Canvas.GetTop(other);

                // Vertical alignment
                if (Math.Abs(x - otherX) < 5)
                {
                    VerticalGuide.X1 = VerticalGuide.X2 = otherX;
                    VerticalGuide.Y1 = 0;
                    VerticalGuide.Y2 = DesignCanvas.ActualHeight;
                    VerticalGuide.Visibility = Visibility.Visible;
                }

                // Horizontal alignment
                if (Math.Abs(y - otherY) < 5)
                {
                    HorizontalGuide.X1 = 0;
                    HorizontalGuide.X2 = DesignCanvas.ActualWidth;
                    HorizontalGuide.Y1 = HorizontalGuide.Y2 = otherY;
                    HorizontalGuide.Visibility = Visibility.Visible;
                }
            }
        }

        private void HideSmartGuides()
        {
            VerticalGuide.Visibility = Visibility.Collapsed;
            HorizontalGuide.Visibility = Visibility.Collapsed;
        }

        private void ShowSelectionAdorner(UIElementControl control)
        {
            // Clear previous selection
            ClearSelection();
            
            // Add selection border to current control
            control.BorderBrush = new SolidColorBrush(Color.FromRgb(108, 99, 255));
            control.BorderThickness = new Thickness(2);

            // Add glow effect for better visibility
            control.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(108, 99, 255),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.5
            };

            // Add resize handles
            // TODO: Add corner and edge handles for resizing
        }

        private void ShowElementProperties(UIElementTemplate element)
        {
            ElementInfoPanel.Visibility = Visibility.Visible;
            // Update property fields
            // TODO: Bind property fields to element
        }

        private void UpdateLayersList()
        {
            LayersList.Items.Clear();
            foreach (var element in _currentLayout.Elements.OrderByDescending(e => e.ZIndex))
            {
                LayersList.Items.Add(new { Name = element.Name, Element = element });
            }
        }

        private void ToggleOrientation()
        {
            SaveUndoState();

            // Swap width and height
            var currentWidth = double.Parse(DeviceFrame.Width.ToString());
            var currentHeight = double.Parse(DeviceFrame.Height.ToString());
            
            UpdateDeviceFrame(currentHeight, currentWidth);

            // Load appropriate template
            var orientation = currentWidth > currentHeight ? Orientation.Vertical : Orientation.Horizontal;
            _currentLayout = _database.GetActiveLayout(orientation) 
                          ?? (orientation == Orientation.Vertical 
                              ? DefaultTemplates.CreatePortraitTemplate() 
                              : DefaultTemplates.CreateLandscapeTemplate());
            
            RefreshCanvas();

            // Animate rotation
            var rotation = new DoubleAnimation(0, 90, TimeSpan.FromMilliseconds(300));
            rotation.Completed += (s, e) =>
            {
                var reverseRotation = new DoubleAnimation(90, 0, TimeSpan.FromMilliseconds(300));
                DeviceFrame.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, reverseRotation);
            };
            
            DeviceFrame.RenderTransform = new RotateTransform();
            DeviceFrame.RenderTransformOrigin = new Point(0.5, 0.5);
            DeviceFrame.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, rotation);
        }

        private void UpdateDeviceFrame(double width, double height)
        {
            DeviceFrame.Width = width;
            DeviceFrame.Height = height;
        }

        private void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (ComboBoxItem)DeviceSelector.SelectedItem;
            var content = selected.Content.ToString();

            switch (content)
            {
                case "iPad (1024×768)":
                    UpdateDeviceFrame(1024, 768);
                    break;
                case "Desktop (1920×1080)":
                    UpdateDeviceFrame(1920, 1080);
                    break;
                case "Portrait (768×1024)":
                    UpdateDeviceFrame(768, 1024);
                    break;
                case "Custom...":
                    // Show custom size dialog
                    break;
            }
        }

        private void SaveUndoState()
        {
            // Deep clone current layout
            var clone = Newtonsoft.Json.JsonConvert.DeserializeObject<UILayoutTemplate>(
                Newtonsoft.Json.JsonConvert.SerializeObject(_currentLayout));
            _undoStack.Push(clone);
            _redoStack.Clear();
        }

        private void Undo()
        {
            if (_undoStack.Count > 0)
            {
                _redoStack.Push(_currentLayout);
                _currentLayout = _undoStack.Pop();
                RefreshCanvas();
            }
        }

        private void Redo()
        {
            if (_redoStack.Count > 0)
            {
                _undoStack.Push(_currentLayout);
                _currentLayout = _redoStack.Pop();
                RefreshCanvas();
            }
        }

        private void ShowPreview()
        {
            // TODO: Show preview window
        }

        private void SaveLayout()
        {
            _database.SaveLayout(_currentLayout);
            
            // Show save confirmation
            var notification = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(20, 10, 20, 10),
                Child = new TextBlock { Text = "Layout Saved!", Foreground = Brushes.White },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 100, 0, 0),
                Opacity = 0
            };

            Grid.SetRowSpan(notification, 3);
            ((Grid)Content).Children.Add(notification);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.BeginTime = TimeSpan.FromSeconds(2);
            fadeOut.Completed += (s, e) => ((Grid)Content).Children.Remove(notification);

            notification.BeginAnimation(OpacityProperty, fadeIn);
            notification.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void AlignElements(string alignment)
        {
            // TODO: Implement alignment logic
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Deselect if clicking on empty canvas
            ClearSelection();
            _selectedElement = null;
            ElementInfoPanel.Visibility = Visibility.Collapsed;
        }
        
        private void ClearSelection()
        {
            // Clear selection border and effects from all controls
            foreach (var control in _elementControls)
            {
                control.BorderBrush = Brushes.Transparent;
                control.BorderThickness = new Thickness(0);
                control.Effect = null;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            // Update cursor position display
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Handle canvas click
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Show context menu
            var position = e.GetPosition(this);
            ContextMenu.Margin = new Thickness(position.X, position.Y, 0, 0);
            ContextMenu.Visibility = Visibility.Visible;
        }

        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            // Handle file drop for images
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                // TODO: Add image element for dropped file
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Keyboard shortcuts
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.Z:
                        Undo();
                        break;
                    case Key.Y:
                        Redo();
                        break;
                    case Key.S:
                        SaveLayout();
                        break;
                    case Key.C:
                        // Copy
                        break;
                    case Key.V:
                        // Paste
                        break;
                }
            }
            else if (e.Key == Key.Delete && _selectedElement != null)
            {
                // Delete selected element
                SaveUndoState();
                _currentLayout.Elements.Remove(_selectedElement);
                _selectedElement = null;
                RefreshCanvas();
            }
        }
        
        #region Profile Management
        
        private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileSelector.SelectedItem is UILayoutProfile profile)
            {
                _currentProfile = profile;
                LoadProfileLayout();
            }
        }
        
        private void LoadProfileLayout()
        {
            if (_currentProfile == null) return;
            
            try
            {
                // Get current orientation
                string orientation = GetCurrentOrientation();
                
                if (_currentProfile.Layouts.ContainsKey(orientation))
                {
                    _currentLayout = _currentProfile.Layouts[orientation];
                    RefreshCanvas();
                }
                else
                {
                    // Create new layout for this orientation
                    _currentLayout = orientation == "Portrait" 
                        ? DefaultTemplates.CreatePortraitTemplate()
                        : DefaultTemplates.CreateLandscapeTemplate();
                    _currentProfile.Layouts[orientation] = _currentLayout;
                }
                
                // Update device selector to match profile
                UpdateDeviceFrame(
                    (int)_currentProfile.ScreenConfig.Resolution.Width,
                    (int)_currentProfile.ScreenConfig.Resolution.Height);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading profile: {ex.Message}", "Profile Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void NewProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "Create New Profile",
                Width = 400,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };
            
            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Name input
            var nameLabel = new TextBlock { Text = "Profile Name:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);
            
            var nameBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(nameBox, 1);
            grid.Children.Add(nameBox);
            
            // Device type
            var deviceLabel = new TextBlock { Text = "Device Type:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(deviceLabel, 2);
            grid.Children.Add(deviceLabel);
            
            var deviceCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            deviceCombo.Items.Add("Tablet");
            deviceCombo.Items.Add("Kiosk");
            deviceCombo.Items.Add("Desktop");
            deviceCombo.Items.Add("Surface");
            deviceCombo.SelectedIndex = 0;
            Grid.SetRow(deviceCombo, 3);
            grid.Children.Add(deviceCombo);
            
            // Resolution
            var resLabel = new TextBlock { Text = "Resolution:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(resLabel, 4);
            grid.Children.Add(resLabel);
            
            var resPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var widthBox = new TextBox { Width = 80, Text = "1920" };
            var xLabel = new TextBlock { Text = " x ", VerticalAlignment = VerticalAlignment.Center };
            var heightBox = new TextBox { Width = 80, Text = "1080" };
            resPanel.Children.Add(widthBox);
            resPanel.Children.Add(xLabel);
            resPanel.Children.Add(heightBox);
            Grid.SetRow(resPanel, 5);
            grid.Children.Add(resPanel);
            
            // Touch enabled
            var touchCheck = new CheckBox { Content = "Touch Enabled", IsChecked = true, Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(touchCheck, 6);
            grid.Children.Add(touchCheck);
            
            // Description
            var descLabel = new TextBlock { Text = "Description:", Margin = new Thickness(0, 0, 0, 5) };
            Grid.SetRow(descLabel, 7);
            grid.Children.Add(descLabel);
            
            var descBox = new TextBox { Height = 60, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
            Grid.SetRow(descBox, 8);
            grid.Children.Add(descBox);
            
            // Buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Create", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 80 };
            
            okButton.Click += (s, args) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    MessageBox.Show("Please enter a profile name.", "Validation Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                var newProfile = new UILayoutProfile
                {
                    Name = nameBox.Text,
                    Description = descBox.Text,
                    Category = deviceCombo.SelectedItem.ToString(),
                    ScreenConfig = new ScreenConfiguration
                    {
                        DeviceType = deviceCombo.SelectedItem.ToString(),
                        Resolution = new Size(
                            double.Parse(widthBox.Text),
                            double.Parse(heightBox.Text)),
                        IsTouchEnabled = touchCheck.IsChecked ?? true,
                        PreferredOrientation = double.Parse(widthBox.Text) > double.Parse(heightBox.Text) 
                            ? ScreenOrientation.Landscape 
                            : ScreenOrientation.Portrait
                    }
                };
                
                // Add default layouts
                newProfile.Layouts["Portrait"] = DefaultTemplates.CreatePortraitTemplate();
                newProfile.Layouts["Landscape"] = DefaultTemplates.CreateLandscapeTemplate();
                
                // Save to database
                _database.SaveProfile(newProfile);
                
                // Refresh profiles list
                LoadProfiles();
                ProfileSelector.SelectedItem = newProfile;
                
                dialog.DialogResult = true;
                dialog.Close();
            };
            
            cancelButton.Click += (s, args) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };
            
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 9);
            grid.Children.Add(buttonPanel);
            
            dialog.Content = grid;
            dialog.ShowDialog();
        }
        
        private void SaveProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfile == null)
            {
                MessageBox.Show("Please select a profile first.", "No Profile", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            try
            {
                // Update current layout in profile
                string orientation = GetCurrentOrientation();
                _currentProfile.Layouts[orientation] = _currentLayout;
                _currentProfile.LastUsedDate = DateTime.Now;
                
                // Save to database
                _database.SaveProfile(_currentProfile);
                
                MessageBox.Show($"Profile '{_currentProfile.Name}' saved successfully!", "Profile Saved", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving profile: {ex.Message}", "Save Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void DeleteProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfile == null)
            {
                MessageBox.Show("Please select a profile to delete.", "No Profile", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            if (_currentProfile.Metadata?.IsLocked == true)
            {
                MessageBox.Show("This profile is locked and cannot be deleted.", "Profile Locked", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var result = MessageBox.Show(
                $"Are you sure you want to delete the profile '{_currentProfile.Name}'?", 
                "Confirm Delete", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _database.DeleteProfile(_currentProfile.Id);
                    LoadProfiles();
                    MessageBox.Show("Profile deleted successfully.", "Profile Deleted", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting profile: {ex.Message}", "Delete Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ImportBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will import the current PhotoboothTouchModern layout.\nAny unsaved changes will be lost.\nDo you want to continue?",
                "Import Layout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var importedLayout = ImportFromPhotoboothTouchModern();
                if (importedLayout != null && importedLayout.Elements.Count > 0)
                {
                    _currentLayout = importedLayout;
                    RefreshCanvas();
                    
                    // Show success notification
                    var notification = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(20, 10, 20, 10),
                        Child = new TextBlock { Text = "Layout Imported Successfully!", Foreground = Brushes.White },
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 50, 0, 0)
                    };
                    
                    Grid.SetRow(notification, 1);
                    (this.Content as Grid).Children.Add(notification);
                    
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(2));
                    fadeOut.BeginTime = TimeSpan.FromSeconds(1);
                    fadeOut.Completed += (s, args) =>
                    {
                        (this.Content as Grid).Children.Remove(notification);
                    };
                    notification.BeginAnimation(OpacityProperty, fadeOut);
                }
                else
                {
                    MessageBox.Show("Failed to import layout.", "Import Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            // Check if there are unsaved changes
            if (_currentLayout != null && _currentLayout.ModifiedDate > _currentLayout.CreatedDate)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before exiting?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Save current layout
                    SaveLayout();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // Don't exit
                    return;
                }
            }
            
            // Close the customizer window or navigate back
            var window = Window.GetWindow(this);
            if (window != null)
            {
                // If this is in a popup window, close it
                if (window.GetType().Name == "ModernUICustomizationWindow" || 
                    window != Application.Current.MainWindow)
                {
                    window.Close();
                }
                else
                {
                    // Navigate back to main dashboard
                    var surfaceWindow = window as SurfacePhotoBoothWindow;
                    if (surfaceWindow != null)
                    {
                        // Return to main dashboard
                        surfaceWindow.ShowDashboard();
                    }
                }
            }
        }
        
        private string GetCurrentOrientation()
        {
            // Check if canvas is in portrait or landscape mode
            return DesignCanvas.ActualWidth > DesignCanvas.ActualHeight ? "Landscape" : "Portrait";
        }
        
        private UILayoutTemplate ImportFromPhotoboothTouchModern()
        {
            try
            {
                var layout = new UILayoutTemplate
                {
                    Id = "photobooth-modern-import",
                    Name = "PhotoBooth Modern Layout",
                    Description = "Imported from PhotoboothTouchModern",
                    Elements = new List<UIElementTemplate>(),
                    Theme = new UITheme
                    {
                        PrimaryColor = System.Windows.Media.Color.FromRgb(76, 175, 80),
                        SecondaryColor = System.Windows.Media.Color.FromRgb(33, 150, 243),
                        AccentColor = System.Windows.Media.Color.FromRgb(255, 193, 7),
                        BackgroundColor = System.Windows.Media.Color.FromRgb(26, 26, 46),
                        TextColor = System.Windows.Media.Colors.White
                    }
                };

                // Camera Preview (center)
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "camera-preview",
                    Name = "Camera Preview",
                    Type = ElementType.Camera,
                    Anchor = AnchorPoint.Center,
                    AnchorOffset = new Point(0, 0),
                    SizeMode = SizeMode.Relative,
                    RelativeSize = new Size(0.9, 0.85),
                    MinSize = new Size(640, 480),
                    ZIndex = 0,
                    IsVisible = true,
                    IsEnabled = true,
                    Properties = new Dictionary<string, object>
                    {
                        { "BorderThickness", 5 },
                        { "BorderColor", "#FFFFFF" },
                        { "CornerRadius", 20 }
                    }
                });

                // Start Button (center overlay)
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "start-button",
                    Name = "Start Session",
                    Type = ElementType.Button,
                    Anchor = AnchorPoint.Center,
                    AnchorOffset = new Point(0, 0),
                    SizeMode = SizeMode.Fixed,
                    MinSize = new Size(300, 300),
                    MaxSize = new Size(300, 300),
                    ZIndex = 10,
                    IsVisible = true,
                    IsEnabled = true,
                    ActionCommand = "StartPhotoSession",
                    Properties = new Dictionary<string, object>
                    {
                        { "Text", "START" },
                        { "FontSize", 48 },
                        { "BackgroundColor", "#4CAF50" },
                        { "CornerRadius", 150 },
                        { "IconSize", 80 },
                        { "Icon", "📸" }
                    }
                });

                // Settings Button (top left)
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "settings-button",
                    Name = "Settings",
                    Type = ElementType.Button,
                    Anchor = AnchorPoint.TopLeft,
                    AnchorOffset = new Point(20, 20),
                    SizeMode = SizeMode.Fixed,
                    MinSize = new Size(60, 60),
                    MaxSize = new Size(60, 60),
                    ZIndex = 5,
                    IsVisible = true,
                    IsEnabled = true,
                    ActionCommand = "OpenSettings",
                    Properties = new Dictionary<string, object>
                    {
                        { "Text", "⚙️" },
                        { "FontSize", 28 },
                        { "BackgroundColor", "#2196F3" },
                        { "CornerRadius", 30 }
                    }
                });

                // Gallery Button (top right)
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "gallery-button",
                    Name = "Gallery",
                    Type = ElementType.Button,
                    Anchor = AnchorPoint.TopRight,
                    AnchorOffset = new Point(-20, 20),
                    SizeMode = SizeMode.Fixed,
                    MinSize = new Size(60, 60),
                    MaxSize = new Size(60, 60),
                    ZIndex = 5,
                    IsVisible = true,
                    IsEnabled = true,
                    ActionCommand = "OpenGallery",
                    Properties = new Dictionary<string, object>
                    {
                        { "Text", "🖼️" },
                        { "FontSize", 28 },
                        { "BackgroundColor", "#FF9800" },
                        { "CornerRadius", 30 }
                    }
                });

                // Stop Button (shown during session)
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "stop-button",
                    Name = "Stop Session",
                    Type = ElementType.Button,
                    Anchor = AnchorPoint.TopRight,
                    AnchorOffset = new Point(-20, 20),
                    SizeMode = SizeMode.Fixed,
                    MinSize = new Size(50, 50),
                    MaxSize = new Size(50, 50),
                    ZIndex = 15,
                    IsVisible = false, // Hidden by default
                    IsEnabled = true,
                    ActionCommand = "StopSession",
                    Properties = new Dictionary<string, object>
                    {
                        { "Text", "✕" },
                        { "FontSize", 24 },
                        { "BackgroundColor", "#F44336" },
                        { "CornerRadius", 25 }
                    }
                });

                // Countdown Overlay
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "countdown-overlay",
                    Name = "Countdown",
                    Type = ElementType.Countdown,
                    Anchor = AnchorPoint.Center,
                    AnchorOffset = new Point(0, 0),
                    SizeMode = SizeMode.Fixed,
                    MinSize = new Size(400, 400),
                    MaxSize = new Size(400, 400),
                    ZIndex = 20,
                    IsVisible = false, // Hidden by default
                    IsEnabled = false,
                    Properties = new Dictionary<string, object>
                    {
                        { "FontSize", 200 },
                        { "TextColor", "#FFFFFF" },
                        { "BackgroundColor", "#80000000" },
                        { "CornerRadius", 200 }
                    }
                });

                // Session Info Text (bottom)
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "session-info",
                    Name = "Session Info",
                    Type = ElementType.Text,
                    Anchor = AnchorPoint.BottomCenter,
                    AnchorOffset = new Point(0, -50),
                    SizeMode = SizeMode.Fixed,
                    MinSize = new Size(400, 50),
                    ZIndex = 5,
                    IsVisible = true,
                    IsEnabled = false,
                    Properties = new Dictionary<string, object>
                    {
                        { "Text", "Touch START to begin" },
                        { "FontSize", 18 },
                        { "TextColor", "#FFFFFF" },
                        { "BackgroundColor", "#80000000" },
                        { "CornerRadius", 25 },
                        { "Padding", 10 }
                    }
                });

                // Home Button (bottom left)
                layout.Elements.Add(new UIElementTemplate
                {
                    Id = "home-button",
                    Name = "Home",
                    Type = ElementType.Button,
                    Anchor = AnchorPoint.BottomLeft,
                    AnchorOffset = new Point(20, -20),
                    SizeMode = SizeMode.Fixed,
                    MinSize = new Size(60, 60),
                    MaxSize = new Size(60, 60),
                    ZIndex = 5,
                    IsVisible = true,
                    IsEnabled = true,
                    ActionCommand = "ReturnHome",
                    Properties = new Dictionary<string, object>
                    {
                        { "Text", "🏠" },
                        { "FontSize", 28 },
                        { "BackgroundColor", "#9C27B0" },
                        { "CornerRadius", 30 }
                    }
                });

                return layout;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error importing layout: {ex.Message}");
                return null;
            }
        }
        
        #endregion
    }

    // Helper class to wrap UI elements
    public class UIElementControl : Border
    {
        public UIElementTemplate Element { get; set; }
        public FrameworkElement Visual { get; set; }

        public UIElementControl(UIElementTemplate element)
        {
            Element = element;
            Background = Brushes.Transparent;
            BorderBrush = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            MinWidth = 50;
            MinHeight = 50;
            Cursor = Cursors.SizeAll;
        }

        public new FrameworkElement Child
        {
            get => Visual;
            set
            {
                Visual = value;
                base.Child = value;
            }
        }
    }
}