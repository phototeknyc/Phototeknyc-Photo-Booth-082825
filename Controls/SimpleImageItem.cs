using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Controls
{
    /// <summary>
    /// Simple image item that can be manipulated on the canvas
    /// </summary>
    public class SimpleImageItem : SimpleCanvasItem
    {
        // Placeholder properties
        public static readonly DependencyProperty PlaceholderNameProperty =
            DependencyProperty.Register("PlaceholderName", typeof(string), typeof(SimpleImageItem),
                new PropertyMetadata(string.Empty, OnPlaceholderNameChanged));

        public static readonly DependencyProperty PlaceholderBackgroundProperty =
            DependencyProperty.Register("PlaceholderBackground", typeof(Brush), typeof(SimpleImageItem),
                new PropertyMetadata(null, OnPlaceholderBackgroundChanged));
        // Stroke properties
        public static readonly DependencyProperty StrokeBrushProperty =
            DependencyProperty.Register("StrokeBrush", typeof(Brush), typeof(SimpleImageItem),
                new PropertyMetadata(Brushes.DarkGray, OnStrokeChanged));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register("StrokeThickness", typeof(double), typeof(SimpleImageItem),
                new PropertyMetadata(2.0, OnStrokeChanged));
        // Static placeholder tracking
        private static int _totalPlaceholders = 0;
        private int _placeholderNumber = 0;

        // Method to reset the placeholder counter
        public static void ResetPlaceholderCounter()
        {
            _totalPlaceholders = 0;
        }

        // Method to update the counter to track the highest placeholder number
        public static void UpdatePlaceholderCounter(int highestNumber)
        {
            if (highestNumber > _totalPlaceholders)
            {
                _totalPlaceholders = highestNumber;
            }
        }

        // Predefined color palette for placeholders - professional and visually distinct colors
        private static readonly Color[] ColorPalette = new Color[]
        {
            Color.FromRgb(255, 182, 193), // Light Pink
            Color.FromRgb(173, 216, 230), // Light Blue
            Color.FromRgb(152, 251, 152), // Pale Green
            Color.FromRgb(255, 218, 185), // Peach
            Color.FromRgb(221, 160, 221), // Plum
            Color.FromRgb(255, 255, 224), // Light Yellow
            Color.FromRgb(176, 224, 230), // Powder Blue
            Color.FromRgb(255, 228, 225), // Misty Rose
            Color.FromRgb(240, 230, 140), // Khaki
            Color.FromRgb(255, 192, 203), // Pink
            Color.FromRgb(135, 206, 235), // Sky Blue
            Color.FromRgb(144, 238, 144), // Light Green
            Color.FromRgb(255, 160, 122), // Light Salmon
            Color.FromRgb(216, 191, 216), // Thistle
            Color.FromRgb(255, 239, 213), // Papaya Whip
            Color.FromRgb(175, 238, 238), // Pale Turquoise
            Color.FromRgb(255, 245, 238), // Seashell
            Color.FromRgb(250, 250, 210), // Light Goldenrod Yellow
        };

        public int PlaceholderNumber
        {
            get => _placeholderNumber;
            set
            {
                _placeholderNumber = Math.Max(1, value); // Ensure minimum value of 1
                UpdatePlaceholderVisibility();
                UpdatePlaceholderColor();
            }
        }
        // Dependency properties for image properties
        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(ImageSource), typeof(SimpleImageItem),
                new PropertyMetadata(null, OnImageSourceChanged));

        public static readonly DependencyProperty StretchProperty =
            DependencyProperty.Register("Stretch", typeof(Stretch), typeof(SimpleImageItem),
                new PropertyMetadata(Stretch.Uniform, OnStretchChanged));

        public static readonly DependencyProperty ImagePathProperty =
            DependencyProperty.Register("ImagePath", typeof(string), typeof(SimpleImageItem),
                new PropertyMetadata(string.Empty, OnImagePathChanged));

        public static readonly DependencyProperty IsPlaceholderProperty =
            DependencyProperty.Register("IsPlaceholder", typeof(bool), typeof(SimpleImageItem),
                new PropertyMetadata(false, OnIsPlaceholderChanged));

        // Properties
        public ImageSource ImageSource
        {
            get => (ImageSource)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        public Stretch Stretch
        {
            get => (Stretch)GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        public string ImagePath
        {
            get => (string)GetValue(ImagePathProperty);
            set => SetValue(ImagePathProperty, value);
        }

        public bool IsPlaceholder
        {
            get => (bool)GetValue(IsPlaceholderProperty);
            set => SetValue(IsPlaceholderProperty, value);
        }

        public string PlaceholderName
        {
            get => (string)GetValue(PlaceholderNameProperty);
            set => SetValue(PlaceholderNameProperty, value);
        }

        public Brush PlaceholderBackground
        {
            get => (Brush)GetValue(PlaceholderBackgroundProperty);
            set => SetValue(PlaceholderBackgroundProperty, value);
        }

        public Brush StrokeBrush
        {
            get => (Brush)GetValue(StrokeBrushProperty);
            set => SetValue(StrokeBrushProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        // UI elements
        private Image _image;
        private Border _border;
        private TextBlock _placeholderIcon;
        private TextBlock _placeholderLabel;
        private Grid _containerGrid;

        public SimpleImageItem() : base()
        {
            InitializeImageItem();
        }

        protected override void PositionSelectionHandles()
        {
            // For images, place handles around the inner image content (inside the stroke/border)
            if (_selectionHandles == null)
            {
                base.PositionSelectionHandles();
                return;
            }

            // Determine inner content bounds by subtracting the current stroke/border thickness
            double borderThickness = 0;
            if (_border != null)
            {
                // BorderThickness is uniform, driven by StrokeThickness
                borderThickness = _border.BorderThickness.Left;
            }

            double contentLeft = Left + borderThickness;
            double contentTop = Top + borderThickness;
            double contentWidth = Math.Max(0, Width - (borderThickness * 2));
            double contentHeight = Math.Max(0, Height - (borderThickness * 2));

            // Fallback to base behavior if content is degenerate
            if (contentWidth <= 0 || contentHeight <= 0)
            {
                base.PositionSelectionHandles();
                return;
            }

            var halfHandle = HandleSize / 2;

            // Corner handles
            Canvas.SetLeft(_selectionHandles[0], contentLeft - halfHandle); // TopLeft
            Canvas.SetTop(_selectionHandles[0], contentTop - halfHandle);

            Canvas.SetLeft(_selectionHandles[1], contentLeft + contentWidth - halfHandle); // TopRight
            Canvas.SetTop(_selectionHandles[1], contentTop - halfHandle);

            Canvas.SetLeft(_selectionHandles[2], contentLeft - halfHandle); // BottomLeft
            Canvas.SetTop(_selectionHandles[2], contentTop + contentHeight - halfHandle);

            Canvas.SetLeft(_selectionHandles[3], contentLeft + contentWidth - halfHandle); // BottomRight
            Canvas.SetTop(_selectionHandles[3], contentTop + contentHeight - halfHandle);

            // Edge handles
            Canvas.SetLeft(_selectionHandles[4], contentLeft + contentWidth / 2 - halfHandle); // Top
            Canvas.SetTop(_selectionHandles[4], contentTop - halfHandle);

            Canvas.SetLeft(_selectionHandles[5], contentLeft + contentWidth / 2 - halfHandle); // Bottom
            Canvas.SetTop(_selectionHandles[5], contentTop + contentHeight - halfHandle);

            Canvas.SetLeft(_selectionHandles[6], contentLeft - halfHandle); // Left
            Canvas.SetTop(_selectionHandles[6], contentTop + contentHeight / 2 - halfHandle);

            Canvas.SetLeft(_selectionHandles[7], contentLeft + contentWidth - halfHandle); // Right
            Canvas.SetTop(_selectionHandles[7], contentTop + contentHeight / 2 - halfHandle);

            // Rotate handle and line relative to inner top edge
            if (_rotateHandle != null)
            {
                var rotateHandleHalf = (_rotateHandle.Width / 2);
                Canvas.SetLeft(_rotateHandle, contentLeft + contentWidth / 2 - rotateHandleHalf);
                Canvas.SetTop(_rotateHandle, contentTop - RotateHandleDistance - rotateHandleHalf);
            }

            if (_rotateHandleLine != null)
            {
                _rotateHandleLine.X1 = contentLeft + contentWidth / 2;
                _rotateHandleLine.Y1 = contentTop;
                _rotateHandleLine.X2 = contentLeft + contentWidth / 2;
                _rotateHandleLine.Y2 = contentTop - RotateHandleDistance;
            }
        }

        public SimpleImageItem(string imagePath) : this()
        {
            LoadImage(imagePath);
        }

        public SimpleImageItem(bool isPlaceholder, int? specificPlaceholderNumber = null) : this()
        {
            if (isPlaceholder)
            {
                if (specificPlaceholderNumber.HasValue)
                {
                    // Use the specific placeholder number provided
                    PlaceholderNumber = specificPlaceholderNumber.Value;
                    // Update the total counter if needed
                    if (specificPlaceholderNumber.Value > _totalPlaceholders)
                    {
                        _totalPlaceholders = specificPlaceholderNumber.Value;
                    }
                }
                else
                {
                    // Auto-increment as before
                    _totalPlaceholders++;
                    PlaceholderNumber = _totalPlaceholders;
                }
            }
            // Set IsPlaceholder AFTER setting the number to avoid double increment
            IsPlaceholder = isPlaceholder;
        }

        protected override void InitializeItem()
        {
            base.InitializeItem();

            // Set default size for image items
            Width = 200;
            Height = 150;
        }

        private void InitializeImageItem()
        {
            // Create the visual structure
            _containerGrid = new Grid();

            _border = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(2),
                Child = _containerGrid
            };

            _image = new Image
            {
                Stretch = Stretch,
                StretchDirection = StretchDirection.Both
            };

            // Placeholder icon + label (large and legible)
            var placeholderStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _placeholderIcon = new TextBlock
            {
                Text = "\uE114", // Camera glyph in Segoe MDL2 Assets
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 48,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkGray,
                TextAlignment = TextAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 4,
                    ShadowDepth = 1,
                    Opacity = 0.5
                }
            };

            _placeholderLabel = new TextBlock
            {
                Text = "Photo 1",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkGray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 3,
                    ShadowDepth = 1,
                    Opacity = 0.5
                }
            };

            placeholderStack.Children.Add(_placeholderIcon);
            placeholderStack.Children.Add(_placeholderLabel);

            _containerGrid.Children.Add(_image);
            _containerGrid.Children.Add(placeholderStack);

            Content = _border;

            // Update visual when selection changes
            SelectionChanged += OnSelectionChanged;

            // Respond to size changes to scale placeholder typography
            SizeChanged += (s, e) => UpdatePlaceholderSizing();

            // Set initial state
            UpdatePlaceholderVisibility();

            // Add number change buttons for placeholders
            if (IsPlaceholder)
            {
                AddNumberChangeButtons();
            }
        }

        private Button _incrementButton;
        private Button _decrementButton;
        private StackPanel _numberButtonsPanel;

        private void AddNumberChangeButtons()
        {
            // Create buttons panel
            _numberButtonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            };

            // Create decrement button
            _decrementButton = new Button
            {
                Content = "-",
                Width = 40,
                Height = 40,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 107, 107)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(5, 0, 5, 0),
                Cursor = Cursors.Hand
            };
            _decrementButton.Click += (s, e) =>
            {
                if (_placeholderNumber > 1)
                {
                    PlaceholderNumber--;
                }
                e.Handled = true;
            };

            // Create increment button
            _incrementButton = new Button
            {
                Content = "+",
                Width = 40,
                Height = 40,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(200, 76, 175, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(5, 0, 5, 0),
                Cursor = Cursors.Hand
            };
            _incrementButton.Click += (s, e) =>
            {
                if (_placeholderNumber < 999)
                {
                    PlaceholderNumber++;
                }
                e.Handled = true;
            };

            _numberButtonsPanel.Children.Add(_decrementButton);
            _numberButtonsPanel.Children.Add(_incrementButton);

            // Add to the container grid
            _containerGrid.Children.Add(_numberButtonsPanel);
        }

        private void UpdatePlaceholderColor()
        {
            if (!IsPlaceholder) return;

            // Ensure placeholder number is at least 1
            int number = Math.Max(1, _placeholderNumber);
            var colorIndex = (number - 1) % 18;

            var colors = new Color[]
            {
                Color.FromRgb(255, 182, 193), // Light Pink
                Color.FromRgb(173, 216, 230), // Light Blue
                Color.FromRgb(152, 251, 152), // Pale Green
                Color.FromRgb(255, 218, 185), // Peach
                Color.FromRgb(221, 160, 221), // Plum
                Color.FromRgb(255, 255, 224), // Light Yellow
                Color.FromRgb(176, 224, 230), // Powder Blue
                Color.FromRgb(255, 228, 225), // Misty Rose
                Color.FromRgb(240, 230, 140), // Khaki
                Color.FromRgb(255, 192, 203), // Pink
                Color.FromRgb(135, 206, 235), // Sky Blue
                Color.FromRgb(144, 238, 144), // Light Green
                Color.FromRgb(255, 160, 122), // Light Salmon
                Color.FromRgb(216, 191, 216), // Thistle
                Color.FromRgb(255, 239, 213), // Papaya Whip
                Color.FromRgb(175, 238, 238), // Pale Turquoise
                Color.FromRgb(255, 245, 238), // Seashell
                Color.FromRgb(250, 250, 210), // Light Goldenrod Yellow
            };

            PlaceholderBackground = new SolidColorBrush(colors[colorIndex]);
        }

        private SimpleDesignerCanvas GetParentCanvas()
        {
            DependencyObject parent = this;
            while (parent != null)
            {
                parent = VisualTreeHelper.GetParent(parent);
                if (parent is SimpleDesignerCanvas)
                    return parent as SimpleDesignerCanvas;
            }
            return null;
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            UpdateSelectionVisual();

            // Show/hide number change buttons for placeholders
            if (IsPlaceholder && _numberButtonsPanel != null)
            {
                _numberButtonsPanel.Visibility = IsSelected ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateSelectionVisual()
        {
            if (_border != null)
            {
                // Preserve stroke as primary outline; selection via handles
                _border.BorderBrush = StrokeBrush ?? Brushes.DarkGray;
                _border.BorderThickness = new Thickness(Math.Max(0, StrokeThickness));
            }
        }

        private void UpdatePlaceholderVisibility()
        {
            if (_placeholderLabel != null && _placeholderIcon != null && _image != null)
            {
                _placeholderLabel.Visibility = (IsPlaceholder || ImageSource == null) ? Visibility.Visible : Visibility.Collapsed;
                _placeholderIcon.Visibility = (IsPlaceholder || ImageSource == null) ? Visibility.Visible : Visibility.Collapsed;
                _image.Visibility = (IsPlaceholder || ImageSource == null) ? Visibility.Collapsed : Visibility.Visible;

                if (IsPlaceholder)
                {
                    // Use explicit placeholder background if provided, else color from palette
                    if (PlaceholderBackground is SolidColorBrush setBrush)
                    {
                        _border.Background = setBrush;
                    }
                    else
                    {
                        var color = GetColorForPlaceholder(_placeholderNumber);
                        _border.Background = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B));
                    }
                    if (StrokeBrush == null || StrokeBrush == Brushes.DarkGray)
                    {
                        Color baseColor;
                        if (_border.Background is SolidColorBrush bgForStroke)
                            baseColor = bgForStroke.Color;
                        else
                            baseColor = Colors.DarkGray;

                        _border.BorderBrush = new SolidColorBrush(Color.FromArgb(255,
                            (byte)(baseColor.R * 0.7),
                            (byte)(baseColor.G * 0.7),
                            (byte)(baseColor.B * 0.7)));
                    }
                    else
                    {
                        _border.BorderBrush = StrokeBrush;
                    }
                    _border.BorderThickness = new Thickness(Math.Max(0, StrokeThickness));

                    var label = string.IsNullOrWhiteSpace(PlaceholderName) ? $"Photo {_placeholderNumber}" : PlaceholderName;
                    _placeholderLabel.Text = label;

                    // Adjust text color for readability based on background
                    // Compute brightness from current background
                    Color bgColor;
                    if (_border.Background is SolidColorBrush bg)
                        bgColor = bg.Color;
                    else
                    {
                        var c = GetColorForPlaceholder(_placeholderNumber);
                        bgColor = Color.FromRgb(c.R, c.G, c.B);
                    }
                    var brightness = (bgColor.R * 0.299 + bgColor.G * 0.587 + bgColor.B * 0.114);
                    var fg = brightness > 150 ? Brushes.Black : Brushes.White;
                    _placeholderLabel.Foreground = fg;
                    _placeholderIcon.Foreground = fg;

                    UpdatePlaceholderSizing();
                }
                else
                {
                    _border.Background = Brushes.White;
                    _border.BorderBrush = StrokeBrush ?? Brushes.DarkGray;
                    _border.BorderThickness = new Thickness(Math.Max(0, StrokeThickness));
                }
            }
        }

        private void UpdatePlaceholderSizing()
        {
            // Scale icon and label sizes relative to current item size for legibility
            double h = Height;
            double w = Width;
            if (double.IsNaN(h) || h <= 0) h = 150;
            if (double.IsNaN(w) || w <= 0) w = 200;
            double minDim = Math.Min(w, h);

            // Icon about 35% of the smaller dimension, label about 15%
            double iconSize = Math.Max(28, Math.Round(minDim * 0.35));
            double labelSize = Math.Max(14, Math.Round(minDim * 0.15));

            if (_placeholderIcon != null) _placeholderIcon.FontSize = iconSize;
            if (_placeholderLabel != null) _placeholderLabel.FontSize = labelSize;
        }

        private static Color GetColorForPlaceholder(int placeholderNumber)
        {
            if (placeholderNumber <= 0) return ColorPalette[0];
            return ColorPalette[(placeholderNumber - 1) % ColorPalette.Length];
        }

        private static void OnPlaceholderNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleImageItem item)
            {
                item.UpdatePlaceholderVisibility();
                item.OnPropertyChanged("PlaceholderName");
            }
        }

        private static void OnPlaceholderBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleImageItem item)
            {
                item.UpdatePlaceholderVisibility();
                item.OnPropertyChanged("PlaceholderBackground");
            }
        }

        private static void OnStrokeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleImageItem item && item._border != null)
            {
                item._border.BorderBrush = item.StrokeBrush ?? Brushes.DarkGray;
                item._border.BorderThickness = new Thickness(Math.Max(0, item.StrokeThickness));
                item.OnPropertyChanged("Stroke");
                // Reposition handles to stay aligned with the new inner edge
                item.UpdateSelectionHandles();
            }
        }

        // Load image from file path
        public bool LoadImage(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return false;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                ImageSource = bitmap;
                ImagePath = filePath;
                IsPlaceholder = false;

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load image: {ex.Message}");
                return false;
            }
        }

        // Load image from byte array
        public bool LoadImage(byte[] imageData)
        {
            try
            {
                if (imageData == null || imageData.Length == 0)
                {
                    return false;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(imageData);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                ImageSource = bitmap;
                IsPlaceholder = false;

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load image from byte array: {ex.Message}");
                return false;
            }
        }

        // Clear image and revert to placeholder
        public void ClearImage()
        {
            ImageSource = null;
            ImagePath = string.Empty;
            IsPlaceholder = true;
        }

        // Fit image to maintain aspect ratio within bounds
        public void FitToAspectRatio()
        {
            if (ImageSource == null) return;

            var imageWidth = ImageSource.Width;
            var imageHeight = ImageSource.Height;

            if (imageWidth <= 0 || imageHeight <= 0) return;

            var aspectRatio = imageWidth / imageHeight;
            var currentAspectRatio = Width / Height;

            if (aspectRatio > currentAspectRatio)
            {
                // Image is wider - fit to width
                Height = Width / aspectRatio;
            }
            else
            {
                // Image is taller - fit to height
                Width = Height * aspectRatio;
            }
        }

        // Event handlers for property changes
        private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleImageItem item && item._image != null)
            {
                item._image.Source = (ImageSource)e.NewValue;
                item.UpdatePlaceholderVisibility();
                item.OnPropertyChanged("ImageSource");
            }
        }

        private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleImageItem item && item._image != null)
            {
                item._image.Stretch = (Stretch)e.NewValue;
                item.OnPropertyChanged("Stretch");
            }
        }

        private static void OnImagePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleImageItem item)
            {
                item.OnPropertyChanged("ImagePath");
            }
        }

        private static void OnIsPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SimpleImageItem item)
            {
                // If changing to placeholder and no number assigned, assign one
                if ((bool)e.NewValue && item._placeholderNumber == 0)
                {
                    _totalPlaceholders++;
                    item.PlaceholderNumber = _totalPlaceholders;
                }

                item.UpdatePlaceholderVisibility();
                item.OnPropertyChanged("IsPlaceholder");

                // Add or remove number change buttons based on placeholder status
                if ((bool)e.NewValue && item._numberButtonsPanel == null)
                {
                    item.AddNumberChangeButtons();
                }
                else if (!(bool)e.NewValue && item._numberButtonsPanel != null)
                {
                    item._containerGrid?.Children.Remove(item._numberButtonsPanel);
                    item._numberButtonsPanel = null;
                    item._incrementButton = null;
                    item._decrementButton = null;
                }
            }
        }

        // Override abstract methods
        public override string GetDisplayName()
        {
            if (IsPlaceholder)
            {
                return "Photo Placeholder";
            }

            if (!string.IsNullOrEmpty(ImagePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(ImagePath);
                if (fileName.Length > 20)
                    fileName = fileName.Substring(0, 17) + "...";
                return $"Image: {fileName}";
            }

            return "Image";
        }

        public override SimpleCanvasItem Clone()
        {
            SimpleImageItem clone;

            // Create clone with proper initialization based on type
            if (this.IsPlaceholder)
            {
                // Create as placeholder with the correct number from the start
                clone = new SimpleImageItem(true, this.PlaceholderNumber);
                clone.PlaceholderName = this.PlaceholderName;
                clone.PlaceholderBackground = this.PlaceholderBackground;
            }
            else
            {
                // Create as regular image
                clone = new SimpleImageItem();
                clone.ImageSource = this.ImageSource;
                clone.ImagePath = this.ImagePath;
            }

            // Copy common properties
            clone.Stretch = this.Stretch;
            clone.Width = this.Width;
            clone.Height = this.Height;
            clone.Left = this.Left + 10; // Slight offset for visual clarity
            clone.Top = this.Top + 10;
            clone.ZIndex = this.ZIndex;
            clone.RotationAngle = this.RotationAngle;
            // Copy stroke (outline) settings
            clone.StrokeBrush = this.StrokeBrush;
            clone.StrokeThickness = this.StrokeThickness;

            // Copy any visual Effect (e.g., drop shadow)
            if (this.Effect != null)
            {
                try { clone.Effect = this.Effect.Clone(); } catch { clone.Effect = this.Effect; }
            }

            return clone;
        }

        // Drag and drop support methods
        public bool CanAcceptDrop(string[] fileNames)
        {
            if (fileNames == null || fileNames.Length == 0)
                return false;

            var extension = System.IO.Path.GetExtension(fileNames[0])?.ToLower();
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".gif":
                case ".tiff":
                case ".webp":
                    return true;
                default:
                    return false;
            }
        }

        public bool AcceptDrop(string[] fileNames)
        {
            if (!CanAcceptDrop(fileNames))
                return false;

            return LoadImage(fileNames[0]);
        }

        // Export the image source for saving templates
        public ImageSource GetImageForExport()
        {
            return ImageSource;
        }

        // Get image data as byte array for database storage
        public byte[] GetImageData()
        {
            try
            {
                if (ImageSource is BitmapSource bitmapSource)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                    using (var stream = new MemoryStream())
                    {
                        encoder.Save(stream);
                        return stream.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get image data: {ex.Message}");
            }

            return null;
        }
    }
}
