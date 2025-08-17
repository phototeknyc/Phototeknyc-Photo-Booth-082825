using System;
using System.Threading.Tasks;
using System.Windows;

namespace Photobooth.Services
{
    /// <summary>
    /// Extension methods for easy integration of HostGalleryService
    /// </summary>
    public static class HostGalleryExtensions
    {
        private static HostGalleryService _hostGalleryService;
        
        /// <summary>
        /// Get or create the host gallery service instance
        /// </summary>
        private static HostGalleryService GetService()
        {
            if (_hostGalleryService == null)
            {
                _hostGalleryService = new HostGalleryService();
            }
            return _hostGalleryService;
        }
        
        /// <summary>
        /// Open the master gallery in browser
        /// </summary>
        public static async Task OpenMasterGallery()
        {
            try
            {
                var service = GetService();
                var filePath = await service.GenerateAndSaveLocalGallery();
                System.Diagnostics.Debug.WriteLine($"Master gallery opened: {filePath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open master gallery: {ex.Message}", 
                    "Gallery Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Open S3 bucket in browser
        /// </summary>
        public static void OpenS3Console()
        {
            try
            {
                var service = GetService();
                service.OpenS3BucketInBrowser();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open S3 console: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Get gallery URL for current event
        /// </summary>
        public static string GetEventGalleryUrl(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
                return null;
                
            var service = GetService();
            return service.GetEventGalleryUrl(eventName);
        }
        
        /// <summary>
        /// Generate customer gallery for an event
        /// </summary>
        public static async Task<string> GenerateCustomerGallery(string eventName, int eventId)
        {
            try
            {
                // Get the cloud share service
                var shareService = CloudShareProvider.GetShareService();
                
                // Check if it's the runtime service that supports event galleries
                if (shareService is CloudShareServiceRuntime runtimeService)
                {
                    var (galleryUrl, password) = await runtimeService.CreateEventGalleryAsync(eventName, eventId);
                    
                    if (!string.IsNullOrEmpty(galleryUrl))
                    {
                        System.Diagnostics.Debug.WriteLine($"Customer gallery created: {galleryUrl}");
                        
                        // Create shareable text with password
                        string shareText = $"{galleryUrl}\nPassword: {password}";
                        
                        // Copy to clipboard
                        System.Windows.Clipboard.SetText(shareText);
                        
                        MessageBox.Show($"Customer gallery created successfully!\n\nURL: {galleryUrl}\nPassword: {password}\n\nThe link and password have been copied to your clipboard.\n\nShare both with your customers.", 
                            "Gallery Created", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                        return galleryUrl;
                    }
                    else
                    {
                        MessageBox.Show("Failed to create customer gallery. Please check your AWS credentials and try again.", 
                            "Gallery Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("Customer gallery generation requires AWS cloud service to be configured.", 
                        "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate customer gallery: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            return null;
        }
    }
}