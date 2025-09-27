using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Photobooth.Database;
using Photobooth.Services;

namespace Photobooth.Controls
{
    public partial class EventTemplatesOverlay : UserControl
    {
        private EventData _currentEvent;
        private List<TemplateData> _eventTemplates;
        private TemplateData _selectedTemplate;
        private EventSelectionService _eventService;
        private TemplateDatabase _database;
        private Action<int> _loadTemplateCallback;
        private int? _defaultTemplateId;

        public event EventHandler CloseRequested;

        public EventTemplatesOverlay()
        {
            InitializeComponent();
            _eventService = EventSelectionService.Instance;
            _database = new TemplateDatabase();
        }

        public void ShowForEvent(EventData eventData, Action<int> loadTemplateCallback = null)
        {
            _currentEvent = eventData;
            _loadTemplateCallback = loadTemplateCallback;

            if (_currentEvent == null)
            {
                EventNameText.Text = "";
                return;
            }

            EventNameText.Text = $"- {_currentEvent.Name}";
            LoadEventTemplates();

            // Show/hide load button based on callback
            if (LoadTemplateButton != null)
            {
                LoadTemplateButton.Visibility = _loadTemplateCallback != null ?
                    Visibility.Visible : Visibility.Collapsed;
            }

            this.Visibility = Visibility.Visible;
        }

        private void LoadEventTemplates()
        {
            try
            {
                TemplatesList.Children.Clear();

                // Get templates associated with this event
                var eventTemplates = _database.GetEventTemplates(_currentEvent.Id);
                _eventTemplates = eventTemplates;

                // Find the default template from the list (IsDefault is set during GetEventTemplates)
                var defaultTemplate = eventTemplates.FirstOrDefault(t => t.IsDefault);
                _defaultTemplateId = defaultTemplate?.Id;

                if (_eventTemplates.Count == 0)
                {
                    var noTemplatesText = new TextBlock
                    {
                        Text = "No templates associated with this event",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 20)
                    };
                    TemplatesList.Children.Add(noTemplatesText);
                    return;
                }

                foreach (var template in _eventTemplates)
                {
                    CreateTemplateItem(template);
                }
            }
            catch
            {
                // Silently handle errors
            }
        }

        private void CreateTemplateItem(TemplateData template)
        {
            var itemBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand,
                Tag = template
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Thumbnail
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Buttons

            // Do not load saved thumbnails; show a simple placeholder instead
            AddThumbnailPlaceholder(grid, 0);

            // Template info
            var infoPanel = new StackPanel
            {
                Margin = new Thickness(15, 10, 10, 10)
            };

            var nameText = new TextBlock
            {
                Text = template.Name,
                FontSize = 16,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Medium
            };
            infoPanel.Children.Add(nameText);

            var detailsText = new TextBlock
            {
                Text = $"Size: {template.CanvasWidth} x {template.CanvasHeight}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                Margin = new Thickness(0, 3, 0, 0)
            };
            infoPanel.Children.Add(detailsText);

            // Check if this is the default template
            if (_defaultTemplateId.HasValue && template.Id == _defaultTemplateId.Value)
            {
                var defaultBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 5, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                var defaultText = new TextBlock
                {
                    Text = "DEFAULT",
                    FontSize = 10,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold
                };
                defaultBadge.Child = defaultText;
                infoPanel.Children.Add(defaultBadge);
            }

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // Action buttons
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            // Unlink button
            var unlinkButton = CreateActionButton("\uE10A", "#FF9800", "Unlink from Event");
            unlinkButton.Click += (s, e) =>
            {
                e.Handled = true;
                UnlinkTemplate(template);
            };
            buttonsPanel.Children.Add(unlinkButton);

            // Delete button
            var deleteButton = CreateActionButton("\uE74D", "#E74C3C", "Delete Template");
            deleteButton.Click += (s, e) =>
            {
                e.Handled = true;
                DeleteTemplate(template);
            };
            deleteButton.Margin = new Thickness(5, 0, 0, 0); // Add spacing
            buttonsPanel.Children.Add(deleteButton);

            Grid.SetColumn(buttonsPanel, 2);
            grid.Children.Add(buttonsPanel);

            itemBorder.Child = grid;

            // Selection handling
            itemBorder.MouseLeftButtonDown += (s, e) =>
            {
                SelectTemplate(template);
                HighlightSelectedItem(itemBorder);
            };

            // Hover effect
            itemBorder.MouseEnter += (s, e) =>
            {
                if (!itemBorder.Tag.Equals(_selectedTemplate))
                {
                    itemBorder.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                }
            };

            itemBorder.MouseLeave += (s, e) =>
            {
                if (!itemBorder.Tag.Equals(_selectedTemplate))
                {
                    itemBorder.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                }
            };

            TemplatesList.Children.Add(itemBorder);
        }

        private Button CreateActionButton(string icon, string color, string tooltip)
        {
            var button = new Button
            {
                Width = 36,
                Height = 36,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Margin = new Thickness(5, 0, 0, 0)
            };

            button.Template = CreateButtonTemplate();

            var iconText = new TextBlock
            {
                Text = icon,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
            };

            button.Content = iconText;
            return button;
        }

        private ControlTemplate CreateButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));

            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            borderFactory.AppendChild(presenterFactory);
            template.VisualTree = borderFactory;

            return template;
        }

        private void HighlightSelectedItem(Border selectedBorder)
        {
            // Reset all items
            foreach (Border item in TemplatesList.Children.OfType<Border>())
            {
                item.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                item.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            }

            // Highlight selected
            selectedBorder.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            selectedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        }

        private void AddThumbnailPlaceholder(Grid grid, int column)
        {
            var placeholderBorder = new Border
            {
                Width = 60,
                Height = 60,
                Margin = new Thickness(10, 10, 0, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40))
            };

            var placeholderIcon = new TextBlock
            {
                Text = "\uE91B", // Image icon
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            placeholderBorder.Child = placeholderIcon;
            Grid.SetColumn(placeholderBorder, column);
            grid.Children.Add(placeholderBorder);
        }

        private void SelectTemplate(TemplateData template)
        {
            _selectedTemplate = template;
            ShowTemplatePreview(template);
        }

        private void ShowTemplatePreview(TemplateData template)
        {
            try
            {
                NoPreviewText.Visibility = Visibility.Collapsed;
                PreviewBorder.Visibility = Visibility.Visible;
                TemplateInfoPanel.Visibility = Visibility.Visible;

                // Hide both render paths by default; enable one below
                if (PreviewImage != null) PreviewImage.Visibility = Visibility.Collapsed;
                if (PreviewViewbox != null) PreviewViewbox.Visibility = Visibility.Collapsed;

                PreviewTitle.Text = $"Preview - {template.Name}";
                TemplateNameInfo.Text = template.Name;
                TemplateDetailsInfo.Text = $"Canvas: {template.CanvasWidth} x {template.CanvasHeight} | " +
                                          $"Items: {template.CanvasItems?.Count ?? 0}";

                // Update default button text
                var isDefault = _defaultTemplateId.HasValue && template.Id == _defaultTemplateId.Value;
                SetDefaultButton.Content = new TextBlock
                {
                    Text = isDefault ? "Remove Default" : "Set as Default",
                    FontSize = 13,
                    Foreground = Brushes.White
                };

                // Set canvas dimensions to match the template
                PreviewCanvas.Width = template.CanvasWidth;
                PreviewCanvas.Height = template.CanvasHeight;
                PreviewCanvas.Children.Clear();

                // Try to use thumbnail/preview image first for fast loading
                bool previewLoaded = false; // We no longer use saved thumbnails

                // If no thumbnail available or it failed to load, render manually
                if (!previewLoaded)
                {
                    // Set canvas dimensions first
                    PreviewCanvas.Width = template.CanvasWidth;
                    PreviewCanvas.Height = template.CanvasHeight;

                    // Set canvas background
                    if (!string.IsNullOrEmpty(template.BackgroundImagePath))
                    {
                        try
                        {
                            if (System.IO.File.Exists(template.BackgroundImagePath))
                            {
                                var bitmap = new BitmapImage(new Uri(template.BackgroundImagePath, UriKind.Absolute));
                                var bgBrush = new ImageBrush(bitmap)
                                {
                                    Stretch = Stretch.Uniform,   // Avoid cropping — show full background
                                    AlignmentX = AlignmentX.Center,
                                    AlignmentY = AlignmentY.Center
                                };
                                PreviewCanvas.Background = bgBrush;
                            }
                            else
                            {
                                PreviewCanvas.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                            }
                        }
                        catch
                        {
                            PreviewCanvas.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                        }
                    }
                    else
                    {
                        PreviewCanvas.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                    }

                    // Load canvas items from database if not already loaded
                    if (template.CanvasItems == null || template.CanvasItems.Count == 0)
                    {
                        var database = new TemplateDatabase();
                        template.CanvasItems = database.GetCanvasItems(template.Id);
                    }

                    // Add canvas items for preview
                    if (template.CanvasItems != null)
                    {
                        foreach (var item in template.CanvasItems.OrderBy(i => i.ZIndex))
                        {
                            AddPreviewItem(item);
                        }
                    }
                    // Ensure canvas path is visible when rendering manually
                    if (PreviewViewbox != null) PreviewViewbox.Visibility = Visibility.Visible;
                    if (PreviewImage != null) PreviewImage.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                // Silently handle errors
            }
        }

        private void AddPreviewItem(CanvasItemData item)
        {
            UIElement element = null;

            switch (item.ItemType)
            {
                case "Text":
                    var textBlock = new TextBlock
                    {
                        Text = item.Text ?? "Sample Text",
                        FontSize = item.FontSize ?? 20
                    };

                    // Apply text color
                    if (!string.IsNullOrEmpty(item.TextColor))
                    {
                        try
                        {
                            textBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.TextColor));
                        }
                        catch
                        {
                            textBlock.Foreground = Brushes.White;
                        }
                    }
                    else
                    {
                        textBlock.Foreground = Brushes.White;
                    }

                    if (!string.IsNullOrEmpty(item.FontFamily))
                        textBlock.FontFamily = new FontFamily(item.FontFamily);

                    // Apply font weight if bold
                    if (item.IsBold)
                        textBlock.FontWeight = FontWeights.Bold;

                    // Apply font style if italic
                    if (item.IsItalic)
                        textBlock.FontStyle = FontStyles.Italic;

                    element = textBlock;
                    break;

                case "Image":
                    var imageBorder = new Border
                    {
                        Width = item.Width,
                        Height = item.Height,
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        BorderThickness = new Thickness(0)
                    };

                    // Try to load the actual image if available
                    if (!string.IsNullOrEmpty(item.ImagePath) && System.IO.File.Exists(item.ImagePath))
                    {
                        try
                        {
                            var image = new Image
                            {
                                Source = new BitmapImage(new Uri(item.ImagePath, UriKind.Absolute)),
                                Stretch = Stretch.UniformToFill
                            };
                            imageBorder.Child = image;
                        }
                        catch
                        {
                            // If image load fails, show placeholder
                        }
                    }
                    element = imageBorder;
                    break;

                case "Placeholder":
                    // Resolve placeholder color to match designer (uses saved color or palette by number)
                    Color fillColor = ResolvePlaceholderColor(item);

                    var placeholderBorder = new Border
                    {
                        Width = item.Width,
                        Height = item.Height,
                        Background = new SolidColorBrush(fillColor),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        BorderThickness = new Thickness(1)
                    };

                    var placeholderGrid = new Grid();

                    // Add camera icon and text in a stack
                    var placeholderStack = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    // Camera icon using Segoe MDL2 Assets
                    var cameraIcon = new TextBlock
                    {
                        Text = "\uE722",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 32,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 5),
                        Foreground = Brushes.Black
                    };
                    placeholderStack.Children.Add(cameraIcon);

                    // Add placeholder label: "Photo 1", "Photo 2", etc.
                    int photoNo = GetPlaceholderNumber(item);
                    var placeholderText = new TextBlock
                    {
                        Text = $"Photo {photoNo}",
                        Foreground = Brushes.Black,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold
                    };
                    placeholderStack.Children.Add(placeholderText);
                    placeholderGrid.Children.Add(placeholderStack);

                    placeholderBorder.Child = placeholderGrid;
                    element = placeholderBorder;
                    break;

                case "Shape":
                    var shape = new Rectangle
                    {
                        Width = item.Width,
                        Height = item.Height
                    };

                    // Apply fill color if available
                    if (!string.IsNullOrEmpty(item.FillColor))
                    {
                        try
                        {
                            shape.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.FillColor));
                        }
                        catch
                        {
                            shape.Fill = Brushes.Transparent;
                        }
                    }
                    else
                    {
                        shape.Fill = Brushes.Transparent;
                    }

                    // Apply stroke if available
                    if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0)
                    {
                        try
                        {
                            shape.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StrokeColor));
                            shape.StrokeThickness = item.StrokeThickness;
                        }
                        catch { }
                    }

                    element = shape;
                    break;
            }

            if (element != null)
            {
                Canvas.SetLeft(element, item.X);
                Canvas.SetTop(element, item.Y);
                Canvas.SetZIndex(element, item.ZIndex);

                // Apply opacity if set
                if (item.Opacity > 0 && item.Opacity < 1)
                {
                    element.Opacity = item.Opacity;
                }

                // Apply rotation if set
                if (item.Rotation != 0)
                {
                    element.RenderTransformOrigin = new Point(0.5, 0.5);
                    element.RenderTransform = new RotateTransform(item.Rotation);
                }

                PreviewCanvas.Children.Add(element);
            }
        }

        private Color ResolvePlaceholderColor(CanvasItemData item)
        {
            // Try the stored color first
            if (!string.IsNullOrEmpty(item.PlaceholderColor))
            {
                try
                {
                    return (Color)ColorConverter.ConvertFromString(item.PlaceholderColor);
                }
                catch { /* fall through to palette */ }
            }

            // Use the same palette logic as the designer for consistency
            int number = item.PlaceholderNumber ?? 1;
            return GetDesignerPlaceholderColor(number);
        }

        private int GetPlaceholderNumber(CanvasItemData item)
        {
            if (item.PlaceholderNumber.HasValue && item.PlaceholderNumber.Value > 0)
                return item.PlaceholderNumber.Value;

            // Fallback: parse digits from name like "Placeholder 2"
            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                var digits = new string(item.Name.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out int n) && n > 0)
                    return n;
            }
            return 1;
        }

        private Color GetDesignerPlaceholderColor(int placeholderNumber)
        {
            // Mirror TemplateService.GetPlaceholderColor palette
            Color[] palette = new Color[]
            {
                Color.FromRgb(255, 182, 193), // Light Pink
                Color.FromRgb(173, 216, 230), // Light Blue
                Color.FromRgb(144, 238, 144), // Light Green
                Color.FromRgb(255, 218, 185), // Peach
                Color.FromRgb(221, 160, 221), // Plum
                Color.FromRgb(255, 255, 224), // Light Yellow
                Color.FromRgb(176, 224, 230), // Powder Blue
                Color.FromRgb(255, 228, 196), // Bisque
                Color.FromRgb(216, 191, 216), // Thistle
                Color.FromRgb(240, 230, 140), // Khaki
                Color.FromRgb(255, 192, 203), // Pink
                Color.FromRgb(230, 230, 250), // Lavender
            };

            int idx = Math.Max(0, (placeholderNumber - 1) % palette.Length);
            return palette[idx];
        }

        private void UnlinkTemplate(TemplateData template)
        {
            var result = MessageBox.Show(
                $"Remove '{template.Name}' from event '{_currentEvent.Name}'?",
                "Unlink Template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _database.RemoveTemplateFromEvent(_currentEvent.Id, template.Id);
                    LoadEventTemplates();

                    // Clear preview if this was selected
                    if (_selectedTemplate?.Id == template.Id)
                    {
                        _selectedTemplate = null;
                        NoPreviewText.Visibility = Visibility.Visible;
                        PreviewBorder.Visibility = Visibility.Collapsed;
                        TemplateInfoPanel.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to unlink template: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void DeleteTemplate(TemplateData template)
        {
            var result = MessageBox.Show(
                $"Permanently delete template '{template.Name}'?\n\nThis action cannot be undone.",
                "Delete Template",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var database = new TemplateDatabase();
                    database.DeleteTemplate(template.Id);
                    LoadEventTemplates();

                    // Clear preview if this was selected
                    if (_selectedTemplate?.Id == template.Id)
                    {
                        _selectedTemplate = null;
                        NoPreviewText.Visibility = Visibility.Visible;
                        PreviewBorder.Visibility = Visibility.Collapsed;
                        TemplateInfoPanel.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete template: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate == null) return;

            try
            {
                var isDefault = _defaultTemplateId.HasValue && _selectedTemplate.Id == _defaultTemplateId.Value;

                if (isDefault)
                {
                    // Remove as default
                    _database.AssignTemplateToEvent(_currentEvent.Id, _selectedTemplate.Id, false);
                }
                else
                {
                    // Set as default
                    _database.AssignTemplateToEvent(_currentEvent.Id, _selectedTemplate.Id, true);
                }

                LoadEventTemplates();
                ShowTemplatePreview(_selectedTemplate);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update default template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTemplate != null && _loadTemplateCallback != null)
            {
                _loadTemplateCallback(_selectedTemplate.Id);
                Close();
            }
        }

        private void AddTemplate_Click(object sender, RoutedEventArgs e)
        {
            // Show template browser to add existing templates
            ShowTemplateBrowser();
        }

        private void ShowTemplateBrowser()
        {
            try
            {
                var database = new TemplateDatabase();
                var allTemplates = database.GetAllTemplates();

                // Filter out templates already assigned to this event
                var availableTemplates = allTemplates.Where(t =>
                    !_eventTemplates.Any(et => et.Id == t.Id)).ToList();

                if (!availableTemplates.Any())
                {
                    MessageBox.Show("No additional templates available to add.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Create selection dialog
                var dialog = new Window
                {
                    Title = "Add Template to Event",
                    Width = 500,
                    Height = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48))
                };

                var listBox = new ListBox
                {
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };

                foreach (var template in availableTemplates)
                {
                    var item = new ListBoxItem
                    {
                        Content = template.Name,
                        Tag = template,
                        Padding = new Thickness(10),
                        FontSize = 14
                    };
                    listBox.Items.Add(item);
                }

                listBox.MouseDoubleClick += (s, e) =>
                {
                    if (listBox.SelectedItem is ListBoxItem selectedItem)
                    {
                        var template = selectedItem.Tag as TemplateData;
                        _database.AssignTemplateToEvent(_currentEvent.Id, template.Id);
                        dialog.Close();
                        LoadEventTemplates();
                    }
                };

                dialog.Content = listBox;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to show template browser: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void Close()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            this.Visibility = Visibility.Collapsed;
        }
    }
}
