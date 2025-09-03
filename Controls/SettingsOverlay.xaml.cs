using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Photobooth.Services;
using CameraControl.Devices;

namespace Photobooth.Controls
{
    /// <summary>
    /// Interaction logic for SettingsOverlay.xaml
    /// </summary>
    public partial class SettingsOverlay : UserControl
    {
        private readonly SettingsManagementService _settingsService;
        private readonly PinLockService _pinLockService;
        private string _currentCategory;
        private Dictionary<string, object> _pendingChanges;
        
        public event EventHandler SettingsClosed;
        public event EventHandler SettingsApplied;
        
        public SettingsOverlay()
        {
            InitializeComponent();
            _settingsService = SettingsManagementService.Instance;
            _pinLockService = PinLockService.Instance;
            _pendingChanges = new Dictionary<string, object>();
            
            // Subscribe to service events
            _settingsService.SettingChanged += OnSettingChanged;
            _settingsService.SettingsSaved += OnSettingsSaved;
            _settingsService.SettingsReset += OnSettingsReset;
        }
        
        /// <summary>
        /// Show the overlay with animation (with PIN protection)
        /// </summary>
        public void ShowOverlay(bool bypassPin = false)
        {
            try
            {
                Log.Debug("SettingsOverlay: Requesting settings access");
                
                // Check if PIN protection is enabled and configured (unless bypassed)
                if (!bypassPin && _pinLockService.IsPinProtectionEnabled)
                {
                    Log.Debug("SettingsOverlay: PIN protection enabled, requesting access");
                    // Request settings access through PIN service
                    _pinLockService.RequestSettingsAccess((granted) =>
                    {
                        if (granted)
                        {
                            Dispatcher.Invoke(() => ShowOverlayInternal());
                        }
                        else
                        {
                            Log.Debug("SettingsOverlay: Access denied");
                        }
                    });
                }
                else
                {
                    // No PIN protection or bypassed, show immediately
                    Log.Debug($"SettingsOverlay: {(bypassPin ? "Bypassing PIN" : "No PIN protection")}, showing directly");
                    ShowOverlayInternal();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to show overlay: {ex.Message}");
                // Fallback - try to show without PIN
                ShowOverlayInternal();
            }
        }
        
        private void ShowOverlayInternal()
        {
            Log.Debug("SettingsOverlay: Showing overlay");
            
            // Make sure the control itself is visible
            this.Visibility = Visibility.Visible;
            
            // Load categories
            LoadCategories();
            
            // Show overlay
            MainOverlay.Visibility = Visibility.Visible;
            
            // Animate in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            MainOverlay.BeginAnimation(OpacityProperty, fadeIn);
        }
        
        /// <summary>
        /// Hide the overlay with animation
        /// </summary>
        public void HideOverlay()
        {
            try
            {
                Log.Debug("SettingsOverlay: Hiding overlay");
                
                // Hide detail panel if open
                HideCategoryDetail();
                
                // Animate out
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) =>
                {
                    MainOverlay.Visibility = Visibility.Collapsed;
                    this.Visibility = Visibility.Collapsed;
                    _pendingChanges.Clear();
                };
                MainOverlay.BeginAnimation(OpacityProperty, fadeOut);
                
                SettingsClosed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to hide overlay: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load setting categories into the grid
        /// </summary>
        private void LoadCategories()
        {
            try
            {
                var categories = new List<CategoryViewModel>();
                
                // Session Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Session",
                    Icon = "⏱️",
                    Summary = $"Countdown: {_settingsService.Session.CountdownSeconds}s, Photos: {_settingsService.Session.NumberOfPhotos}",
                    SettingsCount = 7
                });
                
                // Camera Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Camera",
                    Icon = "📷",
                    Summary = $"Live View: {(_settingsService.Camera.EnableIdleLiveView ? "On" : "Off")}, {_settingsService.Camera.LiveViewFrameRate} FPS",
                    SettingsCount = 4
                });
                
                // Print Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Printing",
                    Icon = "🖨️",
                    Summary = $"Enabled: {(_settingsService.Print.EnablePrinting ? "Yes" : "No")}, Max: {_settingsService.Print.MaxPrintsPerSession}",
                    SettingsCount = 5
                });
                
                // Filter Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Filters",
                    Icon = "🎨",
                    Summary = $"Filters: {(_settingsService.Filters.EnableFilters ? "On" : "Off")}, Beauty: {(_settingsService.Filters.BeautyModeEnabled ? "On" : "Off")}",
                    SettingsCount = 7
                });
                
                // Display Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Display",
                    Icon = "🖥️",
                    Summary = $"Fullscreen: {(_settingsService.Display.FullscreenMode ? "Yes" : "No")}, Size: {(int)(_settingsService.Display.ButtonSizeScale * 100)}%",
                    SettingsCount = 3
                });
                
                // Retake Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Retake",
                    Icon = "🔄",
                    Summary = $"Enabled: {(_settingsService.Retake.EnableRetake ? "Yes" : "No")}, Timeout: {_settingsService.Retake.RetakeTimeout}s",
                    SettingsCount = 3
                });
                
                // Storage Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Storage",
                    Icon = "💾",
                    Summary = "Photo location and organization settings",
                    SettingsCount = 4
                });
                
                // Sharing Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Sharing",
                    Icon = "📤",
                    Summary = $"QR: {(_settingsService.Sharing.EnableQRCode ? "On" : "Off")}, Email: {(_settingsService.Sharing.EnableEmail ? "On" : "Off")}",
                    SettingsCount = 4
                });
                
                // Security Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Security",
                    Icon = "🔒",
                    Summary = $"Lock: {(_settingsService.Security.EnableLockFeature ? "Enabled" : "Disabled")}",
                    SettingsCount = 2
                });
                
                CategoriesGrid.ItemsSource = categories;
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load categories: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show category detail panel
        /// </summary>
        private void ShowCategoryDetail(string categoryName)
        {
            try
            {
                _currentCategory = categoryName;
                CategoryTitle.Text = categoryName;
                
                // Load settings for this category
                LoadCategorySettings(categoryName);
                
                // Show and animate panel
                CategoryDetailPanel.Visibility = Visibility.Visible;
                
                var slideIn = new DoubleAnimation(400, 0, TimeSpan.FromMilliseconds(300));
                slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                DetailPanelTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to show category detail: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hide category detail panel
        /// </summary>
        private void HideCategoryDetail()
        {
            if (CategoryDetailPanel.Visibility != Visibility.Visible) return;
            
            var slideOut = new DoubleAnimation(0, 400, TimeSpan.FromMilliseconds(200));
            slideOut.Completed += (s, e) =>
            {
                CategoryDetailPanel.Visibility = Visibility.Collapsed;
                SettingsListPanel.Children.Clear();
            };
            DetailPanelTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        }
        
        /// <summary>
        /// Load settings for a specific category
        /// </summary>
        private void LoadCategorySettings(string categoryName)
        {
            SettingsListPanel.Children.Clear();
            
            // Special handling for Security category to add PIN controls
            if (categoryName == "Security")
            {
                LoadSecuritySettings();
                return;
            }
            
            var settings = _settingsService.GetCategorizedSettings();
            if (!settings.ContainsKey(categoryName)) return;
            
            foreach (var setting in settings[categoryName])
            {
                var settingControl = CreateSettingControl(setting, categoryName);
                if (settingControl != null)
                {
                    SettingsListPanel.Children.Add(settingControl);
                }
            }
        }
        
        /// <summary>
        /// Create appropriate control for a setting
        /// </summary>
        private UIElement CreateSettingControl(SettingItem setting, string category)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };
            
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Setting name - use DisplayName for UI, Name for reflection
            var nameText = new TextBlock
            {
                Text = setting.DisplayName ?? setting.Name,  // Use DisplayName if available, otherwise Name
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(nameText, 0);
            grid.Children.Add(nameText);
            
            // Setting control based on type
            UIElement control = null;
            
            switch (setting.Type)
            {
                case SettingType.Toggle:
                    var toggle = new CheckBox
                    {
                        IsChecked = (bool)setting.Value,
                        Style = FindResource("SettingToggleStyle") as Style,
                        Tag = $"{category}.{setting.Name}"
                    };
                    toggle.Checked += (s, e) => OnSettingValueChanged(category, setting.Name, true);
                    toggle.Unchecked += (s, e) => OnSettingValueChanged(category, setting.Name, false);
                    control = toggle;
                    break;
                    
                case SettingType.Slider:
                    var sliderPanel = new StackPanel();
                    
                    var valueText = new TextBlock
                    {
                        Text = $"{setting.Value} {setting.Unit ?? ""}",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    
                    var slider = new Slider
                    {
                        Minimum = setting.Min,
                        Maximum = setting.Max,
                        Value = Convert.ToDouble(setting.Value),
                        Style = FindResource("ModernSliderStyle") as Style,
                        Tag = $"{category}.{setting.Name}"
                    };
                    
                    slider.ValueChanged += (s, e) =>
                    {
                        valueText.Text = $"{(int)e.NewValue} {setting.Unit ?? ""}";
                        // Special handling for ButtonSizeScale which needs to be divided by 100
                        var value = setting.Name == "ButtonSizeScale" ? e.NewValue / 100.0 : e.NewValue;
                        OnSettingValueChanged(category, setting.Name, value);
                    };
                    
                    sliderPanel.Children.Add(valueText);
                    sliderPanel.Children.Add(slider);
                    control = sliderPanel;
                    break;
            }
            
            if (control != null)
            {
                Grid.SetRow(control, 1);
                grid.Children.Add(control);
            }
            
            container.Child = grid;
            return container;
        }
        
        /// <summary>
        /// Handle setting value change - Auto-save immediately
        /// </summary>
        private void OnSettingValueChanged(string category, string settingName, object value)
        {
            try
            {
                Log.Debug($"SettingsOverlay: Auto-saving {category}.{settingName} = {value}");
                
                // Auto-save immediately - the service will handle the actual saving
                _settingsService.UpdateSetting(category, settingName, value);
                
                // Still track pending changes for UI feedback (optional)
                var key = $"{category}.{settingName}";
                _pendingChanges[key] = value;
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to auto-save setting {category}.{settingName}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply pending changes (now just clears pending list since auto-save is enabled)
        /// </summary>
        private void ApplyPendingChanges()
        {
            // With auto-save enabled, changes are already applied
            // This method now just clears the pending changes for UI feedback
            _pendingChanges.Clear();
            SettingsApplied?.Invoke(this, EventArgs.Empty);
        }
        
        #region Event Handlers
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideOverlay();
        }
        
        private void CategoryCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is CategoryViewModel category)
            {
                ShowCategoryDetail(category.Name);
            }
        }
        
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            HideCategoryDetail();
        }
        
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyPendingChanges();
            HideCategoryDetail();
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyPendingChanges();
            // Settings are already auto-saved, so just show confirmation
            MessageBox.Show("Settings are automatically saved!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reset all settings to defaults?", 
                "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                _settingsService.ResetToDefaults();
                LoadCategories();
            }
        }
        
        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            // With auto-save enabled, no need to prompt about unsaved changes
            // All changes are already saved automatically
            _pendingChanges.Clear();
            HideOverlay();
        }
        
        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            // Refresh categories if needed
            LoadCategories();
        }
        
        private void OnSettingsSaved(object sender, EventArgs e)
        {
            Log.Debug("SettingsOverlay: Settings saved");
        }
        
        private void OnSettingsReset(object sender, EventArgs e)
        {
            Log.Debug("SettingsOverlay: Settings reset");
        }
        
        #endregion
        
        #region Security Settings
        
        /// <summary>
        /// Load security-specific settings with PIN management
        /// </summary>
        private void LoadSecuritySettings()
        {
            try
            {
                // Enable Lock Feature toggle
                var enableLockSetting = new SettingItem
                {
                    Name = "EnableLockFeature",
                    DisplayName = "Enable Interface Lock",
                    Type = SettingType.Toggle,
                    Value = Properties.Settings.Default.EnableLockFeature
                };
                var enableLockControl = CreateSettingControl(enableLockSetting, "Security");
                if (enableLockControl != null)
                {
                    SettingsListPanel.Children.Add(enableLockControl);
                }
                
                // Lock Message Text Box
                var messageContainer = CreateLockMessageControl();
                SettingsListPanel.Children.Add(messageContainer);
                
                // PIN Management Section
                var pinSection = CreatePinManagementSection();
                SettingsListPanel.Children.Add(pinSection);
                
                // Current PIN Status
                var statusSection = CreatePinStatusSection();
                SettingsListPanel.Children.Add(statusSection);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load security settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create lock message control
        /// </summary>
        private UIElement CreateLockMessageControl()
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };
            
            var stackPanel = new StackPanel();
            
            // Title
            var titleText = new TextBlock
            {
                Text = "Lock Screen Message",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stackPanel.Children.Add(titleText);
            
            // Message TextBox
            var messageBox = new TextBox
            {
                Text = GetLockMessage(),
                FontSize = 12,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Height = 60,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true
            };
            
            messageBox.TextChanged += (s, e) =>
            {
                SetLockMessage(messageBox.Text);
                Properties.Settings.Default.Save();
                OnSettingValueChanged("Security", "LockMessage", messageBox.Text);
            };
            
            stackPanel.Children.Add(messageBox);
            
            // Help text
            var helpText = new TextBlock
            {
                Text = "This message will be displayed when the interface is locked",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(helpText);
            
            container.Child = stackPanel;
            return container;
        }
        
        /// <summary>
        /// Create PIN management section
        /// </summary>
        private UIElement CreatePinManagementSection()
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };
            
            var stackPanel = new StackPanel();
            
            // Title
            var titleText = new TextBlock
            {
                Text = "PIN Management",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stackPanel.Children.Add(titleText);
            
            // Button panel
            var buttonPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            
            // Set New PIN button
            var setPinButton = new Button
            {
                Content = "Set New PIN",
                Padding = new Thickness(15, 8, 15, 8),
                Margin = new Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            setPinButton.Click += SetPinButton_Click;
            ApplyButtonStyle(setPinButton);
            buttonPanel.Children.Add(setPinButton);
            
            // Test PIN button
            var testPinButton = new Button
            {
                Content = "Test PIN",
                Padding = new Thickness(15, 8, 15, 8),
                Margin = new Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            testPinButton.Click += TestPinButton_Click;
            ApplyButtonStyle(testPinButton);
            buttonPanel.Children.Add(testPinButton);
            
            // Clear PIN button
            var clearPinButton = new Button
            {
                Content = "Clear PIN",
                Padding = new Thickness(15, 8, 15, 8),
                Margin = new Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            clearPinButton.Click += ClearPinButton_Click;
            ApplyButtonStyle(clearPinButton);
            buttonPanel.Children.Add(clearPinButton);
            
            stackPanel.Children.Add(buttonPanel);
            
            container.Child = stackPanel;
            return container;
        }
        
        /// <summary>
        /// Create PIN status section
        /// </summary>
        private UIElement CreatePinStatusSection()
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };
            
            var stackPanel = new StackPanel();
            
            // Title
            var titleText = new TextBlock
            {
                Text = "PIN Status",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stackPanel.Children.Add(titleText);
            
            // Status text
            var hasPinSet = !string.IsNullOrEmpty(Properties.Settings.Default.LockPin);
            var statusText = new TextBlock
            {
                Text = hasPinSet ? "✓ PIN is configured" : "✗ No PIN set",
                FontSize = 12,
                Foreground = hasPinSet ? 
                    new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : 
                    new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22)),
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(statusText);
            
            // Info text
            var infoText = new TextBlock
            {
                Text = hasPinSet ? 
                    "Your settings and gallery are protected with a PIN" : 
                    "Set a PIN to protect settings and gallery access",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(infoText);
            
            container.Child = stackPanel;
            return container;
        }
        
        /// <summary>
        /// Apply button hover style
        /// </summary>
        private void ApplyButtonStyle(Button button)
        {
            button.Template = CreateButtonTemplate();
        }
        
        private ControlTemplate CreateButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            
            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            
            factory.AppendChild(contentFactory);
            template.VisualTree = factory;
            
            return template;
        }
        
        private void SetPinButton_Click(object sender, RoutedEventArgs e)
        {
            _pinLockService.RequestSetNewPin((success) =>
            {
                if (success)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("PIN has been set successfully!", "PIN Set", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadCategorySettings("Security"); // Refresh the security settings
                    });
                }
            });
        }
        
        private void TestPinButton_Click(object sender, RoutedEventArgs e)
        {
            _pinLockService.RequestSettingsAccess((granted) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (granted)
                    {
                        MessageBox.Show("PIN verified successfully!", "PIN Test", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Incorrect PIN or cancelled", "PIN Test", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            });
        }
        
        private void ClearPinButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear the PIN? This will disable all PIN protection.", 
                "Clear PIN", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                Properties.Settings.Default.LockPin = string.Empty;
                Properties.Settings.Default.Save();
                MessageBox.Show("PIN has been cleared", "PIN Cleared", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                LoadCategorySettings("Security"); // Refresh the security settings
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private string GetLockMessage()
        {
            // Try to get LockUIMessage first (if it exists), fallback to LockPin description
            try 
            {
                // Use reflection to check if property exists
                var property = Properties.Settings.Default.GetType().GetProperty("LockUIMessage");
                if (property != null)
                {
                    var value = property.GetValue(Properties.Settings.Default) as string;
                    return value ?? "Interface is locked";
                }
            }
            catch { }
            
            // Fallback to a default message
            return "Interface is locked. Please contact staff for assistance.";
        }
        
        private void SetLockMessage(string message)
        {
            try
            {
                // Try to set LockUIMessage if it exists
                var property = Properties.Settings.Default.GetType().GetProperty("LockUIMessage");
                if (property != null)
                {
                    property.SetValue(Properties.Settings.Default, message);
                }
            }
            catch { }
        }
        
        #endregion
    }
    
    /// <summary>
    /// View model for category cards
    /// </summary>
    public class CategoryViewModel
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Summary { get; set; }
        public int SettingsCount { get; set; }
    }
}