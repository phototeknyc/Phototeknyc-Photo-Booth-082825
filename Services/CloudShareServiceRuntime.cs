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
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime: No AWS credentials found in User environment variables");
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS_ACCESS_KEY_ID = '{accessKey}'");
                    System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: AWS_SECRET_ACCESS_KEY = '{secretKey}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Constructor exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Stack trace: {ex.StackTrace}");
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
                
                // Upload each photo
                foreach (var photoPath in photoPaths)
                {
                    if (!File.Exists(photoPath))
                        continue;
                    
                    try
                    {
                        // Generate S3 key
                        var key = $"sessions/{sessionId}/{Path.GetFileName(photoPath)}";
                        System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: Uploading {Path.GetFileName(photoPath)} with key: {key}");
                        
                        // Check if types are initialized
                        if (_putObjectRequestType == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"CloudShareServiceRuntime: PutObjectRequest type is null, skipping upload");
                            continue;
                        }
                        
                        // Resize image for upload
                        var resizedImageBytes = ResizeImageForUpload(photoPath);
                        
                        // Create thumbnail
                        var thumbnailBytes = CreateThumbnail(photoPath);
                        var thumbnailKey = $"sessions/{sessionId}/thumbs/{Path.GetFileName(photoPath)}";
                        
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
                    string galleryKey = $"sessions/{sessionId}/index.html";
                    
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
                    var messageBody = $"Your photos are ready! View and download them here: {galleryUrl}";
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
        
        function downloadAll() {
            let delay = 0;
            photos.forEach((url, index) => {
                setTimeout(() => {
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `photo_${index + 1}.jpg`;
                    document.body.appendChild(link);
                    link.click();
                    document.body.removeChild(link);
                }, delay);
                delay += 300; // Small delay between downloads to avoid browser blocking
            });
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
        
        private string GeneratePresignedUrl(string key)
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
                
                requestType.GetProperty("Expires").SetValue(request, DateTime.UtcNow.AddHours(24)); // URL valid for 24 hours
                
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
    }
}