using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows;
using Photobooth.Database;

namespace Photobooth.Services
{
    public class ImageProcessingService
    {
        /// <summary>
        /// Resize and crop image to fit placeholder while maintaining aspect ratio or filling completely
        /// </summary>
        public async Task<WriteableBitmap> ResizeImageForPlaceholder(WriteableBitmap sourceImage, PhotoPlaceholder placeholder)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Convert WriteableBitmap to System.Drawing.Bitmap for processing
                    var sourceBitmap = WriteableBitmapToBitmap(sourceImage);
                    
                    // Calculate target dimensions
                    var targetWidth = (int)placeholder.Width;
                    var targetHeight = (int)placeholder.Height;
                    
                    Bitmap processedBitmap;
                    
                    if (placeholder.MaintainAspectRatio)
                    {
                        // Resize maintaining aspect ratio (may leave empty space)
                        processedBitmap = ResizeKeepAspectRatio(sourceBitmap, targetWidth, targetHeight);
                    }
                    else
                    {
                        // Resize to fill completely (may crop)
                        processedBitmap = ResizeAndCrop(sourceBitmap, targetWidth, targetHeight);
                    }
                    
                    // Convert back to WriteableBitmap
                    var result = BitmapToWriteableBitmap(processedBitmap);
                    
                    // Cleanup
                    sourceBitmap.Dispose();
                    processedBitmap.Dispose();
                    
                    return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Image processing error: {ex.Message}");
                    return sourceImage; // Return original if processing fails
                }
            });
        }

        /// <summary>
        /// Resize image to fit within bounds while maintaining aspect ratio
        /// </summary>
        private Bitmap ResizeKeepAspectRatio(Bitmap source, int maxWidth, int maxHeight)
        {
            var ratioX = (double)maxWidth / source.Width;
            var ratioY = (double)maxHeight / source.Height;
            var ratio = Math.Min(ratioX, ratioY);

            var newWidth = (int)(source.Width * ratio);
            var newHeight = (int)(source.Height * ratio);

            var destImage = new Bitmap(maxWidth, maxHeight);
            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.Clear(Color.Transparent);

                // Center the image
                var x = (maxWidth - newWidth) / 2;
                var y = (maxHeight - newHeight) / 2;

                graphics.DrawImage(source, x, y, newWidth, newHeight);
            }

            return destImage;
        }

        /// <summary>
        /// Resize and crop image to fill bounds completely
        /// </summary>
        private Bitmap ResizeAndCrop(Bitmap source, int targetWidth, int targetHeight)
        {
            var ratioX = (double)targetWidth / source.Width;
            var ratioY = (double)targetHeight / source.Height;
            var ratio = Math.Max(ratioX, ratioY); // Use max to fill completely

            var newWidth = (int)(source.Width * ratio);
            var newHeight = (int)(source.Height * ratio);

            var destImage = new Bitmap(targetWidth, targetHeight);
            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;

                // Center the crop
                var x = (targetWidth - newWidth) / 2;
                var y = (targetHeight - newHeight) / 2;

                graphics.DrawImage(source, x, y, newWidth, newHeight);
            }

            return destImage;
        }

        /// <summary>
        /// Generate final composite image with all photos inserted into template
        /// </summary>
        public async Task<WriteableBitmap> GenerateComposite(TemplateData template, WriteableBitmap[] capturedPhotos)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var canvasWidth = (int)template.CanvasWidth;
                    var canvasHeight = (int)template.CanvasHeight;
                    
                    var composite = new Bitmap(canvasWidth, canvasHeight);
                    
                    using (var graphics = Graphics.FromImage(composite))
                    {
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;

                        // Set background color
                        var backgroundColor = Color.White;
                        if (!string.IsNullOrEmpty(template.BackgroundColor))
                        {
                            try
                            {
                                backgroundColor = ColorTranslator.FromHtml(template.BackgroundColor);
                            }
                            catch { /* Use default white */ }
                        }
                        graphics.Clear(backgroundColor);

                        // TODO: Draw background image if template has one
                        
                        // Insert captured photos into placeholders
                        var photoboothService = new PhotoboothService();
                        var placeholders = photoboothService.GetPhotoPlaceholders(template);
                        
                        for (int i = 0; i < Math.Min(capturedPhotos.Length, placeholders.Count); i++)
                        {
                            if (capturedPhotos[i] != null)
                            {
                                var placeholder = placeholders[i];
                                var photoBitmap = WriteableBitmapToBitmap(capturedPhotos[i]);
                                
                                var destRect = new Rectangle(
                                    (int)placeholder.X,
                                    (int)placeholder.Y,
                                    (int)placeholder.Width,
                                    (int)placeholder.Height
                                );
                                
                                graphics.DrawImage(photoBitmap, destRect);
                                photoBitmap.Dispose();
                            }
                        }
                    }
                    
                    return BitmapToWriteableBitmap(composite);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Composite generation error: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// Convert WriteableBitmap to System.Drawing.Bitmap
        /// </summary>
        private Bitmap WriteableBitmapToBitmap(WriteableBitmap writeableBitmap)
        {
            using (var stream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(writeableBitmap));
                encoder.Save(stream);
                
                return new Bitmap(stream);
            }
        }

        /// <summary>
        /// Convert System.Drawing.Bitmap to WriteableBitmap
        /// </summary>
        private WriteableBitmap BitmapToWriteableBitmap(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return new WriteableBitmap(bitmapImage);
            }
        }

        /// <summary>
        /// Apply basic image enhancements (brightness, contrast, saturation)
        /// </summary>
        public async Task<WriteableBitmap> EnhanceImage(WriteableBitmap source, float brightness = 0f, float contrast = 1f, float saturation = 1f)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var sourceBitmap = WriteableBitmapToBitmap(source);
                    var result = ApplyImageEnhancements(sourceBitmap, brightness, contrast, saturation);
                    var writeableBitmap = BitmapToWriteableBitmap(result);
                    
                    sourceBitmap.Dispose();
                    result.Dispose();
                    
                    return writeableBitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Image enhancement error: {ex.Message}");
                    return source;
                }
            });
        }

        private Bitmap ApplyImageEnhancements(Bitmap source, float brightness, float contrast, float saturation)
        {
            var result = new Bitmap(source.Width, source.Height);
            
            using (var graphics = Graphics.FromImage(result))
            {
                // Create color matrix for adjustments
                var colorMatrix = new ColorMatrix();
                
                // Brightness
                colorMatrix.Matrix40 = brightness;
                colorMatrix.Matrix41 = brightness;
                colorMatrix.Matrix42 = brightness;
                
                // Contrast
                colorMatrix.Matrix00 = contrast;
                colorMatrix.Matrix11 = contrast;
                colorMatrix.Matrix22 = contrast;
                
                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);
                
                graphics.DrawImage(source,
                    new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height,
                    GraphicsUnit.Pixel, attributes);
            }
            
            return result;
        }
    }
}