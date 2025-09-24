using System;
using System.IO;
using System.Diagnostics;
using ImageMagick;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// High-performance photo filter service using Magick.NET
    /// Provides better quality and faster processing than GDI+
    /// </summary>
    public class PhotoFilterServiceMagick
    {
        private readonly string lutFolder;

        public PhotoFilterServiceMagick()
        {
            // Set up LUT folder for custom filters
            lutFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Filters", "LUTs");
            if (!Directory.Exists(lutFolder))
            {
                Directory.CreateDirectory(lutFolder);
            }
            
            // Configure Magick.NET for optimal performance
            ResourceLimits.Thread = (uint)Environment.ProcessorCount;
            ResourceLimits.Throttle = 100; // Use full CPU
            // Set memory and performance limits for faster processing
            ResourceLimits.Memory = 2L * 1024 * 1024 * 1024; // 2GB memory limit
            ResourceLimits.Area = 128L * 1024 * 1024; // 128MB area limit
            ResourceLimits.Disk = 1L * 1024 * 1024 * 1024; // 1GB disk limit

            // ENABLE GPU ACCELERATION via OpenCL for faster image processing
            try
            {
                OpenCL.IsEnabled = true; // Enable OpenCL GPU acceleration
                if (OpenCL.IsEnabled)
                {
                    Debug.WriteLine("[PhotoFilterServiceMagick] ✅ GPU acceleration ENABLED via OpenCL");
                    Debug.WriteLine("[PhotoFilterServiceMagick] OpenCL GPU acceleration enabled for image processing");

                    // Log OpenCL device info
                    foreach (var device in OpenCL.Devices)
                    {
                        Debug.WriteLine($"[PhotoFilterServiceMagick] OpenCL Device: {device.Name} - Type: {device.DeviceType}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PhotoFilterServiceMagick] OpenCL GPU not available: {ex.Message}");
            }
        }

        public string ApplyFilterToFile(string inputPath, string outputPath, FilterType filterType, float intensity = 1.0f)
        {
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
                    
                    // Apply the selected filter
                    ApplyFilter(image, filterType, intensity);
                    
                    // Set quality for output - lower for previews
                    image.Quality = inputPath.Contains("_preview") ? 85 : 95;
                    
                    // Save the result
                    image.Write(outputPath);
                    
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                // Failed to apply filter, returning original
                if (inputPath != outputPath && File.Exists(inputPath))
                {
                    File.Copy(inputPath, outputPath, true);
                }
                return outputPath;
            }
        }

        private void ApplyFilter(MagickImage image, FilterType filterType, float intensity)
        {
            switch (filterType)
            {
                case FilterType.None:
                    // No filter applied
                    break;
                    
                case FilterType.BlackAndWhite:
                    ApplyBlackAndWhite(image, intensity);
                    break;
                    
                case FilterType.Sepia:
                    ApplySepia(image, intensity);
                    break;
                    
                case FilterType.Vintage:
                    ApplyVintage(image, intensity);
                    break;
                    
                case FilterType.Glamour:
                    ApplyGlamour(image, intensity);
                    break;
                    
                case FilterType.Cool:
                    ApplyCool(image, intensity);
                    break;
                    
                case FilterType.Warm:
                    ApplyWarm(image, intensity);
                    break;
                    
                case FilterType.HighContrast:
                    ApplyHighContrast(image, intensity);
                    break;
                    
                case FilterType.Soft:
                    ApplySoft(image, intensity);
                    break;
                    
                case FilterType.Vivid:
                    ApplyVivid(image, intensity);
                    break;
                    
                case FilterType.Custom:
                    // Custom LUT-based filters
                    break;
            }
            
            // Apply intensity if less than 100%
            if (intensity < 1.0f && filterType != FilterType.None)
            {
                // Blend with original using intensity as alpha
                using (var original = image.Clone())
                {
                    image.Composite(original, CompositeOperator.Blend, 
                        $"{(int)((1.0f - intensity) * 100)}");
                }
            }
        }

        private void ApplyBlackAndWhite(MagickImage image, float intensity)
        {
            // High-quality black and white conversion
            image.Grayscale();
            
            // Enhance contrast slightly for better B&W look
            image.ContrastStretch(new Percentage(2), new Percentage(98));
        }

        private void ApplySepia(MagickImage image, float intensity)
        {
            // Magick.NET's built-in sepia tone is excellent
            image.SepiaTone(new Percentage(80));
        }

        private void ApplyVintage(MagickImage image, float intensity)
        {
            // Vintage effect with faded colors and vignette
            
            // Reduce saturation
            image.Modulate(new Percentage(100), new Percentage(70), new Percentage(100));
            
            // Add slight yellow/brown tint
            image.Colorize(new MagickColor("#704214"), new Percentage(10));
            
            // Add vignette
            image.Vignette(0, 3, 10, 10);
            
            // Slight blur for vintage softness
            image.GaussianBlur(0.5, 0.5);
            
            // Reduce contrast slightly
            image.BrightnessContrast(new Percentage(5), new Percentage(-10));
        }

        private void ApplyGlamour(MagickImage image, float intensity)
        {
            // Simple black and white glamour with high contrast
            image.Grayscale();
            image.ContrastStretch(new Percentage(15), new Percentage(85));
        }

        private void ApplyCool(MagickImage image, float intensity)
        {
            // Cool tone - enhance blues, reduce reds
            image.Modulate(new Percentage(100), new Percentage(110), new Percentage(95));
            
            // Shift colors toward blue
            var colorMatrix = new MagickColorMatrix(3,
                0.9, 0.0, 0.0,
                0.0, 1.0, 0.0,
                0.0, 0.0, 1.2);
            image.ColorMatrix(colorMatrix);
        }

        private void ApplyWarm(MagickImage image, float intensity)
        {
            // Warm tone - enhance reds/yellows, reduce blues
            image.Modulate(new Percentage(100), new Percentage(110), new Percentage(105));
            
            // Shift colors toward warm tones
            var colorMatrix = new MagickColorMatrix(3,
                1.2, 0.0, 0.0,
                0.0, 1.1, 0.0,
                0.0, 0.0, 0.8);
            image.ColorMatrix(colorMatrix);
        }

        private void ApplyHighContrast(MagickImage image, float intensity)
        {
            // Dramatic contrast increase
            image.ContrastStretch(new Percentage(10), new Percentage(90));
            
            // Increase local contrast
            image.Enhance();
            
            // Boost overall contrast
            image.BrightnessContrast(new Percentage(0), new Percentage(30));
        }

        private void ApplySoft(MagickImage image, float intensity)
        {
            // Soft focus effect
            using (var blurred = image.Clone())
            {
                // Create soft blur
                blurred.GaussianBlur(3, 2);
                
                // Blend with original for soft focus effect
                image.Composite(blurred, CompositeOperator.SoftLight, "50");
            }
            
            // Reduce contrast slightly for softer look
            image.BrightnessContrast(new Percentage(5), new Percentage(-10));
        }

        private void ApplyVivid(MagickImage image, float intensity)
        {
            // Vivid colors - boost saturation and contrast
            
            // Increase saturation significantly
            image.Modulate(new Percentage(100), new Percentage(140), new Percentage(100));
            
            // Enhance local contrast
            image.Enhance();
            
            // Boost overall contrast
            image.BrightnessContrast(new Percentage(5), new Percentage(20));
            
            // Sharpen for extra pop
            image.Sharpen(2, 1);
        }
    }
}