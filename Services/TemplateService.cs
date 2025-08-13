using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesignerCanvas;
using Photobooth.Database;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace Photobooth.Services
{
    public class TemplateService
    {
        private readonly TemplateDatabase database;
        
        public TemplateService()
        {
            database = new TemplateDatabase();
        }
        
        public int SaveCurrentCanvas(string templateName, string description, IEnumerable<ICanvasItem> canvasItems, 
            double canvasWidth, double canvasHeight, Brush backgroundBrush = null)
        {
            try
            {
                // Create template data - save canvas in its native paper dimensions (e.g., 600x1800 for 2x6)
                var template = new TemplateData
                {
                    Name = templateName,
                    Description = description,
                    CanvasWidth = canvasWidth,
                    CanvasHeight = canvasHeight,
                    BackgroundColor = BrushToColorString(backgroundBrush),
                    ThumbnailImagePath = GenerateCanvasThumbnailPath(canvasItems, canvasWidth, canvasHeight, backgroundBrush, templateName)
                };
                
                // Save template and get ID
                int templateId = database.SaveTemplate(template);
                
                // Save each canvas item in the same coordinate system as they exist on canvas
                // Canvas items should already be in native paper dimensions
                int zIndex = 0;
                foreach (var item in canvasItems)
                {
                    var canvasItemData = ConvertToCanvasItemData(item, templateId, zIndex++);
                    database.SaveCanvasItem(canvasItemData);
                }
                
                return templateId;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        
        public List<TemplateData> GetAllTemplates()
        {
            return database.GetAllTemplates();
        }
        
        public bool LoadTemplate(int templateId, Action<TemplateData, List<ICanvasItem>> onLoaded)
        {
            try
            {
                var template = database.GetTemplate(templateId);
                if (template == null)
                {
                    MessageBox.Show("Template not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                var canvasItemsData = database.GetCanvasItems(templateId);
                var canvasItems = new List<ICanvasItem>();
                
                // Load items in their native dimensions - no scaling
                // The canvas view will handle display scaling
                foreach (var itemData in canvasItemsData)
                {
                    var canvasItem = ConvertToCanvasItem(itemData);
                    if (canvasItem != null)
                    {
                        canvasItems.Add(canvasItem);
                    }
                }
                
                onLoaded(template, canvasItems);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        
        public void UpdateTemplate(int templateId, string name, string description)
        {
            try
            {
                var template = database.GetTemplate(templateId);
                if (template != null)
                {
                    template.Name = name;
                    template.Description = description;
                    database.UpdateTemplate(templateId, template);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public bool UpdateTemplateCanvas(int templateId, IEnumerable<ICanvasItem> canvasItems, 
            double canvasWidth, double canvasHeight, Brush backgroundBrush = null)
        {
            try
            {
                // Get existing template
                var template = database.GetTemplate(templateId);
                if (template == null)
                {
                    MessageBox.Show("Template not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                // Update template properties
                template.CanvasWidth = canvasWidth;
                template.CanvasHeight = canvasHeight;
                template.BackgroundColor = BrushToColorString(backgroundBrush);
                template.ThumbnailImagePath = GenerateCanvasThumbnailPath(canvasItems, canvasWidth, canvasHeight, backgroundBrush, template.Name);
                template.ModifiedDate = DateTime.Now;
                
                // Update template in database
                database.UpdateTemplate(templateId, template);
                
                // Delete old canvas items
                database.DeleteCanvasItems(templateId);
                
                // Save new canvas items
                int zIndex = 0;
                foreach (var item in canvasItems)
                {
                    var canvasItemData = ConvertToCanvasItemData(item, templateId, zIndex++);
                    database.SaveCanvasItem(canvasItemData);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update template canvas: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        
        public void DeleteTemplate(int templateId)
        {
            try
            {
                database.DeleteTemplate(templateId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public int DuplicateTemplate(int templateId, string newName = null)
        {
            try
            {
                return database.DuplicateTemplate(templateId, newName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to duplicate template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        
        private CanvasItemData ConvertToCanvasItemData(ICanvasItem item, int templateId, int zIndex)
        {
            var data = new CanvasItemData
            {
                TemplateId = templateId,
                ZIndex = zIndex,
                IsVisible = true
            };
            
            // Common properties for all items that implement IBoxCanvasItem
            // Save in the coordinate system as they exist - no conversion
            if (item is IBoxCanvasItem boxItem)
            {
                data.X = boxItem.Left;
                data.Y = boxItem.Top;
                data.Width = boxItem.Width;
                data.Height = boxItem.Height;
                data.LockedPosition = boxItem.LockedPosition;
            }
            
            // Common properties - handle rotation and aspect ratio based on item type
            if (item is CanvasItem canvasItem)
            {
                data.Rotation = canvasItem.Angle;
                data.LockedAspectRatio = canvasItem.LockedAspectRatio;
            }
            else if (item is TextCanvasItem textCanvasItem)
            {
                data.Rotation = textCanvasItem.Angle;
                data.LockedAspectRatio = textCanvasItem.LockedAspectRatio;
            }
            
            // Specific item type handling
            switch (item)
            {
                case TextCanvasItem textItem:
                    data.ItemType = "Text";
                    data.Name = string.IsNullOrEmpty(textItem.Text) ? "Text Item" : $"Text: {textItem.Text.Substring(0, Math.Min(textItem.Text.Length, 20))}";
                    data.Text = textItem.Text;
                    data.FontFamily = textItem.FontFamily;
                    data.FontSize = textItem.FontSize;
                    data.TextColor = BrushToColorString(textItem.Foreground);
                    data.IsBold = textItem.IsBold;
                    data.IsItalic = textItem.IsItalic;
                    data.IsUnderlined = textItem.IsUnderlined;
                    data.HasShadow = textItem.HasShadow;
                    data.ShadowOffsetX = textItem.ShadowOffsetX;
                    data.ShadowOffsetY = textItem.ShadowOffsetY;
                    data.ShadowBlurRadius = textItem.ShadowBlurRadius;
                    data.ShadowColor = ColorToColorString(textItem.ShadowColor);
                    data.HasOutline = textItem.HasOutline;
                    data.OutlineThickness = textItem.OutlineThickness;
                    data.OutlineColor = BrushToColorString(textItem.OutlineColor);
                    data.TextAlignment = textItem.TextAlignment.ToString();
                    break;
                    
                case PlaceholderCanvasItem placeholderItem:
                    data.ItemType = "Placeholder";
                    data.Name = $"Placeholder {placeholderItem.PlaceholderNo}";
                    data.PlaceholderNumber = placeholderItem.PlaceholderNo;
                    data.PlaceholderColor = BrushToColorString(placeholderItem.Background);
                    break;
                    
                case ImageCanvasItem imageItem:
                    data.ItemType = "Image";
                    data.Name = "Image Item";
                    if (imageItem.Image is BitmapImage bitmapImage && bitmapImage.UriSource != null)
                    {
                        // Save the local path instead of URI string for better compatibility
                        string imagePath = bitmapImage.UriSource.ToString();
                        if (bitmapImage.UriSource.IsFile)
                        {
                            imagePath = bitmapImage.UriSource.LocalPath;
                        }
                        data.ImagePath = imagePath;
                        data.ImageHash = GenerateImageHash(imagePath);
                    }
                    break;
                    
                case ShapeCanvasItem shapeItem:
                    data.ItemType = "Shape";
                    data.Name = $"Shape: {shapeItem.ShapeType}";
                    data.ShapeType = shapeItem.ShapeType.ToString();
                    data.FillColor = BrushToColorString(shapeItem.Fill);
                    data.StrokeColor = BrushToColorString(shapeItem.Stroke);
                    data.StrokeThickness = shapeItem.StrokeThickness;
                    data.HasNoFill = shapeItem.HasNoFill;
                    data.HasNoStroke = shapeItem.HasNoStroke;
                    break;
                    
                default:
                    data.ItemType = "Unknown";
                    data.Name = item.GetType().Name;
                    break;
            }
            
            return data;
        }
        
        private ICanvasItem ConvertToCanvasItem(CanvasItemData data)
        {
            ICanvasItem item = null;
            
            switch (data.ItemType)
            {
                case "Text":
                    var textItem = new TextCanvasItem();
                    // Suppress auto-sizing while loading from database
                    textItem.SuppressAutoSize = true;
                    
                    // Set text properties
                    textItem.Text = data.Text ?? "";
                    if (!string.IsNullOrEmpty(data.FontFamily))
                        textItem.FontFamily = data.FontFamily;
                    if (data.FontSize.HasValue)
                        textItem.FontSize = data.FontSize.Value;
                    textItem.Foreground = ColorStringToBrush(data.TextColor);
                    textItem.IsBold = data.IsBold;
                    textItem.IsItalic = data.IsItalic;
                    textItem.IsUnderlined = data.IsUnderlined;
                    textItem.HasShadow = data.HasShadow;
                    textItem.ShadowOffsetX = data.ShadowOffsetX;
                    textItem.ShadowOffsetY = data.ShadowOffsetY;
                    textItem.ShadowBlurRadius = data.ShadowBlurRadius;
                    textItem.ShadowColor = ColorStringToColor(data.ShadowColor);
                    textItem.HasOutline = data.HasOutline;
                    textItem.OutlineThickness = data.OutlineThickness;
                    textItem.OutlineColor = ColorStringToBrush(data.OutlineColor);
                    if (!string.IsNullOrEmpty(data.TextAlignment) && 
                        Enum.TryParse<TextAlignment>(data.TextAlignment, out var alignment))
                    {
                        textItem.TextAlignment = alignment;
                    }
                    item = textItem;
                    break;
                    
                case "Placeholder":
                    var placeholderItem = new PlaceholderCanvasItem();
                    if (data.PlaceholderNumber.HasValue)
                        placeholderItem.PlaceholderNo = data.PlaceholderNumber.Value;
                    placeholderItem.Background = ColorStringToBrush(data.PlaceholderColor);
                    item = placeholderItem;
                    break;
                    
                case "Image":
                    var imageItem = new ImageCanvasItem();
                    if (!string.IsNullOrEmpty(data.ImagePath))
                    {
                        try
                        {
                            string imagePath = data.ImagePath;
                            
                            // Handle URI format (file:///) 
                            if (imagePath.StartsWith("file:///"))
                            {
                                imagePath = new Uri(imagePath).LocalPath;
                            }
                            
                            // Check if file exists at the path
                            if (File.Exists(imagePath))
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                                bitmap.EndInit();
                                bitmap.Freeze(); // Improve performance
                                imageItem.Image = bitmap;
                            }
                            else
                            {
                                // Try as a URI directly (in case it's a valid URI string)
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.UriSource = new Uri(data.ImagePath);
                                bitmap.EndInit();
                                bitmap.Freeze();
                                imageItem.Image = bitmap;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log the error for debugging
                            System.Diagnostics.Debug.WriteLine($"Failed to load image from path: {data.ImagePath}. Error: {ex.Message}");
                        }
                    }
                    item = imageItem;
                    break;
                    
                case "Shape":
                    // Parse shape type first
                    ShapeType shapeType = ShapeType.Rectangle; // Default
                    if (!string.IsNullOrEmpty(data.ShapeType))
                    {
                        Enum.TryParse<ShapeType>(data.ShapeType, out shapeType);
                    }
                    
                    // Create shape with initial position (will be set later from common properties)
                    var shapeItem = new ShapeCanvasItem(0, 0, shapeType);
                    
                    // Set fill and stroke
                    if (!data.HasNoFill)
                    {
                        shapeItem.Fill = ColorStringToBrush(data.FillColor);
                    }
                    shapeItem.HasNoFill = data.HasNoFill;
                    
                    if (!data.HasNoStroke)
                    {
                        shapeItem.Stroke = ColorStringToBrush(data.StrokeColor);
                    }
                    shapeItem.HasNoStroke = data.HasNoStroke;
                    
                    shapeItem.StrokeThickness = data.StrokeThickness;
                    item = shapeItem;
                    break;
            }
            
            // Set common properties - load in the same coordinate system as saved
            if (item is IBoxCanvasItem boxItem)
            {
                boxItem.Left = data.X;
                boxItem.Top = data.Y;
                boxItem.Width = data.Width;
                boxItem.Height = data.Height;
                boxItem.LockedPosition = data.LockedPosition;
            }
            
            // Set rotation and aspect ratio based on item type
            if (item is CanvasItem canvasItem)
            {
                canvasItem.Angle = data.Rotation;
                canvasItem.LockedAspectRatio = data.LockedAspectRatio;
            }
            else if (item is TextCanvasItem textCanvasItem)
            {
                textCanvasItem.Angle = data.Rotation;
                textCanvasItem.LockedAspectRatio = data.LockedAspectRatio;
                // Re-enable auto-sizing after loading from database
                textCanvasItem.SuppressAutoSize = false;
            }
            
            return item;
        }
        
        private string BrushToColorString(Brush brush)
        {
            if (brush is SolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            return null;
        }
        
        private string ColorToColorString(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        private Brush ColorStringToBrush(string colorString)
        {
            if (string.IsNullOrEmpty(colorString))
                return new SolidColorBrush(Colors.Black);
                
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorString);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.Black);
            }
        }
        
        private Color ColorStringToColor(string colorString)
        {
            if (string.IsNullOrEmpty(colorString))
                return Colors.Black;
                
            try
            {
                return (Color)ColorConverter.ConvertFromString(colorString);
            }
            catch
            {
                return Colors.Black;
            }
        }
        
        private string GenerateImageHash(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return null;
                
            try
            {
                using (var stream = File.OpenRead(imagePath))
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(stream);
                    return Convert.ToBase64String(hash);
                }
            }
            catch
            {
                return null;
            }
        }
        
        private string GenerateCanvasThumbnailPath(IEnumerable<ICanvasItem> items, double canvasWidth, double canvasHeight, Brush background, string templateName)
        {
            try
            {
                // Match the aspect ratio of 2x6 template
                const int thumbnailWidth = 280;
                const int thumbnailHeight = 840; // 280 * 3 for 2:6 ratio
                
                // Check if canvas dimensions look like display dimensions (< 500) or pixel dimensions (> 500)
                bool isPixelDimensions = canvasWidth > 500 || canvasHeight > 500;
                double actualCanvasWidth = canvasWidth;
                double actualCanvasHeight = canvasHeight;
                
                // If these are pixel dimensions, convert to display dimensions for proper scaling
                if (isPixelDimensions)
                {
                    // For a 2x6 template: 600x1800 pixels at 300 DPI = 2x6 inches
                    // Display at roughly 72-96 DPI for screen = divide by ~3-4
                    actualCanvasWidth = canvasWidth / 2.3;
                    actualCanvasHeight = canvasHeight / 2.3;
                }
                
                var renderTarget = new RenderTargetBitmap(thumbnailWidth, thumbnailHeight, 96, 96, PixelFormats.Pbgra32);
                
                var drawingVisual = new DrawingVisual();
                using (var context = drawingVisual.RenderOpen())
                {
                    // Draw white background first
                    context.DrawRectangle(new SolidColorBrush(Colors.White), 
                        null, new Rect(0, 0, thumbnailWidth, thumbnailHeight));
                    
                    // Calculate scale to fit template in thumbnail
                    double scaleX = thumbnailWidth / actualCanvasWidth;
                    double scaleY = thumbnailHeight / actualCanvasHeight;
                    double scale = Math.Min(scaleX, scaleY); // Fill the entire thumbnail area
                    
                    // Calculate centered position
                    double scaledWidth = actualCanvasWidth * scale;
                    double scaledHeight = actualCanvasHeight * scale;
                    double offsetX = (thumbnailWidth - scaledWidth) / 2;
                    double offsetY = (thumbnailHeight - scaledHeight) / 2;
                    
                    // Draw template background
                    context.DrawRectangle(background ?? new SolidColorBrush(Colors.LightGray), 
                        new Pen(new SolidColorBrush(Colors.Gray), 1), 
                        new Rect(offsetX, offsetY, scaledWidth, scaledHeight));
                    
                    // Draw actual content of items
                    foreach (var item in items)
                    {
                        if (item is IBoxCanvasItem boxItem)
                        {
                            // Scale item dimensions and apply offset for centering
                            double itemX = boxItem.Left;
                            double itemY = boxItem.Top;
                            double itemWidth = boxItem.Width;
                            double itemHeight = boxItem.Height;
                            
                            // If saved with pixel dimensions, convert to display dimensions
                            if (isPixelDimensions)
                            {
                                itemX = itemX / 2.3;
                                itemY = itemY / 2.3;
                                itemWidth = itemWidth / 2.3;
                                itemHeight = itemHeight / 2.3;
                            }
                            
                            var rect = new Rect(
                                offsetX + (itemX * scale),
                                offsetY + (itemY * scale),
                                itemWidth * scale,
                                itemHeight * scale
                            );
                            
                            // Apply rotation if the item supports it
                            double angle = 0;
                            if (item is CanvasItem canvasItem)
                                angle = canvasItem.Angle;
                            else if (item is TextCanvasItem textCanvasItem)
                                angle = textCanvasItem.Angle;
                            
                            context.PushTransform(new RotateTransform(angle, 
                                rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
                            
                            if (item is ImageCanvasItem imageItem && imageItem.Image != null)
                            {
                                // Draw actual image
                                context.DrawImage(imageItem.Image, rect);
                            }
                            else if (item is PlaceholderCanvasItem placeholder)
                            {
                                // Draw placeholder with actual background
                                context.DrawRectangle(placeholder.Background ?? new SolidColorBrush(Colors.LightPink), 
                                    new Pen(new SolidColorBrush(Colors.White), 0.5), rect);
                                
                                // Draw placeholder text
                                var formattedText = new FormattedText(
                                    $"Picture {placeholder.PlaceholderNo}",
                                    CultureInfo.InvariantCulture,
                                    FlowDirection.LeftToRight,
                                    new Typeface("Arial"),
                                    Math.Min(12, rect.Height / 4),
                                    new SolidColorBrush(Colors.DarkGray),
                                    1.0);
                                
                                context.DrawText(formattedText, 
                                    new Point(rect.Left + (rect.Width - formattedText.Width) / 2, 
                                             rect.Top + (rect.Height - formattedText.Height) / 2));
                            }
                            else if (item is TextCanvasItem textItem)
                            {
                                // Draw text with actual formatting
                                var typeface = new Typeface(
                                    new FontFamily(textItem.FontFamily ?? "Arial"),
                                    textItem.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                                    textItem.IsBold ? FontWeights.Bold : FontWeights.Normal,
                                    FontStretches.Normal);
                                
                                var formattedText = new FormattedText(
                                    textItem.Text ?? "",
                                    CultureInfo.InvariantCulture,
                                    FlowDirection.LeftToRight,
                                    typeface,
                                    textItem.FontSize * scale,
                                    textItem.Foreground ?? new SolidColorBrush(Colors.Black),
                                    1.0);
                                
                                formattedText.TextAlignment = textItem.TextAlignment;
                                formattedText.MaxTextWidth = rect.Width;
                                formattedText.MaxTextHeight = rect.Height;
                                
                                // Draw text shadow if enabled
                                if (textItem.HasShadow)
                                {
                                    var shadowBrush = new SolidColorBrush(textItem.ShadowColor);
                                    shadowBrush.Opacity = 0.5;
                                    
                                    // Create a separate FormattedText for shadow
                                    var shadowText = new FormattedText(
                                        textItem.Text ?? "",
                                        CultureInfo.InvariantCulture,
                                        FlowDirection.LeftToRight,
                                        typeface,
                                        textItem.FontSize * scale,
                                        shadowBrush,
                                        1.0);
                                    
                                    shadowText.TextAlignment = textItem.TextAlignment;
                                    shadowText.MaxTextWidth = rect.Width;
                                    shadowText.MaxTextHeight = rect.Height;
                                    
                                    context.DrawText(shadowText, 
                                        new Point(rect.Left + textItem.ShadowOffsetX * scale, 
                                                 rect.Top + textItem.ShadowOffsetY * scale));
                                }
                                
                                context.DrawText(formattedText, new Point(rect.Left, rect.Top));
                            }
                            else
                            {
                                // Default rectangle for unknown items
                                context.DrawRectangle(new SolidColorBrush(Colors.LightGray), 
                                    new Pen(new SolidColorBrush(Colors.Gray), 0.5), rect);
                            }
                            
                            context.Pop(); // Pop rotation transform
                        }
                    }
                }
                
                renderTarget.Render(drawingVisual);
                
                // Create thumbnails directory in AppData
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string thumbnailsPath = Path.Combine(appDataPath, "Photobooth", "Thumbnails");
                if (!Directory.Exists(thumbnailsPath))
                {
                    Directory.CreateDirectory(thumbnailsPath);
                }
                
                // Generate unique filename
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeName = string.Join("_", templateName.Split(Path.GetInvalidFileNameChars()));
                string thumbnailFileName = $"thumb_{safeName}_{timestamp}.png";
                string thumbnailPath = Path.Combine(thumbnailsPath, thumbnailFileName);
                
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderTarget));
                
                using (var fileStream = new FileStream(thumbnailPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
                
                return thumbnailPath;
            }
            catch
            {
                return null;
            }
        }
    }
}