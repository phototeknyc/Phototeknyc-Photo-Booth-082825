using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Photobooth.Controls.ModularComponents;
using Photobooth.Services;
using CameraControl.Devices;

namespace Photobooth.Controls
{
    /// <summary>
    /// Interaction logic for BackgroundSelectionControl.xaml
    /// </summary>
    public partial class BackgroundSelectionControl : UserControl
    {
        #region Private Fields

        private BackgroundRemovalModule _module;
        private VirtualBackgroundService _backgroundService;
        private string _selectedBackgroundPath;
        private string _currentCategory = "Solid";
        private Dictionary<string, Border> _backgroundThumbnails;

        #endregion

        #region Events

        public event EventHandler<BackgroundSelectedEventArgs> BackgroundSelected;
        public event EventHandler Closed;

        #endregion

        #region Constructor

        public BackgroundSelectionControl(BackgroundRemovalModule module = null)
        {
            InitializeComponent();
            _module = module;
            _backgroundService = VirtualBackgroundService.Instance;
            _backgroundThumbnails = new Dictionary<string, Border>();

            LoadBackgrounds();
            LoadSettings();
        }

        #endregion

        #region Initialization

        private async void LoadBackgrounds()
        {
            try
            {
                ShowLoading(true);

                await _backgroundService.LoadBackgroundsAsync();

                // Create category tabs
                CreateCategoryTabs();

                // Load initial category
                LoadCategory(_currentCategory);

                ShowLoading(false);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load backgrounds: {ex.Message}");
                ShowLoading(false);
                MessageBox.Show("Failed to load virtual backgrounds", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSettings()
        {
            try
            {
                // Temporarily unhook events to prevent them firing during initialization
                EnableBackgroundRemovalCheckbox.Checked -= EnableBackgroundRemoval_Changed;
                EnableBackgroundRemovalCheckbox.Unchecked -= EnableBackgroundRemoval_Changed;
                LivePreviewCheckbox.Checked -= LivePreview_Changed;
                LivePreviewCheckbox.Unchecked -= LivePreview_Changed;

                // Load enable/disable setting
                EnableBackgroundRemovalCheckbox.IsChecked = Properties.Settings.Default.EnableBackgroundRemoval;

                // Load live preview setting
                LivePreviewCheckbox.IsChecked = Properties.Settings.Default.EnableLiveViewBackgroundRemoval;

                // Re-hook events
                EnableBackgroundRemovalCheckbox.Checked += EnableBackgroundRemoval_Changed;
                EnableBackgroundRemovalCheckbox.Unchecked += EnableBackgroundRemoval_Changed;
                LivePreviewCheckbox.Checked += LivePreview_Changed;
                LivePreviewCheckbox.Unchecked += LivePreview_Changed;

                // Manually trigger the state update
                EnableBackgroundRemoval_Changed(null, null);
            }
            catch (Exception ex)
            {
                Log.Debug($"Error loading background removal settings: {ex.Message}");
            }
        }

        private void CreateCategoryTabs()
        {
            CategoryTabsPanel.Children.Clear();

            var categories = _backgroundService.GetCategories();
            bool isFirst = true;

            foreach (var category in categories)
            {
                var tab = new RadioButton
                {
                    Content = category,
                    Style = FindResource("CategoryTabStyle") as Style,
                    GroupName = "CategoryTabs",
                    IsChecked = isFirst
                };

                tab.Checked += (s, e) => LoadCategory(category);

                CategoryTabsPanel.Children.Add(tab);
                isFirst = false;
            }
        }

        #endregion

        #region Background Loading

        private void LoadCategory(string category)
        {
            _currentCategory = category;
            BackgroundsPanel.Children.Clear();
            _backgroundThumbnails.Clear();

            var backgrounds = _backgroundService.GetBackgroundsByCategory(category);

            foreach (var background in backgrounds)
            {
                var thumbnail = CreateBackgroundThumbnail(background);
                BackgroundsPanel.Children.Add(thumbnail);
                _backgroundThumbnails[background.FilePath] = thumbnail;
            }

            // Select first background if none selected
            if (string.IsNullOrEmpty(_selectedBackgroundPath) && backgrounds.Any())
            {
                SelectBackground(backgrounds.First().FilePath);
            }
        }

        private Border CreateBackgroundThumbnail(VirtualBackground background)
        {
            var border = new Border
            {
                Style = FindResource("BackgroundThumbnailStyle") as Style,
                Tag = background.FilePath
            };

            var grid = new Grid();

            // Background image
            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = LoadThumbnailImage(background.ThumbnailPath)
            };

            grid.Children.Add(image);

            // Name overlay
            var nameOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 25
            };

            var nameText = new TextBlock
            {
                Text = background.Name,
                Foreground = Brushes.White,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            nameOverlay.Child = nameText;
            grid.Children.Add(nameOverlay);

            // Selection indicator
            var selectionBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                Visibility = Visibility.Collapsed,
                Name = "SelectionBorder"
            };
            grid.Children.Add(selectionBorder);

            border.Child = grid;

            // Click handler
            border.MouseLeftButtonUp += (s, e) =>
            {
                SelectBackground(background.FilePath);
            };

            return border;
        }

        private BitmapImage LoadThumbnailImage(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 200;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Debug($"Failed to load thumbnail: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Background Selection

        private void SelectBackground(string backgroundPath)
        {
            _selectedBackgroundPath = backgroundPath;

            // Update UI selection
            foreach (var thumbnail in _backgroundThumbnails.Values)
            {
                var grid = thumbnail.Child as Grid;
                var selectionBorder = grid?.Children.OfType<Border>()
                    .FirstOrDefault(b => b.Name == "SelectionBorder");

                if (selectionBorder != null)
                {
                    selectionBorder.Visibility = thumbnail.Tag.ToString() == backgroundPath
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }

            // Apply to module if available
            if (_module != null && EnableBackgroundRemovalCheckbox.IsChecked == true)
            {
                _module.SetVirtualBackground(backgroundPath);

                // Enable live preview if checked
                if (LivePreviewCheckbox.IsChecked == true)
                {
                    _module.EnableLiveViewRemoval(true);
                }
            }

            // Fire event
            BackgroundSelected?.Invoke(this, new BackgroundSelectedEventArgs
            {
                BackgroundPath = backgroundPath
            });

            Log.Debug($"Background selected: {Path.GetFileName(backgroundPath)}");
        }

        #endregion

        #region Event Handlers

        private void EnableBackgroundRemoval_Changed(object sender, RoutedEventArgs e)
        {
            // Check if controls are initialized
            if (EnableBackgroundRemovalCheckbox == null)
                return;

            var isEnabled = EnableBackgroundRemovalCheckbox.IsChecked == true;

            // Save setting
            Properties.Settings.Default.EnableBackgroundRemoval = isEnabled;
            Properties.Settings.Default.Save();

            // Update module
            if (_module != null && LivePreviewCheckbox != null)
            {
                _module.EnableLiveViewRemoval(isEnabled && LivePreviewCheckbox.IsChecked == true);
            }

            // Enable/disable other controls (with null checks)
            if (BackgroundsPanel != null)
                BackgroundsPanel.IsEnabled = isEnabled;

            if (LivePreviewCheckbox != null)
                LivePreviewCheckbox.IsEnabled = isEnabled;

            if (ApplyButton != null)
                ApplyButton.IsEnabled = isEnabled;

            if (UploadButton != null)
                UploadButton.IsEnabled = isEnabled;

            Log.Debug($"Background removal {(isEnabled ? "enabled" : "disabled")}");
        }

        private void LivePreview_Changed(object sender, RoutedEventArgs e)
        {
            var isEnabled = LivePreviewCheckbox.IsChecked == true;

            // Save setting
            Properties.Settings.Default.EnableLiveViewBackgroundRemoval = isEnabled;
            Properties.Settings.Default.Save();

            // Update module
            if (_module != null && EnableBackgroundRemovalCheckbox.IsChecked == true)
            {
                _module.EnableLiveViewRemoval(isEnabled);
            }

            Log.Debug($"Live preview {(isEnabled ? "enabled" : "disabled")}");
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ShowLoading(true);

                    var success = await _backgroundService.AddCustomBackground(dialog.FileName);

                    if (success)
                    {
                        // Reload custom category
                        LoadCategory("Custom");

                        // Select the new background
                        var customBackgrounds = _backgroundService.GetBackgroundsByCategory("Custom");
                        var newBackground = customBackgrounds.LastOrDefault();
                        if (newBackground != null)
                        {
                            SelectBackground(newBackground.FilePath);
                        }
                    }

                    ShowLoading(false);
                }
                catch (Exception ex)
                {
                    ShowLoading(false);
                    Log.Error($"Failed to upload custom background: {ex.Message}");
                    MessageBox.Show("Failed to upload background image", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedBackgroundPath))
            {
                MessageBox.Show("Please select a background first", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Apply and close
            if (_module != null)
            {
                _module.SetVirtualBackground(_selectedBackgroundPath);
            }

            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Helper Methods

        private void ShowLoading(bool show)
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion
    }

    #region Event Args

    public class BackgroundSelectedEventArgs : EventArgs
    {
        public string BackgroundPath { get; set; }
    }

    #endregion
}