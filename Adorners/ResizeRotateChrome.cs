using System.Windows;
using System.Windows.Controls;

namespace Photobooth.Adorners
{
    public class ResizeRotateChrome : Control
    {
        static ResizeRotateChrome()
        {
            FrameworkElement.DefaultStyleKeyProperty.OverrideMetadata(typeof(ResizeRotateChrome), new FrameworkPropertyMetadata(typeof(ResizeRotateChrome)));
        }
        
        // Scale property for handle sizing
        public static readonly DependencyProperty HandleScaleProperty =
            DependencyProperty.Register("HandleScale", typeof(double), typeof(ResizeRotateChrome), 
                new PropertyMetadata(1.0));

        public double HandleScale
        {
            get { return (double)GetValue(HandleScaleProperty); }
            set { SetValue(HandleScaleProperty, value); }
        }
    }
}
