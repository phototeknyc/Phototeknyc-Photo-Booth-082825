using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CameraControl.Devices;
using Photobooth.Controls;

namespace Photobooth.Services
{
    /// <summary>
    /// Wrapper service for the comprehensive print system
    /// 
    /// Integrates with the existing sophisticated print infrastructure:
    /// - PrintService.cs: Core printing with dual printer routing and pooling
    /// - PrinterMonitorService.cs: Real-time printer status monitoring  
    /// - PrintDialog.xaml/.cs: Full-featured print dialog UI
    /// - PrinterPoolService.cs: High-volume printer pooling (3-4x speed)
    /// - Advanced printer profiles with DEVMODE capture for DNP printers
    /// - Automatic 2x6 vs 4x6 routing based on image dimensions
    /// - Print limits and session tracking
    /// - Support for multiple print formats and media types
    /// 
    /// This service provides a clean interface to all these features for PhotoboothTouchModern
    /// </summary>
    public class PrintingService
    {
        private readonly PrintService printService;
        private readonly PrinterMonitorService printerMonitor;
        private readonly PrintSettingsService _settingsService;
        
        // Track last printed items for UI state
        private string lastProcessedImagePath;
        private string lastProcessedImagePathForPrinting;
        private bool lastProcessedWas2x6Template; // Critical: tracks if original template was 2x6, even if output is 4x6
        
        // Print button visibility tracking
        private DateTime lastPrintTime = DateTime.MinValue;
        private string lastPrintedSessionId;
        
        // Expose printer status event from monitor service
        public event EventHandler<PrinterMonitorService.PrinterStatusEventArgs> PrinterStatusChanged
        {
            add { printerMonitor.PrinterStatusChanged += value; }
            remove { printerMonitor.PrinterStatusChanged -= value; }
        }
        
        public PrintingService()
        {
            printService = PrintService.Instance;
            printerMonitor = PrinterMonitorService.Instance;
            _settingsService = PrintSettingsService.Instance;
        }
        
        /// <summary>
        /// Check if printing is enabled and printer is ready
        /// Integrates with the comprehensive print system
        /// </summary>
        public bool IsPrintingEnabled()
        {
            try
            {
                return _settingsService.EnablePrinting && printService.IsPrinterReady();
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Get printer status using the existing print service
        /// </summary>
        public PrinterStatus GetPrinterStatus()
        {
            try
            {
                string printerName = printService.GetCurrentPrinterName();
                bool isReady = printService.IsPrinterReady();
                
                return new PrinterStatus
                {
                    PrinterName = printerName,
                    IsOnline = isReady,
                    QueueLength = 0,
                    LastChecked = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Log.Error($"PrintingService: Failed to get printer status: {ex.Message}");
                return new PrinterStatus { IsOnline = false };
            }
        }
        
        /// <summary>
        /// Print image using the comprehensive print system
        /// Supports dual printer routing, pooling, and all advanced features
        /// IMPORTANT: is2x6Template indicates the ORIGINAL template format, not the output image dimensions
        /// </summary>
        public async Task<bool> PrintImageAsync(string imagePath, string sessionId = null, bool is2x6Template = false)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    Log.Error($"PrintingService: Invalid image path: {imagePath}");
                    return false;
                }
                
                if (!IsPrintingEnabled())
                {
                    Log.Debug("PrintingService: Printing is disabled");
                    return false;
                }
                
                // Use the comprehensive PrintService with all its features:
                // - Automatic dual printer routing based on image dimensions
                // - Printer pooling for high-volume events
                // - Print limits and session tracking
                // - Advanced printer settings and profiles
                
                var photoPaths = new List<string> { imagePath };
                string effectiveSessionId = sessionId ?? $"session_{DateTime.Now:yyyyMMdd_HHmmss}";
                
                var result = await Task.Run(() => 
                {
                    try
                    {
                        // Use the comprehensive PrintService with isOriginal2x6Format flag
                        // This ensures proper routing: composed 2x6 templates (even if 4x6 output) go to 2x6 printer
                        return printService.PrintPhotos(photoPaths, effectiveSessionId, 1, is2x6Template);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"PrintingService: Print failed: {ex.Message}");
                        return new PrintResult { Success = false, Message = ex.Message };
                    }
                });
                
                if (result.Success)
                {
                    lastPrintTime = DateTime.Now;
                    lastProcessedImagePath = imagePath;
                    lastProcessedWas2x6Template = is2x6Template;
                    lastPrintedSessionId = effectiveSessionId;
                    
                    Log.Debug($"PrintingService: Successfully printed {imagePath}");
                    Log.Debug($"PrintingService: {result.Message}");
                }
                else
                {
                    Log.Error($"PrintingService: Print failed - {result.Message}");
                }
                
                return result.Success;
            }
            catch (Exception ex)
            {
                Log.Error($"PrintingService: Print operation failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Show the comprehensive print dialog
        /// Integrates with the existing PrintDialog system
        /// </summary>
        public bool ShowPrintDialog(System.Collections.ObjectModel.ObservableCollection<SessionGroup> sessions)
        {
            try
            {
                if (!IsPrintingEnabled())
                {
                    return false;
                }
                
                var printDialog = new Controls.PrintDialog(sessions);
                return printDialog.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                Log.Error($"PrintingService: Failed to show print dialog: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Check if print button should be visible
        /// </summary>
        public bool ShouldShowPrintButton(string sessionId, string processedImagePath)
        {
            // Show if we have a processed image
            if (!string.IsNullOrEmpty(processedImagePath) && File.Exists(processedImagePath))
            {
                return true;
            }
            
            // Show if recently printed this session
            if (!string.IsNullOrEmpty(sessionId) && 
                sessionId == lastPrintedSessionId &&
                (DateTime.Now - lastPrintTime).TotalMinutes < 5)
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Update processed image paths for printing
        /// CRITICAL: Also tracks if the original template was 2x6 format for proper printer routing
        /// </summary>
        public void UpdateProcessedImagePaths(string displayPath, string printPath, bool was2x6Template = false)
        {
            lastProcessedImagePath = displayPath;
            lastProcessedImagePathForPrinting = printPath ?? displayPath;
            lastProcessedWas2x6Template = was2x6Template;
            
            Log.Debug($"PrintingService: Updated paths - Display: {displayPath}, Print: {printPath}, Was2x6Template: {was2x6Template}");
        }
        
        /// <summary>
        /// Get the correct image path for printing
        /// </summary>
        public string GetImagePathForPrinting()
        {
            // Use print-specific path if available, otherwise use display path
            return !string.IsNullOrEmpty(lastProcessedImagePathForPrinting) 
                ? lastProcessedImagePathForPrinting 
                : lastProcessedImagePath;
        }
        
        /// <summary>
        /// Get whether the last processed template was originally a 2x6 format
        /// This is critical for proper printer routing of composed templates
        /// </summary>
        public bool GetWas2x6Template()
        {
            return lastProcessedWas2x6Template;
        }
        
        /// <summary>
        /// Print the current processed image with proper template format routing
        /// Uses the stored template format information for correct printer selection
        /// </summary>
        public async Task<bool> PrintCurrentImageAsync(string sessionId = null)
        {
            string imagePath = GetImagePathForPrinting();
            if (string.IsNullOrEmpty(imagePath))
            {
                Log.Error("PrintingService: No processed image available to print");
                return false;
            }
            
            // Use the stored template format flag for proper routing
            return await PrintImageAsync(imagePath, sessionId, lastProcessedWas2x6Template);
        }
        
        /// <summary>
        /// Start printer monitoring (integrates with PrinterMonitorService)
        /// </summary>
        public void StartMonitoring()
        {
            try
            {
                // Get the current printer name from settings
                string printerName = printService.GetCurrentPrinterName();
                printerMonitor.StartMonitoring(printerName);
                Log.Debug($"PrintingService: Started comprehensive printer monitoring for '{printerName ?? "default"}'");
            }
            catch (Exception ex)
            {
                Log.Error($"PrintingService: Failed to start monitoring: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stop printer monitoring
        /// </summary>
        public void StopMonitoring()
        {
            try
            {
                printerMonitor.StopMonitoring();
                Log.Debug("PrintingService: Stopped printer monitoring");
            }
            catch (Exception ex)
            {
                Log.Error($"PrintingService: Failed to stop monitoring: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get remaining print counts using comprehensive print limits
        /// </summary>
        public (int sessionRemaining, int eventRemaining) GetRemainingPrints(string sessionId = null)
        {
            try
            {
                int sessionRemaining = printService.GetRemainingSessionPrints(sessionId ?? "");
                int eventRemaining = printService.GetRemainingEventPrints();
                return (sessionRemaining, eventRemaining);
            }
            catch (Exception ex)
            {
                Log.Error($"PrintingService: Failed to get remaining prints: {ex.Message}");
                return (0, 0);
            }
        }
        
        /// <summary>
        /// Check if printer supports the comprehensive features
        /// (dual routing, advanced settings)
        /// </summary>
        public bool SupportsAdvancedFeatures()
        {
            try
            {
                // Check if dual printer routing is enabled
                bool dualRoutingEnabled = _settingsService.AutoRoutePrinter;
                
                // Check if print system is fully enabled
                bool printingEnabled = _settingsService.EnablePrinting;
                
                return dualRoutingEnabled && printingEnabled;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Process print request for gallery session with all business logic
        /// Handles photo selection, 2x6 detection, and modal/direct printing
        /// </summary>
        public async Task<PrintRequestResult> ProcessGalleryPrintRequestAsync(SessionGalleryData gallerySession)
        {
            try
            {
                if (gallerySession?.Photos == null)
                {
                    return new PrintRequestResult { Success = false, Message = "No gallery session provided" };
                }

                // Business logic: Find the best photo to print
                var printPhoto = FindBestPhotoToPrint(gallerySession.Photos);
                if (printPhoto == null)
                {
                    Log.Debug("No suitable print photo found in gallery session");
                    return new PrintRequestResult { Success = false, Message = "No composed image to print" };
                }

                // Business logic: Determine if this is a 2x6 template
                bool is2x6Template = DetermineIf2x6Template(printPhoto);
                
                Log.Debug($"★★★ GALLERY PRINT: {printPhoto.FileName}, PhotoType: {printPhoto.PhotoType}, Is2x6: {is2x6Template}");

                // Business logic: Check print modal settings
                var shouldShowModal = ShouldShowPrintModal();
                
                if (shouldShowModal)
                {
                    // Return result indicating modal should be shown
                    return new PrintRequestResult 
                    { 
                        Success = true, 
                        ShowModal = true, 
                        ImagePath = printPhoto.FilePath, 
                        SessionId = gallerySession.SessionFolder, 
                        Is2x6Template = is2x6Template,
                        Message = "Show print modal"
                    };
                }
                else
                {
                    // Print directly
                    bool success = await PrintImageAsync(printPhoto.FilePath, gallerySession.SessionFolder, is2x6Template);
                    return new PrintRequestResult 
                    { 
                        Success = success, 
                        ShowModal = false,
                        Message = success ? "Photos sent to printer!" : "Print failed" 
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing gallery print request: {ex.Message}");
                return new PrintRequestResult { Success = false, Message = $"Print failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Process print request for active session with all business logic
        /// Handles image path selection, dimension analysis, and modal/direct printing
        /// </summary>
        public async Task<PrintRequestResult> ProcessSessionPrintRequestAsync(PhotoboothSessionService sessionService)
        {
            try
            {
                if (sessionService?.IsSessionActive != true)
                {
                    return new PrintRequestResult { Success = false, Message = "No active session" };
                }

                // Business logic: Determine which image to print
                var printImageInfo = DeterminePrintImagePath(sessionService);
                if (string.IsNullOrEmpty(printImageInfo.ImagePath))
                {
                    return new PrintRequestResult { Success = false, Message = "No photo to print" };
                }

                // Business logic: Analyze image properties (for logging/debugging)
                AnalyzePrintImageProperties(printImageInfo.ImagePath, printImageInfo.IsUsingPrintSpecificPath);

                Log.Debug($"★★★ SESSION PRINT: {printImageInfo.ImagePath}, Is2x6: {sessionService.IsCurrentTemplate2x6}");

                // Business logic: Check print modal settings
                var shouldShowModal = ShouldShowPrintModal();
                
                if (shouldShowModal)
                {
                    // Return result indicating modal should be shown
                    return new PrintRequestResult 
                    { 
                        Success = true, 
                        ShowModal = true, 
                        ImagePath = printImageInfo.ImagePath, 
                        SessionId = sessionService.CurrentSessionId, 
                        Is2x6Template = sessionService.IsCurrentTemplate2x6,
                        Message = "Show print modal"
                    };
                }
                else
                {
                    // Print directly
                    bool success = await PrintImageAsync(printImageInfo.ImagePath, sessionService.CurrentSessionId, sessionService.IsCurrentTemplate2x6);
                    return new PrintRequestResult 
                    { 
                        Success = success, 
                        ShowModal = false,
                        Message = success ? "Photo sent to printer!" : "Print failed" 
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing session print request: {ex.Message}");
                return new PrintRequestResult { Success = false, Message = $"Print failed: {ex.Message}" };
            }
        }

        #region Private Business Logic Methods

        /// <summary>
        /// Business logic: Find the best photo to print from gallery session
        /// Priority: 4x6_print > COMP, with file existence check
        /// </summary>
        private PhotoGalleryData FindBestPhotoToPrint(List<PhotoGalleryData> photos)
        {
            // First look for the 4x6_print version (duplicated 2x6)
            var printPhoto = photos?.FirstOrDefault(p => p.PhotoType == "4x6_print" && File.Exists(p.FilePath));
            
            // If no 4x6_print version, look for regular composed image
            if (printPhoto == null)
            {
                printPhoto = photos?.FirstOrDefault(p => p.PhotoType == "COMP" && File.Exists(p.FilePath));
            }

            return printPhoto;
        }

        /// <summary>
        /// Business logic: Determine if photo represents a 2x6 template
        /// Uses multiple detection methods: PhotoType, filename, filepath
        /// </summary>
        private bool DetermineIf2x6Template(PhotoGalleryData photo)
        {
            if (photo == null) return false;

            string fileName = photo.FileName?.ToLower() ?? "";
            string filePath = photo.FilePath?.ToLower() ?? "";
            
            return photo.PhotoType == "4x6_print" || // 4x6_print means it's a duplicated 2x6
                   fileName.Contains("2x6") || fileName.Contains("2_6") || fileName.Contains("2-6") ||
                   filePath.Contains("2x6") || filePath.Contains("2_6") || filePath.Contains("2-6") ||
                   fileName.Contains("4x6_print"); // Also check for 4x6_print in filename
        }

        /// <summary>
        /// Business logic: Determine print image path and whether using print-specific version
        /// Priority: ComposedImagePrintPath > ComposedImagePath
        /// </summary>
        private (string ImagePath, bool IsUsingPrintSpecificPath) DeterminePrintImagePath(PhotoboothSessionService sessionService)
        {
            Log.Debug($"★★★ PRINT PATH SELECTION ★★★");
            Log.Debug($"  - ComposedImagePath (display): {sessionService.ComposedImagePath}");
            Log.Debug($"  - ComposedImagePrintPath (print): {sessionService.ComposedImagePrintPath}");
            Log.Debug($"  - IsCurrentTemplate2x6: {sessionService.IsCurrentTemplate2x6}");

            string imagePath = !string.IsNullOrEmpty(sessionService.ComposedImagePrintPath) ? 
                sessionService.ComposedImagePrintPath : sessionService.ComposedImagePath;
            
            bool isUsingPrintSpecific = imagePath == sessionService.ComposedImagePrintPath;
            
            Log.Debug($"  - SELECTED imageToPrint: {imagePath}");
            
            // Verify file exists
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                return (imagePath, isUsingPrintSpecific);
            }

            return (null, false);
        }

        /// <summary>
        /// Business logic: Analyze print image properties for logging
        /// Checks dimensions and logs diagnostic information
        /// </summary>
        private void AnalyzePrintImageProperties(string imagePath, bool isUsingPrintSpecificPath)
        {
            try
            {
                using (var img = System.Drawing.Image.FromFile(imagePath))
                {
                    Log.Debug($"  - Image dimensions: {img.Width}x{img.Height}");
                    bool is4x6Duplicate = img.Width == 1200 && img.Height == 1800;
                    Log.Debug($"  - Is 4x6 duplicate: {is4x6Duplicate}");
                }

                if (isUsingPrintSpecificPath)
                {
                    Log.Debug($"  - ✅ Using 4x6 duplicate for printing (different from display)");
                }
                else
                {
                    Log.Debug($"  - ⚠️ Using same image for display and print");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error analyzing print image properties: {ex.Message}");
            }
        }

        /// <summary>
        /// Business logic: Determine if print modal should be shown
        /// Based on print settings configuration
        /// </summary>
        private bool ShouldShowPrintModal()
        {
            bool bypassPrintDialog = Properties.Settings.Default.ShowPrintDialog == false;
            bool showCopiesModal = _settingsService.ShowPrintCopiesModal;
            
            System.Diagnostics.Debug.WriteLine($"🔍 PRINT MODAL CHECK: bypassPrintDialog={bypassPrintDialog}, showCopiesModal={showCopiesModal}");
            
            return bypassPrintDialog && showCopiesModal;
        }

        #endregion
    }

    /// <summary>
    /// Result of print request processing operation
    /// Contains success status, modal display flag, and print parameters
    /// </summary>
    public class PrintRequestResult
    {
        public bool Success { get; set; }
        public bool ShowModal { get; set; }
        public string ImagePath { get; set; }
        public string SessionId { get; set; }
        public bool Is2x6Template { get; set; }
        public string Message { get; set; }
    }
}