using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Photobooth.Adorners
{
    public class SizeAdorner : Adorner
    {
        private SizeChrome chrome;
        private VisualCollection visuals;
        private ContentControl designerItem;

        protected override int VisualChildrenCount
        {
            get
            {
                return this.visuals.Count;
            }
        }

        public SizeAdorner(ContentControl designerItem)
            : base(designerItem)
        {
            this.SnapsToDevicePixels = true;
            this.designerItem = designerItem;
            this.chrome = new SizeChrome();
            this.chrome.DataContext = designerItem;
            
            // Calculate actual pixel dimensions
            CalculateActualPixelDimensions();
            
            this.visuals = new VisualCollection(this);
            this.visuals.Add(this.chrome);
        }
        
        private void CalculateActualPixelDimensions()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SizeAdorner: Calculating dimensions for item W={designerItem.Width}, H={designerItem.Height}");
                
                // Get the canvas to calculate the scale
                var mainPage = Photobooth.Pages.MainPage.Instance;
                var canvas = mainPage?.ViewModel?.CustomDesignerCanvas as DesignerCanvas.Controls.DesignerCanvas;
                
                if (canvas != null && canvas.ActualPixelWidth > 0 && canvas.ActualWidth > 0)
                {
                    // Calculate the scale factor
                    double displayScale = canvas.ActualWidth / canvas.ActualPixelWidth;
                    
                    // Set actual pixel dimensions
                    chrome.ActualPixelWidth = designerItem.Width / displayScale;
                    chrome.ActualPixelHeight = designerItem.Height / displayScale;
                    
                    System.Diagnostics.Debug.WriteLine($"SizeAdorner: Canvas found. Scale={displayScale}, ActualPixels: W={chrome.ActualPixelWidth:F0}, H={chrome.ActualPixelHeight:F0}");
                }
                else
                {
                    // For a 2x6 template (600x1800), if displayed as ~261x783, scale is 261/600 = 0.435
                    // So to get actual pixels from display, we divide by scale: display / 0.435 = actual
                    // But we don't know the exact template size, so we'll use a fixed scale based on observation
                    // 261 display -> 600 actual means scale factor of 600/261 = 2.3
                    double estimatedScale = 2.3;
                    chrome.ActualPixelWidth = designerItem.Width * estimatedScale;
                    chrome.ActualPixelHeight = designerItem.Height * estimatedScale;
                    
                    System.Diagnostics.Debug.WriteLine($"SizeAdorner: Using estimated scale. ActualPixels: W={chrome.ActualPixelWidth:F0}, H={chrome.ActualPixelHeight:F0}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SizeAdorner: Error - {ex.Message}");
                // If anything fails, just use the display dimensions
                chrome.ActualPixelWidth = designerItem.Width;
                chrome.ActualPixelHeight = designerItem.Height;
            }
        }

        protected override Visual GetVisualChild(int index)
        {
            return this.visuals[index];
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            // Recalculate actual pixel dimensions on each arrange
            CalculateActualPixelDimensions();
            
            this.chrome.Arrange(new Rect(new Point(0.0, 0.0), arrangeBounds));
            return arrangeBounds;
        }
    }
}
