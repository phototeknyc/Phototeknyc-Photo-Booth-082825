using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Threading;
using Photobooth.Services;
using CameraControl.Devices;
using Microsoft.Win32;

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
        private readonly Dictionary<Control, DispatcherTimer> _debounceTimers = new Dictionary<Control, DispatcherTimer>();
        private List<CategoryViewModel> _allCategories;
        private string _searchText = string.Empty;
        
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

            // Clear search when showing
            if (SearchTextBox != null)
            {
                SearchTextBox.Text = string.Empty;
                _searchText = string.Empty;
            }

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
                if (_allCategories == null)
                {
                    _allCategories = GetAllCategories();
                }

                var categories = FilterCategories(_allCategories);
                DisplayCategories(categories);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load categories: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all available categories
        /// </summary>
        private List<CategoryViewModel> GetAllCategories()
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
                    Summary = $"Rotation: {GetRotationText(_settingsService.Camera.CameraRotation)}, Live View: {(_settingsService.Camera.EnableIdleLiveView ? "On" : "Off")}",
                    SettingsCount = 7
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
                
                // Capture Modes Settings
                var captureModesService = CaptureModesService.Instance;
                var enabledModes = captureModesService.EnabledModes.Count;
                categories.Add(new CategoryViewModel
                {
                    Name = "Capture Modes",
                    Icon = "🎬",
                    Summary = $"Modes: {(Properties.Settings.Default.CaptureModesEnabled ? "Enabled" : "Disabled")}, Active: {enabledModes}",
                    SettingsCount = 8
                });

                // GIF/Animation Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "GIF/Animation",
                    Icon = "🎞️",
                    Summary = $"GIF: {(Properties.Settings.Default.EnableGifGeneration ? "On" : "Off")}, Speed: {Properties.Settings.Default.GifFrameDelay / 1000.0:F1}s",
                    SettingsCount = 6
                });

                // Video Recording Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Video Recording",
                    Icon = "📹",
                    Summary = $"Duration: {Properties.Settings.Default.VideoDuration}s, Quality: {Properties.Settings.Default.VideoQuality ?? "1080p"}",
                    SettingsCount = 6
                });

                // Boomerang Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Boomerang",
                    Icon = "🔁",
                    Summary = "Forward-backward looping videos",
                    SettingsCount = 5
                });

                // Flipbook Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Flipbook",
                    Icon = "📖",
                    Summary = "Flipbook-style animations",
                    SettingsCount = 6
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

                // Background Removal Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "BackgroundRemoval",
                    DisplayName = "Background Removal",
                    Icon = "🎭",
                    Summary = $"Removal: {(_settingsService.BackgroundRemoval.EnableBackgroundRemoval ? "On" : "Off")}, Guest Picker: {(Properties.Settings.Default.UseGuestBackgroundPicker ? "On" : "Off")}",
                    SettingsCount = 10  // Updated to include all settings
                });
                
                // Debug/Logging Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Debug",
                    Icon = "🐛",
                    Summary = $"Debug Logging: {(DebugService.Instance.IsDebugEnabled ? "Enabled" : "Disabled")}",
                    SettingsCount = 3
                });
                
                // Live View Camera Controls
                var liveViewService = LiveViewCameraControlService.Instance;
                categories.Add(new CategoryViewModel
                {
                    Name = "Live View",
                    Icon = "📹",
                    Summary = $"Camera Controls: {(liveViewService.IsEnabled ? "Active" : "Inactive")}, ISO: {liveViewService.CurrentISO}",
                    SettingsCount = 6
                });

                // Cloud Share Settings
                var cloudShareEnabled = Environment.GetEnvironmentVariable("CLOUD_SHARING_ENABLED", EnvironmentVariableTarget.User) == "True";
                categories.Add(new CategoryViewModel
                {
                    Name = "Cloud Share",
                    Icon = "☁️",
                    Summary = $"Cloud Sharing: {(cloudShareEnabled ? "Enabled" : "Disabled")}",
                    SettingsCount = 8
                });

                // Cloud Sync Settings
                categories.Add(new CategoryViewModel
                {
                    Name = "Cloud Sync",
                    Icon = "🔄☁️",
                    Summary = $"Sync: {(Properties.Settings.Default.EnableCloudSync ? "Enabled" : "Disabled")}, S3: {(!string.IsNullOrEmpty(Properties.Settings.Default.S3AccessKey) ? "Configured" : "Not Set")}",
                    SettingsCount = 7
                });

                return categories;
            }

            /// <summary>
            /// Filter categories based on search text
            /// </summary>
            private List<CategoryViewModel> FilterCategories(List<CategoryViewModel> categories)
            {
                if (string.IsNullOrWhiteSpace(_searchText))
                {
                    return categories;
                }

                var searchLower = _searchText.ToLower();
                var filtered = new List<CategoryViewModel>();

                foreach (var category in categories)
                {
                    // Check if category name or summary contains search text
                    bool matches = category.Name.ToLower().Contains(searchLower) ||
                                  category.Summary.ToLower().Contains(searchLower);

                    // Also check common keywords for each category
                    if (!matches)
                    {
                        matches = CheckCategoryKeywords(category.Name, searchLower);
                    }

                    if (matches)
                    {
                        filtered.Add(category);
                    }
                }

                return filtered;
            }

            /// <summary>
            /// Check if search matches category-specific keywords
            /// </summary>
            private bool CheckCategoryKeywords(string categoryName, string searchText)
            {
                var keywords = new Dictionary<string, string[]>
                {
                    ["Session"] = new[] { "countdown", "timer", "photos", "duration", "delay", "beauty", "mode" },
                    ["Camera"] = new[] { "live", "view", "focus", "auto", "lens", "capture", "shot" },
                    ["Printing"] = new[] { "print", "printer", "copies", "paper", "maximum", "enable" },
                    ["Filters"] = new[] { "filter", "effect", "beauty", "enhance", "color", "style" },
                    ["Display"] = new[] { "fullscreen", "screen", "button", "size", "scale", "ui" },
                    ["Retake"] = new[] { "retry", "redo", "timeout", "allow", "enable" },
                    ["Storage"] = new[] { "save", "folder", "location", "path", "organize", "directory" },
                    ["Capture Modes"] = new[] { "gif", "video", "boomerang", "burst", "flipbook", "mode" },
                    ["GIF/Animation"] = new[] { "animate", "speed", "frame", "delay", "loop" },
                    ["Video Recording"] = new[] { "record", "duration", "quality", "resolution", "fps" },
                    ["Boomerang"] = new[] { "loop", "forward", "backward", "reverse" },
                    ["Flipbook"] = new[] { "pages", "flip", "book", "animation" },
                    ["Sharing"] = new[] { "email", "sms", "text", "qr", "code", "upload" },
                    ["Advanced"] = new[] { "debug", "log", "admin", "developer", "system" },
                    ["Sync/Cloud"] = new[] { "cloud", "sync", "upload", "s3", "aws", "backup" }
                };

                if (keywords.TryGetValue(categoryName, out var categoryKeywords))
                {
                    return categoryKeywords.Any(keyword => keyword.Contains(searchText));
                }

                return false;
            }

            /// <summary>
            /// Get display text for rotation angle
            /// </summary>
            private string GetRotationText(int degrees)
            {
                switch (degrees)
                {
                    case 90: return "90°";
                    case 180: return "180°";
                    case 270: return "270°";
                    default: return "None";
                }
            }

            /// <summary>
            /// Get display-friendly name for category
            /// </summary>
            private string GetDisplayName(string categoryName)
            {
                switch (categoryName)
                {
                    case "BackgroundRemoval":
                        return "Background Removal";
                    case "GIF/Animation":
                        return "GIF/Animation";
                    case "Video Recording":
                        return "Video Recording";
                    case "Capture Modes":
                        return "Capture Modes";
                    default:
                        return categoryName;
                }
            }

            /// <summary>
            /// Display filtered categories
            /// </summary>
            private void DisplayCategories(List<CategoryViewModel> categories)
            {
                Log.Debug($"SettingsOverlay: Displaying {categories.Count} categories (search: '{_searchText}')");

                // Set display names if not already set
                foreach (var category in categories)
                {
                    if (string.IsNullOrEmpty(category.DisplayName))
                    {
                        category.DisplayName = GetDisplayName(category.Name);
                    }
                }

                CategoriesGrid.ItemsSource = categories;

                // Show "no results" message if search returned nothing
                if (categories.Count == 0 && !string.IsNullOrWhiteSpace(_searchText))
                {
                    ShowNoResultsMessage();
                }
            }

            /// <summary>
            /// Show message when no search results found
            /// </summary>
            private void ShowNoResultsMessage()
            {
                var noResultsMessage = new TextBlock
                {
                    Text = $"No settings found for '{_searchText}'",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20)
                };

                CategoriesGrid.ItemsSource = new[] { noResultsMessage };
            }
        
        /// <summary>
        /// Show category detail panel
        /// </summary>
        private void ShowCategoryDetail(string categoryName)
        {
            try
            {
                _currentCategory = categoryName;
                CategoryTitle.Text = GetDisplayName(categoryName);
                
                // Load settings for this category
                LoadCategorySettings(categoryName);
                
                // Show and animate panel
                CategoryDetailPanel.Visibility = Visibility.Visible;
                
                var slideIn = new DoubleAnimation(500, 0, TimeSpan.FromMilliseconds(300));
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
            
            var slideOut = new DoubleAnimation(0, 500, TimeSpan.FromMilliseconds(200));
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
            
            // Special handling for Debug category to add debug controls
            if (categoryName == "Debug")
            {
                LoadDebugSettings();
                return;
            }
            
            // Special handling for Live View category to add camera controls
            if (categoryName == "Live View")
            {
                LoadLiveViewCameraSettings();
                return;
            }
            
            // Special handling for Capture Modes category
            if (categoryName == "Capture Modes")
            {
                LoadCaptureModesSettings();
                return;
            }

            // Special handling for Cloud Share category
            if (categoryName == "Cloud Share")
            {
                LoadCloudShareSettings();
                return;
            }

            // Special handling for Cloud Sync category
            if (categoryName == "Cloud Sync")
            {
                LoadCloudSyncSettings();
                return;
            }

            // Special handling for GIF/Animation category
            if (categoryName == "GIF/Animation")
            {
                LoadGifAnimationSettings();
                return;
            }

            // Special handling for Video Recording category
            if (categoryName == "Video Recording")
            {
                LoadVideoRecordingSettings();
                return;
            }

            // Special handling for Boomerang category
            if (categoryName == "Boomerang")
            {
                LoadBoomerangSettings();
                return;
            }

            // Special handling for Flipbook category
            if (categoryName == "Flipbook")
            {
                LoadFlipbookSettings();
                return;
            }

            // Special handling for Background Removal category
            if (categoryName == "BackgroundRemoval")
            {
                LoadBackgroundRemovalSettings();
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
                        // Special handling for properties that need to be divided by 100 (percentages to decimals)
                        var value = (setting.Name == "ButtonSizeScale" || setting.Name == "IdleBackgroundOpacity")
                            ? e.NewValue / 100.0
                            : e.NewValue;
                        OnSettingValueChanged(category, setting.Name, value);
                    };

                    sliderPanel.Children.Add(valueText);
                    sliderPanel.Children.Add(slider);
                    control = sliderPanel;
                    break;

                case SettingType.Dropdown:
                    var dropdown = new ComboBox
                    {
                        Width = 200,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Tag = $"{category}.{setting.Name}"
                    };

                    // Add options based on whether we have DropdownOptions or simple Options
                    if (setting.DropdownOptions != null && setting.DropdownOptions.Count > 0)
                    {
                        foreach (var option in setting.DropdownOptions)
                        {
                            var item = new ComboBoxItem { Content = option.Display, Tag = option.Value };
                            dropdown.Items.Add(item);

                            // Select current value
                            if (option.Value?.Equals(setting.Value) == true)
                            {
                                dropdown.SelectedItem = item;
                            }
                        }
                    }
                    else if (setting.Options != null && setting.Options.Count > 0)
                    {
                        foreach (var option in setting.Options)
                        {
                            var item = new ComboBoxItem { Content = option };
                            dropdown.Items.Add(item);

                            // Select current value
                            if (option?.Equals(setting.Value?.ToString()) == true)
                            {
                                dropdown.SelectedItem = item;
                            }
                        }
                    }

                    dropdown.SelectionChanged += (s, e) =>
                    {
                        if (dropdown.SelectedItem is ComboBoxItem selectedItem)
                        {
                            var value = selectedItem.Tag ?? selectedItem.Content?.ToString();
                            OnSettingValueChanged(category, setting.Name, value);
                        }
                    };

                    control = dropdown;
                    break;

                case SettingType.Text:
                    Log.Debug($"Creating Text control for setting: {setting.Name}, Value: {setting.Value}");
                    // Special handling for file/folder paths
                    if (setting.Name == "IdleBackgroundImage" || setting.Name == "PhotoLocation" || setting.Name == "SessionFolder")
                    {
                        var filePanel = new StackPanel
                        {
                            Orientation = Orientation.Vertical
                        };

                        var inputRow = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 0, 0, 5)
                        };

                        var textBox = new TextBox
                        {
                            Text = setting.Value?.ToString() ?? "",
                            Width = 250,
                            Height = 30,
                            Padding = new Thickness(6, 4, 6, 4),
                            FontSize = 11,
                            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                            BorderThickness = new Thickness(1),
                            Tag = $"{category}.{setting.Name}",
                            IsReadOnly = true,
                            VerticalContentAlignment = VerticalAlignment.Center,
                            ToolTip = setting.Value?.ToString() ?? "No file selected"
                        };

                        var browseButton = new Button
                        {
                            Content = "Browse",
                            Margin = new Thickness(5, 0, 0, 0),
                            Padding = new Thickness(10, 4, 10, 4),
                            FontSize = 11,
                            Background = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            MinWidth = 60,
                            Height = 30
                        };

                        var clearButton = new Button
                        {
                            Content = "Clear",
                            Margin = new Thickness(5, 0, 0, 0),
                            Padding = new Thickness(10, 4, 10, 4),
                            FontSize = 11,
                            Background = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            MinWidth = 50,
                            Height = 30
                        };

                        browseButton.Click += (s, e) =>
                        {
                            if (setting.Name == "IdleBackgroundImage")
                            {
                                // File picker for background image
                                var dialog = new OpenFileDialog
                                {
                                    Title = "Select Background Image",
                                    Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                                    CheckFileExists = true
                                };

                                if (dialog.ShowDialog() == true)
                                {
                                    textBox.Text = dialog.FileName;
                                    OnSettingValueChanged(category, setting.Name, dialog.FileName);
                                }
                            }
                            else
                            {
                                // Folder picker for PhotoLocation and SessionFolder
                                var dialog = new System.Windows.Forms.FolderBrowserDialog
                                {
                                    Description = $"Select {setting.DisplayName}",
                                    ShowNewFolderButton = true
                                };

                                if (!string.IsNullOrEmpty(textBox.Text))
                                {
                                    dialog.SelectedPath = textBox.Text;
                                }

                                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                                {
                                    textBox.Text = dialog.SelectedPath;
                                    OnSettingValueChanged(category, setting.Name, dialog.SelectedPath);
                                }
                            }
                        };

                        clearButton.Click += (s, e) =>
                        {
                            textBox.Text = "";
                            OnSettingValueChanged(category, setting.Name, "");
                        };

                        inputRow.Children.Add(textBox);
                        inputRow.Children.Add(browseButton);
                        inputRow.Children.Add(clearButton);

                        filePanel.Children.Add(inputRow);
                        control = filePanel;
                        Log.Debug($"Created file picker control for {setting.Name} with input row containing {inputRow.Children.Count} children");
                    }
                    else
                    {
                        // Regular text input for other text settings
                        var textBox = new TextBox
                        {
                            Text = setting.Value?.ToString() ?? "",
                            Width = 400,
                            Padding = new Thickness(8, 6, 8, 6),
                            FontSize = 12,
                            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                            Foreground = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                            BorderThickness = new Thickness(1),
                            Tag = $"{category}.{setting.Name}"
                        };

                        textBox.TextChanged += (s, e) =>
                        {
                            OnSettingValueChanged(category, setting.Name, textBox.Text);
                        };

                        control = textBox;
                    }
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

        /// <summary>
        /// Handle search text changes
        /// </summary>
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchText = SearchTextBox.Text ?? string.Empty;

                // Show/hide clear button
                if (ClearSearchButton != null)
                {
                    ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(_searchText)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }

                // Apply filter with slight delay to improve performance while typing
                if (_debounceTimers.ContainsKey(SearchTextBox))
                {
                    _debounceTimers[SearchTextBox].Stop();
                }
                else
                {
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(300)
                    };
                    timer.Tick += (s, args) =>
                    {
                        timer.Stop();
                        LoadCategories();
                    };
                    _debounceTimers[SearchTextBox] = timer;
                }

                _debounceTimers[SearchTextBox].Start();
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Search error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear search text
        /// </summary>
        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            SearchTextBox.Focus();
        }

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
        
        #region Debug Settings
        
        /// <summary>
        /// Load debug/logging settings with toggle controls
        /// </summary>
        private void LoadDebugSettings()
        {
            try
            {
                // Debug Logging Enable toggle
                var enableDebugSetting = new SettingItem
                {
                    Name = "EnableDebugLogging",
                    DisplayName = "Enable Debug Logging",
                    Type = SettingType.Toggle,
                    Value = DebugService.Instance.IsDebugEnabled,
                    Description = "Enable detailed debug logging output for troubleshooting"
                };
                var enableDebugControl = CreateDebugSettingControl(enableDebugSetting);
                SettingsListPanel.Children.Add(enableDebugControl);
                
                // Debug Output Location info
                var outputLocationInfo = CreateDebugInfoSection("Debug Output Location", 
                    "Debug messages appear in:\n• Visual Studio Debug Output window\n• Debug console applications\n• Logging tools and frameworks");
                SettingsListPanel.Children.Add(outputLocationInfo);
                
                // Debug Categories info
                var categoriesInfo = CreateDebugInfoSection("Debug Categories", 
                    "When enabled, debug logging includes:\n• UI Service operations\n• Camera operations\n• Queue service status\n• Gallery actions\n• Share operations\n• Session management");
                SettingsListPanel.Children.Add(categoriesInfo);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load debug settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create debug setting control with immediate feedback
        /// </summary>
        private UIElement CreateDebugSettingControl(SettingItem setting)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 15, 0) };
            
            // Setting name
            var nameText = new TextBlock
            {
                Text = setting.DisplayName,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(nameText);
            
            // Description
            var descText = new TextBlock
            {
                Text = setting.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(descText);
            
            Grid.SetColumn(stackPanel, 0);
            grid.Children.Add(stackPanel);
            
            // Toggle switch
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("SettingToggleStyle"),
                IsChecked = (bool)setting.Value,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Immediate toggle handling for debug service
            toggle.Checked += (s, e) =>
            {
                DebugService.Instance.IsDebugEnabled = true;
                Log.Debug("Debug logging enabled via Settings Overlay");
                // Update category summary
                LoadCategories();
            };
            
            toggle.Unchecked += (s, e) =>
            {
                Log.Debug("Debug logging disabled via Settings Overlay");
                DebugService.Instance.IsDebugEnabled = false;
                // Update category summary
                LoadCategories();
            };
            
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            
            container.Child = grid;
            return container;
        }
        
        /// <summary>
        /// Create debug information section
        /// </summary>
        private UIElement CreateDebugInfoSection(string title, string content)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                BorderThickness = new Thickness(1)
            };
            
            var stackPanel = new StackPanel();
            
            // Title
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stackPanel.Children.Add(titleText);
            
            // Content
            var contentText = new TextBlock
            {
                Text = content,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16
            };
            stackPanel.Children.Add(contentText);
            
            container.Child = stackPanel;
            return container;
        }
        
        #endregion
        
        #region Capture Modes Settings
        
        /// <summary>
        /// Load capture modes settings
        /// </summary>
        private void LoadCaptureModesSettings()
        {
            try
            {
                var captureModesService = CaptureModesService.Instance;
                var modulesConfig = PhotoboothModulesConfig.Instance;
                
                // Enable Multiple Capture Modes toggle
                var enableModesSetting = new SettingItem
                {
                    Name = "CaptureModesEnabled",
                    DisplayName = "Enable Multiple Capture Modes",
                    Type = SettingType.Toggle,
                    Value = Properties.Settings.Default.CaptureModesEnabled,
                    Description = "Allow users to select different capture types"
                };
                var enableModesControl = CreateSettingControl(enableModesSetting, "CaptureModes");
                SettingsListPanel.Children.Add(enableModesControl);
                
                // Individual mode toggles
                var modesContainer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(15)
                };
                
                var modesGrid = new Grid();
                modesGrid.ColumnDefinitions.Add(new ColumnDefinition());
                modesGrid.ColumnDefinitions.Add(new ColumnDefinition());
                modesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                modesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                modesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                modesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                modesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                
                // Title
                var titleText = new TextBlock
                {
                    Text = "Available Capture Modes",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(titleText, 0);
                Grid.SetColumnSpan(titleText, 2);
                modesGrid.Children.Add(titleText);
                
                // Photo Mode
                AddModeToggle(modesGrid, "Photo", Properties.Settings.Default.CaptureModePhoto, 1, 0,
                    (value) => { Properties.Settings.Default.CaptureModePhoto = value; Properties.Settings.Default.Save(); });
                
                // Video Mode - uses modulesConfig
                AddModeToggle(modesGrid, "Video", modulesConfig.VideoEnabled, 1, 1,
                    (value) => { 
                        modulesConfig.VideoEnabled = value; 
                        captureModesService.SetModeEnabled(Photobooth.Services.CaptureMode.Video, value);
                    });
                
                // Boomerang Mode
                AddModeToggle(modesGrid, "Boomerang", Properties.Settings.Default.CaptureModeBoomerang, 2, 0,
                    (value) => { Properties.Settings.Default.CaptureModeBoomerang = value; Properties.Settings.Default.Save(); });
                
                // GIF Mode
                AddModeToggle(modesGrid, "GIF", Properties.Settings.Default.CaptureModeGif, 2, 1,
                    (value) => { Properties.Settings.Default.CaptureModeGif = value; Properties.Settings.Default.Save(); });
                
                // Green Screen Mode
                AddModeToggle(modesGrid, "Green Screen", Properties.Settings.Default.CaptureModeGreenScreen, 3, 0,
                    (value) => { Properties.Settings.Default.CaptureModeGreenScreen = value; Properties.Settings.Default.Save(); });
                
                // AI Mode
                AddModeToggle(modesGrid, "AI Photo", Properties.Settings.Default.CaptureModeAI, 3, 1,
                    (value) => { Properties.Settings.Default.CaptureModeAI = value; Properties.Settings.Default.Save(); });
                
                // Flipbook Mode
                AddModeToggle(modesGrid, "Flipbook", Properties.Settings.Default.CaptureModeFlipbook, 4, 0,
                    (value) => { Properties.Settings.Default.CaptureModeFlipbook = value; Properties.Settings.Default.Save(); });
                
                modesContainer.Child = modesGrid;
                SettingsListPanel.Children.Add(modesContainer);
                
                // Default Mode selection
                var defaultModeContainer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(15)
                };
                
                var defaultModePanel = new StackPanel();
                
                var defaultModeTitle = new TextBlock
                {
                    Text = "Default Capture Mode",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                defaultModePanel.Children.Add(defaultModeTitle);
                
                var defaultModeCombo = new ComboBox
                {
                    Width = 200,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                
                // Add available modes to combo
                var modes = new[] { "Photo", "Video", "Boomerang", "Gif", "GreenScreen", "AI", "Flipbook" };
                foreach (var mode in modes)
                {
                    defaultModeCombo.Items.Add(new ComboBoxItem { Content = mode });
                }
                
                // Set current selection
                var currentDefault = Properties.Settings.Default.DefaultCaptureMode ?? "Photo";
                for (int i = 0; i < defaultModeCombo.Items.Count; i++)
                {
                    if ((defaultModeCombo.Items[i] as ComboBoxItem)?.Content?.ToString() == currentDefault)
                    {
                        defaultModeCombo.SelectedIndex = i;
                        break;
                    }
                }
                
                defaultModeCombo.SelectionChanged += (s, e) =>
                {
                    if (defaultModeCombo.SelectedItem is ComboBoxItem selectedItem)
                    {
                        var modeName = selectedItem.Content.ToString();
                        Properties.Settings.Default.DefaultCaptureMode = modeName;
                        Properties.Settings.Default.Save();
                        
                        if (Enum.TryParse<Photobooth.Services.CaptureMode>(modeName, out var mode))
                        {
                            captureModesService.CurrentMode = mode;
                        }
                    }
                };
                
                defaultModePanel.Children.Add(defaultModeCombo);
                defaultModeContainer.Child = defaultModePanel;
                SettingsListPanel.Children.Add(defaultModeContainer);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load capture modes settings: {ex.Message}");
            }
        }
        
        private void AddModeToggle(Grid container, string modeName, bool isChecked, int row, int column, Action<bool> onChanged)
        {
            var checkBox = new CheckBox
            {
                Content = modeName,
                IsChecked = isChecked,
                Foreground = Brushes.White,
                Margin = new Thickness(5)
            };
            
            checkBox.Checked += (s, e) => onChanged(true);
            checkBox.Unchecked += (s, e) => onChanged(false);
            
            Grid.SetRow(checkBox, row);
            Grid.SetColumn(checkBox, column);
            container.Children.Add(checkBox);
        }
        
        #endregion
        
        #region Live View Camera Settings
        
        /// <summary>
        /// Load live view camera controls
        /// </summary>
        private void LoadLiveViewCameraSettings()
        {
            try
            {
                var liveViewService = LiveViewCameraControlService.Instance;
                
                // Enable/Disable Live View Controls toggle
                var enableControlsSetting = new SettingItem
                {
                    Name = "EnableLiveViewControls",
                    DisplayName = "Enable Live View Camera Controls",
                    Type = SettingType.Toggle,
                    Value = liveViewService.IsEnabled,
                    Description = "Enable real-time camera adjustments that affect live view display"
                };
                var enableControlsControl = CreateLiveViewToggleControl(enableControlsSetting);
                SettingsListPanel.Children.Add(enableControlsControl);
                
                // Camera Settings Section
                if (liveViewService.IsEnabled)
                {
                    // ISO Control
                    var isoControl = CreateLiveViewComboControl("ISO", "Current ISO Setting", 
                        liveViewService.GetAvailableISOValues(), liveViewService.CurrentISO,
                        (value) => liveViewService.SetISO(value));
                    SettingsListPanel.Children.Add(isoControl);
                    
                    // Aperture Control
                    var apertureControl = CreateLiveViewComboControl("Aperture", "Current Aperture Setting", 
                        liveViewService.GetAvailableApertureValues(), liveViewService.CurrentAperture,
                        (value) => liveViewService.SetAperture(value));
                    SettingsListPanel.Children.Add(apertureControl);
                    
                    // Shutter Speed Control
                    var shutterControl = CreateLiveViewComboControl("Shutter Speed", "Current Shutter Speed Setting", 
                        liveViewService.GetAvailableShutterSpeedValues(), liveViewService.CurrentShutterSpeed,
                        (value) => liveViewService.SetShutterSpeed(value));
                    SettingsListPanel.Children.Add(shutterControl);
                    
                    // White Balance Control
                    var wbControl = CreateLiveViewComboControl("White Balance", "Current White Balance Setting", 
                        liveViewService.GetAvailableWhiteBalanceValues(), liveViewService.CurrentWhiteBalance,
                        (value) => liveViewService.SetWhiteBalance(value));
                    SettingsListPanel.Children.Add(wbControl);
                    
                    // Exposure Compensation Control
                    var expControl = CreateLiveViewComboControl("Exposure Compensation", "Current Exposure Compensation Setting", 
                        liveViewService.GetAvailableExposureCompensationValues(), liveViewService.CurrentExposureCompensation,
                        (value) => liveViewService.SetExposureCompensation(value));
                    SettingsListPanel.Children.Add(expControl);
                }
                
                // Information Section
                var infoSection = CreateLiveViewInfoSection();
                SettingsListPanel.Children.Add(infoSection);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load live view camera settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create live view toggle control with immediate feedback
        /// </summary>
        private UIElement CreateLiveViewToggleControl(SettingItem setting)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 15, 0) };
            
            // Setting name
            var nameText = new TextBlock
            {
                Text = setting.DisplayName,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(nameText);
            
            // Description
            var descText = new TextBlock
            {
                Text = setting.Description,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(descText);
            
            Grid.SetColumn(stackPanel, 0);
            grid.Children.Add(stackPanel);
            
            // Toggle switch
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("SettingToggleStyle"),
                IsChecked = (bool)setting.Value,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // Immediate toggle handling
            toggle.Checked += (s, e) =>
            {
                LiveViewCameraControlService.Instance.IsEnabled = true;
                LoadCategories(); // Refresh category summary
                LoadLiveViewCameraSettings(); // Refresh this panel
            };
            
            toggle.Unchecked += (s, e) =>
            {
                LiveViewCameraControlService.Instance.IsEnabled = false;
                LoadCategories(); // Refresh category summary  
                LoadLiveViewCameraSettings(); // Refresh this panel
            };
            
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            
            container.Child = grid;
            return container;
        }
        
        /// <summary>
        /// Create live view combo box control for camera settings
        /// </summary>
        private UIElement CreateLiveViewComboControl(string displayName, string description, 
            List<string> options, string currentValue, Action<string> onChange)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 15, 0) };
            
            // Setting name
            var nameText = new TextBlock
            {
                Text = displayName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 3)
            };
            stackPanel.Children.Add(nameText);
            
            // Description
            var descText = new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0))
            };
            stackPanel.Children.Add(descText);
            
            Grid.SetColumn(stackPanel, 0);
            grid.Children.Add(stackPanel);
            
            // ComboBox
            var comboBox = new ComboBox
            {
                Width = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                BorderThickness = new Thickness(1)
            };
            
            foreach (var option in options)
            {
                comboBox.Items.Add(option);
            }
            
            // Set current selection
            var currentIndex = options.IndexOf(currentValue);
            if (currentIndex >= 0)
                comboBox.SelectedIndex = currentIndex;
            
            // Handle selection changes
            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedItem != null)
                {
                    onChange(comboBox.SelectedItem.ToString());
                    LoadCategories(); // Refresh category summary
                }
            };
            
            Grid.SetColumn(comboBox, 1);
            grid.Children.Add(comboBox);
            
            container.Child = grid;
            return container;
        }
        
        /// <summary>
        /// Create live view information section
        /// </summary>
        private UIElement CreateLiveViewInfoSection()
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                BorderThickness = new Thickness(1)
            };
            
            var stackPanel = new StackPanel();
            
            // Title
            var titleText = new TextBlock
            {
                Text = "Live View Camera Controls",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stackPanel.Children.Add(titleText);
            
            // Content
            var contentText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16
            };
            contentText.Inlines.Add(new Run("Real-time camera adjustments that affect live view display:\n"));
            contentText.Inlines.Add(new Run("• ISO: Controls sensor sensitivity and image brightness\n"));
            contentText.Inlines.Add(new Run("• Aperture: Controls depth of field and exposure\n"));
            contentText.Inlines.Add(new Run("• Shutter Speed: Controls motion blur and exposure\n"));
            contentText.Inlines.Add(new Run("• White Balance: Controls color temperature\n"));
            contentText.Inlines.Add(new Run("• Exposure Compensation: Fine-tune overall exposure\n\n"));
            contentText.Inlines.Add(new Run("Note: These settings affect live view preview only. Original camera settings are restored when disabled."));
            
            stackPanel.Children.Add(contentText);
            
            container.Child = stackPanel;
            return container;
        }
        
        #endregion

        #region Cloud Share Settings

        /// <summary>
        /// Create a toggle control for cloud share settings
        /// </summary>
        private UIElement CreateCloudShareToggle(string displayName, bool value, string description, Action<bool> onChanged)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var stackPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = displayName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stackPanel.Children.Add(nameText);

            var toggle = new CheckBox
            {
                IsChecked = value,
                Style = FindResource("SettingToggleStyle") as Style
            };
            toggle.Checked += (s, e) => onChanged?.Invoke(true);
            toggle.Unchecked += (s, e) => onChanged?.Invoke(false);
            stackPanel.Children.Add(toggle);

            if (!string.IsNullOrEmpty(description))
            {
                var descText = new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    Margin = new Thickness(0, 5, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                stackPanel.Children.Add(descText);
            }

            container.Child = stackPanel;
            return container;
        }

        /// <summary>
        /// Create a text input control for cloud settings
        /// </summary>
        private UIElement CreateCloudTextInput(string displayName, string value, string description, bool isPassword, Action<string> onChanged)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var stackPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = displayName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stackPanel.Children.Add(nameText);

            if (isPassword)
            {
                var passwordBox = new PasswordBox
                {
                    Width = 300,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                    Padding = new Thickness(5)
                };

                // Set password after creating control to avoid triggering change event
                passwordBox.Password = value ?? "";

                // Use a flag to prevent saving during initial load
                bool isInitializing = true;
                passwordBox.Loaded += (s, e) => isInitializing = false;

                passwordBox.PasswordChanged += (s, e) =>
                {
                    if (isInitializing || onChanged == null) return;
                    // Debounce to avoid heavy work on every keystroke/paste
                    DebounceInput(passwordBox, () => onChanged.Invoke(passwordBox.Password), 400);
                };

                // Also handle lost focus to ensure password is saved
                passwordBox.LostFocus += (s, e) =>
                {
                    if (!isInitializing && onChanged != null)
                    {
                        onChanged.Invoke(passwordBox.Password);
                    }
                };

                stackPanel.Children.Add(passwordBox);
            }
            else
            {
                var textBox = new TextBox
                {
                    Text = value ?? "",
                    Width = 300,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                    Padding = new Thickness(5)
                };
                // Debounce changes to prevent lag when typing/pasting
                textBox.TextChanged += (s, e) =>
                {
                    if (onChanged == null) return;
                    DebounceInput(textBox, () => onChanged.Invoke(textBox.Text), 300);
                };
                // Ensure save on commit
                textBox.LostFocus += (s, e) => onChanged?.Invoke(textBox.Text);
                stackPanel.Children.Add(textBox);
            }

            if (!string.IsNullOrEmpty(description))
            {
                var descText = new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    Margin = new Thickness(0, 5, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                stackPanel.Children.Add(descText);
            }

            container.Child = stackPanel;
            return container;
        }

        /// <summary>
        /// Debounce helper to coalesce rapid input changes into a single callback.
        /// </summary>
        private void DebounceInput(Control control, Action callback, int milliseconds)
        {
            if (!_debounceTimers.TryGetValue(control, out var timer))
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    callback?.Invoke();
                };
                _debounceTimers[control] = timer;
            }
            else
            {
                timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            }

            // Restart timer
            timer.Stop();
            timer.Start();
        }

        /// <summary>
        /// Create a toggle control for cloud sync settings
        /// </summary>
        private UIElement CreateCloudNumberInput(string displayName, double value, string description, double min, double max, Action<double> onChanged)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stackPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = displayName,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(nameText);

            if (!string.IsNullOrEmpty(description))
            {
                var descText = new TextBlock
                {
                    Text = description,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                stackPanel.Children.Add(descText);
            }

            Grid.SetColumn(stackPanel, 0);
            grid.Children.Add(stackPanel);

            // Create number input with up/down buttons
            var inputPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBox = new TextBox
            {
                Text = value.ToString(),
                Width = 80,
                Height = 35,
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            // Up button
            var upButton = new Button
            {
                Content = "▲",
                Width = 30,
                Height = 35,
                Margin = new Thickness(5, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            };

            // Down button
            var downButton = new Button
            {
                Content = "▼",
                Width = 30,
                Height = 35,
                Margin = new Thickness(2, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x45)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            };

            // Event handlers
            Action updateValue = () =>
            {
                if (double.TryParse(textBox.Text, out double newValue))
                {
                    newValue = Math.Max(min, Math.Min(max, newValue));
                    textBox.Text = newValue.ToString();
                    onChanged?.Invoke(newValue);
                }
            };

            textBox.LostFocus += (s, e) => updateValue();
            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    updateValue();
                }
            };

            upButton.Click += (s, e) =>
            {
                if (double.TryParse(textBox.Text, out double currentValue))
                {
                    currentValue = Math.Min(max, currentValue + 1);
                    textBox.Text = currentValue.ToString();
                    onChanged?.Invoke(currentValue);
                }
            };

            downButton.Click += (s, e) =>
            {
                if (double.TryParse(textBox.Text, out double currentValue))
                {
                    currentValue = Math.Max(min, currentValue - 1);
                    textBox.Text = currentValue.ToString();
                    onChanged?.Invoke(currentValue);
                }
            };

            inputPanel.Children.Add(textBox);
            inputPanel.Children.Add(upButton);
            inputPanel.Children.Add(downButton);

            Grid.SetColumn(inputPanel, 1);
            grid.Children.Add(inputPanel);

            container.Child = grid;
            return container;
        }

        private UIElement CreateCloudSyncToggle(string displayName, bool value, string description, string settingName)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var stackPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = displayName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stackPanel.Children.Add(nameText);

            var toggle = new CheckBox
            {
                IsChecked = value,
                Style = FindResource("SettingToggleStyle") as Style
            };

            toggle.Checked += (s, e) => {
                var property = Properties.Settings.Default.GetType().GetProperty(settingName);
                if (property != null)
                {
                    property.SetValue(Properties.Settings.Default, true);
                    Properties.Settings.Default.Save();
                }
            };

            toggle.Unchecked += (s, e) => {
                var property = Properties.Settings.Default.GetType().GetProperty(settingName);
                if (property != null)
                {
                    property.SetValue(Properties.Settings.Default, false);
                    Properties.Settings.Default.Save();
                }
            };

            stackPanel.Children.Add(toggle);

            if (!string.IsNullOrEmpty(description))
            {
                var descText = new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    Margin = new Thickness(0, 5, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                stackPanel.Children.Add(descText);
            }

            container.Child = stackPanel;
            return container;
        }

        /// <summary>
        /// Load cloud share settings
        /// </summary>
        private void LoadCloudShareSettings()
        {
            try
            {
                // Cloud Sharing Enabled
                var cloudEnabled = Environment.GetEnvironmentVariable("CLOUD_SHARING_ENABLED", EnvironmentVariableTarget.User) == "True";
                var enableCloudControl = CreateCloudShareToggle("Enable Cloud Sharing",
                    cloudEnabled,
                    "Enable cloud sharing for photos",
                    (value) => {
                        Environment.SetEnvironmentVariable("CLOUD_SHARING_ENABLED", value.ToString(), EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("CLOUD_SHARING_ENABLED", value.ToString(), EnvironmentVariableTarget.Process);
                    });
                SettingsListPanel.Children.Add(enableCloudControl);

                // AWS Access Key
                var awsAccessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User) ?? "";
                var accessKeyControl = CreateCloudTextInput("AWS Access Key ID",
                    awsAccessKey,
                    "AWS Access Key for S3",
                    false,
                    (value) => {
                        value = value?.Trim();
                        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", value, EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", value, EnvironmentVariableTarget.Process);
                    });
                SettingsListPanel.Children.Add(accessKeyControl);

                // AWS Secret Key
                var awsSecretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User) ?? "";
                var secretKeyControl = CreateCloudTextInput("AWS Secret Access Key",
                    awsSecretKey,
                    "AWS Secret Key for S3",
                    true,
                    (value) => {
                        value = value?.Trim();
                        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", value, EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", value, EnvironmentVariableTarget.Process);
                    });
                SettingsListPanel.Children.Add(secretKeyControl);

                // S3 Bucket Name
                var bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME", EnvironmentVariableTarget.User) ?? "photobooth-shares";
                var bucketControl = CreateCloudTextInput("S3 Bucket Name",
                    bucketName,
                    "S3 bucket for storing shared photos",
                    false,
                    (value) => {
                        value = value?.Trim();
                        Environment.SetEnvironmentVariable("S3_BUCKET_NAME", value, EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("S3_BUCKET_NAME", value, EnvironmentVariableTarget.Process);
                    });
                SettingsListPanel.Children.Add(bucketControl);

                // Gallery Base URL
                var galleryUrl = Environment.GetEnvironmentVariable("GALLERY_BASE_URL", EnvironmentVariableTarget.User) ?? "https://photos.yourapp.com";
                var galleryUrlControl = CreateCloudTextInput("Gallery Base URL",
                    galleryUrl,
                    "Base URL for the online gallery",
                    false,
                    (value) => {
                        value = value?.Trim();
                        Environment.SetEnvironmentVariable("GALLERY_BASE_URL", value, EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("GALLERY_BASE_URL", value, EnvironmentVariableTarget.Process);
                    });
                SettingsListPanel.Children.Add(galleryUrlControl);

                // Use Pre‑Signed URLs toggle
                var usePresigned = string.Equals(Environment.GetEnvironmentVariable("USE_PRESIGNED_URLS", EnvironmentVariableTarget.User), "True", StringComparison.OrdinalIgnoreCase);
                var presignedToggle = CreateCloudShareToggle(
                    "Use Pre‑Signed URLs (private bucket)",
                    usePresigned,
                    "When enabled, links embed time‑limited signatures. Disable for clean public URLs (requires public read on events/* or CloudFront).",
                    (value) => {
                        Environment.SetEnvironmentVariable("USE_PRESIGNED_URLS", value.ToString(), EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("USE_PRESIGNED_URLS", value.ToString(), EnvironmentVariableTarget.Process);
                        // Reset provider to apply immediately
                        try { Photobooth.Services.CloudShareProvider.Reset(); } catch {}
                    });
                SettingsListPanel.Children.Add(presignedToggle);

                // Auto Share
                var autoShare = Environment.GetEnvironmentVariable("CLOUD_AUTO_SHARE", EnvironmentVariableTarget.User) == "True";
                var autoShareControl = CreateCloudShareToggle("Auto Share Photos",
                    autoShare,
                    "Automatically share photos after capture",
                    (value) => {
                        Environment.SetEnvironmentVariable("CLOUD_AUTO_SHARE", value.ToString(), EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("CLOUD_AUTO_SHARE", value.ToString(), EnvironmentVariableTarget.Process);
                    });
                SettingsListPanel.Children.Add(autoShareControl);

                // Optimize Photos
                var optimizePhotos = Environment.GetEnvironmentVariable("CLOUD_OPTIMIZE_PHOTOS", EnvironmentVariableTarget.User) != "False";
                var optimizeControl = CreateCloudShareToggle("Optimize Photos for Upload",
                    optimizePhotos,
                    "Compress photos before uploading to save bandwidth",
                    (value) => {
                        Environment.SetEnvironmentVariable("CLOUD_OPTIMIZE_PHOTOS", value.ToString(), EnvironmentVariableTarget.User);
                        Environment.SetEnvironmentVariable("CLOUD_OPTIMIZE_PHOTOS", value.ToString(), EnvironmentVariableTarget.Process);
                    });
                SettingsListPanel.Children.Add(optimizeControl);

                // SMS Settings (if Twilio configured)
                var twilioSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID", EnvironmentVariableTarget.User) ?? "";
                if (!string.IsNullOrEmpty(twilioSid))
                {
                    var enableSms = !string.IsNullOrEmpty(twilioSid);
                    var smsControl = CreateCloudShareToggle("Enable SMS Sharing",
                        enableSms,
                        "Allow sharing photos via SMS",
                        (value) => {
                            // SMS is enabled based on Twilio credentials presence
                            // This is just a display toggle
                        });
                    SettingsListPanel.Children.Add(smsControl);
                }

                // Add info section
                var infoSection = CreateDebugInfoSection("Cloud Share Info",
                    "Cloud sharing allows photos to be uploaded to AWS S3 and shared via QR codes, email, or SMS.\n\n" +
                    "Required: AWS credentials with S3 permissions\n" +
                    "Optional: Twilio credentials for SMS sharing");
                SettingsListPanel.Children.Add(infoSection);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load cloud share settings: {ex.Message}");
            }
        }

        #endregion

        #region Cloud Sync Settings

        /// <summary>
        /// Load cloud sync settings
        /// </summary>
        private void LoadCloudSyncSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;

                // Debug log to verify credentials are loaded
                Log.Debug($"LoadCloudSyncSettings: S3AccessKey present: {!string.IsNullOrEmpty(settings.S3AccessKey)}");
                Log.Debug($"LoadCloudSyncSettings: S3SecretKey length: {settings.S3SecretKey?.Length ?? 0}");
                Log.Debug($"LoadCloudSyncSettings: S3BucketName: {settings.S3BucketName ?? "null"}");

                // Enable Cloud Sync
                var enableSyncControl = CreateCloudSyncToggle("Enable Cloud Sync",
                    settings.EnableCloudSync,
                    "Enable synchronization with cloud storage",
                    "EnableCloudSync");
                SettingsListPanel.Children.Add(enableSyncControl);

                // Auto Sync on Startup
                var autoSyncControl = CreateCloudSyncToggle("Auto Sync on Startup",
                    settings.AutoSyncOnStartup,
                    "Automatically sync when application starts",
                    "AutoSyncOnStartup");
                SettingsListPanel.Children.Add(autoSyncControl);

                // Sync Interval
                var syncIntervalControl = CreateCloudNumberInput("Sync Interval (minutes)",
                    settings.SyncIntervalMinutes,
                    "How often to sync with cloud (minimum 1 minute)",
                    1, 1440, // Min 1 minute, max 24 hours
                    (value) => {
                        Properties.Settings.Default.SyncIntervalMinutes = (int)value;
                        Properties.Settings.Default.Save();
                        // Update the sync timer
                        Photobooth.Services.PhotoBoothSyncService.Instance.SetSyncInterval((int)value);
                    });
                SettingsListPanel.Children.Add(syncIntervalControl);

                // Sync Templates
                var syncTemplatesControl = CreateCloudSyncToggle("Sync Templates",
                    settings.SyncTemplates,
                    "Synchronize template designs across devices",
                    "SyncTemplates");
                SettingsListPanel.Children.Add(syncTemplatesControl);

                // Sync Settings
                var syncSettingsControl = CreateCloudSyncToggle("Sync Settings",
                    settings.SyncSettings,
                    "Synchronize application settings across devices",
                    "SyncSettings");
                SettingsListPanel.Children.Add(syncSettingsControl);

                // Sync Events
                var syncEventsControl = CreateCloudSyncToggle("Sync Events",
                    settings.SyncEvents,
                    "Synchronize event configurations across devices",
                    "SyncEvents");
                SettingsListPanel.Children.Add(syncEventsControl);

                // S3 Access Key
                var s3AccessKeyControl = CreateCloudTextInput("S3 Access Key",
                    settings.S3AccessKey ?? "",
                    "AWS S3 Access Key for sync",
                    false,
                    (value) => {
                        value = value?.Trim();
                        Properties.Settings.Default.S3AccessKey = value;
                        Properties.Settings.Default.Save();
                        // Also update environment variables for PhotoBoothSyncService
                        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", value, EnvironmentVariableTarget.Process);
                        // Reinitialize cloud service with new credentials
                        Photobooth.Services.PhotoBoothSyncService.Instance.ReinitializeCloudService();
                    });
                SettingsListPanel.Children.Add(s3AccessKeyControl);

                // S3 Secret Key
                var s3SecretKeyControl = CreateCloudTextInput("S3 Secret Key",
                    settings.S3SecretKey ?? "",
                    "AWS S3 Secret Key for sync",
                    true,
                    (value) => {
                        Log.Debug($"SettingsOverlay: S3SecretKey changed, new length: {value?.Length ?? 0}");

                        // Only save if value is not null (even empty string is valid if user explicitly cleared it)
                        if (value != null)
                        {
                            value = value?.Trim();
                            Properties.Settings.Default.S3SecretKey = value;
                            Properties.Settings.Default.Save();

                            // Also update environment variables for PhotoBoothSyncService
                            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", value, EnvironmentVariableTarget.Process);

                            // Reinitialize cloud service with new credentials
                            Photobooth.Services.PhotoBoothSyncService.Instance.ReinitializeCloudService();

                            Log.Debug($"SettingsOverlay: S3SecretKey saved successfully");
                        }
                        else
                        {
                            Log.Debug("SettingsOverlay: Attempted to set S3SecretKey to null, ignoring");
                        }
                    });
                SettingsListPanel.Children.Add(s3SecretKeyControl);

                // S3 Bucket Name
                var s3BucketControl = CreateCloudTextInput("S3 Bucket Name",
                    settings.S3BucketName ?? "",
                    "S3 bucket for sync storage",
                    false,
                    (value) => {
                        value = value?.Trim();
                        Properties.Settings.Default.S3BucketName = value;
                        Properties.Settings.Default.Save();
                        // Also update environment variables for PhotoBoothSyncService
                        Environment.SetEnvironmentVariable("S3_BUCKET_NAME", value, EnvironmentVariableTarget.Process);
                        // Reinitialize cloud service with new credentials
                        Photobooth.Services.PhotoBoothSyncService.Instance.ReinitializeCloudService();
                    });
                SettingsListPanel.Children.Add(s3BucketControl);

                // S3 Region
                var s3RegionControl = CreateCloudTextInput("S3 Region",
                    settings.S3Region ?? "us-east-1",
                    "AWS region for S3 bucket (e.g., us-east-1, us-west-2)",
                    false,
                    (value) => {
                        value = value?.Trim();
                        Properties.Settings.Default.S3Region = value;
                        Properties.Settings.Default.Save();
                        // Also update environment variables for PhotoBoothSyncService
                        Environment.SetEnvironmentVariable("S3_REGION", value, EnvironmentVariableTarget.Process);
                        // Reinitialize cloud service with new credentials
                        Photobooth.Services.PhotoBoothSyncService.Instance.ReinitializeCloudService();
                    });
                SettingsListPanel.Children.Add(s3RegionControl);

                // Add sync status info with test button
                var syncService = Photobooth.Services.PhotoBoothSyncService.Instance;
                var statusText = "Not Connected";
                var statusColor = "#FF5722";

                bool hasValidCredentials = !string.IsNullOrEmpty(settings.S3AccessKey) &&
                                          !string.IsNullOrEmpty(settings.S3SecretKey) &&
                                          !string.IsNullOrEmpty(settings.S3BucketName);

                if (hasValidCredentials)
                {
                    statusText = "Ready to Sync";
                    statusColor = "#4CAF50";
                }

                var statusInfo = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 10, 0, 10)
                };

                var statusPanel = new StackPanel();

                // Status header and text
                var statusHeaderPanel = new DockPanel();
                statusHeaderPanel.Children.Add(new TextBlock
                {
                    Text = "Sync Status",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                // Add test and sync buttons if credentials are valid
                if (hasValidCredentials)
                {
                    // Create a stack panel for buttons
                    var buttonPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };

                    // Test Connection button
                    var testButton = new Button
                    {
                        Content = "Test Connection",
                        Width = 120,
                        Height = 28,
                        Margin = new Thickness(5, 0, 0, 5),
                        Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand,
                        FontSize = 12
                    };
                    testButton.Template = CreateButtonTemplate();
                    testButton.Click += async (s, e) => await TestCloudSyncConnection(testButton);
                    buttonPanel.Children.Add(testButton);

                    // Sync Now button
                    var syncButton = new Button
                    {
                        Content = "Sync Now",
                        Width = 100,
                        Height = 28,
                        Margin = new Thickness(5, 0, 0, 5),
                        Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand,
                        FontSize = 12
                    };
                    syncButton.Template = CreateButtonTemplate();
                    syncButton.Click += async (s, e) => await PerformCloudSync(syncButton);
                    buttonPanel.Children.Add(syncButton);

                    DockPanel.SetDock(buttonPanel, Dock.Right);
                    statusHeaderPanel.Children.Add(buttonPanel);
                }

                statusPanel.Children.Add(statusHeaderPanel);

                var statusTextBlock = new TextBlock
                {
                    Name = "CloudSyncStatusText",
                    Text = statusText,
                    FontSize = 12,
                    Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(statusColor)
                };
                statusPanel.Children.Add(statusTextBlock);

                // Add last sync info if available
                var lastSyncInfo = new TextBlock
                {
                    Name = "CloudSyncLastSyncText",
                    Text = "",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    Margin = new Thickness(0, 5, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                statusPanel.Children.Add(lastSyncInfo);

                statusInfo.Child = statusPanel;
                SettingsListPanel.Children.Add(statusInfo);

                // Add info section
                var infoSection = CreateDebugInfoSection("Cloud Sync Info",
                    "Cloud Sync keeps your photobooth data synchronized across multiple devices.\n\n" +
                    "Synced data includes:\n" +
                    "• Photo templates and layouts\n" +
                    "• Application settings\n" +
                    "• Event configurations\n\n" +
                    "Requires AWS S3 credentials with read/write permissions.");
                SettingsListPanel.Children.Add(infoSection);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load cloud sync settings: {ex.Message}");
            }
        }

        #endregion

        #region Cloud Sync Test Methods

        /// <summary>
        /// Test Cloud Sync connection
        /// </summary>
        private async Task TestCloudSyncConnection(Button testButton)
        {
            try
            {
                // Disable button and show testing status
                testButton.IsEnabled = false;
                var originalContent = testButton.Content;
                testButton.Content = "Testing...";

                // Find status text block in the same container
                var statusPanel = testButton.Parent as DockPanel;
                var parentStackPanel = statusPanel?.Parent as StackPanel;
                var statusTextBlock = parentStackPanel?.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.Name == "CloudSyncStatusText");
                var lastSyncTextBlock = parentStackPanel?.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.Name == "CloudSyncLastSyncText");

                if (statusTextBlock != null)
                {
                    statusTextBlock.Text = "Testing connection...";
                    statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Amber
                }

                // Test the connection
                var syncService = Photobooth.Services.PhotoBoothSyncService.Instance;
                bool testResult = false;
                string resultMessage = "";

                try
                {
                    // Try to perform a basic S3 operation like checking if bucket exists
                    var cloudService = syncService.GetCloudService();
                    if (cloudService != null)
                    {
                        // Try to list objects in the bucket (limited to 1) to test connection
                        testResult = await cloudService.TestConnectionAsync();

                        if (testResult)
                        {
                            resultMessage = "Connection successful!";

                            // Try to get last sync time
                            var manifest = await syncService.GetRemoteManifestAsync();
                            if (manifest != null && manifest.LastModified != DateTime.MinValue)
                            {
                                if (lastSyncTextBlock != null)
                                {
                                    lastSyncTextBlock.Text = $"Last sync: {manifest.LastModified:g}";
                                    lastSyncTextBlock.Visibility = Visibility.Visible;
                                }
                            }
                        }
                        else
                        {
                            resultMessage = "Connection failed - check credentials";
                        }
                    }
                    else
                    {
                        resultMessage = "Cloud service not initialized";
                    }
                }
                catch (Exception ex)
                {
                    resultMessage = $"Error: {ex.Message}";
                    Log.Error($"SettingsOverlay: Cloud Sync test failed: {ex.Message}");
                }

                // Update status based on result
                if (statusTextBlock != null)
                {
                    statusTextBlock.Text = resultMessage;
                    statusTextBlock.Foreground = testResult
                        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) // Green
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22)); // Red
                }

                // Show result in button temporarily
                testButton.Content = testResult ? "✓ Success" : "✗ Failed";
                testButton.Background = testResult
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22));

                // Reset button after 3 seconds
                await Task.Delay(3000);
                testButton.Content = originalContent;
                testButton.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                testButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Error testing Cloud Sync: {ex.Message}");
                testButton.Content = "Test Connection";
                testButton.IsEnabled = true;

                MessageBox.Show($"Error testing connection: {ex.Message}", "Cloud Sync Test",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Perform a cloud sync operation
        /// </summary>
        private async Task PerformCloudSync(Button syncButton)
        {
            try
            {
                // Disable button and show syncing status
                syncButton.IsEnabled = false;
                var originalContent = syncButton.Content;
                syncButton.Content = "Syncing...";

                // Find status text blocks in the same container
                var buttonPanel = syncButton.Parent as StackPanel;
                var statusHeaderPanel = buttonPanel?.Parent as DockPanel;
                var statusPanel = statusHeaderPanel?.Parent as StackPanel;
                var statusTextBlock = statusPanel?.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.Name == "CloudSyncStatusText");
                var lastSyncTextBlock = statusPanel?.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.Name == "CloudSyncLastSyncText");

                if (statusTextBlock != null)
                {
                    statusTextBlock.Text = "Synchronizing...";
                    statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Amber
                }

                // Perform the sync
                var syncService = Photobooth.Services.PhotoBoothSyncService.Instance;
                bool syncSuccess = false;
                string resultMessage = "";
                int itemsSynced = 0;

                try
                {
                    // Initialize sync options
                    var syncOptions = new Photobooth.Services.SyncOptions
                    {
                        SyncTemplates = Properties.Settings.Default.SyncTemplates,
                        SyncSettings = Properties.Settings.Default.SyncSettings,
                        SyncEvents = Properties.Settings.Default.SyncEvents,
                        ForceSync = true // Force sync even if no changes detected
                    };

                    // Perform sync
                    var result = await syncService.SyncAsync(syncOptions);

                    if (result != null && result.Success)
                    {
                        syncSuccess = true;
                        itemsSynced = result.TemplatesSynced + result.SettingsSynced +
                                     result.EventsSynced + result.DatabaseItemsSynced;
                        resultMessage = $"Sync completed! {itemsSynced} items synchronized";

                        // Update last sync time
                        if (lastSyncTextBlock != null)
                        {
                            lastSyncTextBlock.Text = $"Last sync: {DateTime.Now:g}";
                            lastSyncTextBlock.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        resultMessage = result?.Message ?? "Sync failed - unknown error";
                    }
                }
                catch (Exception ex)
                {
                    resultMessage = $"Sync error: {ex.Message}";
                    Log.Error($"SettingsOverlay: Cloud Sync failed: {ex.Message}");
                }

                // Update status based on result
                if (statusTextBlock != null)
                {
                    statusTextBlock.Text = resultMessage;
                    statusTextBlock.Foreground = syncSuccess
                        ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) // Green
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22)); // Red
                }

                // Show result in button temporarily
                syncButton.Content = syncSuccess ? $"✓ {itemsSynced} items" : "✗ Failed";
                syncButton.Background = syncSuccess
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22));

                // Reset button after 3 seconds
                await Task.Delay(3000);
                syncButton.Content = originalContent;
                syncButton.Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                syncButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Error performing sync: {ex.Message}");
                syncButton.Content = "Sync Now";
                syncButton.IsEnabled = true;

                MessageBox.Show($"Error performing sync: {ex.Message}", "Cloud Sync",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

        #region Video and Animation Settings

        /// <summary>
        /// Load GIF/Animation settings
        /// </summary>
        private void LoadGifAnimationSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;

                // Enable GIF Generation
                var enableGifControl = CreateSettingToggle("Enable GIF Generation",
                    settings.EnableGifGeneration,
                    "Create animated GIFs from captured photos",
                    "EnableGifGeneration",
                    (value) =>
                    {
                        Properties.Settings.Default.EnableGifGeneration = value;
                        Properties.Settings.Default.Save();
                    });
                SettingsListPanel.Children.Add(enableGifControl);

                // Frame Delay
                var frameDelayControl = CreateSliderControl("Frame Delay",
                    settings.GifFrameDelay / 1000.0, // Convert ms to seconds for display
                    0.1, 5.0, 0.1,
                    "seconds",
                    "Speed of animation playback",
                    (value) =>
                    {
                        Properties.Settings.Default.GifFrameDelay = (int)(value * 1000); // Convert back to ms
                        Properties.Settings.Default.Save();
                    });
                SettingsListPanel.Children.Add(frameDelayControl);

                // Animation Quality
                var qualityControl = CreateSliderControl("Animation Quality",
                    settings.GifQuality,
                    1, 100, 1,
                    "%",
                    "Quality of generated animations",
                    (value) =>
                    {
                        Properties.Settings.Default.GifQuality = (int)value;
                        Properties.Settings.Default.Save();
                    });
                SettingsListPanel.Children.Add(qualityControl);

                // Add note about more settings coming soon
                var noteText = new TextBlock
                {
                    Text = "Additional MP4 and looping options coming soon...",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(15, 10, 15, 5)
                };
                SettingsListPanel.Children.Add(noteText);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load GIF/Animation settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Load Video Recording settings
        /// </summary>
        private void LoadVideoRecordingSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;

                // Video Duration
                var durationControl = CreateSliderControl("Video Duration",
                    settings.VideoDuration,
                    5, 60, 5,
                    "seconds",
                    "Maximum video recording duration",
                    (value) =>
                    {
                        Properties.Settings.Default.VideoDuration = (int)value;
                        Properties.Settings.Default.Save();
                    });
                SettingsListPanel.Children.Add(durationControl);

                // Video Quality
                var qualityItems = new List<string> { "720p", "1080p", "4K" };
                var qualityControl = CreateDropdownControl("Video Quality",
                    settings.VideoQuality ?? "1080p",
                    qualityItems,
                    "Resolution of recorded videos",
                    (value) =>
                    {
                        Properties.Settings.Default.VideoQuality = value;
                        Properties.Settings.Default.Save();
                    });
                SettingsListPanel.Children.Add(qualityControl);

                // Video Frame Rate
                var frameRateControl = CreateSliderControl("Frame Rate",
                    int.TryParse(settings.VideoFrameRate, out int fr) ? fr : 30,
                    15, 60, 5,
                    "fps",
                    "Frames per second for video recording",
                    (value) =>
                    {
                        Properties.Settings.Default.VideoFrameRate = ((int)value).ToString();
                        Properties.Settings.Default.Save();
                    });
                SettingsListPanel.Children.Add(frameRateControl);

                // Add note about more settings coming soon
                var noteText = new TextBlock
                {
                    Text = "Additional video recording options including audio and compression settings coming soon...",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(15, 10, 15, 5)
                };
                SettingsListPanel.Children.Add(noteText);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load Video Recording settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Load Boomerang settings
        /// </summary>
        private void LoadBoomerangSettings()
        {
            try
            {
                // Add placeholder message for now
                var container = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(15)
                };

                var infoText = new TextBlock
                {
                    Text = "Boomerang settings allow you to create forward-backward looping videos.\n\nSettings for frame count, playback speed, and auto-generation will be available in a future update.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap
                };

                container.Child = infoText;
                SettingsListPanel.Children.Add(container);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load Boomerang settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Load Flipbook settings
        /// </summary>
        private void LoadFlipbookSettings()
        {
            try
            {
                // Add placeholder message for now
                var container = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(15)
                };

                var infoText = new TextBlock
                {
                    Text = "Flipbook settings allow you to create animated flipbook-style presentations.\n\nSettings for page count, flip speed, style selection, and PDF generation will be available in a future update.",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap
                };

                container.Child = infoText;
                SettingsListPanel.Children.Add(container);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsOverlay: Failed to load Flipbook settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper to create a toggle control
        /// </summary>
        private UIElement CreateSettingToggle(string title, bool value, string description, string settingName, Action<bool> onChanged)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var stackPanel = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(titleBlock);

            if (!string.IsNullOrEmpty(description))
            {
                var descBlock = new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                stackPanel.Children.Add(descBlock);
            }

            var toggle = new CheckBox
            {
                IsChecked = value,
                Style = FindResource("SettingToggleStyle") as Style
            };
            toggle.Checked += (s, e) => onChanged(true);
            toggle.Unchecked += (s, e) => onChanged(false);
            stackPanel.Children.Add(toggle);

            container.Child = stackPanel;
            return container;
        }

        /// <summary>
        /// Helper to create a slider control
        /// </summary>
        private UIElement CreateSliderControl(string title, double value, double min, double max, double tickFrequency,
            string unit, string description, Action<double> onChanged)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var stackPanel = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(titleBlock);

            if (!string.IsNullOrEmpty(description))
            {
                var descBlock = new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                stackPanel.Children.Add(descBlock);
            }

            var valueText = new TextBlock
            {
                Text = $"{value:F1} {unit}",
                FontSize = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(valueText);

            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                TickFrequency = tickFrequency,
                IsSnapToTickEnabled = true
            };
            slider.ValueChanged += (s, e) =>
            {
                valueText.Text = $"{e.NewValue:F1} {unit}";
                onChanged(e.NewValue);
            };
            stackPanel.Children.Add(slider);

            container.Child = stackPanel;
            return container;
        }

        /// <summary>
        /// Helper to create a number input control
        /// </summary>
        private UIElement CreateNumberInput(string title, int value, string description, int min, int max, Action<double> onChanged)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var stackPanel = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(titleBlock);

            if (!string.IsNullOrEmpty(description))
            {
                var descBlock = new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                stackPanel.Children.Add(descBlock);
            }

            var inputPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var textBox = new TextBox
            {
                Text = value.ToString(),
                Width = 80,
                FontSize = 12,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            textBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(textBox.Text, out int newValue))
                {
                    if (newValue >= min && newValue <= max)
                    {
                        onChanged(newValue);
                    }
                }
            };
            inputPanel.Children.Add(textBox);

            var rangeText = new TextBlock
            {
                Text = $"  ({min} - {max})",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                VerticalAlignment = VerticalAlignment.Center
            };
            inputPanel.Children.Add(rangeText);

            stackPanel.Children.Add(inputPanel);

            container.Child = stackPanel;
            return container;
        }

        /// <summary>
        /// Helper to create a dropdown control
        /// </summary>
        private UIElement CreateDropdownControl(string title, string selectedValue, List<string> items,
            string description, Action<string> onChanged)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15)
            };

            var stackPanel = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            stackPanel.Children.Add(titleBlock);

            if (!string.IsNullOrEmpty(description))
            {
                var descBlock = new TextBlock
                {
                    Text = description,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                stackPanel.Children.Add(descBlock);
            }

            var comboBox = new ComboBox
            {
                ItemsSource = items,
                SelectedValue = selectedValue,
                FontSize = 12,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5)
            };

            comboBox.SelectionChanged += (s, e) =>
            {
                if (comboBox.SelectedItem != null)
                {
                    onChanged(comboBox.SelectedItem.ToString());
                }
            };
            stackPanel.Children.Add(comboBox);

            container.Child = stackPanel;
            return container;
        }

        #endregion

        #region Background Removal Settings

        /// <summary>
        /// Load background removal settings with event background management
        /// </summary>
        private void LoadBackgroundRemovalSettings()
        {
            try
            {
                // Enable Background Removal
                var enableRemovalSetting = new SettingItem
                {
                    Name = "EnableBackgroundRemoval",
                    DisplayName = "Enable Background Removal",
                    Type = SettingType.Toggle,
                    Value = Properties.Settings.Default.EnableBackgroundRemoval
                };
                var enableRemovalControl = CreateSettingControl(enableRemovalSetting, "BackgroundRemoval");
                SettingsListPanel.Children.Add(enableRemovalControl);

                // Enable Live View Background Removal
                var enableLiveViewSetting = new SettingItem
                {
                    Name = "EnableLiveViewBackgroundRemoval",
                    DisplayName = "Enable Live View Background Removal",
                    Type = SettingType.Toggle,
                    Value = Properties.Settings.Default.EnableLiveViewBackgroundRemoval
                };
                var enableLiveViewControl = CreateSettingControl(enableLiveViewSetting, "BackgroundRemoval");
                SettingsListPanel.Children.Add(enableLiveViewControl);

                // Background Removal Quality
                var qualitySetting = new SettingItem
                {
                    Name = "BackgroundRemovalQuality",
                    DisplayName = "Processing Quality",
                    Type = SettingType.Dropdown,
                    Value = Properties.Settings.Default.BackgroundRemovalQuality,
                    DropdownOptions = new List<DropdownOption>
                    {
                        new DropdownOption { Display = "Low (Fast)", Value = "Low" },
                        new DropdownOption { Display = "Medium (Balanced)", Value = "Medium" },
                        new DropdownOption { Display = "High (Best Quality)", Value = "High" }
                    }
                };
                var qualityControl = CreateSettingControl(qualitySetting, "BackgroundRemoval");
                SettingsListPanel.Children.Add(qualityControl);

                // Edge Refinement Slider
                var edgeRefinementSetting = new SettingItem
                {
                    Name = "BackgroundRemovalEdgeRefinement",
                    DisplayName = "Edge Refinement",
                    Type = SettingType.Slider,
                    Value = Properties.Settings.Default.BackgroundRemovalEdgeRefinement,
                    Min = 0,
                    Max = 10,
                    Unit = ""
                };
                var edgeRefinementControl = CreateSettingControl(edgeRefinementSetting, "BackgroundRemoval");
                SettingsListPanel.Children.Add(edgeRefinementControl);

                // GPU Acceleration
                var gpuAccelerationSetting = new SettingItem
                {
                    Name = "BackgroundRemovalUseGPU",
                    DisplayName = "Enable GPU Acceleration (DirectML)",
                    Type = SettingType.Toggle,
                    Value = Properties.Settings.Default.BackgroundRemovalUseGPU
                };
                var gpuAccelerationControl = CreateSettingControl(gpuAccelerationSetting, "BackgroundRemoval");
                SettingsListPanel.Children.Add(gpuAccelerationControl);

                // Default Virtual Background
                var defaultBackgroundContainer = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x35)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(15)
                };

                var defaultBgGrid = new Grid();
                defaultBgGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                defaultBgGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Label
                var defaultBgLabel = new TextBlock
                {
                    Text = "Default Virtual Background",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid.SetRow(defaultBgLabel, 0);
                defaultBgGrid.Children.Add(defaultBgLabel);

                // Path display and browse button
                var pathPanel = new Grid();
                pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var pathText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(Properties.Settings.Default.DefaultVirtualBackground)
                        ? "No background selected"
                        : System.IO.Path.GetFileName(Properties.Settings.Default.DefaultVirtualBackground),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(pathText, 0);
                pathPanel.Children.Add(pathText);

                var browseBtn = new Button
                {
                    Content = "Browse...",
                    Padding = new Thickness(15, 8, 15, 8),
                    Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };
                browseBtn.Click += (s, e) =>
                {
                    var dialog = new OpenFileDialog
                    {
                        Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                        Title = "Select Virtual Background"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        Properties.Settings.Default.DefaultVirtualBackground = dialog.FileName;
                        Properties.Settings.Default.Save();
                        pathText.Text = System.IO.Path.GetFileName(dialog.FileName);

                        // Also update the VirtualBackgroundService
                        VirtualBackgroundService.Instance.SetSelectedBackground(dialog.FileName);
                    }
                };
                Grid.SetColumn(browseBtn, 1);
                pathPanel.Children.Add(browseBtn);

                Grid.SetRow(pathPanel, 1);
                defaultBgGrid.Children.Add(pathPanel);

                defaultBackgroundContainer.Child = defaultBgGrid;
                SettingsListPanel.Children.Add(defaultBackgroundContainer);

                // Separator
                var separator = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
                    Margin = new Thickness(0, 15, 0, 15)
                };
                SettingsListPanel.Children.Add(separator);

                // Guest Background Selection Section
                var guestSectionHeader = new TextBlock
                {
                    Text = "Guest Background Selection",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                SettingsListPanel.Children.Add(guestSectionHeader);

                // Enable Guest Background Picker
                var enableGuestPickerSetting = new SettingItem
                {
                    Name = "UseGuestBackgroundPicker",
                    DisplayName = "Enable Guest Background Selection",
                    Type = SettingType.Toggle,
                    Value = Properties.Settings.Default.UseGuestBackgroundPicker
                };
                var enableGuestPickerControl = CreateSettingControl(enableGuestPickerSetting, "BackgroundRemoval");
                SettingsListPanel.Children.Add(enableGuestPickerControl);

                // Description text
                var descriptionText = new TextBlock
                {
                    Text = "Allow guests to select their own backgrounds at the start of each photo session",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                    Margin = new Thickness(0, 5, 0, 15)
                };
                SettingsListPanel.Children.Add(descriptionText);

                // Manage Event Backgrounds Button
                var manageButton = new Button
                {
                    Content = "Manage Event Backgrounds",
                    Height = 45,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 0, 0, 10),
                    IsEnabled = Properties.Settings.Default.UseGuestBackgroundPicker
                };

                // Button style
                manageButton.Style = new Style(typeof(Button));
                manageButton.Background = new LinearGradientBrush(
                    Color.FromRgb(0x4C, 0xAF, 0x50),
                    Color.FromRgb(0x45, 0xA0, 0x49),
                    new Point(0, 0),
                    new Point(1, 1));
                manageButton.Foreground = Brushes.White;
                manageButton.BorderThickness = new Thickness(0);

                manageButton.Click += ManageEventBackgroundsButton_Click;

                SettingsListPanel.Children.Add(manageButton);

                // Manage Virtual Backgrounds Button
                var manageVirtualButton = new Button
                {
                    Content = "Manage Virtual Backgrounds",
                    Height = 45,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                // Button style
                manageVirtualButton.Style = new Style(typeof(Button));
                manageVirtualButton.Background = new LinearGradientBrush(
                    Color.FromRgb(0x66, 0x7E, 0xEA),
                    Color.FromRgb(0x76, 0x4B, 0xA2),
                    new Point(0, 0),
                    new Point(1, 1));
                manageVirtualButton.Foreground = Brushes.White;
                manageVirtualButton.BorderThickness = new Thickness(0);

                manageVirtualButton.Click += ManageVirtualBackgroundsButton_Click;

                SettingsListPanel.Children.Add(manageVirtualButton);

                // Update button states when settings change
                var checkboxes = SettingsListPanel.Children.OfType<Border>()
                    .SelectMany(b => (b.Child as Grid)?.Children.OfType<CheckBox>() ?? new CheckBox[0]);

                foreach (var checkbox in checkboxes)
                {
                    if (checkbox.Tag?.ToString()?.Contains("UseGuestBackgroundPicker") == true)
                    {
                        checkbox.Checked += (s, e) => manageButton.IsEnabled = true;
                        checkbox.Unchecked += (s, e) => manageButton.IsEnabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load background removal settings: {ex.Message}");
            }
        }

        private void ManageEventBackgroundsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get current event
                var db = new Photobooth.Database.TemplateDatabase();
                var events = db.GetAllEvents();

                if (events != null && events.Count > 0)
                {
                    var currentEvent = events.OrderByDescending(ev => ev.EventDate).FirstOrDefault();
                    if (currentEvent != null)
                    {
                        // Hide the settings overlay temporarily
                        HideOverlay();

                        // Show the event background manager
                        Photobooth.Controls.EventBackgroundManager.ShowInWindow(currentEvent, Application.Current.MainWindow);

                        // Show the settings overlay again
                        ShowOverlay(bypassPin: true); // Bypass PIN since user already authenticated
                    }
                    else
                    {
                        MessageBox.Show("Please create or select an event first.", "No Event",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessageBox.Show("No events found. Please create an event first.", "No Events",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to open event background manager: {ex.Message}");
                MessageBox.Show("Failed to open background manager. Please try again.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ManageVirtualBackgroundsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Hide the settings overlay temporarily
                HideOverlay();

                // Get current event (may be null for virtual backgrounds management)
                var db = new Photobooth.Database.TemplateDatabase();
                var events = db.GetAllEvents();
                var currentEvent = events?.OrderByDescending(ev => ev.EventDate).FirstOrDefault();

                // Use unified EventBackgroundManager for all background management
                Photobooth.Controls.EventBackgroundManager.ShowInWindow(currentEvent, Application.Current.MainWindow);

                // Show the settings overlay again
                ShowOverlay(bypassPin: true); // Bypass PIN since user already authenticated
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to open background manager: {ex.Message}");
                MessageBox.Show("Failed to open background manager. Please try again.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }

    /// <summary>
    /// View model for category cards
    /// </summary>
    public class CategoryViewModel
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Icon { get; set; }
        public string Summary { get; set; }
        public int SettingsCount { get; set; }
    }
}
