using DesignerCanvas;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Design.Canvas;

namespace Photobooth
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public static CanvasContainer dcvs_container = new CanvasContainer();

		public MainWindow()
		{
			InitializeComponent();
			dcvs_container.CanvasRatio = 4.0 / 6.0;
		}

		private void dcvs_MouseDown(object sender, MouseButtonEventArgs e)
		{

		}

		private void dcvs_MouseMove(object sender, MouseEventArgs e)
		{
			// todo remove it later
			// this listener is not needed as it is already handled by the designer canvas
			try
			{
				MousePositionLabel.Content = dcvs_container.dcvs.PointToCanvas(e.GetPosition(dcvs_container.dcvs));
			}
			catch (Exception)
			{ }
		}

		private void btnImport_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
				openFileDialog.Filter = "Image files (*.jpg, *.jpeg, *.jpe, *.jfif, *.png, *.bmp, *.dib, *.gif, *.tif, *.tiff, *.ico, *.svg, *.svgz, *.wmf, *.emf, *.webp)|*.jpg; *.jpeg; *.jpe; *.jfif; *.png; *.bmp; *.dib; *.gif; *.tif; *.tiff; *.ico; *.svg; *.svgz; *.wmf; *.emf; *.webp|All files (*.*)|*.*";
				openFileDialog.Title = "Select a background image";
				if (openFileDialog.ShowDialog() == true)
				{
					// always use for aspect ratio
					var img = new ImageCanvasItem(0, 0, dcvs_container.dcvs.ActualPixelWidth / 2, dcvs_container.dcvs.ActualPixelHeight, new BitmapImage(new Uri(openFileDialog.FileName)), 2, 6);


					dcvs_container.dcvs.Items.Add(img);
				}
			}
			catch (FileNotFoundException ex)
			{
				MessageBox.Show(ex.Message, "File not found", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			catch (Exception)
			{ }
		}

		private void btnExport_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				// save file
				Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
				saveFileDialog.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tif)|*.tif|GIF Image (*.gif)|*.gif|All files (*.*)|*.*";
				saveFileDialog.Title = "Save an image";
				if (saveFileDialog.ShowDialog() == true)
				{
					var encoder = CanvasImageExporter.EncoderFromFileName(saveFileDialog.FileName);
					using (var s = File.Open(saveFileDialog.FileName, FileMode.Create))
					{
						CanvasImageExporter.ExportImage(dcvs_container.dcvs, s, encoder, 300, 300);
					}
				}
			}
			catch (Exception)
			{ }
		}

		private void btnPrint_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				BitmapSource image = CanvasImageExporter.CreateImage(dcvs_container.dcvs, 300, 300);
				// print as 4x6 image and also show the preview image
				var printDialog = new PrintDialog();
				if (printDialog.ShowDialog() == true)
				{
					var printCapabilities = printDialog.PrintQueue.GetPrintCapabilities(printDialog.PrintTicket);
					var scale = Math.Min(printCapabilities.PageImageableArea.ExtentWidth / image.Width, printCapabilities.PageImageableArea.ExtentHeight / image.Height);
					var printImage = new Image
					{
						Source = image,
						Stretch = Stretch.Uniform,
						StretchDirection = StretchDirection.DownOnly,
						Width = image.Width * scale,
						Height = image.Height * scale
					};
					var printCanvas = new Canvas
					{
						Width = printImage.Width,
						Height = printImage.Height
					};
					printCanvas.Children.Add(printImage);
					printDialog.PrintVisual(printCanvas, "Photobooth");
				}
			}
			catch (Exception)
			{ }
		}

		private void btnClear_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				dcvs_container.dcvs.Items.Clear();
			}
			catch (Exception)
			{ }
		}

		private void btnPlaceholder_Click(object sender, RoutedEventArgs e)
		{
			// Create a new PlaceholderCanvasItem with specified properties
			var placeholderItem = new PlaceholderCanvasItem(50, 50, 100, 100, 3, 2);

			// Add the PlaceholderCanvasItem to your canvas or UI element
			dcvs_container.dcvs.Items.Add(placeholderItem);
		}

		private void btnLockSize_Click(object sender, RoutedEventArgs e)
		{
			// lock all selected items
			foreach (IBoxCanvasItem item in dcvs_container.dcvs.SelectedItems)
			{
				item.Resizeable = !item.Resizeable;

				//  remove and re-add to update the adorner
				dcvs_container.dcvs.Items.Remove(item);
				dcvs_container.dcvs.Items.Add(item);
            }
        }

		private void btnLockAspectRatio_Click(object sender, RoutedEventArgs e)
		{
			// lock all selected items
			foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
			{
				item.LockedAspectRatio = !item.LockedAspectRatio;
				//if (item.LockedAspectRatio)
				//{
				//	item.LockedAspectRatio = false;
				//} else
				//{
				//	item.LockedAspectRatio = true;
				//	item.AspectRatio = item.AspectRatio;
				//}
			}
		}

		private void btnLockPosition_Click(object sender, RoutedEventArgs e)
		{
			// lock all selected items
			foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
			{
				item.LockedPosition = !item.LockedPosition;
			}
		}

		private void btnHorizontalVerticalRatio_Click(object sender, RoutedEventArgs e)
		{
			// reverse aspect ratios of all selected items
			foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
			{
				item.reverseAspectRatio();
			}
		}


		private void Window_Initialized(object sender, EventArgs e)
		{
			dcvs_container.MouseMove += dcvs_MouseMove;
			dcvs_container.MouseDown += dcvs_MouseDown;
		}

        private void btnHVCanvas_Click(object sender, RoutedEventArgs e)
        {
			dcvs_container.CanvasRatio = 1 / dcvs_container.CanvasRatio;
        }
    }

}

// Path: DesignerCanvas/CanvasImageExporter.cs
//2100x1500 at 300dpi is 7x5 inches