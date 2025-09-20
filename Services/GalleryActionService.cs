using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CameraControl.Devices;
using Photobooth.Models;

namespace Photobooth.Services
{
    /// <summary>
    /// Service that handles unified actions for both current sessions and gallery sessions
    /// Provides context-aware coordination between UI and business services
    /// </summary>
    public class GalleryActionService
    {
        #region Dependencies
        private readonly SessionManager _sessionManager;
        private readonly PrintingService _printingService;
        private readonly PhotoboothSessionService _sessionService;
        private readonly EventGalleryService _galleryService;
        private readonly PhotoboothUIService _uiService;
        private readonly IShareService _shareService;
        private readonly SharingUIService _sharingUIService;
        private readonly Database.TemplateDatabase _database;
        private readonly PhotoboothQueueService _queueService;
        #endregion

        #region Events
        public event EventHandler<ActionCompletedEventArgs> ActionCompleted;
        public event EventHandler<ActionErrorEventArgs> ActionError;
        #endregion

        public GalleryActionService(
            SessionManager sessionManager,
            PrintingService printingService,
            PhotoboothSessionService sessionService,
            EventGalleryService galleryService,
            PhotoboothUIService uiService,
            IShareService shareService,
            SharingUIService sharingUIService = null,
            Database.TemplateDatabase database = null)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _printingService = printingService ?? throw new ArgumentNullException(nameof(printingService));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _galleryService = galleryService ?? throw new ArgumentNullException(nameof(galleryService));
            _uiService = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _shareService = shareService ?? throw new ArgumentNullException(nameof(shareService));
            _sharingUIService = sharingUIService; // Optional - will be set later if needed
            _database = database ?? new Database.TemplateDatabase(); // Create if not provided
            _queueService = PhotoboothQueueService.Instance; // Get singleton instance
        }
        
        /// <summary>
        /// Set the sharing UI service (used when service is created before UI service)
        /// </summary>
        public void SetSharingUIService(SharingUIService sharingUIService)
        {
            // _sharingUIService = sharingUIService; // Commented out - constructor parameter is better
        }
        
        /// <summary>
        /// Send SMS with the provided phone number using queue service
        /// </summary>
        public async Task<bool> SendSMSAsync(string phoneNumber, bool isGalleryMode = false, object gallerySession = null)
        {
            try
            {
                Log.Debug($"GalleryActionService.SendSMSAsync: Called with phone number: {phoneNumber}, isGallery: {isGalleryMode}");
                
                if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length < 5)
                {
                    _uiService.UpdateStatus("Please enter a valid phone number");
                    Log.Debug($"GalleryActionService.SendSMSAsync: Invalid phone number length: {phoneNumber?.Length ?? 0}");
                    return false;
                }

                // Determine session context
                string sessionId = null;
                
                if (isGalleryMode && gallerySession != null)
                {
                    sessionId = ((dynamic)gallerySession)?.SessionFolder?.ToString();
                }
                else
                {
                    sessionId = _sessionService?.CurrentSessionId;
                }

                if (string.IsNullOrEmpty(sessionId))
                {
                    _uiService.UpdateStatus("No active session found");
                    return false;
                }

                // Use queue service to handle SMS sending
                var result = await _queueService.QueueSmsAsync(sessionId, phoneNumber, isGalleryMode);
                
                if (result.Success)
                {
                    if (result.SentImmediately)
                    {
                        _uiService.UpdateStatus($"SMS sent successfully to {phoneNumber}");
                        Log.Debug($"GalleryActionService: SMS sent immediately to {phoneNumber}");
                    }
                    else
                    {
                        _uiService.UpdateStatus($"SMS queued for {phoneNumber} - will send when photos are uploaded");
                        Log.Debug($"GalleryActionService: SMS queued for {phoneNumber}");
                    }
                    return true;
                }
                else
                {
                    _uiService.UpdateStatus(result.Message);
                    Log.Error($"GalleryActionService: SMS queue failed - {result.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GalleryActionService: SMS send error: {ex.Message}");
                _uiService.UpdateStatus("SMS sending failed");
                return false;
            }
        }

        /// <summary>
        /// Share session (works for both current and gallery sessions)
        /// </summary>
        public async Task<bool> ShareSessionAsync(bool isGalleryMode, object gallerySession = null)
        {
            try
            {
                if (isGalleryMode && gallerySession != null)
                {
                    return await ShareGallerySessionAsync(gallerySession);
                }
                else
                {
                    return await ShareCurrentSessionAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GalleryActionService: Share error: {ex.Message}");
                ActionError?.Invoke(this, new ActionErrorEventArgs { Action = "Share", Error = ex });
                return false;
            }
        }

        /// <summary>
        /// Print session (works for both current and gallery sessions)
        /// </summary>
        public async Task<bool> PrintSessionAsync(bool isGalleryMode, object gallerySession = null)
        {
            try
            {
                if (isGalleryMode && gallerySession != null)
                {
                    return await PrintGallerySessionAsync(gallerySession);
                }
                else
                {
                    return await PrintCurrentSessionAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GalleryActionService: Print error: {ex.Message}");
                ActionError?.Invoke(this, new ActionErrorEventArgs { Action = "Print", Error = ex });
                return false;
            }
        }

        /// <summary>
        /// Email session (works for both current and gallery sessions)
        /// </summary>
        public async Task<bool> EmailSessionAsync(bool isGalleryMode, object gallerySession = null)
        {
            try
            {
                if (isGalleryMode && gallerySession != null)
                {
                    return await EmailGallerySessionAsync(gallerySession);
                }
                else
                {
                    return await EmailCurrentSessionAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GalleryActionService: Email error: {ex.Message}");
                ActionError?.Invoke(this, new ActionErrorEventArgs { Action = "Email", Error = ex });
                return false;
            }
        }

        /// <summary>
        /// SMS session (works for both current and gallery sessions)
        /// </summary>
        public async Task<bool> SmsSessionAsync(bool isGalleryMode, object gallerySession = null)
        {
            try
            {
                if (isGalleryMode && gallerySession != null)
                {
                    return await SmsGallerySessionAsync(gallerySession);
                }
                else
                {
                    return await SmsCurrentSessionAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GalleryActionService: SMS error: {ex.Message}");
                ActionError?.Invoke(this, new ActionErrorEventArgs { Action = "SMS", Error = ex });
                return false;
            }
        }

        /// <summary>
        /// Generate QR code (works for both current and gallery sessions)
        /// </summary>
        public async Task<bool> GenerateQRCodeAsync(bool isGalleryMode, object gallerySession = null)
        {
            try
            {
                if (isGalleryMode && gallerySession != null)
                {
                    return await GenerateGalleryQRCodeAsync(gallerySession);
                }
                else
                {
                    return await GenerateCurrentQRCodeAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GalleryActionService: QR Code error: {ex.Message}");
                ActionError?.Invoke(this, new ActionErrorEventArgs { Action = "QRCode", Error = ex });
                return false;
            }
        }

        #region Private Implementation Methods

        private async Task<bool> ShareCurrentSessionAsync()
        {
            _uiService.UpdateStatus("Sharing session...");
            
            // Get ALL session files including composed images and GIF/MP4
            var allFiles = _sessionService.AllSessionFiles;
            var sessionId = _sessionService.CurrentSessionId;
            
            Log.Debug($"GalleryActionService: Sharing {allFiles.Count} files for session {sessionId}");
            
            if (allFiles != null && allFiles.Count > 0 && !string.IsNullOrEmpty(sessionId))
            {
                var shareResult = await _shareService.CreateShareableGalleryAsync(sessionId, allFiles);
                
                if (shareResult != null && shareResult.Success)
                {
                    _uiService.UpdateStatus($"Session shared! URL: {shareResult.GalleryUrl}");
                    
                    // Generate and show QR code
                    if (!string.IsNullOrEmpty(shareResult.GalleryUrl))
                    {
                        var qrCodeImage = _shareService.GenerateQRCode(shareResult.GalleryUrl);
                        if (qrCodeImage != null)
                        {
                            Log.Debug("QR code generated for current session");
                        }
                    }
                    
                    // Store the share result for QR code and SMS functionality
                    _lastShareResult = shareResult;
                    
                    ActionCompleted?.Invoke(this, new ActionCompletedEventArgs 
                    { 
                        Action = "Share", 
                        Success = true, 
                        Result = shareResult 
                    });
                    
                    return true;
                }
                else
                {
                    _uiService.UpdateStatus("Failed to share session");
                    return false;
                }
            }
            
            _uiService.UpdateStatus("No session to share");
            return false;
        }

        private async Task<bool> ShareGallerySessionAsync(dynamic gallerySession)
        {
            _uiService.UpdateStatus("Sharing gallery session...");
            Log.Debug($"Sharing gallery session: {gallerySession?.SessionFolder}");

            if (gallerySession?.Photos != null)
            {
                // Extract photo paths from gallery session (matches working page code)
                var photoPaths = new List<string>();
                try
                {
                    foreach (var photo in gallerySession.Photos)
                    {
                        if (photo?.FilePath != null && File.Exists(photo.FilePath))
                        {
                            photoPaths.Add(photo.FilePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error extracting photo paths from gallery session: {ex.Message}");
                    return false;
                }
                
                if (photoPaths.Count > 0)
                {
                    // Use cloud sharing service to create shareable gallery
                    string sessionId = gallerySession.SessionFolder?.ToString() ?? Guid.NewGuid().ToString();
                    var shareResult = await _shareService.CreateShareableGalleryAsync(sessionId, photoPaths);
                    
                    if (shareResult != null && shareResult.Success)
                    {
                        _uiService.UpdateStatus($"Gallery shared! URL: {shareResult.GalleryUrl}");
                        
                        // Generate and show QR code
                        if (!string.IsNullOrEmpty(shareResult.GalleryUrl))
                        {
                            var qrCodeImage = _shareService.GenerateQRCode(shareResult.GalleryUrl);
                            if (qrCodeImage != null)
                            {
                                // Store QR code for display (could be passed to UI service)
                                Log.Debug("QR code generated for gallery session");
                            }
                        }
                        
                        // Store the share result for QR code and SMS functionality
                        _lastShareResult = shareResult;
                        
                        ActionCompleted?.Invoke(this, new ActionCompletedEventArgs 
                        { 
                            Action = "ShareGallery", 
                            Success = true, 
                            Result = shareResult 
                        });
                        
                        return true;
                    }
                    else
                    {
                        _uiService.UpdateStatus("Failed to share gallery session");
                        return false;
                    }
                }
            }
            
            _uiService.UpdateStatus("No photos in gallery session to share");
            return false;
        }

        private async Task<bool> PrintCurrentSessionAsync()
        {
            _uiService.UpdateStatus("Printing...");
            
            var currentSession = _sessionService.CurrentSessionId;
            if (!string.IsNullOrEmpty(currentSession))
            {
                var lastPhoto = _sessionService.CapturedPhotoPaths?.LastOrDefault();
                if (!string.IsNullOrEmpty(lastPhoto))
                {
                    bool success = await _printingService.PrintImageAsync(lastPhoto, currentSession);
                    _uiService.UpdateStatus(success ? "Sent to printer" : "Print failed");
                    ActionCompleted?.Invoke(this, new ActionCompletedEventArgs { Action = "Print", Success = success });
                    return success;
                }
                else
                {
                    _uiService.UpdateStatus("No photo to print");
                }
            }
            else
            {
                _uiService.UpdateStatus("No session to print");
            }
            return false;
        }

        private async Task<bool> PrintGallerySessionAsync(dynamic gallerySession)
        {
            _uiService.UpdateStatus("Printing gallery session...");
            Log.Debug($"Printing gallery session: {gallerySession?.SessionFolder}");
            
            if (gallerySession?.Photos != null)
            {
                try
                {
                    // Find the last photo with a valid file path
                    string lastPhoto = null;
                    foreach (var photo in gallerySession.Photos)
                    {
                        if (photo?.FilePath != null && File.Exists(photo.FilePath))
                        {
                            lastPhoto = photo.FilePath;
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(lastPhoto))
                    {
                        string sessionId = gallerySession.SessionFolder?.ToString() ?? "gallery";
                        bool success = await _printingService.PrintImageAsync(lastPhoto, sessionId);
                        _uiService.UpdateStatus(success ? "Gallery photo sent to printer" : "Gallery print failed");
                        ActionCompleted?.Invoke(this, new ActionCompletedEventArgs { Action = "PrintGallery", Success = success });
                        return success;
                    }
                    else
                    {
                        _uiService.UpdateStatus("Gallery photo not found");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error printing gallery session: {ex.Message}");
                    _uiService.UpdateStatus("Gallery print failed");
                    return false;
                }
            }
            else
            {
                _uiService.UpdateStatus("No photos in gallery session to print");
            }
            return false;
        }

        private async Task<bool> EmailCurrentSessionAsync()
        {
            // Email functionality would require additional email service integration
            _uiService.UpdateStatus("Email not available - use Share or SMS instead");
            ActionCompleted?.Invoke(this, new ActionCompletedEventArgs { Action = "Email", Success = false });
            return false;
        }

        private async Task<bool> EmailGallerySessionAsync(dynamic gallerySession)
        {
            // Email functionality would require additional email service integration
            _uiService.UpdateStatus("Email not available - use Share or SMS instead");
            ActionCompleted?.Invoke(this, new ActionCompletedEventArgs { Action = "EmailGallery", Success = false });
            return false;
        }

        private async Task<bool> SmsCurrentSessionAsync()
        {
            try
            {
                string sessionGuid = _sessionService?.CurrentSessionId;
                if (string.IsNullOrEmpty(sessionGuid))
                {
                    _uiService.UpdateStatus("No active session found");
                    return false;
                }

                // Show SMS phone pad immediately - queue service handles URL availability
                if (_sharingUIService != null)
                {
                    _sharingUIService.ShowSmsPhonePadOverlay();
                    Log.Debug($"GalleryActionService: SMS phone pad displayed for session {sessionGuid}");
                    return true;
                }
                else
                {
                    _uiService.UpdateStatus("SMS service not available");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error showing SMS for current session: {ex.Message}");
                _uiService.UpdateStatus("SMS failed");
                return false;
            }
        }

        private async Task<bool> SmsGallerySessionAsync(dynamic gallerySession)
        {
            try
            {
                string sessionId = gallerySession?.SessionFolder?.ToString();
                if (string.IsNullOrEmpty(sessionId))
                {
                    _uiService.UpdateStatus("Invalid gallery session");
                    return false;
                }

                // Show SMS phone pad immediately - queue service handles URL availability
                if (_sharingUIService != null)
                {
                    _sharingUIService.ShowSmsPhonePadOverlay();
                    Log.Debug($"GalleryActionService: Gallery SMS phone pad displayed for session {sessionId}");
                    return true;
                }
                else
                {
                    _uiService.UpdateStatus("SMS service not available");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error showing SMS for gallery session: {ex.Message}");
                _uiService.UpdateStatus("SMS failed");
                return false;
            }
        }

        private async Task<bool> GenerateCurrentQRCodeAsync()
        {
            try
            {
                string sessionGuid = _sessionService?.CurrentSessionId;
                if (string.IsNullOrEmpty(sessionGuid))
                {
                    _uiService.UpdateStatus("No active session found");
                    return false;
                }

                // Use queue service to check QR visibility
                var qrResult = await _queueService.CheckQRVisibilityAsync(sessionGuid, false);
                
                // Update button states based on queue service result
                if (_sharingUIService != null)
                {
                    _sharingUIService.SetSmsButtonState(qrResult.EnableSMS, qrResult.SMSMessage);
                    _sharingUIService.SetQrButtonState(qrResult.IsVisible, qrResult.Message);
                }
                
                if (qrResult.IsVisible && qrResult.QRCodeImage != null)
                {
                    // CRITICAL: Check if session is being cleared before showing QR code
                    if (_sessionService.IsSessionBeingCleared)
                    {
                        Log.Debug($"GalleryActionService: Session being cleared, NOT showing QR code for URL: {qrResult.GalleryUrl}");
                        return false;
                    }

                    _uiService.UpdateStatus("QR Code ready");

                    // Display QR code using SharingUIService
                    if (_sharingUIService != null)
                    {
                        _sharingUIService.ShowQrCodeOverlay(qrResult.GalleryUrl, qrResult.QRCodeImage);
                        Log.Debug($"GalleryActionService: QR code displayed via queue service for URL: {qrResult.GalleryUrl}");
                    }
                    
                    ActionCompleted?.Invoke(this, new ActionCompletedEventArgs 
                    { 
                        Action = "QRCode", 
                        Success = true, 
                        Result = qrResult 
                    });
                    return true;
                }
                else
                {
                    _uiService.UpdateStatus(qrResult.Message);
                    Log.Debug($"GalleryActionService: QR code not ready - {qrResult.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error generating QR code for current session: {ex.Message}");
                _uiService.UpdateStatus("QR Code generation failed");
                return false;
            }
        }

        private async Task<bool> GenerateGalleryQRCodeAsync(dynamic gallerySession)
        {
            try
            {
                string sessionId = gallerySession?.SessionFolder?.ToString();
                Log.Debug($"GalleryActionService.GenerateGalleryQRCodeAsync: SessionFolder value = '{sessionId}'");
                
                if (string.IsNullOrEmpty(sessionId))
                {
                    _uiService.UpdateStatus("Invalid gallery session");
                    return false;
                }

                Log.Debug($"GalleryActionService.GenerateGalleryQRCodeAsync: Checking QR visibility for sessionId: {sessionId}");
                
                // Use queue service to check QR visibility for gallery session
                var qrResult = await _queueService.CheckQRVisibilityAsync(sessionId, true);
                
                // Update button states based on queue service result
                if (_sharingUIService != null)
                {
                    _sharingUIService.SetSmsButtonState(qrResult.EnableSMS, qrResult.SMSMessage);
                    _sharingUIService.SetQrButtonState(qrResult.IsVisible, qrResult.Message);
                }
                
                if (qrResult.IsVisible && qrResult.QRCodeImage != null)
                {
                    // CRITICAL: Check if session is being cleared before showing QR code
                    if (_sessionService.IsSessionBeingCleared)
                    {
                        Log.Debug($"GalleryActionService: Session being cleared, NOT showing Gallery QR code for URL: {qrResult.GalleryUrl}");
                        return false;
                    }

                    _uiService.UpdateStatus("Gallery QR Code ready");

                    // Display QR code using SharingUIService
                    if (_sharingUIService != null)
                    {
                        _sharingUIService.ShowQrCodeOverlay(qrResult.GalleryUrl, qrResult.QRCodeImage);
                        Log.Debug($"GalleryActionService: Gallery QR code displayed via queue service for URL: {qrResult.GalleryUrl}");
                    }
                    
                    ActionCompleted?.Invoke(this, new ActionCompletedEventArgs 
                    { 
                        Action = "QRCodeGallery", 
                        Success = true, 
                        Result = qrResult 
                    });
                    return true;
                }
                else
                {
                    _uiService.UpdateStatus(qrResult.Message);
                    Log.Debug($"GalleryActionService: Gallery QR code not ready - {qrResult.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error generating QR code for gallery session: {ex.Message}");
                _uiService.UpdateStatus("Gallery QR Code generation failed");
                return false;
            }
        }

        #region SMS Helper Methods
        private ShareResult _lastShareResult;

        private async Task<bool> SendSMSWithPrompt(string galleryUrl, string sessionType)
        {
            try
            {
                // For now, use a simple phone number prompt - in a real implementation,
                // this would show a phone number input dialog
                _uiService.UpdateStatus($"SMS available for {sessionType} - phone pad UI needed");
                
                // TODO: Implement phone number input dialog
                // string phoneNumber = await _uiService.PromptForPhoneNumber();
                // if (!string.IsNullOrEmpty(phoneNumber))
                // {
                //     bool success = await _shareService.SendSMSAsync(phoneNumber, galleryUrl);
                //     if (success)
                //     {
                //         _uiService.ShowNotification($"SMS sent to {phoneNumber}!", NotificationType.Success, 3000);
                //         return true;
                //     }
                // }
                
                ActionCompleted?.Invoke(this, new ActionCompletedEventArgs { Action = "SMS", Success = false });
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"SMS prompt error: {ex.Message}");
                return false;
            }
        }
        #endregion

        #endregion
    }

    #region Event Args Classes
    public class ActionCompletedEventArgs : EventArgs
    {
        public string Action { get; set; }
        public bool Success { get; set; }
        public object Result { get; set; }
    }

    public class ActionErrorEventArgs : EventArgs
    {
        public string Action { get; set; }
        public Exception Error { get; set; }
    }
    #endregion
}