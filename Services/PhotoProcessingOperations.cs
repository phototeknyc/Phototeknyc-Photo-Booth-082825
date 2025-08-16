using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using Photobooth.Database;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles all photo processing operations including filters, template composition, and image generation
    /// </summary>
    public class PhotoProcessingOperations
    {
        private readonly Pages.PhotoboothTouchModern _parent;
        private PhotoFilterServiceHybrid _filterService;
        
        // UI Elements
        private readonly Grid postSessionFilterOverlay;
        private readonly ItemsControl filterItemsControl;
        private readonly System.Windows.Controls.Image filterPreviewImage;
        private readonly TextBlock statusText;
        
        // Properties
        public PhotoFilterServiceHybrid FilterService 
        { 
            get
            {
                if (_filterService == null)
                {
                    _filterService = new PhotoFilterServiceHybrid();
                }
                return _filterService;
            }
        }
        
        public PhotoProcessingOperations(Pages.PhotoboothTouchModern parent)
        {
            _parent = parent;
            
            // Get UI elements from parent
            postSessionFilterOverlay = parent.FindName("postSessionFilterOverlay") as Grid;
            filterItemsControl = parent.FindName("filterItemsControl") as ItemsControl;
            filterPreviewImage = parent.FindName("filterPreviewImage") as System.Windows.Controls.Image;
            statusText = parent.FindName("statusText") as TextBlock;
        }
        
        /// <summary>
        /// Apply filter to a photo
        /// </summary>
        public async Task<string> ApplyFilterToPhoto(string inputPath, FilterType filterType, bool isPreview = false)
        {
            try
            {
                if (filterType == FilterType.None)
                    return inputPath;
                
                if (!File.Exists(inputPath))
                {
                    Log.Error($"ApplyFilterToPhoto: Input file not found: {inputPath}");
                    return inputPath;
                }
                
                // Generate output path
                string outputDir = Path.GetDirectoryName(inputPath);
                string outputFileName = isPreview 
                    ? $"{Path.GetFileNameWithoutExtension(inputPath)}_preview_{filterType}.jpg"
                    : $"{Path.GetFileNameWithoutExtension(inputPath)}_{filterType}.jpg";
                string outputPath = Path.Combine(outputDir, outputFileName);
                
                // Use cached preview if available
                if (isPreview && File.Exists(outputPath))
                {
                    Log.Debug($"ApplyFilterToPhoto: Using cached preview for {filterType}");
                    return outputPath;
                }
                
                // Apply filter using filter service
                if (FilterService == null)
                {
                    Log.Error("ApplyFilterToPhoto: Filter service is not initialized");
                    return inputPath;
                }
                
                string result = await Task.Run(() => FilterService.ApplyFilterToFile(inputPath, outputPath, filterType));
                Log.Debug($"ApplyFilterToPhoto: Applied {filterType} filter to {inputPath}, result: {result}");
                
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"ApplyFilterToPhoto: Failed to apply filter {filterType} to {inputPath}: {ex.Message}");
                return inputPath; // Return original if filter fails
            }
        }
        
        /// <summary>
        /// Compose template with photos
        /// </summary>
        public async Task<string> ComposeTemplateWithPhotos(TemplateData currentTemplate, List<string> capturedPhotoPaths, 
            EventData currentEvent, TemplateDatabase database)
        {
            try
            {
                if (currentTemplate == null || capturedPhotoPaths.Count == 0)
                {
                    Log.Error("ComposeTemplateWithPhotos: No template or photos available");
                    return null;
                }
                
                Log.Debug($"ComposeTemplateWithPhotos: Starting composition with {capturedPhotoPaths.Count} photos");
                Log.Debug($"ComposeTemplateWithPhotos: Template ID={currentTemplate.Id}, Name={currentTemplate.Name}");
                
                // Load template data and canvas items from database
                var canvasItems = database.GetCanvasItems(currentTemplate.Id);
                if (canvasItems == null || canvasItems.Count == 0)
                {
                    Log.Error($"ComposeTemplateWithPhotos: No canvas items found for template {currentTemplate.Id}");
                    return null;
                }
                
                // Use template dimensions from database
                int templateWidth = (int)currentTemplate.CanvasWidth;
                int templateHeight = (int)currentTemplate.CanvasHeight;
                
                // Create bitmap for composition
                var finalBitmap = new Bitmap(templateWidth, templateHeight);
                using (var graphics = Graphics.FromImage(finalBitmap))
                {
                    // Set high quality rendering
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    
                    // Fill with white background
                    graphics.Clear(System.Drawing.Color.White);
                    
                    // Process canvas items in Z-order  
                    var sortedItems = canvasItems.OrderBy(item => item.ZIndex).ToList();
                    
                    Log.Debug($"ComposeTemplateWithPhotos: Processing {sortedItems.Count} canvas items");
                    
                    foreach (var item in sortedItems)
                    {
                        await DrawCanvasItem(graphics, item, capturedPhotoPaths, currentEvent);
                    }
                }
                
                // Save the composed image
                string outputPath = GenerateOutputPath(currentEvent, currentTemplate);
                SaveComposedImage(finalBitmap, outputPath);
                
                // Handle 2x6 template duplication if needed
                string printPath = await HandleTemplateForPrinting(finalBitmap, templateWidth, templateHeight, 
                    outputPath, currentEvent);
                
                // Update parent with processed paths
                _parent.UpdateProcessedImagePaths(outputPath, printPath);
                
                // Determine correct output format for database - CRITICAL for proper printer routing
                bool is2x6Template = Is2x6Template(templateWidth, templateHeight);
                string outputFormat = is2x6Template ? "2x6" : "4x6";
                
                Log.Debug($"★★★ TEMPLATE FORMAT DEBUG: templateWidth={templateWidth}, templateHeight={templateHeight}, is2x6Template={is2x6Template}, outputFormat='{outputFormat}' ★★★");
                
                // Save to database with correct format
                _parent.SaveComposedImageToDatabase(outputPath, outputFormat);
                
                // Add to photo strip
                _parent.Dispatcher.BeginInvoke(new Action(() => _parent.AddComposedImageToPhotoStrip(outputPath)));
                
                finalBitmap.Dispose();
                
                Log.Debug($"ComposeTemplateWithPhotos: Completed. Output: {outputPath}");
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error("ComposeTemplateWithPhotos: Failed to compose template", ex);
                return null;
            }
        }
        
        /// <summary>
        /// Draw a single canvas item
        /// </summary>
        private async Task DrawCanvasItem(Graphics graphics, CanvasItemData item, List<string> capturedPhotoPaths, EventData currentEvent)
        {
            var destRect = new Rectangle(
                (int)item.X,
                (int)item.Y,
                (int)item.Width,
                (int)item.Height);
            
            Log.Debug($"Drawing item - Type={item.ItemType}, Name={item.Name}, ZIndex={item.ZIndex}");
            
            // Save graphics state before applying rotation
            var savedState = graphics.Save();
            
            // Apply rotation if needed
            if (item.Rotation != 0)
            {
                float centerX = destRect.X + destRect.Width / 2f;
                float centerY = destRect.Y + destRect.Height / 2f;
                graphics.TranslateTransform(centerX, centerY);
                graphics.RotateTransform((float)item.Rotation);
                graphics.TranslateTransform(-centerX, -centerY);
            }
            
            // Draw based on item type
            if (item.ItemType == "PlaceholderCanvasItem" || item.ItemType == "Placeholder")
            {
                DrawPhotoPlaceholder(graphics, item, destRect, capturedPhotoPaths);
            }
            else if ((item.ItemType == "ImageCanvasItem" || item.ItemType == "Image") && !string.IsNullOrEmpty(item.ImagePath))
            {
                DrawImage(graphics, item, destRect, currentEvent);
            }
            else if (item.ItemType == "Text")
            {
                DrawText(graphics, item, destRect);
            }
            else if (item.ItemType == "Shape")
            {
                DrawShape(graphics, item, destRect);
            }
            
            // Restore graphics state after drawing
            graphics.Restore(savedState);
        }
        
        /// <summary>
        /// Draw photo placeholder with captured photo
        /// </summary>
        private void DrawPhotoPlaceholder(Graphics graphics, CanvasItemData item, Rectangle destRect, List<string> capturedPhotoPaths)
        {
            int placeholderIndex = (item.PlaceholderNumber ?? 1) - 1;
            
            if (placeholderIndex >= 0 && placeholderIndex < capturedPhotoPaths.Count)
            {
                var photoPath = capturedPhotoPaths[placeholderIndex];
                Log.Debug($"Inserting photo into placeholder {item.PlaceholderNumber} from {photoPath}");
                
                if (File.Exists(photoPath))
                {
                    using (var photo = System.Drawing.Image.FromFile(photoPath))
                    {
                        // Calculate source rectangle to maintain aspect ratio
                        Rectangle sourceRect;
                        float destAspect = (float)destRect.Width / destRect.Height;
                        float photoAspect = (float)photo.Width / photo.Height;
                        
                        if (photoAspect > destAspect)
                        {
                            // Photo is wider - crop width
                            int cropWidth = (int)(photo.Height * destAspect);
                            int xOffset = (photo.Width - cropWidth) / 2;
                            sourceRect = new Rectangle(xOffset, 0, cropWidth, photo.Height);
                        }
                        else
                        {
                            // Photo is taller - crop height
                            int cropHeight = (int)(photo.Width / destAspect);
                            int yOffset = (photo.Height - cropHeight) / 2;
                            sourceRect = new Rectangle(0, yOffset, photo.Width, cropHeight);
                        }
                        
                        graphics.DrawImage(photo, destRect, sourceRect, GraphicsUnit.Pixel);
                        Log.Debug($"Successfully drew photo into placeholder {item.PlaceholderNumber}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Draw image item
        /// </summary>
        private void DrawImage(Graphics graphics, CanvasItemData item, Rectangle destRect, EventData currentEvent)
        {
            try
            {
                // Build image path for template asset
                string imagePath = item.ImagePath;
                
                if (imagePath.StartsWith("file:///"))
                {
                    imagePath = imagePath.Substring(8);
                }
                else if (imagePath.StartsWith("file://"))
                {
                    imagePath = imagePath.Substring(7);
                }
                
                if (File.Exists(imagePath))
                {
                    using (var img = System.Drawing.Image.FromFile(imagePath))
                    {
                        graphics.CompositingMode = CompositingMode.SourceOver;
                        graphics.DrawImage(img, destRect);
                        Log.Debug($"Drew image from path {imagePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Failed to draw image: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Draw text item
        /// </summary>
        private void DrawText(Graphics graphics, CanvasItemData item, Rectangle destRect)
        {
            try
            {
                if (!string.IsNullOrEmpty(item.Text))
                {
                    // Parse font settings
                    float fontSize = (float)(item.FontSize ?? 20);
                    string fontFamily = item.FontFamily ?? "Arial";
                    System.Drawing.FontStyle fontStyle = System.Drawing.FontStyle.Regular;
                    
                    if (item.FontWeight == "Bold")
                        fontStyle |= System.Drawing.FontStyle.Bold;
                    if (item.FontStyle == "Italic")
                        fontStyle |= System.Drawing.FontStyle.Italic;
                    
                    using (var font = new Font(fontFamily, (float)fontSize, fontStyle))
                    using (var brush = new SolidBrush(ParseColor(item.TextColor)))
                    {
                        var format = new StringFormat();
                        
                        // Set alignment
                        if (item.TextAlignment == "Center")
                            format.Alignment = StringAlignment.Center;
                        else if (item.TextAlignment == "Right")
                            format.Alignment = StringAlignment.Far;
                        
                        graphics.DrawString(item.Text, font, brush, destRect, format);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Failed to draw text: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Draw shape item
        /// </summary>
        private void DrawShape(Graphics graphics, CanvasItemData item, Rectangle destRect)
        {
            try
            {
                Brush fillBrush = null;
                Pen strokePen = null;
                
                // Create fill brush if needed
                if (!string.IsNullOrEmpty(item.FillColor))
                {
                    fillBrush = new SolidBrush(ParseColor(item.FillColor));
                }
                
                // Create stroke pen if needed
                if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0)
                {
                    strokePen = new Pen(ParseColor(item.StrokeColor), (float)item.StrokeThickness);
                }
                
                // Draw the shape
                if (item.ShapeType == "Rectangle")
                {
                    if (fillBrush != null)
                        graphics.FillRectangle(fillBrush, destRect);
                    if (strokePen != null)
                        graphics.DrawRectangle(strokePen, destRect);
                }
                else if (item.ShapeType == "Ellipse")
                {
                    if (fillBrush != null)
                        graphics.FillEllipse(fillBrush, destRect);
                    if (strokePen != null)
                        graphics.DrawEllipse(strokePen, destRect);
                }
                
                fillBrush?.Dispose();
                strokePen?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug($"Failed to draw shape: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Parse color string to Color
        /// </summary>
        private System.Drawing.Color ParseColor(string colorString)
        {
            if (string.IsNullOrEmpty(colorString))
                return System.Drawing.Color.Black;
            
            try
            {
                if (colorString.StartsWith("#"))
                {
                    return System.Drawing.ColorTranslator.FromHtml(colorString);
                }
                return System.Drawing.Color.FromName(colorString);
            }
            catch
            {
                return System.Drawing.Color.Black;
            }
        }
        
        /// <summary>
        /// Generate output path for composed image
        /// </summary>
        private string GenerateOutputPath(EventData currentEvent, TemplateData currentTemplate)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string eventFolderName = SanitizeFileName(currentEvent.Name);
            string outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth", eventFolderName);
            
            Directory.CreateDirectory(outputDir);
            
            return Path.Combine(outputDir, $"{currentEvent.Name}_{timestamp}_composed.jpg");
        }
        
        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Unknown";
            
            // Remove invalid path characters
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            
            return fileName;
        }
        
        /// <summary>
        /// Save composed image to file
        /// </summary>
        private void SaveComposedImage(Bitmap bitmap, string outputPath)
        {
            // Get JPEG encoder
            ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);
            
            // Set encoder parameters for quality
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
            
            bitmap.Save(outputPath, jpegEncoder, encoderParams);
            Log.Debug($"Saved composed image to {outputPath}");
        }
        
        /// <summary>
        /// Get image encoder
        /// </summary>
        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Handle template for printing (2x6 duplication)
        /// </summary>
        private async Task<string> HandleTemplateForPrinting(Bitmap finalBitmap, int templateWidth, int templateHeight,
            string outputPath, EventData currentEvent)
        {
            bool is2x6Template = Is2x6Template(templateWidth, templateHeight);
            bool duplicate2x6To4x6 = true; // Default to duplicating 2x6 templates
            
            if (is2x6Template && duplicate2x6To4x6)
            {
                Log.Debug($"Detected 2x6 template, creating 4x6 version for printing");
                
                using (var duplicatedBitmap = Create4x6From2x6(finalBitmap))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string outputDir = Path.GetDirectoryName(outputPath);
                    string printPath = Path.Combine(outputDir, $"{currentEvent.Name}_{timestamp}_4x6_print.jpg");
                    
                    SaveComposedImage(duplicatedBitmap, printPath);
                    Log.Debug($"Saved 4x6 print version to {printPath}");
                    return printPath;
                }
            }
            
            return outputPath;
        }
        
        /// <summary>
        /// Check if template is 2x6
        /// </summary>
        private bool Is2x6Template(int width, int height)
        {
            int targetDpi = 300; // Default print DPI
            
            // 2x6 at common DPIs
            int[] validDpis = { 150, 300, 600 };
            foreach (int dpi in validDpis)
            {
                int expected2inchPixels = 2 * dpi;
                int expected6inchPixels = 6 * dpi;
                
                // Allow 5% tolerance
                if (Math.Abs(width - expected2inchPixels) < expected2inchPixels * 0.05 &&
                    Math.Abs(height - expected6inchPixels) < expected6inchPixels * 0.05)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Create 4x6 from 2x6 by duplicating side by side
        /// </summary>
        private Bitmap Create4x6From2x6(Bitmap source2x6)
        {
            int targetDpi = 300; // Default print DPI
            int width4x6 = 4 * targetDpi;
            int height4x6 = 6 * targetDpi;
            
            var result = new Bitmap(width4x6, height4x6);
            using (var g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                
                // Draw left half
                g.DrawImage(source2x6, new Rectangle(0, 0, width4x6 / 2, height4x6));
                
                // Draw right half (duplicate)
                g.DrawImage(source2x6, new Rectangle(width4x6 / 2, 0, width4x6 / 2, height4x6));
            }
            
            return result;
        }
        
        /// <summary>
        /// Generate filter previews for selection
        /// </summary>
        public async Task GenerateFilterPreviews(string samplePhotoPath, ItemsControl filterItemsControl)
        {
            if (!Properties.Settings.Default.EnableFilters)
            {
                Log.Debug("Filters disabled, skipping preview generation");
                return;
            }
            
            var filterItems = new List<FilterItem>();
            
            // Add None filter first
            filterItems.Add(new FilterItem 
            { 
                FilterType = FilterType.None,
                DisplayName = "None",
                PreviewImage = new BitmapImage(new Uri(samplePhotoPath))
            });
            
            // Generate previews for each filter
            foreach (FilterType filterType in Enum.GetValues(typeof(FilterType)))
            {
                if (filterType == FilterType.None) continue;
                
                try
                {
                    var filterTask = ApplyFilterToPhoto(samplePhotoPath, filterType, true);
                    var timeoutTask = Task.Delay(2000);
                    
                    var completedTask = await Task.WhenAny(filterTask, timeoutTask);
                    
                    if (completedTask == filterTask)
                    {
                        string previewPath = await filterTask;
                        if (File.Exists(previewPath))
                        {
                            filterItems.Add(new FilterItem
                            {
                                FilterType = filterType,
                                DisplayName = filterType.ToString(),
                                PreviewImage = new BitmapImage(new Uri(previewPath))
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to generate preview for {filterType}: {ex.Message}");
                }
            }
            
            // Update UI
            filterItemsControl.ItemsSource = filterItems;
        }
        
        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            // PhotoFilterServiceHybrid doesn't implement IDisposable
            _filterService = null;
        }
    }
    
    /// <summary>
    /// Filter item for UI display
    /// </summary>
    public class FilterItem
    {
        public FilterType FilterType { get; set; }
        public string DisplayName { get; set; }
        public BitmapImage PreviewImage { get; set; }
    }
}