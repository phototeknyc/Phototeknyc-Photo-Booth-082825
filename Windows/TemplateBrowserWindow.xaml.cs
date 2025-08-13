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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Database;
using Photobooth.Services;

namespace Photobooth.Windows
{
    public partial class TemplateBrowserWindow : Window, INotifyPropertyChanged
    {
        private readonly TemplateService templateService;
        private readonly TemplateDatabase templateDatabase;
        private List<TemplateItemViewModel> allTemplates;
        private ObservableCollection<TemplateItemViewModel> filteredTemplates;
        private readonly int _preselectedTemplateId;
        
        public TemplateData SelectedTemplate { get; private set; }

        public TemplateBrowserWindow() : this(-1)
        {
        }

        public TemplateBrowserWindow(int preselectedTemplateId)
        {
            InitializeComponent();
            DataContext = this;
            
            _preselectedTemplateId = preselectedTemplateId;
            
            templateService = new TemplateService();
            templateDatabase = new TemplateDatabase();
            filteredTemplates = new ObservableCollection<TemplateItemViewModel>();
            
            TemplatesList.ItemsSource = filteredTemplates;
            
            Loaded += TemplateBrowserWindow_Loaded;
        }

        private async void TemplateBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTemplates();
        }

        private async Task LoadTemplates()
        {
            try
            {
                StatusText.Text = "Loading templates...";
                
                var templates = templateDatabase.GetAllTemplates();
                allTemplates = new List<TemplateItemViewModel>();
                
                foreach (var template in templates)
                {
                    var viewModel = new TemplateItemViewModel(template);
                    
                    // Generate thumbnail if not exists
                    if (string.IsNullOrEmpty(template.ThumbnailImagePath) || !File.Exists(template.ThumbnailImagePath))
                    {
                        await GenerateThumbnail(viewModel);
                    }
                    else
                    {
                        viewModel.ThumbnailImageSource = LoadImageFromPath(template.ThumbnailImagePath);
                    }
                    
                    allTemplates.Add(viewModel);
                }
                
                FilterAndSortTemplates();
                UpdateStatus();
                
                // Mark the preselected template for highlighting
                if (_preselectedTemplateId > 0)
                {
                    var templateToHighlight = filteredTemplates.FirstOrDefault(vm => vm.Template.Id == _preselectedTemplateId);
                    if (templateToHighlight != null)
                    {
                        // Mark this template as the last used one for visual highlighting
                        templateToHighlight.IsLastUsed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading templates: {ex.Message}";
                ShowEmptyState();
            }
        }

        private async Task GenerateThumbnail(TemplateItemViewModel viewModel)
        {
            try
            {
                // Create a simple thumbnail from template data
                var thumbnail = CreateTemplateThumbnail(viewModel.Template);
                viewModel.ThumbnailImageSource = thumbnail;
                viewModel.HasThumbnail = thumbnail != null;
                
                // Save thumbnail to file for future use
                if (thumbnail != null)
                {
                    try
                    {
                        var thumbnailPath = SaveThumbnailToFile(thumbnail, viewModel.Template.Name, viewModel.Template.Id);
                        if (!string.IsNullOrEmpty(thumbnailPath))
                        {
                            viewModel.Template.ThumbnailImagePath = thumbnailPath;
                            templateDatabase.UpdateTemplate(viewModel.Template.Id, viewModel.Template);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to save thumbnail to file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate thumbnail for template {viewModel.Template.Id}: {ex.Message}");
                viewModel.HasThumbnail = false;
            }
        }

        private BitmapImage CreateTemplateThumbnail(TemplateData template)
        {
            try
            {
                // Create a simple visual representation of the template
                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // Background
                    var backgroundColor = Colors.White;
                    if (!string.IsNullOrEmpty(template.BackgroundColor))
                    {
                        try
                        {
                            backgroundColor = (Color)ColorConverter.ConvertFromString(template.BackgroundColor);
                        }
                        catch { /* Use default */ }
                    }
                    
                    var backgroundBrush = new SolidColorBrush(backgroundColor);
                    drawingContext.DrawRectangle(backgroundBrush, null, new Rect(0, 0, 200, 150));
                    
                    // Add template name text
                    var formattedText = new FormattedText(
                        template.Name ?? "Untitled",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        12,
                        Brushes.Black,
                        96);
                    
                    drawingContext.DrawText(formattedText, new Point(10, 10));
                    
                    // Add a simple template indicator
                    drawingContext.DrawRectangle(null, new Pen(Brushes.Gray, 1), new Rect(10, 40, 180, 100));
                    drawingContext.DrawLine(new Pen(Brushes.LightGray, 1), new Point(20, 60), new Point(180, 60));
                    drawingContext.DrawLine(new Pen(Brushes.LightGray, 1), new Point(20, 80), new Point(180, 80));
                    drawingContext.DrawLine(new Pen(Brushes.LightGray, 1), new Point(20, 100), new Point(180, 100));
                }

                var renderTargetBitmap = new RenderTargetBitmap(200, 150, 96, 96, PixelFormats.Pbgra32);
                renderTargetBitmap.Render(drawingVisual);

                var bitmapImage = new BitmapImage();
                using (var stream = new MemoryStream())
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
                    encoder.Save(stream);
                    stream.Position = 0;
                    
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                }

                return bitmapImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create thumbnail: {ex.Message}");
                return null;
            }
        }

        private void FilterAndSortTemplates()
        {
            var searchText = SearchBox.Text?.ToLower() ?? "";
            var sortBy = ((ComboBoxItem)SortComboBox.SelectedItem)?.Content?.ToString() ?? "Name";
            
            var filtered = allTemplates.Where(t => 
                string.IsNullOrEmpty(searchText) ||
                (t.Template.Name?.ToLower().Contains(searchText) == true) ||
                (t.Template.Description?.ToLower().Contains(searchText) == true)
            );
            
            // Sort templates
            switch (sortBy)
            {
                case "Date Created":
                    filtered = filtered.OrderByDescending(t => t.Template.CreatedDate);
                    break;
                case "Date Modified":
                    filtered = filtered.OrderByDescending(t => t.Template.ModifiedDate);
                    break;
                default: // Name
                    filtered = filtered.OrderBy(t => t.Template.Name ?? "");
                    break;
            }
            
            filteredTemplates.Clear();
            foreach (var template in filtered)
            {
                filteredTemplates.Add(template);
            }
            
            // Show/hide empty state
            if (filteredTemplates.Count == 0)
            {
                ShowEmptyState();
            }
            else
            {
                HideEmptyState();
            }
        }

        private void UpdateStatus()
        {
            if (allTemplates?.Count == 0)
            {
                StatusText.Text = "No templates found";
            }
            else if (filteredTemplates.Count != allTemplates.Count)
            {
                StatusText.Text = $"Showing {filteredTemplates.Count} of {allTemplates.Count} templates";
            }
            else
            {
                StatusText.Text = $"{allTemplates.Count} template(s) loaded";
            }
        }

        private void ShowEmptyState()
        {
            EmptyState.Visibility = Visibility.Visible;
        }

        private void HideEmptyState()
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }

        // Event Handlers
        private async void RefreshTemplates_Click(object sender, RoutedEventArgs e)
        {
            await LoadTemplates();
        }

        private async void ImportTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog()
                {
                    Title = "Import Template",
                    Filter = "Template Package (*.zip)|*.zip|Template JSON (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".zip"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusText.Text = "Importing template...";
                    
                    // Import logic would go here (similar to DesignerVM)
                    // For now, just refresh after import
                    
                    await LoadTemplates();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndSortTemplates();
            UpdateStatus();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (allTemplates != null)
            {
                FilterAndSortTemplates();
            }
        }

        private void TemplateCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) // Double click
            {
                var border = sender as Border;
                var template = border?.Tag as TemplateItemViewModel;
                if (template != null)
                {
                    LoadSelectedTemplate(template);
                }
            }
        }

        private void LoadTemplate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var template = button?.Tag as TemplateItemViewModel;
            if (template != null)
            {
                LoadSelectedTemplate(template);
            }
        }

        private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var template = button?.Tag as TemplateItemViewModel;
            if (template != null)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the template '{template.Template.Name}'?\n\nThis action cannot be undone.",
                    "Delete Template",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        templateDatabase.DeleteTemplate(template.Template.Id);
                        await LoadTemplates();
                        StatusText.Text = "Template deleted successfully";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete template: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void LoadSelectedTemplate(TemplateItemViewModel template)
        {
            SelectedTemplate = template.Template;
            this.DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        // Helper methods
        private BitmapImage ByteArrayToBitmapImage(byte[] byteArray)
        {
            if (byteArray == null || byteArray.Length == 0) return null;

            try
            {
                var bitmapImage = new BitmapImage();
                using (var stream = new MemoryStream(byteArray))
                {
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                }
                return bitmapImage;
            }
            catch
            {
                return null;
            }
        }

        private byte[] BitmapImageToByteArray(BitmapImage bitmapImage)
        {
            if (bitmapImage == null) return null;

            try
            {
                using (var stream = new MemoryStream())
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
                    encoder.Save(stream);
                    return stream.ToArray();
                }
            }
            catch
            {
                return null;
            }
        }

        private BitmapImage LoadImageFromPath(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return null;

            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
            catch
            {
                return null;
            }
        }

        private string SaveThumbnailToFile(BitmapImage bitmapImage, string templateName, int templateId)
        {
            if (bitmapImage == null) return null;

            try
            {
                // Create thumbnails directory in AppData
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string thumbnailsPath = Path.Combine(appDataPath, "Photobooth", "Thumbnails");
                if (!Directory.Exists(thumbnailsPath))
                {
                    Directory.CreateDirectory(thumbnailsPath);
                }

                // Generate unique filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeName = string.Join("_", templateName.Split(Path.GetInvalidFileNameChars()));
                string thumbnailFileName = $"thumb_{safeName}_{templateId}_{timestamp}.png";
                string thumbnailPath = Path.Combine(thumbnailsPath, thumbnailFileName);

                // Save image to file
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapImage));

                using (var fileStream = new FileStream(thumbnailPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                return thumbnailPath;
            }
            catch
            {
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ViewModel for template items
    public class TemplateItemViewModel : INotifyPropertyChanged
    {
        public TemplateData Template { get; }

        private BitmapImage _thumbnailImageSource;
        public BitmapImage ThumbnailImageSource
        {
            get => _thumbnailImageSource;
            set
            {
                _thumbnailImageSource = value;
                OnPropertyChanged();
            }
        }

        private bool _hasThumbnail;
        public bool HasThumbnail
        {
            get => _hasThumbnail;
            set
            {
                _hasThumbnail = value;
                OnPropertyChanged();
            }
        }

        public string Name => Template.Name ?? "Untitled Template";
        public string Description => Template.Description ?? "No description";
        public DateTime CreatedDate => Template.CreatedDate;
        public string SizeText => $"{Template.CanvasWidth:0} × {Template.CanvasHeight:0}";

        private bool _isLastUsed;
        public bool IsLastUsed
        {
            get => _isLastUsed;
            set
            {
                _isLastUsed = value;
                OnPropertyChanged();
            }
        }

        public TemplateItemViewModel(TemplateData template)
        {
            Template = template;
            HasThumbnail = !string.IsNullOrEmpty(template.ThumbnailImagePath) && File.Exists(template.ThumbnailImagePath);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}