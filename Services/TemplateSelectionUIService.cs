using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Photobooth.Models;
using Photobooth.Database;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles all UI operations for template selection
    /// Keeps the page thin and maintains clean architecture
    /// </summary>
    public class TemplateSelectionUIService
    {
        #region Events
        public event EventHandler<TemplateUIEventArgs> ShowOverlayRequested;
        public event EventHandler<TemplateUIEventArgs> HideOverlayRequested;
        public event EventHandler<TemplateUIEventArgs> UpdateOverlayRequested;
        public event EventHandler<TemplateCardClickedEventArgs> TemplateCardClicked;
        #endregion

        #region Private Fields
        private readonly List<TemplateDisplayData> _templateDisplayData;
        private Grid _overlayGrid;
        private ItemsControl _templatesGrid;
        private TextBlock _titleText;
        private TextBlock _subtitleText;
        #endregion

        #region Constructor
        public TemplateSelectionUIService()
        {
            _templateDisplayData = new List<TemplateDisplayData>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Initialize UI controls (called once from page)
        /// </summary>
        public void InitializeControls(Grid overlayGrid, ItemsControl templatesGrid, TextBlock titleText, TextBlock subtitleText)
        {
            _overlayGrid = overlayGrid;
            _templatesGrid = templatesGrid;
            _titleText = titleText;
            _subtitleText = subtitleText;
        }

        /// <summary>
        /// Show template selection overlay
        /// </summary>
        public void ShowTemplateSelection(EventData eventData, List<TemplateData> templates)
        {
            try
            {
                Log.Debug($"TemplateSelectionUIService.ShowTemplateSelection: Called with event '{eventData?.Name}' and {templates?.Count ?? 0} templates");
                
                if (_overlayGrid == null || _templatesGrid == null)
                {
                    Log.Error($"TemplateSelectionUIService: UI controls not initialized");
                    Log.Error($"  _overlayGrid: {_overlayGrid != null}, _templatesGrid: {_templatesGrid != null}");
                    return;
                }

                // Prepare display data
                var displayData = PrepareTemplateDisplayData(templates);
                Log.Debug($"TemplateSelectionUIService: Prepared {displayData.Count} display items");

                // Update UI elements
                UpdateOverlayTitle(eventData);
                UpdateTemplatesGrid(displayData);

                // Show overlay
                ShowOverlay();

                Log.Debug($"TemplateSelectionUIService: Showing {templates.Count} templates for selection - overlay should now be visible");
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateSelectionUIService: Error showing template selection: {ex.Message}");
                Log.Error($"TemplateSelectionUIService: Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Hide template selection overlay
        /// </summary>
        public void HideTemplateSelection()
        {
            try
            {
                HideOverlay();
                ClearTemplatesGrid();
                Log.Debug("TemplateSelectionUIService: Template selection hidden");
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateSelectionUIService: Error hiding template selection: {ex.Message}");
            }
        }

        /// <summary>
        /// Generate preview canvas for a template
        /// </summary>
        public Canvas GenerateTemplatePreview(TemplateData template, double maxWidth = 300, double maxHeight = 400)
        {
            try
            {
                if (template == null) return null;

                var previewGenerator = new TemplatePreviewGenerator();
                return previewGenerator.GeneratePreview(template, maxWidth, maxHeight);
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateSelectionUIService: Error generating preview: {ex.Message}");
                return CreateErrorPreview(maxWidth, maxHeight);
            }
        }

        /// <summary>
        /// Handle template card click
        /// </summary>
        public void HandleTemplateCardClick(object templateData)
        {
            try
            {
                var displayData = templateData as TemplateDisplayData;
                if (displayData?.Template != null)
                {
                    Log.Debug($"TemplateSelectionUIService: Template card clicked - {displayData.Name}");
                    OnTemplateCardClicked(displayData.Template);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateSelectionUIService: Error handling template click: {ex.Message}");
            }
        }
        #endregion

        #region Private Methods
        private List<TemplateDisplayData> PrepareTemplateDisplayData(List<TemplateData> templates)
        {
            _templateDisplayData.Clear();

            if (templates == null) return _templateDisplayData;

            var database = new TemplateDatabase();
            
            foreach (var template in templates)
            {
                // Load template items to get actual photo count
                var items = database.GetCanvasItems(template.Id);
                int photoCount = items?.Count(item => item.ItemType == "Placeholder") ?? 1;
                
                var displayData = new TemplateDisplayData
                {
                    Template = template,
                    Name = template.Name,
                    PhotoCount = photoCount,
                    Dimensions = $"{template.CanvasWidth}x{template.CanvasHeight}",
                    Preview = GenerateTemplatePreview(template, 280, 320),
                    IsSelected = false
                };

                _templateDisplayData.Add(displayData);
            }

            return _templateDisplayData;
        }


        private void UpdateOverlayTitle(EventData eventData)
        {
            if (_titleText != null)
            {
                _titleText.Text = eventData != null 
                    ? $"Select Template for {eventData.Name}" 
                    : "Select Template";
            }

            if (_subtitleText != null)
            {
                _subtitleText.Text = "Choose your photo layout";
            }
        }

        private void UpdateTemplatesGrid(List<TemplateDisplayData> displayData)
        {
            if (_templatesGrid != null)
            {
                _templatesGrid.ItemsSource = null;
                _templatesGrid.ItemsSource = displayData;
            }
        }

        private void ShowOverlay()
        {
            Log.Debug($"TemplateSelectionUIService.ShowOverlay: Setting overlay visibility to Visible");
            
            if (_overlayGrid != null)
            {
                _overlayGrid.Visibility = Visibility.Visible;
                Log.Debug($"TemplateSelectionUIService.ShowOverlay: Overlay visibility set to {_overlayGrid.Visibility}");
            }
            else
            {
                Log.Error("TemplateSelectionUIService.ShowOverlay: _overlayGrid is null!");
            }

            ShowOverlayRequested?.Invoke(this, new TemplateUIEventArgs());
        }

        private void HideOverlay()
        {
            if (_overlayGrid != null)
            {
                _overlayGrid.Visibility = Visibility.Collapsed;
            }

            HideOverlayRequested?.Invoke(this, new TemplateUIEventArgs());
        }

        private void ClearTemplatesGrid()
        {
            if (_templatesGrid != null)
            {
                _templatesGrid.ItemsSource = null;
            }
            _templateDisplayData.Clear();
        }

        private Canvas CreateErrorPreview(double width, double height)
        {
            var canvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(Colors.LightGray)
            };

            var text = new TextBlock
            {
                Text = "Preview Error",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.DarkGray)
            };

            Canvas.SetLeft(text, width / 2 - 40);
            Canvas.SetTop(text, height / 2 - 10);
            canvas.Children.Add(text);

            return canvas;
        }

        private void OnTemplateCardClicked(TemplateData template)
        {
            TemplateCardClicked?.Invoke(this, new TemplateCardClickedEventArgs { Template = template });
        }
        #endregion
    }

    #region Template Preview Generator - Separate Concern
    /// <summary>
    /// Generates visual previews of templates
    /// Separated from UI service for single responsibility
    /// </summary>
    public class TemplatePreviewGenerator
    {
        private readonly TemplateDatabase _database;
        
        public TemplatePreviewGenerator()
        {
            _database = new TemplateDatabase();
        }
        
        public Canvas GeneratePreview(TemplateData template, double maxWidth, double maxHeight)
        {
            if (template == null) return null;

            Log.Debug($"TemplatePreviewGenerator: Generating preview for template '{template.Name}' (ID: {template.Id})");

            // Create canvas
            var canvas = new Canvas();
            
            // Calculate scale
            double templateWidth = template.CanvasWidth > 0 ? template.CanvasWidth : 600;
            double templateHeight = template.CanvasHeight > 0 ? template.CanvasHeight : 400;
            
            double scaleX = maxWidth / templateWidth;
            double scaleY = maxHeight / templateHeight;
            double scale = Math.Min(scaleX, scaleY);
            
            canvas.Width = templateWidth * scale;
            canvas.Height = templateHeight * scale;
            
            // Set template background 
            if (!string.IsNullOrEmpty(template.BackgroundImagePath) && System.IO.File.Exists(template.BackgroundImagePath))
            {
                // Try to set background image
                try
                {
                    var bgImage = new Image
                    {
                        Source = new BitmapImage(new Uri(template.BackgroundImagePath)),
                        Stretch = Stretch.UniformToFill,
                        Width = canvas.Width,
                        Height = canvas.Height
                    };
                    Canvas.SetLeft(bgImage, 0);
                    Canvas.SetTop(bgImage, 0);
                    canvas.Children.Add(bgImage);
                    Log.Debug($"TemplatePreviewGenerator: Applied background image: {template.BackgroundImagePath}");
                }
                catch
                {
                    // Fallback to color if image fails
                    SetBackgroundColor();
                }
            }
            else
            {
                SetBackgroundColor();
            }
            
            void SetBackgroundColor()
            {
                if (!string.IsNullOrEmpty(template.BackgroundColor))
                {
                    var bgColor = ParseColorString(template.BackgroundColor, Colors.White);
                    canvas.Background = new SolidColorBrush(bgColor);
                    Log.Debug($"TemplatePreviewGenerator: Applied background color: {template.BackgroundColor}");
                }
                else
                {
                    canvas.Background = new SolidColorBrush(Colors.White); // Default white background
                }
            }
            
            // Add subtle outline to show template boundaries
            AddOutline(canvas);
            
            // Load actual template items from database
            var templateItems = _database.GetCanvasItems(template.Id);
            Log.Debug($"TemplatePreviewGenerator: Found {templateItems?.Count ?? 0} canvas items for template ID {template.Id}");
            
            if (templateItems != null && templateItems.Count > 0)
            {
                var itemTypes = string.Join(", ", templateItems.Select(i => i.ItemType));
                Log.Debug($"TemplatePreviewGenerator: Using actual template items - {itemTypes}");
                AddActualTemplateItems(canvas, templateItems, scale);
            }
            else
            {
                Log.Debug($"TemplatePreviewGenerator: No items found, using placeholder slots");
                // Fallback to placeholder slots if no items found
                AddPlaceholderPhotoSlots(canvas, scale);
            }
            
            // Labels removed per user request - focusing on clean preview only
            
            return canvas;
        }

        private void AddOutline(Canvas canvas)
        {
            // Create a rectangle outline instead of a filled border
            var outline = new Rectangle
            {
                Width = canvas.Width,
                Height = canvas.Height,
                Stroke = new SolidColorBrush(Color.FromArgb(80, 200, 200, 200)), // Subtle light gray outline
                StrokeThickness = 1.5,
                StrokeDashArray = null, // Solid line (set to new DoubleCollection {4, 2} for dashed)
                Fill = null, // No fill - completely transparent
                RadiusX = 4,
                RadiusY = 4
            };
            
            Canvas.SetLeft(outline, 0);
            Canvas.SetTop(outline, 0);
            canvas.Children.Insert(0, outline); // Insert at beginning so it's behind everything
        }
        
        private void AddBorder(Canvas canvas)
        {
            var border = new Border
            {
                Width = canvas.Width,
                Height = canvas.Height,
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 150, 150, 150)), // Very subtle gray border
                BorderThickness = new Thickness(0.5),
                Background = new SolidColorBrush(Colors.Transparent), // Transparent background
                CornerRadius = new CornerRadius(4)
            };
            canvas.Children.Add(border);
        }

        private void AddActualTemplateItems(Canvas canvas, List<CanvasItemData> items, double scale)
        {
            foreach (var item in items.OrderBy(i => i.ZIndex))
            {
                UIElement element = null;
                
                switch (item.ItemType)
                {
                    case "Placeholder":
                        element = CreatePlaceholderElement(item, scale);
                        break;
                    case "Image":
                        element = CreateImageElement(item, scale);
                        break;
                    case "Text":
                        element = CreateTextElement(item, scale);
                        break;
                    case "Shape":
                        element = CreateShapeElement(item, scale);
                        break;
                }
                
                if (element != null)
                {
                    Canvas.SetLeft(element, item.X * scale);
                    Canvas.SetTop(element, item.Y * scale);
                    canvas.Children.Add(element);
                }
            }
        }
        
        private UIElement CreatePlaceholderElement(CanvasItemData item, double scale)
        {
            // Parse placeholder color from database
            Color placeholderColor = ParseColorString(item.PlaceholderColor, Color.FromArgb(30, 0, 0, 0));
            
            var border = new Border
            {
                Width = item.Width * scale,
                Height = item.Height * scale,
                Background = new SolidColorBrush(placeholderColor),
                BorderBrush = new SolidColorBrush(Colors.DarkGray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2)
            };
            
            // Add a grid to hold multiple elements
            var grid = new Grid();
            
            // Add diagonal lines for placeholder effect (optional - lighter color)
            if (placeholderColor.A > 100) // Only add lines if placeholder is quite opaque
            {
                var line1 = new System.Windows.Shapes.Line
                {
                    X1 = 0, Y1 = 0,
                    X2 = item.Width * scale, Y2 = item.Height * scale,
                    Stroke = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    StrokeThickness = 0.5
                };
                
                var line2 = new System.Windows.Shapes.Line
                {
                    X1 = item.Width * scale, Y1 = 0,
                    X2 = 0, Y2 = item.Height * scale,
                    Stroke = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    StrokeThickness = 0.5
                };
                
                grid.Children.Add(line1);
                grid.Children.Add(line2);
            }
            
            // Add placeholder number if available
            if (item.PlaceholderNumber > 0)
            {
                var numberText = new TextBlock
                {
                    Text = item.PlaceholderNumber.ToString(),
                    FontSize = Math.Min(30, item.Height * scale / 3),
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255))
                };
                grid.Children.Add(numberText);
            }
            else
            {
                // Camera icon if no number
                var icon = new TextBlock
                {
                    Text = "📷",
                    FontSize = Math.Min(20, item.Height * scale / 4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))
                };
                grid.Children.Add(icon);
            }
            
            border.Child = grid;
            
            if (item.Rotation != 0)
            {
                border.RenderTransform = new RotateTransform(item.Rotation, border.Width / 2, border.Height / 2);
            }
            
            return border;
        }
        
        private Color ParseColorString(string colorString, Color defaultColor)
        {
            if (string.IsNullOrEmpty(colorString))
                return defaultColor;
            
            try
            {
                // Handle different color formats
                if (colorString.StartsWith("#"))
                {
                    return (Color)ColorConverter.ConvertFromString(colorString);
                }
                else if (colorString.StartsWith("rgb"))
                {
                    // Parse RGB format: rgb(255,255,255) or rgba(255,255,255,0.5)
                    var match = System.Text.RegularExpressions.Regex.Match(colorString, 
                        @"rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)");
                    
                    if (match.Success)
                    {
                        byte r = byte.Parse(match.Groups[1].Value);
                        byte g = byte.Parse(match.Groups[2].Value);
                        byte b = byte.Parse(match.Groups[3].Value);
                        byte a = 255;
                        
                        if (match.Groups[4].Success)
                        {
                            float alpha = float.Parse(match.Groups[4].Value);
                            a = (byte)(alpha * 255);
                        }
                        
                        return Color.FromArgb(a, r, g, b);
                    }
                }
                else
                {
                    // Try parsing as a named color
                    var namedColor = ColorConverter.ConvertFromString(colorString);
                    if (namedColor != null)
                        return (Color)namedColor;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"TemplatePreviewGenerator: Failed to parse color '{colorString}': {ex.Message}");
            }
            
            return defaultColor;
        }
        
        private UIElement CreateImageElement(CanvasItemData item, double scale)
        {
            var container = new Grid
            {
                Width = item.Width * scale,
                Height = item.Height * scale
            };
            
            // Try to load actual image if path is available
            if (!string.IsNullOrEmpty(item.ImagePath) && System.IO.File.Exists(item.ImagePath))
            {
                try
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(item.ImagePath)),
                        Stretch = Stretch.UniformToFill,
                        Width = item.Width * scale,
                        Height = item.Height * scale
                    };
                    container.Children.Add(image);
                }
                catch
                {
                    // If image fails to load, show placeholder
                    AddImagePlaceholder(container);
                }
            }
            else
            {
                // Show styled placeholder for image
                AddImagePlaceholder(container);
            }
            
            if (item.Rotation != 0)
            {
                container.RenderTransform = new RotateTransform(item.Rotation, container.Width / 2, container.Height / 2);
            }
            
            return container;
        }
        
        private void AddImagePlaceholder(Grid container)
        {
            var border = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(40, 200, 200, 200), 0),
                        new GradientStop(Color.FromArgb(40, 150, 150, 150), 1)
                    }
                },
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 100, 100, 100)),
                BorderThickness = new Thickness(0.5)
            };
            
            var icon = new TextBlock
            {
                Text = "🖼️",
                FontSize = Math.Min(20, container.Height / 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(100, 80, 80, 80))
            };
            
            border.Child = icon;
            container.Children.Add(border);
        }
        
        private UIElement CreateTextElement(CanvasItemData item, double scale)
        {
            var textBlock = new TextBlock
            {
                Text = !string.IsNullOrEmpty(item.Text) ? item.Text : "Text",
                FontSize = (item.FontSize ?? 12) * scale,
                Foreground = new SolidColorBrush(Colors.Black)
            };
            
            if (!string.IsNullOrEmpty(item.FontFamily))
                textBlock.FontFamily = new System.Windows.Media.FontFamily(item.FontFamily);
            
            if (item.IsBold)
                textBlock.FontWeight = FontWeights.Bold;
            
            if (item.IsItalic)
                textBlock.FontStyle = FontStyles.Italic;
            
            if (item.Rotation != 0)
            {
                textBlock.RenderTransform = new RotateTransform(item.Rotation);
            }
            
            return textBlock;
        }
        
        private UIElement CreateShapeElement(CanvasItemData item, double scale)
        {
            // Parse shape colors
            var fillColor = ParseColorString(item.FillColor, Color.FromArgb(100, 200, 200, 200));
            var strokeColor = ParseColorString(item.StrokeColor, Colors.Gray);
            
            UIElement shape = null;
            
            // Determine shape type (default to rectangle)
            if (item.ShapeType == "Ellipse" || item.ShapeType == "Circle")
            {
                var ellipse = new Ellipse
                {
                    Width = item.Width * scale,
                    Height = item.Height * scale,
                    Fill = new SolidColorBrush(fillColor),
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = (item.StrokeThickness > 0 ? item.StrokeThickness : 1) * scale
                };
                shape = ellipse;
            }
            else // Rectangle or default
            {
                var rectangle = new Rectangle
                {
                    Width = item.Width * scale,
                    Height = item.Height * scale,
                    Fill = new SolidColorBrush(fillColor),
                    Stroke = new SolidColorBrush(strokeColor),
                    StrokeThickness = (item.StrokeThickness > 0 ? item.StrokeThickness : 1) * scale
                    // Note: CornerRadius not available in CanvasItemData - using default rounded corners
                    // RadiusX = 2,
                    // RadiusY = 2
                };
                shape = rectangle;
            }
            
            if (item.Rotation != 0 && shape != null)
            {
                shape.RenderTransform = new RotateTransform(item.Rotation, item.Width * scale / 2, item.Height * scale / 2);
            }
            
            return shape;
        }
        
        private void AddPlaceholderPhotoSlots(Canvas canvas, double scale)
        {
            // Detect template type based on dimensions
            double aspectRatio = canvas.Width / canvas.Height;
            
            // Common template aspect ratios
            bool is2x6Strip = Math.Abs(aspectRatio - 0.33) < 0.1;  // 2x6 photo strip (vertical)
            bool is4x6Print = Math.Abs(aspectRatio - 1.5) < 0.1;   // 4x6 landscape
            bool is6x4Print = Math.Abs(aspectRatio - 0.67) < 0.1;  // 6x4 portrait
            bool isSquare = Math.Abs(aspectRatio - 1.0) < 0.1;     // Square
            
            if (is2x6Strip)
            {
                // 2x6 strip layout - 3 or 4 photos vertically
                AddVerticalStripLayout(canvas, 3);
            }
            else if (is4x6Print)
            {
                // 4x6 landscape - 2x2 or 1+2 layout
                AddLandscapeLayout(canvas);
            }
            else if (is6x4Print)
            {
                // 6x4 portrait - vertical stack
                AddPortraitLayout(canvas);
            }
            else if (isSquare)
            {
                // Square - 2x2 grid
                Add2x2GridLayout(canvas);
            }
            else
            {
                // Default - 2x2 grid
                Add2x2GridLayout(canvas);
            }
        }
        
        private void AddVerticalStripLayout(Canvas canvas, int photoCount)
        {
            double margin = canvas.Width * 0.1;
            double slotWidth = canvas.Width - (margin * 2);
            double totalHeight = canvas.Height - (margin * 2);
            double spacing = canvas.Height * 0.02;
            double slotHeight = (totalHeight - (spacing * (photoCount - 1))) / photoCount;
            
            var colors = GetGradientColors(photoCount);
            
            for (int i = 0; i < photoCount; i++)
            {
                var border = CreatePhotoSlotWithColor(slotWidth, slotHeight, colors[i]);
                Canvas.SetLeft(border, margin);
                Canvas.SetTop(border, margin + i * (slotHeight + spacing));
                canvas.Children.Add(border);
            }
        }
        
        private void AddLandscapeLayout(Canvas canvas)
        {
            double margin = canvas.Width * 0.05;
            double spacing = canvas.Width * 0.02;
            
            // Left side - large photo
            double largeWidth = (canvas.Width - margin * 2 - spacing) * 0.5;
            double largeHeight = canvas.Height - margin * 2;
            
            var largeBorder = CreatePhotoSlot(largeWidth, largeHeight);
            Canvas.SetLeft(largeBorder, margin);
            Canvas.SetTop(largeBorder, margin);
            canvas.Children.Add(largeBorder);
            
            // Right side - 2 smaller photos
            double smallWidth = largeWidth;
            double smallHeight = (largeHeight - spacing) / 2;
            
            for (int i = 0; i < 2; i++)
            {
                var smallBorder = CreatePhotoSlot(smallWidth, smallHeight);
                Canvas.SetLeft(smallBorder, margin + largeWidth + spacing);
                Canvas.SetTop(smallBorder, margin + i * (smallHeight + spacing));
                canvas.Children.Add(smallBorder);
            }
        }
        
        private void AddPortraitLayout(Canvas canvas)
        {
            double margin = canvas.Width * 0.08;
            double spacing = canvas.Height * 0.02;
            double slotWidth = canvas.Width - (margin * 2);
            
            // Top large photo
            double largeHeight = canvas.Height * 0.4;
            var largeBorder = CreatePhotoSlot(slotWidth, largeHeight);
            Canvas.SetLeft(largeBorder, margin);
            Canvas.SetTop(largeBorder, margin);
            canvas.Children.Add(largeBorder);
            
            // Bottom 2 smaller photos
            double smallHeight = (canvas.Height - margin * 2 - largeHeight - spacing * 2) / 2;
            
            for (int i = 0; i < 2; i++)
            {
                var smallBorder = CreatePhotoSlot(slotWidth, smallHeight);
                Canvas.SetLeft(smallBorder, margin);
                Canvas.SetTop(smallBorder, margin + largeHeight + spacing + i * (smallHeight + spacing));
                canvas.Children.Add(smallBorder);
            }
        }
        
        private void Add2x2GridLayout(Canvas canvas)
        {
            double margin = canvas.Width * 0.08;
            double spacing = canvas.Width * 0.04;
            double slotSize = (Math.Min(canvas.Width, canvas.Height) - margin * 2 - spacing) / 2;
            
            // Center the grid if canvas is not square
            double offsetX = (canvas.Width - (slotSize * 2 + spacing)) / 2;
            double offsetY = (canvas.Height - (slotSize * 2 + spacing)) / 2;
            
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    var border = CreatePhotoSlot(slotSize, slotSize);
                    Canvas.SetLeft(border, offsetX + col * (slotSize + spacing));
                    Canvas.SetTop(border, offsetY + row * (slotSize + spacing));
                    canvas.Children.Add(border);
                }
            }
        }
        
        private Border CreatePhotoSlot(double width, double height)
        {
            return CreatePhotoSlotWithColor(width, height, GetRandomPlaceholderColor());
        }
        
        private Border CreatePhotoSlotWithColor(double width, double height, Color bgColor)
        {
            var border = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 80, 80, 80)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(4)
            };
            
            var grid = new Grid();
            
            // Add diagonal lines for photo placeholder effect
            var line1 = new System.Windows.Shapes.Line
            {
                X1 = 0, Y1 = 0,
                X2 = width, Y2 = height,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 0.5
            };
            
            var line2 = new System.Windows.Shapes.Line
            {
                X1 = width, Y1 = 0,
                X2 = 0, Y2 = height,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                StrokeThickness = 0.5
            };
            
            grid.Children.Add(line1);
            grid.Children.Add(line2);
            
            // Camera icon with better visibility
            var icon = new TextBlock
            {
                Text = "📷",
                FontSize = Math.Min(24, Math.Min(width, height) / 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))
            };
            
            // Add shadow effect to icon
            icon.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 3,
                ShadowDepth = 1,
                Opacity = 0.5
            };
            
            grid.Children.Add(icon);
            border.Child = grid;
            
            return border;
        }
        
        private Color GetRandomPlaceholderColor()
        {
            // Array of pleasant placeholder colors
            var colors = new Color[]
            {
                Color.FromArgb(60, 76, 175, 80),   // Green
                Color.FromArgb(60, 33, 150, 243),  // Blue
                Color.FromArgb(60, 255, 152, 0),   // Orange
                Color.FromArgb(60, 156, 39, 176),  // Purple
                Color.FromArgb(60, 255, 87, 34),   // Deep Orange
                Color.FromArgb(60, 0, 188, 212),   // Cyan
                Color.FromArgb(60, 233, 30, 99),   // Pink
                Color.FromArgb(60, 103, 58, 183),  // Deep Purple
                Color.FromArgb(60, 255, 193, 7),   // Amber
                Color.FromArgb(60, 96, 125, 139)   // Blue Grey
            };
            
            // Use a deterministic index based on current time to vary colors
            int index = (int)(DateTime.Now.Ticks % colors.Length);
            return colors[index];
        }
        
        private List<Color> GetGradientColors(int count)
        {
            var colors = new List<Color>();
            
            // Define gradient color schemes
            var schemes = new[]
            {
                // Blue to Purple gradient
                new[] { Color.FromArgb(50, 33, 150, 243), Color.FromArgb(50, 156, 39, 176) },
                // Green to Blue gradient
                new[] { Color.FromArgb(50, 76, 175, 80), Color.FromArgb(50, 0, 188, 212) },
                // Orange to Pink gradient
                new[] { Color.FromArgb(50, 255, 152, 0), Color.FromArgb(50, 233, 30, 99) },
                // Purple to Pink gradient
                new[] { Color.FromArgb(50, 103, 58, 183), Color.FromArgb(50, 244, 67, 54) }
            };
            
            // Select a scheme based on template
            var scheme = schemes[DateTime.Now.Second % schemes.Length];
            var startColor = scheme[0];
            var endColor = scheme[1];
            
            for (int i = 0; i < count; i++)
            {
                float ratio = count > 1 ? (float)i / (count - 1) : 0;
                
                byte a = (byte)(startColor.A + (endColor.A - startColor.A) * ratio);
                byte r = (byte)(startColor.R + (endColor.R - startColor.R) * ratio);
                byte g = (byte)(startColor.G + (endColor.G - startColor.G) * ratio);
                byte b = (byte)(startColor.B + (endColor.B - startColor.B) * ratio);
                
                colors.Add(Color.FromArgb(a, r, g, b));
            }
            
            return colors;
        }

        private void AddLabels(Canvas canvas, TemplateData template, List<CanvasItemData> items)
        {
            // Template name label with subtle background
            var nameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3)
            };
            
            var nameLabel = new TextBlock
            {
                Text = template.Name,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 50, 50, 50))
            };
            
            nameBorder.Child = nameLabel;
            Canvas.SetLeft(nameBorder, 5);
            Canvas.SetTop(nameBorder, 5);
            canvas.Children.Add(nameBorder);
            
            // Photo count label - count actual placeholders
            int photoCount = items?.Count(item => item.ItemType == "Placeholder") ?? 4;
            
            var countBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3)
            };
            
            var countLabel = new TextBlock
            {
                Text = $"{photoCount} photos",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100))
            };
            
            countBorder.Child = countLabel;
            Canvas.SetRight(countBorder, 5);
            Canvas.SetBottom(countBorder, 5);
            canvas.Children.Add(countBorder);
        }
    }
    #endregion

    #region Event Args
    public class TemplateUIEventArgs : EventArgs
    {
        public string Message { get; set; }
    }

    public class TemplateCardClickedEventArgs : EventArgs
    {
        public TemplateData Template { get; set; }
    }

    public class TemplateDisplayData
    {
        public TemplateData Template { get; set; }
        public string Name { get; set; }
        public int PhotoCount { get; set; }
        public string Dimensions { get; set; }
        public Canvas Preview { get; set; }
        public bool IsSelected { get; set; }
    }
    #endregion
}