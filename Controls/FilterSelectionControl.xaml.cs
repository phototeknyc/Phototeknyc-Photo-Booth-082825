using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Services;

namespace Photobooth.Controls
{
    public partial class FilterSelectionControl : UserControl
    {
        private PhotoFilterService filterService;
        private ObservableCollection<FilterItem> filterItems;
        private Bitmap originalImage;
        private FilterItem selectedFilter;
        
        public event EventHandler<FilterSelectedEventArgs> FilterSelected;
        
        public FilterSelectionControl()
        {
            InitializeComponent();
            filterService = new PhotoFilterService();
            filterItems = new ObservableCollection<FilterItem>();
            filterItemsControl.ItemsSource = filterItems;
            InitializeFilters();
            
            // Hook up unloaded event
            this.Unloaded += UserControl_Unloaded;
        }
        
        private void InitializeFilters()
        {
            // Get enabled filters from settings
            string enabledFiltersString = Properties.Settings.Default.EnabledFilters;
            var enabledFilters = string.IsNullOrEmpty(enabledFiltersString) 
                ? new List<string>() 
                : enabledFiltersString.Split(',').ToList();
            
            // Always add "Original" option
            filterItems.Add(new FilterItem
            {
                FilterType = FilterType.None,
                Name = "Original",
                IsSelected = true
            });
            
            // Add only enabled filters
            var allFilters = new[]
            {
                new { Type = FilterType.BlackAndWhite, Name = "B&W", Key = "BlackWhite" },
                new { Type = FilterType.Sepia, Name = "Sepia", Key = "Sepia" },
                new { Type = FilterType.Vintage, Name = "Vintage", Key = "Vintage" },
                new { Type = FilterType.Glamour, Name = "Glamour", Key = "Glamour" },
                new { Type = FilterType.Cool, Name = "Cool", Key = "Cool" },
                new { Type = FilterType.Warm, Name = "Warm", Key = "Warm" },
                new { Type = FilterType.HighContrast, Name = "Contrast", Key = "HighContrast" },
                new { Type = FilterType.Soft, Name = "Soft", Key = "Soft" },
                new { Type = FilterType.Vivid, Name = "Vivid", Key = "Vivid" }
            };
            
            foreach (var filter in allFilters)
            {
                // If no filters are specified in settings, show all
                // Otherwise, only show enabled filters
                if (enabledFilters.Count == 0 || enabledFilters.Contains(filter.Key))
                {
                    filterItems.Add(new FilterItem
                    {
                        FilterType = filter.Type,
                        Name = filter.Name,
                        IsSelected = false
                    });
                }
            }
            
            // Set default selection
            if (filterItems.Count > 0)
            {
                selectedFilter = filterItems[0];
                selectedFilter.IsSelected = true;
            }
        }
        
        public async Task SetSourceImage(string imagePath)
        {
            if (!File.Exists(imagePath))
                return;
                
            await Task.Run(() =>
            {
                originalImage?.Dispose();
                originalImage = new Bitmap(imagePath);
            });
            
            await GenerateFilterPreviews();
        }
        
        public async Task SetSourceImage(BitmapImage bitmapImage)
        {
            if (bitmapImage == null)
                return;
                
            await Task.Run(() =>
            {
                originalImage?.Dispose();
                originalImage = BitmapImageToBitmap(bitmapImage);
            });
            
            await GenerateFilterPreviews();
        }
        
        private async Task GenerateFilterPreviews()
        {
            if (originalImage == null)
                return;
                
            // Show loading indicator
            Dispatcher.Invoke(() => loadingIndicator.Visibility = Visibility.Visible);
            
            // Create thumbnail version for faster preview generation
            Bitmap thumbnail = null;
            await Task.Run(() =>
            {
                int thumbWidth = 100;
                int thumbHeight = (int)(originalImage.Height * (thumbWidth / (float)originalImage.Width));
                thumbnail = new Bitmap(originalImage, thumbWidth, thumbHeight);
            });
            
            // Generate preview for each filter
            foreach (var filterItem in filterItems)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        Bitmap preview = filterService.ApplyFilter(
                            thumbnail, 
                            filterItem.FilterType, 
                            Properties.Settings.Default.FilterIntensity / 100f);
                        
                        Dispatcher.Invoke(() =>
                        {
                            filterItem.PreviewImage = BitmapToBitmapImage(preview);
                            preview.Dispose();
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error generating preview for {filterItem.Name}: {ex.Message}");
                    }
                });
            }
            
            thumbnail?.Dispose();
            
            // Hide loading indicator
            Dispatcher.Invoke(() => loadingIndicator.Visibility = Visibility.Collapsed);
        }
        
        private void FilterItem_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var filterItem = border?.Tag as FilterItem;
            
            if (filterItem == null || filterItem == selectedFilter)
                return;
                
            // Update selection
            if (selectedFilter != null)
                selectedFilter.IsSelected = false;
                
            filterItem.IsSelected = true;
            selectedFilter = filterItem;
            
            // Raise event
            FilterSelected?.Invoke(this, new FilterSelectedEventArgs
            {
                SelectedFilter = filterItem.FilterType,
                FilterName = filterItem.Name
            });
        }
        
        public FilterType GetSelectedFilter()
        {
            return selectedFilter?.FilterType ?? FilterType.None;
        }
        
        public void SetSelectedFilter(FilterType filterType)
        {
            foreach (var item in filterItems)
            {
                item.IsSelected = item.FilterType == filterType;
                if (item.IsSelected)
                    selectedFilter = item;
            }
        }
        
        public async Task<Bitmap> ApplySelectedFilter(Bitmap sourceImage)
        {
            if (sourceImage == null || selectedFilter == null)
                return sourceImage;
                
            return await Task.Run(() =>
            {
                float intensity = Properties.Settings.Default.FilterIntensity / 100f;
                return filterService.ApplyFilter(sourceImage, selectedFilter.FilterType, intensity);
            });
        }
        
        public async Task<string> ApplySelectedFilterToFile(string inputPath, string outputPath = null)
        {
            if (!File.Exists(inputPath))
                return inputPath;
                
            if (string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(inputPath);
                string filename = Path.GetFileNameWithoutExtension(inputPath);
                string ext = Path.GetExtension(inputPath);
                outputPath = Path.Combine(dir, $"{filename}_filtered{ext}");
            }
            
            return await Task.Run(() =>
            {
                float intensity = Properties.Settings.Default.FilterIntensity / 100f;
                return filterService.ApplyFilterToFile(inputPath, outputPath, selectedFilter.FilterType, intensity);
            });
        }
        
        private Bitmap BitmapImageToBitmap(BitmapImage bitmapImage)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                Bitmap bitmap = new Bitmap(outStream);
                return new Bitmap(bitmap);
            }
        }
        
        private BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }
        
        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            originalImage?.Dispose();
            filterItems.Clear();
        }
    }
    
    public class FilterItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private BitmapImage _previewImage;
        
        public FilterType FilterType { get; set; }
        public string Name { get; set; }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
        
        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class FilterSelectedEventArgs : EventArgs
    {
        public FilterType SelectedFilter { get; set; }
        public string FilterName { get; set; }
    }
}