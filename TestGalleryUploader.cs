using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Photobooth.Services;

namespace Photobooth
{
    public class TestGalleryUploader
    {
        public static async Task<string> UploadTestGallery()
        {
            try
            {
                Console.WriteLine("Starting test gallery upload...");
                
                // Get the cloud share service
                var shareService = CloudShareProvider.GetShareService();
                
                if (shareService is CloudShareServiceRuntime runtimeService)
                {
                    // Create a simple test event gallery
                    var (galleryUrl, password) = await runtimeService.CreateEventGalleryAsync("Test Event", 1);
                    
                    if (!string.IsNullOrEmpty(galleryUrl))
                    {
                        Console.WriteLine($"✅ Test gallery uploaded successfully!");
                        Console.WriteLine($"URL: {galleryUrl}");
                        Console.WriteLine($"Password: {password}");
                        Console.WriteLine("\nTry accessing the URL in your browser.");
                        return galleryUrl;
                    }
                    else
                    {
                        Console.WriteLine("❌ Failed to upload test gallery");
                        return null;
                    }
                }
                else if (shareService != null)
                {
                    Console.WriteLine("Using fallback share service - limited functionality");
                    Console.WriteLine("❌ CloudShareServiceRuntime is required for full gallery creation");
                    return null;
                }
                else
                {
                    Console.WriteLine("❌ No valid share service available. Check AWS configuration.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                return null;
            }
        }
        
        public static async Task<bool> UploadSimpleTestFile()
        {
            try
            {
                Console.WriteLine("Uploading simple test HTML file...");
                
                // Create simple HTML content
                string testHtml = @"<!DOCTYPE html>
<html>
<head>
    <title>S3 Test Page</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }
        .container {
            text-align: center;
            padding: 40px;
            background: rgba(255,255,255,0.1);
            border-radius: 20px;
        }
        h1 { font-size: 3em; margin-bottom: 20px; }
        p { font-size: 1.5em; }
        .success { color: #4CAF50; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>✅ S3 Access Working!</h1>
        <p>If you can see this page, your S3 bucket permissions are configured correctly.</p>
        <p class='success'>The events folder is publicly accessible.</p>
        <p>Time: <script>document.write(new Date().toLocaleString());</script></p>
    </div>
</body>
</html>";

                // Get the service and upload
                var shareService = CloudShareProvider.GetShareService();
                
                if (shareService is CloudShareServiceRuntime runtimeService)
                {
                    // Use reflection to access the private UploadHtmlToS3 method
                    var type = runtimeService.GetType();
                    var method = type.GetMethod("UploadHtmlToS3", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (method != null)
                    {
                        string testKey = "events/test/test-access.html";
                        bool uploadResult = (bool)method.Invoke(runtimeService, new object[] { testHtml, testKey });
                        
                        if (uploadResult)
                        {
                            string testUrl = $"https://phototeknyc.s3.amazonaws.com/{testKey}";
                            Console.WriteLine($"✅ Test file uploaded successfully!");
                            Console.WriteLine($"URL: {testUrl}");
                            Console.WriteLine("\nTry accessing this URL in your browser.");
                            
                            // Try to open in browser
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = testUrl,
                                UseShellExecute = true
                            });
                            
                            return true;
                        }
                    }
                }
                
                Console.WriteLine("❌ Failed to upload test file");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error uploading test file: {ex.Message}");
                return false;
            }
        }
    }
}