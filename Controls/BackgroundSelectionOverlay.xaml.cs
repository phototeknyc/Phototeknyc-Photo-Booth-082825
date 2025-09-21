using System;
using System.Collections.Generic;
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
    /// Interaction logic for BackgroundSelectionOverlay.xaml
    /// Quick background selection before starting photo session
    /// </summary>
    public partial class BackgroundSelectionOverlay : UserControl
    {
        #region Private Fields

        private VirtualBackgroundService _backgroundService;
        private ObservableCollection<BackgroundQuickViewModel> _backgrounds;
        private BackgroundQuickViewModel _selectedBackground;
        private string _currentCategory = "All";
        private List<BackgroundQuickViewModel> _allBackgrounds;

        #endregion

        #region Events

        public event EventHandler CancelRequested;
        public event EventHandler<BackgroundSelectedForSessionEventArgs> BackgroundSelected;
        public event EventHandler NoBackgroundSelected;

        #endregion

        #region Constructor

        public BackgroundSelectionOverlay()
        {
            InitializeComponent();
            _backgrounds = new ObservableCollection<BackgroundQuickViewModel>();
            _allBackgrounds = new List<BackgroundQuickViewModel>();
            BackgroundsItemsControl.ItemsSource = _backgrounds;
            Initialize();
        }

        #endregion

        #region Initialization

        private async void Initialize()
        {
            try
            {
                // Get service instance
                _backgroundService = VirtualBackgroundService.Instance;

                // Load backgrounds
                await _backgroundService.LoadBackgroundsAsync();

                // Load all backgrounds
                LoadAllBackgrounds();

                // Show popular by default
                LoadCategory("Popular");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize BackgroundSelectionOverlay: {ex.Message}");
            }
        }

        private void LoadAllBackgrounds()
        {
            _allBackgrounds.Clear();

            var categories = _backgroundService.GetCategories();
            foreach (var category in categories)
            {
                var backgroundList = _backgroundService.GetBackgroundsByCategory(category);
                foreach (var bg in backgroundList)
                {
                    _allBackgrounds.Add(new BackgroundQuickViewModel
                    {
                        Id = bg.Id,
                        Name = bg.Name,
                        Category = bg.Category,
                        FilePath = bg.FilePath,
                        ThumbnailPath = bg.ThumbnailPath ?? bg.FilePath,
                        IsDefault = bg.IsDefault,
                        IsPopular = IsPopularBackground(bg.Name, bg.Category)
                    });
                }
            }
        }

        private bool IsPopularBackground(string name, string category)
        {
            // Define popular backgrounds
            var popularNames = new[] { "White", "Gray", "Blue", "Sunset", "Ocean", "Office" };
            return popularNames.Any(p => name.Contains(p));
        }

        #endregion

        #region Category Management

        private void CategoryTab_Checked(object sender, RoutedEventArgs e)
        {
            // Prevent events during initialization
            if (!IsLoaded)
                return;

            if (sender is RadioButton radioButton)
            {
                var category = radioButton.Content.ToString();
                if (category.Contains("Solid"))
                    category = "Solid";
                else if (category.Contains("Gradient"))
                    category = "Gradient";

                _currentCategory = category;
                LoadCategory(category);
            }
        }

        private void LoadCategory(string category)
        {
            try
            {
                // Ensure collections are initialized
                if (_backgrounds == null)
                {
                    _backgrounds = new ObservableCollection<BackgroundQuickViewModel>();
                    if (BackgroundsItemsControl != null)
                    {
                        BackgroundsItemsControl.ItemsSource = _backgrounds;
                    }
                }

                if (_allBackgrounds == null)
                {
                    _allBackgrounds = new List<BackgroundQuickViewModel>();
                }

                _backgrounds.Clear();

                IEnumerable<BackgroundQuickViewModel> filteredBackgrounds;

                switch (category)
                {
                    case "All":
                        filteredBackgrounds = _allBackgrounds;
                        break;
                    case "Popular":
                        filteredBackgrounds = _allBackgrounds.Where(b => b.IsPopular);
                        break;
                    default:
                        filteredBackgrounds = _allBackgrounds.Where(b => b.Category == category);
                        break;
                }

                foreach (var bg in filteredBackgrounds)
                {
                    _backgrounds.Add(bg);
                }

                // Auto-select first item if none selected
                if (_selectedBackground == null && _backgrounds.Count > 0)
                {
                    SelectBackground(_backgrounds[0]);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load category {category}: {ex.Message}");
            }
        }

        #endregion

        #region Background Selection

        private void BackgroundItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is BackgroundQuickViewModel background)
            {
                SelectBackground(background);
            }
        }

        private void SelectBackground(BackgroundQuickViewModel background)
        {
            // Deselect previous
            if (_selectedBackground != null)
            {
                _selectedBackground.IsSelected = false;
            }

            // Select new
            _selectedBackground = background;
            _selectedBackground.IsSelected = true;

            // Update UI
            SelectedBackgroundText.Text = background.Name;
            StartButton.IsEnabled = true;

            // Save selection to service
            _backgroundService.SetSelectedBackground(background.FilePath);
        }

        #endregion

        #region Button Click Handlers

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBackground != null)
            {
                // Raise event with selected background
                BackgroundSelected?.Invoke(this, new BackgroundSelectedForSessionEventArgs
                {
                    BackgroundPath = _selectedBackground.FilePath,
                    BackgroundName = _selectedBackground.Name,
                    Category = _selectedBackground.Category
                });

                Log.Debug($"Background selected for session: {_selectedBackground.Name}");
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear any selected background
            _backgroundService.SetSelectedBackground(null);

            // Raise event to start session without background
            NoBackgroundSelected?.Invoke(this, EventArgs.Empty);

            Log.Debug("Starting session without virtual background");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Pre-select a specific background
        /// </summary>
        public void SetSelectedBackground(string backgroundPath)
        {
            var background = _allBackgrounds.FirstOrDefault(b => b.FilePath == backgroundPath);
            if (background != null)
            {
                SelectBackground(background);
            }
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

            SelectedBackgroundText.Text = "None";
            StartButton.IsEnabled = false;

            // Reset to All category
            AllTab.IsChecked = true;
            LoadCategory("All");
        }

        #endregion
    }

    #region View Models

    public class BackgroundQuickViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string FilePath { get; set; }
        public string ThumbnailPath { get; set; }
        public bool IsDefault { get; set; }
        public bool IsPopular { get; set; }

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

    public class BackgroundSelectedForSessionEventArgs : EventArgs
    {
        public string BackgroundPath { get; set; }
        public string BackgroundName { get; set; }
        public string Category { get; set; }
    }

    #endregion
}