using System;
using System.IO;
using System.Windows.Media.Imaging;
using ImageMagick;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Live View Enhancement Service
    /// Provides display-only adjustments for live view without affecting camera settings
    /// Perfect for dark venues where you need better visibility while keeping camera settings for flash
    /// </summary>
    public class LiveViewEnhancementService
    {
        #region Properties
        /// <summary>
        /// Brightness adjustment (-100 to +100, 0 = no change)
        /// </summary>
        public int Brightness { get; set; } = 0;

        /// <summary>
        /// Contrast adjustment (-100 to +100, 0 = no change)
        /// </summary>
        public int Contrast { get; set; } = 0;

        /// <summary>
        /// Gamma correction (0.1 to 3.0, 1.0 = no change)
        /// </summary>
        public double Gamma { get; set; } = 1.0;

        /// <summary>
        /// Enable auto-enhancement for dark conditions
        /// </summary>
        public bool AutoEnhanceForDarkVenues { get; set; } = false;

        /// <summary>
        /// Enable/disable live view enhancement
        /// </summary>
        public bool IsEnabled { get; set; } = false;
        #endregion

        #region Events
        /// <summary>
        /// Fired when enhancement settings change
        /// </summary>
        public event EventHandler EnhancementChanged;
        #endregion

        #region Constructor
        public LiveViewEnhancementService()
        {
            Log.Debug("LiveViewEnhancementService initialized");
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Process live view image data with enhancement settings
        /// </summary>
        /// <param name="originalImageData">Original live view image bytes</param>
        /// <returns>Enhanced image bytes, or original if enhancement disabled</returns>
        public byte[] ProcessLiveViewImage(byte[] originalImageData)
        {
            if (!IsEnabled || originalImageData == null || originalImageData.Length == 0)
            {
                return originalImageData;
            }

            try
            {
                using (var image = new MagickImage(originalImageData))
                {
                    // Apply auto-enhancement for dark venues
                    if (AutoEnhanceForDarkVenues)
                    {
                        ApplyDarkVenueAutoEnhancement(image);
                    }
                    else
                    {
                        // Apply manual adjustments
                        ApplyManualAdjustments(image);
                    }

                    // Convert back to bytes
                    return image.ToByteArray(MagickFormat.Jpeg);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LiveViewEnhancementService: Error processing image: {ex.Message}");
                return originalImageData; // Return original on error
            }
        }

        /// <summary>
        /// Create enhanced BitmapImage directly for WPF display
        /// </summary>
        /// <param name="originalImageData">Original live view image bytes</param>
        /// <returns>Enhanced BitmapImage for display</returns>
        public BitmapImage ProcessLiveViewImageToBitmap(byte[] originalImageData)
        {
            var processedData = ProcessLiveViewImage(originalImageData);
            
            using (var ms = new MemoryStream(processedData))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        /// <summary>
        /// Set brightness adjustment
        /// </summary>
        /// <param name="brightness">Brightness (-100 to +100)</param>
        public void SetBrightness(int brightness)
        {
            Brightness = Math.Max(-100, Math.Min(100, brightness));
            Log.Debug($"LiveViewEnhancement: Brightness set to {Brightness}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set contrast adjustment
        /// </summary>
        /// <param name="contrast">Contrast (-100 to +100)</param>
        public void SetContrast(int contrast)
        {
            Contrast = Math.Max(-100, Math.Min(100, contrast));
            Log.Debug($"LiveViewEnhancement: Contrast set to {Contrast}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set gamma correction
        /// </summary>
        /// <param name="gamma">Gamma (0.1 to 3.0)</param>
        public void SetGamma(double gamma)
        {
            Gamma = Math.Max(0.1, Math.Min(3.0, gamma));
            Log.Debug($"LiveViewEnhancement: Gamma set to {Gamma}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Toggle auto-enhancement for dark venues
        /// </summary>
        public void ToggleAutoEnhancement()
        {
            AutoEnhanceForDarkVenues = !AutoEnhanceForDarkVenues;
            Log.Debug($"LiveViewEnhancement: Auto enhancement {(AutoEnhanceForDarkVenues ? "enabled" : "disabled")}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Apply dark venue preset
        /// </summary>
        public void ApplyDarkVenuePreset()
        {
            Brightness = 30;
            Contrast = 20;
            Gamma = 0.8;
            IsEnabled = true;
            AutoEnhanceForDarkVenues = false;
            Log.Debug("LiveViewEnhancement: Dark venue preset applied");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Reset all enhancements
        /// </summary>
        public void ResetEnhancements()
        {
            Brightness = 0;
            Contrast = 0;
            Gamma = 1.0;
            AutoEnhanceForDarkVenues = false;
            IsEnabled = false;
            Log.Debug("LiveViewEnhancement: All enhancements reset");
            OnEnhancementChanged();
        }
        #endregion

        #region Private Methods
        private void ApplyDarkVenueAutoEnhancement(MagickImage image)
        {
            // Auto-enhance for dark conditions
            // Increase brightness and contrast automatically
            // Adjust gamma for better visibility in dark areas
            
            image.BrightnessContrast(new Percentage(25), new Percentage(15));
            image.Gamma(0.75);
            
            // Optional: Apply histogram equalization for better detail in shadows
            image.Equalize(Channels.All);
            
            Log.Debug("LiveViewEnhancement: Auto dark venue enhancement applied");
        }

        private void ApplyManualAdjustments(MagickImage image)
        {
            // Apply brightness and contrast if set
            if (Brightness != 0 || Contrast != 0)
            {
                image.BrightnessContrast(new Percentage(Brightness), new Percentage(Contrast));
            }

            // Apply gamma correction if not default
            if (Math.Abs(Gamma - 1.0) > 0.01)
            {
                image.Gamma(Gamma);
            }

            Log.Debug($"LiveViewEnhancement: Manual adjustments applied - Brightness: {Brightness}, Contrast: {Contrast}, Gamma: {Gamma}");
        }

        private void OnEnhancementChanged()
        {
            EnhancementChanged?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Presets
        /// <summary>
        /// Get available enhancement presets
        /// </summary>
        public static class Presets
        {
            public static readonly EnhancementPreset Normal = new EnhancementPreset
            {
                Name = "Normal",
                Brightness = 0,
                Contrast = 0,
                Gamma = 1.0,
                AutoEnhance = false
            };

            public static readonly EnhancementPreset DarkVenue = new EnhancementPreset
            {
                Name = "Dark Venue",
                Brightness = 30,
                Contrast = 20,
                Gamma = 0.8,
                AutoEnhance = false
            };

            public static readonly EnhancementPreset VeryDark = new EnhancementPreset
            {
                Name = "Very Dark",
                Brightness = 50,
                Contrast = 30,
                Gamma = 0.6,
                AutoEnhance = false
            };

            public static readonly EnhancementPreset AutoDark = new EnhancementPreset
            {
                Name = "Auto Dark",
                Brightness = 0,
                Contrast = 0,
                Gamma = 1.0,
                AutoEnhance = true
            };
        }

        public class EnhancementPreset
        {
            public string Name { get; set; }
            public int Brightness { get; set; }
            public int Contrast { get; set; }
            public double Gamma { get; set; }
            public bool AutoEnhance { get; set; }
        }

        /// <summary>
        /// Apply a preset to the service
        /// </summary>
        public void ApplyPreset(EnhancementPreset preset)
        {
            Brightness = preset.Brightness;
            Contrast = preset.Contrast;
            Gamma = preset.Gamma;
            AutoEnhanceForDarkVenues = preset.AutoEnhance;
            IsEnabled = true;
            
            Log.Debug($"LiveViewEnhancement: Applied preset '{preset.Name}'");
            OnEnhancementChanged();
        }
        #endregion
    }
}