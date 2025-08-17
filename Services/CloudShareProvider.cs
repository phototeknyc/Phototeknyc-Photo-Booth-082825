using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Photobooth.Services
{
    /// <summary>
    /// Factory for creating share service with dynamic loading
    /// </summary>
    public static class CloudShareProvider
    {
        private static IShareService _instance;
        private static readonly object _lock = new object();

        public static IShareService GetShareService()
        {
            System.Diagnostics.Debug.WriteLine("CloudShareProvider.GetShareService: Called");
            
            if (_instance == null)
            {
                System.Diagnostics.Debug.WriteLine("CloudShareProvider: Instance is null, creating new instance");
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        // Try to load the full implementation with AWS/Twilio
                        try
                        {
                            System.Diagnostics.Debug.WriteLine("CloudShareProvider: Attempting to load cloud implementation...");
                            
                            // Load AWS and Twilio assemblies dynamically
                            var awsCorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AWSSDK.Core.dll");
                            var awsS3Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AWSSDK.S3.dll");
                            var twilioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Twilio.dll");
                            
                            System.Diagnostics.Debug.WriteLine($"CloudShareProvider: BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
                            System.Diagnostics.Debug.WriteLine($"CloudShareProvider: AWS Core exists: {File.Exists(awsCorePath)} at {awsCorePath}");
                            System.Diagnostics.Debug.WriteLine($"CloudShareProvider: AWS S3 exists: {File.Exists(awsS3Path)} at {awsS3Path}");
                            System.Diagnostics.Debug.WriteLine($"CloudShareProvider: Twilio exists: {File.Exists(twilioPath)} at {twilioPath}");
                            
                            if (File.Exists(awsCorePath) && File.Exists(awsS3Path))
                            {
                                System.Diagnostics.Debug.WriteLine("CloudShareProvider: Loading AWS assemblies...");
                                Assembly.LoadFrom(awsCorePath);
                                Assembly.LoadFrom(awsS3Path);
                                
                                // Twilio is optional
                                if (File.Exists(twilioPath))
                                {
                                    System.Diagnostics.Debug.WriteLine("CloudShareProvider: Loading Twilio assembly...");
                                    Assembly.LoadFrom(twilioPath);
                                }
                                
                                // Create runtime implementation directly
                                System.Diagnostics.Debug.WriteLine("CloudShareProvider: Creating CloudShareServiceRuntime instance...");
                                
                                try
                                {
                                    _instance = new CloudShareServiceRuntime();
                                    System.Diagnostics.Debug.WriteLine("CloudShareProvider: Successfully created CloudShareServiceRuntime!");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"CloudShareProvider: Failed to create CloudShareServiceRuntime: {ex.Message}");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("CloudShareProvider: Required AWS SDK files not found");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareProvider: Exception loading cloud implementation: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"CloudShareProvider: Stack trace: {ex.StackTrace}");
                        }
                        
                        // Fall back to stub implementation
                        if (_instance == null)
                        {
                            _instance = new StubShareService();
                            System.Diagnostics.Debug.WriteLine("CloudShareProvider: Using stub implementation (cloud features not available)");
                        }
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareProvider: Returning existing instance: {_instance.GetType().Name}");
            }
            
            return _instance;
        }
        
        /// <summary>
        /// Reset the provider to force reload (useful after changing settings)
        /// </summary>
        public static void Reset()
        {
            System.Diagnostics.Debug.WriteLine("CloudShareProvider.Reset: Clearing instance");
            lock (_lock)
            {
                _instance = null;
            }
        }
    }

    /// <summary>
    /// Stub implementation when cloud services are not available
    /// </summary>
    public class StubShareService : IShareService
    {
        private readonly PhotoOptimizationService _optimizationService;
        
        public StubShareService()
        {
            _optimizationService = new PhotoOptimizationService();
        }

        public async Task<ShareResult> CreateShareableGalleryAsync(string sessionId, List<string> photoPaths, string eventName = null)
        {
            return await Task.FromResult(CreateLocalShareResult(sessionId, photoPaths));
        }

        public async Task<bool> SendSMSAsync(string phoneNumber, string galleryUrl)
        {
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine($"SMS stub: Would send to {phoneNumber}: {galleryUrl}");
            return false;
        }

        public BitmapImage GenerateQRCode(string url)
        {
            try
            {
                using (var bitmap = new Bitmap(300, 300))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.White);
                        g.DrawRectangle(Pens.Black, 0, 0, 299, 299);
                        
                        // Draw QR-like corner patterns
                        DrawFinderPattern(g, 10, 10);
                        DrawFinderPattern(g, 230, 10);
                        DrawFinderPattern(g, 10, 230);
                        
                        // Draw some data patterns
                        var random = new Random(url.GetHashCode());
                        for (int x = 90; x < 210; x += 10)
                        {
                            for (int y = 90; y < 210; y += 10)
                            {
                                if (random.Next(2) == 1)
                                {
                                    g.FillRectangle(Brushes.Black, x, y, 8, 8);
                                }
                            }
                        }
                        
                        // Add URL text at bottom
                        using (var font = new Font("Arial", 7))
                        {
                            var shortUrl = url.Length > 40 ? url.Substring(0, 37) + "..." : url;
                            g.DrawString(shortUrl, font, Brushes.Black, 5, 280);
                        }
                    }
                    
                    using (var memory = new MemoryStream())
                    {
                        bitmap.Save(memory, ImageFormat.Png);
                        memory.Position = 0;
                        
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memory;
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();
                        
                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating QR code: {ex.Message}");
                return null;
            }
        }
        
        private void DrawFinderPattern(Graphics g, int x, int y)
        {
            // Draw QR code finder pattern
            g.FillRectangle(Brushes.Black, x, y, 60, 60);
            g.FillRectangle(Brushes.White, x + 10, y + 10, 40, 40);
            g.FillRectangle(Brushes.Black, x + 20, y + 20, 20, 20);
        }

        private ShareResult CreateLocalShareResult(string sessionId, List<string> photoPaths)
        {
            var localUrl = $"file:///{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Photobooth", "Sessions", sessionId)}";
            
            return new ShareResult
            {
                SessionId = sessionId,
                Success = true,
                GalleryUrl = localUrl,
                ShortUrl = localUrl,
                QRCodeImage = GenerateQRCode(localUrl),
                UploadedPhotos = photoPaths.Select(p => new UploadedPhoto 
                { 
                    OriginalPath = p,
                    WebUrl = p,
                    ThumbnailUrl = p,
                    UploadedAt = DateTime.Now
                }).ToList()
            };
        }
    }

    // ShareResult and UploadedPhoto are defined in SimpleShareServiceStub.cs
}