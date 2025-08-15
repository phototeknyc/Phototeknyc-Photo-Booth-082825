using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageMagick;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for optimizing photos to reduce bandwidth and storage costs
    /// </summary>
    public class PhotoOptimizationService
    {
        // Resolution presets for different use cases
        public class ResolutionPreset
        {
            public string Name { get; set; }
            public int MaxWidth { get; set; }
            public int MaxHeight { get; set; }
            public int JpegQuality { get; set; }
            public string FileSuffix { get; set; }
        }

        // Standard presets for photobooth usage
        public static readonly ResolutionPreset ThumbnailPreset = new ResolutionPreset
        {
            Name = "Thumbnail",
            MaxWidth = 400,
            MaxHeight = 400,
            JpegQuality = 80,
            FileSuffix = "_thumb"
        };

        public static readonly ResolutionPreset WebPreset = new ResolutionPreset
        {
            Name = "Web",
            MaxWidth = 1920,
            MaxHeight = 1920,
            JpegQuality = 85,
            FileSuffix = "_web"
        };

        public static readonly ResolutionPreset SocialMediaPreset = new ResolutionPreset
        {
            Name = "Social",
            MaxWidth = 1080,
            MaxHeight = 1080,
            JpegQuality = 90,
            FileSuffix = "_social"
        };

        public static readonly ResolutionPreset PrintPreset = new ResolutionPreset
        {
            Name = "Print",
            MaxWidth = 3000,
            MaxHeight = 3000,
            JpegQuality = 95,
            FileSuffix = "_print"
        };

        private readonly bool _useImageMagick;
        
        public PhotoOptimizationService(bool useImageMagick = true)
        {
            _useImageMagick = useImageMagick;
        }

        /// <summary>
        /// Optimize a photo for web sharing (reduces file size significantly)
        /// </summary>
        public async Task<OptimizationResult> OptimizeForWebAsync(string inputPath, string outputPath = null)
        {
            return await OptimizePhotoAsync(inputPath, WebPreset, outputPath);
        }

        /// <summary>
        /// Create a thumbnail version of the photo
        /// </summary>
        public async Task<OptimizationResult> CreateThumbnailAsync(string inputPath, string outputPath = null)
        {
            return await OptimizePhotoAsync(inputPath, ThumbnailPreset, outputPath);
        }

        /// <summary>
        /// Optimize photo with custom settings
        /// </summary>
        public async Task<OptimizationResult> OptimizePhotoAsync(
            string inputPath, 
            ResolutionPreset preset, 
            string outputPath = null)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found", inputPath);

            // Generate output path if not provided
            if (string.IsNullOrEmpty(outputPath))
            {
                var dir = Path.GetDirectoryName(inputPath);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
                outputPath = Path.Combine(dir, $"{nameWithoutExt}{preset.FileSuffix}.jpg");
            }

            var result = new OptimizationResult
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                OriginalSize = new FileInfo(inputPath).Length
            };

            try
            {
                if (_useImageMagick)
                {
                    await OptimizeWithImageMagickAsync(inputPath, outputPath, preset, result);
                }
                else
                {
                    await OptimizeWithGdiPlusAsync(inputPath, outputPath, preset, result);
                }

                result.OptimizedSize = new FileInfo(outputPath).Length;
                result.CompressionRatio = (double)result.OptimizedSize / result.OriginalSize;
                result.SavedBytes = result.OriginalSize - result.OptimizedSize;
                result.SavedPercentage = (1 - result.CompressionRatio) * 100;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Create multiple optimized versions for different use cases
        /// </summary>
        public async Task<MultiVersionResult> CreateMultipleVersionsAsync(
            string inputPath,
            string outputDirectory = null,
            bool includeThumbnail = true,
            bool includeWeb = true,
            bool includeSocial = false,
            bool includePrint = false)
        {
            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = Path.GetDirectoryName(inputPath);
            }

            var result = new MultiVersionResult
            {
                InputPath = inputPath,
                OriginalSize = new FileInfo(inputPath).Length
            };

            var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
            
            // Create requested versions
            if (includeThumbnail)
            {
                var thumbPath = Path.Combine(outputDirectory, $"{nameWithoutExt}_thumb.jpg");
                var thumbResult = await OptimizePhotoAsync(inputPath, ThumbnailPreset, thumbPath);
                result.Thumbnail = thumbResult;
            }

            if (includeWeb)
            {
                var webPath = Path.Combine(outputDirectory, $"{nameWithoutExt}_web.jpg");
                var webResult = await OptimizePhotoAsync(inputPath, WebPreset, webPath);
                result.Web = webResult;
            }

            if (includeSocial)
            {
                var socialPath = Path.Combine(outputDirectory, $"{nameWithoutExt}_social.jpg");
                var socialResult = await OptimizePhotoAsync(inputPath, SocialMediaPreset, socialPath);
                result.Social = socialResult;
            }

            if (includePrint)
            {
                var printPath = Path.Combine(outputDirectory, $"{nameWithoutExt}_print.jpg");
                var printResult = await OptimizePhotoAsync(inputPath, PrintPreset, printPath);
                result.Print = printResult;
            }

            // Calculate total savings
            result.TotalOptimizedSize = 0;
            if (result.Thumbnail != null) result.TotalOptimizedSize += result.Thumbnail.OptimizedSize;
            if (result.Web != null) result.TotalOptimizedSize += result.Web.OptimizedSize;
            if (result.Social != null) result.TotalOptimizedSize += result.Social.OptimizedSize;
            if (result.Print != null) result.TotalOptimizedSize += result.Print.OptimizedSize;

            result.TotalSavedBytes = (result.OriginalSize * 
                (includeThumbnail ? 1 : 0) + (includeWeb ? 1 : 0) + 
                (includeSocial ? 1 : 0) + (includePrint ? 1 : 0)) - result.TotalOptimizedSize;
            
            result.TotalSavedPercentage = (double)result.TotalSavedBytes / 
                (result.OriginalSize * ((includeThumbnail ? 1 : 0) + (includeWeb ? 1 : 0) + 
                (includeSocial ? 1 : 0) + (includePrint ? 1 : 0))) * 100;

            return result;
        }

        /// <summary>
        /// Optimize using ImageMagick for better quality and compression
        /// </summary>
        private async Task OptimizeWithImageMagickAsync(
            string inputPath, 
            string outputPath, 
            ResolutionPreset preset,
            OptimizationResult result)
        {
            await Task.Run(() =>
            {
                using (var image = new MagickImage(inputPath))
                {
                    // Store original dimensions
                    result.OriginalWidth = image.Width;
                    result.OriginalHeight = image.Height;

                    // Remove metadata to save space
                    image.Strip();

                    // Auto-orient based on EXIF data
                    image.AutoOrient();

                    // Calculate new dimensions maintaining aspect ratio
                    var (newWidth, newHeight) = CalculateNewDimensions(
                        image.Width, image.Height, 
                        preset.MaxWidth, preset.MaxHeight);

                    // Only resize if image is larger than target
                    if (newWidth < image.Width || newHeight < image.Height)
                    {
                        image.Resize(newWidth, newHeight);
                        // Use high-quality resize filter
                        // image.FilterType = FilterType.Lanczos; // Not available in this version
                    }

                    result.OptimizedWidth = image.Width;
                    result.OptimizedHeight = image.Height;

                    // Set compression quality
                    image.Quality = preset.JpegQuality;

                    // Additional optimizations
                    image.Interlace = Interlace.Jpeg; // Progressive JPEG
                    image.ColorSpace = ColorSpace.sRGB; // Ensure sRGB color space
                    
                    // Apply slight sharpening after resize for better quality
                    if (newWidth < result.OriginalWidth)
                    {
                        image.UnsharpMask(0.5, 0.5, 0.5, 0.05);
                    }

                    // Write optimized image
                    image.Write(outputPath);
                }
            });
        }

        /// <summary>
        /// Optimize using GDI+ (fallback method)
        /// </summary>
        private async Task OptimizeWithGdiPlusAsync(
            string inputPath, 
            string outputPath, 
            ResolutionPreset preset,
            OptimizationResult result)
        {
            await Task.Run(() =>
            {
                using (var originalImage = Image.FromFile(inputPath))
                {
                    result.OriginalWidth = originalImage.Width;
                    result.OriginalHeight = originalImage.Height;

                    // Calculate new dimensions
                    var (newWidth, newHeight) = CalculateNewDimensions(
                        originalImage.Width, originalImage.Height, 
                        preset.MaxWidth, preset.MaxHeight);

                    // Only resize if needed
                    Image processedImage = originalImage;
                    bool shouldDispose = false;

                    if (newWidth < originalImage.Width || newHeight < originalImage.Height)
                    {
                        var resizedImage = new Bitmap(newWidth, newHeight);
                        using (var graphics = Graphics.FromImage(resizedImage))
                        {
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                            graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);
                        }
                        processedImage = resizedImage;
                        shouldDispose = true;
                    }

                    result.OptimizedWidth = processedImage.Width;
                    result.OptimizedHeight = processedImage.Height;

                    // Save with specified JPEG quality
                    var jpegEncoder = GetJpegEncoder();
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality, 
                        (long)preset.JpegQuality);

                    processedImage.Save(outputPath, jpegEncoder, encoderParams);

                    if (shouldDispose)
                    {
                        processedImage.Dispose();
                    }
                }
            });
        }

        /// <summary>
        /// Calculate new dimensions maintaining aspect ratio
        /// </summary>
        private (int width, int height) CalculateNewDimensions(
            int originalWidth, int originalHeight, 
            int maxWidth, int maxHeight)
        {
            if (originalWidth <= maxWidth && originalHeight <= maxHeight)
                return (originalWidth, originalHeight);

            double aspectRatio = (double)originalWidth / originalHeight;
            
            int newWidth = maxWidth;
            int newHeight = (int)(maxWidth / aspectRatio);

            if (newHeight > maxHeight)
            {
                newHeight = maxHeight;
                newWidth = (int)(maxHeight * aspectRatio);
            }

            return (newWidth, newHeight);
        }

        /// <summary>
        /// Get JPEG encoder for GDI+
        /// </summary>
        private ImageCodecInfo GetJpegEncoder()
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            return codecs.FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        }

        /// <summary>
        /// Batch optimize multiple photos
        /// </summary>
        public async Task<BatchOptimizationResult> BatchOptimizeAsync(
            string[] inputPaths,
            ResolutionPreset preset,
            string outputDirectory = null,
            int maxParallelism = 4)
        {
            var result = new BatchOptimizationResult
            {
                TotalFiles = inputPaths.Length
            };

            var semaphore = new System.Threading.SemaphoreSlim(maxParallelism);
            var tasks = inputPaths.Select(async inputPath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var optimizeResult = await OptimizePhotoAsync(inputPath, preset, 
                        outputDirectory != null ? 
                        Path.Combine(outputDirectory, Path.GetFileName(inputPath)) : null);
                    
                    return optimizeResult;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            
            result.Results = results;
            result.SuccessCount = results.Count(r => r.Success);
            result.FailureCount = results.Count(r => !r.Success);
            result.TotalOriginalSize = results.Sum(r => r.OriginalSize);
            result.TotalOptimizedSize = results.Sum(r => r.OptimizedSize);
            result.TotalSavedBytes = result.TotalOriginalSize - result.TotalOptimizedSize;
            result.AverageSavedPercentage = results.Average(r => r.SavedPercentage);

            return result;
        }
    }

    /// <summary>
    /// Result of photo optimization
    /// </summary>
    public class OptimizationResult
    {
        public bool Success { get; set; }
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public long OriginalSize { get; set; }
        public long OptimizedSize { get; set; }
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
        public int OptimizedWidth { get; set; }
        public int OptimizedHeight { get; set; }
        public double CompressionRatio { get; set; }
        public long SavedBytes { get; set; }
        public double SavedPercentage { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of creating multiple versions
    /// </summary>
    public class MultiVersionResult
    {
        public string InputPath { get; set; }
        public long OriginalSize { get; set; }
        public OptimizationResult Thumbnail { get; set; }
        public OptimizationResult Web { get; set; }
        public OptimizationResult Social { get; set; }
        public OptimizationResult Print { get; set; }
        public long TotalOptimizedSize { get; set; }
        public long TotalSavedBytes { get; set; }
        public double TotalSavedPercentage { get; set; }
    }

    /// <summary>
    /// Result of batch optimization
    /// </summary>
    public class BatchOptimizationResult
    {
        public int TotalFiles { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public long TotalOriginalSize { get; set; }
        public long TotalOptimizedSize { get; set; }
        public long TotalSavedBytes { get; set; }
        public double AverageSavedPercentage { get; set; }
        public OptimizationResult[] Results { get; set; }
    }
}