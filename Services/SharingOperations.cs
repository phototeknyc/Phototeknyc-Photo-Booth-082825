using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Services;
using Photobooth.Database;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles all sharing operations including QR codes, SMS, and uploads
    /// </summary>
    public class SharingOperations
    {
        private readonly Pages.PhotoboothTouchModern _parent;
        private OfflineQueueService _cachedOfflineQueueService;
        private string _smsPhoneNumber = "+1";
        
        // UI Elements that need to be passed in
        private readonly Grid sharingOverlay;
        private readonly Grid smsPhonePadOverlay;
        private readonly Image qrCodeImage;
        private readonly TextBlock galleryUrlText;
        private readonly TextBox phoneNumberTextBox;
        private readonly Button sendSmsButton;
        private readonly TextBlock smsPhoneDisplay;
        
        public SharingOperations(Pages.PhotoboothTouchModern parent)
        {
            _parent = parent;
            
            // Get UI elements from parent
            sharingOverlay = parent.FindName("sharingOverlay") as Grid;
            smsPhonePadOverlay = parent.FindName("smsPhonePadOverlay") as Grid;
            qrCodeImage = parent.FindName("qrCodeImage") as Image;
            galleryUrlText = parent.FindName("galleryUrlText") as TextBlock;
            phoneNumberTextBox = parent.FindName("phoneNumberTextBox") as TextBox;
            sendSmsButton = parent.FindName("sendSmsButton") as Button;
            smsPhoneDisplay = parent.FindName("smsPhoneDisplay") as TextBlock;
        }
        
        /// <summary>
        /// Get or create cached offline queue service
        /// </summary>
        public OfflineQueueService GetOrCreateOfflineQueueService()
        {
            if (_cachedOfflineQueueService == null)
            {
                _cachedOfflineQueueService = new OfflineQueueService();
            }
            return _cachedOfflineQueueService;
        }
        
        /// <summary>
        /// Handle QR code sharing button click
        /// </summary>
        public void HandleQrCodeSharingClick(ShareResult currentShareResult, string currentSessionGuid)
        {
            Log.Debug("SharingOperations.HandleQrCodeSharingClick: Starting QR code sharing");
            
            try
            {
                // Check if we have a cached gallery URL first (instant)
                if (currentShareResult != null && !string.IsNullOrEmpty(currentShareResult.GalleryUrl))
                {
                    // Show QR code immediately with cached data
                    ShowQRCodeOverlay(currentShareResult.GalleryUrl);
                    return;
                }
                
                // Check database in background for existing URL
                if (!string.IsNullOrEmpty(currentSessionGuid))
                {
                    // Show loading QR overlay immediately
                    ShowQRCodeOverlay("Loading..."); // Show overlay with loading state
                    
                    // Load data in background
                    Task.Run(() => 
                    {
                        try
                        {
                            var db = new TemplateDatabase();
                            var galleryUrl = db.GetPhotoSessionGalleryUrl(currentSessionGuid);
                            
                            if (!string.IsNullOrEmpty(galleryUrl))
                            {
                                Log.Debug($"SharingOperations: Found gallery URL in database: {galleryUrl}");
                                
                                // Generate QR code
                                var cloudService = CloudShareProvider.GetShareService();
                                var qrCodeImageBitmap = cloudService?.GenerateQRCode(galleryUrl);
                                
                                // Update UI on main thread
                                _parent.Dispatcher.Invoke(() => 
                                {
                                    if (qrCodeImageBitmap != null)
                                    {
                                        UpdateQRCodeDisplay(galleryUrl, qrCodeImageBitmap);
                                    }
                                });
                                
                                // Update parent's share result for caching
                                _parent.UpdateShareResult(new ShareResult
                                {
                                    Success = true,
                                    GalleryUrl = galleryUrl,
                                    QRCodeImage = qrCodeImageBitmap,
                                    UploadedPhotos = new List<UploadedPhoto>()
                                });
                            }
                            else
                            {
                                // No URL found, close overlay and show message
                                _parent.Dispatcher.Invoke(() => 
                                {
                                    HideQRCodeOverlay();
                                    _parent.ShowSimpleMessage("Photos need to be uploaded first before sharing QR code.");
                                });
                            }
                        }
                        catch (Exception dbEx)
                        {
                            Log.Error($"SharingOperations: Error checking database: {dbEx.Message}");
                            _parent.Dispatcher.Invoke(() => 
                            {
                                HideQRCodeOverlay();
                                _parent.ShowSimpleMessage("Failed to load QR code.");
                            });
                        }
                    });
                }
                else
                {
                    _parent.ShowSimpleMessage("Photos need to be uploaded first before sharing QR code.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SharingOperations.HandleQrCodeSharingClick: Error showing QR code: {ex.Message}");
                _parent.ShowSimpleMessage($"QR code display failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle SMS sharing button click
        /// </summary>
        public void HandleSmsSharingClick(string lastProcessedImagePath, List<string> capturedPhotoPaths, 
            ShareResult currentShareResult, string currentSessionGuid)
        {
            Log.Debug("SharingOperations.HandleSmsSharingClick: Starting SMS sharing (offline-capable)");
            
            try
            {
                if (lastProcessedImagePath == null)
                {
                    _parent.ShowSimpleMessage("No photos available for sharing");
                    return;
                }

                // Show SMS phone pad immediately - no waiting
                ShowSmsPhonePadOverlay();
                
                // Queue upload in background if needed (non-blocking)
                Task.Run(async () => 
                {
                    try
                    {
                        // Only queue for upload if not already uploaded
                        if (currentShareResult == null || string.IsNullOrEmpty(currentShareResult.GalleryUrl))
                        {
                            // Prepare photos for upload/queue
                            var photosToShare = new List<string>();
                            if (capturedPhotoPaths != null && capturedPhotoPaths.Count > 0)
                            {
                                photosToShare.AddRange(capturedPhotoPaths);
                            }
                            if (!string.IsNullOrEmpty(lastProcessedImagePath) && File.Exists(lastProcessedImagePath))
                            {
                                photosToShare.Add(lastProcessedImagePath);
                            }

                            string sessionId = currentSessionGuid ?? Guid.NewGuid().ToString();
                            
                            // Check database first for existing gallery URL
                            var db = new TemplateDatabase();
                            var existingUrl = db.GetPhotoSessionGalleryUrl(sessionId);
                            
                            if (!string.IsNullOrEmpty(existingUrl))
                            {
                                // Found existing URL, use it
                                var cloudService = CloudShareProvider.GetShareService();
                                var shareResult = new ShareResult
                                {
                                    Success = true,
                                    GalleryUrl = existingUrl,
                                    QRCodeImage = cloudService?.GenerateQRCode(existingUrl),
                                    UploadedPhotos = new List<UploadedPhoto>()
                                };
                                _parent.UpdateShareResult(shareResult);
                            }
                            else
                            {
                                // Queue for upload
                                var queueService = GetOrCreateOfflineQueueService();
                                var uploadResult = await queueService.QueuePhotosForUpload(sessionId, photosToShare);
                                
                                if (uploadResult.Success)
                                {
                                    var shareResult = new ShareResult
                                    {
                                        Success = true,
                                        GalleryUrl = uploadResult.GalleryUrl,
                                        QRCodeImage = uploadResult.QRCodeImage,
                                        UploadedPhotos = new List<UploadedPhoto>()
                                    };
                                    _parent.UpdateShareResult(shareResult);
                                    
                                    // Store gallery URL if immediate upload
                                    if (uploadResult.Immediate && !string.IsNullOrEmpty(uploadResult.GalleryUrl))
                                    {
                                        db.UpdatePhotoSessionGalleryUrl(sessionId, uploadResult.GalleryUrl);
                                    }
                                }
                            }
                            
                            // Update UI on main thread
                            _parent.Dispatcher.Invoke(() => _parent.UpdateSharingButtonsVisibility());
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"SharingOperations.HandleSmsSharingClick: Background upload error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"SharingOperations.HandleSmsSharingClick: Error starting SMS sharing: {ex.Message}");
                _parent.ShowSimpleMessage($"SMS sharing failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show QR code overlay
        /// </summary>
        public void ShowQRCodeOverlay(string galleryUrl)
        {
            try
            {
                // Show overlay immediately
                if (sharingOverlay != null)
                    sharingOverlay.Visibility = Visibility.Visible;
                
                if (string.IsNullOrEmpty(galleryUrl) || galleryUrl == "Loading...")
                {
                    // Show loading state
                    if (qrCodeImage != null)
                        qrCodeImage.Source = null;
                    if (galleryUrlText != null)
                        galleryUrlText.Text = "Loading...";
                    return;
                }
                
                var cloudService = CloudShareProvider.GetShareService();
                var qrCodeBitmap = cloudService.GenerateQRCode(galleryUrl);
                
                if (qrCodeBitmap != null)
                {
                    UpdateQRCodeDisplay(galleryUrl, qrCodeBitmap);
                    
                    // Hide SMS-related elements (phone number textbox and send button)
                    if (phoneNumberTextBox != null)
                    {
                        phoneNumberTextBox.Visibility = Visibility.Collapsed;
                    }
                    if (sendSmsButton != null)
                    {
                        sendSmsButton.Visibility = Visibility.Collapsed;
                    }
                    
                    Log.Debug($"SharingOperations.ShowQRCodeOverlay: QR code displayed for URL: {galleryUrl}");
                }
                else
                {
                    _parent.ShowSimpleMessage("Failed to generate QR code");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SharingOperations.ShowQRCodeOverlay: Error showing QR code: {ex.Message}");
                _parent.ShowSimpleMessage("Failed to display QR code");
            }
        }
        
        /// <summary>
        /// Update QR code display
        /// </summary>
        private void UpdateQRCodeDisplay(string galleryUrl, BitmapImage qrCodeBitmap)
        {
            if (qrCodeImage != null)
            {
                qrCodeImage.Source = qrCodeBitmap;
            }
            
            if (galleryUrlText != null)
            {
                galleryUrlText.Text = galleryUrl;
            }
        }
        
        /// <summary>
        /// Hide QR code overlay
        /// </summary>
        public void HideQRCodeOverlay()
        {
            if (sharingOverlay != null)
                sharingOverlay.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// Show SMS phone pad overlay
        /// </summary>
        public void ShowSmsPhonePadOverlay()
        {
            _smsPhoneNumber = "+1";
            UpdateSmsPhoneDisplay();
            if (smsPhonePadOverlay != null)
                smsPhonePadOverlay.Visibility = Visibility.Visible;
        }
        
        /// <summary>
        /// Hide SMS phone pad overlay
        /// </summary>
        public void HideSmsPhonePadOverlay()
        {
            if (smsPhonePadOverlay != null)
                smsPhonePadOverlay.Visibility = Visibility.Collapsed;
        }
        
        /// <summary>
        /// Handle SMS phone pad button click
        /// </summary>
        public void HandleSmsPhonePadButton(string digit)
        {
            if (_smsPhoneNumber.Length < 20) // Max phone number length
            {
                _smsPhoneNumber += digit;
                UpdateSmsPhoneDisplay();
            }
        }
        
        /// <summary>
        /// Handle SMS phone backspace
        /// </summary>
        public void HandleSmsPhoneBackspace()
        {
            // Remove last digit, but keep "+1" as minimum
            if (_smsPhoneNumber.Length > 2)
            {
                _smsPhoneNumber = _smsPhoneNumber.Substring(0, _smsPhoneNumber.Length - 1);
                UpdateSmsPhoneDisplay();
            }
        }
        
        /// <summary>
        /// Update SMS phone display
        /// </summary>
        private void UpdateSmsPhoneDisplay()
        {
            if (smsPhoneDisplay != null)
            {
                // Format phone number for display (e.g., +1 (555) 123-4567)
                string formatted = _smsPhoneNumber;
                if (_smsPhoneNumber.StartsWith("+1") && _smsPhoneNumber.Length > 2)
                {
                    string digits = _smsPhoneNumber.Substring(2);
                    if (digits.Length >= 10)
                    {
                        formatted = $"+1 ({digits.Substring(0, 3)}) {digits.Substring(3, 3)}-{digits.Substring(6)}";
                    }
                    else if (digits.Length >= 6)
                    {
                        formatted = $"+1 ({digits.Substring(0, 3)}) {digits.Substring(3)}";
                    }
                    else if (digits.Length >= 3)
                    {
                        formatted = $"+1 ({digits.Substring(0, 3)}) {digits.Substring(3)}";
                    }
                    else if (digits.Length > 0)
                    {
                        formatted = $"+1 ({digits}";
                    }
                }
                smsPhoneDisplay.Text = formatted;
            }
        }
        
        /// <summary>
        /// Send SMS with gallery link
        /// </summary>
        public async Task<bool> SendSms(ShareResult currentShareResult, string currentSessionGuid)
        {
            try
            {
                // Validate phone number
                if (_smsPhoneNumber.Length < 5) // Minimum phone number length
                {
                    _parent.ShowSimpleMessage("Please enter a valid phone number");
                    return false;
                }
                
                // Use the phone number from our dedicated phone pad
                string phoneNumber = _smsPhoneNumber;
                
                // Get gallery URL from current share result or use pending URL
                string galleryUrl = currentShareResult?.GalleryUrl;
                string sessionId = currentSessionGuid ?? Guid.NewGuid().ToString();
                
                // If no gallery URL, this means photos are pending upload
                if (string.IsNullOrEmpty(galleryUrl))
                {
                    galleryUrl = $"https://photos.app/pending/{sessionId}";
                    Log.Debug($"SharingOperations.SendSms: Using pending URL: {galleryUrl}");
                }
                
                // Use cached offline queue service for SMS (works offline)
                var queueService = GetOrCreateOfflineQueueService();
                var queueResult = await queueService.QueueSMS(phoneNumber, galleryUrl, sessionId);
                
                if (queueResult.Success)
                {
                    Log.Debug($"SharingOperations.SendSms: SMS queued successfully for {phoneNumber}");
                    
                    // Log SMS in the database as well
                    try
                    {
                        var db = new TemplateDatabase();
                        db.LogSMSSend(sessionId, phoneNumber, galleryUrl, queueResult.Immediate, 
                                     queueResult.Immediate ? null : "Queued for sending when online");
                        Log.Debug($"Logged SMS queue result to database for session {sessionId}");
                    }
                    catch (Exception dbEx)
                    {
                        Log.Error($"Failed to log SMS to database: {dbEx.Message}");
                    }
                    
                    // Show success message
                    if (queueResult.Immediate)
                    {
                        _parent.ShowSimpleMessage("SMS sent successfully!");
                    }
                    else
                    {
                        _parent.ShowSimpleMessage("SMS queued for sending when online");
                    }
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"SharingOperations.SendSms: Error sending SMS: {ex.Message}");
                _parent.ShowSimpleMessage($"SMS send failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            _cachedOfflineQueueService = null;
        }
    }
}