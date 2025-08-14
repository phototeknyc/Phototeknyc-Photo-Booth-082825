using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Photobooth.Services
{
    public class GifGenerationService
    {
        // NETSCAPE2.0 Application Extension for infinite looping
        // 0x21 0xFF - Application Extension
        // 0x0B - Block size (11 bytes)
        // NETSCAPE2.0 - Application identifier
        // 0x03 0x01 - Sub-block with loop count
        // 0x00 0x00 - Loop count (0 = infinite)
        // 0x00 - Block terminator
        private static readonly byte[] GifAnimationHeader = new byte[] { 
            0x21, 0xFF, 0x0B, 
            0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, // "NETSCAPE2.0"
            0x03, 0x01, 0x00, 0x00, // Sub-block: infinite loop
            0x00 // Block terminator
        };
        private static readonly byte[] GifGraphicControlExtension = new byte[] { 0x21, 0xF9, 0x04 };
        
        public static string GenerateAnimatedGif(List<string> imagePaths, string outputPath, int frameDelayMs = 500, int maxWidth = 600, int maxHeight = 400, int quality = 85)
        {
            try
            {
                if (imagePaths == null || imagePaths.Count == 0)
                {
                    throw new ArgumentException("No images provided for GIF generation");
                }
                
                // Filter out non-existent files
                var validPaths = imagePaths.Where(File.Exists).ToList();
                if (validPaths.Count == 0)
                {
                    throw new ArgumentException("No valid image files found");
                }
                
                if (validPaths.Count == 1)
                {
                    // Single image - just copy it as-is
                    File.Copy(validPaths[0], outputPath, true);
                    return outputPath;
                }
                
                // Create GIF using System.Drawing
                using (var stream = new FileStream(outputPath, FileMode.Create))
                {
                    using (var writer = new BinaryWriter(stream))
                    {
                        // Process first image to get dimensions and write header
                        using (var firstImage = Image.FromFile(validPaths[0]))
                        {
                            var dimensions = CalculateDimensions(firstImage.Width, firstImage.Height, maxWidth, maxHeight);
                            
                            // Write GIF for first frame
                            using (var resized = ResizeImage(firstImage, dimensions.Item1, dimensions.Item2))
                            {
                                resized.Save(stream, ImageFormat.Gif);
                            }
                            
                            // Reposition to add animation header
                            stream.Position = stream.Length - 1; // Before the terminator
                            
                            // Write NETSCAPE2.0 Application Extension for looping
                            writer.Write(GifAnimationHeader);
                            
                            // Process remaining frames
                            for (int i = 1; i < validPaths.Count; i++)
                            {
                                using (var image = Image.FromFile(validPaths[i]))
                                using (var resized = ResizeImage(image, dimensions.Item1, dimensions.Item2))
                                {
                                    WriteGifFrame(writer, resized, frameDelayMs);
                                }
                            }
                            
                            // Write terminator
                            writer.Write((byte)0x3B);
                        }
                    }
                }
                
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GIF generation failed: {ex.Message}");
                throw;
            }
        }
        
        private static void WriteGifFrame(BinaryWriter writer, Image image, int delayMs)
        {
            // Convert to GIF in memory
            using (var ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Gif);
                ms.Position = 0;
                
                var gifData = ms.ToArray();
                
                // Write Graphic Control Extension
                writer.Write(GifGraphicControlExtension);
                writer.Write((byte)0x05); // Block size
                writer.Write((short)(delayMs / 10)); // Delay in 1/100 seconds
                writer.Write((byte)0x00); // Transparent color index
                writer.Write((byte)0x00); // Block terminator
                
                // Skip GIF header and write image data
                // GIF87a/89a header is 13 bytes, logical screen descriptor is 7 bytes
                writer.Write(gifData, 781, gifData.Length - 782); // Skip header, keep image data
            }
        }
        
        private static Tuple<int, int> CalculateDimensions(int originalWidth, int originalHeight, int maxWidth, int maxHeight)
        {
            double aspectRatio = (double)originalWidth / originalHeight;
            
            int newWidth = originalWidth;
            int newHeight = originalHeight;
            
            if (originalWidth > maxWidth)
            {
                newWidth = maxWidth;
                newHeight = (int)(maxWidth / aspectRatio);
            }
            
            if (newHeight > maxHeight)
            {
                newHeight = maxHeight;
                newWidth = (int)(maxHeight * aspectRatio);
            }
            
            return new Tuple<int, int>(newWidth, newHeight);
        }
        
        private static Image ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);
            
            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
            
            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                
                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }
            
            return destImage;
        }
        
        // Alternative simple approach using Windows Imaging Component with resizing
        public static string GenerateSimpleAnimatedGif(List<string> imagePaths, string outputPath, int frameDelayMs = 1000, int maxWidth = 1920, int maxHeight = 1080)
        {
            try
            {
                if (imagePaths == null || imagePaths.Count == 0)
                    return null;
                
                var validPaths = imagePaths.Where(File.Exists).ToList();
                if (validPaths.Count == 0)
                    return null;
                
                // Always use the advanced method for proper looping GIF
                return GenerateAnimatedGifWithLooping(validPaths, outputPath, frameDelayMs, maxWidth, maxHeight);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Simple GIF generation failed: {ex.Message}");
                return null;
            }
        }
        
        // Improved GIF generation with proper looping
        private static string GenerateAnimatedGifWithLooping(List<string> imagePaths, string outputPath, int frameDelayMs, int maxWidth, int maxHeight)
        {
            try
            {
                using (var outputStream = new FileStream(outputPath, FileMode.Create))
                {
                    // First, resize all images to consistent dimensions
                    var resizedImages = new List<BitmapFrame>();
                    int targetWidth = 0;
                    int targetHeight = 0;
                    
                    foreach (var imagePath in imagePaths)
                    {
                        var originalBitmap = new BitmapImage();
                        originalBitmap.BeginInit();
                        originalBitmap.UriSource = new Uri(imagePath);
                        originalBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        originalBitmap.EndInit();
                        
                        // Calculate resize dimensions for first image
                        if (targetWidth == 0)
                        {
                            double aspectRatio = (double)originalBitmap.PixelWidth / originalBitmap.PixelHeight;
                            
                            if (originalBitmap.PixelWidth > maxWidth || originalBitmap.PixelHeight > maxHeight)
                            {
                                if (aspectRatio > (double)maxWidth / maxHeight)
                                {
                                    targetWidth = maxWidth;
                                    targetHeight = (int)(maxWidth / aspectRatio);
                                }
                                else
                                {
                                    targetHeight = maxHeight;
                                    targetWidth = (int)(maxHeight * aspectRatio);
                                }
                            }
                            else
                            {
                                targetWidth = originalBitmap.PixelWidth;
                                targetHeight = originalBitmap.PixelHeight;
                            }
                        }
                        
                        // Resize image
                        var resizedBitmap = new BitmapImage();
                        resizedBitmap.BeginInit();
                        resizedBitmap.UriSource = new Uri(imagePath);
                        resizedBitmap.DecodePixelWidth = targetWidth;
                        resizedBitmap.DecodePixelHeight = targetHeight;
                        resizedBitmap.CacheOption = BitmapCacheOption.OnLoad;
                        resizedBitmap.EndInit();
                        
                        resizedImages.Add(BitmapFrame.Create(resizedBitmap));
                    }
                    
                    // Create animated GIF using System.Drawing for proper looping support
                    // This ensures NETSCAPE2.0 extension is properly added
                    return GenerateAnimatedGif(imagePaths, outputPath, frameDelayMs, maxWidth, maxHeight, 75);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GenerateAnimatedGifWithLooping failed: {ex.Message}");
                
                // Final fallback: try the original advanced method
                try
                {
                    return GenerateAnimatedGif(imagePaths, outputPath, frameDelayMs, maxWidth, maxHeight, 75);
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}