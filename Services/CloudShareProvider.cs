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
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        // Try to load the full implementation with AWS/Twilio
                        try
                        {
                            // Load AWS and Twilio assemblies dynamically
                            var awsCorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AWSSDK.Core.dll");
                            var awsS3Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AWSSDK.S3.dll");
                            var twilioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Twilio.dll");
                            
                            if (File.Exists(awsCorePath) && File.Exists(awsS3Path) && File.Exists(twilioPath))
                            {
                                Assembly.LoadFrom(awsCorePath);
                                Assembly.LoadFrom(awsS3Path);
                                Assembly.LoadFrom(twilioPath);
                                
                                // Create full implementation via reflection
                                var fullImplType = Type.GetType("Photobooth.Services.CloudShareService, Photobooth");
                                if (fullImplType != null)
                                {
                                    _instance = (IShareService)Activator.CreateInstance(fullImplType);
                                    System.Diagnostics.Debug.WriteLine("Loaded full cloud implementation");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not load cloud implementation: {ex.Message}");
                        }
                        
                        // Fall back to stub implementation
                        if (_instance == null)
                        {
                            _instance = new StubShareService();
                            System.Diagnostics.Debug.WriteLine("Using stub implementation");
                        }
                    }
                }
            }
            
            return _instance;
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

        public async Task<ShareResult> CreateShareableGalleryAsync(string sessionId, List<string> photoPaths)
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
                using (var bitmap = new Bitmap(200, 200))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.White);
                        g.DrawRectangle(Pens.Black, 0, 0, 199, 199);
                        
                        for (int i = 0; i < 10; i++)
                        {
                            for (int j = 0; j < 10; j++)
                            {
                                if ((i + j) % 2 == 0)
                                {
                                    g.FillRectangle(Brushes.Black, i * 20, j * 20, 20, 20);
                                }
                            }
                        }
                        
                        using (var font = new Font("Arial", 8))
                        {
                            g.DrawString("QR", font, Brushes.Black, 85, 90);
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