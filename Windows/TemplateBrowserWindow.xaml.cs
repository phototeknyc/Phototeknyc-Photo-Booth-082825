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
                    
                    // Always generate a preview; ignore stored thumbnails
                    await GenerateThumbnail(viewModel);
                    
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
                // Create a robust thumbnail from template data (background + placeholders with labels)
                var thumbnail = CreateTemplatePreview(viewModel.Template, 260, 140);
                viewModel.ThumbnailImageSource = thumbnail;
                viewModel.HasThumbnail = thumbnail != null;
                
                // Do not save thumbnails to disk anymore; we render previews dynamically
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate thumbnail for template {viewModel.Template.Id}: {ex.Message}");
                viewModel.HasThumbnail = false;
            }
        }

        private BitmapImage CreateTemplateThumbnail(TemplateData template)
        {
            // Legacy method kept for compatibility; now delegates to CreateTemplatePreview
            return CreateTemplatePreview(template, 200, 150);
        }

        // New: robust preview generator used for Template Browser cards
        private BitmapImage CreateTemplatePreview(TemplateData template, int maxWidth, int maxHeight)
        {
            try
            {
                double tW = Math.Max(1, template.CanvasWidth);
                double tH = Math.Max(1, template.CanvasHeight);
                double scale = Math.Min((double)maxWidth / tW, (double)maxHeight / tH);
                int outW = Math.Max(1, (int)(tW * scale));
                int outH = Math.Max(1, (int)(tH * scale));

                var db = new TemplateDatabase();
                var items = db.GetCanvasItems(template.Id) ?? new List<CanvasItemData>();

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // Background
                    if (!string.IsNullOrEmpty(template.BackgroundImagePath) && File.Exists(template.BackgroundImagePath))
                    {
                        var bg = LoadImageFromPath(template.BackgroundImagePath);
                        if (bg != null)
                        {
                            double imgRatio = (double)bg.PixelWidth / Math.Max(1, bg.PixelHeight);
                            double outRatio = (double)outW / Math.Max(1, outH);
                            Rect dest;
                            if (imgRatio > outRatio)
                            {
                                double h = outW / imgRatio;
                                dest = new Rect(0, (outH - h) / 2, outW, h);
                            }
                            else
                            {
                                double w = outH * imgRatio;
                                dest = new Rect((outW - w) / 2, 0, w, outH);
                            }
                            dc.DrawImage(bg, dest);
                        }
                        else
                        {
                            dc.DrawRectangle(new SolidColorBrush(Colors.Black), null, new Rect(0, 0, outW, outH));
                        }
                    }
                    else
                    {
                        var brush = new LinearGradientBrush(Colors.DimGray, Colors.Black, 90);
                        dc.DrawRectangle(brush, null, new Rect(0, 0, outW, outH));
                    }

                    // Draw all items (image, placeholder, text, shape) in Z order
                    int i = 0;
                    foreach (var item in items.OrderBy(x => x.ZIndex))
                    {
                        double x = item.X * scale;
                        double y = item.Y * scale;
                        double w = Math.Max(1, item.Width * scale);
                        double h = Math.Max(1, item.Height * scale);
                        var rect = new Rect(x, y, w, h);

                        if (item.Rotation != 0)
                        {
                            dc.PushTransform(new RotateTransform(item.Rotation, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
                        }

                        switch (item.ItemType)
                        {
                            case "Image":
                                if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                                {
                                    try
                                    {
                                        var img = LoadImageFromPath(item.ImagePath);
                                        if (img != null) dc.DrawImage(img, rect);
                                    }
                                    catch { }
                                }
                                break;

                            case "Placeholder":
                                Color col;
                                if (!string.IsNullOrEmpty(item.PlaceholderColor))
                                {
                                    try { col = (Color)ColorConverter.ConvertFromString(item.PlaceholderColor); }
                                    catch { col = GetPaletteColor(item.PlaceholderNumber ?? (i + 1)); }
                                }
                                else
                                {
                                    col = GetPaletteColor(item.PlaceholderNumber ?? (i + 1));
                                }
                                var geom = new RectangleGeometry(rect, 6, 6);
                                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(220, col.R, col.G, col.B)),
                                               new Pen(new SolidColorBrush(Colors.White), 2), geom);
                                int n = item.PlaceholderNumber ?? (i + 1);
                                var ft = new FormattedText(
                                    $"Photo {n}",
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    FlowDirection.LeftToRight,
                                    new Typeface("Segoe UI"),
                                    Math.Min(w, h) * 0.16,
                                    new SolidColorBrush(Color.FromRgb(50, 50, 50)), 96);
                                dc.DrawText(ft, new Point(rect.Left + (rect.Width - ft.Width) / 2, rect.Top + (rect.Height - ft.Height) / 2));
                                break;

                            case "Text":
                                var tf = new Typeface(
                                    new FontFamily(string.IsNullOrEmpty(item.FontFamily) ? "Segoe UI" : item.FontFamily),
                                    item.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                                    item.IsBold ? FontWeights.Bold : FontWeights.Normal,
                                    FontStretches.Normal);
                                Brush brush = new SolidColorBrush(Colors.Black);
                                if (!string.IsNullOrEmpty(item.TextColor))
                                {
                                    try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.TextColor)); } catch { }
                                }
                                double fs = Math.Max(8, (item.FontSize ?? 20) * scale);
                                var t = new FormattedText(item.Text ?? string.Empty,
                                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                    tf, fs, brush, 96);
                                t.MaxTextWidth = rect.Width; t.MaxTextHeight = rect.Height;
                                dc.DrawText(t, new Point(rect.Left, rect.Top));
                                break;

                            case "Shape":
                                Brush fill = Brushes.Transparent; Pen pen = null;
                                if (!string.IsNullOrEmpty(item.FillColor)) { try { fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.FillColor)); } catch { } }
                                if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0) { try { pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.StrokeColor)), item.StrokeThickness); } catch { } }
                                var st = item.ShapeType?.Trim()?.ToLowerInvariant();
                                if (st == "circle" || st == "ellipse")
                                    dc.DrawEllipse(fill, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
                                else
                                    dc.DrawRectangle(fill, pen, rect);
                                break;
                        }

                        if (item.Rotation != 0) dc.Pop();
                        i++;
                    }
                }

                var rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create template preview: {ex.Message}");
                return null;
            }
        }

        private System.Windows.Media.Color GetPaletteColor(int n)
        {
            System.Windows.Media.Color[] palette = new System.Windows.Media.Color[]
            {
                System.Windows.Media.Color.FromRgb(255,182,193), // Light Pink
                System.Windows.Media.Color.FromRgb(173,216,230), // Light Blue
                System.Windows.Media.Color.FromRgb(144,238,144), // Light Green
                System.Windows.Media.Color.FromRgb(255,228,181), // Peach
                System.Windows.Media.Color.FromRgb(221,160,221), // Plum
                System.Windows.Media.Color.FromRgb(240,230,140), // Khaki
            };
            int idx = Math.Max(0, (n - 1) % palette.Length);
            return palette[idx];
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
