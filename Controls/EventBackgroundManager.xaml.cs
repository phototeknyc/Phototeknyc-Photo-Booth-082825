using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using CameraControl.Devices;
using Microsoft.Win32;
using Photobooth.Database;
using Photobooth.Models;
using Photobooth.Services;
using Newtonsoft.Json;

namespace Photobooth.Controls
{
    /// <summary>
    /// Unified control for managing virtual backgrounds and event background selections
    /// </summary>
    public partial class EventBackgroundManager : UserControl
    {
        #region Private Fields

        private EventBackgroundService _eventBackgroundService;
        private VirtualBackgroundService _virtualBackgroundService;
        private EventData _currentEvent;
        private ObservableCollection<UnifiedBackgroundViewModel> _allBackgrounds;
        private ObservableCollection<UnifiedBackgroundViewModel> _selectedBackgrounds;
        private ObservableCollection<UnifiedBackgroundViewModel> _customBackgrounds;
        private ObservableCollection<UnifiedBackgroundViewModel> _filteredBackgrounds;
        private string _searchText = string.Empty;
        private string _currentCategory = "All";
        private PhotoPlacementData _photoPlacementData;
        private string _selectedBackgroundPath;

        #endregion

        #region Events

        public event EventHandler SelectionSaved;
        public event EventHandler Closed;

        #endregion

        #region Constructor

        public EventBackgroundManager()
        {
            InitializeComponent();
            _allBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();
            _selectedBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();
            _customBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();
            _filteredBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();

            // Handle Loaded event to ensure proper initialization
            this.Loaded += EventBackgroundManager_Loaded;

            Initialize();
        }

        private void EventBackgroundManager_Loaded(object sender, RoutedEventArgs e)
        {
            // Set initial view after everything is loaded
            if (AllTab?.IsChecked == true)
            {
                ShowView("All");
            }
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            _eventBackgroundService = EventBackgroundService.Instance;
            _virtualBackgroundService = VirtualBackgroundService.Instance;

            AllBackgroundsList.ItemsSource = _filteredBackgrounds;
            CategoryBackgroundsList.ItemsSource = _filteredBackgrounds;
            SelectedBackgroundsList.ItemsSource = _selectedBackgrounds;
            CustomBackgroundsList.ItemsSource = _customBackgrounds;
        }

        /// <summary>
        /// Load backgrounds for event management and show the overlay
        /// </summary>
        public async Task LoadForEventAsync(EventData eventData)
        {
            // Show the overlay
            Show();

            _currentEvent = eventData;

            if (eventData != null)
            {
                EventNameText.Text = $"Managing backgrounds for: {eventData.Name}";
            }

            try
            {
                ShowLoading(true);

                // Load all available backgrounds
                await _virtualBackgroundService.LoadBackgroundsAsync();

                // Load current event selections
                if (_currentEvent != null)
                {
                    await _eventBackgroundService.LoadEventBackgroundsAsync(_currentEvent);

                    // Restore previously saved selections from database
                    if (!string.IsNullOrEmpty(_currentEvent.BackgroundSettings))
                    {
                        try
                        {
                            var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(_currentEvent.BackgroundSettings);
                            System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Loaded background settings for event: {_currentEvent.Name}");
                            System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Selected background: {_currentEvent.SelectedBackgroundPath}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Failed to parse background settings: {ex.Message}");
                        }
                    }

                    // Load photo placement data if available
                    if (!string.IsNullOrEmpty(_currentEvent.PhotoPlacementData))
                    {
                        try
                        {
                            _photoPlacementData = PhotoPlacementData.FromJson(_currentEvent.PhotoPlacementData);
                            System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Loaded photo placement data for event: {_currentEvent.Name}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Failed to parse photo placement data: {ex.Message}");
                        }
                    }
                    else if (!string.IsNullOrEmpty(Properties.Settings.Default.PhotoPlacementData))
                    {
                        // Fallback to settings if not in event
                        try
                        {
                            _photoPlacementData = PhotoPlacementData.FromJson(Properties.Settings.Default.PhotoPlacementData);
                            System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Loaded photo placement data from settings");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Failed to parse settings photo placement data: {ex.Message}");
                        }
                    }

                    // Set selected background path if available
                    if (!string.IsNullOrEmpty(_currentEvent.SelectedBackgroundPath))
                    {
                        _selectedBackgroundPath = _currentEvent.SelectedBackgroundPath;
                        System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Set selected background path: {_selectedBackgroundPath}");
                    }
                    else if (!string.IsNullOrEmpty(Properties.Settings.Default.SelectedVirtualBackground))
                    {
                        // Fallback to settings if not in event
                        _selectedBackgroundPath = Properties.Settings.Default.SelectedVirtualBackground;
                        System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Set selected background path from settings: {_selectedBackgroundPath}");
                    }
                }

                // Populate UI
                await PopulateBackgroundsAsync();

                UpdateSelectionCount();
                UpdateEmptyState();

                // Load the selected background into the positioner if available
                if (!string.IsNullOrEmpty(_selectedBackgroundPath) && PhotoPositioner != null)
                {
                    PhotoPositioner.SetBackground(_selectedBackgroundPath);

                    // Also load placement data if available
                    if (_photoPlacementData != null)
                    {
                        PhotoPositioner.SetPlacementData(_photoPlacementData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load backgrounds for event: {ex.Message}");
                MessageBox.Show("Failed to load backgrounds. Please try again.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async Task PopulateBackgroundsAsync()
        {
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    _allBackgrounds.Clear();
                    _selectedBackgrounds.Clear();
                    _customBackgrounds.Clear();

                    // Get all backgrounds from virtual background service
                    var allBgs = _virtualBackgroundService.GetAllBackgrounds();

                    // Get currently selected background IDs for the event
                    var selectedIds = _eventBackgroundService.EventBackgrounds
                        .Select(eb => eb.BackgroundId)
                        .ToList();

                    foreach (var bg in allBgs)
                    {
                        var viewModel = new UnifiedBackgroundViewModel
                        {
                            Id = bg.Id,
                            Name = bg.Name,
                            Category = bg.Category,
                            ThumbnailPath = bg.ThumbnailPath,
                            BackgroundPath = bg.FilePath,
                            IsSelected = selectedIds.Contains(bg.Id),
                            IsCustom = bg.Category == "Custom"
                        };

                        _allBackgrounds.Add(viewModel);

                        if (viewModel.IsSelected)
                        {
                            _selectedBackgrounds.Add(viewModel);
                        }

                        if (viewModel.IsCustom)
                        {
                            _customBackgrounds.Add(viewModel);
                        }
                    }

                    // Initially show all backgrounds
                    FilterBackgrounds();
                    PopulateCategories();
                });
            });
        }

        private void PopulateCategories()
        {
            CategoryList.Children.Clear();

            var categories = _allBackgrounds
                .Where(b => !b.IsCustom)
                .Select(b => b.Category)
                .Distinct()
                .OrderBy(c => c);

            // Add "All" option
            var allButton = CreateCategoryButton("All", true);
            CategoryList.Children.Add(allButton);

            foreach (var category in categories)
            {
                var button = CreateCategoryButton(category, false);
                CategoryList.Children.Add(button);
            }
        }

        private RadioButton CreateCategoryButton(string category, bool isChecked)
        {
            var button = new RadioButton
            {
                Content = category,
                GroupName = "Categories",
                IsChecked = isChecked,
                Style = FindResource("TabButtonStyle") as Style,
                Tag = category,
                Margin = new Thickness(0, 2, 0, 2)
            };

            button.Checked += CategoryButton_Checked;
            return button;
        }

        #endregion

        #region Tab Navigation

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            // Prevent firing during initialization
            if (!IsLoaded)
                return;

            if (AllTab?.IsChecked == true)
            {
                ShowView("All");
            }
            else if (CategoriesTab?.IsChecked == true)
            {
                ShowView("Categories");
            }
            else if (EventTab?.IsChecked == true)
            {
                ShowView("Event");
            }
            else if (CustomTab?.IsChecked == true)
            {
                ShowView("Custom");
            }
        }

        private void ShowView(string viewName)
        {
            // Check if UI elements are initialized
            if (AllBackgroundsView == null || CategoriesView == null ||
                EventSelectionView == null || CustomUploadView == null)
            {
                return;
            }

            AllBackgroundsView.Visibility = Visibility.Collapsed;
            CategoriesView.Visibility = Visibility.Collapsed;
            EventSelectionView.Visibility = Visibility.Collapsed;
            CustomUploadView.Visibility = Visibility.Collapsed;

            switch (viewName)
            {
                case "All":
                    AllBackgroundsView.Visibility = Visibility.Visible;
                    break;
                case "Categories":
                    CategoriesView.Visibility = Visibility.Visible;
                    break;
                case "Event":
                    EventSelectionView.Visibility = Visibility.Visible;
                    UpdateEmptyState();
                    break;
                case "Custom":
                    CustomUploadView.Visibility = Visibility.Visible;
                    break;
            }
        }

        #endregion

        #region Background Selection

        private void BackgroundItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string backgroundId)
            {
                var background = _allBackgrounds.FirstOrDefault(b => b.Id == backgroundId);
                if (background != null)
                {
                    // Toggle selection and auto-save immediately
                    ToggleBackgroundSelection(background);

                    // Also save to settings immediately for instant persistence
                    SaveSelectionsToSettingsImmediately();
                }
            }
        }

        private async void RemoveBackground_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string backgroundId)
            {
                var background = _selectedBackgrounds.FirstOrDefault(b => b.Id == backgroundId);
                if (background != null)
                {
                    background.IsSelected = false;
                    _selectedBackgrounds.Remove(background);
                    UpdateSelectionCount();
                    UpdateEmptyState();

                    // Auto-save immediately when background is removed
                    SaveSelectionsToSettingsImmediately();
                    await AutoSaveEventSettings();
                }
                e.Handled = true; // Prevent event bubbling to BackgroundItem_Click
            }
        }

        private async void ToggleBackgroundSelection(UnifiedBackgroundViewModel background)
        {
            // Save current position for previous background before switching
            if (!string.IsNullOrEmpty(_selectedBackgroundPath) && PhotoPositioner != null)
            {
                var currentPlacement = PhotoPositioner.GetPlacementData();
                if (currentPlacement != null)
                {
                    // Save position for the current background
                    await _eventBackgroundService.SavePhotoPlacementForBackground(_selectedBackgroundPath, currentPlacement);
                    _photoPlacementData = currentPlacement;
                }
            }

            background.IsSelected = !background.IsSelected;

            // Immediately save to settings for instant persistence
            if (background.IsSelected)
            {
                Properties.Settings.Default.SelectedVirtualBackground = background.BackgroundPath;
                Properties.Settings.Default.Save();
            }

            if (background.IsSelected)
            {
                if (!_selectedBackgrounds.Contains(background))
                {
                    _selectedBackgrounds.Add(background);
                }

                // Update the live preview with the selected background
                _selectedBackgroundPath = background.BackgroundPath;
                if (PhotoPositioner != null)
                {
                    PhotoPositioner.SetBackground(_selectedBackgroundPath);

                    // Load saved placement data for this specific background
                    var savedPlacement = _eventBackgroundService.GetPhotoPlacementForBackground(_selectedBackgroundPath);
                    if (savedPlacement != null)
                    {
                        PhotoPositioner.SetPlacementData(savedPlacement);
                        _photoPlacementData = savedPlacement;
                    }
                    else
                    {
                        // Create default placement if none saved
                        _photoPlacementData = new Models.PhotoPlacementData
                        {
                            PlacementZones = new System.Collections.Generic.List<Models.PhotoPlacementZone>
                            {
                                new Models.PhotoPlacementZone
                                {
                                    PhotoIndex = 0,
                                    X = 0.1,
                                    Y = 0.1,
                                    Width = 0.8,
                                    Height = 0.8,
                                    Rotation = 0,
                                    IsEnabled = true
                                }
                            }
                        };
                        PhotoPositioner.SetPlacementData(_photoPlacementData);

                        // Save the default placement for this background
                        await _eventBackgroundService.SavePhotoPlacementForBackground(_selectedBackgroundPath, _photoPlacementData);
                    }
                }
            }
            else
            {
                _selectedBackgrounds.Remove(background);

                // Save the current position for the background being deselected
                if (background.BackgroundPath == _selectedBackgroundPath && PhotoPositioner != null)
                {
                    var currentPlacement = PhotoPositioner.GetPlacementData();
                    if (currentPlacement != null)
                    {
                        await _eventBackgroundService.SavePhotoPlacementForBackground(background.BackgroundPath, currentPlacement);
                    }
                }

                // Clear preview if no backgrounds selected
                if (!_selectedBackgrounds.Any())
                {
                    _selectedBackgroundPath = null;
                    if (PhotoPositioner != null)
                    {
                        PhotoPositioner.SetBackground("");
                        // Reset to default positioning when no background selected
                        PhotoPositioner.SetPlacementData(new Models.PhotoPlacementData
                        {
                            PlacementZones = new System.Collections.Generic.List<Models.PhotoPlacementZone>
                            {
                                new Models.PhotoPlacementZone
                                {
                                    PhotoIndex = 0,
                                    X = 0.1,
                                    Y = 0.1,
                                    Width = 0.8,
                                    Height = 0.8,
                                    Rotation = 0,
                                    IsEnabled = true
                                }
                            }
                        });
                    }
                }
                else if (_selectedBackgrounds.Any())
                {
                    // If there are still selected backgrounds, switch to the first one
                    var firstBackground = _selectedBackgrounds.First();
                    _selectedBackgroundPath = firstBackground.BackgroundPath;
                    if (PhotoPositioner != null)
                    {
                        PhotoPositioner.SetBackground(_selectedBackgroundPath);
                        var savedPlacement = _eventBackgroundService.GetPhotoPlacementForBackground(_selectedBackgroundPath);
                        if (savedPlacement != null)
                        {
                            PhotoPositioner.SetPlacementData(savedPlacement);
                        }
                    }
                }
            }

            UpdateSelectionCount();
            UpdateEmptyState();

            // Auto-save the changes
            await AutoSaveEventSettings();
        }

        private void CategoryButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton button && button.Tag is string category)
            {
                _currentCategory = category;
                FilterBackgrounds();
            }
        }

        #endregion

        #region Search and Filter

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text?.ToLower() ?? string.Empty;
            FilterBackgrounds();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        private void FilterBackgrounds()
        {
            _filteredBackgrounds.Clear();

            var filtered = _allBackgrounds.AsEnumerable();

            // Apply category filter
            if (_currentCategory != "All" && !string.IsNullOrEmpty(_currentCategory))
            {
                filtered = filtered.Where(b => b.Category == _currentCategory);
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filtered = filtered.Where(b =>
                    b.Name?.ToLower().Contains(_searchText) == true ||
                    b.Category?.ToLower().Contains(_searchText) == true);
            }

            foreach (var bg in filtered)
            {
                _filteredBackgrounds.Add(bg);
            }
        }

        #endregion

        #region Custom Backgrounds

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Background Images",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ShowLoading(true);

                try
                {
                    foreach (var filePath in openFileDialog.FileNames)
                    {
                        await _virtualBackgroundService.AddCustomBackground(filePath);
                    }

                    // Reload backgrounds to show new uploads
                    await _virtualBackgroundService.ReloadCustomBackgroundsAsync();
                    await PopulateBackgroundsAsync();

                    MessageBox.Show($"Successfully uploaded {openFileDialog.FileNames.Length} background(s)",
                        "Upload Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to upload backgrounds: {ex.Message}");
                    MessageBox.Show($"Failed to upload backgrounds: {ex.Message}",
                        "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ShowLoading(false);
                }
            }
        }

        private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedCustom = _customBackgrounds.Where(b => b.IsSelected).ToList();

            if (!selectedCustom.Any())
            {
                MessageBox.Show("Please select custom backgrounds to delete.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedCustom.Count} custom background(s)?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ShowLoading(true);

                try
                {
                    foreach (var bg in selectedCustom)
                    {
                        // Delete file if it exists
                        if (File.Exists(bg.BackgroundPath))
                        {
                            File.Delete(bg.BackgroundPath);
                        }

                        // Delete thumbnail if it exists
                        if (!string.IsNullOrEmpty(bg.ThumbnailPath) && File.Exists(bg.ThumbnailPath))
                        {
                            File.Delete(bg.ThumbnailPath);
                        }

                        // Remove from collections
                        _allBackgrounds.Remove(bg);
                        _customBackgrounds.Remove(bg);
                        _selectedBackgrounds.Remove(bg);
                    }

                    FilterBackgrounds();
                    UpdateSelectionCount();

                    MessageBox.Show($"Deleted {selectedCustom.Count} custom background(s)",
                        "Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to delete backgrounds: {ex.Message}");
                    MessageBox.Show($"Failed to delete backgrounds: {ex.Message}",
                        "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ShowLoading(false);
                }
            }
        }

        #endregion

        #region Quick Actions

        private void SelectPopular_Click(object sender, RoutedEventArgs e)
        {
            // Clear current selection
            foreach (var bg in _selectedBackgrounds.ToList())
            {
                bg.IsSelected = false;
            }
            _selectedBackgrounds.Clear();

            // Select popular backgrounds (first 3 solids, 2 gradients)
            var popularBackgrounds = _allBackgrounds
                .Where(b => b.Category == "Solid")
                .Take(3)
                .Concat(_allBackgrounds
                    .Where(b => b.Category == "Gradient")
                    .Take(2));

            foreach (var bg in popularBackgrounds)
            {
                bg.IsSelected = true;
                _selectedBackgrounds.Add(bg);
            }

            UpdateSelectionCount();
            UpdateEmptyState();
            FilterBackgrounds();

            // Switch to event tab to show selection
            EventTab.IsChecked = true;
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all selected backgrounds?",
                "Clear Selection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var bg in _selectedBackgrounds.ToList())
                {
                    bg.IsSelected = false;
                }
                _selectedBackgrounds.Clear();

                UpdateSelectionCount();
                UpdateEmptyState();
                FilterBackgrounds();
            }
        }

        #endregion

        #region Save and Close

        // SaveButton_Click removed - now uses auto-save on selection

        /// <summary>
        /// Immediately save selections to settings for instant persistence
        /// </summary>
        private void SaveSelectionsToSettingsImmediately()
        {
            try
            {
                // Save selected background IDs
                var selectedIds = _selectedBackgrounds.Select(b => b.Id).ToList();
                Properties.Settings.Default.EventBackgroundIds = string.Join(",", selectedIds);

                // Save first selected background as the default
                if (_selectedBackgrounds.Any())
                {
                    var firstBackground = _selectedBackgrounds.First();
                    Properties.Settings.Default.SelectedVirtualBackground = firstBackground.BackgroundPath;
                    Properties.Settings.Default.EnableBackgroundRemoval = true;
                }
                else
                {
                    // Clear selection if no backgrounds selected
                    Properties.Settings.Default.SelectedVirtualBackground = string.Empty;
                }

                // Save photo placement data if available
                if (_photoPlacementData != null)
                {
                    Properties.Settings.Default.PhotoPlacementData = _photoPlacementData.ToJson();
                }

                // Save immediately
                Properties.Settings.Default.Save();

                System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Selections saved immediately to settings");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save selections immediately: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close overlay when clicking on backdrop
            if (e.OriginalSource == sender)
            {
                Hide();
            }
        }

        /// <summary>
        /// Show the overlay
        /// </summary>
        public void Show()
        {
            this.Visibility = Visibility.Visible;

            // Animate in
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        /// <summary>
        /// Hide the overlay
        /// </summary>
        public void Hide()
        {
            // Animate out
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            fadeOut.Completed += (s, e) =>
            {
                this.Visibility = Visibility.Collapsed;
                Closed?.Invoke(this, EventArgs.Empty);
            };
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void EnableGuestPicker_Changed(object sender, RoutedEventArgs e)
        {
            // Setting is automatically saved through binding
            Properties.Settings.Default.Save();
        }

        private async void PhotoPositioner_PositionChanged(object sender, PhotoPlacementData e)
        {
            // Auto-save positioning data for the current background
            if (!string.IsNullOrEmpty(_selectedBackgroundPath) && e != null)
            {
                // Save position for the specific current background (not globally)
                await _eventBackgroundService.SavePhotoPlacementForBackground(_selectedBackgroundPath, e);

                // Auto-save to database immediately
                await AutoSaveEventSettings();

                System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Photo position updated and auto-saved for background: {_selectedBackgroundPath}");
            }
        }

        /// <summary>
        /// Auto-save all event settings including backgrounds and positioning
        /// </summary>
        private async Task AutoSaveEventSettings()
        {
            if (_currentEvent == null) return;

            try
            {
                // Update event with current settings
                if (_selectedBackgrounds.Any())
                {
                    var firstBackground = _selectedBackgrounds.First();
                    _currentEvent.SelectedBackgroundPath = firstBackground.BackgroundPath;
                    _currentEvent.SelectedBackgroundType = firstBackground.IsCustom ? "Custom" : firstBackground.Category;

                    // Save selected background IDs
                    var selectedIds = _selectedBackgrounds.Select(b => b.Id).ToList();
                    _currentEvent.BackgroundSettings = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        SelectedCount = selectedIds.Count,
                        AllIds = selectedIds,
                        DefaultId = firstBackground.Id,
                        DefaultBackgroundPath = _selectedBackgroundPath,
                        LastUpdated = DateTime.Now
                    });

                    // Also save to EventBackgroundService
                    await _eventBackgroundService.SaveEventBackgroundsAsync(_currentEvent, selectedIds);
                    Log.Debug($"Saved {selectedIds.Count} backgrounds to event service");
                }

                // Save photo placement data
                if (_photoPlacementData != null)
                {
                    _currentEvent.PhotoPlacementData = _photoPlacementData.ToJson();
                }

                // Update in database
                var templateDb = new Database.TemplateDatabase();
                templateDb.UpdateEvent(_currentEvent.Id, _currentEvent);

                // Also save to settings for quick access
                Properties.Settings.Default.EventBackgroundIds = string.Join(",", _selectedBackgrounds.Select(b => b.Id));
                if (!string.IsNullOrEmpty(_selectedBackgroundPath))
                {
                    Properties.Settings.Default.SelectedVirtualBackground = _selectedBackgroundPath;
                }
                Properties.Settings.Default.Save();

                System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Auto-saved event settings for: {_currentEvent.Name}");

                // Fire the SelectionSaved event for any listeners
                SelectionSaved?.Invoke(this, EventArgs.Empty);

                // Show brief save feedback
                ShowSaveConfirmation();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to auto-save event settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Show brief visual confirmation that settings were saved
        /// </summary>
        private void ShowSaveConfirmation()
        {
            // Update selection count to show save happened
            if (SelectionCountText != null)
            {
                var originalText = SelectionCountText.Text;
                SelectionCountText.Text = "✓ Saved";
                SelectionCountText.Foreground = new SolidColorBrush(Colors.Green);

                // Restore original text after brief delay
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    SelectionCountText.Text = originalText;
                    SelectionCountText.Foreground = new SolidColorBrush(Colors.White);
                };
                timer.Start();
            }
        }

        // Removed old overlay methods - using integrated positioner now

        #endregion

        #region UI Updates

        private void UpdateSelectionCount()
        {
            if (SelectionCountText != null)
                SelectionCountText.Text = _selectedBackgrounds.Count.ToString();
        }

        private void UpdateEmptyState()
        {
            if (EmptyStateText != null)
                EmptyStateText.Visibility = _selectedBackgrounds.Any() ?
                    Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowLoading(bool show)
        {
            if (LoadingOverlay != null)
                LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Show the manager as an overlay in the parent window
        /// </summary>
        public static async void ShowInWindow(EventData eventData, Window owner = null)
        {
            if (owner == null)
                owner = Application.Current.MainWindow;

            // Find or create the manager overlay
            EventBackgroundManager manager = null;

            // Look for existing overlay in the window
            if (owner.Content is Grid rootGrid)
            {
                foreach (var child in rootGrid.Children)
                {
                    if (child is EventBackgroundManager existing)
                    {
                        manager = existing;
                        break;
                    }
                }
            }

            // If not found, create and add to window
            if (manager == null)
            {
                manager = new EventBackgroundManager();

                // Add to the window's root grid
                if (owner.Content is Grid grid)
                {
                    Grid.SetRowSpan(manager, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(manager, Math.Max(1, grid.ColumnDefinitions.Count));
                    Panel.SetZIndex(manager, 9999); // Ensure it's on top
                    grid.Children.Add(manager);
                }
                else if (owner.Content is Panel panel)
                {
                    Panel.SetZIndex(manager, 9999);
                    panel.Children.Add(manager);
                }
                else
                {
                    // Wrap existing content in a Grid
                    var originalContent = owner.Content;
                    var newGrid = new Grid();
                    owner.Content = newGrid;
                    newGrid.Children.Add(originalContent as UIElement);
                    Panel.SetZIndex(manager, 9999);
                    newGrid.Children.Add(manager);
                }
            }

            // Load the event data and show
            await manager.LoadForEventAsync(eventData);
        }

        #endregion
    }

    #region View Models

    public class UnifiedBackgroundViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string ThumbnailPath { get; set; }
        public string BackgroundPath { get; set; }
        public bool IsCustom { get; set; }

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

    #endregion
}