using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Converters
{
    /// <summary>
    /// Converter that handles null image paths gracefully
    /// Returns transparent image for null/empty paths instead of throwing exceptions
    /// </summary>
    public class NullImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var path = value as string;
                
                // Return null for empty paths - WPF Image control handles null gracefully
                if (string.IsNullOrEmpty(path))
                {
                    return null;
                }
                
                // Check if file exists
                if (!System.IO.File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine($"NullImageConverter: File not found: {path}");
                    return null;
                }
                
                // Load the image with more robust handling
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                
                // Try absolute path first
                try
                {
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                }
                catch (UriFormatException)
                {
                    // If absolute path fails, try relative
                    bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                }
                
                bitmap.EndInit();
                bitmap.Freeze();
                
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NullImageConverter: Error loading image: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}