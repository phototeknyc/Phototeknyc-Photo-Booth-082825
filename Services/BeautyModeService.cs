using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ImageMagick;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for applying beauty mode enhancement to photos
    /// Optimized for speed with simplified processing
    /// </summary>
    public class BeautyModeService
    {
        private static BeautyModeService _instance;
        public static BeautyModeService Instance => _instance ?? (_instance = new BeautyModeService());

        /// <summary>
        /// Apply beauty mode enhancement to a photo (OPTIMIZED VERSION)
        /// </summary>
        /// <param name="inputPath">Input photo path</param>
        /// <param name="outputPath">Output photo path (can be same as input to overwrite)</param>
        /// <param name="intensity">Intensity of beauty mode (0-100)</param>
        public void ApplyBeautyMode(string inputPath, string outputPath, int intensity = 50)
        {
            if (!File.Exists(inputPath))
            {
                System.Diagnostics.Debug.WriteLine($"BeautyModeService: Input file not found: {inputPath}");
                return;
            }

            try
            {
                // Ensure intensity is within valid range
                intensity = Math.Max(0, Math.Min(100, intensity));
                
                using (var image = new MagickImage(inputPath))
                {
                    // OPTIMIZED: Single-pass beauty enhancement
                    // Combines smoothing and enhancement in fewer operations
                    
                    if (intensity > 0)
                    {
                        // 1. Quick soft blur for skin smoothing (single operation)
                        double blurRadius = 1.0 + (intensity / 100.0 * 2.0); // 1-3 radius
                        image.Blur(blurRadius, blurRadius / 2);
                        
                        // 2. Simple contrast and brightness adjustment for glow effect
                        // Lighter and brighter for beauty effect
                        int brightnessBoost = (int)(intensity / 100.0 * 5); // 0-5%
                        image.Modulate(new Percentage(100 + brightnessBoost), new Percentage(100), new Percentage(100));
                        
                        // 3. Slight saturation boost for healthy look
                        int saturationBoost = (int)(intensity / 100.0 * 10); // 0-10%
                        image.Modulate(new Percentage(100), new Percentage(100 + saturationBoost), new Percentage(100));
                        
                        // 4. Optional: Very subtle warm tint for skin tone
                        if (intensity > 30)
                        {
                            // Add minimal warm tint
                            image.Colorize(new MagickColor("#FFE4B5"), new Percentage(intensity / 20)); // Max 5% tint
                        }
                    }
                    
                    // Save the result
                    image.Write(outputPath);
                }
                
                System.Diagnostics.Debug.WriteLine($"BeautyModeService: Applied beauty mode to {inputPath} with intensity {intensity}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BeautyModeService: Error applying beauty mode: {ex.Message}");
                // If beauty mode fails, copy original to output
                if (inputPath != outputPath)
                {
                    File.Copy(inputPath, outputPath, true);
                }
            }
        }

        /// <summary>
        /// Apply beauty mode to multiple photos
        /// </summary>
        public void ApplyBeautyModeToPhotos(string[] photoPaths, int intensity = 50)
        {
            if (!Properties.Settings.Default.BeautyModeEnabled)
                return;

            foreach (var photoPath in photoPaths)
            {
                if (!string.IsNullOrEmpty(photoPath) && File.Exists(photoPath))
                {
                    ApplyBeautyMode(photoPath, photoPath, intensity);
                }
            }
        }

        /// <summary>
        /// Check if beauty mode is enabled
        /// </summary>
        public bool IsEnabled()
        {
            return Properties.Settings.Default.BeautyModeEnabled;
        }

        /// <summary>
        /// Get current intensity setting
        /// </summary>
        public int GetIntensity()
        {
            return Properties.Settings.Default.BeautyModeIntensity;
        }
    }
}