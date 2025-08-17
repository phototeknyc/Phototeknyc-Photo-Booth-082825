using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Photobooth.Services
{
    /// <summary>
    /// Simple sharing service for backwards compatibility
    /// This wraps the CloudShareProvider to get the appropriate implementation
    /// </summary>
    public class SimpleShareService : IShareService
    {
        private readonly PhotoOptimizationService _optimizationService;
        
        public SimpleShareService()
        {
            _optimizationService = new PhotoOptimizationService();
        }

        /// <summary>
        /// Upload photos and create a shareable gallery (STUB)
        /// </summary>
        public async Task<ShareResult> CreateShareableGalleryAsync(string sessionId, List<string> photoPaths, string eventName = null)
        {
            // Stub implementation - returns local result
            return await Task.FromResult(CreateLocalShareResult(sessionId, photoPaths));
        }

        /// <summary>
        /// Send SMS with gallery link (STUB)
        /// </summary>
        public async Task<bool> SendSMSAsync(string phoneNumber, string galleryUrl)
        {
            // Stub implementation
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine($"SMS stub: Would send to {phoneNumber}: {galleryUrl}");
            return false; // SMS not available in stub
        }

        /// <summary>
        /// Generate QR code for URL
        /// </summary>
        public BitmapImage GenerateQRCode(string url)
        {
            try
            {
                // Create a simple placeholder QR code image
                using (var bitmap = new Bitmap(200, 200))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.White);
                        g.DrawRectangle(Pens.Black, 0, 0, 199, 199);
                        
                        // Draw placeholder pattern
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
                        
                        // Add text
                        using (var font = new Font("Arial", 8))
                        {
                            g.DrawString("QR", font, Brushes.Black, 85, 90);
                        }
                    }
                    
                    // Convert to BitmapImage
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

        /// <summary>
        /// Create local share result (fallback when cloud is not available)
        /// </summary>
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

    /// <summary>
    /// Result of sharing operation
    /// </summary>
    public class ShareResult
    {
        public string SessionId { get; set; }
        public bool Success { get; set; }
        public string GalleryUrl { get; set; }
        public string ShortUrl { get; set; }
        public BitmapImage QRCodeImage { get; set; }
        public List<UploadedPhoto> UploadedPhotos { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Uploaded photo information
    /// </summary>
    public class UploadedPhoto
    {
        public string OriginalPath { get; set; }
        public string WebUrl { get; set; }
        public string ThumbnailUrl { get; set; }
        public DateTime UploadedAt { get; set; }
    }
}