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
using System.Windows.Threading;

namespace Photobooth.Controls
{
    /// <summary>
    /// Event-centric background manager with auto-save functionality
    /// All changes are automatically saved to the current event
    /// </summary>
    public partial class EventBackgroundManager : UserControl
    {
        #region Private Fields

        private EventBackgroundService _eventBackgroundService;
        private VirtualBackgroundService _virtualBackgroundService;
        private EventService _eventService;
        private EventData _currentEvent;
        private ObservableCollection<UnifiedBackgroundViewModel> _allBackgrounds;
        private ObservableCollection<UnifiedBackgroundViewModel> _selectedBackgrounds;
        private ObservableCollection<UnifiedBackgroundViewModel> _customBackgrounds;
        private ObservableCollection<UnifiedBackgroundViewModel> _filteredBackgrounds;
        private ObservableCollection<EventData> _events;
        private string _searchText = string.Empty;
        private string _currentCategory = "All";
        private PhotoPlacementData _currentPlacementData;
        private DispatcherTimer _autoSaveTimer;
        private bool _isLoadingEvent = false;

        #endregion

        #region Events

        public event EventHandler SelectionSaved;
        public event EventHandler Closed;
        public event EventHandler EventChanged;

        #endregion

        #region Constructor

        public EventBackgroundManager()
        {
            // Initialize collections BEFORE InitializeComponent to avoid null references
            _allBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();
            _selectedBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();
            _customBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();
            _filteredBackgrounds = new ObservableCollection<UnifiedBackgroundViewModel>();
            _events = new ObservableCollection<EventData>();

            InitializeComponent();

            // Handle Loaded event to ensure proper initialization
            this.Loaded += EventBackgroundManager_Loaded;

            Initialize();
        }

        private void EventBackgroundManager_Loaded(object sender, RoutedEventArgs e)
        {
            // Load events for dropdown
            LoadEvents();

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
            _eventService = new EventService();

            // Subscribe to service events
            _eventBackgroundService.BackgroundsChanged += OnBackgroundsChanged;
            _eventBackgroundService.SettingsChanged += OnSettingsChanged;
            _eventBackgroundService.BackgroundSelected += OnBackgroundSelected;

            // Initialize auto-save timer (saves placement data after 500ms of inactivity)
            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

            // Set ItemsSource (with null checks)
            if (AllBackgroundsList != null) AllBackgroundsList.ItemsSource = _filteredBackgrounds;
            if (CategoryBackgroundsList != null) CategoryBackgroundsList.ItemsSource = _filteredBackgrounds;
            if (SelectedBackgroundsList != null) SelectedBackgroundsList.ItemsSource = _selectedBackgrounds;
            if (CustomBackgroundsList != null) CustomBackgroundsList.ItemsSource = _customBackgrounds;

            // Add event selection UI if not present
            AddEventSelectionUI();
        }

        private void AddEventSelectionUI()
        {
            // This will be added in XAML, but we can set up the binding here
            if (EventComboBox != null)
            {
                EventComboBox.ItemsSource = _events;
                EventComboBox.DisplayMemberPath = "Name";
                EventComboBox.SelectionChanged -= EventComboBox_SelectionChanged; // Remove first to avoid duplicate
                EventComboBox.SelectionChanged += EventComboBox_SelectionChanged;
            }
        }

        private async void LoadEvents()
        {
            try
            {
                var events = _eventService.GetAllEvents();
                _events.Clear();
                foreach (var evt in events)
                {
                    _events.Add(evt);
                }

                // Select current event if already set
                if (EventComboBox != null)
                {
                    if (_currentEvent != null)
                    {
                        EventComboBox.SelectedItem = _events.FirstOrDefault(e => e.Id == _currentEvent.Id);
                    }
                    else if (_events.Any())
                    {
                        // Auto-select first event
                        EventComboBox.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load events: {ex.Message}");
            }
        }

        private async void EventComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingEvent) return;

            var selectedEvent = EventComboBox.SelectedItem as EventData;
            if (selectedEvent != null)
            {
                await LoadForEventAsync(selectedEvent);
            }
        }

        #endregion

        #region Event Service Event Handlers

        private void OnBackgroundsChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => RefreshBackgroundLists());
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() => UpdateSettingsUI());
        }

        private void OnBackgroundSelected(object sender, string backgroundPath)
        {
            Dispatcher.Invoke(() => UpdateSelectedBackground(backgroundPath));
        }

        #endregion

        #region Event Loading

        /// <summary>
        /// Load backgrounds for a specific event
        /// </summary>
        public async Task LoadForEventAsync(EventData eventData)
        {
            if (eventData == null) return;

            _isLoadingEvent = true;

            try
            {
                // Check if we're already loaded for this event
                if (_currentEvent != null && _currentEvent.Id == eventData.Id)
                {
                    System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Already loaded for event: {eventData.Name}");
                    return;
                }

                _currentEvent = eventData;

                // Update UI
                if (EventNameText != null)
                {
                    EventNameText.Text = $"Event: {eventData.Name}";
                }

                ShowLoading(true);

                // Load all available backgrounds first
                await _virtualBackgroundService.LoadBackgroundsAsync();

                // Load event-specific data from database
                await _eventBackgroundService.LoadEventAsync(eventData);

                // Refresh UI lists
                RefreshBackgroundLists();
                UpdateSettingsUI();

                // Update event dropdown selection
                if (EventComboBox != null && EventComboBox.SelectedItem != eventData)
                {
                    EventComboBox.SelectedItem = _events.FirstOrDefault(e => e.Id == eventData.Id);
                }

                ShowLoading(false);

                // Fire event changed
                EventChanged?.Invoke(this, EventArgs.Empty);

                System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Loaded event: {eventData.Name} with {_eventBackgroundService.EventBackgrounds.Count} backgrounds");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EventBackgroundManager] Error loading event: {ex.Message}");
                ShowLoading(false);
                ShowError($"Failed to load event: {ex.Message}");
            }
            finally
            {
                _isLoadingEvent = false;
            }

            // Show the overlay
            Show();
        }

        #endregion

        #region Background Management

        private void RefreshBackgroundLists()
        {
            // Ensure collections are initialized
            if (_allBackgrounds == null || _selectedBackgrounds == null || _customBackgrounds == null)
            {
                return;
            }

            _allBackgrounds.Clear();
            _selectedBackgrounds.Clear();
            _customBackgrounds.Clear();

            // Get all available backgrounds
            var availableBackgrounds = _virtualBackgroundService.GetAllBackgrounds();

            // Get event backgrounds
            var eventBackgrounds = _eventBackgroundService.EventBackgrounds;

            // Create view models for all available backgrounds
            foreach (var bg in availableBackgrounds)
            {
                var bgPath = bg.BackgroundPath ?? bg.FilePath; // Handle both properties
                var viewModel = new UnifiedBackgroundViewModel
                {
                    BackgroundPath = bgPath,
                    BackgroundName = bg.Name ?? Path.GetFileNameWithoutExtension(bgPath),
                    IsSelected = eventBackgrounds.Any(eb => eb.BackgroundPath == bgPath),
                    IsCustom = bgPath.Contains("\\Custom\\"),
                    Category = GetBackgroundCategory(bgPath)
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

            // Apply current filter
            ApplyFilter();
        }

        private void UpdateSelectedBackground(string backgroundPath)
        {
            // Update view models
            foreach (var vm in _allBackgrounds)
            {
                vm.IsCurrentlyActive = vm.BackgroundPath == backgroundPath;
            }
        }

        private void UpdateSettingsUI()
        {
            if (_eventBackgroundService.CurrentSettings == null) return;

            var settings = _eventBackgroundService.CurrentSettings;

            // Update checkboxes
            if (EnableBackgroundRemovalCheckBox != null)
            {
                EnableBackgroundRemovalCheckBox.IsChecked = settings.EnableBackgroundRemoval;
            }

            if (UseGuestPickerCheckBox != null)
            {
                UseGuestPickerCheckBox.IsChecked = settings.UseGuestBackgroundPicker;
            }

            // Update quality combo
            if (QualityComboBox != null)
            {
                QualityComboBox.SelectedItem = QualityComboBox.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag?.ToString() == settings.BackgroundRemovalQuality);
            }
        }

        #endregion

        #region Background Selection (Auto-Save)

        private void BackgroundItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UnifiedBackgroundViewModel viewModel)
            {
                if (_currentEvent == null)
                {
                    ShowError("Please select an event first");
                    return;
                }

                // Check if this is in selected view - if so, it's a selection
                var isInSelectedView = SelectedBackgroundsList.Items.Contains(viewModel);

                if (isInSelectedView)
                {
                    // Select this background as active
                    _eventBackgroundService.SelectBackground(viewModel.BackgroundPath);
                }
                else
                {
                    // Toggle selection for event
                    if (viewModel.IsSelected)
                    {
                        // Remove from event
                        _eventBackgroundService.RemoveBackground(viewModel.BackgroundPath);
                        viewModel.IsSelected = false;
                    }
                    else
                    {
                        // Add to event
                        _eventBackgroundService.AddBackground(viewModel.BackgroundPath, viewModel.BackgroundName);
                        viewModel.IsSelected = true;

                        // If it's the first background, select it
                        if (_selectedBackgrounds.Count == 0)
                        {
                            _eventBackgroundService.SelectBackground(viewModel.BackgroundPath);
                        }
                    }
                }
            }
        }

        private void RemoveBackground_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UnifiedBackgroundViewModel viewModel)
            {
                if (_currentEvent == null) return;

                // Remove from event (auto-saves)
                _eventBackgroundService.RemoveBackground(viewModel.BackgroundPath);
            }
        }

        private void SetAsDefault_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is UnifiedBackgroundViewModel viewModel)
            {
                if (_currentEvent == null) return;

                // Select as default (auto-saves)
                _eventBackgroundService.SelectBackground(viewModel.BackgroundPath);
            }
        }

        #endregion

        #region Photo Positioning (Auto-Save)

        private void PositionPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UnifiedBackgroundViewModel viewModel)
            {
                if (_currentEvent == null)
                {
                    ShowError("Please select an event first");
                    return;
                }

                ShowPhotoPositioner(viewModel.BackgroundPath);
            }
        }

        private void ShowPhotoPositioner(string backgroundPath)
        {
            // Show photo positioner overlay
            PhotoPositionerOverlay.Visibility = Visibility.Visible;

            // Load current placement data
            _currentPlacementData = _eventBackgroundService.GetPlacementData(backgroundPath) ?? new PhotoPlacementData();

            // Set up the positioning UI
            // TODO: Implement the actual positioning UI

            // For now, show a placeholder
            PositionerContent.Children.Clear();
            PositionerContent.Children.Add(new TextBlock
            {
                Text = "Photo Positioning UI\n(To be implemented)",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 24
            });
        }

        private void SavePhotoPosition_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlacementData != null && !string.IsNullOrEmpty(_eventBackgroundService.SelectedBackgroundPath))
            {
                // Save placement data (auto-saves to database)
                _eventBackgroundService.UpdatePhotoPlacement(_currentPlacementData);

                PhotoPositionerOverlay.Visibility = Visibility.Collapsed;
                ShowNotification("Photo position saved");
            }
        }

        private void CancelPhotoPosition_Click(object sender, RoutedEventArgs e)
        {
            PhotoPositionerOverlay.Visibility = Visibility.Collapsed;
        }

        private void OnPlacementDataChanged()
        {
            // Start/restart auto-save timer
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private void PhotoPositioner_PositionChanged(object sender, EventArgs e)
        {
            // Handle position change from the SimplePhotoPositioner control
            OnPlacementDataChanged();
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            _autoSaveTimer.Stop();

            if (_currentPlacementData != null && !string.IsNullOrEmpty(_eventBackgroundService.SelectedBackgroundPath))
            {
                // Auto-save placement data
                _eventBackgroundService.UpdatePhotoPlacement(_currentPlacementData);
                System.Diagnostics.Debug.WriteLine("[EventBackgroundManager] Auto-saved photo placement data");
            }
        }

        #endregion

        #region Settings Management (Auto-Save)

        private void EnableBackgroundRemoval_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentEvent == null || _isLoadingEvent) return;

            var checkBox = sender as CheckBox;
            if (checkBox != null)
            {
                // Update setting (auto-saves)
                _eventBackgroundService.UpdateSetting("EnableBackgroundRemoval", checkBox.IsChecked == true);
                ShowNotification("Background removal setting updated");
            }
        }

        private void UseGuestPicker_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentEvent == null || _isLoadingEvent) return;

            var checkBox = sender as CheckBox;
            if (checkBox != null)
            {
                // Update setting (auto-saves)
                _eventBackgroundService.UpdateSetting("UseGuestBackgroundPicker", checkBox.IsChecked == true);
                ShowNotification("Guest picker setting updated");
            }
        }

        private void Quality_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentEvent == null || _isLoadingEvent) return;

            var comboBox = sender as ComboBox;
            var selectedItem = comboBox?.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                var quality = selectedItem.Tag?.ToString() ?? "Low";
                _eventBackgroundService.UpdateSetting("BackgroundRemovalQuality", quality);
                ShowNotification("Quality setting updated");
            }
        }

        #endregion

        #region Custom Background Management

        private async void AddCustomBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEvent == null)
            {
                ShowError("Please select an event first");
                return;
            }

            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp",
                Multiselect = true,
                Title = "Select Background Images"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var fileName in openFileDialog.FileNames)
                {
                    try
                    {
                        // Copy to custom backgrounds folder
                        string customDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "Backgrounds", "Custom");
                        Directory.CreateDirectory(customDir);

                        string destFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(fileName)}";
                        string destPath = Path.Combine(customDir, destFileName);

                        File.Copy(fileName, destPath, true);

                        // Add to event (auto-saves)
                        _eventBackgroundService.AddBackground(destPath, Path.GetFileNameWithoutExtension(destFileName));

                        ShowNotification($"Added custom background: {destFileName}");
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Failed to add background: {ex.Message}");
                    }
                }

                // Reload backgrounds
                await _virtualBackgroundService.LoadBackgroundsAsync();
                RefreshBackgroundLists();
            }
        }

        private void DeleteCustomBackground_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is UnifiedBackgroundViewModel viewModel)
            {
                if (!viewModel.IsCustom)
                {
                    ShowError("Only custom backgrounds can be deleted");
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{viewModel.BackgroundName}'?\n\nThis will remove it from all events.",
                    "Delete Background",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Remove from current event if selected
                        if (viewModel.IsSelected && _currentEvent != null)
                        {
                            _eventBackgroundService.RemoveBackground(viewModel.BackgroundPath);
                        }

                        // Delete the file
                        if (File.Exists(viewModel.BackgroundPath))
                        {
                            File.Delete(viewModel.BackgroundPath);
                        }

                        // Remove from collections
                        _allBackgrounds.Remove(viewModel);
                        _customBackgrounds.Remove(viewModel);
                        _selectedBackgrounds.Remove(viewModel);
                        _filteredBackgrounds.Remove(viewModel);

                        ShowNotification("Background deleted successfully");
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Failed to delete background: {ex.Message}");
                    }
                }
            }
        }

        #endregion

        #region UI Management

        private void ShowView(string viewName)
        {
            // Hide all views (with null checks)
            if (AllView != null) AllView.Visibility = Visibility.Collapsed;
            if (CategoryView != null) CategoryView.Visibility = Visibility.Collapsed;
            if (SelectedView != null) SelectedView.Visibility = Visibility.Collapsed;
            if (CustomView != null) CustomView.Visibility = Visibility.Collapsed;

            // Show selected view
            switch (viewName)
            {
                case "All":
                    if (AllView != null) AllView.Visibility = Visibility.Visible;
                    _currentCategory = "All";
                    break;
                case "Category":
                    if (CategoryView != null) CategoryView.Visibility = Visibility.Visible;
                    break;
                case "Selected":
                    if (SelectedView != null) SelectedView.Visibility = Visibility.Visible;
                    break;
                case "Custom":
                    if (CustomView != null) CustomView.Visibility = Visibility.Visible;
                    break;
            }

            ApplyFilter();
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            // Skip if not fully initialized
            if (!this.IsLoaded)
            {
                return;
            }

            if (sender is RadioButton radioButton)
            {
                ShowView(radioButton.Tag?.ToString() ?? "All");
            }
        }

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                _currentCategory = button.Tag?.ToString() ?? "All";
                ApplyFilter();

                // Update category button states
                foreach (var child in CategoryButtons.Children)
                {
                    if (child is Button categoryButton)
                    {
                        categoryButton.Background = categoryButton == button
                            ? FindResource("AccentBrush") as Brush
                            : FindResource("CardBackgroundBrush") as Brush;
                    }
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchBox != null)
            {
                _searchText = SearchBox.Text?.ToLower() ?? string.Empty;
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            // Ensure collections are initialized
            if (_filteredBackgrounds == null || _allBackgrounds == null)
            {
                return;
            }

            _filteredBackgrounds.Clear();

            var filtered = _allBackgrounds.AsEnumerable();

            // Apply category filter
            if (_currentCategory != "All")
            {
                filtered = filtered.Where(b => b.Category == _currentCategory);
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filtered = filtered.Where(b =>
                    b.BackgroundName?.ToLower().Contains(_searchText) == true ||
                    b.BackgroundPath?.ToLower().Contains(_searchText) == true);
            }

            foreach (var item in filtered)
            {
                _filteredBackgrounds.Add(item);
            }
        }

        private string GetBackgroundCategory(string path)
        {
            if (path.Contains("\\Popular\\")) return "Popular";
            if (path.Contains("\\Nature\\")) return "Nature";
            if (path.Contains("\\Abstract\\")) return "Abstract";
            if (path.Contains("\\Holiday\\")) return "Holiday";
            if (path.Contains("\\Custom\\")) return "Custom";
            return "Other";
        }

        private void ShowLoading(bool show)
        {
            if (LoadingOverlay != null)
            {
                LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ShowNotification(string message)
        {
            // Show temporary notification
            if (NotificationText != null && NotificationPanel != null)
            {
                NotificationText.Text = message;
                NotificationPanel.Visibility = Visibility.Visible;

                // Hide after 2 seconds
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, timerArgs) =>
                {
                    NotificationPanel.Visibility = Visibility.Collapsed;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        #endregion

        #region Overlay Management

        public void Show()
        {
            this.Visibility = Visibility.Visible;

            // Animate in
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(OpacityProperty, animation);
        }

        public void Hide()
        {
            var animation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            animation.Completed += (s, e) =>
            {
                this.Visibility = Visibility.Collapsed;
                Closed?.Invoke(this, EventArgs.Empty);
            };

            this.BeginAnimation(OpacityProperty, animation);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void OverlayBackground_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Allow closing by clicking outside if the click is on the background
            if (e.Source == sender)
            {
                Hide();
            }
        }

        #endregion

        #region Copy From Event

        private async void CopyFromEvent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEvent == null)
            {
                ShowError("Please select an event first");
                return;
            }

            // Show event selection dialog
            var dialog = new Window
            {
                Title = "Copy Backgrounds From Event",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner = Window.GetWindow(this)
            };

            var stackPanel = new StackPanel { Margin = new Thickness(10) };
            stackPanel.Children.Add(new TextBlock { Text = "Select event to copy from:", Margin = new Thickness(0, 0, 0, 10) });

            var listBox = new ListBox { Height = 200 };
            var otherEvents = _events.Where(evt => evt.Id != _currentEvent.Id).ToList();
            listBox.ItemsSource = otherEvents;
            listBox.DisplayMemberPath = "Name";
            stackPanel.Children.Add(listBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };

            var okButton = new Button { Content = "Copy", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
            okButton.Click += (s, clickArgs) =>
            {
                var selectedEvent = listBox.SelectedItem as EventData;
                if (selectedEvent != null)
                {
                    _eventBackgroundService.CopyFromEvent(selectedEvent.Id);
                    ShowNotification($"Copied backgrounds from {selectedEvent.Name}");
                    dialog.DialogResult = true;
                }
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button { Content = "Cancel", Width = 80 };
            cancelButton.Click += (s, clickArgs) => dialog.DialogResult = false;
            buttonPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(buttonPanel);
            dialog.Content = stackPanel;

            dialog.ShowDialog();
        }

        #endregion

        #region Clear All

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEvent == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to remove all backgrounds from '{_currentEvent.Name}'?",
                "Clear All Backgrounds",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _eventBackgroundService.ClearAllBackgrounds();
                ShowNotification("All backgrounds removed");
            }
        }

        #endregion

        #region Missing Event Handlers

        private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Allow closing by clicking backdrop
            if (e.Source == sender)
            {
                Hide();
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox != null)
            {
                SearchBox.Text = string.Empty;
            }
        }

        private void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            AddCustomBackground_Click(sender, e);
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            // Handle deletion of selected custom backgrounds
            var selectedItems = _customBackgrounds.Where(b => b.IsSelected).ToList();
            foreach (var item in selectedItems)
            {
                DeleteCustomBackground(item);
            }
        }

        private void DeleteCustomBackground(UnifiedBackgroundViewModel viewModel)
        {
            if (!viewModel.IsCustom) return;

            try
            {
                // Remove from event if selected
                if (viewModel.IsSelected && _currentEvent != null)
                {
                    _eventBackgroundService.RemoveBackground(viewModel.BackgroundPath);
                }

                // Delete file
                if (File.Exists(viewModel.BackgroundPath))
                {
                    File.Delete(viewModel.BackgroundPath);
                }

                // Remove from collections
                _allBackgrounds.Remove(viewModel);
                _customBackgrounds.Remove(viewModel);
                _selectedBackgrounds.Remove(viewModel);
                _filteredBackgrounds.Remove(viewModel);

                ShowNotification("Background deleted");
            }
            catch (Exception ex)
            {
                ShowError($"Failed to delete background: {ex.Message}");
            }
        }

        private void SelectPopular_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEvent == null)
            {
                ShowError("Please select an event first");
                return;
            }

            // Select popular backgrounds
            var popularBackgrounds = _allBackgrounds.Where(b => b.Category == "Popular").Take(6);
            foreach (var bg in popularBackgrounds)
            {
                if (!bg.IsSelected)
                {
                    _eventBackgroundService.AddBackground(bg.BackgroundPath, bg.BackgroundName);
                    bg.IsSelected = true;
                }
            }

            RefreshBackgroundLists();
            ShowNotification("Popular backgrounds selected");
        }

        public static void ShowInWindow(Window owner, EventData eventData = null)
        {
            // Static method for compatibility - creates instance in a window
            var manager = new EventBackgroundManager();
            ShowInternal(owner, eventData, manager);
        }

        // Overload with different parameter order for compatibility
        public static void ShowInWindow(EventData eventData, Window owner)
        {
            // Static method for compatibility - creates instance in a window
            var manager = new EventBackgroundManager();
            ShowInternal(owner, eventData, manager);
        }

        private static void ShowInternal(Window owner, EventData eventData, EventBackgroundManager manager)
        {
            var window = new Window
            {
                Title = "Background Manager",
                Content = manager,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 1200,
                Height = 800,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent
            };

            if (eventData != null)
            {
                manager.LoadForEventAsync(eventData).Wait();
            }

            window.ShowDialog();
        }

        #endregion
    }

    #region View Models

    public class UnifiedBackgroundViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isCurrentlyActive;

        public string BackgroundPath { get; set; }
        public string BackgroundName { get; set; }
        public string Category { get; set; }
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

        public bool IsCurrentlyActive
        {
            get => _isCurrentlyActive;
            set
            {
                _isCurrentlyActive = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion
}