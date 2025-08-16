using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Database;
using Photobooth.Services;

namespace Photobooth.Windows
{
    public partial class PhotoboothTemplateSelectorWindow : Window, INotifyPropertyChanged
    {
        private readonly PhotoboothService photoboothService;
        
        public TemplateData SelectedTemplate { get; private set; }
        public ObservableCollection<PhotoboothTemplateViewModel> Templates { get; set; }
        
        private string _eventTitle;
        public string EventTitle
        {
            get => _eventTitle;
            set
            {
                _eventTitle = value;
                OnPropertyChanged();
            }
        }

        public PhotoboothTemplateSelectorWindow(List<TemplateData> templates, EventData eventData)
        {
            InitializeComponent();
            DataContext = this;
            
            photoboothService = new PhotoboothService();
            
            EventTitle = $"{eventData.Name} - {eventData.EventType}";
            
            Templates = new ObservableCollection<PhotoboothTemplateViewModel>();
            
            foreach (var template in templates)
            {
                var viewModel = new PhotoboothTemplateViewModel(template, photoboothService);
                Templates.Add(viewModel);
            }
        }

        private void TemplateCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is PhotoboothTemplateViewModel viewModel)
            {
                // Show large preview instead of immediately selecting
                ShowLargePreview(viewModel);
                e.Handled = true;
            }
        }
        
        private void ShowLargePreview(PhotoboothTemplateViewModel viewModel)
        {
            // Update the selected template
            SelectedTemplate = viewModel.TemplateData;
            
            // Update the large preview UI
            LargePreviewImage.Source = viewModel.ThumbnailImageSource ?? GenerateLargerPreview(viewModel.TemplateData);
            LargePreviewTitle.Text = viewModel.Name;
            LargePreviewDescription.Text = viewModel.Description;
            
            // Show the overlay
            PreviewOverlay.Visibility = Visibility.Visible;
        }
        
        private BitmapImage GenerateLargerPreview(TemplateData template)
        {
            try
            {
                // First try to load the actual template background image at larger size
                if (!string.IsNullOrEmpty(template.BackgroundImagePath) && System.IO.File.Exists(template.BackgroundImagePath))
                {
                    var backgroundImage = new BitmapImage();
                    backgroundImage.BeginInit();
                    backgroundImage.CacheOption = BitmapCacheOption.OnLoad;
                    backgroundImage.UriSource = new Uri(template.BackgroundImagePath, UriKind.Absolute);
                    
                    // Larger size for preview
                    double maxWidth = 600;
                    double maxHeight = 400;
                    double aspectRatio = template.CanvasWidth / template.CanvasHeight;
                    
                    if (aspectRatio > maxWidth / maxHeight)
                    {
                        backgroundImage.DecodePixelWidth = (int)maxWidth;
                    }
                    else
                    {
                        backgroundImage.DecodePixelHeight = (int)maxHeight;
                    }
                    
                    backgroundImage.EndInit();
                    backgroundImage.Freeze();
                    return backgroundImage;
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        private void PreviewOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            // Hide overlay if clicking on the background
            if (e.OriginalSource == sender)
            {
                PreviewOverlay.Visibility = Visibility.Collapsed;
            }
        }
        
        private void StartSession_Click(object sender, RoutedEventArgs e)
        {
            // Start the session with the selected template
            this.DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PhotoboothTemplateViewModel : INotifyPropertyChanged
    {
        public TemplateData TemplateData { get; }
        private readonly PhotoboothService photoboothService;

        public PhotoboothTemplateViewModel(TemplateData templateData, PhotoboothService photoboothService)
        {
            TemplateData = templateData;
            this.photoboothService = photoboothService;
            
            LoadThumbnail();
        }

        public string Name => TemplateData.Name ?? "Untitled Template";
        public string Description => TemplateData.Description ?? "No description available";

        private int _photoCount = -1;
        public string PhotoCountText 
        { 
            get 
            {
                if (_photoCount == -1)
                    _photoCount = photoboothService.GetTemplatePhotoCount(TemplateData);
                
                return _photoCount == 1 ? "1 Photo" : $"{_photoCount} Photos";
            }
        }

        private BitmapImage _thumbnailImageSource;
        public BitmapImage ThumbnailImageSource
        {
            get => _thumbnailImageSource;
            set
            {
                _thumbnailImageSource = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }

        public bool HasThumbnail => ThumbnailImageSource != null;

        private void LoadThumbnail()
        {
            try
            {
                // Load the stored thumbnail or background image
                if (!string.IsNullOrEmpty(TemplateData.ThumbnailImagePath) && File.Exists(TemplateData.ThumbnailImagePath))
                {
                    ThumbnailImageSource = LoadImageFromPath(TemplateData.ThumbnailImagePath);
                }
                else if (!string.IsNullOrEmpty(TemplateData.BackgroundImagePath) && File.Exists(TemplateData.BackgroundImagePath))
                {
                    ThumbnailImageSource = LoadImageFromPath(TemplateData.BackgroundImagePath);
                }
                else
                {
                    // Generate a thumbnail from the actual template
                    ThumbnailImageSource = GenerateTemplateThumbnail();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load thumbnail: {ex.Message}");
                // Fallback to simple thumbnail if generation fails
                ThumbnailImageSource = CreateSimpleThumbnail();
            }
        }

        private BitmapImage CreateSimpleThumbnail()
        {
            try
            {
                // Create a simple colored rectangle as thumbnail
                var width = 200;
                var height = 150;
                
                var bitmap = new WriteableBitmap(width, height, 96, 96, 
                    System.Windows.Media.PixelFormats.Bgr32, null);
                
                // Fill with a gradient or solid color
                var stride = width * 4;
                var pixels = new byte[height * stride];
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var offset = y * stride + x * 4;
                        pixels[offset] = 100;     // Blue
                        pixels[offset + 1] = 120; // Green
                        pixels[offset + 2] = 200; // Red
                        pixels[offset + 3] = 255; // Alpha
                    }
                }
                
                bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
                
                return BitmapToBitmapImage(bitmap);
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

        private BitmapImage BitmapToBitmapImage(WriteableBitmap writeableBitmap)
        {
            try
            {
                using (var stream = new MemoryStream())
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(writeableBitmap));
                    encoder.Save(stream);
                    stream.Position = 0;

                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    return bitmapImage;
                }
            }
            catch
            {
                return null;
            }
        }

        private BitmapImage GenerateTemplateThumbnail()
        {
            try
            {
                // Generate a colorful preview for each template type
                double templateWidth = TemplateData.CanvasWidth;
                double templateHeight = TemplateData.CanvasHeight;
                double maxThumbWidth = 240;
                double maxThumbHeight = 160;
                
                // Calculate scale to fit within max bounds while maintaining aspect ratio
                double scale = Math.Min(maxThumbWidth / templateWidth, maxThumbHeight / templateHeight);
                int thumbWidth = (int)(templateWidth * scale);
                int thumbHeight = (int)(templateHeight * scale);
                
                // Create a drawing visual to render the template
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // Generate unique colors based on template name/ID
                    var random = new Random(TemplateData.Id);
                    var bgColors = new[]
                    {
                        System.Windows.Media.Color.FromRgb(255, 182, 193), // Light Pink
                        System.Windows.Media.Color.FromRgb(135, 206, 235), // Sky Blue
                        System.Windows.Media.Color.FromRgb(152, 251, 152), // Pale Green
                        System.Windows.Media.Color.FromRgb(255, 218, 185), // Peach
                        System.Windows.Media.Color.FromRgb(221, 160, 221), // Plum
                        System.Windows.Media.Color.FromRgb(240, 230, 140)  // Khaki
                    };
                    
                    // Draw gradient background
                    var gradientBrush = new System.Windows.Media.LinearGradientBrush(
                        bgColors[random.Next(bgColors.Length)],
                        bgColors[random.Next(bgColors.Length)],
                        0);
                    
                    dc.DrawRectangle(gradientBrush, null, new Rect(0, 0, thumbWidth, thumbHeight));
                    
                    // Get template items from database
                    var database = new TemplateDatabase();
                    var items = database.GetCanvasItems(TemplateData.Id);
                    
                    // If no items, create default layout based on template name
                    if (items == null || !items.Any())
                    {
                        items = CreateDefaultItems(TemplateData);
                    }
                    
                    // Draw colorful placeholder areas
                    var placeholderColors = new[]
                    {
                        System.Windows.Media.Color.FromArgb(200, 255, 192, 203), // Pink with transparency
                        System.Windows.Media.Color.FromArgb(200, 173, 216, 230), // Light Blue
                        System.Windows.Media.Color.FromArgb(200, 144, 238, 144), // Light Green
                        System.Windows.Media.Color.FromArgb(200, 255, 228, 181), // Moccasin
                    };
                    
                    int colorIndex = 0;
                    foreach (var item in items.Where(i => i.ItemType == "Placeholder"))
                    {
                        double x = item.X * scale;
                        double y = item.Y * scale;
                        double width = item.Width * scale;
                        double height = item.Height * scale;
                        
                        // Draw colorful placeholder with rounded corners
                        var placeholderBrush = new System.Windows.Media.SolidColorBrush(
                            placeholderColors[colorIndex % placeholderColors.Length]);
                        
                        // Create rounded rectangle geometry
                        var rect = new System.Windows.Media.RectangleGeometry(new Rect(x, y, width, height));
                        rect.RadiusX = 5;
                        rect.RadiusY = 5;
                        
                        dc.DrawGeometry(
                            placeholderBrush,
                            new System.Windows.Media.Pen(
                                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)), 
                                2),
                            rect);
                        
                        // Add "Picture X" text
                        var text = $"Picture {colorIndex + 1}";
                        var formattedText = new System.Windows.Media.FormattedText(
                            text,
                            System.Globalization.CultureInfo.CurrentCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            new System.Windows.Media.Typeface("Segoe UI"),
                            Math.Min(width, height) * 0.15,
                            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)),
                            96);
                        
                        dc.DrawText(formattedText, 
                            new System.Windows.Point(x + (width - formattedText.Width) / 2, 
                                                    y + (height - formattedText.Height) / 2));
                        
                        colorIndex++;
                    }
                }
                
                // Render visual to bitmap
                var renderBitmap = new RenderTargetBitmap(
                    thumbWidth, thumbHeight,
                    96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                renderBitmap.Render(visual);
                
                // Convert to BitmapImage
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                
                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    stream.Position = 0;
                    
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = stream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    
                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate template thumbnail: {ex.Message}");
                return null;
            }
        }

        private List<Database.CanvasItemData> CreateDefaultItems(TemplateData template)
        {
            var items = new List<Database.CanvasItemData>();
            string nameLower = template.Name?.ToLower() ?? "";
            
            // Create default layouts based on template type
            if (nameLower.Contains("strip") || nameLower.Contains("4x1"))
            {
                // Vertical strip - 4 photos
                for (int i = 0; i < 4; i++)
                {
                    items.Add(new Database.CanvasItemData
                    {
                        ItemType = "Placeholder",
                        X = template.CanvasWidth * 0.1,
                        Y = template.CanvasHeight * 0.05 + (i * template.CanvasHeight * 0.235),
                        Width = template.CanvasWidth * 0.8,
                        Height = template.CanvasHeight * 0.22
                    });
                }
            }
            else if (nameLower.Contains("2x2") || nameLower.Contains("grid"))
            {
                // 2x2 grid
                for (int row = 0; row < 2; row++)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        items.Add(new Database.CanvasItemData
                        {
                            ItemType = "Placeholder",
                            X = template.CanvasWidth * (0.1 + col * 0.45),
                            Y = template.CanvasHeight * (0.1 + row * 0.45),
                            Width = template.CanvasWidth * 0.35,
                            Height = template.CanvasHeight * 0.35
                        });
                    }
                }
            }
            else
            {
                // Default single photo
                items.Add(new Database.CanvasItemData
                {
                    ItemType = "Placeholder",
                    X = template.CanvasWidth * 0.1,
                    Y = template.CanvasHeight * 0.1,
                    Width = template.CanvasWidth * 0.8,
                    Height = template.CanvasHeight * 0.8
                });
            }
            
            return items;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}