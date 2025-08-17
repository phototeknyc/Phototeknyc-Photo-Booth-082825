// This file requires AWS SDK and Twilio packages
#if !DESIGN_TIME_BUILD

using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Full cloud implementation with AWS S3 and Twilio
    /// </summary>
    public class CloudShareService : IShareService
    {
        private readonly AmazonS3Client _s3Client;
        private readonly HttpClient _httpClient;
        private readonly string _bucketName;
        private readonly string _baseShareUrl;
        private readonly PhotoOptimizationService _optimizationService;
        private readonly bool _smsEnabled;
        private readonly string _twilioPhoneNumber;

        public CloudShareService()
        {
            _httpClient = new HttpClient();
            _optimizationService = new PhotoOptimizationService();
            
            _bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME", EnvironmentVariableTarget.User) ?? "photobooth-shares";
            _baseShareUrl = Environment.GetEnvironmentVariable("GALLERY_BASE_URL", EnvironmentVariableTarget.User) ?? "https://photos.yourapp.com";
            
            // Initialize S3 client if credentials are available
            try
            {
                var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
                var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);
                
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
            var twilioSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID", EnvironmentVariableTarget.User);
            var twilioAuth = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN", EnvironmentVariableTarget.User);
            _twilioPhoneNumber = Environment.GetEnvironmentVariable("TWILIO_PHONE_NUMBER", EnvironmentVariableTarget.User);
            
            _smsEnabled = !string.IsNullOrEmpty(twilioSid) && 
                         !string.IsNullOrEmpty(twilioAuth) && 
                         !string.IsNullOrEmpty(_twilioPhoneNumber);
            
            if (_smsEnabled)
            {
                try
                {
                    TwilioClient.Init(twilioSid, twilioAuth);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Twilio initialization failed: {ex.Message}");
                    _smsEnabled = false;
                }
            }
        }

        public async Task<ShareResult> CreateShareableGalleryAsync(string sessionId, List<string> photoPaths, string eventName = null)
        {
            var result = new ShareResult
            {
                SessionId = sessionId,
                Success = false,
                UploadedPhotos = new List<UploadedPhoto>()
            };

            try
            {
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
                        
                        // Generate S3 key with event separation
                        string eventFolder = string.IsNullOrEmpty(eventName) ? "general" : SanitizeForS3Key(eventName);
                        var key = $"events/{eventFolder}/sessions/{sessionId}/{Path.GetFileName(photoPath)}";
                        
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
                result.ShortUrl = result.GalleryUrl;
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
                                qrCodeImage.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
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
                
                // Return placeholder
                return new StubShareService().GenerateQRCode(url);
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
        
        private string SanitizeForS3Key(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "general";
            
            // Remove or replace characters that are problematic in S3 keys
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
                .Replace("|", "-")
                .Replace("#", "-")
                .Replace("%", "-")
                .Replace("&", "-")
                .Replace("{", "-")
                .Replace("}", "-")
                .Replace("^", "-")
                .Replace("[", "-")
                .Replace("]", "-")
                .Replace("`", "-")
                .Replace("~", "-");
            
            // Remove consecutive dashes
            while (sanitized.Contains("--"))
                sanitized = sanitized.Replace("--", "-");
            
            // Trim dashes from start and end
            sanitized = sanitized.Trim('-');
            
            // Ensure it's not empty after sanitization
            if (string.IsNullOrEmpty(sanitized))
                return "general";
            
            // Limit length for S3 key compatibility
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50).TrimEnd('-');
            
            return sanitized.ToLower();
        }
    }
}

#endif