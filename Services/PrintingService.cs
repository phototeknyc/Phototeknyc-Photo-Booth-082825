using System;
using System.Collections.Generic;
using System.IO;
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
        }
        
        /// <summary>
        /// Check if printing is enabled and printer is ready
        /// Integrates with the comprehensive print system
        /// </summary>
        public bool IsPrintingEnabled()
        {
            try
            {
                return Properties.Settings.Default.EnablePrinting && printService.IsPrinterReady();
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
                bool dualRoutingEnabled = Properties.Settings.Default.AutoRoutePrinter;
                
                // Check if print system is fully enabled
                bool printingEnabled = Properties.Settings.Default.EnablePrinting;
                
                return dualRoutingEnabled && printingEnabled;
            }
            catch
            {
                return false;
            }
        }
    }
}