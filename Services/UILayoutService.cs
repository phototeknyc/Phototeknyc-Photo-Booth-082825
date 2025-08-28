using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Photobooth.Database;
using Photobooth.Models.UITemplates;
using Photobooth.Pages;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for applying custom UI layouts to PhotoboothTouchModern
    /// Falls back to default UI if no custom layout is active
    /// </summary>
    public class UILayoutService
    {
        private readonly UILayoutDatabase _database;
        private UILayoutTemplate _currentLayout;
        private UILayoutProfile _currentProfile;
        private bool _isCustomLayoutActive;

        public UILayoutService()
        {
            _database = new UILayoutDatabase();
            _database.InitializePredefinedProfiles();
        }

        /// <summary>
        /// Loads and applies custom layout based on active profile
        /// </summary>
        public void ApplyLayoutToPage(Page page, Panel mainContainer)
        {
            try
            {
                // Get active profile
                _currentProfile = _database.GetActiveProfile();
                
                if (_currentProfile != null)
                {
                    // Determine current orientation
                    var orientation = GetCurrentOrientation();
                    var orientationKey = orientation == Orientation.Vertical ? "Portrait" : "Landscape";
                    
                    // Get layout for current orientation from profile
                    if (_currentProfile.Layouts.ContainsKey(orientationKey))
                    {
                        _currentLayout = _currentProfile.Layouts[orientationKey];
                        ApplyCustomLayout(page, mainContainer);
                        _isCustomLayoutActive = true;
                        System.Diagnostics.Debug.WriteLine($"Applied layout from profile: {_currentProfile.Name} ({orientationKey})");
                    }
                    else
                    {
                        // Fallback to any active layout
                        _currentLayout = _database.GetActiveLayout(orientation);
                        if (_currentLayout != null)
                        {
                            ApplyCustomLayout(page, mainContainer);
                            _isCustomLayoutActive = true;
                        }
                        else
                        {
                            _isCustomLayoutActive = false;
                            System.Diagnostics.Debug.WriteLine("No custom layout in profile, using default UI");
                        }
                    }
                }
                else
                {
                    // No profile active, try direct layout
                    var orientation = GetCurrentOrientation();
                    _currentLayout = _database.GetActiveLayout(orientation);
                    
                    if (_currentLayout != null)
                    {
                        ApplyCustomLayout(page, mainContainer);
                        _isCustomLayoutActive = true;
                    }
                    else
                    {
                        _isCustomLayoutActive = false;
                        System.Diagnostics.Debug.WriteLine("No active profile or layout, using default UI");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying custom layout: {ex.Message}");
                _isCustomLayoutActive = false;
                // Default UI remains unchanged
            }
        }

        /// <summary>
        /// Quick switch to a different profile
        /// </summary>
        public void SwitchToProfile(string profileId, Page page, Panel mainContainer)
        {
            try
            {
                _database.SetActiveProfile(profileId);
                ApplyLayoutToPage(page, mainContainer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error switching profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Get available profiles for quick switching
        /// </summary>
        public List<UILayoutProfile> GetAvailableProfiles()
        {
            return _database.GetAllProfiles();
        }

        /// <summary>
        /// Get current active profile
        /// </summary>
        public UILayoutProfile GetCurrentProfile()
        {
            return _currentProfile;
        }

        private void ApplyCustomLayout(Page page, Panel mainContainer)
        {
            // Create a new grid to hold custom elements
            var customGrid = new Grid
            {
                Name = "CustomLayoutGrid"
            };

            // Apply theme
            if (_currentLayout.Theme != null)
            {
                page.Background = new SolidColorBrush(_currentLayout.Theme.BackgroundColor);
            }

            // Add each element from the layout
            foreach (var element in _currentLayout.Elements.OrderBy(e => e.ZIndex))
            {
                var uiElement = CreateUIElement(element);
                if (uiElement != null)
                {
                    // Position element based on anchor and offset
                    PositionElement(uiElement, element, customGrid);
                    customGrid.Children.Add(uiElement);
                }
            }

            // Add custom grid as overlay (preserves existing UI underneath)
            mainContainer.Children.Add(customGrid);
            Panel.SetZIndex(customGrid, 1000); // Ensure it's on top
        }

        private FrameworkElement CreateUIElement(UIElementTemplate template)
        {
            switch (template.Type)
            {
                case ElementType.Button:
                    return CreateButton(template);
                case ElementType.Text:
                    return CreateTextBlock(template);
                case ElementType.Image:
                    return CreateImage(template);
                case ElementType.Background:
                    return CreateBackground(template);
                case ElementType.Camera:
                    return CreateCameraPreview(template);
                case ElementType.Countdown:
                    return CreateCountdown(template);
                default:
                    return null;
            }
        }

        private Button CreateButton(UIElementTemplate template)
        {
            var button = new Button
            {
                Name = template.Id.Replace("-", "_"),
                Content = GetPropertyValue<string>(template, "Text", "Button"),
                FontSize = GetPropertyValue<double>(template, "FontSize", 24),
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Apply background color
            var bgColor = GetPropertyValue<string>(template, "BackgroundColor", "#4CAF50");
            button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor));

            // Apply corner radius through template
            var cornerRadius = GetPropertyValue<double>(template, "CornerRadius", 15);
            button.Template = CreateButtonTemplate(cornerRadius);

            // Wire up action command
            if (!string.IsNullOrEmpty(template.ActionCommand))
            {
                button.Tag = template.ActionCommand;
                button.Click += CustomButton_Click;
            }

            // Set visibility
            button.Visibility = template.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = template.IsEnabled;
            button.Opacity = template.Opacity;

            return button;
        }

        private ControlTemplate CreateButtonTemplate(double cornerRadius)
        {
            var template = new ControlTemplate(typeof(Button));
            
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") 
            { 
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) 
            });
            
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            
            border.AppendChild(contentPresenter);
            template.VisualTree = border;
            
            return template;
        }

        private TextBlock CreateTextBlock(UIElementTemplate template)
        {
            var textBlock = new TextBlock
            {
                Name = template.Id.Replace("-", "_"),
                Text = GetPropertyValue<string>(template, "Text", "Label"),
                FontSize = GetPropertyValue<double>(template, "FontSize", 16),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            textBlock.Visibility = template.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            textBlock.Opacity = template.Opacity;

            return textBlock;
        }

        private Image CreateImage(UIElementTemplate template)
        {
            var image = new Image
            {
                Name = template.Id.Replace("-", "_"),
                Stretch = Stretch.Uniform
            };

            var imagePath = GetPropertyValue<string>(template, "ImagePath", "");
            if (!string.IsNullOrEmpty(imagePath))
            {
                try
                {
                    // Check if it's a file path and if file exists
                    if (!imagePath.StartsWith("http://") && !imagePath.StartsWith("https://"))
                    {
                        var fullPath = System.IO.Path.IsPathRooted(imagePath) ? 
                            imagePath : 
                            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
                        
                        if (System.IO.File.Exists(fullPath))
                        {
                            image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(fullPath, UriKind.Absolute));
                        }
                    }
                    else
                    {
                        image.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imagePath, UriKind.Absolute));
                    }
                }
                catch
                {
                    // Use placeholder if image fails to load
                    System.Diagnostics.Debug.WriteLine($"Could not load image: {imagePath}");
                }
            }

            image.Visibility = template.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            image.Opacity = template.Opacity;

            return image;
        }

        private Rectangle CreateBackground(UIElementTemplate template)
        {
            var rect = new Rectangle
            {
                Name = template.Id.Replace("-", "_"),
                Fill = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0))
            };

            var imagePath = GetPropertyValue<string>(template, "ImagePath", "");
            if (!string.IsNullOrEmpty(imagePath))
            {
                try
                {
                    // Check if it's a file path and if file exists
                    if (!imagePath.StartsWith("http://") && !imagePath.StartsWith("https://"))
                    {
                        var fullPath = System.IO.Path.IsPathRooted(imagePath) ? 
                            imagePath : 
                            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
                        
                        if (System.IO.File.Exists(fullPath))
                        {
                            var imageBrush = new ImageBrush
                            {
                                ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(fullPath, UriKind.Absolute)),
                                Stretch = Stretch.UniformToFill
                            };
                            rect.Fill = imageBrush;
                        }
                    }
                    else
                    {
                        var imageBrush = new ImageBrush
                        {
                            ImageSource = new System.Windows.Media.Imaging.BitmapImage(new Uri(imagePath, UriKind.Absolute)),
                            Stretch = Stretch.UniformToFill
                        };
                        rect.Fill = imageBrush;
                    }
                }
                catch
                {
                    // Keep solid color if image fails
                    System.Diagnostics.Debug.WriteLine($"Could not load background image: {imagePath}");
                }
            }

            rect.Visibility = template.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            rect.Opacity = template.Opacity;

            return rect;
        }

        private Border CreateCameraPreview(UIElementTemplate template)
        {
            var border = new Border
            {
                Name = template.Id.Replace("-", "_"),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(GetPropertyValue<double>(template, "BorderThickness", 5)),
                CornerRadius = new CornerRadius(GetPropertyValue<double>(template, "CornerRadius", 20)),
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
            };

            // Camera preview content will be set by the photobooth logic
            border.Tag = "CameraPreviewPlaceholder";
            border.Visibility = template.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            border.Opacity = template.Opacity;

            return border;
        }

        private Border CreateCountdown(UIElementTemplate template)
        {
            var border = new Border
            {
                Name = template.Id.Replace("-", "_"),
                CornerRadius = new CornerRadius(100),
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0))
            };

            var textBlock = new TextBlock
            {
                Text = "3",
                FontSize = GetPropertyValue<double>(template, "FontSize", 72),
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = textBlock;
            border.Tag = "CountdownPlaceholder";
            border.Visibility = template.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            border.Opacity = template.Opacity;

            return border;
        }

        private void PositionElement(FrameworkElement element, UIElementTemplate template, Grid container)
        {
            // Set size based on mode
            if (template.SizeMode == SizeMode.Fixed)
            {
                element.Width = template.MinSize.Width;
                element.Height = template.MinSize.Height;
            }
            else if (template.SizeMode == SizeMode.Relative)
            {
                // Will be set in SizeChanged event handler
                element.Tag = template; // Store template for later sizing
            }

            // Position based on anchor
            switch (template.Anchor)
            {
                case AnchorPoint.TopLeft:
                    element.HorizontalAlignment = HorizontalAlignment.Left;
                    element.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case AnchorPoint.TopCenter:
                    element.HorizontalAlignment = HorizontalAlignment.Center;
                    element.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case AnchorPoint.TopRight:
                    element.HorizontalAlignment = HorizontalAlignment.Right;
                    element.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case AnchorPoint.Center:
                    element.HorizontalAlignment = HorizontalAlignment.Center;
                    element.VerticalAlignment = VerticalAlignment.Center;
                    break;
                case AnchorPoint.BottomLeft:
                    element.HorizontalAlignment = HorizontalAlignment.Left;
                    element.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case AnchorPoint.BottomCenter:
                    element.HorizontalAlignment = HorizontalAlignment.Center;
                    element.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case AnchorPoint.BottomRight:
                    element.HorizontalAlignment = HorizontalAlignment.Right;
                    element.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
            }

            // Apply margin for offset
            var marginLeft = template.AnchorOffset.X;
            var marginTop = template.AnchorOffset.Y;
            element.Margin = new Thickness(marginLeft, marginTop, -marginLeft, -marginTop);

            // Set z-index
            Panel.SetZIndex(element, template.ZIndex);
        }

        private void CustomButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag == null) return;

            var command = button.Tag.ToString();
            
            // Find the PhotoboothTouchModern page
            var page = FindParentPage(button);
            if (page is PhotoboothTouchModern modernPage)
            {
                // Execute the appropriate command
                switch (command)
                {
                    case "StartPhotoSession":
                        modernPage.StartPhotoSession();
                        break;
                    case "OpenSettings":
                        modernPage.OpenSettings();
                        break;
                    case "OpenGallery":
                        modernPage.OpenGallery();
                        break;
                    case "ReturnHome":
                        modernPage.ReturnHome();
                        break;
                    // Add more commands as needed
                }
            }
        }

        private Page FindParentPage(DependencyObject child)
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            
            while (parentObject != null)
            {
                if (parentObject is Page page)
                    return page;
                    
                parentObject = VisualTreeHelper.GetParent(parentObject);
            }
            
            return null;
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

        private Orientation GetCurrentOrientation()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            
            return screenWidth > screenHeight ? Orientation.Horizontal : Orientation.Vertical;
        }

        public void RefreshLayout(Page page, Panel mainContainer)
        {
            // Clear any existing custom layout
            var existingCustomGrid = mainContainer.Children.OfType<Grid>()
                .FirstOrDefault(g => g.Name == "CustomLayoutGrid");
            
            if (existingCustomGrid != null)
            {
                mainContainer.Children.Remove(existingCustomGrid);
            }

            // Reapply layout
            ApplyLayoutToPage(page, mainContainer);
        }

        public bool IsCustomLayoutActive => _isCustomLayoutActive;
        
        public UILayoutTemplate CurrentLayout => _currentLayout;
    }
}