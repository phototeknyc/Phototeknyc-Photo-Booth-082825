using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Photobooth.Services
{
    /// <summary>
    /// Simplified cloud service interface for the photobooth app
    /// </summary>
    public class CloudService
    {
        private readonly IShareService _shareService;
        private readonly SessionManager _sessionManager;
        
        public CloudService()
        {
            _shareService = CloudShareProvider.GetShareService();
            _sessionManager = new SessionManager();
        }
        
        /// <summary>
        /// Check if cloud sharing is enabled
        /// </summary>
        public bool IsCloudEnabled()
        {
            var cloudEnabled = Environment.GetEnvironmentVariable("CLOUD_SHARING_ENABLED");
            return cloudEnabled == "True";
        }
        
        /// <summary>
        /// Share current session photos
        /// </summary>
        public async Task<CloudShareResult> ShareSessionAsync(string sessionId, string phoneNumber = null)
        {
            if (!IsCloudEnabled())
            {
                return new CloudShareResult 
                { 
                    Success = false, 
                    Message = "Cloud sharing is not enabled" 
                };
            }
            
            var session = _sessionManager.GetSession(sessionId);
            if (session == null)
            {
                return new CloudShareResult 
                { 
                    Success = false, 
                    Message = "Session not found" 
                };
            }
            
            var result = await _sessionManager.ShareSessionAsync(session, phoneNumber);
            
            return new CloudShareResult
            {
                Success = result.Success,
                Message = result.ErrorMessage ?? "Shared successfully",
                GalleryUrl = result.GalleryUrl,
                QRCodeImage = result.QRCodeImage
            };
        }
        
        /// <summary>
        /// Get QR code for session
        /// </summary>
        public BitmapImage GetSessionQRCode(string sessionId)
        {
            var session = _sessionManager.GetSession(sessionId);
            if (session?.ShareInfo?.QRCodeImagePath != null && File.Exists(session.ShareInfo.QRCodeImagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(session.ShareInfo.QRCodeImagePath);
                    bitmap.EndInit();
                    return bitmap;
                }
                catch { }
            }
            
            // Generate placeholder
            return _shareService.GenerateQRCode($"Session: {sessionId}");
        }
    }
    
    public class CloudShareResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string GalleryUrl { get; set; }
        public BitmapImage QRCodeImage { get; set; }
    }
}