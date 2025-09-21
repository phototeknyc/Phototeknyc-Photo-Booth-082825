using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using Microsoft.Win32;
using Photobooth.Services;

namespace Photobooth.Controls
{
    /// <summary>
    /// Interaction logic for VirtualBackgroundsOverlay.xaml
    /// </summary>
    public partial class VirtualBackgroundsOverlay : UserControl
    {
        #region Private Fields

        private VirtualBackgroundService _backgroundService;
        private BackgroundRemovalService _removalService;
        private ObservableCollection<BackgroundViewModel> _backgrounds;
        private BackgroundViewModel _selectedBackground;
        private string _currentCategory = "Solid";

        #endregion

        #region Events

        public event EventHandler CloseRequested;
        public event EventHandler<VirtualBackgroundSelectedEventArgs> BackgroundSelected;

        #endregion

        #region Constructor

        public VirtualBackgroundsOverlay()
        {
            InitializeComponent();
            _backgrounds = new ObservableCollection<BackgroundViewModel>();
            BackgroundsItemsControl.ItemsSource = _backgrounds;
            Initialize();
        }

        #endregion

        #region Initialization

        private async void Initialize()
        {
            try
            {
                // Get service instances
                _backgroundService = VirtualBackgroundService.Instance;
                _removalService = BackgroundRemovalService.Instance;

                // Load backgrounds
                await _backgroundService.LoadBackgroundsAsync();

                // Load settings
                LoadSettings();

                // Load initial category
                LoadCategory(_currentCategory);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize VirtualBackgroundsOverlay: {ex.Message}");
                MessageBox.Show("Failed to load virtual backgrounds", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSettings()
        {
            // Load from Properties.Settings.Default
            EnableBackgroundRemovalCheckbox.IsChecked = Properties.Settings.Default.EnableBackgroundRemoval;
            EnableLiveViewCheckbox.IsChecked = Properties.Settings.Default.EnableLiveViewBackgroundRemoval;

            // Set quality based on setting
            var quality = Properties.Settings.Default.BackgroundRemovalQuality;
            switch (quality)
            {
                case "Low":
                    QualityComboBox.SelectedIndex = 0;
                    break;
                case "High":
                    QualityComboBox.SelectedIndex = 2;
                    break;
                default:
                    QualityComboBox.SelectedIndex = 1; // Medium
                    break;
            }

            EdgeRefinementSlider.Value = Properties.Settings.Default.BackgroundRemovalEdgeRefinement;
        }

        #endregion

        #region Category Management

        private void CategoryTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                // Get category name and transform it
                var category = radioButton.Content.ToString()
                    .Replace(" ", "")
                    .Replace("Colors", "");

                // Handle plural to singular conversion for Gradients
                if (category == "Gradients")
                {
                    category = "Gradient";
                }

                _currentCategory = category;
                LoadCategory(_currentCategory);
            }
        }

        private void LoadCategory(string category)
        {
            try
            {
                // Ensure _backgrounds is initialized
                if (_backgrounds == null)
                {
                    Log.Error("_backgrounds collection was null, reinitializing");
                    _backgrounds = new ObservableCollection<BackgroundViewModel>();
                    if (BackgroundsItemsControl != null)
                    {
                        BackgroundsItemsControl.ItemsSource = _backgrounds;
                    }
                }

                _backgrounds.Clear();

                // Ensure service is initialized
                if (_backgroundService == null)
                {
                    Log.Error("Background service was null");
                    return;
                }

                var backgroundList = _backgroundService.GetBackgroundsByCategory(category);
                if (backgroundList == null || backgroundList.Count == 0)
                {
                    Log.Debug($"No backgrounds found for category: {category}");
                    NoPreviewText.Text = $"No backgrounds in {category} category";
                    NoPreviewText.Visibility = Visibility.Visible;
                    return;
                }

                foreach (var bg in backgroundList)
                {
                    _backgrounds.Add(new BackgroundViewModel
                    {
                        Id = bg.Id,
                        Name = bg.Name,
                        Category = bg.Category,
                        FilePath = bg.FilePath,
                        ThumbnailPath = bg.ThumbnailPath ?? bg.FilePath,
                        IsDefault = bg.IsDefault
                    });
                }

                // Hide no preview text if we have backgrounds
                if (_backgrounds.Count > 0)
                {
                    NoPreviewText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load category {category}: {ex.Message}");
                // Ensure collection is not null even on error
                if (_backgrounds == null)
                {
                    _backgrounds = new ObservableCollection<BackgroundViewModel>();
                }
            }
        }

        #endregion

        #region Background Selection

        private void BackgroundItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is BackgroundViewModel background)
            {
                SelectBackground(background);
            }
        }

        private void SelectBackground(BackgroundViewModel background)
        {
            // Deselect previous
            if (_selectedBackground != null)
            {
                _selectedBackground.IsSelected = false;
            }

            // Select new
            _selectedBackground = background;
            _selectedBackground.IsSelected = true;

            // Update preview
            UpdatePreview();

            // Enable apply button
            ApplyButton.IsEnabled = true;

            // Show delete button for custom backgrounds
            DeleteButton.Visibility = background.Category == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePreview()
        {
            if (_selectedBackground != null)
            {
                try
                {
                    // Load preview image
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_selectedBackground.ThumbnailPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    PreviewImage.Source = bitmap;
                    NoPreviewText.Visibility = Visibility.Collapsed;

                    // Update info
                    BackgroundNameText.Text = _selectedBackground.Name;
                    BackgroundCategoryText.Text = $"Category: {_selectedBackground.Category}";
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to load preview: {ex.Message}");
                    NoPreviewText.Text = "Failed to load preview";
                    NoPreviewText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                PreviewImage.Source = null;
                NoPreviewText.Visibility = Visibility.Visible;
                BackgroundNameText.Text = "None";
                BackgroundCategoryText.Text = "";
            }
        }

        #endregion

        #region Button Click Handlers

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBackground != null)
            {
                // Save selected background to VirtualBackgroundService
                _backgroundService.SetSelectedBackground(_selectedBackground.FilePath);

                // Save to settings for persistence
                // TODO: Uncomment when Settings.Designer.cs is regenerated
                // Properties.Settings.Default.SelectedVirtualBackground = _selectedBackground.FilePath;
                Properties.Settings.Default.Save();

                // Raise event
                BackgroundSelected?.Invoke(this, new VirtualBackgroundSelectedEventArgs
                {
                    BackgroundPath = _selectedBackground.FilePath,
                    BackgroundName = _selectedBackground.Name,
                    Category = _selectedBackground.Category
                });

                MessageBox.Show($"Background '{_selectedBackground.Name}' applied", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear selected background
            _backgroundService.SetSelectedBackground(null);

            // Clear from settings
            // TODO: Uncomment when Settings.Designer.cs is regenerated
            // Properties.Settings.Default.SelectedVirtualBackground = string.Empty;
            Properties.Settings.Default.Save();

            MessageBox.Show("Background removal enabled without replacement", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Background Image",
                    Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    UploadButton.IsEnabled = false;
                    UploadButton.Content = "Uploading...";

                    // Add custom background with original filename
                    var originalName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                    var success = await _backgroundService.AddCustomBackground(openFileDialog.FileName, originalName);

                    if (success)
                    {
                        // Reload backgrounds to show the new upload
                        await _backgroundService.ReloadCustomBackgroundsAsync();

                        // Switch to custom tab and reload
                        CustomTab.IsChecked = true;
                        LoadCategory("Custom");

                        MessageBox.Show("Background uploaded successfully", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to upload background", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to upload background: {ex.Message}");
                MessageBox.Show($"Upload failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UploadButton.IsEnabled = true;
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                stackPanel.Children.Add(new TextBlock { Text = "📁", FontSize = 18, Margin = new Thickness(0, 0, 8, 0) });
                stackPanel.Children.Add(new TextBlock { Text = "Upload", FontSize = 14 });
                UploadButton.Content = stackPanel;
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBackground != null && _selectedBackground.Category == "Custom")
            {
                var result = MessageBox.Show($"Delete '{_selectedBackground.Name}'?", "Confirm Delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Delete the file
                        if (File.Exists(_selectedBackground.FilePath))
                        {
                            File.Delete(_selectedBackground.FilePath);
                        }

                        // Delete thumbnail
                        if (File.Exists(_selectedBackground.ThumbnailPath))
                        {
                            File.Delete(_selectedBackground.ThumbnailPath);
                        }

                        // Reload category
                        LoadCategory("Custom");

                        // Clear selection
                        _selectedBackground = null;
                        UpdatePreview();
                        ApplyButton.IsEnabled = false;
                        DeleteButton.Visibility = Visibility.Collapsed;

                        MessageBox.Show("Background deleted", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to delete background: {ex.Message}");
                        MessageBox.Show($"Delete failed: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #endregion

        #region Settings Changes

        private void EnableBackgroundRemoval_Changed(object sender, RoutedEventArgs e)
        {
            // Check if controls are initialized
            if (EnableBackgroundRemovalCheckbox == null)
                return;

            var isEnabled = EnableBackgroundRemovalCheckbox.IsChecked == true;

            // Enable/disable other controls with null checks
            if (EnableLiveViewCheckbox != null)
                EnableLiveViewCheckbox.IsEnabled = isEnabled;

            if (QualityComboBox != null)
                QualityComboBox.IsEnabled = isEnabled;

            if (EdgeRefinementSlider != null)
                EdgeRefinementSlider.IsEnabled = isEnabled;

            if (BlurBackgroundCheckbox != null)
                BlurBackgroundCheckbox.IsEnabled = isEnabled;

            if (BackgroundsScrollViewer != null)
                BackgroundsScrollViewer.IsEnabled = isEnabled;

            if (ApplyButton != null)
                ApplyButton.IsEnabled = isEnabled && _selectedBackground != null;

            // Update setting
            Properties.Settings.Default.EnableBackgroundRemoval = isEnabled;
        }

        private void SaveSettings()
        {
            try
            {
                // Save all settings
                Properties.Settings.Default.EnableBackgroundRemoval = EnableBackgroundRemovalCheckbox.IsChecked == true;
                Properties.Settings.Default.EnableLiveViewBackgroundRemoval = EnableLiveViewCheckbox.IsChecked == true;

                // Save quality
                switch (QualityComboBox.SelectedIndex)
                {
                    case 0:
                        Properties.Settings.Default.BackgroundRemovalQuality = "Low";
                        break;
                    case 2:
                        Properties.Settings.Default.BackgroundRemovalQuality = "High";
                        break;
                    default:
                        Properties.Settings.Default.BackgroundRemovalQuality = "Medium";
                        break;
                }

                Properties.Settings.Default.BackgroundRemovalEdgeRefinement = (int)EdgeRefinementSlider.Value;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save settings: {ex.Message}");
            }
        }

        #endregion
    }

    #region View Models

    public class BackgroundViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string FilePath { get; set; }
        public string ThumbnailPath { get; set; }
        public bool IsDefault { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class VirtualBackgroundSelectedEventArgs : EventArgs
    {
        public string BackgroundPath { get; set; }
        public string BackgroundName { get; set; }
        public string Category { get; set; }
    }

    #endregion
}