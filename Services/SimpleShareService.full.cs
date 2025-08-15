using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using QRCoder;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Photobooth.Services
{
    public class SimpleShareService
    {
        private readonly AmazonS3Client _s3Client;
        private readonly HttpClient _httpClient;
        private readonly string _bucketName = "photobooth-shares";
        private readonly string _baseShareUrl = "https://photos.yourapp.com";
        private readonly PhotoOptimizationService _optimizationService;
        
        // SMS configuration (optional)
        private readonly bool _smsEnabled;
        private readonly string _twilioAccountSid;
        private readonly string _twilioAuthToken;
        private readonly string _twilioPhoneNumber;

        public SimpleShareService()
        {
            _httpClient = new HttpClient();
            _optimizationService = new PhotoOptimizationService();
            
            // Initialize S3 client if credentials are available
            try
            {
                // Try environment variables first
                var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
                var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
                
                if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
                {
                    _s3Client = new AmazonS3Client(accessKey, secretKey, RegionEndpoint.USEast1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"S3 initialization failed: {ex.Message}");
            }
            
            // Initialize Twilio if configured
            _twilioAccountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID");
            _twilioAuthToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN");
            _twilioPhoneNumber = Environment.GetEnvironmentVariable("TWILIO_PHONE_NUMBER");
            
            _smsEnabled = !string.IsNullOrEmpty(_twilioAccountSid) && 
                         !string.IsNullOrEmpty(_twilioAuthToken) && 
                         !string.IsNullOrEmpty(_twilioPhoneNumber);
            
            if (_smsEnabled)
            {
                try
                {
                    TwilioClient.Init(_twilioAccountSid, _twilioAuthToken);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Twilio initialization failed: {ex.Message}");
                    _smsEnabled = false;
                }
            }
        }

        /// <summary>
        /// Upload photos and create a shareable gallery
        /// </summary>
        public async Task<ShareResult> CreateShareableGalleryAsync(string sessionId, List<string> photoPaths)
        {
            var result = new ShareResult
            {
                SessionId = sessionId,
                Success = false,
                UploadedPhotos = new List<UploadedPhoto>()
            };

            try
            {
                // If S3 is not configured, use local sharing only
                if (_s3Client == null)
                {
                    return CreateLocalShareResult(sessionId, photoPaths);
                }

                // Ensure bucket exists
                await EnsureBucketExistsAsync();

                // Upload each photo
                foreach (var photoPath in photoPaths)
                {
                    if (!File.Exists(photoPath))
                        continue;

                    try
                    {
                        // Optimize photo for web
                        var optimizedPath = Path.Combine(Path.GetTempPath(), $"opt_{Path.GetFileName(photoPath)}");
                        var optimizationResult = await _optimizationService.OptimizeForWebAsync(photoPath, optimizedPath);
                        
                        var fileToUpload = optimizationResult.Success ? optimizedPath : photoPath;
                        
                        // Generate S3 key
                        var key = $"sessions/{sessionId}/{Path.GetFileName(photoPath)}";
                        
                        // Upload to S3
                        using (var fileStream = File.OpenRead(fileToUpload))
                        {
                            var putRequest = new PutObjectRequest
                            {
                                BucketName = _bucketName,
                                Key = key,
                                InputStream = fileStream,
                                ContentType = "image/jpeg",
                                CannedACL = S3CannedACL.PublicRead
                            };
                            
                            await _s3Client.PutObjectAsync(putRequest);
                        }
                        
                        // Clean up temp file
                        if (File.Exists(optimizedPath))
                            File.Delete(optimizedPath);
                        
                        // Add to result
                        var photoUrl = $"https://{_bucketName}.s3.amazonaws.com/{key}";
                        result.UploadedPhotos.Add(new UploadedPhoto
                        {
                            OriginalPath = photoPath,
                            WebUrl = photoUrl,
                            ThumbnailUrl = photoUrl,
                            UploadedAt = DateTime.Now
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to upload {photoPath}: {ex.Message}");
                    }
                }

                // Generate gallery URL
                result.GalleryUrl = $"{_baseShareUrl}/gallery/{sessionId}";
                result.ShortUrl = result.GalleryUrl; // In real implementation, use URL shortener
                result.QRCodeImage = GenerateQRCode(result.GalleryUrl);
                result.Success = result.UploadedPhotos.Any();
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"Gallery creation failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Send SMS with gallery link
        /// </summary>
        public async Task<bool> SendSMSAsync(string phoneNumber, string galleryUrl)
        {
            if (!_smsEnabled)
            {
                System.Diagnostics.Debug.WriteLine("SMS not configured");
                return false;
            }

            try
            {
                var message = await MessageResource.CreateAsync(
                    body: $"Your photos are ready! 📸\nView and download: {galleryUrl}\nLink expires in 7 days.",
                    from: new PhoneNumber(_twilioPhoneNumber),
                    to: new PhoneNumber(phoneNumber)
                );

                System.Diagnostics.Debug.WriteLine($"SMS sent successfully: {message.Sid}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SMS send failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generate QR code for URL
        /// </summary>
        public BitmapImage GenerateQRCode(string url)
        {
            try
            {
                using (var qrGenerator = new QRCodeGenerator())
                {
                    var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                    using (var qrCode = new QRCode(qrCodeData))
                    {
                        using (var qrCodeImage = qrCode.GetGraphic(10))
                        {
                            using (var memory = new MemoryStream())
                            {
                                qrCodeImage.Save(memory, ImageFormat.Png);
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
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating QR code: {ex.Message}");
                return GeneratePlaceholderQRCode();
            }
        }

        /// <summary>
        /// Generate a placeholder QR code when the real one fails
        /// </summary>
        private BitmapImage GeneratePlaceholderQRCode()
        {
            try
            {
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
            catch
            {
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

        /// <summary>
        /// Ensure S3 bucket exists
        /// </summary>
        private async Task<bool> EnsureBucketExistsAsync()
        {
            try
            {
                var response = await _s3Client.ListBucketsAsync();
                if (!response.Buckets.Any(b => b.BucketName == _bucketName))
                {
                    await _s3Client.PutBucketAsync(new PutBucketRequest
                    {
                        BucketName = _bucketName,
                        BucketRegion = S3Region.USEast1
                    });
                    
                    System.Diagnostics.Debug.WriteLine($"Created bucket: {_bucketName}");
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to ensure bucket exists: {ex.Message}");
                return false;
            }
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