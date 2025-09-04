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
        /// Simulated ISO adjustment for live view (100 to 6400) - Default for dark venues
        /// </summary>
        public int SimulatedISO { get; set; } = 800;

        /// <summary>
        /// Simulated aperture adjustment for live view (1.4 to 22) - Default for dark venues
        /// </summary>
        public double SimulatedAperture { get; set; } = 2.8;

        /// <summary>
        /// Simulated shutter speed effect for live view - Default for dark venues with flash
        /// </summary>
        public string SimulatedShutterSpeed { get; set; } = "1/125";

        /// <summary>
        /// Simulated exposure compensation (-3 to +3 EV)
        /// </summary>
        public double SimulatedExposureCompensation { get; set; } = 0.0;

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
            DebugService.LogDebug("LiveViewEnhancementService initialized");
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
                DebugService.LogError($"LiveViewEnhancementService: Error processing image: {ex.Message}");
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
            DebugService.LogDebug($"LiveViewEnhancement: Brightness set to {Brightness}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set contrast adjustment
        /// </summary>
        /// <param name="contrast">Contrast (-100 to +100)</param>
        public void SetContrast(int contrast)
        {
            Contrast = Math.Max(-100, Math.Min(100, contrast));
            DebugService.LogDebug($"LiveViewEnhancement: Contrast set to {Contrast}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set gamma correction
        /// </summary>
        /// <param name="gamma">Gamma (0.1 to 3.0)</param>
        public void SetGamma(double gamma)
        {
            Gamma = Math.Max(0.1, Math.Min(3.0, gamma));
            DebugService.LogDebug($"LiveViewEnhancement: Gamma set to {Gamma}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Toggle auto-enhancement for dark venues
        /// </summary>
        public void ToggleAutoEnhancement()
        {
            AutoEnhanceForDarkVenues = !AutoEnhanceForDarkVenues;
            DebugService.LogDebug($"LiveViewEnhancement: Auto enhancement {(AutoEnhanceForDarkVenues ? "enabled" : "disabled")}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set simulated ISO for live view
        /// </summary>
        public void SetSimulatedISO(int isoValue)
        {
            SimulatedISO = Math.Max(100, Math.Min(6400, isoValue));
            DebugService.LogDebug($"LiveViewEnhancement: Simulated ISO set to {SimulatedISO}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set simulated aperture for live view
        /// </summary>
        public void SetSimulatedAperture(double apertureValue)
        {
            SimulatedAperture = Math.Max(1.4, Math.Min(22.0, apertureValue));
            DebugService.LogDebug($"LiveViewEnhancement: Simulated aperture set to f/{SimulatedAperture}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set simulated shutter speed for live view
        /// </summary>
        public void SetSimulatedShutterSpeed(string shutterValue)
        {
            SimulatedShutterSpeed = shutterValue;
            DebugService.LogDebug($"LiveViewEnhancement: Simulated shutter speed set to {SimulatedShutterSpeed}");
            OnEnhancementChanged();
        }

        /// <summary>
        /// Set simulated exposure compensation for live view
        /// </summary>
        public void SetSimulatedExposureCompensation(double compensationValue)
        {
            SimulatedExposureCompensation = Math.Max(-3.0, Math.Min(3.0, compensationValue));
            DebugService.LogDebug($"LiveViewEnhancement: Simulated exposure compensation set to {SimulatedExposureCompensation:+0.0;-0.0;0} EV");
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
            DebugService.LogDebug("LiveViewEnhancement: Dark venue preset applied");
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
            DebugService.LogDebug("LiveViewEnhancement: All enhancements reset");
            OnEnhancementChanged();
        }
        #endregion

        #region Private Methods
        private void ApplyDarkVenueAutoEnhancement(MagickImage image)
        {
            // Auto-enhance for dark conditions - more aggressive for dark venues
            // Increase brightness and contrast significantly
            // Adjust gamma for much better visibility in dark areas
            
            image.BrightnessContrast(new Percentage(45), new Percentage(30));
            image.GammaCorrect(0.6);
            
            // Apply histogram equalization for better detail in shadows
            image.Equalize(Channels.All);
            
            // Additional adjustment for better visibility in shadows
            try 
            {
                // Brighten shadows more aggressively
                image.Modulate(new Percentage(110), new Percentage(120), new Percentage(100));
            }
            catch
            {
                // Ignore if Modulate is not available
            }
            
            DebugService.LogDebug("LiveViewEnhancement: Aggressive auto dark venue enhancement applied");
        }

        private void ApplyManualAdjustments(MagickImage image)
        {
            // Calculate total brightness adjustment from all exposure settings
            var totalBrightnessAdjustment = CalculateExposureBrightness();
            var finalBrightness = Brightness + totalBrightnessAdjustment;
            
            // Apply brightness and contrast adjustments
            if (finalBrightness != 0 || Contrast != 0)
            {
                image.BrightnessContrast(new Percentage(finalBrightness), new Percentage(Contrast));
            }

            // Apply gamma correction if not default
            if (Math.Abs(Gamma - 1.0) > 0.01)
            {
                image.GammaCorrect(Gamma);
            }

            // Apply ISO noise simulation for higher ISO values
            if (SimulatedISO > 400)
            {
                ApplyISONoiseSimulation(image);
            }

            DebugService.LogDebug($"LiveViewEnhancement: Manual adjustments applied - Base Brightness: {Brightness}, Exposure Brightness: {totalBrightnessAdjustment}, Final Brightness: {finalBrightness}, Contrast: {Contrast}, Gamma: {Gamma}, ISO: {SimulatedISO}");
        }

        /// <summary>
        /// Calculate brightness adjustment based on simulated exposure settings
        /// Optimized for dark venue photography
        /// </summary>
        private int CalculateExposureBrightness()
        {
            var brightnessAdjustment = 0;

            // ISO adjustment (higher ISO = much brighter for dark venues)
            // Base ISO 400 for dark venues, each doubling adds 25% brightness
            var isoStops = Math.Log(SimulatedISO / 400.0) / Math.Log(2);
            brightnessAdjustment += (int)(isoStops * 25);

            // Aperture adjustment (lower f-number = significantly brighter)
            // f/2.8 is baseline for dark venues, each full stop changes brightness dramatically
            var apertureStops = Math.Log(SimulatedAperture / 2.8) / Math.Log(2);
            brightnessAdjustment -= (int)(apertureStops * 30);

            // Shutter speed adjustment (slower shutter for dark venues)
            var shutterBrightness = CalculateShutterBrightness(SimulatedShutterSpeed);
            brightnessAdjustment += shutterBrightness;

            // Exposure compensation (direct EV adjustment - more aggressive)
            brightnessAdjustment += (int)(SimulatedExposureCompensation * 30);

            // Allow wider range for dark venue adjustments
            return Math.Max(-100, Math.Min(150, brightnessAdjustment));
        }

        /// <summary>
        /// Calculate brightness adjustment from shutter speed
        /// Optimized for dark venue photography
        /// </summary>
        private int CalculateShutterBrightness(string shutterSpeed)
        {
            try
            {
                if (shutterSpeed.StartsWith("1/"))
                {
                    var denominator = int.Parse(shutterSpeed.Substring(2));
                    // 1/125 is baseline for dark venues (faster for flash), slower = much brighter
                    var stops = Math.Log(125.0 / denominator) / Math.Log(2);
                    return (int)(stops * 25);
                }
                else if (int.TryParse(shutterSpeed, out int wholeSeconds))
                {
                    // Very slow shutter speeds (1 second or more) = extremely bright
                    var stops = Math.Log(wholeSeconds * 125) / Math.Log(2);
                    return (int)(stops * 25);
                }
                else if (shutterSpeed.Contains("."))
                {
                    // Handle fractional seconds like "0.5"
                    if (double.TryParse(shutterSpeed, out double fractionalSeconds))
                    {
                        var stops = Math.Log(fractionalSeconds * 125) / Math.Log(2);
                        return (int)(stops * 25);
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            return 0;
        }

        /// <summary>
        /// Apply simulated ISO noise for high ISO values
        /// </summary>
        private void ApplyISONoiseSimulation(MagickImage image)
        {
            try
            {
                // Add noise starting from ISO 800 for dark venues
                if (SimulatedISO >= 800)
                {
                    var noiseIntensity = Math.Min(1.0, (SimulatedISO - 800) / 2400.0); // Scale from 800 to 3200
                    
                    if (noiseIntensity > 0)
                    {
                        // Add realistic grain pattern
                        image.AddNoise(NoiseType.Gaussian, Channels.All);
                        
                        // Adjust noise based on ISO level
                        var noiseReduction = 1.0 - (noiseIntensity * 0.15); // Subtle noise effect
                        image.Evaluate(Channels.All, EvaluateOperator.Multiply, noiseReduction);
                        
                        DebugService.LogDebug($"LiveViewEnhancement: Applied ISO noise simulation for {SimulatedISO} (intensity: {noiseIntensity:F2})");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"LiveViewEnhancement: ISO noise simulation failed: {ex.Message}");
            }
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
                Brightness = 40,
                Contrast = 35,
                Gamma = 0.7,
                AutoEnhance = false
            };

            public static readonly EnhancementPreset VeryDark = new EnhancementPreset
            {
                Name = "Very Dark",
                Brightness = 60,
                Contrast = 45,
                Gamma = 0.5,
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
            
            DebugService.LogDebug($"LiveViewEnhancement: Applied preset '{preset.Name}'");
            OnEnhancementChanged();
        }
        #endregion
    }
}