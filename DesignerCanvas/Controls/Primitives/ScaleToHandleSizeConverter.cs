using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DesignerCanvas.Controls.Primitives
{
    public class ScaleToHandleSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double scale = value is double ? (double)value : 1.0;
            double baseSize = parameter is string paramStr && double.TryParse(paramStr, out double param) ? param : 7.0;
            
            if (scale <= 0) scale = 1.0;
            
            // Calculate inverse scale size: smaller scale = larger handles
            double adjustedSize = baseSize / scale;
            
            // Clamp between reasonable limits
            adjustedSize = Math.Max(3.0, Math.Min(adjustedSize, 30.0));
            
            // Debug output
            System.Diagnostics.Debug.WriteLine($"ScaleToHandleSizeConverter: scale={scale:F3}, baseSize={baseSize}, adjustedSize={adjustedSize:F1}");
            
            return adjustedSize;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}