using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Photobooth.Design.Canvas
{
	/// <summary>
	/// Interaction logic for CanvasContainer.xaml
	/// </summary>
	public partial class CanvasContainer : UserControl
	{
		public DesignerCanvas.Controls.DesignerCanvas Canvas
		{
			get { return dcvs; }
			set { dcvs = value; }	
		}

		private double _ratio = 1.0;
		public double CanvasRatio
		{
			get { return _ratio; }
			set 
			{
				_ratio = value;
				// ViewBox handles scaling - no manual width adjustment needed
			}
		}

		public CanvasContainer()
		{
			System.Diagnostics.Debug.WriteLine("CanvasContainer constructor called");
			InitializeComponent();
			System.Diagnostics.Debug.WriteLine("CanvasContainer InitializeComponent completed");
			
			this.Loaded += (s, e) => 
			{
				System.Diagnostics.Debug.WriteLine("CanvasContainer Loaded event fired");
				UpdateCanvasScale();
			};
			
			// Also try updating on size changed
			this.SizeChanged += (s, e) =>
			{
				System.Diagnostics.Debug.WriteLine($"CanvasContainer SizeChanged: {e.NewSize.Width}x{e.NewSize.Height}");
				UpdateCanvasScale();
			};
		}

		private void dcvs_MouseDown(object sender, MouseButtonEventArgs e)
		{

		}

		private void dcvs_MouseMove(object sender, MouseEventArgs e)
		{
		}

		private void Grid_Initialized(object sender, EventArgs e)
		{
		}

		private void Border_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			UpdateCanvasScale();
		}

		public void UpdateCanvasScale()
		{
			// Get the ScaleTransform from the Grid's LayoutTransform
			ScaleTransform scaleTransform = null;
			if (canvasGrid != null && canvasGrid.LayoutTransform is ScaleTransform)
			{
				scaleTransform = (ScaleTransform)canvasGrid.LayoutTransform;
			}
			
			System.Diagnostics.Debug.WriteLine($"UpdateCanvasScale called - dcvs:{dcvs != null} scaleTransform:{scaleTransform != null} canvasBorder:{canvasBorder != null}");
			
			if (dcvs == null || scaleTransform == null || canvasBorder == null) 
			{
				System.Diagnostics.Debug.WriteLine("UpdateCanvasScale returning early - null check failed");
				return;
			}
			
			// Get the actual pixel dimensions of the canvas
			double canvasWidth = dcvs.ActualPixelWidth > 0 ? dcvs.ActualPixelWidth : 600;
			double canvasHeight = dcvs.ActualPixelHeight > 0 ? dcvs.ActualPixelHeight : 1800;
			
			// If canvas has explicit dimensions, use those
			if (!double.IsNaN(dcvs.Width) && dcvs.Width > 0)
				canvasWidth = dcvs.Width;
			if (!double.IsNaN(dcvs.Height) && dcvs.Height > 0)
				canvasHeight = dcvs.Height;
			
			// Get the available space in the border (accounting for border thickness and margin)
			double availableWidth = canvasBorder.ActualWidth - 4; // Subtract border thickness
			double availableHeight = canvasBorder.ActualHeight - 4;
			
			if (availableWidth <= 0 || availableHeight <= 0)
			{
				// Try again after layout completes
				Dispatcher.BeginInvoke(new Action(() => UpdateCanvasScale()), 
					System.Windows.Threading.DispatcherPriority.Loaded);
				return;
			}
			
			// Calculate scale to fit canvas in available space while maintaining aspect ratio
			double scaleX = availableWidth / canvasWidth;
			double scaleY = availableHeight / canvasHeight;
			double scale = Math.Min(scaleX, scaleY);
			
			// Apply a bit of padding (90% of available space for better visibility)
			scale *= 0.9;
			
			// Ensure scale is reasonable (not too small or too large)
			scale = Math.Max(0.1, Math.Min(2.0, scale));
			
			// Apply the scale transform
			scaleTransform.ScaleX = scale;
			scaleTransform.ScaleY = scale;
			
			System.Diagnostics.Debug.WriteLine($"Canvas scale applied: {scale:F3} (Canvas: {canvasWidth}x{canvasHeight}, Available: {availableWidth}x{availableHeight})");
			
			// Also show in a message for debugging
			// MessageBox.Show($"Scale: {scale:F3}\nCanvas: {canvasWidth}x{canvasHeight}\nAvailable: {availableWidth}x{availableHeight}", "Debug Scale");
		}
	}
}
