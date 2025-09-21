using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using Photobooth.Models;
using Newtonsoft.Json;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for managing and applying virtual backgrounds
    /// </summary>
    public class VirtualBackgroundService : IDisposable
    {
        #region Singleton

        private static VirtualBackgroundService _instance;
        private static readonly object _lock = new object();

        public static VirtualBackgroundService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new VirtualBackgroundService();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Private Fields

        private readonly string _backgroundsFolder;
        private Dictionary<string, List<VirtualBackground>> _backgroundsByCategory;
        private bool _isInitialized;
        private string _selectedBackgroundPath;

        #endregion

        #region Constructor

        private VirtualBackgroundService()
        {
            _backgroundsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "Backgrounds");
            _backgroundsByCategory = new Dictionary<string, List<VirtualBackground>>();

            // Ensure backgrounds folder exists
            EnsureBackgroundFolders();
        }

        #endregion

        #region Initialization

        public async Task LoadBackgroundsAsync()
        {
            // Always reload Custom category to pick up new uploads
            if (_isInitialized)
            {
                await ReloadCustomBackgroundsAsync();
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    _backgroundsByCategory.Clear();

                    // Load backgrounds from each category folder
                    var categories = new[] { "Solid", "Gradient", "Nature", "Office", "Abstract", "Custom" };

                    foreach (var category in categories)
                    {
                        var categoryPath = Path.Combine(_backgroundsFolder, category);
                        if (!Directory.Exists(categoryPath))
                        {
                            Directory.CreateDirectory(categoryPath);
                            CreateDefaultBackgrounds(category, categoryPath);
                        }

                        var backgrounds = LoadBackgroundsFromFolder(category, categoryPath);
                        _backgroundsByCategory[category] = backgrounds;
                    }

                    _isInitialized = true;
                });

                Log.Debug($"Loaded {_backgroundsByCategory.Sum(kvp => kvp.Value.Count)} virtual backgrounds");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load virtual backgrounds: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Reload only custom backgrounds to pick up newly uploaded files
        /// </summary>
        public async Task ReloadCustomBackgroundsAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    var customPath = Path.Combine(_backgroundsFolder, "Custom");
                    if (!Directory.Exists(customPath))
                    {
                        Directory.CreateDirectory(customPath);
                        _backgroundsByCategory["Custom"] = new List<VirtualBackground>();
                        return;
                    }

                    // Reload custom backgrounds from folder
                    var backgrounds = LoadBackgroundsFromFolder("Custom", customPath);
                    _backgroundsByCategory["Custom"] = backgrounds;

                    Log.Debug($"Reloaded {backgrounds.Count} custom backgrounds");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to reload custom backgrounds: {ex.Message}");
            }
        }

        private void EnsureBackgroundFolders()
        {
            if (!Directory.Exists(_backgroundsFolder))
            {
                Directory.CreateDirectory(_backgroundsFolder);
            }

            // Create category folders
            var categories = new[] { "Solid", "Gradient", "Nature", "Office", "Abstract", "Custom" };
            foreach (var category in categories)
            {
                var categoryPath = Path.Combine(_backgroundsFolder, category);
                if (!Directory.Exists(categoryPath))
                {
                    Directory.CreateDirectory(categoryPath);
                }
            }
        }

        private void CreateDefaultBackgrounds(string category, string categoryPath)
        {
            try
            {
                // Create default solid color backgrounds
                if (category == "Solid")
                {
                    var colors = new Dictionary<string, Color>
                    {
                        { "White", Color.White },
                        { "Black", Color.Black },
                        { "Gray", Color.Gray },
                        { "Blue", Color.FromArgb(0, 120, 215) },
                        { "Green", Color.FromArgb(16, 124, 16) },
                        { "Red", Color.FromArgb(232, 17, 35) },
                        { "Purple", Color.FromArgb(128, 0, 128) },
                        { "Orange", Color.FromArgb(255, 165, 0) }
                    };

                    foreach (var colorInfo in colors)
                    {
                        var path = Path.Combine(categoryPath, $"{colorInfo.Key}.png");
                        if (!File.Exists(path))
                        {
                            CreateSolidColorBackground(path, colorInfo.Value);
                            Log.Debug($"Created default solid background: {colorInfo.Key}");
                        }
                    }
                }
                // Create default gradient backgrounds
                else if (category == "Gradient")
                {
                    var gradients = new Dictionary<string, Color[]>
                    {
                        { "Blue Gradient", new[] { Color.FromArgb(52, 143, 235), Color.FromArgb(86, 204, 242) } },
                        { "Purple Gradient", new[] { Color.FromArgb(142, 45, 226), Color.FromArgb(74, 0, 224) } },
                        { "Green Gradient", new[] { Color.FromArgb(56, 239, 125), Color.FromArgb(17, 153, 142) } },
                        { "Sunset", new[] { Color.FromArgb(255, 94, 77), Color.FromArgb(255, 206, 84) } },
                        { "Ocean", new[] { Color.FromArgb(44, 62, 80), Color.FromArgb(52, 152, 219) } }
                    };

                    foreach (var gradientInfo in gradients)
                    {
                        var path = Path.Combine(categoryPath, $"{gradientInfo.Key}.png");
                        if (!File.Exists(path))
                        {
                            CreateGradientBackground(path, gradientInfo.Value[0], gradientInfo.Value[1]);
                            Log.Debug($"Created default gradient background: {gradientInfo.Key}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to create default backgrounds for {category}: {ex.Message}");
            }
        }

        private void CreateSolidColorBackground(string path, Color color)
        {
            using (var bitmap = new Bitmap(1920, 1080))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private void CreateGradientBackground(string path, Color color1, Color color2)
        {
            using (var bitmap = new Bitmap(1920, 1080))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                using (var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    color1, color2, LinearGradientMode.Vertical))
                {
                    graphics.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private List<VirtualBackground> LoadBackgroundsFromFolder(string category, string folderPath)
        {
            var backgrounds = new List<VirtualBackground>();

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
            var files = Directory.GetFiles(folderPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToArray();

            foreach (var file in files)
            {
                try
                {
                    var background = new VirtualBackground
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = Path.GetFileNameWithoutExtension(file),
                        Category = category,
                        FilePath = file,
                        ThumbnailPath = GenerateThumbnail(file),
                        IsDefault = false
                    };

                    backgrounds.Add(background);
                }
                catch (Exception ex)
                {
                    Log.Debug($"Failed to load background {file}: {ex.Message}");
                }
            }

            return backgrounds;
        }

        private string GenerateThumbnail(string imagePath)
        {
            var thumbnailFolder = Path.Combine(_backgroundsFolder, "Thumbnails");
            Directory.CreateDirectory(thumbnailFolder);

            var fileName = Path.GetFileNameWithoutExtension(imagePath);
            var thumbnailPath = Path.Combine(thumbnailFolder, $"{fileName}_thumb.jpg");

            if (!File.Exists(thumbnailPath))
            {
                try
                {
                    using (var original = Image.FromFile(imagePath))
                    {
                        var thumbWidth = 200;
                        var thumbHeight = (int)(original.Height * (thumbWidth / (float)original.Width));

                        using (var thumbnail = new Bitmap(thumbWidth, thumbHeight))
                        using (var graphics = Graphics.FromImage(thumbnail))
                        {
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(original, 0, 0, thumbWidth, thumbHeight);
                            thumbnail.Save(thumbnailPath, ImageFormat.Jpeg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"Failed to create thumbnail for {imagePath}: {ex.Message}");
                    return imagePath; // Use original as thumbnail
                }
            }

            return thumbnailPath;
        }

        #endregion

        #region Background Application

        public async Task<string> ApplyBackgroundAsync(string foregroundPath, string maskPath, string backgroundPath, string outputFolder)
        {
            return await ApplyBackgroundWithPositioningAsync(foregroundPath, maskPath, backgroundPath, outputFolder, null, 0);
        }

        /// <summary>
        /// Apply background with photo positioning data
        /// </summary>
        public async Task<string> ApplyBackgroundWithPositioningAsync(
            string foregroundPath,
            string maskPath,
            string backgroundPath,
            string outputFolder,
            PhotoPlacementData placementData = null,
            int photoIndex = 0)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string outputPath = Path.Combine(outputFolder,
                        $"{Path.GetFileNameWithoutExtension(foregroundPath)}_composed.jpg");

                    using (var foreground = Image.FromFile(foregroundPath) as Bitmap)
                    using (var mask = Image.FromFile(maskPath) as Bitmap)
                    using (var background = LoadOrCreateBackground(backgroundPath, foreground.Width, foreground.Height))
                    using (var result = new Bitmap(background.Width, background.Height))
                    using (var graphics = Graphics.FromImage(result))
                    {
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;

                        // Draw background
                        graphics.DrawImage(background, 0, 0, result.Width, result.Height);

                        // Check if we have positioning data
                        if (placementData != null && placementData.PlacementZones != null)
                        {
                            var zone = placementData.PlacementZones.FirstOrDefault(z => z.PhotoIndex == photoIndex && z.IsEnabled);
                            if (zone != null)
                            {
                                // Apply photo with positioning
                                ApplyPhotoWithPositioning(graphics, foreground, mask, zone, result.Width, result.Height);
                            }
                            else
                            {
                                // No zone for this photo index, use default centered placement
                                ApplyForegroundWithMask(graphics, foreground, mask);
                            }
                        }
                        else
                        {
                            // No positioning data, use default full-frame placement
                            ApplyForegroundWithMask(graphics, foreground, mask);
                        }

                        // Save result as JPEG with high quality
                        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                        result.Save(outputPath, encoder, encoderParams);
                    }

                    return outputPath;
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to apply virtual background: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Apply photo to specific position on background
        /// </summary>
        private void ApplyPhotoWithPositioning(Graphics graphics, Bitmap foreground, Bitmap mask, PhotoPlacementZone zone, int canvasWidth, int canvasHeight)
        {
            // Calculate actual position and size
            int x = (int)(zone.X * canvasWidth);
            int y = (int)(zone.Y * canvasHeight);
            int width = (int)(zone.Width * canvasWidth);
            int height = (int)(zone.Height * canvasHeight);

            // Create resized versions of foreground and mask
            using (var resizedForeground = new Bitmap(foreground, width, height))
            using (var resizedMask = new Bitmap(mask, width, height))
            {
                // Save current state
                var state = graphics.Save();

                // Apply rotation if needed
                if (Math.Abs(zone.Rotation) > 0.01)
                {
                    // Calculate center point for rotation
                    float centerX = x + width / 2f;
                    float centerY = y + height / 2f;

                    // Rotate around center
                    graphics.TranslateTransform(centerX, centerY);
                    graphics.RotateTransform((float)zone.Rotation);
                    graphics.TranslateTransform(-centerX, -centerY);
                }

                // Apply border/frame if configured
                if (zone.BorderSettings != null && zone.BorderSettings.ShowBorder)
                {
                    ApplyPhotoBorder(graphics, x, y, width, height, zone.BorderSettings);
                }

                // Draw the photo with mask at the specified position
                using (var photoWithAlpha = ApplyMaskToImage(resizedForeground, resizedMask))
                {
                    graphics.DrawImage(photoWithAlpha, x, y);
                }

                // Restore graphics state
                graphics.Restore(state);
            }
        }

        /// <summary>
        /// Apply mask to image and return image with alpha channel
        /// </summary>
        private Bitmap ApplyMaskToImage(Bitmap image, Bitmap mask)
        {
            var result = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    Color imageColor = image.GetPixel(x, y);
                    Color maskColor = mask.GetPixel(x, y);

                    // Use mask brightness as alpha
                    int alpha = maskColor.GetBrightness() > 0.5 ? 255 : 0;

                    result.SetPixel(x, y, Color.FromArgb(alpha, imageColor));
                }
            }

            return result;
        }

        /// <summary>
        /// Apply border/frame effects to photo
        /// </summary>
        private void ApplyPhotoBorder(Graphics graphics, int x, int y, int width, int height, PhotoBorderSettings settings)
        {
            // Apply drop shadow if enabled
            if (settings.ShowShadow)
            {
                using (var shadowBrush = new SolidBrush(Color.FromArgb((int)(settings.ShadowOpacity * 255), ColorTranslator.FromHtml(settings.ShadowColor))))
                {
                    graphics.FillRectangle(shadowBrush,
                        x + (float)settings.ShadowOffsetX,
                        y + (float)settings.ShadowOffsetY,
                        width, height);
                }
            }

            // Apply border
            if (settings.BorderThickness > 0)
            {
                using (var borderPen = new Pen(ColorTranslator.FromHtml(settings.BorderColor), (float)settings.BorderThickness))
                {
                    if (settings.CornerRadius > 0)
                    {
                        // Draw rounded rectangle
                        DrawRoundedRectangle(graphics, borderPen, x, y, width, height, (int)settings.CornerRadius);
                    }
                    else
                    {
                        // Draw regular rectangle
                        graphics.DrawRectangle(borderPen, x, y, width, height);
                    }
                }
            }
        }

        /// <summary>
        /// Draw a rounded rectangle
        /// </summary>
        private void DrawRoundedRectangle(Graphics graphics, Pen pen, int x, int y, int width, int height, int radius)
        {
            using (var path = new GraphicsPath())
            {
                path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
                path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
                path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();

                graphics.DrawPath(pen, path);
            }
        }

        private Bitmap LoadOrCreateBackground(string backgroundPath, int width, int height)
        {
            if (string.IsNullOrEmpty(backgroundPath) || !File.Exists(backgroundPath))
            {
                // Create default gradient background
                return CreateGradientBackground(width, height);
            }

            var background = Image.FromFile(backgroundPath) as Bitmap;

            // Resize if needed
            if (background.Width != width || background.Height != height)
            {
                var resized = new Bitmap(width, height);
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(background, 0, 0, width, height);
                }
                background.Dispose();
                return resized;
            }

            return background;
        }

        private Bitmap CreateGradientBackground(int width, int height)
        {
            var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                using (var brush = new LinearGradientBrush(
                    new Point(0, 0),
                    new Point(width, height),
                    Color.FromArgb(200, 200, 255),
                    Color.FromArgb(255, 200, 200)))
                {
                    graphics.FillRectangle(brush, 0, 0, width, height);
                }
            }
            return bitmap;
        }

        private void ApplyForegroundWithMask(Graphics graphics, Bitmap foreground, Bitmap mask)
        {
            // Create a new bitmap with alpha channel
            using (var foregroundWithAlpha = new Bitmap(foreground.Width, foreground.Height, PixelFormat.Format32bppArgb))
            {
                // Lock bits for faster processing
                var foregroundData = foreground.LockBits(
                    new Rectangle(0, 0, foreground.Width, foreground.Height),
                    ImageLockMode.ReadOnly,
                    foreground.PixelFormat);

                var maskData = mask.LockBits(
                    new Rectangle(0, 0, mask.Width, mask.Height),
                    ImageLockMode.ReadOnly,
                    mask.PixelFormat);

                var outputData = foregroundWithAlpha.LockBits(
                    new Rectangle(0, 0, foregroundWithAlpha.Width, foregroundWithAlpha.Height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    unsafe
                    {
                        byte* foregroundPtr = (byte*)foregroundData.Scan0;
                        byte* maskPtr = (byte*)maskData.Scan0;
                        byte* outputPtr = (byte*)outputData.Scan0;

                        int foregroundStride = foregroundData.Stride;
                        int maskStride = maskData.Stride;
                        int outputStride = outputData.Stride;

                        for (int y = 0; y < foreground.Height; y++)
                        {
                            byte* foregroundRow = foregroundPtr + (y * foregroundStride);
                            byte* maskRow = maskPtr + (y * maskStride);
                            byte* outputRow = outputPtr + (y * outputStride);

                            for (int x = 0; x < foreground.Width; x++)
                            {
                                // Get mask value (assuming grayscale mask)
                                byte alpha = maskRow[x * (maskData.Stride / mask.Width)];

                                // Get foreground pixel (assuming BGR or BGRA)
                                int foregroundIndex = x * (foregroundData.Stride / foreground.Width);
                                byte b = foregroundRow[foregroundIndex];
                                byte g = foregroundRow[foregroundIndex + 1];
                                byte r = foregroundRow[foregroundIndex + 2];

                                // Set output pixel with alpha from mask
                                int outputIndex = x * 4;
                                outputRow[outputIndex] = b;     // Blue
                                outputRow[outputIndex + 1] = g; // Green
                                outputRow[outputIndex + 2] = r; // Red
                                outputRow[outputIndex + 3] = alpha; // Alpha from mask
                            }
                        }
                    }
                }
                finally
                {
                    foreground.UnlockBits(foregroundData);
                    mask.UnlockBits(maskData);
                    foregroundWithAlpha.UnlockBits(outputData);
                }

                // Draw the foreground with alpha channel onto the background
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.DrawImage(foregroundWithAlpha, 0, 0, foreground.Width, foreground.Height);
            }
        }

        #endregion

        #region Public Methods

        public List<VirtualBackground> GetAllBackgrounds()
        {
            var allBackgrounds = new List<VirtualBackground>();
            foreach (var category in _backgroundsByCategory.Values)
            {
                allBackgrounds.AddRange(category);
            }
            return allBackgrounds;
        }

        public List<VirtualBackground> GetBackgroundsByCategory(string category)
        {
            if (_backgroundsByCategory.ContainsKey(category))
            {
                return _backgroundsByCategory[category];
            }
            return new List<VirtualBackground>();
        }

        public List<string> GetCategories()
        {
            return _backgroundsByCategory.Keys.ToList();
        }

        public VirtualBackground GetBackgroundById(string id)
        {
            foreach (var category in _backgroundsByCategory.Values)
            {
                var background = category.FirstOrDefault(b => b.Id == id);
                if (background != null)
                    return background;
            }
            return null;
        }

        public string GetDefaultBackgroundPath()
        {
            // Return selected background if set
            if (!string.IsNullOrEmpty(_selectedBackgroundPath))
            {
                return _selectedBackgroundPath;
            }

            // Otherwise check settings
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DefaultVirtualBackground))
            {
                _selectedBackgroundPath = Properties.Settings.Default.DefaultVirtualBackground;
                return _selectedBackgroundPath;
            }

            // Return first solid white background as default
            var solidBackgrounds = GetBackgroundsByCategory("Solid");
            var white = solidBackgrounds.FirstOrDefault(b => b.Name.Contains("White"));
            return white?.FilePath ?? "";
        }

        public void SetSelectedBackground(string backgroundPath)
        {
            _selectedBackgroundPath = backgroundPath;

            // Also save to settings for persistence
            Properties.Settings.Default.DefaultVirtualBackground = backgroundPath ?? "";
            Properties.Settings.Default.Save();
        }

        public string GetDefaultBackground()
        {
            // Return first solid white background as default
            var solidBackgrounds = GetBackgroundsByCategory("Solid");
            var white = solidBackgrounds.FirstOrDefault(b => b.Name.Contains("White"));
            return white?.FilePath ?? "";
        }

        public async Task<bool> AddCustomBackground(string imagePath, string name = null)
        {
            try
            {
                // Ensure custom folder exists
                var customFolder = Path.Combine(_backgroundsFolder, "Custom");
                Directory.CreateDirectory(customFolder);

                // Generate unique filename to avoid conflicts
                var baseName = name ?? Path.GetFileNameWithoutExtension(imagePath);
                var extension = Path.GetExtension(imagePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var uniqueFileName = $"{baseName}_{timestamp}{extension}";
                var destinationPath = Path.Combine(customFolder, uniqueFileName);

                // Ensure source file exists
                if (!File.Exists(imagePath))
                {
                    Log.Error($"Source file does not exist: {imagePath}");
                    return false;
                }

                // Copy file to custom folder
                await Task.Run(() => File.Copy(imagePath, destinationPath, true));

                // Verify the file was copied
                if (!File.Exists(destinationPath))
                {
                    Log.Error($"Failed to copy file to: {destinationPath}");
                    return false;
                }

                // Generate thumbnail
                var thumbnailPath = GenerateThumbnail(destinationPath);

                // Create background entry
                var background = new VirtualBackground
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = baseName,
                    Category = "Custom",
                    FilePath = destinationPath,
                    ThumbnailPath = thumbnailPath,
                    IsDefault = false
                };

                if (!_backgroundsByCategory.ContainsKey("Custom"))
                {
                    _backgroundsByCategory["Custom"] = new List<VirtualBackground>();
                }

                _backgroundsByCategory["Custom"].Add(background);

                Log.Debug($"Successfully added custom background: {uniqueFileName} at {destinationPath}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to add custom background: {ex.Message}");
                return false;
            }
        }

        public void ApplyBlur(string imagePath, int blurRadius)
        {
            // Apply blur effect to background for depth
            // Implementation would use image processing library
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _backgroundsByCategory?.Clear();
            _isInitialized = false;
            Log.Debug("VirtualBackgroundService disposed");
        }

        #endregion
    }

    #region Supporting Classes

    public class VirtualBackground
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string FilePath { get; set; }
        public string ThumbnailPath { get; set; }
        public bool IsDefault { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    #endregion
}