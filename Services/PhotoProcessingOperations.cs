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
using QRCoder;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles all photo processing operations including filters, template composition, and image generation
    /// </summary>
    public class PhotoProcessingOperations
    {
        private readonly dynamic _parent;
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
        
        public PhotoProcessingOperations(Pages.PhotoboothTouchModernRefactored parent)
        {
            _parent = parent;
            
            // Get UI elements from parent
            postSessionFilterOverlay = parent.FindName("postSessionFilterOverlay") as Grid;
            filterItemsControl = parent.FindName("filterItemsControl") as ItemsControl;
            filterPreviewImage = parent.FindName("filterPreviewImage") as System.Windows.Controls.Image;
            statusText = parent.FindName("statusText") as TextBlock;
        }

        // Constructor for service-based usage with adapter
        public PhotoProcessingOperations(dynamic serviceAdapter)
        {
            _parent = serviceAdapter;
            
            // No UI elements needed for service-based composition
            postSessionFilterOverlay = null;
            filterItemsControl = null;
            filterPreviewImage = null;
            statusText = null;
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
                    
                    // Fill with template background color or white as default
                    var backgroundColor = System.Drawing.Color.White;
                    if (!string.IsNullOrEmpty(currentTemplate.BackgroundColor))
                    {
                        try
                        {
                            // Parse WPF color format (e.g., #FFRRGGBB or #RRGGBB)
                            var colorString = currentTemplate.BackgroundColor;
                            if (colorString.StartsWith("#") && colorString.Length == 9)
                            {
                                // WPF format with alpha channel (#AARRGGBB)
                                var alpha = Convert.ToByte(colorString.Substring(1, 2), 16);
                                var red = Convert.ToByte(colorString.Substring(3, 2), 16);
                                var green = Convert.ToByte(colorString.Substring(5, 2), 16);
                                var blue = Convert.ToByte(colorString.Substring(7, 2), 16);
                                backgroundColor = System.Drawing.Color.FromArgb(alpha, red, green, blue);
                            }
                            else if (colorString.StartsWith("#") && colorString.Length == 7)
                            {
                                // Standard HTML format (#RRGGBB)
                                backgroundColor = System.Drawing.ColorTranslator.FromHtml(colorString);
                            }
                            else
                            {
                                // Try other formats (named colors, etc.)
                                backgroundColor = System.Drawing.ColorTranslator.FromHtml(colorString);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"ComposeTemplateWithPhotos: Could not parse background color '{currentTemplate.BackgroundColor}': {ex.Message}");
                        }
                    }
                    graphics.Clear(backgroundColor);
                    
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
                
                // Debug logging
                Log.Debug($"ComposeTemplateWithPhotos: outputPath = {outputPath}");
                Log.Debug($"ComposeTemplateWithPhotos: printPath = {printPath}");
                Log.Debug($"ComposeTemplateWithPhotos: Are paths different? {outputPath != printPath}");
                
                // Verify the print file actually exists
                if (printPath != outputPath && File.Exists(printPath))
                {
                    using (var img = System.Drawing.Image.FromFile(printPath))
                    {
                        Log.Debug($"ComposeTemplateWithPhotos: Print file verified - dimensions: {img.Width}x{img.Height}");
                    }
                }
                
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
            else if (string.Equals(item.ItemType, "QRCode", StringComparison.OrdinalIgnoreCase))
            {
                DrawQrCode(graphics, item, destRect, currentEvent);
            }
            else if (string.Equals(item.ItemType, "Barcode", StringComparison.OrdinalIgnoreCase))
            {
                DrawBarcode(graphics, item, destRect, currentEvent);
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

                // DON'T use the transparent PNG for template composition
                // The transparent PNG is only for virtual background processing
                // If a virtual background was applied, the original photo would have been replaced
                // If no virtual background, we want to use the original photo with its background

                // Just use the photo as-is (either original or replaced with virtual background)
                Log.Debug($"Using photo for composition: {photoPath}");

                Log.Debug($"Inserting photo into placeholder {item.PlaceholderNumber} from {photoPath}");

                if (File.Exists(photoPath))
                {
                    using (var photo = System.Drawing.Image.FromFile(photoPath))
                    {
                        // Check if virtual background is enabled and this photo already has it applied
                        bool hasVirtualBackground = Properties.Settings.Default.EnableBackgroundRemoval &&
                                                   !string.IsNullOrEmpty(VirtualBackgroundService.Instance.GetDefaultBackgroundPath()) &&
                                                   File.Exists(VirtualBackgroundService.Instance.GetDefaultBackgroundPath());

                        Rectangle sourceRect;

                        if (hasVirtualBackground)
                        {
                            // Photo already has virtual background with positioning applied
                            // Use the entire image without cropping to preserve the positioning
                            Log.Debug($"Photo has virtual background - using full image without cropping");
                            sourceRect = new Rectangle(0, 0, photo.Width, photo.Height);
                        }
                        else
                        {
                            // Normal photo without virtual background - apply standard cropping
                            // Calculate source rectangle to maintain aspect ratio
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
                        }

                        // Draw shadow if enabled
                        if (item.HasShadow && !string.IsNullOrEmpty(item.ShadowColor))
                        {
                            var shadowColor = ParseColor(item.ShadowColor);
                            int blur = (int)Math.Max(0, Math.Round(item.ShadowBlurRadius));
                            int offX = (int)Math.Round(item.ShadowOffsetX);
                            int offY = (int)Math.Round(item.ShadowOffsetY);

                            // Create a shadow mask from the photo
                            using (var mask = new Bitmap(destRect.Width, destRect.Height, PixelFormat.Format32bppArgb))
                            using (var mg = Graphics.FromImage(mask))
                            {
                                mg.SmoothingMode = SmoothingMode.AntiAlias;
                                mg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                mg.DrawImage(photo, new Rectangle(0, 0, destRect.Width, destRect.Height), sourceRect, GraphicsUnit.Pixel);

                                using (var shadowBmp = CreateShadowFromAlpha(mask, shadowColor, blur))
                                {
                                    graphics.DrawImage(shadowBmp, new Rectangle(destRect.X + offX, destRect.Y + offY, destRect.Width, destRect.Height));
                                }
                            }
                        }

                        // Draw the photo
                        graphics.DrawImage(photo, destRect, sourceRect, GraphicsUnit.Pixel);

                        // Draw stroke/border if enabled
                        if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0)
                        {
                            using (var pen = new Pen(ParseColor(item.StrokeColor), (float)item.StrokeThickness))
                            {
                                pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                                graphics.DrawRectangle(pen, destRect);
                            }
                        }

                        Log.Debug($"Successfully drew photo into placeholder {item.PlaceholderNumber}");
                    }
                }
            }
        }

        /// <summary>
        /// Get the path to the background-removed version of a photo
        /// </summary>
        private string GetBackgroundRemovedPath(string originalPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(originalPath);
                var fileName = Path.GetFileNameWithoutExtension(originalPath);
                var extension = Path.GetExtension(originalPath);

                // Check for background removed folder
                var bgRemovedFolder = Path.Combine(directory, "BackgroundRemoved");
                if (Directory.Exists(bgRemovedFolder))
                {
                    // Look for the processed file with _nobg suffix
                    var processedFile = Path.Combine(bgRemovedFolder, $"{fileName}_composed{extension}");
                    if (File.Exists(processedFile))
                    {
                        return processedFile;
                    }

                    // Also check for _nobg version
                    processedFile = Path.Combine(bgRemovedFolder, $"{fileName}_nobg.png");
                    if (File.Exists(processedFile))
                    {
                        return processedFile;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Error checking for background-removed photo: {ex.Message}");
            }

            return originalPath;
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
                        // Shadow derived from image alpha (if any)
                        if (item.HasShadow && !string.IsNullOrEmpty(item.ShadowColor))
                        {
                            var shadowColor = ParseColor(item.ShadowColor);
                            int blur = (int)Math.Max(0, Math.Round(item.ShadowBlurRadius));
                            int offX = (int)Math.Round(item.ShadowOffsetX);
                            int offY = (int)Math.Round(item.ShadowOffsetY);
                            using (var mask = new Bitmap(destRect.Width, destRect.Height, PixelFormat.Format32bppArgb))
                            using (var mg = Graphics.FromImage(mask))
                            {
                                mg.SmoothingMode = SmoothingMode.AntiAlias;
                                mg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                mg.DrawImage(img, new Rectangle(0, 0, destRect.Width, destRect.Height));
                                using (var shadowBmp = CreateShadowFromAlpha(mask, shadowColor, blur))
                                {
                                    graphics.DrawImage(shadowBmp, new Rectangle(destRect.X + offX, destRect.Y + offY, destRect.Width, destRect.Height));
                                }
                            }
                        }

                        graphics.CompositingMode = CompositingMode.SourceOver;
                        graphics.DrawImage(img, destRect);
                        Log.Debug($"Drew image from path {imagePath}");
                    }

                    // Optional stroke/border around image
                    if (!string.IsNullOrEmpty(item.StrokeColor) && item.StrokeThickness > 0)
                    {
                        using (var pen = new Pen(ParseColor(item.StrokeColor), (float)item.StrokeThickness))
                        {
                            pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                            graphics.DrawRectangle(pen, destRect);
                        }
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
                    // Prefer WPF text rendering for exact visual match with designer
                    if (TryDrawTextWithWpf(graphics, item, destRect))
                        return;

                    // Fallback to GDI+ path (close approximation)
                    // Convert WPF font size (device-independent pixels) to GDI+ font size
                    double wpfFontSize = item.FontSize ?? 20.0;

                    // WPF uses device-independent pixels (96 DPI), GDI+ Font constructor with Point units
                    // also assumes 96 DPI, so direct conversion should work
                    // However, to match exactly, we convert to points: DIP * (72/96) = points
                    float fontSizeInPoints = (float)(wpfFontSize * 72.0 / 96.0);
                    Log.Debug($"DrawText GDI+ fallback: WPF FontSize={wpfFontSize}, Points={fontSizeInPoints}");

                    string fontFamily = item.FontFamily ?? "Arial";
                    System.Drawing.FontStyle fontStyle = System.Drawing.FontStyle.Regular;
                    if (string.Equals(item.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase))
                        fontStyle |= System.Drawing.FontStyle.Bold;
                    if (string.Equals(item.FontStyle, "Italic", StringComparison.OrdinalIgnoreCase))
                        fontStyle |= System.Drawing.FontStyle.Italic;

                    // Use Point units for better WPF compatibility
                    using (var font = new Font(fontFamily, fontSizeInPoints, fontStyle, GraphicsUnit.Point))
                    {
                        // Anti-aliased grayscale tends to look better in saved bitmaps than ClearType
                        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                        // Account for the 5px TextBlock margin in designer (left/top/right/bottom)
                        var innerRect = new Rectangle(destRect.X + 5, destRect.Y + 5, Math.Max(0, destRect.Width - 10), Math.Max(0, destRect.Height - 10));

                        var format = new StringFormat();
                        // Center vertically like SimpleTextItem does
                        format.LineAlignment = StringAlignment.Center;

                        if (string.Equals(item.TextAlignment, "Center", StringComparison.OrdinalIgnoreCase))
                            format.Alignment = StringAlignment.Center;
                        else if (string.Equals(item.TextAlignment, "Right", StringComparison.OrdinalIgnoreCase))
                            format.Alignment = StringAlignment.Far;
                        else
                            format.Alignment = StringAlignment.Near;

                        // Draw shadow if enabled
                        if (item.HasShadow && !string.IsNullOrEmpty(item.ShadowColor))
                        {
                            var shadowColor = ParseColor(item.ShadowColor);
                            int offX = (int)Math.Round(item.ShadowOffsetX);
                            int offY = (int)Math.Round(item.ShadowOffsetY);

                            using (var shadowBrush = new SolidBrush(System.Drawing.Color.FromArgb(128, shadowColor)))
                            {
                                var shadowRect = new Rectangle(innerRect.X + offX, innerRect.Y + offY, innerRect.Width, innerRect.Height);
                                graphics.DrawString(item.Text, font, shadowBrush, shadowRect, format);
                            }
                        }

                        // Draw outline/stroke if enabled (using HasOutline from text items)
                        if (item.HasOutline && item.OutlineThickness > 0 && !string.IsNullOrEmpty(item.OutlineColor))
                        {
                            using (var path = new GraphicsPath())
                            {
                                // Add text to path for outline effect
                                path.AddString(item.Text, new FontFamily(fontFamily), (int)fontStyle,
                                    fontSizeInPoints * graphics.DpiY / 72.0f, innerRect, format);

                                // Draw outline
                                using (var pen = new Pen(ParseColor(item.OutlineColor), (float)item.OutlineThickness))
                                {
                                    pen.LineJoin = LineJoin.Round;
                                    graphics.DrawPath(pen, path);
                                }

                                // Fill text on top
                                using (var brush = new SolidBrush(ParseColor(item.TextColor)))
                                {
                                    graphics.FillPath(brush, path);
                                }
                            }
                        }
                        else
                        {
                            // Draw text normally without outline
                            using (var brush = new SolidBrush(ParseColor(item.TextColor)))
                            {
                                graphics.DrawString(item.Text, font, brush, innerRect, format);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Failed to draw text: {ex.Message}");
            }
        }

        /// <summary>
        /// Draw barcode item (Code39) from template CustomProperties
        /// </summary>
        private void DrawBarcode(Graphics graphics, CanvasItemData item, Rectangle destRect, EventData currentEvent)
        {
            try
            {
                // Defaults
                string value = null;
                string symbology = "Code39";
                double moduleWidth = 2.0; // width of a narrow bar in units
                bool includeLabel = true;

                if (!string.IsNullOrWhiteSpace(item.CustomProperties))
                {
                    try
                    {
                        dynamic props = JsonConvert.DeserializeObject(item.CustomProperties);
                        value = props?.Value != null ? (string)props.Value : null;
                        if (props?.Symbology != null) symbology = (string)props.Symbology;
                        if (props?.ModuleWidth != null) double.TryParse(props.ModuleWidth.ToString(), out moduleWidth);
                        if (props?.IncludeLabel != null) bool.TryParse(props.IncludeLabel.ToString(), out includeLabel);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"DrawBarcode: Failed to parse CustomProperties: {ex.Message}");
                    }
                }

                // Resolve tokens (supports {SESSION.URL}, {EVENT.URL}, etc.)
                value = ResolveTokensForComposition(value, currentEvent);
                if (string.IsNullOrWhiteSpace(value))
                {
                    Log.Debug("DrawBarcode: Empty value after token resolution; skipping barcode draw");
                    return;
                }

                // Only Code39 is supported for now
                if (!string.Equals(symbology, "Code39", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug($"DrawBarcode: Unsupported symbology '{symbology}', expected Code39");
                    return;
                }

                // Prepare Code39 patterns
                var patterns = GetCode39Patterns();
                string raw = value.ToUpperInvariant();
                // Replace unsupported chars with '-'
                var sb = new System.Text.StringBuilder();
                foreach (var ch in raw)
                {
                    sb.Append(patterns.ContainsKey(ch) ? ch : '-');
                }
                raw = sb.ToString();

                // Full data includes start/stop '*'
                string data = "*" + raw + "*";

                // Compute total units to scale to dest width
                // Each pattern has 9 elements; 'w' counts as 3 units, 'n' as 1 unit; inter-character space adds 1 narrow unit
                int totalUnits = 0;
                foreach (char ch in data)
                {
                    if (!patterns.TryGetValue(ch, out var pat)) continue;
                    foreach (char c in pat)
                        totalUnits += (c == 'w') ? 3 : 1;
                    totalUnits += 1; // inter-character space
                }
                if (totalUnits <= 0) return;

                // Scale units to pixels to fit exactly within destRect width
                double pixelsPerUnit = destRect.Width / (double)totalUnits;
                if (pixelsPerUnit <= 0) return;

                // Height allocation
                int labelHeight = includeLabel ? Math.Min(24, Math.Max(14, destRect.Height / 6)) : 0;
                int barHeight = Math.Max(1, destRect.Height - labelHeight);

                // Render bars
                using (var brush = new SolidBrush(System.Drawing.Color.Black))
                {
                    double x = destRect.X;
                    foreach (char ch in data)
                    {
                        if (!patterns.TryGetValue(ch, out var pat)) continue;
                        bool drawBar = true; // patterns alternate bar/space starting with bar
                        foreach (char c in pat)
                        {
                            int units = (c == 'w') ? 3 : 1;
                            double w = units * pixelsPerUnit;
                            if (drawBar)
                            {
                                var rect = new Rectangle((int)Math.Round(x), destRect.Y, (int)Math.Round(w), barHeight);
                                graphics.FillRectangle(brush, rect);
                            }
                            x += w;
                            drawBar = !drawBar;
                        }
                        // Inter-character narrow space
                        x += 1 * pixelsPerUnit;
                    }
                }

                // Draw human-readable label
                if (includeLabel)
                {
                    try
                    {
                        using (var font = new System.Drawing.Font("Arial", (float)(labelHeight * 0.7), System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        using (var brush = new SolidBrush(System.Drawing.Color.Black))
                        {
                            var rect = new Rectangle(destRect.X, destRect.Bottom - labelHeight, destRect.Width, labelHeight);
                            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            graphics.DrawString(raw, font, brush, rect, sf);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"DrawBarcode: Failed to draw label: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"DrawBarcode: Failed to draw barcode: {ex.Message}");
            }
        }

        private Dictionary<char, string> GetCode39Patterns()
        {
            return new Dictionary<char, string>
            {
                {'0', "nnnwwnwnw"}, {'1', "wnnwnnnnw"}, {'2', "nnwwnnnnw"}, {'3', "wnwwnnnnn"},
                {'4', "nnnwwnnnw"}, {'5', "wnnwwnnnn"}, {'6', "nnwwwnnnn"}, {'7', "nnnwnnwnw"},
                {'8', "wnnwnnwnn"}, {'9', "nnwwnnwnn"}, {'A', "wnnnnwnnw"}, {'B', "nnwnnwnnw"},
                {'C', "wnwnnwnnn"}, {'D', "nnnnwwnnw"}, {'E', "wnnnwwnnn"}, {'F', "nnwnwwnnn"},
                {'G', "nnnnnwwnw"}, {'H', "wnnnnwwnn"}, {'I', "nnwnnwwnn"}, {'J', "nnnnwwwnn"},
                {'K', "wnnnnnnww"}, {'L', "nnwnnnnww"}, {'M', "wnwnnnnwn"}, {'N', "nnnnwnnww"},
                {'O', "wnnnwnnwn"}, {'P', "nnwnwnnwn"}, {'Q', "nnnnnnwww"}, {'R', "wnnnnnwwn"},
                {'S', "nnwnnnwwn"}, {'T', "nnnnwnwwn"}, {'U', "wwnnnnnnw"}, {'V', "nwwnnnnnw"},
                {'W', "wwwnnnnnn"}, {'X', "nwnnwnnnw"}, {'Y', "wwnnwnnnn"}, {'Z', "nwwnwnnnn"},
                {'-', "nwnnnnwnw"}, {'.', "wwnnnnwnn"}, {' ', "nwwnnnwnn"}, {'*', "nwnnwnwnn"},
                {'$', "nwnwnwnnn"}, {'/', "nwnwnnnwn"}, {'+', "nwnnnwnwn"}, {'%', "nnnwnwnwn"}
            };
        }

        /// <summary>
        /// Draw QR code item from template CustomProperties
        /// </summary>
        private void DrawQrCode(Graphics graphics, CanvasItemData item, Rectangle destRect, EventData currentEvent)
        {
            try
            {
                string value = null;
                string ecc = "Q";
                int pixelsPerModule = 4;

                if (!string.IsNullOrWhiteSpace(item.CustomProperties))
                {
                    try
                    {
                        dynamic props = JsonConvert.DeserializeObject(item.CustomProperties);
                        value = props?.Value != null ? (string)props.Value : null;
                        ecc = props?.ECC != null ? (string)props.ECC : ecc;
                        if (props?.PixelsPerModule != null)
                        {
                            // Handle ints or strings
                            int.TryParse(props.PixelsPerModule.ToString(), out pixelsPerModule);
                            if (pixelsPerModule <= 0) pixelsPerModule = 4;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"DrawQrCode: Failed to parse CustomProperties: {ex.Message}");
                    }
                }

                // Resolve tokens in value using current event
                value = ResolveTokensForComposition(value, currentEvent);
                if (string.IsNullOrWhiteSpace(value))
                {
                    Log.Debug("DrawQrCode: Empty value after token resolution; skipping QR draw");
                    return;
                }

                // Map ECC
                QRCodeGenerator.ECCLevel eccLevel = QRCodeGenerator.ECCLevel.Q;
                try
                {
                    if (!string.IsNullOrEmpty(ecc) && Enum.TryParse(ecc, out QRCodeGenerator.ECCLevel parsed))
                    {
                        eccLevel = parsed;
                    }
                }
                catch { }

                using (var generator = new QRCodeGenerator())
                using (var data = generator.CreateQrCode(value, eccLevel))
                using (var qr = new QRCode(data))
                using (var bmp = qr.GetGraphic(Math.Max(1, pixelsPerModule), System.Drawing.Color.Black, System.Drawing.Color.White, true))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    graphics.DrawImage(bmp, destRect);
                    Log.Debug("DrawQrCode: QR code drawn successfully");
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"DrawQrCode: Failed to draw QR code: {ex.Message}");
            }
        }

        private string ResolveTokensForComposition(string input, EventData ev)
        {
            if (string.IsNullOrEmpty(input)) return input;
            try
            {
                string s = input;
                // Date/Time tokens
                s = System.Text.RegularExpressions.Regex.Replace(s, "\\{DATE(?::([^}]+))?\\}", m =>
                {
                    var fmt = m.Groups[1].Success ? m.Groups[1].Value : "yyyy-MM-dd";
                    return DateTime.Now.ToString(fmt);
                });
                s = System.Text.RegularExpressions.Regex.Replace(s, "\\{TIME(?::([^}]+))?\\}", m =>
                {
                    var fmt = m.Groups[1].Success ? m.Groups[1].Value : "HH:mm";
                    return DateTime.Now.ToString(fmt);
                });

                // Event tokens
                if (ev != null)
                {
                    s = s.Replace("{EVENT.NAME}", ev.Name ?? string.Empty);
                    s = System.Text.RegularExpressions.Regex.Replace(s, "\\{EVENT.DATE(?::([^}]+))?\\}", m =>
                    {
                        var fmt = m.Groups[1].Success ? m.Groups[1].Value : "yyyy-MM-dd";
                        if (ev.EventDate.HasValue) return ev.EventDate.Value.ToString(fmt);
                        return string.Empty;
                    });
                    string galleryUrl = !string.IsNullOrWhiteSpace(ev.GalleryUrl)
                        ? ev.GalleryUrl
                        : ComputeEventUrlFallback(ev.Name);
                    s = s.Replace("{EVENT.URL}", galleryUrl);
                }
                else
                {
                    s = s.Replace("{EVENT.NAME}", string.Empty).Replace("{EVENT.URL}", string.Empty);
                    s = System.Text.RegularExpressions.Regex.Replace(s, "\\{EVENT.DATE(?::([^}]+))?\\}", m => string.Empty);
                }

                // Session tokens
                var sessionGuid = GetCurrentSessionGuidSafe(ev);
                if (!string.IsNullOrEmpty(sessionGuid))
                {
                    var sessionUrl = GetOrComputeSessionUrl(sessionGuid, ev?.Name);
                    if (!string.IsNullOrEmpty(sessionUrl))
                    {
                        s = s.Replace("{SESSION.URL}", sessionUrl);
                    }
                    else
                    {
                        s = s.Replace("{SESSION.URL}", string.Empty);
                    }
                }
                else
                {
                    // Fallback: use event URL so QR is not blank if session GUID isn't ready yet
                    var fallback = ComputeEventUrlFallback(ev?.Name);
                    s = s.Replace("{SESSION.URL}", fallback ?? string.Empty);
                }

                return s;
            }
            catch
            {
                return input;
            }
        }

        private string GetCurrentSessionGuidSafe(EventData ev)
        {
            try
            {
                // Try to call parent method if available
                var guid = _parent?.GetCurrentSessionGuid();
                if (guid is string s && !string.IsNullOrWhiteSpace(s)) return s;

                // Attempt to create a database session via parent (even if offline)
                try
                {
                    var method = _parent?.GetType().GetMethod("CreateDatabaseSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (method != null)
                    {
                        method.Invoke(_parent, null);
                        // Try again after creating session
                        guid = _parent?.GetCurrentSessionGuid();
                        if (guid is string s2 && !string.IsNullOrWhiteSpace(s2)) return s2;
                    }
                    else
                    {
                        // Fallback: try to call DatabaseOperations.CreateSession(eventId, templateId) directly
                        var dbField = _parent?.GetType().GetField("databaseOperations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        var dbOps = dbField?.GetValue(_parent);
                        if (dbOps != null)
                        {
                            // Discover current template ID via reflection (if available)
                            int? eventId = ev?.Id;
                            int? templateId = null;
                            try
                            {
                                var tplField = _parent.GetType().GetField("currentTemplate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                var tpl = tplField?.GetValue(_parent);
                                if (tpl != null)
                                {
                                    var idProp = tpl.GetType().GetProperty("Id");
                                    if (idProp != null)
                                    {
                                        templateId = idProp.GetValue(tpl) as int?;
                                        if (templateId == null)
                                        {
                                            object idObj = idProp.GetValue(tpl);
                                            if (idObj != null && int.TryParse(idObj.ToString(), out int idParsed)) templateId = idParsed;
                                        }
                                    }
                                }
                            }
                            catch { }

                            var createSessionMethod = dbOps.GetType().GetMethod("CreateSession", new Type[] { typeof(int?), typeof(int?) });
                            if (createSessionMethod != null)
                            {
                                createSessionMethod.Invoke(dbOps, new object[] { (object)eventId, (object)templateId });
                                guid = _parent?.GetCurrentSessionGuid();
                                if (guid is string s3 && !string.IsNullOrWhiteSpace(s3)) return s3;
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
            return null;
        }

        private string GetOrComputeSessionUrl(string sessionGuid, string eventName)
        {
            try
            {
                // 1) Try DB
                var db = new Photobooth.Database.TemplateDatabase();
                var url = db.GetPhotoSessionGalleryUrl(sessionGuid);
                if (!string.IsNullOrWhiteSpace(url)) return url;

                // 2) Compute predictable URL
                return ComputeSessionUrlFallback(eventName, sessionGuid);
            }
            catch
            {
                return null;
            }
        }

        private string ComputeEventUrlFallback(string eventName)
        {
            string baseUrl = Environment.GetEnvironmentVariable("GALLERY_BASE_URL", EnvironmentVariableTarget.User)
                               ?? "https://phototeknyc.s3.amazonaws.com";
            string evt = SanitizeForS3Key(eventName);
            return $"{baseUrl}/events/{evt}/";
        }

        private string ComputeSessionUrlFallback(string eventName, string sessionGuid)
        {
            if (string.IsNullOrEmpty(sessionGuid)) return null;
            string baseUrl = Environment.GetEnvironmentVariable("GALLERY_BASE_URL", EnvironmentVariableTarget.User)
                               ?? "https://phototeknyc.s3.amazonaws.com";
            string evt = SanitizeForS3Key(eventName);
            return $"{baseUrl}/events/{evt}/sessions/{sessionGuid}/index.html";
        }

        private string SanitizeForS3Key(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "general";
            var sanitized = input.Trim()
                .Replace(" ", "-")
                .Replace("/", "-")
                .Replace("\\", "-")
                .Replace(":", "-")
                .Replace("*", "-")
                .Replace("?", "-")
                .Replace("\"", "-")
                .Replace("<", "-")
                .Replace(">", "-")
                .Replace("|", "-");
            while (sanitized.Contains("--")) sanitized = sanitized.Replace("--", "-");
            sanitized = sanitized.Trim('-');
            if (string.IsNullOrEmpty(sanitized)) return "general";
            if (sanitized.Length > 50) sanitized = sanitized.Substring(0, 50).TrimEnd('-');
            return sanitized.ToLowerInvariant();
        }

        // Render text using WPF FormattedText to match designer exactly, then draw into GDI bitmap
        private bool TryDrawTextWithWpf(Graphics g, CanvasItemData item, Rectangle destRect)
        {
            try
            {
                // Get the actual system DPI to ensure accurate rendering
                // Use the Graphics DPI for consistency with the target bitmap
                double dpiX = g.DpiX;
                double dpiY = g.DpiY;

                // Apply same 5px margin used by SimpleTextItem's TextBlock
                int pad = 5;
                int w = Math.Max(0, destRect.Width - 2 * pad);
                int h = Math.Max(0, destRect.Height - 2 * pad);
                if (w <= 0 || h <= 0) return false;

                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Clip to our drawing area
                    dc.PushClip(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(0, 0, w, h)));

                    // Build typeface
                    var wpfFamily = new System.Windows.Media.FontFamily(item.FontFamily ?? "Arial");
                    var wpfStyle = string.Equals(item.FontStyle, "Italic", StringComparison.OrdinalIgnoreCase)
                        ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
                    var wpfWeight = string.Equals(item.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase)
                        ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
                    var typeface = new System.Windows.Media.Typeface(wpfFamily, wpfStyle, wpfWeight, System.Windows.FontStretches.Normal);

                    // Font size in device-independent pixels (same as SimpleTextItem uses)
                    double fontSize = item.FontSize ?? 20.0;

                    // Calculate pixelsPerDip for accurate font rendering
                    // This ensures the font size matches exactly what WPF TextBlock renders
                    double pixelsPerDip = dpiX / 96.0;

                    Log.Debug($"TryDrawTextWithWpf: FontSize={fontSize}, DPI={dpiX}x{dpiY}, PixelsPerDip={pixelsPerDip}");

                    // Foreground brush
                    var color = System.Windows.Media.ColorConverter.ConvertFromString(item.TextColor ?? "#FF000000");
                    var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)color);

                    // FormattedText with proper DPI handling
                    var ft = new System.Windows.Media.FormattedText(
                        item.Text,
                        System.Globalization.CultureInfo.CurrentUICulture,
                        System.Windows.FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        brush,
                        pixelsPerDip);

                    ft.MaxTextWidth = w;
                    ft.MaxTextHeight = h;

                    // Compute our own alignment offsets to reuse for outline geometry
                    ft.TextAlignment = System.Windows.TextAlignment.Left; // we handle x offset
                    double contentWidth = Math.Min(ft.WidthIncludingTrailingWhitespace, w);
                    double x = 0;
                    if (string.Equals(item.TextAlignment, "Center", StringComparison.OrdinalIgnoreCase))
                        x = (w - contentWidth) / 2.0;
                    else if (string.Equals(item.TextAlignment, "Right", StringComparison.OrdinalIgnoreCase))
                        x = w - contentWidth;

                    // Vertical alignment - center the text like SimpleTextItem does
                    double y = Math.Max(0, (h - ft.Height) / 2.0);

                    // Optional outline
                    if (item.HasOutline && item.OutlineThickness > 0 && !string.IsNullOrEmpty(item.OutlineColor))
                    {
                        var oc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.OutlineColor);
                        var pen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(oc), item.OutlineThickness);
                        pen.LineJoin = System.Windows.Media.PenLineJoin.Round;
                        var geo = ft.BuildGeometry(new System.Windows.Point(x, y));
                        dc.DrawGeometry(null, pen, geo);
                    }

                    // Fill text on top
                    dc.DrawText(ft, new System.Windows.Point(x, y));
                    dc.Pop();
                }

                // Render at the actual DPI to match the target Graphics context
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, dpiX, dpiY, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(dv);

                // Convert to System.Drawing.Bitmap
                using (var ms = new System.IO.MemoryStream())
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    encoder.Save(ms);
                    ms.Position = 0;
                    using (var bmp = new Bitmap(ms))
                    {
                        // Draw shadow if requested (use text bitmap alpha as mask)
                        if (item.HasShadow && !string.IsNullOrEmpty(item.ShadowColor))
                        {
                            var shadowColor = ParseColor(item.ShadowColor);
                            int blur = (int)Math.Max(0, Math.Round(item.ShadowBlurRadius));
                            int offX = (int)Math.Round(item.ShadowOffsetX);
                            int offY = (int)Math.Round(item.ShadowOffsetY);
                            using (var shadowBmp = CreateShadowFromAlpha(bmp, shadowColor, blur))
                            {
                                g.DrawImage(shadowBmp, new System.Drawing.Rectangle(destRect.X + pad + offX, destRect.Y + pad + offY, w, h));
                            }
                        }

                        // Then the actual text
                        g.DrawImage(bmp, new System.Drawing.Rectangle(destRect.X + pad, destRect.Y + pad, w, h));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug($"TryDrawTextWithWpf fallback to GDI+: {ex.Message}");
                return false;
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
                
                // Shadow (rendered from shape silhouette)
                if (item.HasShadow && !string.IsNullOrEmpty(item.ShadowColor))
                {
                    var shadowColor = ParseColor(item.ShadowColor);
                    int blur = (int)Math.Max(0, Math.Round(item.ShadowBlurRadius));
                    int offX = (int)Math.Round(item.ShadowOffsetX);
                    int offY = (int)Math.Round(item.ShadowOffsetY);

                    using (var mask = new Bitmap(destRect.Width, destRect.Height, PixelFormat.Format32bppArgb))
                    using (var mg = Graphics.FromImage(mask))
                    {
                        mg.SmoothingMode = SmoothingMode.AntiAlias;
                        if (item.ShapeType == "Ellipse")
                        {
                            mg.FillEllipse(System.Drawing.Brushes.White, new Rectangle(0, 0, destRect.Width, destRect.Height));
                        }
                        else // Rectangle or other -> rectangle mask
                        {
                            mg.FillRectangle(System.Drawing.Brushes.White, new Rectangle(0, 0, destRect.Width, destRect.Height));
                        }

                        using (var shadowBmp = CreateShadowFromAlpha(mask, shadowColor, blur))
                        {
                            graphics.DrawImage(shadowBmp, new Rectangle(destRect.X + offX, destRect.Y + offY, destRect.Width, destRect.Height));
                        }
                    }
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

        // --- Shadow and blur helpers ---
        private Bitmap CreateRectShadowBitmap(int width, int height, System.Drawing.Color color, int blurRadius)
        {
            using (var baseBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(baseBmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(255, color)))
                {
                    g.FillRectangle(brush, new Rectangle(0, 0, width, height));
                }
                return CreateShadowFromAlpha(baseBmp, color, blurRadius);
            }
        }

        private Bitmap CreateShadowFromAlpha(Bitmap alphaSource, System.Drawing.Color color, int blurRadius)
        {
            // Colorize by replacing RGB and keeping source alpha
            var colored = new Bitmap(alphaSource.Width, alphaSource.Height, PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, alphaSource.Width, alphaSource.Height);
            var srcData = alphaSource.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dstData = colored.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int stride = srcData.Stride;
            int bytes = Math.Abs(stride) * alphaSource.Height;
            byte[] src = new byte[bytes];
            byte[] dst = new byte[bytes];
            Marshal.Copy(srcData.Scan0, src, 0, bytes);

            for (int y = 0; y < alphaSource.Height; y++)
            {
                for (int x = 0; x < alphaSource.Width; x++)
                {
                    int i = y * stride + x * 4;
                    byte a = src[i + 3];
                    dst[i + 0] = color.B;
                    dst[i + 1] = color.G;
                    dst[i + 2] = color.R;
                    dst[i + 3] = a; // preserve alpha as mask
                }
            }

            Marshal.Copy(dst, 0, dstData.Scan0, bytes);
            alphaSource.UnlockBits(srcData);
            colored.UnlockBits(dstData);

            // Blur for softness
            if (blurRadius > 0)
            {
                var blurred = ApplyBoxBlur(colored, blurRadius);
                colored.Dispose();
                return blurred;
            }

            return colored;
        }

        private Bitmap ApplyBoxBlur(Bitmap source, int radius)
        {
            if (radius < 1) return new Bitmap(source);
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, source.Width, source.Height);
            var srcData = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var resData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int stride = srcData.Stride;
            int bytes = Math.Abs(stride) * source.Height;
            byte[] src = new byte[bytes];
            byte[] dst = new byte[bytes];
            Marshal.Copy(srcData.Scan0, src, 0, bytes);

            int w = source.Width;
            int h = source.Height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0, count = 0;
                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        int py = Math.Min(h - 1, Math.Max(0, y + ky));
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int px = Math.Min(w - 1, Math.Max(0, x + kx));
                            int idx = py * stride + px * 4;
                            b += src[idx + 0];
                            g += src[idx + 1];
                            r += src[idx + 2];
                            a += src[idx + 3];
                            count++;
                        }
                    }
                    int di = y * stride + x * 4;
                    dst[di + 0] = (byte)(b / count);
                    dst[di + 1] = (byte)(g / count);
                    dst[di + 2] = (byte)(r / count);
                    dst[di + 3] = (byte)(a / count);
                }
            }

            Marshal.Copy(dst, 0, resData.Scan0, bytes);
            source.UnlockBits(srcData);
            result.UnlockBits(resData);
            return result;
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
                if (colorString.StartsWith("#") && colorString.Length == 9)
                {
                    // WPF format with alpha channel (#AARRGGBB)
                    var alpha = Convert.ToByte(colorString.Substring(1, 2), 16);
                    var red = Convert.ToByte(colorString.Substring(3, 2), 16);
                    var green = Convert.ToByte(colorString.Substring(5, 2), 16);
                    var blue = Convert.ToByte(colorString.Substring(7, 2), 16);
                    return System.Drawing.Color.FromArgb(alpha, red, green, blue);
                }
                else if (colorString.StartsWith("#"))
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
            // Use proper event-based folder structure: EventName/composed/
            string outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth", eventFolderName, "composed");
            
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
            bool duplicate2x6To4x6 = Properties.Settings.Default.Duplicate2x6To4x6; // Read from settings
            
            Log.Debug($"★★★ HandleTemplateForPrinting START ★★★");
            Log.Debug($"  - Template dimensions: {templateWidth}x{templateHeight}");
            Log.Debug($"  - is2x6Template: {is2x6Template}");
            Log.Debug($"  - duplicate2x6To4x6 setting: {duplicate2x6To4x6}");
            Log.Debug($"  - Original outputPath: {outputPath}");
            
            if (is2x6Template && duplicate2x6To4x6)
            {
                Log.Debug($"  - ✅ WILL CREATE 4x6 DUPLICATE");
                
                using (var duplicatedBitmap = Create4x6From2x6(finalBitmap))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    // Use event-based print folder structure: EventName/print/
                    string eventFolderName = SanitizeFileName(currentEvent.Name);
                    string printDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        "Photobooth", eventFolderName, "print");
                    Directory.CreateDirectory(printDir);
                    string printPath = Path.Combine(printDir, $"{currentEvent.Name}_{timestamp}_4x6_print.jpg");
                    
                    SaveComposedImage(duplicatedBitmap, printPath);
                    Log.Debug($"  - Created 4x6 at: {printPath}");
                    
                    // Verify the created file
                    if (File.Exists(printPath))
                    {
                        using (var verifyImg = System.Drawing.Image.FromFile(printPath))
                        {
                            Log.Debug($"  - Verified 4x6 dimensions: {verifyImg.Width}x{verifyImg.Height}");
                        }
                    }
                    
                    Log.Debug($"★★★ HandleTemplateForPrinting END - Returning 4x6: {printPath} ★★★");
                    return printPath;
                }
            }
            
            Log.Debug($"  - ⚠️ NO DUPLICATION - Using original");
            Log.Debug($"★★★ HandleTemplateForPrinting END - Returning original: {outputPath} ★★★");
            return outputPath;
        }
        
        /// <summary>
        /// Check if template is 2x6
        /// </summary>
        private bool Is2x6Template(int width, int height)
        {
            Log.Debug($"Is2x6Template: Checking dimensions {width}x{height}");
            int targetDpi = 300; // Default print DPI
            
            // 2x6 at common DPIs
            int[] validDpis = { 150, 300, 600 };
            foreach (int dpi in validDpis)
            {
                int expected2inchPixels = 2 * dpi;
                int expected6inchPixels = 6 * dpi;
                
                Log.Debug($"Is2x6Template: Checking against {dpi} DPI - Expected: {expected2inchPixels}x{expected6inchPixels}");
                
                // Allow 5% tolerance
                if (Math.Abs(width - expected2inchPixels) < expected2inchPixels * 0.05 &&
                    Math.Abs(height - expected6inchPixels) < expected6inchPixels * 0.05)
                {
                    Log.Debug($"Is2x6Template: MATCH FOUND! This is a 2x6 template at {dpi} DPI");
                    return true;
                }
            }
            
            Log.Debug($"Is2x6Template: NO MATCH - {width}x{height} is not a 2x6 template");
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
