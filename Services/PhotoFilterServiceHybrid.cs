using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ImageMagick;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Hybrid photo filter service that uses Magick.NET when available
    /// Falls back to GDI+ if needed
    /// </summary>
    public class PhotoFilterServiceHybrid
    {
        private readonly string lutFolder;
        private readonly bool useMagick = true;
        private readonly PhotoFilterService gdiService;

        public PhotoFilterServiceHybrid()
        {
            // Set up LUT folder for custom filters
            lutFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Filters", "LUTs");
            if (!Directory.Exists(lutFolder))
            {
                Directory.CreateDirectory(lutFolder);
            }
            
            // Keep GDI service as fallback
            gdiService = new PhotoFilterService();
            
            // Configure Magick.NET for optimal performance
            try
            {
                ResourceLimits.Thread = (uint)Environment.ProcessorCount;
                ResourceLimits.Throttle = 100;
                // Set more aggressive memory and performance limits for faster processing
                ResourceLimits.Memory = 2L * 1024 * 1024 * 1024; // 2GB memory limit
                ResourceLimits.Area = 128L * 1024 * 1024; // 128MB area limit
                ResourceLimits.Disk = 1L * 1024 * 1024 * 1024; // 1GB disk limit

                // ENABLE GPU ACCELERATION via OpenCL
                try
                {
                    OpenCL.IsEnabled = true;
                    if (OpenCL.IsEnabled)
                    {
                        Debug.WriteLine("[PhotoFilterServiceHybrid] ✅ GPU acceleration ENABLED via OpenCL");
                        Debug.WriteLine("[PhotoFilterServiceHybrid] OpenCL GPU acceleration enabled");
                    }
                }
                catch
                {
                    // OpenCL not available, continue with CPU
                }
                useMagick = true;
            }
            catch
            {
                useMagick = false;
                // Magick.NET not available, using GDI+ fallback
            }
        }

        public string ApplyFilterToFile(string inputPath, string outputPath, FilterType filterType, float intensity = 1.0f)
        {
            if (!useMagick)
            {
                // Fallback to GDI+
                return gdiService.ApplyFilterToFile(inputPath, outputPath, filterType, intensity);
            }

            try
            {
                using (var image = new MagickImage(inputPath))
                {
                    // Optimize for preview images - reduce size for faster processing
                    if (inputPath.Contains("_preview") && (image.Width > 800 || image.Height > 800))
                    {
                        // Resize preview images for much faster processing
                        image.Resize(new MagickGeometry(800, 800) { IgnoreAspectRatio = false });
                    }
                    
                    // Apply the selected filter using Magick.NET
                    ApplyMagickFilter(image, filterType, intensity);
                    
                    // Set quality for output - lower for previews
                    image.Quality = inputPath.Contains("_preview") ? 85 : 95;
                    
                    // Save the result
                    image.Write(outputPath);
                    
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                // Magick.NET failed, falling back to GDI+
                return gdiService.ApplyFilterToFile(inputPath, outputPath, filterType, intensity);
            }
        }

        private void ApplyMagickFilter(MagickImage image, FilterType filterType, float intensity)
        {
            MagickImage original = null;
            if (intensity < 1.0f)
            {
                original = (MagickImage)image.Clone();
            }

            switch (filterType)
            {
                case FilterType.None:
                    break;
                    
                case FilterType.BlackAndWhite:
                    // High-quality black and white
                    image.Grayscale();
                    image.ContrastStretch(new Percentage(2), new Percentage(98));
                    break;
                    
                case FilterType.Sepia:
                    image.SepiaTone(new Percentage(80));
                    break;
                    
                case FilterType.Vintage:
                    // Vintage effect
                    image.Modulate(new Percentage(100), new Percentage(70), new Percentage(100));
                    image.Colorize(new MagickColor("#704214"), new Percentage(10));
                    image.Vignette(0, 3, 10, 10);
                    break;
                    
                case FilterType.Glamour:
                    // Simple black and white with high contrast
                    image.Grayscale();
                    image.ContrastStretch(new Percentage(15), new Percentage(85));
                    break;
                    
                case FilterType.Cool:
                    // Cool blue tones
                    image.Modulate(new Percentage(100), new Percentage(110), new Percentage(95));
                    image.Evaluate(Channels.Red, EvaluateOperator.Multiply, 0.9);
                    image.Evaluate(Channels.Blue, EvaluateOperator.Multiply, 1.2);
                    break;
                    
                case FilterType.Warm:
                    // Warm orange/yellow tones
                    image.Modulate(new Percentage(100), new Percentage(110), new Percentage(105));
                    image.Evaluate(Channels.Red, EvaluateOperator.Multiply, 1.2);
                    image.Evaluate(Channels.Green, EvaluateOperator.Multiply, 1.1);
                    image.Evaluate(Channels.Blue, EvaluateOperator.Multiply, 0.8);
                    break;
                    
                case FilterType.HighContrast:
                    image.ContrastStretch(new Percentage(10), new Percentage(90));
                    image.BrightnessContrast(new Percentage(0), new Percentage(30));
                    break;
                    
                case FilterType.Soft:
                    // Soft focus
                    using (var blurred = (MagickImage)image.Clone())
                    {
                        blurred.GaussianBlur(3, 2);
                        image.Composite(blurred, CompositeOperator.SoftLight, "50");
                    }
                    image.BrightnessContrast(new Percentage(5), new Percentage(-10));
                    break;
                    
                case FilterType.Vivid:
                    // Vivid colors
                    image.Modulate(new Percentage(100), new Percentage(140), new Percentage(100));
                    image.BrightnessContrast(new Percentage(5), new Percentage(20));
                    break;
            }

            // Apply intensity blending if needed
            if (intensity < 1.0f && original != null)
            {
                image.Composite(original, CompositeOperator.Blend, 
                    ((int)((1.0f - intensity) * 100)).ToString());
                original.Dispose();
            }
        }

        public Bitmap ApplyFilter(Bitmap original, FilterType filterType, float intensity = 1.0f)
        {
            // For compatibility - use GDI+ version
            return gdiService.ApplyFilter(original, filterType, intensity);
        }
    }
}