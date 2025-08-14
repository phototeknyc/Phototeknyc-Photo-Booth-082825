using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;

namespace Photobooth.Services
{
    /// <summary>
    /// GIF generation service using Magick.NET library for proper animated GIF creation with looping
    /// Requires: Install-Package Magick.NET-Q16-AnyCPU
    /// </summary>
    public class MagickGifService
    {
        public static string GenerateAnimatedGif(
            List<string> imagePaths, 
            string outputPath, 
            int frameDelayMs = 1500, 
            int maxWidth = 1920, 
            int maxHeight = 1080,
            bool addOverlay = false,
            string overlayPath = null)
        {
            try
            {
                if (imagePaths == null || !imagePaths.Any())
                {
                    System.Diagnostics.Debug.WriteLine("MagickGifService: No images provided");
                    return null;
                }

                var validPaths = imagePaths.Where(File.Exists).ToList();
                if (!validPaths.Any())
                {
                    System.Diagnostics.Debug.WriteLine("MagickGifService: No valid image files found");
                    return null;
                }

                using (var collection = new MagickImageCollection())
                {
                    // Load overlay image if specified
                    IMagickImage overlayImage = null;
                    if (addOverlay && !string.IsNullOrEmpty(overlayPath) && File.Exists(overlayPath))
                    {
                        overlayImage = new MagickImage(overlayPath);
                        overlayImage.Resize(maxWidth, maxHeight);
                    }

                    foreach (var imagePath in validPaths)
                    {
                        try
                        {
                            var image = new MagickImage(imagePath);
                            
                            // Calculate resize dimensions maintaining aspect ratio
                            var geometry = new MagickGeometry(maxWidth, maxHeight)
                            {
                                IgnoreAspectRatio = false,
                                Greater = false // Only shrink if larger
                            };
                            
                            image.Resize(geometry);
                            
                            // Set background color for any empty space
                            image.BackgroundColor = MagickColors.Black;
                            image.Extent(maxWidth, maxHeight, Gravity.Center);
                            
                            // Add overlay if specified
                            if (overlayImage != null)
                            {
                                image.Composite(overlayImage, Gravity.Center, CompositeOperator.Over);
                            }
                            
                            // Set animation delay (in 1/100th of a second)
                            image.AnimationDelay = frameDelayMs / 10;
                            
                            // Add to collection (collection takes ownership)
                            collection.Add(image);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"MagickGifService: Error processing image {imagePath}: {ex.Message}");
                        }
                    }

                    if (collection.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("MagickGifService: No images could be loaded");
                        return null;
                    }

                    // Optimize the GIF for smaller file size
                    collection.Optimize();
                    
                    // Set the GIF to loop infinitely (0 = infinite)
                    collection[0].AnimationIterations = 0;
                    
                    // Write the animated GIF
                    collection.Write(outputPath, MagickFormat.Gif);
                    
                    // Clean up overlay
                    overlayImage?.Dispose();
                    
                    System.Diagnostics.Debug.WriteLine($"MagickGifService: Successfully created animated GIF at {outputPath}");
                    System.Diagnostics.Debug.WriteLine($"  Frames: {collection.Count}, Frame delay: {frameDelayMs}ms, Size: {maxWidth}x{maxHeight}");
                    
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MagickGifService: Failed to generate GIF: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create a simple slideshow GIF with fade transitions
        /// </summary>
        public static string GenerateSlideshowGif(
            List<string> imagePaths,
            string outputPath,
            int frameDelayMs = 2000,
            int transitionFrames = 5,
            int maxWidth = 1920,
            int maxHeight = 1080)
        {
            try
            {
                if (imagePaths == null || !imagePaths.Any())
                    return null;

                var validPaths = imagePaths.Where(File.Exists).ToList();
                if (!validPaths.Any())
                    return null;

                using (var collection = new MagickImageCollection())
                {
                    for (int i = 0; i < validPaths.Count; i++)
                    {
                        var image = new MagickImage(validPaths[i]);
                        
                        // Resize image
                        var geometry = new MagickGeometry(maxWidth, maxHeight)
                        {
                            IgnoreAspectRatio = false,
                            Greater = false
                        };
                        image.Resize(geometry);
                        
                        // Center on black background
                        image.BackgroundColor = MagickColors.Black;
                        image.Extent(maxWidth, maxHeight, Gravity.Center);
                        
                        // Add main frame with delay
                        image.AnimationDelay = frameDelayMs / 10;
                        collection.Add(image.Clone());
                        
                        // Add transition frames to next image (except for last image)
                        if (i < validPaths.Count - 1 && transitionFrames > 0)
                        {
                            var nextImage = new MagickImage(validPaths[i + 1]);
                            nextImage.Resize(geometry);
                            nextImage.BackgroundColor = MagickColors.Black;
                            nextImage.Extent(maxWidth, maxHeight, Gravity.Center);
                            
                            // Create fade transition
                            for (int t = 1; t <= transitionFrames; t++)
                            {
                                var transitionFrame = image.Clone();
                                double opacity = (double)t / transitionFrames;
                                var overlayFrame = nextImage.Clone();
                                overlayFrame.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, opacity);
                                transitionFrame.Composite(overlayFrame, Gravity.Center, CompositeOperator.Over);
                                overlayFrame.Dispose();
                                
                                transitionFrame.AnimationDelay = 5; // 50ms per transition frame
                                collection.Add(transitionFrame);
                            }
                            
                            nextImage.Dispose();
                        }
                        
                        image.Dispose();
                    }

                    // Add loop back to first image
                    if (validPaths.Count > 1 && transitionFrames > 0)
                    {
                        var lastImage = new MagickImage(validPaths[validPaths.Count - 1]);
                        var firstImage = new MagickImage(validPaths[0]);
                        
                        var geometry = new MagickGeometry(maxWidth, maxHeight)
                        {
                            IgnoreAspectRatio = false,
                            Greater = false
                        };
                        
                        lastImage.Resize(geometry);
                        lastImage.BackgroundColor = MagickColors.Black;
                        lastImage.Extent(maxWidth, maxHeight, Gravity.Center);
                        
                        firstImage.Resize(geometry);
                        firstImage.BackgroundColor = MagickColors.Black;
                        firstImage.Extent(maxWidth, maxHeight, Gravity.Center);
                        
                        // Create fade transition back to first image
                        for (int t = 1; t <= transitionFrames; t++)
                        {
                            var transitionFrame = lastImage.Clone();
                            double opacity = (double)t / transitionFrames;
                            var overlayFrame = firstImage.Clone();
                            overlayFrame.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, opacity);
                            transitionFrame.Composite(overlayFrame, Gravity.Center, CompositeOperator.Over);
                            overlayFrame.Dispose();
                            
                            transitionFrame.AnimationDelay = 5;
                            collection.Add(transitionFrame);
                        }
                        
                        lastImage.Dispose();
                        firstImage.Dispose();
                    }

                    // Optimize and set infinite loop
                    collection.Optimize();
                    collection[0].AnimationIterations = 0;
                    
                    // Write the GIF
                    collection.Write(outputPath, MagickFormat.Gif);
                    
                    System.Diagnostics.Debug.WriteLine($"MagickGifService: Created slideshow GIF with {collection.Count} frames");
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MagickGifService: Slideshow generation failed: {ex.Message}");
                return null;
            }
        }
    }
}