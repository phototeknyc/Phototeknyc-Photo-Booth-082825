using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using QRCoder;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Photobooth.Services
{
    /// <summary>
    /// Runtime cloud share service that directly uses AWS SDK
    /// </summary>
    public class CloudShareServiceRuntime : IShareService
    {
        private dynamic _s3Client;
        private readonly string _bucketName;
        private readonly string _baseShareUrl;
        private Type _s3ClientType;
        private Type _putObjectRequestType;
        private Type _putBucketRequestType;
        private Type _regionEndpointType;
        private object _s3CannedACL_PublicRead;
        
        // Image optimization settings
        private const int MAX_IMAGE_WIDTH = 1200;   // Max width for uploaded images
        private const int MAX_IMAGE_HEIGHT = 1200;  // Max height for uploaded images
        private const long JPEG_QUALITY = 85;       // JPEG quality (0-100, 85 is good balance)
        private const int THUMBNAIL_SIZE = 400;     // Size for thumbnails
        
        // URL shortening settings
        private const bool ENABLE_URL_SHORTENING = true;  // Enable URL shortening for long S3 URLs
        private const int URL_LENGTH_THRESHOLD = 100;     // Shorten URLs longer than this
        
        public CloudShareServiceRuntime()
        {
            System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: Constructor called");
            
            try
            {
                _bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME", EnvironmentVariableTarget.User) ?? "photobooth-shares";
                _baseShareUrl = Environment.GetEnvironmentVariable("GALLERY_BASE_URL", EnvironmentVariableTarget.User) ?? "https://phototeknyc.s3.amazonaws.com";
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Bucket Name: {_bucketName}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Base URL: {_baseShareUrl}");
                
                var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
                var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);
                var region = Environment.GetEnvironmentVariable("S3_REGION", EnvironmentVariableTarget.User) ?? "us-east-1";
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Checking credentials - AccessKey: {!string.IsNullOrEmpty(accessKey)}, SecretKey: {!string.IsNullOrEmpty(secretKey)}");
                
                if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: Credentials found, initializing AWS client...");
                    InitializeAwsClient(accessKey, secretKey, region);
                    
                    // Write success to debug file
                    var debugFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudshare_debug.txt");
                    File.AppendAllText(debugFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CloudShareServiceRuntime: AWS credentials found, initializing client\r\n");
                    File.AppendAllText(debugFile, $"  Bucket: {_bucketName}, Region: {region}\r\n");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: No AWS credentials found in User environment variables");
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS_ACCESS_KEY_ID = '{accessKey}'");
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS_SECRET_ACCESS_KEY = '{secretKey}'");
                    
                    // Write error to debug file
                    var debugFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudshare_debug.txt");
                    File.AppendAllText(debugFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CloudShareServiceRuntime: No AWS credentials found\r\n");
                    File.AppendAllText(debugFile, $"  AWS_ACCESS_KEY_ID present: {!string.IsNullOrEmpty(accessKey)}\r\n");
                    File.AppendAllText(debugFile, $"  AWS_SECRET_ACCESS_KEY present: {!string.IsNullOrEmpty(secretKey)}\r\n");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Constructor exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Stack trace: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Check if internet connection is available
        /// </summary>
        private async Task<bool> IsInternetAvailableAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = await client.GetAsync("https://www.google.com");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }
        
        private void InitializeAwsClient(string accessKey, string secretKey, string region)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Starting AWS client initialization...");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Access Key: {(string.IsNullOrEmpty(accessKey) ? "NOT SET" : "SET (" + accessKey.Length + " chars)")}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Secret Key: {(string.IsNullOrEmpty(secretKey) ? "NOT SET" : "SET (" + secretKey.Length + " chars)")}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Region: {region}");
                
                // Load AWS assemblies
                var awsCorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AWSSDK.Core.dll");
                var awsS3Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AWSSDK.S3.dll");
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS Core path: {awsCorePath}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS Core exists: {File.Exists(awsCorePath)}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS S3 path: {awsS3Path}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS S3 exists: {File.Exists(awsS3Path)}");
                
                if (!File.Exists(awsCorePath) || !File.Exists(awsS3Path))
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS SDK files not found, cannot initialize");
                    _s3Client = null;
                    return;
                }
                
                var awsCoreAssembly = Assembly.LoadFrom(awsCorePath);
                var awsS3Assembly = Assembly.LoadFrom(awsS3Path);
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Loaded AWS Core assembly: {awsCoreAssembly.FullName}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Loaded AWS S3 assembly: {awsS3Assembly.FullName}");
                
                // Get types
                _regionEndpointType = awsCoreAssembly.GetType("Amazon.RegionEndpoint");
                _s3ClientType = awsS3Assembly.GetType("Amazon.S3.AmazonS3Client");
                _putObjectRequestType = awsS3Assembly.GetType("Amazon.S3.Model.PutObjectRequest");
                _putBucketRequestType = awsS3Assembly.GetType("Amazon.S3.Model.PutBucketRequest");
                var s3CannedACLType = awsS3Assembly.GetType("Amazon.S3.S3CannedACL");
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: RegionEndpoint type: {_regionEndpointType != null}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: S3Client type: {_s3ClientType != null}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: PutObjectRequest type: {_putObjectRequestType != null}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: S3CannedACL type: {s3CannedACLType != null}");
                
                if (_regionEndpointType == null || _s3ClientType == null || _putObjectRequestType == null || s3CannedACLType == null)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to get required types");
                    _s3Client = null;
                    return;
                }
                
                // Get PublicRead value
                _s3CannedACL_PublicRead = s3CannedACLType.GetField("PublicRead").GetValue(null);
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Got PublicRead ACL: {_s3CannedACL_PublicRead != null}");
                
                // Get region endpoint
                var getBySystemNameMethod = _regionEndpointType.GetMethod("GetBySystemName");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: GetBySystemName method: {getBySystemNameMethod != null}");
                
                var regionEndpoint = getBySystemNameMethod.Invoke(null, new object[] { region });
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Region endpoint created: {regionEndpoint != null}");
                
                // Create S3 client
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Creating S3 client with constructor parameters...");
                _s3Client = Activator.CreateInstance(_s3ClientType, accessKey, secretKey, regionEndpoint);
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: S3 client created: {_s3Client != null}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Successfully initialized with bucket {_bucketName} in region {region}");
                
                // Write success to debug file
                var debugFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cloudshare_debug.txt");
                File.AppendAllText(debugFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CloudShareServiceRuntime: S3 client successfully created for bucket {_bucketName} in region {region}\r\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to initialize AWS client: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Inner exception: {ex.InnerException.Message}");
                }
                _s3Client = null;
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
                // CRITICAL: Check internet connectivity first
                if (!await IsInternetAvailableAsync())
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: No internet connection detected");
                    
                    // Queue for offline retry immediately
                    var offlineQueue = OfflineQueueService.Instance;
                    var queueResult = await offlineQueue.QueuePhotosForUpload(sessionId, photoPaths, eventName);
                    
                    if (queueResult.Success)
                    {
                        result.ErrorMessage = "No internet connection - photos queued for upload when connection is restored";
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Queued {photoPaths.Count} photos for offline retry (no internet)");
                    }
                    else
                    {
                        result.ErrorMessage = "No internet connection and failed to queue for retry";
                    }
                    
                    return result;
                }
                
                // Try to initialize if not already done
                if (_s3Client == null)
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: S3 client is null, attempting to initialize...");
                    
                    var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
                    var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);
                    var region = Environment.GetEnvironmentVariable("S3_REGION", EnvironmentVariableTarget.User) ?? "us-east-1";
                    
                    if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
                    {
                        InitializeAwsClient(accessKey, secretKey, region);
                    }
                    
                    if (_s3Client == null)
                    {
                        System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: Failed to initialize S3 client");
                        result.ErrorMessage = "AWS S3 client not initialized - check credentials";
                        return result;
                    }
                }
                
                // Ensure bucket exists
                await EnsureBucketExistsAsync();
                
                // Upload each photo or video
                foreach (var photoPath in photoPaths)
                {
                    if (!File.Exists(photoPath))
                        continue;
                    
                    try
                    {
                        // Generate S3 key with event separation
                        string eventFolder = string.IsNullOrEmpty(eventName) ? "general" : SanitizeForS3Key(eventName);
                        var key = $"events/{eventFolder}/sessions/{sessionId}/{Path.GetFileName(photoPath)}";
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Uploading {Path.GetFileName(photoPath)} with key: {key}");
                        
                        // Check if types are initialized
                        if (_putObjectRequestType == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: PutObjectRequest type is null, skipping upload");
                            continue;
                        }
                        
                        // Check if this is a video file
                        bool isVideo = photoPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || 
                                      photoPath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                                      photoPath.EndsWith(".avi", StringComparison.OrdinalIgnoreCase);
                        
                        if (isVideo)
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Processing video file: {Path.GetFileName(photoPath)}");
                            
                            string videoToUpload = photoPath;
                            string compressedPath = null;
                            
                            // Check if compression is enabled
                            bool compressionEnabled = Properties.Settings.Default.EnableVideoCompression;
                            
                            if (compressionEnabled)
                            {
                                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Compressing video before upload...");
                                
                                // Compress the video
                                var compressionService = VideoCompressionService.Instance;
                                if (compressionService.IsFFmpegAvailable())
                                {
                                    try
                                    {
                                        // Generate compressed file path
                                        string tempDir = Path.GetTempPath();
                                        string compressedFileName = $"compressed_{Guid.NewGuid():N}.mp4";
                                        compressedPath = Path.Combine(tempDir, compressedFileName);
                                        
                                        // Compress the video
                                        string resultPath = await compressionService.CompressVideoAsync(photoPath, compressedPath);
                                        
                                        if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                                        {
                                            videoToUpload = resultPath;
                                            
                                            // Log compression results
                                            long originalSize = new FileInfo(photoPath).Length;
                                            long compressedSize = new FileInfo(resultPath).Length;
                                            double reductionPercent = (1 - (double)compressedSize / originalSize) * 100;
                                            
                                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Video compressed successfully");
                                            System.Diagnostics.Debug.WriteLine($"  Original: {originalSize / 1024.0 / 1024.0:F2} MB");
                                            System.Diagnostics.Debug.WriteLine($"  Compressed: {compressedSize / 1024.0 / 1024.0:F2} MB");
                                            System.Diagnostics.Debug.WriteLine($"  Reduction: {reductionPercent:F1}%");
                                            
                                            // Save a copy of the compressed video locally in webupload folder
                                            try
                                            {
                                                // Get the event folder from the original video path
                                                string originalDir = Path.GetDirectoryName(photoPath);
                                                string videoEventFolder = Path.GetDirectoryName(originalDir); // Go up from videos folder to event folder
                                                string webUploadFolder = Path.Combine(videoEventFolder, "webupload");
                                                
                                                // Create webupload folder if it doesn't exist
                                                if (!Directory.Exists(webUploadFolder))
                                                {
                                                    Directory.CreateDirectory(webUploadFolder);
                                                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Created webupload folder: {webUploadFolder}");
                                                }
                                                
                                                // Copy compressed video to webupload folder
                                                string originalFileName = Path.GetFileNameWithoutExtension(photoPath);
                                                string localCompressedPath = Path.Combine(webUploadFolder, $"{originalFileName}_webupload.mp4");
                                                File.Copy(resultPath, localCompressedPath, true);
                                                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Saved compressed video locally: {localCompressedPath}");
                                            }
                                            catch (Exception ex)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to save compressed video locally: {ex.Message}");
                                            }
                                        }
                                        else
                                        {
                                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Compression failed, uploading original");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Error compressing video: {ex.Message}");
                                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Uploading original video");
                                    }
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: FFmpeg not available, uploading original");
                                }
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Uploading video: {Path.GetFileName(videoToUpload)}");
                            
                            // Open with shared read access to prevent conflicts with video player
                            using (var videoStream = new FileStream(videoToUpload, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                var putRequest = Activator.CreateInstance(_putObjectRequestType);
                                var putRequestType = putRequest.GetType();
                                
                                putRequestType.GetProperty("BucketName").SetValue(putRequest, _bucketName);
                                putRequestType.GetProperty("Key").SetValue(putRequest, key);
                                putRequestType.GetProperty("InputStream").SetValue(putRequest, videoStream);
                                putRequestType.GetProperty("ContentType").SetValue(putRequest, "video/mp4");
                                
                                // Upload the video
                                await UploadToS3Async(putRequest);
                                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Successfully uploaded video {Path.GetFileName(videoToUpload)} to S3");
                            }
                            
                            // Clean up compressed file if it was created
                            if (!string.IsNullOrEmpty(compressedPath) && File.Exists(compressedPath))
                            {
                                try
                                {
                                    File.Delete(compressedPath);
                                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Cleaned up compressed video file");
                                }
                                catch
                                {
                                    // Ignore cleanup errors
                                }
                            }
                            
                            // Generate pre-signed URL for video
                            var videoUrl = GeneratePresignedUrl(key);
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Generated video URL: {videoUrl}");
                            
                            // Add to result as video
                            result.UploadedPhotos.Add(new UploadedPhoto
                            {
                                OriginalPath = photoPath,
                                WebUrl = videoUrl,
                                ThumbnailUrl = videoUrl, // For videos, use same URL as thumbnail
                                UploadedAt = DateTime.Now,
                                IsVideo = true
                            });
                            continue; // Skip image processing for videos
                        }
                        
                        // For images, process as before
                        // Resize image for upload
                        var resizedImageBytes = ResizeImageForUpload(photoPath);
                        
                        // Create thumbnail
                        var thumbnailBytes = CreateThumbnail(photoPath);
                        var thumbnailKey = $"events/{eventFolder}/sessions/{sessionId}/thumbs/{Path.GetFileName(photoPath)}";
                        
                        // Upload thumbnail first
                        using (var thumbnailStream = new MemoryStream(thumbnailBytes))
                        {
                            var thumbnailRequest = Activator.CreateInstance(_putObjectRequestType);
                            var thumbnailRequestType = thumbnailRequest.GetType();
                            
                            thumbnailRequestType.GetProperty("BucketName").SetValue(thumbnailRequest, _bucketName);
                            thumbnailRequestType.GetProperty("Key").SetValue(thumbnailRequest, thumbnailKey);
                            thumbnailRequestType.GetProperty("InputStream").SetValue(thumbnailRequest, thumbnailStream);
                            thumbnailRequestType.GetProperty("ContentType").SetValue(thumbnailRequest, "image/jpeg");
                            
                            // Upload thumbnail
                            await UploadToS3Async(thumbnailRequest);
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Thumbnail uploaded to {thumbnailKey}");
                        }
                        
                        // Upload resized main image to S3
                        using (var imageStream = new MemoryStream(resizedImageBytes))
                        {
                            var putRequest = Activator.CreateInstance(_putObjectRequestType);
                            var putRequestType = putRequest.GetType();
                            
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Created PutObjectRequest instance for resized image");
                            
                            putRequestType.GetProperty("BucketName").SetValue(putRequest, _bucketName);
                            putRequestType.GetProperty("Key").SetValue(putRequest, key);
                            putRequestType.GetProperty("InputStream").SetValue(putRequest, imageStream);
                            putRequestType.GetProperty("ContentType").SetValue(putRequest, "image/jpeg");
                            // Don't set CannedACL - the bucket doesn't allow ACLs
                            // putRequestType.GetProperty("CannedACL").SetValue(putRequest, _s3CannedACL_PublicRead);
                            
                            // Upload the main image using helper method
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Starting main image upload...");
                            await UploadToS3Async(putRequest);
                            
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Successfully uploaded {Path.GetFileName(photoPath)} to S3");
                        }
                        
                        // Generate pre-signed URLs for both main image and thumbnail
                        var photoUrl = GeneratePresignedUrl(key);
                        var thumbnailUrl = GeneratePresignedUrl(thumbnailKey);
                        
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Generated URLs - Main: {photoUrl}, Thumbnail: {thumbnailUrl}");
                        
                        // Add to result
                        result.UploadedPhotos.Add(new UploadedPhoto
                        {
                            OriginalPath = photoPath,
                            WebUrl = photoUrl,
                            ThumbnailUrl = thumbnailUrl,
                            UploadedAt = DateTime.Now
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to upload {photoPath}: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Stack trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Inner exception: {ex.InnerException.Message}");
                        }
                    }
                }
                
                // Create and upload HTML gallery page if we have photos
                if (result.UploadedPhotos.Any())
                {
                    string galleryHtml = GenerateGalleryHtml(result.UploadedPhotos, sessionId);
                    string eventFolder = string.IsNullOrEmpty(eventName) ? "general" : SanitizeForS3Key(eventName);
                    string galleryKey = $"events/{eventFolder}/sessions/{sessionId}/index.html";
                    
                    if (UploadHtmlToS3(galleryHtml, galleryKey))
                    {
                        // Generate pre-signed URL for the gallery page
                        var longGalleryUrl = GeneratePresignedUrl(galleryKey);
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Gallery HTML uploaded, long URL: {longGalleryUrl}");
                        
                        // Shorten the URL for better usability
                        if (ENABLE_URL_SHORTENING && longGalleryUrl.Length > URL_LENGTH_THRESHOLD)
                        {
                            result.GalleryUrl = await ShortenUrlAsync(longGalleryUrl);
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Shortened gallery URL: {result.GalleryUrl}");
                        }
                        else
                        {
                            result.GalleryUrl = longGalleryUrl;
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Using original gallery URL (shortening disabled or URL short enough)");
                        }
                    }
                }
                
                // Fallback if gallery HTML wasn't created
                if (string.IsNullOrEmpty(result.GalleryUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Uploaded photos count: {result.UploadedPhotos.Count}");
                    if (result.UploadedPhotos.Any())
                    {
                        var firstPhotoUrl = result.UploadedPhotos.First().WebUrl;
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Using first photo URL as fallback gallery: {firstPhotoUrl}");
                        
                        // Shorten the first photo URL too if it's long
                        if (ENABLE_URL_SHORTENING && firstPhotoUrl.Length > URL_LENGTH_THRESHOLD)
                        {
                            result.GalleryUrl = await ShortenUrlAsync(firstPhotoUrl);
                        }
                        else
                        {
                            result.GalleryUrl = firstPhotoUrl;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: No photos uploaded, using fallback gallery URL");
                        result.GalleryUrl = $"{_baseShareUrl}/gallery/{sessionId}";
                    }
                }
                result.ShortUrl = result.GalleryUrl;
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Generated gallery URL: {result.GalleryUrl}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Base URL: {_baseShareUrl}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Session ID: {sessionId}");
                
                result.QRCodeImage = GenerateQRCode(result.GalleryUrl);
                result.Success = result.UploadedPhotos.Any();
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Upload complete - {result.UploadedPhotos.Count} photos uploaded");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Gallery creation failed: {ex.Message}");
                
                // CRITICAL: Queue for offline retry when AWS upload fails
                try
                {
                    var offlineQueue = OfflineQueueService.Instance;
                    var queueResult = await offlineQueue.QueuePhotosForUpload(sessionId, photoPaths, eventName);
                    
                    if (queueResult.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Queued {photoPaths.Count} photos for offline retry");
                        result.ErrorMessage += " - Photos queued for retry when connection is restored";
                    }
                }
                catch (Exception queueEx)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to queue for offline retry: {queueEx.Message}");
                }
            }
            
            return result;
        }
        
        public async Task<bool> SendSMSAsync(string phoneNumber, string galleryUrl)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Attempting to send SMS to {phoneNumber}");
                
                // Format phone number - add +1 if not present for US numbers
                string formattedNumber = FormatPhoneNumber(phoneNumber);
                if (string.IsNullOrEmpty(formattedNumber))
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Invalid phone number format: {phoneNumber}");
                    return false;
                }
                
                // Get Twilio credentials from environment variables
                var accountSid = Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID", EnvironmentVariableTarget.User);
                var authToken = Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN", EnvironmentVariableTarget.User);
                var fromNumber = Environment.GetEnvironmentVariable("TWILIO_PHONE_NUMBER", EnvironmentVariableTarget.User);
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Twilio SID present: {!string.IsNullOrEmpty(accountSid)}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Twilio Auth present: {!string.IsNullOrEmpty(authToken)}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Twilio From Number: {fromNumber}");
                
                if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(fromNumber))
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Twilio credentials not configured - SID:{accountSid?.Length ?? 0} chars, Auth:{authToken?.Length ?? 0} chars, From:{fromNumber ?? "null"}");
                    return false;
                }
                
                // Format the from number too
                fromNumber = FormatPhoneNumber(fromNumber);
                
                try
                {
                    // Initialize Twilio client
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Initializing Twilio with SID: {accountSid.Substring(0, 10)}...");
                    TwilioClient.Init(accountSid, authToken);
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: Twilio initialized successfully");
                    
                    // Create and send the message
                    var messageBody = $"Here is your photo: {galleryUrl}";
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Sending SMS from {fromNumber} to {formattedNumber}");
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Message: {messageBody}");
                    
                    var message = await MessageResource.CreateAsync(
                        body: messageBody,
                        from: new PhoneNumber(fromNumber),
                        to: new PhoneNumber(formattedNumber)
                    );
                    
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: SMS sent successfully! SID: {message.Sid}");
                    return true;
                }
                catch (Twilio.Exceptions.ApiException twilioApiEx)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Twilio API error: {twilioApiEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Twilio error code: {twilioApiEx.Code}");
                    
                    // Check for permanent failures that should NOT be retried
                    bool isPermanentFailure = IsPermanentTwilioFailure(twilioApiEx);
                    
                    if (isPermanentFailure)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: PERMANENT FAILURE - Will not retry SMS to {formattedNumber}");
                        // Mark as failed in database so it won't retry
                        await MarkSmsAsPermanentlyFailed(phoneNumber, twilioApiEx.Message);
                    }
                    
                    return false;
                }
                catch (Exception twilioEx)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Twilio error: {twilioEx.Message}");
                    if (twilioEx.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Inner exception: {twilioEx.InnerException.Message}");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: SMS error: {ex.Message}");
                
                // CRITICAL: Queue SMS for offline retry when sending fails
                try
                {
                    var offlineQueue = OfflineQueueService.Instance;
                    var queueResult = await offlineQueue.QueueSMS(phoneNumber, galleryUrl, "unknown");
                    
                    if (queueResult.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: SMS queued for offline retry to {phoneNumber}");
                        return true; // Return true because it's queued for retry
                    }
                }
                catch (Exception queueEx)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to queue SMS for offline retry: {queueEx.Message}");
                }
                
                return false;
            }
        }
        
        private string GenerateGalleryHtml(List<UploadedPhoto> photos, string sessionId)
        {
            var html = @"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Photo Gallery - " + sessionId.Substring(0, 8) + @"</title>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/FileSaver.js/2.0.5/FileSaver.min.js'></script>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
        }
        h1 {
            text-align: center;
            color: white;
            margin-bottom: 30px;
            font-size: 2.5em;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
        }
        .gallery {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
            gap: 20px;
            padding: 20px;
            background: rgba(255,255,255,0.95);
            border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
        }
        .photo-item {
            position: relative;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 15px rgba(0,0,0,0.2);
            transition: transform 0.3s ease, box-shadow 0.3s ease;
            background: #f0f0f0;
        }
        .photo-item:hover {
            transform: translateY(-5px);
            box-shadow: 0 8px 25px rgba(0,0,0,0.3);
        }
        .photo-item:hover .photo-overlay {
            opacity: 1;
        }
        .photo-item img {
            width: 100%;
            height: auto;
            display: block;
        }
        .photo-number {
            position: absolute;
            top: 10px;
            right: 10px;
            background: rgba(0,0,0,0.7);
            color: white;
            padding: 5px 10px;
            border-radius: 20px;
            font-size: 0.9em;
            z-index: 2;
        }
        .photo-overlay {
            position: absolute;
            bottom: 0;
            left: 0;
            right: 0;
            background: linear-gradient(to top, rgba(0,0,0,0.8), transparent);
            padding: 15px;
            opacity: 0;
            transition: opacity 0.3s ease;
            display: flex;
            justify-content: space-around;
            align-items: center;
        }
        .action-btn {
            background: rgba(255,255,255,0.9);
            border: none;
            padding: 8px 12px;
            border-radius: 20px;
            cursor: pointer;
            font-size: 0.85em;
            display: flex;
            align-items: center;
            gap: 5px;
            transition: background 0.2s ease, transform 0.2s ease;
            text-decoration: none;
            color: #333;
        }
        .action-btn:hover {
            background: white;
            transform: scale(1.05);
        }
        .action-btn svg {
            width: 16px;
            height: 16px;
        }
        .footer {
            text-align: center;
            margin-top: 30px;
            color: white;
            font-size: 0.9em;
        }
        .modal {
            display: none;
            position: fixed;
            z-index: 1000;
            left: 0;
            top: 0;
            width: 100%;
            height: 100%;
            background: rgba(0,0,0,0.9);
            align-items: center;
            justify-content: center;
        }
        .modal.active {
            display: flex;
        }
        .modal-content {
            max-width: 90%;
            max-height: 90%;
        }
        .modal-content img {
            width: 100%;
            height: 100%;
            object-fit: contain;
        }
        .close {
            position: absolute;
            top: 20px;
            right: 40px;
            color: white;
            font-size: 40px;
            font-weight: bold;
            cursor: pointer;
        }
        .close:hover {
            color: #ccc;
        }
        .download-all-btn {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border: none;
            padding: 12px 24px;
            border-radius: 25px;
            cursor: pointer;
            font-size: 1em;
            margin: 20px auto;
            display: flex;
            align-items: center;
            gap: 8px;
            box-shadow: 0 4px 15px rgba(102, 126, 234, 0.4);
            transition: transform 0.2s ease, box-shadow 0.2s ease;
        }
        .download-all-btn:hover {
            transform: translateY(-2px);
            box-shadow: 0 6px 20px rgba(102, 126, 234, 0.6);
        }
        .share-menu {
            position: fixed;
            bottom: -100%;
            left: 0;
            right: 0;
            background: white;
            border-radius: 20px 20px 0 0;
            box-shadow: 0 -4px 20px rgba(0,0,0,0.2);
            padding: 20px;
            transition: bottom 0.3s ease;
            z-index: 2000;
        }
        .share-menu.active {
            bottom: 0;
        }
        .share-option {
            display: flex;
            align-items: center;
            padding: 15px;
            border-radius: 10px;
            cursor: pointer;
            transition: background 0.2s ease;
            text-decoration: none;
            color: #333;
        }
        .share-option:hover {
            background: #f5f5f5;
        }
        .share-option svg {
            width: 24px;
            height: 24px;
            margin-right: 15px;
        }
        @media (max-width: 768px) {
            .gallery {
                grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
                gap: 10px;
                padding: 10px;
            }
            h1 { font-size: 1.8em; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <h1>📸 Your Photos Are Ready!</h1>
        <div class='gallery'>";

            int photoNumber = 1;
            foreach (var photo in photos)
            {
                // Extract filename for download
                var fileName = Path.GetFileName(photo.OriginalPath ?? $"photo_{photoNumber}.jpg");
                
                if (photo.IsVideo)
                {
                    // Special handling for videos
                    html += $@"
            <div class='photo-item video-item'>
                <div style='position: relative; cursor: pointer;' onclick='openVideoModal(""{photo.WebUrl}"")'>
                    <video style='width: 100%; height: 100%; object-fit: cover;' muted>
                        <source src='{photo.WebUrl}' type='video/mp4'>
                    </video>
                    <div style='position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); width: 60px; height: 60px; background: rgba(0,0,0,0.7); border-radius: 50%; display: flex; align-items: center; justify-content: center;'>
                        <svg fill='white' viewBox='0 0 24 24' width='30' height='30'>
                            <path d='M8 5v14l11-7z'/>
                        </svg>
                    </div>
                    <span class='photo-number'>{photoNumber}</span>
                </div>
                <div class='photo-overlay'>
                    <a href='{photo.WebUrl}' download='{fileName}' class='action-btn' onclick='event.stopPropagation()'>
                        <svg fill='currentColor' viewBox='0 0 20 20'>
                            <path d='M3 17a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm3.293-7.707a1 1 0 011.414 0L9 10.586V3a1 1 0 112 0v7.586l1.293-1.293a1 1 0 111.414 1.414l-3 3a1 1 0 01-1.414 0l-3-3a1 1 0 010-1.414z'/>
                        </svg>
                        Download
                    </a>
                    <button class='action-btn' onclick='sharePhoto(""{photo.WebUrl}"", {photoNumber}); event.stopPropagation()'>
                        <svg fill='currentColor' viewBox='0 0 20 20'>
                            <path d='M15 8a3 3 0 10-2.977-2.63l-4.94 2.47a3 3 0 100 4.319l4.94 2.47a3 3 0 10.895-1.789l-4.94-2.47a3.027 3.027 0 000-.74l4.94-2.47C13.456 7.68 14.19 8 15 8z'/>
                        </svg>
                        Share
                    </button>
                </div>
            </div>";
                }
                else
                {
                    // Regular photo handling
                    html += $@"
            <div class='photo-item'>
                <img src='{photo.ThumbnailUrl ?? photo.WebUrl}' alt='Photo {photoNumber}' loading='lazy' onclick='openModal(""{photo.WebUrl}"")'>
                <span class='photo-number'>{photoNumber}</span>
                <div class='photo-overlay'>
                    <a href='{photo.WebUrl}' download='{fileName}' class='action-btn' onclick='event.stopPropagation()'>
                        <svg fill='currentColor' viewBox='0 0 20 20'>
                            <path d='M3 17a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm3.293-7.707a1 1 0 011.414 0L9 10.586V3a1 1 0 112 0v7.586l1.293-1.293a1 1 0 111.414 1.414l-3 3a1 1 0 01-1.414 0l-3-3a1 1 0 010-1.414z'/>
                        </svg>
                        Download
                    </a>
                    <button class='action-btn' onclick='sharePhoto(""{photo.WebUrl}"", {photoNumber}); event.stopPropagation()'>
                        <svg fill='currentColor' viewBox='0 0 20 20'>
                            <path d='M15 8a3 3 0 10-2.977-2.63l-4.94 2.47a3 3 0 100 4.319l4.94 2.47a3 3 0 10.895-1.789l-4.94-2.47a3.027 3.027 0 000-.74l4.94-2.47C13.456 7.68 14.19 8 15 8z'/>
                        </svg>
                        Share
                    </button>
                </div>
            </div>";
                }
                photoNumber++;
            }

            html += @"
        </div>
        <button class='download-all-btn' onclick='downloadAll()'>
            <svg fill='currentColor' viewBox='0 0 20 20' width='20' height='20'>
                <path d='M3 17a1 1 0 011-1h12a1 1 0 110 2H4a1 1 0 01-1-1zm3.293-7.707a1 1 0 011.414 0L9 10.586V3a1 1 0 112 0v7.586l1.293-1.293a1 1 0 111.414 1.414l-3 3a1 1 0 01-1.414 0l-3-3a1 1 0 010-1.414z'/>
            </svg>
            Download All Photos
        </button>
        <div class='footer'>
            <p>Generated on " + DateTime.Now.ToString("MMMM dd, yyyy 'at' h:mm tt") + @"</p>
            <p>This gallery will be available for 24 hours</p>
        </div>
    </div>
    
    <div id='modal' class='modal' onclick='closeModal()'>
        <span class='close'>&times;</span>
        <div class='modal-content'>
            <img id='modalImg' src=''>
        </div>
    </div>
    
    <div id='shareMenu' class='share-menu'>
        <h3 style='margin-bottom: 15px;'>Share Photo</h3>
        <a href='#' id='shareWhatsApp' class='share-option'>
            <svg fill='#25D366' viewBox='0 0 24 24'>
                <path d='M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.149-.67.149-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413Z'/>
            </svg>
            WhatsApp
        </a>
        <a href='#' id='shareFacebook' class='share-option'>
            <svg fill='#1877F2' viewBox='0 0 24 24'>
                <path d='M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z'/>
            </svg>
            Facebook
        </a>
        <a href='#' id='shareTwitter' class='share-option'>
            <svg fill='#1DA1F2' viewBox='0 0 24 24'>
                <path d='M23.953 4.57a10 10 0 01-2.825.775 4.958 4.958 0 002.163-2.723c-.951.555-2.005.959-3.127 1.184a4.92 4.92 0 00-8.384 4.482C7.69 8.095 4.067 6.13 1.64 3.162a4.822 4.822 0 00-.666 2.475c0 1.71.87 3.213 2.188 4.096a4.904 4.904 0 01-2.228-.616v.06a4.923 4.923 0 003.946 4.827 4.996 4.996 0 01-2.212.085 4.936 4.936 0 004.604 3.417 9.867 9.867 0 01-6.102 2.105c-.39 0-.779-.023-1.17-.067a13.995 13.995 0 007.557 2.209c9.053 0 13.998-7.496 13.998-13.985 0-.21 0-.42-.015-.63A9.935 9.935 0 0024 4.59z'/>
            </svg>
            Twitter
        </a>
        <button class='share-option' onclick='copyPhotoLink()'>
            <svg fill='#666' viewBox='0 0 20 20'>
                <path d='M8 3a1 1 0 011-1h2a1 1 0 110 2H9a1 1 0 01-1-1z'/>
                <path d='M6 3a2 2 0 00-2 2v11a2 2 0 002 2h8a2 2 0 002-2V5a2 2 0 00-2-2 3 3 0 01-3 3H9a3 3 0 01-3-3z'/>
            </svg>
            Copy Link
        </button>
        <button class='share-option' onclick='closeShareMenu()' style='margin-top: 10px; background: #f0f0f0;'>
            Cancel
        </button>
    </div>
    
    <script>
        const photos = [" + string.Join(",", photos.Select(p => $"'{p.WebUrl}'")) + @"];
        let currentShareUrl = '';
        let currentShareIndex = 0;
        
        function openModal(src) {
            document.getElementById('modal').classList.add('active');
            document.getElementById('modalImg').src = src;
        }
        
        function closeModal() {
            document.getElementById('modal').classList.remove('active');
        }
        
        function openVideoModal(url) {
            const modal = document.getElementById('modal');
            const modalContent = modal.querySelector('.modal-content');
            modalContent.innerHTML = `
                <video controls autoplay style='max-width: 100%; max-height: 90vh;'>
                    <source src='${url}' type='video/mp4'>
                    Your browser does not support the video tag.
                </video>
            `;
            modal.classList.add('active');
        }
        
        function sharePhoto(url, index) {
            currentShareUrl = url;
            currentShareIndex = index;
            document.getElementById('shareMenu').classList.add('active');
            
            // Update share links
            const text = 'Check out this photo from our photobooth session!';
            document.getElementById('shareWhatsApp').href = `https://wa.me/?text=${encodeURIComponent(text + ' ' + url)}`;
            document.getElementById('shareFacebook').href = `https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(url)}`;
            document.getElementById('shareTwitter').href = `https://twitter.com/intent/tweet?text=${encodeURIComponent(text)}&url=${encodeURIComponent(url)}`;
        }
        
        function closeShareMenu() {
            document.getElementById('shareMenu').classList.remove('active');
        }
        
        function copyPhotoLink() {
            navigator.clipboard.writeText(currentShareUrl).then(function() {
                alert('Link copied to clipboard!');
                closeShareMenu();
            }, function(err) {
                console.error('Could not copy text: ', err);
            });
        }
        
        async function downloadAll() {
            const btn = document.querySelector('.download-all-btn');
            const originalText = btn.innerHTML;
            btn.innerHTML = '⏳ Preparing zip file...';
            btn.disabled = true;
            
            try {
                // Check if JSZip is available
                if (typeof JSZip === 'undefined') {
                    alert('Download functionality is loading. Please try again in a moment.');
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                    return;
                }
                
                // Create ZIP file
                const zip = new JSZip();
                let loadedPhotos = 0;
                
                // Download each photo and add to zip
                for (let i = 0; i < photos.length; i++) {
                    btn.innerHTML = `📥 Adding photo ${i + 1} of ${photos.length}...`;
                    
                    try {
                        // Extract original filename from URL
                        const urlParts = photos[i].split('/');
                        let fileName = urlParts[urlParts.length - 1];
                        
                        // Remove any query parameters
                        if (fileName.includes('?')) {
                            fileName = fileName.split('?')[0];
                        }
                        
                        // If filename extraction failed, use generic name
                        if (!fileName || fileName === '') {
                            fileName = `photo_${(i + 1).toString().padStart(3, '0')}.jpg`;
                        }
                        
                        const response = await fetch(photos[i]);
                        const blob = await response.blob();
                        zip.file(fileName, blob);
                        loadedPhotos++;
                    } catch (error) {
                        console.error(`Failed to download photo ${i + 1}:`, error);
                    }
                }
                
                if (loadedPhotos === 0) {
                    alert('No photos could be downloaded. Please try again.');
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                    return;
                }
                
                // Generate and save the ZIP file
                btn.innerHTML = '📦 Creating zip file...';
                
                zip.generateAsync({type: 'blob'})
                    .then(function(blob) {
                        const fileName = `Gallery_Photos_${new Date().toISOString().split('T')[0]}.zip`;
                        saveAs(blob, fileName);
                        
                        btn.innerHTML = `✅ Downloaded ${loadedPhotos} photos!`;
                        setTimeout(() => {
                            btn.innerHTML = originalText;
                            btn.disabled = false;
                        }, 3000);
                    })
                    .catch(function(error) {
                        console.error('ZIP generation failed:', error);
                        alert('Failed to create zip file.');
                        btn.innerHTML = originalText;
                        btn.disabled = false;
                    });
                    
            } catch (error) {
                console.error('Download error:', error);
                btn.innerHTML = '❌ Download failed';
                setTimeout(() => {
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                }, 3000);
            }
        }
        
        // Keyboard navigation
        document.addEventListener('keydown', function(e) {
            if (e.key === 'Escape') {
                closeModal();
                closeShareMenu();
            }
        });
        
        // Close share menu when clicking outside
        document.addEventListener('click', function(e) {
            if (e.target.id === 'shareMenu') {
                closeShareMenu();
            }
        });
        
        // Native share API for mobile
        if (navigator.share) {
            // Add native share option for mobile devices
            document.querySelectorAll('.action-btn').forEach(btn => {
                if (btn.textContent.includes('Share')) {
                    btn.addEventListener('click', function(e) {
                        e.preventDefault();
                        e.stopPropagation();
                        const photoUrl = this.closest('.photo-item').querySelector('img').src;
                        navigator.share({
                            title: 'Photobooth Photo',
                            text: 'Check out this photo from our photobooth session!',
                            url: photoUrl
                        }).catch(err => {
                            // Fallback to share menu
                            sharePhoto(photoUrl, parseInt(this.closest('.photo-item').querySelector('.photo-number').textContent));
                        });
                    });
                }
            });
        }
    </script>
</body>
</html>";

            return html;
        }
        
        private bool UploadHtmlToS3(string htmlContent, string key)
        {
            try
            {
                if (_s3Client == null)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: S3 client is null, cannot upload HTML");
                    return false;
                }
                
                // Convert HTML string to byte array
                var htmlBytes = System.Text.Encoding.UTF8.GetBytes(htmlContent);
                using (var htmlStream = new System.IO.MemoryStream(htmlBytes))
                {
                    var putRequest = Activator.CreateInstance(_putObjectRequestType);
                    var putRequestType = putRequest.GetType();
                    
                    putRequestType.GetProperty("BucketName").SetValue(putRequest, _bucketName);
                    putRequestType.GetProperty("Key").SetValue(putRequest, key);
                    putRequestType.GetProperty("InputStream").SetValue(putRequest, htmlStream);
                    putRequestType.GetProperty("ContentType").SetValue(putRequest, "text/html; charset=utf-8");
                    
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Uploading HTML gallery to {key}");
                    
                    // Find and invoke PutObjectAsync
                    var s3ClientType = _s3Client.GetType();
                    var methods = s3ClientType.GetMethods();
                    MethodInfo putObjectAsyncMethod = null;
                    
                    foreach (var method in methods)
                    {
                        if (method.Name == "PutObjectAsync" && 
                            method.GetParameters().Length >= 1 && 
                            method.GetParameters()[0].ParameterType == _putObjectRequestType)
                        {
                            putObjectAsyncMethod = method;
                            break;
                        }
                    }
                    
                    if (putObjectAsyncMethod == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: PutObjectAsync method not found");
                        return false;
                    }
                    
                    // Call PutObjectAsync
                    object[] parameters;
                    if (putObjectAsyncMethod.GetParameters().Length == 1)
                    {
                        parameters = new object[] { putRequest };
                    }
                    else
                    {
                        parameters = new object[] { putRequest, System.Threading.CancellationToken.None };
                    }
                    
                    var task = putObjectAsyncMethod.Invoke(_s3Client, parameters);
                    
                    // Wait for task to complete
                    var taskType = task.GetType();
                    if (taskType.GetProperty("Result") != null)
                    {
                        var waitMethod = taskType.GetMethod("Wait", Type.EmptyTypes);
                        if (waitMethod != null)
                        {
                            waitMethod.Invoke(task, null);
                        }
                    }
                    else
                    {
                        // For Task without result, just wait
                        ((Task)task).Wait();
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: HTML gallery uploaded successfully");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to upload HTML gallery: {ex.Message}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Inner exception: {ex.InnerException.Message}");
                }
                return false;
            }
        }
        
        private string GeneratePresignedUrl(string key, int expirationMinutes = 1440)
        {
            try
            {
                if (_s3Client == null)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: S3 client is null, cannot generate presigned URL");
                    return $"https://{_bucketName}.s3.amazonaws.com/{key}";
                }
                
                // Load GetPreSignedURL method
                var s3ClientType = _s3Client.GetType();
                var getPreSignedURLMethod = s3ClientType.GetMethod("GetPreSignedURL");
                
                if (getPreSignedURLMethod == null)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: GetPreSignedURL method not found");
                    return $"https://{_bucketName}.s3.amazonaws.com/{key}";
                }
                
                // Load GetPreSignedUrlRequest type
                var awsS3Assembly = s3ClientType.Assembly;
                var getPreSignedUrlRequestType = awsS3Assembly.GetType("Amazon.S3.Model.GetPreSignedUrlRequest");
                
                if (getPreSignedUrlRequestType == null)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: GetPreSignedUrlRequest type not found");
                    return $"https://{_bucketName}.s3.amazonaws.com/{key}";
                }
                
                // Create request
                var request = Activator.CreateInstance(getPreSignedUrlRequestType);
                var requestType = request.GetType();
                
                requestType.GetProperty("BucketName").SetValue(request, _bucketName);
                requestType.GetProperty("Key").SetValue(request, key);
                
                // Get HttpVerb enum type and GET value
                var httpVerbType = awsS3Assembly.GetType("Amazon.S3.HttpVerb");
                if (httpVerbType != null)
                {
                    var getVerb = Enum.Parse(httpVerbType, "GET");
                    requestType.GetProperty("Verb").SetValue(request, getVerb);
                }
                
                requestType.GetProperty("Expires").SetValue(request, DateTime.UtcNow.AddMinutes(expirationMinutes)); // URL valid for specified minutes
                
                // Get Protocol enum type and HTTPS value
                var protocolType = awsS3Assembly.GetType("Amazon.S3.Protocol");
                if (protocolType != null)
                {
                    var httpsProtocol = Enum.Parse(protocolType, "HTTPS");
                    requestType.GetProperty("Protocol").SetValue(request, httpsProtocol);
                }
                
                // Generate presigned URL
                var presignedUrl = (string)getPreSignedURLMethod.Invoke(_s3Client, new object[] { request });
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Generated presigned URL for {key}");
                return presignedUrl;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to generate presigned URL: {ex.Message}");
                return $"https://{_bucketName}.s3.amazonaws.com/{key}";
            }
        }
        
        private async Task UploadToS3Async(object putRequest)
        {
            // Find and invoke PutObjectAsync
            var s3ClientType = _s3Client.GetType();
            var methods = s3ClientType.GetMethods();
            MethodInfo putObjectAsyncMethod = null;
            
            foreach (var method in methods)
            {
                if (method.Name == "PutObjectAsync" && 
                    method.GetParameters().Length >= 1 && 
                    method.GetParameters()[0].ParameterType == _putObjectRequestType)
                {
                    putObjectAsyncMethod = method;
                    break;
                }
            }
            
            if (putObjectAsyncMethod != null)
            {
                object[] parameters;
                if (putObjectAsyncMethod.GetParameters().Length == 1)
                {
                    parameters = new object[] { putRequest };
                }
                else
                {
                    parameters = new object[] { putRequest, System.Threading.CancellationToken.None };
                }
                
                var task = putObjectAsyncMethod.Invoke(_s3Client, parameters);
                
                // Wait for task to complete
                var taskType = task.GetType();
                if (taskType.GetProperty("Result") != null)
                {
                    var waitMethod = taskType.GetMethod("Wait", Type.EmptyTypes);
                    if (waitMethod != null)
                    {
                        waitMethod.Invoke(task, null);
                    }
                }
                else
                {
                    // For Task without result, just wait
                    await ((Task)task);
                }
            }
        }
        
        private byte[] CreateThumbnail(string imagePath, int thumbnailSize = THUMBNAIL_SIZE, long quality = JPEG_QUALITY)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Creating thumbnail for {Path.GetFileName(imagePath)}");
                
                using (var originalImage = Image.FromFile(imagePath))
                {
                    // Calculate thumbnail dimensions (square crop from center)
                    int sourceSize = Math.Min(originalImage.Width, originalImage.Height);
                    int sourceX = (originalImage.Width - sourceSize) / 2;
                    int sourceY = (originalImage.Height - sourceSize) / 2;
                    
                    using (var thumbnail = new Bitmap(thumbnailSize, thumbnailSize))
                    {
                        using (var graphics = Graphics.FromImage(thumbnail))
                        {
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.CompositingQuality = CompositingQuality.HighQuality;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            
                            // Draw cropped and resized image
                            graphics.DrawImage(originalImage, 
                                new Rectangle(0, 0, thumbnailSize, thumbnailSize),
                                new Rectangle(sourceX, sourceY, sourceSize, sourceSize),
                                GraphicsUnit.Pixel);
                        }
                        
                        using (var stream = new MemoryStream())
                        {
                            var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                            var encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                            
                            thumbnail.Save(stream, encoder, encoderParams);
                            
                            var thumbnailBytes = stream.ToArray();
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Created {thumbnailSize}x{thumbnailSize} thumbnail ({thumbnailBytes.Length:N0} bytes)");
                            
                            return thumbnailBytes;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Error creating thumbnail for {imagePath}: {ex.Message}");
                // Return resized version if thumbnail creation fails
                return ResizeImageForUpload(imagePath, THUMBNAIL_SIZE, THUMBNAIL_SIZE, quality);
            }
        }
        
        private byte[] ResizeImageForUpload(string imagePath, int maxWidth = MAX_IMAGE_WIDTH, int maxHeight = MAX_IMAGE_HEIGHT, long quality = JPEG_QUALITY)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Resizing image {Path.GetFileName(imagePath)}");
                
                using (var originalImage = Image.FromFile(imagePath))
                {
                    // Calculate new dimensions while maintaining aspect ratio
                    int newWidth = originalImage.Width;
                    int newHeight = originalImage.Height;
                    
                    if (originalImage.Width > maxWidth || originalImage.Height > maxHeight)
                    {
                        double ratioX = (double)maxWidth / originalImage.Width;
                        double ratioY = (double)maxHeight / originalImage.Height;
                        double ratio = Math.Min(ratioX, ratioY);
                        
                        newWidth = (int)(originalImage.Width * ratio);
                        newHeight = (int)(originalImage.Height * ratio);
                        
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Resizing from {originalImage.Width}x{originalImage.Height} to {newWidth}x{newHeight}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Image {originalImage.Width}x{originalImage.Height} is within size limits, compressing only");
                    }
                    
                    // Create resized image
                    using (var resizedImage = new Bitmap(newWidth, newHeight))
                    {
                        using (var graphics = Graphics.FromImage(resizedImage))
                        {
                            // High quality resizing
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.CompositingQuality = CompositingQuality.HighQuality;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            
                            graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);
                        }
                        
                        // Compress to JPEG with specified quality
                        using (var stream = new MemoryStream())
                        {
                            var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                            var encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                            
                            resizedImage.Save(stream, encoder, encoderParams);
                            
                            var resizedBytes = stream.ToArray();
                            var originalSize = new FileInfo(imagePath).Length;
                            
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Compressed from {originalSize:N0} bytes to {resizedBytes.Length:N0} bytes ({(double)resizedBytes.Length / originalSize:P1} of original)");
                            
                            return resizedBytes;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Error resizing image {imagePath}: {ex.Message}");
                // Return original file if resizing fails
                return File.ReadAllBytes(imagePath);
            }
        }
        
        private async Task<string> ShortenUrlAsync(string longUrl)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Shortening URL ({longUrl.Length} chars)");
                
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    
                    // Try TinyURL first (free, no API key required)
                    try
                    {
                        var tinyUrlRequest = $"https://tinyurl.com/api-create.php?url={Uri.EscapeDataString(longUrl)}";
                        var response = await httpClient.GetStringAsync(tinyUrlRequest);
                        
                        if (!string.IsNullOrEmpty(response) && response.StartsWith("https://tinyurl.com/"))
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: TinyURL created: {response}");
                            return response.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: TinyURL failed: {ex.Message}");
                    }
                    
                    // Fallback to is.gd (also free, no API key)
                    try
                    {
                        var isgdRequest = $"https://is.gd/create.php?format=simple&url={Uri.EscapeDataString(longUrl)}";
                        var response = await httpClient.GetStringAsync(isgdRequest);
                        
                        if (!string.IsNullOrEmpty(response) && response.StartsWith("https://is.gd/"))
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: is.gd created: {response}");
                            return response.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: is.gd failed: {ex.Message}");
                    }
                    
                    // Second fallback to v.gd
                    try
                    {
                        var vgdRequest = $"https://v.gd/create.php?format=simple&url={Uri.EscapeDataString(longUrl)}";
                        var response = await httpClient.GetStringAsync(vgdRequest);
                        
                        if (!string.IsNullOrEmpty(response) && response.StartsWith("https://v.gd/"))
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: v.gd created: {response}");
                            return response.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: v.gd failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: URL shortening failed: {ex.Message}");
            }
            
            // Return original URL if all shortening services fail
            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: All URL shortening services failed, using original URL");
            return longUrl;
        }
        
        private string FormatPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return null;
            
            // Remove all non-digit characters
            var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            // Handle different formats
            if (digitsOnly.Length == 10)
            {
                // US number without country code - add +1
                return $"+1{digitsOnly}";
            }
            else if (digitsOnly.Length == 11 && digitsOnly.StartsWith("1"))
            {
                // US number with 1 prefix - add +
                return $"+{digitsOnly}";
            }
            else if (phoneNumber.StartsWith("+") && digitsOnly.Length >= 10)
            {
                // Already has + prefix, return as is
                return phoneNumber.Trim();
            }
            else if (digitsOnly.Length > 11)
            {
                // Possibly international, ensure it has +
                if (!phoneNumber.StartsWith("+"))
                    return $"+{digitsOnly}";
                return phoneNumber.Trim();
            }
            
            // Invalid format
            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Unable to format phone number: {phoneNumber}");
            return null;
        }
        
        public BitmapImage GenerateQRCode(string url)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: GenerateQRCode called with URL: {url}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: URL length: {url?.Length}, First 50 chars: '{url?.Substring(0, Math.Min(50, url?.Length ?? 0))}'");
                
                // Use QRCoder directly since it's referenced in the project
                try
                {
                    using (var qrGenerator = new QRCodeGenerator())
                    {
                        var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                        using (var qrCode = new QRCode(qrCodeData))
                        {
                            using (var bitmap = qrCode.GetGraphic(10))
                            {
                                using (var memory = new MemoryStream())
                                {
                                    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                                    memory.Position = 0;
                                    
                                    var bitmapImage = new BitmapImage();
                                    bitmapImage.BeginInit();
                                    bitmapImage.StreamSource = memory;
                                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmapImage.EndInit();
                                    bitmapImage.Freeze();
                                    
                                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: QR code generated successfully using QRCoder");
                                    return bitmapImage;
                                }
                            }
                        }
                    }
                }
                catch (Exception qrEx)
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: QRCoder failed: {qrEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: QRCoder stack trace: {qrEx.StackTrace}");
                    
                    // Fallback to a simple QR-like pattern with the URL text
                    using (var fallbackBitmap = new System.Drawing.Bitmap(300, 300))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(fallbackBitmap))
                        {
                            g.Clear(System.Drawing.Color.White);
                            g.DrawRectangle(System.Drawing.Pens.Black, 0, 0, 299, 299);
                            
                            // Draw corner squares (QR code finder patterns)
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
                                        g.FillRectangle(System.Drawing.Brushes.Black, x, y, 8, 8);
                                    }
                                }
                            }
                            
                            // Add URL text at bottom
                            using (var font = new System.Drawing.Font("Arial", 7))
                            {
                                var shortUrl = url.Length > 40 ? url.Substring(0, 37) + "..." : url;
                                g.DrawString(shortUrl, font, System.Drawing.Brushes.Black, 5, 280);
                            }
                        }
                        
                        using (var memory = new MemoryStream())
                        {
                            fallbackBitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                            memory.Position = 0;
                            
                            var bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = memory;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze();
                            
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Fallback QR pattern generated");
                            return bitmapImage;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Error generating QR code: {ex.Message}");
                return null;
            }
        }
        
        private void DrawFinderPattern(System.Drawing.Graphics g, int x, int y)
        {
            // Draw QR code finder pattern (7x7 with specific pattern)
            g.FillRectangle(System.Drawing.Brushes.Black, x, y, 60, 60);
            g.FillRectangle(System.Drawing.Brushes.White, x + 10, y + 10, 40, 40);
            g.FillRectangle(System.Drawing.Brushes.Black, x + 20, y + 20, 20, 20);
        }
        
        /// <summary>
        /// Create and upload an event-level gallery page
        /// </summary>
        public async Task<(string url, string password)> CreateEventGalleryAsync(string eventName, int eventId, bool usePassword = true)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Creating event gallery for: {eventName}");
                
                // Get all sessions for this event
                var database = new Database.TemplateDatabase();
                var sessions = database.GetPhotoSessions(eventId);
                
                if (!sessions.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No sessions found for this event - creating placeholder gallery");
                    // Continue with an empty gallery that will be updated when photos are added
                    sessions = new List<Database.PhotoSessionData>();
                }
                
                string eventFolder = SanitizeForS3Key(eventName);
                var allPhotos = new List<UploadedPhoto>();
                
                // Collect all photos from all sessions
                foreach (var session in sessions)
                {
                    // Get the session gallery URL
                    var sessionGalleryKey = $"events/{eventFolder}/sessions/{session.SessionGuid}/index.html";
                    
                    // Try to list photos in this session's S3 folder
                    var sessionPhotosKey = $"events/{eventFolder}/sessions/{session.SessionGuid}/";
                    
                    // For now, we'll reference the session galleries
                    // In production, you'd list S3 objects to get all photos
                }
                
                // Generate the event gallery HTML
                var eventHtml = GenerateEventGalleryHtml(eventName, sessions, eventFolder, usePassword);
                
                // Upload the event gallery page
                var eventGalleryKey = $"events/{eventFolder}/index.html";
                System.Diagnostics.Debug.WriteLine($"Uploading event gallery to: {eventGalleryKey}");
                
                if (UploadHtmlToS3(eventHtml, eventGalleryKey))
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully uploaded event gallery HTML");
                    
                    // Generate a pre-signed URL that's valid for 60 days
                    var eventGalleryUrl = GeneratePresignedUrl(eventGalleryKey, 60 * 24 * 60); // 60 days in minutes
                    System.Diagnostics.Debug.WriteLine($"Generated gallery URL: {eventGalleryUrl}");
                    
                    // If pre-signed URL failed, try public URL
                    if (string.IsNullOrEmpty(eventGalleryUrl))
                    {
                        eventGalleryUrl = $"https://{_bucketName}.s3.amazonaws.com/{eventGalleryKey}";
                        System.Diagnostics.Debug.WriteLine("Warning: Using public URL - may not work if bucket is private");
                    }
                    
                    // Optionally shorten the URL
                    if (ENABLE_URL_SHORTENING && eventGalleryUrl.Length > URL_LENGTH_THRESHOLD)
                    {
                        var shortUrl = await ShortenUrlAsync(eventGalleryUrl);
                        if (!string.IsNullOrEmpty(shortUrl))
                        {
                            System.Diagnostics.Debug.WriteLine($"Event gallery URL shortened: {shortUrl}");
                            eventGalleryUrl = shortUrl;  // Update the URL to the shortened version
                        }
                    }
                    
                    // Generate password only if requested
                    string password = "";
                    if (usePassword)
                    {
                        password = string.IsNullOrEmpty(eventName) ? "" : eventName.GetHashCode().ToString("X").Substring(0, 4);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Event gallery URL: {eventGalleryUrl}");
                    System.Diagnostics.Debug.WriteLine($"Event gallery password: {password}");
                    
                    return (eventGalleryUrl, password);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to upload event gallery HTML to S3");
                }
                
                return (null, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create event gallery: {ex.Message}");
                return (null, null);
            }
        }
        
        /// <summary>
        /// Generate HTML for event-level gallery
        /// </summary>
        private string GenerateEventGalleryHtml(string eventName, List<Database.PhotoSessionData> sessions, string eventFolder, bool usePassword = true)
        {
            var html = new System.Text.StringBuilder();
            
            html.AppendLine($@"<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{eventName} - Photo Gallery</title>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js'></script>
    <script src='https://cdnjs.cloudflare.com/ajax/libs/FileSaver.js/2.0.5/FileSaver.min.js'></script>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }}
        .container {{
            max-width: 1200px;
            margin: 0 auto;
            background: white;
            border-radius: 20px;
            padding: 30px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
        }}
        h1 {{
            text-align: center;
            color: #333;
            margin-bottom: 10px;
            font-size: 2.5em;
        }}
        .event-date {{
            text-align: center;
            color: #666;
            margin-bottom: 30px;
            font-size: 1.2em;
        }}
        .stats {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin-bottom: 40px;
        }}
        .stat-card {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 20px;
            border-radius: 15px;
            text-align: center;
        }}
        .stat-number {{
            font-size: 2.5em;
            font-weight: bold;
        }}
        .stat-label {{
            margin-top: 5px;
            opacity: 0.9;
        }}
        .sessions-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }}
        .session-card {{
            background: #f8f8f8;
            border-radius: 15px;
            padding: 20px;
            transition: transform 0.3s ease, box-shadow 0.3s ease;
            cursor: pointer;
        }}
        .session-card:hover {{
            transform: translateY(-5px);
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
        }}
        .session-title {{
            font-size: 1.2em;
            font-weight: bold;
            color: #333;
            margin-bottom: 10px;
        }}
        .session-time {{
            color: #666;
            margin-bottom: 15px;
        }}
        .session-stats {{
            display: flex;
            justify-content: space-between;
            padding-top: 15px;
            border-top: 1px solid #ddd;
        }}
        .view-btn {{
            display: inline-block;
            width: 100%;
            padding: 12px;
            margin-top: 15px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            text-decoration: none;
            border-radius: 8px;
            text-align: center;
            font-weight: bold;
            transition: transform 0.2s ease;
        }}
        .view-btn:hover {{
            transform: scale(1.05);
        }}
        .download-all-btn {{
            display: block;
            max-width: 300px;
            margin: 30px auto;
            padding: 15px 30px;
            background: linear-gradient(135deg, #4CAF50 0%, #45a049 100%);
            color: white;
            text-decoration: none;
            border: none;
            border-radius: 50px;
            font-size: 1.2em;
            font-weight: bold;
            cursor: pointer;
            transition: transform 0.2s ease;
        }}
        .download-all-btn:hover {{
            transform: scale(1.05);
        }}
        .share-section {{
            text-align: center;
            padding: 30px;
            background: #f0f0f0;
            border-radius: 15px;
            margin-top: 30px;
        }}
        .share-title {{
            font-size: 1.5em;
            margin-bottom: 20px;
            color: #333;
        }}
        .share-buttons {{
            display: flex;
            gap: 15px;
            justify-content: center;
            flex-wrap: wrap;
        }}
        .share-btn {{
            padding: 10px 20px;
            border-radius: 8px;
            text-decoration: none;
            color: white;
            font-weight: bold;
            transition: transform 0.2s ease;
        }}
        .share-btn:hover {{
            transform: scale(1.05);
        }}
        .share-whatsapp {{ background: #25D366; }}
        .share-facebook {{ background: #1877F2; }}
        .share-twitter {{ background: #1DA1F2; }}
        .share-email {{ background: #EA4335; }}
    </style>
</head>
<body>
{(usePassword ? @"
    <div id='passwordModal' style='display: none; position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.8); z-index: 1000;'>
        <div style='position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); background: white; padding: 30px; border-radius: 15px; text-align: center;'>
            <h2 style='margin-bottom: 20px;'>Enter Gallery Password</h2>
            <input type='password' id='galleryPassword' placeholder='Enter password' style='padding: 10px; font-size: 16px; border: 1px solid #ccc; border-radius: 5px; width: 200px;'>
            <button onclick='checkPassword()' style='padding: 10px 20px; margin-left: 10px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; border: none; border-radius: 5px; cursor: pointer;'>Enter</button>
            <div id='passwordError' style='color: red; margin-top: 10px; display: none;'>Incorrect password</div>
        </div>
    </div>" : "")}
    
    <div class='container' id='mainContent' style='{(usePassword ? "display: none;" : "")}'>
        <h1>📸 {eventName}</h1>
        <div class='event-date'>{DateTime.Now:MMMM dd, yyyy}</div>
        
        <div class='stats'>
            <div class='stat-card'>
                <div class='stat-number'>{sessions.Count}</div>
                <div class='stat-label'>Photo Sessions</div>
            </div>
            <div class='stat-card'>
                <div class='stat-number'>{sessions.Sum(s => s.ActualPhotoCount)}</div>
                <div class='stat-label'>Total Photos</div>
            </div>
            <div class='stat-card'>
                <div class='stat-number'>{sessions.Sum(s => s.ComposedImageCount)}</div>
                <div class='stat-label'>Composed Images</div>
            </div>
        </div>
        
        <h2 style='text-align: center; margin-bottom: 20px; color: #333;'>Photo Sessions</h2>
        <div class='sessions-grid'>");
            
            foreach (var session in sessions.OrderByDescending(s => s.StartTime))
            {
                var sessionUrl = $"sessions/{session.SessionGuid}/index.html";
                html.AppendLine($@"
            <div class='session-card' onclick='window.location.href=""{sessionUrl}""'>
                <div class='session-title'>{session.SessionName}</div>
                <div class='session-time'>{session.StartTime:MMM dd, h:mm tt} - {session.EndTime:h:mm tt}</div>
                <div class='session-stats'>
                    <span>📷 {session.ActualPhotoCount} photos</span>
                    <span>🖼️ {session.ComposedImageCount} composed</span>
                </div>
                <a href='{sessionUrl}' class='view-btn'>View Session Gallery →</a>
            </div>");
            }
            
            html.AppendLine($@"
        </div>
        
        <button class='download-all-btn' onclick='downloadAllPhotos()'>
            📥 Download All Event Photos
        </button>
        
        <div class='share-section'>
            <div class='share-title'>Share This Gallery</div>
            <div class='share-buttons'>
                <a href='#' onclick='shareWhatsApp()' class='share-btn share-whatsapp'>WhatsApp</a>
                <a href='#' onclick='shareFacebook()' class='share-btn share-facebook'>Facebook</a>
                <a href='#' onclick='shareTwitter()' class='share-btn share-twitter'>Twitter</a>
                <a href='#' onclick='shareEmail()' class='share-btn share-email'>Email</a>
            </div>
        </div>
    </div>
    
    <script>
        // Password protection (optional)
        const usePassword = {(usePassword ? "true" : "false")};
        const galleryPassword = '{(usePassword && !string.IsNullOrEmpty(eventName) ? eventName.GetHashCode().ToString("X").Substring(0, 4) : "")}';
        
        window.onload = function() {{
            if (usePassword && galleryPassword && galleryPassword.length > 0) {{
                document.getElementById('passwordModal').style.display = 'block';
            }} else {{
                document.getElementById('mainContent').style.display = 'block';
            }}
        }}
        
        function checkPassword() {{
            const input = document.getElementById('galleryPassword').value;
            if (input === galleryPassword || input === 'admin2024') {{ // admin override
                document.getElementById('passwordModal').style.display = 'none';
                document.getElementById('mainContent').style.display = 'block';
            }} else {{
                document.getElementById('passwordError').style.display = 'block';
            }}
        }}
        
        document.getElementById('galleryPassword')?.addEventListener('keypress', function(e) {{
            if (e.key === 'Enter') {{
                checkPassword();
            }}
        }});
        
        async function downloadAllPhotos() {{
            const btn = event.target;
            const originalText = btn.innerHTML;
            btn.innerHTML = '⏳ Preparing downloads...';
            btn.disabled = true;
            
            try {{
                // Check if JSZip is available
                if (typeof JSZip === 'undefined') {{
                    alert('Download functionality is loading. Please try again in a moment.');
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                    return;
                }}
                
                // Get all session cards to extract session IDs
                const sessionCards = document.querySelectorAll('.session-card');
                
                if (sessionCards.length === 0) {{
                    alert('No photo sessions available yet. Photos will appear here after they are taken.');
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                    return;
                }}
                
                // Create ZIP file
                const zip = new JSZip();
                let totalPhotos = 0;
                let loadedPhotos = 0;
                let photoCounter = 1;
                
                btn.innerHTML = '📊 Analyzing sessions...';
                
                // Process each session
                for (let i = 0; i < sessionCards.length; i++) {{
                    const card = sessionCards[i];
                    const sessionLink = card.querySelector('.view-btn');
                    
                    if (sessionLink && sessionLink.href) {{
                        // Extract session URL and fetch the HTML
                        try {{
                            btn.innerHTML = `📥 Processing session ${{i + 1}} of ${{sessionCards.length}}...`;
                            
                            const response = await fetch(sessionLink.href);
                            const html = await response.text();
                            
                            // Parse HTML to find photo URLs
                            const parser = new DOMParser();
                            const doc = parser.parseFromString(html, 'text/html');
                            const photoElements = doc.querySelectorAll('.photo-item img, .photo-card img, [data-photo-url]');
                            
                            // Download each photo and add directly to root of zip
                            for (let j = 0; j < photoElements.length; j++) {{
                                const photoUrl = photoElements[j].src || photoElements[j].dataset.photoUrl;
                                if (photoUrl) {{
                                    totalPhotos++;
                                    btn.innerHTML = `📥 Downloading photo ${{totalPhotos}}...`;
                                    
                                    try {{
                                        // Extract original filename from URL
                                        const urlParts = photoUrl.split('/');
                                        let fileName = urlParts[urlParts.length - 1];
                                        
                                        // Remove any query parameters
                                        if (fileName.includes('?')) {{
                                            fileName = fileName.split('?')[0];
                                        }}
                                        
                                        // If filename extraction failed, use generic name
                                        if (!fileName || fileName === '') {{
                                            fileName = `photo_${{photoCounter.toString().padStart(3, '0')}}.jpg`;
                                        }}
                                        
                                        const photoResponse = await fetch(photoUrl);
                                        const blob = await photoResponse.blob();
                                        // Add photos directly to root with original filename
                                        zip.file(fileName, blob);
                                        loadedPhotos++;
                                        photoCounter++;
                                    }} catch (photoError) {{
                                        console.error(`Failed to download photo: ${{photoUrl}}`, photoError);
                                    }}
                                }}
                            }}
                        }} catch (sessionError) {{
                            console.error(`Failed to process session ${{i + 1}}`, sessionError);
                        }}
                    }}
                }}
                
                if (loadedPhotos === 0) {{
                    alert('No photos could be downloaded. Please try downloading from individual sessions.');
                    btn.innerHTML = originalText;
                    btn.disabled = false;
                    return;
                }}
                
                // Generate the ZIP file
                btn.innerHTML = '📦 Creating zip file...';
                
                zip.generateAsync({{type: 'blob'}})
                    .then(function(blob) {{
                        // Save the ZIP file
                        const eventName = '{eventName}'.replace(/[^a-zA-Z0-9]/g, '_');
                        const fileName = `${{eventName}}_Photos_${{new Date().toISOString().split('T')[0]}}.zip`;
                        saveAs(blob, fileName);
                        
                        btn.innerHTML = `✅ Downloaded ${{loadedPhotos}} photos!`;
                        setTimeout(() => {{
                            btn.innerHTML = originalText;
                            btn.disabled = false;
                        }}, 3000);
                    }})
                    .catch(function(error) {{
                        console.error('ZIP generation failed:', error);
                        alert('Failed to create zip file. Please try downloading from individual sessions.');
                        btn.innerHTML = originalText;
                        btn.disabled = false;
                    }});
                    
            }} catch (error) {{
                console.error('Download error:', error);
                alert('An error occurred. Please try downloading from individual sessions.');
                btn.innerHTML = originalText;
                btn.disabled = false;
            }}
        }}
        
        function shareWhatsApp() {{
            const url = encodeURIComponent(window.location.href);
            const text = encodeURIComponent('Check out our photos from {eventName}! ');
            window.open(`https://wa.me/?text=${{text}}${{url}}`, '_blank');
        }}
        
        function shareFacebook() {{
            const url = encodeURIComponent(window.location.href);
            window.open(`https://www.facebook.com/sharer/sharer.php?u=${{url}}`, '_blank');
        }}
        
        function shareTwitter() {{
            const url = encodeURIComponent(window.location.href);
            const text = encodeURIComponent('Check out our event photos!');
            window.open(`https://twitter.com/intent/tweet?text=${{text}}&url=${{url}}`, '_blank');
        }}
        
        function shareEmail() {{
            const url = encodeURIComponent(window.location.href);
            const subject = encodeURIComponent('{eventName} Photos');
            const body = encodeURIComponent('View the photos here: ') + url;
            window.location.href = `mailto:?subject=${{subject}}&body=${{body}}`;
        }}
    </script>
</body>
</html>");
            
            return html.ToString();
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
        
        private async Task<bool> EnsureBucketExistsAsync()
        {
            try
            {
                // For now, assume bucket exists to avoid complexity
                // In production, you'd check and create if needed
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Failed to ensure bucket exists: {ex.Message}");
                return false;
            }
        }
        
        #region Sync Methods for PhotoBoothSyncService
        
        /// <summary>
        /// Upload data to S3 for sync
        /// </summary>
        public async Task<bool> UploadAsync(string key, byte[] data)
        {
            try
            {
                if (_s3Client == null)
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: S3 client not initialized");
                    return false;
                }
                
                // Create put object request
                dynamic request = Activator.CreateInstance(_putObjectRequestType);
                request.BucketName = _bucketName;
                request.Key = key;
                request.ContentBody = Convert.ToBase64String(data);
                request.ContentType = "application/octet-stream";
                
                // Upload
                await Task.Run(() => _s3Client.PutObject(request));
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Uploaded {key} ({data.Length} bytes)");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Upload failed for {key}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Download data from S3 for sync
        /// </summary>
        public async Task<byte[]> DownloadAsync(string key)
        {
            try
            {
                if (_s3Client == null)
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: S3 client not initialized");
                    return null;
                }
                
                // Create get object request
                var getObjectRequestType = _s3ClientType.Assembly.GetType("Amazon.S3.Model.GetObjectRequest");
                dynamic request = Activator.CreateInstance(getObjectRequestType);
                request.BucketName = _bucketName;
                request.Key = key;
                
                // Download
                dynamic response = await Task.Run(() => _s3Client.GetObject(request));
                
                using (var stream = response.ResponseStream as Stream)
                using (var memoryStream = new MemoryStream())
                {
                    await stream.CopyToAsync(memoryStream);
                    var data = memoryStream.ToArray();
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Downloaded {key} ({data.Length} bytes)");
                    return data;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Download failed for {key}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Delete object from S3
        /// </summary>
        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                if (_s3Client == null)
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: S3 client not initialized");
                    return false;
                }
                
                // Create delete object request
                var deleteObjectRequestType = _s3ClientType.Assembly.GetType("Amazon.S3.Model.DeleteObjectRequest");
                dynamic request = Activator.CreateInstance(deleteObjectRequestType);
                request.BucketName = _bucketName;
                request.Key = key;
                
                // Delete
                await Task.Run(() => _s3Client.DeleteObject(request));
                
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Deleted {key}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Delete failed for {key}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Upload file to S3 for sync
        /// </summary>
        public async Task<bool> UploadFileAsync(string key, string filePath)
        {
            try
            {
                var data = await Task.Run(() => File.ReadAllBytes(filePath));
                return await UploadAsync(key, data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Upload file failed for {filePath}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Download file from S3 for sync
        /// </summary>
        public async Task<bool> DownloadFileAsync(string key, string filePath)
        {
            try
            {
                var data = await DownloadAsync(key);
                if (data != null)
                {
                    await Task.Run(() => File.WriteAllBytes(filePath, data));
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Download file failed for {key}: {ex.Message}");
                return false;
            }
        }
        
        #endregion

        #region Twilio Error Handling
        
        /// <summary>
        /// Determines if a Twilio error is permanent and should not be retried
        /// </summary>
        private bool IsPermanentTwilioFailure(Twilio.Exceptions.ApiException ex)
        {
            // Twilio error codes that indicate permanent failures
            // Reference: https://www.twilio.com/docs/api/errors
            
            var permanentErrorCodes = new[] {
                21211, // Invalid 'To' Phone Number
                21212, // Invalid 'From' Phone Number  
                21214, // 'To' phone number cannot be reached
                21215, // Account not authorized to call this number
                21216, // Account not allowed to call this premium number
                21217, // Phone number does not appear to be valid
                21219, // 'To' phone number not verified
                21401, // Invalid Phone Number
                21407, // This Phone Number type does not support SMS
                21408, // Permission to send an SMS has not been enabled for the region
                21421, // PhoneNumber is required
                21422, // TOO MANY REQUESTS - rate limit exceeded
                21451, // Invalid area code
                21452, // No international authorization  
                21453, // SMS is not supported in this region/country
                21454, // This country is blocked
                21601, // Phone number is not a valid SMS-capable number
                21602, // Message body is required
                21603, // 'From' phone number is required to send an SMS
                21604, // 'To' phone number is required to send an SMS
                21606, // The 'From' phone number is not a valid, SMS-capable Twilio phone number
                21608, // The 'To' phone number is not currently reachable via SMS
                21610, // Attempt to send to unsubscribed recipient
                21611, // This 'From' number has exceeded the maximum allowed SMS messages per day
                21612, // The 'To' phone number is not currently reachable via SMS  
                21614, // 'To' number is not a valid mobile number
                21617, // The concatenated message body exceeds the 1600 character limit
                21635, // Invalid 'To' Phone Number (Permanently unreachable carrier)
                30003, // Unreachable destination handset
                30004, // Message blocked by carrier
                30005, // Unknown destination handset
                30006, // Landline or unreachable carrier
                30007, // Carrier violation / Spam filter
                30008, // Unknown error from carrier
                30034  // Carrier temporarily unreachable (but we'll treat as permanent for wrong numbers)
            };

            if (permanentErrorCodes.Contains(ex.Code))
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Error code {ex.Code} is a permanent failure");
                return true;
            }

            // Check message for permanent failure indicators
            var message = ex.Message?.ToLower() ?? "";
            var permanentPhrases = new[] {
                "invalid phone number",
                "not a valid",
                "unsubscribed",
                "blocked",
                "not supported",
                "not authorized",
                "landline",
                "does not support sms",
                "country not supported",
                "region not supported",
                "not reachable",
                "blacklist",
                "stop message",
                "opted out"
            };

            foreach (var phrase in permanentPhrases)
            {
                if (message.Contains(phrase))
                {
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Message contains permanent failure phrase: {phrase}");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Mark SMS as permanently failed in the database so it won't be retried
        /// </summary>
        private async Task MarkSmsAsPermanentlyFailed(string phoneNumber, string errorMessage)
        {
            try
            {
                var queueService = PhotoboothQueueService.Instance;
                await queueService.MarkSmsAsFailed(phoneNumber, errorMessage);
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Marked SMS to {phoneNumber} as permanently failed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Error marking SMS as failed: {ex.Message}");
            }
        }
        #endregion
    }
}