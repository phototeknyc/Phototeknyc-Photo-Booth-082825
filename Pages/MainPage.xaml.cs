using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using Photobooth.MVVM.ViewModels.Designer;
using Photobooth.Services;

namespace Photobooth.Pages
{
    /// <summary>
    /// Interaction logic for MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        private static MainPage _instance;
        public static MainPage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MainPage();
                }
                return _instance;
            }
        }

        public DesignerVM ViewModel { get; private set; }

        public MainPage()
        {
            DebugService.LogDebug("MainPage constructor called");
            InitializeComponent();
            ViewModel = new DesignerVM();
            DataContext = ViewModel;
            // Don't override the default ratio set in ViewModel constructor
            // ViewModel.CustomDesignerCanvas.SetRatio(2, 3);
            _instance = this;
            
            // Set up canvas scaling
            this.Loaded += (s, e) => 
            {
                DebugService.LogDebug("MainPage Loaded event fired");
                UpdateCanvasScale();
                // Set focus to enable keyboard shortcuts
                this.Focus();
                
                // Force refresh of commands when page loads (in case returning from photobooth)
                CommandManager.InvalidateRequerySuggested();
                
                // Clear any stale photobooth window reference
                if (PhotoboothService.PhotoboothWindow != null && !PhotoboothService.PhotoboothWindow.IsVisible)
                {
                    PhotoboothService.PhotoboothWindow = null;
                    DebugService.LogDebug("MainPage Loaded: Cleared stale photobooth window reference");
                }
            };
            this.SizeChanged += (s, e) => 
            {
                DebugService.LogDebug($"MainPage SizeChanged: {e.NewSize.Width}x{e.NewSize.Height}");
                UpdateCanvasScale();
            };
            
            // Subscribe to canvas size changes from ViewModel
            ViewModel.CanvasSizeChanged += (s, e) =>
            {
                DebugService.LogDebug("Canvas size changed event received");
                // Use dispatcher to ensure we're on the UI thread and give layout time to update
                Dispatcher.BeginInvoke(new Action(() => UpdateCanvasScale()), 
                    System.Windows.Threading.DispatcherPriority.Loaded);
            };
            
            // Save-on-close functionality removed (auto-save not implemented yet)
        }
        
        
        private void UpdateCanvasScale()
        {
            DebugService.LogDebug($"UpdateCanvasScale called - canvasScale:{canvasScale != null} canvas:{ViewModel?.CustomDesignerCanvas != null}");
            
            if (canvasScale == null) 
            {
                DebugService.LogDebug("canvasScale is null - ScaleTransform not found!");
                return;
            }
            
            if (ViewModel?.CustomDesignerCanvas == null) 
            {
                DebugService.LogDebug("CustomDesignerCanvas is null!");
                return;
            }
            
            // Get the actual pixel dimensions of the canvas
            double canvasWidth = ViewModel.CustomDesignerCanvas.ActualPixelWidth > 0 ? 
                ViewModel.CustomDesignerCanvas.ActualPixelWidth : 600;
            double canvasHeight = ViewModel.CustomDesignerCanvas.ActualPixelHeight > 0 ? 
                ViewModel.CustomDesignerCanvas.ActualPixelHeight : 1800;
            
            DebugService.LogDebug($"Canvas dimensions: {canvasWidth}x{canvasHeight}");
            
            // Get the parent Border that contains the canvas
            var parentBorder = this.FindName("canvasContainer") as Border;
            if (parentBorder == null) 
            {
                DebugService.LogDebug("canvasContainer not found by name, searching in visual tree");
                // Try to find the Grid if Border not found
                var parentGrid = LogicalTreeHelper.GetChildren(this).OfType<Grid>().FirstOrDefault();
                if (parentGrid != null && parentGrid.Children.Count > 0)
                {
                    DebugService.LogDebug($"Found parent grid with {parentGrid.Children.Count} children");
                    var column0Grid = parentGrid.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetColumn(g) == 0 && Grid.GetRow(g) == 1);
                    if (column0Grid != null && column0Grid.Children.Count > 0)
                    {
                        DebugService.LogDebug($"Found column0Grid with {column0Grid.Children.Count} children");
                        parentBorder = column0Grid.Children.OfType<Border>().FirstOrDefault();
                    }
                }
            }
            
            if (parentBorder == null) 
            {
                DebugService.LogDebug("Parent border not found - cannot calculate scale");
                return;
            }
            
            DebugService.LogDebug($"Parent border found: {parentBorder.ActualWidth}x{parentBorder.ActualHeight}");
            
            double availableWidth = parentBorder.ActualWidth - 22; // Account for margins and border
            double availableHeight = parentBorder.ActualHeight - 22;
            
            if (availableWidth <= 0 || availableHeight <= 0) 
            {
                // Try again after layout
                Dispatcher.BeginInvoke(new Action(() => UpdateCanvasScale()), 
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            
            // Calculate scale to fit canvas in available space while maintaining aspect ratio
            double scaleX = availableWidth / canvasWidth;
            double scaleY = availableHeight / canvasHeight;
            double scale = Math.Min(scaleX, scaleY);
            
            // Apply a bit of padding (85% of available space for better fit)
            scale *= 0.85;
            
            // Ensure scale is reasonable - allow smaller scales for large canvases
            // Remove the maximum limit to allow proper scaling of large sizes
            scale = Math.Max(0.05, scale); // Allow scaling down to 5% for very large canvases, no maximum
            
            // Apply the scale transform
            canvasScale.ScaleX = scale;
            canvasScale.ScaleY = scale;
            
            // Update the CurrentScale property for scale-aware handle sizing
            if (ViewModel?.CustomDesignerCanvas != null)
            {
                ViewModel.CustomDesignerCanvas.CurrentScale = scale;
                DebugService.LogDebug($"MainPage setting CurrentScale to: {scale:F3}");
            }
            else
            {
                DebugService.LogDebug("MainPage: CustomDesignerCanvas is null, cannot set CurrentScale");
            }
            
            DebugService.LogDebug($"MainPage canvas scale applied: {scale:F3} (Canvas: {canvasWidth}x{canvasHeight}, Available: {availableWidth}x{availableHeight})");
        }

        // Legacy method for backward compatibility
        public DesignerCanvas.Controls.DesignerCanvas dcvs => ViewModel?.CustomDesignerCanvas;

        private void Image_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel?.ClearCanvasCmd.Execute(null);
        }

        private void Image_MouseLeftButtonDown_1(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel?.ChangeCanvasOrientationCmd.Execute(null);
        }
    }
}
