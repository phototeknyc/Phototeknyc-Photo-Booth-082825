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
            
            // Update the large preview UI using a full render of the template
            LargePreviewImage.Source = GenerateLargerPreview(viewModel.TemplateData) ?? viewModel.ThumbnailImageSource;
            LargePreviewTitle.Text = viewModel.Name;
            LargePreviewDescription.Text = viewModel.Description;
            
            // Show the overlay
            PreviewOverlay.Visibility = Visibility.Visible;
        }
        
        private BitmapImage GenerateLargerPreview(TemplateData template)
        {
            try
            {
                // Render full template (background + items) at a larger size
                double maxWidth = 700;
                double maxHeight = 500;
                double tW = Math.Max(1, template.CanvasWidth);
                double tH = Math.Max(1, template.CanvasHeight);
                double scale = Math.Min(maxWidth / tW, maxHeight / tH);
                int outW = Math.Max(1, (int)(tW * scale));
                int outH = Math.Max(1, (int)(tH * scale));

                var db = new TemplateDatabase();
                var items = db.GetCanvasItems(template.Id) ?? new List<Database.CanvasItemData>();

                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // Background
                    if (!string.IsNullOrEmpty(template.BackgroundImagePath) && File.Exists(template.BackgroundImagePath))
                    {
                        var bg = new BitmapImage();
                        bg.BeginInit();
                        bg.CacheOption = BitmapCacheOption.OnLoad;
                        bg.UriSource = new Uri(template.BackgroundImagePath, UriKind.Absolute);
                        bg.EndInit();
                        bg.Freeze();

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
                        var brush = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Colors.DimGray, System.Windows.Media.Colors.Black, 90);
                        dc.DrawRectangle(brush, null, new Rect(0, 0, outW, outH));
                    }

                    // Draw items by Z order
                    int i = 0;
                    foreach (var item in items.OrderBy(x => x.ZIndex))
                    {
                        double x = item.X * scale;
                        double y = item.Y * scale;
                        double w = Math.Max(1, item.Width * scale);
                        double h = Math.Max(1, item.Height * scale);
                        var rect = new Rect(x, y, w, h);

                        if (item.Rotation != 0)
                            dc.PushTransform(new System.Windows.Media.RotateTransform(item.Rotation, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));

                        switch (item.ItemType)
                        {
                            case "Image":
                                if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                                {
                                    try
                                    {
                                        var img = new BitmapImage(new Uri(item.ImagePath, UriKind.Absolute));
                                        dc.DrawImage(img, rect);
                                    }
                                    catch { }
                                }
                                break;
                            case "Placeholder":
                                System.Windows.Media.Color col;
                                if (!string.IsNullOrEmpty(item.PlaceholderColor))
                                {
                                    try { col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.PlaceholderColor); }
                                    catch { col = System.Windows.Media.Color.FromRgb(255,182,193); }
                                }
                                else
                                {
                                    col = System.Windows.Media.Color.FromRgb(255,182,193);
                                }
                                var rounded = new System.Windows.Media.RectangleGeometry(rect, 6, 6);
                                dc.DrawGeometry(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, col.R, col.G, col.B)),
                                               new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White), 2), rounded);
                                int n = item.PlaceholderNumber ?? (i + 1);
                                var ft = new System.Windows.Media.FormattedText(
                                    $"Photo {n}",
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    System.Windows.FlowDirection.LeftToRight,
                                    new System.Windows.Media.Typeface("Segoe UI"),
                                    Math.Min(w, h) * 0.16,
                                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)), 96);
                                dc.DrawText(ft, new System.Windows.Point(rect.Left + (rect.Width - ft.Width) / 2, rect.Top + (rect.Height - ft.Height) / 2));
                                break;
                            case "Text":
                                var tf = new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily(string.IsNullOrEmpty(item.FontFamily) ? "Segoe UI" : item.FontFamily),
                                    item.IsItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal,
                                    item.IsBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
                                    System.Windows.FontStretches.Normal);
                                System.Windows.Media.Brush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
                                if (!string.IsNullOrEmpty(item.TextColor))
                                {
                                    try { brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.TextColor)); } catch { }
                                }
                                double fs = Math.Max(8, (item.FontSize ?? 20) * scale);
                                var t = new System.Windows.Media.FormattedText(item.Text ?? string.Empty,
                                    System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                                    tf, fs, brush, 96);
                                t.MaxTextWidth = rect.Width; t.MaxTextHeight = rect.Height;
                                dc.DrawText(t, new System.Windows.Point(rect.Left, rect.Top));
                                break;
                            case "Shape":
                                System.Windows.Media.Brush fill = System.Windows.Media.Brushes.Transparent; System.Windows.Media.Pen pen = null;
                                if (!string.IsNullOrEmpty(item.FillColor)) { try { fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.FillColor)); } catch { } }
                                if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0) { try { pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.StrokeColor)), item.StrokeThickness); } catch { } }
                                dc.DrawRectangle(fill, pen, rect);
                                break;
                        }

                        if (item.Rotation != 0) dc.Pop();
                        i++;
                    }
                }

                var rtb = new RenderTargetBitmap(outW, outH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
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
                // Always generate a dynamic preview of the full template (ignore saved thumbnails)
                ThumbnailImageSource = GenerateTemplateThumbnail();
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
                // Render full template preview at card size
                double templateWidth = TemplateData.CanvasWidth;
                double templateHeight = TemplateData.CanvasHeight;
                double maxThumbWidth = 240;
                double maxThumbHeight = 160;

                double scale = Math.Min(maxThumbWidth / templateWidth, maxThumbHeight / templateHeight);
                int thumbWidth = (int)(templateWidth * scale);
                int thumbHeight = (int)(templateHeight * scale);

                var db = new TemplateDatabase();
                var items = db.GetCanvasItems(TemplateData.Id) ?? new List<Database.CanvasItemData>();

                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // Background
                    if (!string.IsNullOrEmpty(TemplateData.BackgroundImagePath) && File.Exists(TemplateData.BackgroundImagePath))
                    {
                        var bg = new BitmapImage();
                        bg.BeginInit();
                        bg.CacheOption = BitmapCacheOption.OnLoad;
                        bg.UriSource = new Uri(TemplateData.BackgroundImagePath, UriKind.Absolute);
                        bg.EndInit();
                        bg.Freeze();

                        double imgRatio = (double)bg.PixelWidth / Math.Max(1, bg.PixelHeight);
                        double outRatio = (double)thumbWidth / Math.Max(1, thumbHeight);
                        Rect dest;
                        if (imgRatio > outRatio)
                        {
                            double h = thumbWidth / imgRatio;
                            dest = new Rect(0, (thumbHeight - h) / 2, thumbWidth, h);
                        }
                        else
                        {
                            double w = thumbHeight * imgRatio;
                            dest = new Rect((thumbWidth - w) / 2, 0, w, thumbHeight);
                        }
                        dc.DrawImage(bg, dest);
                    }
                    else
                    {
                        var brush = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Colors.DimGray, System.Windows.Media.Colors.Black, 90);
                        dc.DrawRectangle(brush, null, new Rect(0, 0, thumbWidth, thumbHeight));
                    }

                    int i = 0;
                    foreach (var item in items.OrderBy(it => it.ZIndex))
                    {
                        double x = item.X * scale;
                        double y = item.Y * scale;
                        double width = Math.Max(1, item.Width * scale);
                        double height = Math.Max(1, item.Height * scale);
                        var rect = new Rect(x, y, width, height);

                        if (item.Rotation != 0)
                            dc.PushTransform(new System.Windows.Media.RotateTransform(item.Rotation, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));

                        switch (item.ItemType)
                        {
                            case "Image":
                                if (!string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                                {
                                    try
                                    {
                                        var img = new BitmapImage(new Uri(item.ImagePath, UriKind.Absolute));
                                        dc.DrawImage(img, rect);
                                    }
                                    catch { }
                                }
                                break;
                            case "Placeholder":
                                var pc = System.Windows.Media.Colors.LightPink;
                                if (!string.IsNullOrEmpty(item.PlaceholderColor))
                                {
                                    try { pc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.PlaceholderColor); } catch { }
                                }
                                var rg = new System.Windows.Media.RectangleGeometry(rect, 6, 6);
                                dc.DrawGeometry(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, pc.R, pc.G, pc.B)),
                                               new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White), 2), rg);
                                int n = item.PlaceholderNumber ?? (i + 1);
                                var ft = new System.Windows.Media.FormattedText($"Photo {n}",
                                    System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                                    new System.Windows.Media.Typeface("Segoe UI"), Math.Min(width, height) * 0.16,
                                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50,50,50)), 96);
                                dc.DrawText(ft, new System.Windows.Point(rect.Left + (rect.Width - ft.Width) / 2, rect.Top + (rect.Height - ft.Height) / 2));
                                break;
                            case "Text":
                                var tf = new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily(string.IsNullOrEmpty(item.FontFamily) ? "Segoe UI" : item.FontFamily),
                                    item.IsItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal,
                                    item.IsBold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
                                    System.Windows.FontStretches.Normal);
                                var tb = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
                                if (!string.IsNullOrEmpty(item.TextColor)) { try { tb = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.TextColor)); } catch { }
                                }
                                double fs = Math.Max(8, (item.FontSize ?? 20) * scale);
                                var fmt = new System.Windows.Media.FormattedText(item.Text ?? string.Empty,
                                    System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                                    tf, fs, tb, 96);
                                fmt.MaxTextWidth = rect.Width; fmt.MaxTextHeight = rect.Height;
                                dc.DrawText(fmt, new System.Windows.Point(rect.Left, rect.Top));
                                break;
                            case "Shape":
                                System.Windows.Media.Brush fill = System.Windows.Media.Brushes.Transparent; System.Windows.Media.Pen pen = null;
                                if (!string.IsNullOrEmpty(item.FillColor)) { try { fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.FillColor)); } catch { }
                                }
                                if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0) { try { pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.StrokeColor)), item.StrokeThickness); } catch { }
                                }
                                dc.DrawRectangle(fill, pen, rect);
                                break;
                        }

                        if (item.Rotation != 0) dc.Pop();
                        i++;
                    }
                }

                var renderBitmap = new RenderTargetBitmap(thumbWidth, thumbHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                renderBitmap.Render(visual);

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
