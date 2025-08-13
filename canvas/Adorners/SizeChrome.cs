using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Photobooth.Adorners
{
    public class SizeChrome : Control
    {
        static SizeChrome()
        {
            FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(SizeChrome), new FrameworkPropertyMetadata(typeof(SizeChrome)));
        }
    }

    public class DoubleFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double d = (double)value;
            return Math.Round(d);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
    
    public class SimplePixelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            System.Diagnostics.Debug.WriteLine($"SimplePixelConverter invoked with value: {value}");
            
            if (value is double displaySize)
            {
                // For 2x6: canvas shows as 261x783, actual is 600x1800
                // Scale factor is 600/261 = 2.299
                double actualPixels = displaySize * 2.299;
                string result = Math.Round(actualPixels).ToString() + "px";
                System.Diagnostics.Debug.WriteLine($"SimplePixelConverter: {displaySize} -> {result}");
                return result;
            }
            System.Diagnostics.Debug.WriteLine("SimplePixelConverter: value is not double");
            return "0px";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
