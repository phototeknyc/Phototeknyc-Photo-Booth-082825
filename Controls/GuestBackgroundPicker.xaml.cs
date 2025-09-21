using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CameraControl.Devices;
using Photobooth.Services;

namespace Photobooth.Controls
{
    /// <summary>
    /// Simple touch-friendly background picker for guests
    /// </summary>
    public partial class GuestBackgroundPicker : UserControl
    {
        #region Private Fields

        private EventBackgroundService _eventBackgroundService;
        private ObservableCollection<GuestBackgroundViewModel> _backgrounds;
        private GuestBackgroundViewModel _selectedBackground;

        #endregion

        #region Events

        public event EventHandler<GuestBackgroundSelectedEventArgs> BackgroundSelected;
        public event EventHandler SessionStartRequested;
        public event EventHandler PickerClosed;

        #endregion

        #region Constructor

        public GuestBackgroundPicker()
        {
            InitializeComponent();
            _backgrounds = new ObservableCollection<GuestBackgroundViewModel>();
            BackgroundsGrid.ItemsSource = _backgrounds;
            Initialize();
        }

        #endregion

        #region Initialization

        private async void Initialize()
        {
            try
            {
                _eventBackgroundService = EventBackgroundService.Instance;

                // Load event backgrounds
                await LoadBackgroundsAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize GuestBackgroundPicker: {ex.Message}");
            }
        }

        private async Task LoadBackgroundsAsync()
        {
            try
            {
                // Get guest background options from service
                var options = _eventBackgroundService.GetGuestBackgroundOptions();

                _backgrounds.Clear();

                foreach (var option in options)
                {
                    // Skip "no background" option
                    if (option.Id == "none")
                        continue;

                    _backgrounds.Add(new GuestBackgroundViewModel
                    {
                        Id = option.Id,
                        Name = option.Name,
                        ThumbnailPath = option.ThumbnailPath,
                        BackgroundPath = option.BackgroundPath,
                        IsNoBackground = false,
                        HasThumbnail = !string.IsNullOrEmpty(option.ThumbnailPath)
                    });
                }

                // Don't pre-select any option
                // User must explicitly choose

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load backgrounds: {ex.Message}");
            }
        }

        #endregion

        #region Background Selection

        private void BackgroundOption_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && !string.IsNullOrEmpty(border.Tag?.ToString()))
            {
                var backgroundId = border.Tag.ToString();
                var background = _backgrounds.FirstOrDefault(b => b.Id == backgroundId);

                if (background != null)
                {
                    SelectBackground(background);
                    StartSession();
                }
            }
        }

        private void SelectBackground(GuestBackgroundViewModel background)
        {
            // Deselect previous
            if (_selectedBackground != null)
            {
                _selectedBackground.IsSelected = false;
            }

            // Select new
            _selectedBackground = background;
            _selectedBackground.IsSelected = true;

            // Apply selection to service
            _eventBackgroundService.SelectBackgroundForSession(background.Id);

            Log.Debug($"Guest selected background: {background.Name}");
        }

        #endregion

        #region Session Start

        private void StartSession()
        {
            try
            {
                // Show loading indicator briefly
                LoadingIndicator.Visibility = Visibility.Visible;

                // Raise event immediately
                BackgroundSelected?.Invoke(this, new GuestBackgroundSelectedEventArgs
                {
                    BackgroundId = _selectedBackground.Id,
                    BackgroundPath = _selectedBackground.BackgroundPath,
                    BackgroundName = _selectedBackground.Name
                });

                // Request session start immediately
                SessionStartRequested?.Invoke(this, EventArgs.Empty);

                // Hide the picker overlay
                this.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start session: {ex.Message}");
                LoadingIndicator.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Close Button

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Raise closed event first so the page can react
                PickerClosed?.Invoke(this, EventArgs.Empty);

                // Hide this control
                this.Visibility = Visibility.Collapsed;

                // Try to remove from parent if in a grid
                var parent = this.Parent;
                if (parent is Grid grid)
                {
                    grid.Children.Remove(this);
                }
                else if (parent is Panel panel)
                {
                    panel.Children.Remove(this);
                }

                Log.Debug("[GuestBackgroundPicker] Closed by user");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to close picker: {ex.Message}");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Reload backgrounds for current event
        /// </summary>
        public async Task RefreshBackgroundsAsync()
        {
            await LoadBackgroundsAsync();
        }

        /// <summary>
        /// Reset selection
        /// </summary>
        public void Reset()
        {
            if (_selectedBackground != null)
            {
                _selectedBackground.IsSelected = false;
                _selectedBackground = null;
            }

            LoadingIndicator.Visibility = Visibility.Collapsed;
        }

        #endregion
    }

    #region View Models

    public class GuestBackgroundViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; }
        public string Name { get; set; }
        public string ThumbnailPath { get; set; }
        public string BackgroundPath { get; set; }
        public bool IsNoBackground { get; set; }
        public bool HasThumbnail { get; set; }

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

    public class GuestBackgroundSelectedEventArgs : EventArgs
    {
        public string BackgroundId { get; set; }
        public string BackgroundPath { get; set; }
        public string BackgroundName { get; set; }
    }

    #endregion
}