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
        
        // Dependency property for actual pixel width
        public static readonly DependencyProperty ActualPixelWidthProperty =
            DependencyProperty.Register("ActualPixelWidth", typeof(double), typeof(SizeChrome), new PropertyMetadata(0.0));
            
        public double ActualPixelWidth
        {
            get { return (double)GetValue(ActualPixelWidthProperty); }
            set 
            { 
                SetValue(ActualPixelWidthProperty, value);
                System.Diagnostics.Debug.WriteLine($"SizeChrome: ActualPixelWidth set to {value}");
            }
        }
        
        // Dependency property for actual pixel height  
        public static readonly DependencyProperty ActualPixelHeightProperty =
            DependencyProperty.Register("ActualPixelHeight", typeof(double), typeof(SizeChrome), new PropertyMetadata(0.0));
            
        public double ActualPixelHeight
        {
            get { return (double)GetValue(ActualPixelHeightProperty); }
            set 
            { 
                SetValue(ActualPixelHeightProperty, value);
                System.Diagnostics.Debug.WriteLine($"SizeChrome: ActualPixelHeight set to {value}");
            }
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

    public class ActualPixelSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double displaySize)
            {
                try
                {
                    // Get the canvas from the main window to calculate scale
                    var mainPage = Photobooth.Pages.MainPage.Instance;
                    var canvas = mainPage?.ViewModel?.CustomDesignerCanvas as DesignerCanvas.Controls.DesignerCanvas;
                    
                    if (canvas != null)
                    {
                        // Debug output - always show even if no console
                        string debugMsg = $"ActualPixelSizeConverter: displaySize={displaySize}, ActualPixelWidth={canvas.ActualPixelWidth}, ActualWidth={canvas.ActualWidth}";
                        System.Diagnostics.Debug.WriteLine(debugMsg);
                        Console.WriteLine(debugMsg);
                        
                        if (canvas.ActualPixelWidth > 0 && canvas.ActualWidth > 0)
                        {
                            // Calculate the scale factor (display to actual pixels)
                            double displayScale = canvas.ActualWidth / canvas.ActualPixelWidth;
                            
                            // Convert display size to actual pixels
                            double actualPixels = displaySize / displayScale;
                            
                            System.Diagnostics.Debug.WriteLine($"Scale={displayScale}, actualPixels={actualPixels}");
                            
                            return Math.Round(actualPixels).ToString() + "px";
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Canvas is null!");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ActualPixelSizeConverter error: {ex.Message}");
                }
                
                // Fallback: Use fixed 300 DPI calculation
                // Assuming the canvas is scaled to fit the screen but maintains aspect ratio
                // For a 2x6 template (600x1800px), if displayed as 257.6 width, the scale is 257.6/600 = 0.429
                // So we need to multiply by approximately 2.33
                double estimatedScale = 600.0 / 257.6; // This assumes 2x6 template
                return Math.Round(displaySize * estimatedScale).ToString() + "px";
            }
            
            return "0px";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
