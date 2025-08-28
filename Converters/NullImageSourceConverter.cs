using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Converters
{
    /// <summary>
    /// Converter that handles null image sources to prevent binding errors
    /// Returns a transparent placeholder image for null or empty paths
    /// </summary>
    public class NullImageSourceConverter : IValueConverter
    {
        private static readonly BitmapImage PlaceholderImage = CreatePlaceholderImage();

        private static BitmapImage CreatePlaceholderImage()
        {
            // Create a 1x1 transparent image as placeholder
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri("pack://application:,,,/Photobooth;component/Images/placeholder.png", UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze(); // Freeze for performance
            return bitmap;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Handle null or empty string
                if (value == null || string.IsNullOrWhiteSpace(value?.ToString()))
                {
                    return DependencyProperty.UnsetValue; // Let WPF handle with fallback
                }

                string imagePath = value.ToString();

                // Check if file exists
                if (!System.IO.File.Exists(imagePath))
                {
                    System.Diagnostics.Debug.WriteLine($"NullImageSourceConverter: File not found: {imagePath}");
                    return DependencyProperty.UnsetValue;
                }

                // Create and return BitmapImage
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NullImageSourceConverter: Error loading image: {ex.Message}");
                return DependencyProperty.UnsetValue;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for paths that might be null - returns empty string instead of null
    /// </summary>
    public class NullToEmptyStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}