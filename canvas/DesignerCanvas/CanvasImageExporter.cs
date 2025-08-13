using System;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesignerCanvas
{
	public class CanvasImageExporter
	{
		public const double WpfDpi = 96;  // WPF default DPI

		public static void DoEvents()
		{
			var frame = new DispatcherFrame(true);
			Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded,
				(SendOrPostCallback)(arg =>
				{
					var f = (DispatcherFrame)arg;
					f.Continue = false;
				}), frame);
			Dispatcher.PushFrame(frame);
		}

		public static BitmapSource CreateImage(Controls.DesignerCanvas canvas, double dpiX, double dpiY)
		{
			// Calculate the new size based on the desired DPI and scaling factor (300/96)
			double scale = dpiX / WpfDpi;
			int width = (int)(canvas.RatioX * 100 * scale);
			int height = (int)(canvas.RatioY * 100 * scale);

			var image = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);

			// Temporarily scale the canvas to fit the desired output size
			canvas.LayoutTransform = new ScaleTransform(scale, scale);

			// Render the canvas to the image
			canvas.RenderImage(image);

			// Reset the canvas scale to normal
			canvas.LayoutTransform = null;

			return image;
		}

		public static BitmapEncoder EncoderFromFileName(string fileName)
		{
			if (string.IsNullOrEmpty(fileName))
				throw new ArgumentException("Argument is null or empty", nameof(fileName));
			var ext = Path.GetExtension(fileName);
			switch (ext.ToLowerInvariant())
			{
				case ".bmp":
					return new BmpBitmapEncoder();
				case ".jpg":
				case ".jpeg":
					return new JpegBitmapEncoder();
				case ".png":
					return new PngBitmapEncoder();
				case ".tif":
				case ".tiff":
					return new TiffBitmapEncoder();
				default:
					throw new NotSupportedException("Extension of specified fileName is not supported.");
			}
		}

		public static void ExportImage(Controls.DesignerCanvas canvas, Stream s, BitmapEncoder encoder, double dpiX, double dpiY)
		{
			if (canvas == null) throw new ArgumentNullException(nameof(canvas));
			if (s == null) throw new ArgumentNullException(nameof(s));
			if (encoder == null) throw new ArgumentNullException(nameof(encoder));

			var image = CreateImage(canvas, dpiX, dpiY);
			encoder.Frames.Add(BitmapFrame.Create(image));
			encoder.Save(s);
		}

		public static void ExportImage(Controls.DesignerCanvas canvas, string fileName, double dpiX, double dpiY)
		{
			if (canvas == null) throw new ArgumentNullException(nameof(canvas));
			if (string.IsNullOrEmpty(fileName))
				throw new ArgumentException("Argument is null or empty", nameof(fileName));

			var encoder = EncoderFromFileName(fileName);
			using (var fs = File.OpenWrite(fileName))
			{
				ExportImage(canvas, fs, encoder, dpiX, dpiY);
			}
		}

		public static void ExportImage(BitmapSource source, string fileName)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (string.IsNullOrEmpty(fileName))
				throw new ArgumentException("Argument is null or empty", nameof(fileName));

			var encoder = EncoderFromFileName(fileName);
			encoder.Frames.Add(BitmapFrame.Create(source));
			using (var fs = File.OpenWrite(fileName))
			{
				encoder.Save(fs);
			}
		}
	}

}
