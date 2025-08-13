using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
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
                SelectedTemplate = viewModel.TemplateData;
                this.DialogResult = true;
                Close();
            }
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
                if (!string.IsNullOrEmpty(TemplateData.ThumbnailImagePath) && File.Exists(TemplateData.ThumbnailImagePath))
                {
                    ThumbnailImageSource = LoadImageFromPath(TemplateData.ThumbnailImagePath);
                }
                else
                {
                    // Generate a simple thumbnail
                    ThumbnailImageSource = CreateSimpleThumbnail();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load thumbnail: {ex.Message}");
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}