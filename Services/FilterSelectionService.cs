using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for managing filter selection UI and logic
    /// </summary>
    public class FilterSelectionService
    {
        #region Events
        public event EventHandler<FilterSelectedEventArgs> FilterSelected;
        public event EventHandler FilterSelectionCancelled;
        public event EventHandler ShowFilterSelectionRequested;
        public event EventHandler HideFilterSelectionRequested;
        #endregion

        #region Private Fields
        private readonly PhotoFilterServiceHybrid _filterService;
        private FilterType _selectedFilter = FilterType.None;
        private List<FilterOption> _availableFilters;
        private readonly Random _random = new Random();
        #endregion

        #region Constructor
        public FilterSelectionService()
        {
            _filterService = new PhotoFilterServiceHybrid();
            InitializeAvailableFilters();
        }
        #endregion

        #region Properties
        public FilterType SelectedFilter => _selectedFilter;
        public List<FilterOption> AvailableFilters => _availableFilters;
        #endregion

        #region Public Methods
        /// <summary>
        /// Initialize the list of available filters based on settings
        /// </summary>
        private void InitializeAvailableFilters()
        {
            // Get the enabled filters from settings
            string enabledFiltersString = Properties.Settings.Default.EnabledFilters ?? "";
            var enabledFiltersList = enabledFiltersString.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList();
            
            // Initialize all available filters
            _availableFilters = new List<FilterOption>
            {
                new FilterOption { FilterType = FilterType.None, Name = "Original", IsEnabled = true }
            };
            
            // Only add filters if the filter system is enabled
            if (Properties.Settings.Default.EnableFilters)
            {
                // Add filters based on what's enabled in settings
                // If no filters are specified, enable all by default
                bool useAllFilters = enabledFiltersList.Count == 0;
                
                // Black & White
                if (useAllFilters || enabledFiltersList.Contains("BlackWhite"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.BlackAndWhite, Name = "Black & White", IsEnabled = true });
                
                // Sepia
                if (useAllFilters || enabledFiltersList.Contains("Sepia"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.Sepia, Name = "Sepia", IsEnabled = true });
                
                // Vintage
                if (useAllFilters || enabledFiltersList.Contains("Vintage"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.Vintage, Name = "Vintage", IsEnabled = true });
                
                // Glamour
                if (useAllFilters || enabledFiltersList.Contains("Glamour"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.Glamour, Name = "Glamour", IsEnabled = true });
                
                // Cool
                if (useAllFilters || enabledFiltersList.Contains("Cool"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.Cool, Name = "Cool", IsEnabled = true });
                
                // Warm
                if (useAllFilters || enabledFiltersList.Contains("Warm"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.Warm, Name = "Warm", IsEnabled = true });
                
                // High Contrast
                if (useAllFilters || enabledFiltersList.Contains("HighContrast"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.HighContrast, Name = "High Contrast", IsEnabled = true });
                
                // Vivid
                if (useAllFilters || enabledFiltersList.Contains("Vivid"))
                    _availableFilters.Add(new FilterOption { FilterType = FilterType.Vivid, Name = "Vivid", IsEnabled = true });
            }
        }

        /// <summary>
        /// Request to show the filter selection UI
        /// </summary>
        public void RequestFilterSelection()
        {
            // Log.Debug("FilterSelectionService: Requesting filter selection UI");
            ShowFilterSelectionRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Select a filter
        /// </summary>
        public void SelectFilter(FilterType filter)
        {
            // Log.Debug($"FilterSelectionService: Filter selected - {filter}");
            _selectedFilter = filter;
            
            // Fire event with selected filter
            FilterSelected?.Invoke(this, new FilterSelectedEventArgs { SelectedFilter = filter });
            
            // Hide the selection UI
            HideFilterSelectionRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Cancel filter selection
        /// </summary>
        public void CancelFilterSelection()
        {
            // Log.Debug("FilterSelectionService: Filter selection cancelled");
            _selectedFilter = FilterType.None;
            
            FilterSelectionCancelled?.Invoke(this, EventArgs.Empty);
            HideFilterSelectionRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Apply no filter (continue without filter)
        /// </summary>
        public void ApplyNoFilter()
        {
            SelectFilter(FilterType.None);
        }

        /// <summary>
        /// Get the enabled filters list for UI display
        /// </summary>
        public List<FilterOption> GetEnabledFilters()
        {
            return _availableFilters.Where(f => f.IsEnabled).ToList();
        }

        /// <summary>
        /// Generate preview thumbnails for all filters using the provided photo
        /// </summary>
        public async Task GenerateFilterPreviewsAsync(string photoPath)
        {
            if (string.IsNullOrEmpty(photoPath) || !File.Exists(photoPath))
                return;
                
            // Create a temporary directory for preview images
            string tempDir = Path.Combine(Path.GetTempPath(), "FilterPreviews", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var tasks = new List<Task>();
                
                foreach (var filterOption in _availableFilters.Where(f => f.IsEnabled))
                {
                    tasks.Add(Task.Run(() => GenerateSinglePreview(photoPath, tempDir, filterOption)));
                }
                
                await Task.WhenAll(tasks);
            }
            finally
            {
                // Clean up temp directory after a delay
                Task.Delay(30000).ContinueWith(_ => 
                {
                    try 
                    { 
                        if (Directory.Exists(tempDir))
                            Directory.Delete(tempDir, true); 
                    }
                    catch { }
                });
            }
        }
        
        private void GenerateSinglePreview(string photoPath, string tempDir, FilterOption filterOption)
        {
            try
            {
                string previewPath = Path.Combine(tempDir, $"preview_{filterOption.FilterType}.jpg");
                
                if (filterOption.FilterType == FilterType.None)
                {
                    // For "None" filter, just copy the original
                    File.Copy(photoPath, previewPath, true);
                }
                else
                {
                    // Apply filter to create preview
                    _filterService.ApplyFilterToFile(photoPath, previewPath, filterOption.FilterType);
                }
                
                // Load the preview image into memory
                if (File.Exists(previewPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400; // Match the larger UI thumbnail size
                    bitmap.UriSource = new Uri(previewPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze(); // Make it thread-safe
                    
                    // Update the preview image on UI thread
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        filterOption.PreviewImage = bitmap;
                    });
                }
            }
            catch
            {
                // Silently handle preview generation errors
            }
        }
        
        /// <summary>
        /// Refresh available filters from settings
        /// </summary>
        public void RefreshAvailableFilters()
        {
            InitializeAvailableFilters();
        }
        
        /// <summary>
        /// Reset the selected filter
        /// </summary>
        public void Reset()
        {
            _selectedFilter = FilterType.None;
            InitializeAvailableFilters();
            
            // Clear preview images
            foreach (var filter in _availableFilters)
            {
                filter.PreviewImage = null;
            }
        }
        
        /// <summary>
        /// Automatically select and apply a filter based on settings
        /// </summary>
        /// <returns>The selected filter type, or null if user selection is required</returns>
        public FilterType? GetAutoApplyFilter()
        {
            // Check if auto-apply is enabled
            if (!Properties.Settings.Default.AutoApplyFilter)
                return null;
                
            // Get enabled filters (excluding None)
            var enabledFilters = _availableFilters
                .Where(f => f.IsEnabled && f.FilterType != FilterType.None)
                .ToList();
                
            if (enabledFilters.Count == 0)
            {
                // No filters enabled, use None
                return FilterType.None;
            }
            else if (enabledFilters.Count == 1)
            {
                // Single filter enabled, apply it automatically
                return enabledFilters[0].FilterType;
            }
            else
            {
                // Multiple filters enabled, select randomly
                int randomIndex = _random.Next(enabledFilters.Count);
                return enabledFilters[randomIndex].FilterType;
            }
        }
        
        /// <summary>
        /// Check if filter selection UI should be shown
        /// </summary>
        public bool ShouldShowFilterSelection()
        {
            // Don't show UI if auto-apply is enabled
            if (Properties.Settings.Default.AutoApplyFilter)
                return false;
                
            // Show UI if filters are enabled and user can change them
            return Properties.Settings.Default.EnableFilters && 
                   Properties.Settings.Default.AllowFilterChange;
        }
        #endregion
    }

    #region Supporting Classes
    /// <summary>
    /// Filter option for UI display with preview
    /// </summary>
    public class FilterOption : INotifyPropertyChanged
    {
        private BitmapImage _previewImage;
        
        public FilterType FilterType { get; set; }
        public string Name { get; set; }
        public bool IsEnabled { get; set; }
        
        public BitmapImage PreviewImage 
        { 
            get => _previewImage; 
            set
            {
                _previewImage = value;
                OnPropertyChanged();
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Event args for filter selection
    /// </summary>
    public class FilterSelectedEventArgs : EventArgs
    {
        public FilterType SelectedFilter { get; set; }
    }
    #endregion
}