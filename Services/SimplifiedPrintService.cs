using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Simplified print service that uses saved printer profiles
    /// Eliminates complex orientation logic by using pre-configured settings
    /// </summary>
    public class SimplifiedPrintService
    {
        private static SimplifiedPrintService _instance;
        public static SimplifiedPrintService Instance => _instance ?? (_instance = new SimplifiedPrintService());
        
        private PrinterSettingsManager settingsManager;
        private Image currentImageToPrint;
        private string currentProfileName;
        
        public SimplifiedPrintService()
        {
            settingsManager = PrinterSettingsManager.Instance;
            InitializeDefaultProfiles();
        }
        
        /// <summary>
        /// Initialize default profiles for common paper sizes
        /// </summary>
        private void InitializeDefaultProfiles()
        {
            try
            {
                // Get default printer
                var printDoc = new PrintDocument();
                string defaultPrinter = printDoc.PrinterSettings.PrinterName;
                
                // Create profiles for common sizes if they don't exist
                CreateProfileIfNotExists(defaultPrinter, "4x6", false); // 4x6 Portrait
                CreateProfileIfNotExists(defaultPrinter, "4x6", true);  // 4x6 Landscape
                CreateProfileIfNotExists(defaultPrinter, "2x6", false); // 2x6 Portrait
                CreateProfileIfNotExists(defaultPrinter, "2x6", true);  // 2x6 Landscape
                
                // Get 2x6 printer if configured
                string printer2x6 = Properties.Settings.Default.Printer2x6Name;
                if (!string.IsNullOrEmpty(printer2x6) && printer2x6 != defaultPrinter)
                {
                    CreateProfileIfNotExists(printer2x6, "2x6", false);
                    CreateProfileIfNotExists(printer2x6, "2x6", true);
                    CreateProfileIfNotExists(printer2x6, "4x6", false); // For duplicated 2x6
                    CreateProfileIfNotExists(printer2x6, "4x6", true);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SimplifiedPrintService: Failed to initialize profiles: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create a profile if it doesn't exist
        /// </summary>
        private void CreateProfileIfNotExists(string printerName, string paperSize, bool landscape)
        {
            string profileName = GetProfileName(paperSize, landscape);
            
            // Check if profile already exists
            var testDoc = new PrintDocument();
            testDoc.PrinterSettings.PrinterName = printerName;
            
            if (!settingsManager.LoadPrinterProfile(testDoc, profileName))
            {
                // Profile doesn't exist, create it
                var profile = settingsManager.CreateSimpleProfile(printerName, paperSize, landscape);
                
                // Configure the print document with our desired settings
                ConfigurePrintDocument(testDoc, paperSize, landscape);
                
                // Save the profile (2x6 templates typically need 2 inch cut enabled)
                bool enable2InchCut = paperSize == "2x6";
                settingsManager.SavePrinterProfile(testDoc, profileName, enable2InchCut);
                
                Log.Debug($"SimplifiedPrintService: Created profile '{profileName}' for printer '{printerName}'");
            }
        }
        
        /// <summary>
        /// Configure print document for specific paper size and orientation
        /// </summary>
        private void ConfigurePrintDocument(PrintDocument printDoc, string paperSize, bool landscape)
        {
            // Find or create the paper size
            PaperSize targetSize = null;
            
            foreach (PaperSize size in printDoc.PrinterSettings.PaperSizes)
            {
                if (size.PaperName.Contains(paperSize) || 
                    (paperSize == "4x6" && (size.PaperName.Contains("4x6") || size.PaperName.Contains("4 x 6"))) ||
                    (paperSize == "2x6" && (size.PaperName.Contains("2x6") || size.PaperName.Contains("2 x 6"))))
                {
                    targetSize = size;
                    break;
                }
            }
            
            // If not found, create custom size
            if (targetSize == null)
            {
                switch (paperSize)
                {
                    case "4x6":
                        targetSize = new PaperSize("4x6", 400, 600);
                        break;
                    case "2x6":
                        targetSize = new PaperSize("2x6", 200, 600);
                        break;
                    case "5x7":
                        targetSize = new PaperSize("5x7", 500, 700);
                        break;
                    case "8x10":
                        targetSize = new PaperSize("8x10", 800, 1000);
                        break;
                    default:
                        targetSize = printDoc.DefaultPageSettings.PaperSize;
                        break;
                }
            }
            
            printDoc.DefaultPageSettings.PaperSize = targetSize;
            printDoc.DefaultPageSettings.Landscape = landscape;
            
            // Set high quality printing
            foreach (PrinterResolution resolution in printDoc.PrinterSettings.PrinterResolutions)
            {
                if (resolution.Kind == PrinterResolutionKind.High)
                {
                    printDoc.DefaultPageSettings.PrinterResolution = resolution;
                    break;
                }
            }
            
            // Set minimal margins
            printDoc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        }
        
        /// <summary>
        /// Print an image using a specific profile
        /// </summary>
        public bool PrintImage(string imagePath, string paperSize, bool? forceOrientation = null)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    Log.Error($"SimplifiedPrintService: Image not found: {imagePath}");
                    return false;
                }
                
                // Load the image
                using (var image = Image.FromFile(imagePath))
                {
                    // Determine orientation based on image or forced setting
                    bool imageIsLandscape = image.Width > image.Height;
                    bool useLandscape = forceOrientation ?? imageIsLandscape;
                    
                    // Determine which printer to use
                    string printerName = GetPrinterForSize(paperSize);
                    
                    // Get the profile name
                    string profileName = GetProfileName(paperSize, useLandscape);
                    
                    // Print using the profile
                    return PrintWithProfile(image, printerName, profileName);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SimplifiedPrintService: Failed to print image: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Print with auto-detection of size and orientation
        /// </summary>
        public bool AutoPrint(string imagePath, bool is2x6Template = false)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    Log.Error($"SimplifiedPrintService: Image not found: {imagePath}");
                    return false;
                }
                
                using (var image = Image.FromFile(imagePath))
                {
                    // Detect paper size based on image dimensions or template type
                    string paperSize = DetectPaperSize(image, is2x6Template);
                    
                    // For 2x6 templates, always use landscape
                    // For 4x6, match the image orientation
                    bool useLandscape = is2x6Template ? true : (image.Width > image.Height);
                    
                    // Get printer and profile
                    string printerName = GetPrinterForSize(paperSize);
                    string profileName = GetProfileName(paperSize, useLandscape);
                    
                    Log.Debug($"SimplifiedPrintService: Auto-printing with paper={paperSize}, landscape={useLandscape}, printer={printerName}");
                    
                    return PrintWithProfile(image, printerName, profileName);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SimplifiedPrintService: Auto-print failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Print using a specific profile
        /// </summary>
        private bool PrintWithProfile(Image image, string printerName, string profileName)
        {
            try
            {
                var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = printerName;
                
                // Load the profile
                if (!settingsManager.LoadPrinterProfile(printDoc, profileName))
                {
                    Log.Error($"SimplifiedPrintService: Failed to load profile '{profileName}'");
                    return false;
                }
                
                // Store image and profile for print event
                currentImageToPrint = image;
                currentProfileName = profileName;
                
                // Set up print event
                printDoc.PrintPage += SimplePrintPage;
                
                // Print
                printDoc.Print();
                
                Log.Debug($"SimplifiedPrintService: Successfully printed using profile '{profileName}'");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"SimplifiedPrintService: Print failed: {ex.Message}");
                return false;
            }
            finally
            {
                currentImageToPrint = null;
                currentProfileName = null;
            }
        }
        
        /// <summary>
        /// Simple print page event - just scale and center the image
        /// </summary>
        private void SimplePrintPage(object sender, PrintPageEventArgs e)
        {
            if (currentImageToPrint == null) return;
            
            // Get page bounds
            Rectangle pageBounds = e.PageBounds;
            
            // Calculate scaling to fill the page
            float scaleX = pageBounds.Width / (float)currentImageToPrint.Width;
            float scaleY = pageBounds.Height / (float)currentImageToPrint.Height;
            
            // Use fill mode (cover entire page, may crop)
            float scale = Math.Max(scaleX, scaleY);
            
            // Calculate dimensions
            int scaledWidth = (int)(currentImageToPrint.Width * scale);
            int scaledHeight = (int)(currentImageToPrint.Height * scale);
            
            // Center on page
            int x = (pageBounds.Width - scaledWidth) / 2;
            int y = (pageBounds.Height - scaledHeight) / 2;
            
            // Set high quality
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            
            // Draw the image
            e.Graphics.DrawImage(currentImageToPrint, x, y, scaledWidth, scaledHeight);
            
            Log.Debug($"SimplifiedPrintService: Printed at {x},{y} size {scaledWidth}x{scaledHeight} (scale={scale:F2})");
        }
        
        /// <summary>
        /// Detect paper size from image dimensions
        /// </summary>
        private string DetectPaperSize(Image image, bool is2x6Template)
        {
            if (is2x6Template) return "2x6";
            
            // Check aspect ratio
            float aspectRatio = (float)Math.Max(image.Width, image.Height) / Math.Min(image.Width, image.Height);
            
            // 2x6 = 3:1 ratio
            if (Math.Abs(aspectRatio - 3.0f) < 0.1f) return "2x6";
            
            // 4x6 = 1.5:1 ratio (most common)
            if (Math.Abs(aspectRatio - 1.5f) < 0.2f) return "4x6";
            
            // 5x7 = 1.4:1 ratio
            if (Math.Abs(aspectRatio - 1.4f) < 0.1f) return "5x7";
            
            // 8x10 = 1.25:1 ratio
            if (Math.Abs(aspectRatio - 1.25f) < 0.1f) return "8x10";
            
            // Default to 4x6
            return "4x6";
        }
        
        /// <summary>
        /// Get printer for specific paper size
        /// </summary>
        private string GetPrinterForSize(string paperSize)
        {
            // Use 2x6 printer for 2x6 if configured
            if (paperSize == "2x6" && !string.IsNullOrEmpty(Properties.Settings.Default.Printer2x6Name))
            {
                return Properties.Settings.Default.Printer2x6Name;
            }
            
            // Use default printer for everything else
            var printDoc = new PrintDocument();
            return printDoc.PrinterSettings.PrinterName;
        }
        
        /// <summary>
        /// Get profile name for paper size and orientation
        /// </summary>
        private string GetProfileName(string paperSize, bool landscape)
        {
            return $"{paperSize}_{(landscape ? "Landscape" : "Portrait")}";
        }
        
        /// <summary>
        /// Update a profile's settings (for UI configuration)
        /// </summary>
        public bool UpdateProfile(string printerName, string paperSize, bool landscape, Action<PrintDocument> configureAction)
        {
            try
            {
                var printDoc = new PrintDocument();
                printDoc.PrinterSettings.PrinterName = printerName;
                
                // Let caller configure the document
                configureAction(printDoc);
                
                // Save the profile
                string profileName = GetProfileName(paperSize, landscape);
                // Enable 2 inch cut for 2x6 paper size
                bool enable2InchCut = paperSize == "2x6";
                return settingsManager.SavePrinterProfile(printDoc, profileName, enable2InchCut);
            }
            catch (Exception ex)
            {
                Log.Error($"SimplifiedPrintService: Failed to update profile: {ex.Message}");
                return false;
            }
        }
    }
}