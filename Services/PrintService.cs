using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Data.SQLite;
using System.Windows.Forms;
using System.Management;

namespace Photobooth.Services
{
    public class PrintService
    {
        private static PrintService _instance;
        private static readonly object _lock = new object();
        private PrintDocument printDocument;
        private List<string> imagesToPrint;
        private int currentPrintIndex;
        private string databasePath;
        private Dictionary<string, PrintSessionInfo> sessionPrintTracking;
        private int totalEventPrints;
        private PrintSettingsService _settingsService;

        public static PrintService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PrintService();
                        }
                    }
                }
                return _instance;
            }
        }

        private PrintService()
        {
            _settingsService = PrintSettingsService.Instance;
            printDocument = new PrintDocument();
            printDocument.PrintPage += PrintDocument_PrintPage;
            imagesToPrint = new List<string>();
            sessionPrintTracking = new Dictionary<string, PrintSessionInfo>();
            
            // Initialize database
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Photobooth");
            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);
            
            databasePath = Path.Combine(appDataPath, "print_tracking.db");
            InitializeDatabase();
            LoadPrintHistory();
            
            // Auto-select USB printer on startup
            RefreshPrinterSelection();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();
                
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS PrintHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId TEXT NOT NULL,
                        PhotoPath TEXT NOT NULL,
                        PrintCount INTEGER DEFAULT 0,
                        LastPrintTime DATETIME,
                        EventDate DATE DEFAULT CURRENT_DATE,
                        IsReprint INTEGER DEFAULT 0
                    );
                    
                    CREATE TABLE IF NOT EXISTS EventPrintSummary (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EventDate DATE NOT NULL UNIQUE,
                        TotalPrints INTEGER DEFAULT 0,
                        LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP
                    );
                    
                    CREATE INDEX IF NOT EXISTS idx_session_id ON PrintHistory(SessionId);
                    CREATE INDEX IF NOT EXISTS idx_event_date ON PrintHistory(EventDate);";
                
                using (var command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void LoadPrintHistory()
        {
            try
            {
                using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
                {
                    connection.Open();
                    
                    // Load today's event print count
                    string eventQuery = "SELECT TotalPrints FROM EventPrintSummary WHERE EventDate = DATE('now')";
                    using (var command = new SQLiteCommand(eventQuery, connection))
                    {
                        var result = command.ExecuteScalar();
                        totalEventPrints = result != null ? Convert.ToInt32(result) : 0;
                    }
                    
                    // Load session print history for today
                    string sessionQuery = @"
                        SELECT SessionId, PhotoPath, SUM(PrintCount) as TotalPrints 
                        FROM PrintHistory 
                        WHERE EventDate = DATE('now') 
                        GROUP BY SessionId, PhotoPath";
                    
                    using (var command = new SQLiteCommand(sessionQuery, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string sessionId = reader.GetString(0);
                            string photoPath = reader.GetString(1);
                            int printCount = reader.GetInt32(2);
                            
                            if (!sessionPrintTracking.ContainsKey(sessionId))
                            {
                                sessionPrintTracking[sessionId] = new PrintSessionInfo();
                            }
                            
                            sessionPrintTracking[sessionId].PhotoPrintCounts[photoPath] = printCount;
                            sessionPrintTracking[sessionId].TotalSessionPrints += printCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading print history: {ex.Message}");
            }
        }

        public bool CanPrint(string sessionId, int requestedCopies)
        {
            // Check if printing is enabled
            if (!_settingsService.EnablePrinting)
                return false;
            
            // Check event limit
            int maxEventPrints = _settingsService.MaxEventPrints;
            if (maxEventPrints > 0 && totalEventPrints + requestedCopies > maxEventPrints)
                return false;
            
            // Check session limit
            int maxSessionPrints = _settingsService.MaxSessionPrints;
            if (maxSessionPrints > 0)
            {
                int currentSessionPrints = GetSessionPrintCount(sessionId);
                if (currentSessionPrints + requestedCopies > maxSessionPrints)
                    return false;
            }
            
            return true;
        }

        public int GetSessionPrintCount(string sessionId)
        {
            if (sessionPrintTracking.ContainsKey(sessionId))
                return sessionPrintTracking[sessionId].TotalSessionPrints;
            return 0;
        }

        public int GetRemainingSessionPrints(string sessionId)
        {
            int maxSessionPrints = _settingsService.MaxSessionPrints;
            if (maxSessionPrints <= 0)
                return int.MaxValue; // Unlimited
            
            int used = GetSessionPrintCount(sessionId);
            return Math.Max(0, maxSessionPrints - used);
        }

        public int GetRemainingEventPrints()
        {
            int maxEventPrints = _settingsService.MaxEventPrints;
            if (maxEventPrints <= 0)
                return int.MaxValue; // Unlimited
            
            return Math.Max(0, maxEventPrints - totalEventPrints);
        }

        public PrintResult PrintPhotos(List<string> photoPaths, string sessionId, int copies = 1)
        {
            return PrintPhotos(photoPaths, sessionId, copies, false);
        }
        
        public PrintResult PrintPhotos(List<string> photoPaths, string sessionId, int copies, bool isOriginal2x6Format)
        {
            var result = new PrintResult();
            
            // DEBUG: Log the critical parameters
            System.Diagnostics.Debug.WriteLine($"★★★ PRINT DEBUG START ★★★");
            System.Diagnostics.Debug.WriteLine($"=== PRINT DEBUG: isOriginal2x6Format={isOriginal2x6Format}, AutoRoutePrinter={_settingsService.AutoRoutePrinter}, Printer2x6Name='{_settingsService.Printer2x6Name}'");
            System.Diagnostics.Debug.WriteLine($"=== PRINT DEBUG: Primary PrinterName='{_settingsService.PrinterName}'");
            System.Diagnostics.Debug.WriteLine($"=== PRINT DEBUG: EnablePrinting={_settingsService.EnablePrinting}");
            System.Diagnostics.Debug.WriteLine($"★★★ PRINT DEBUG END ★★★");
            
            // Validate print allowance
            int totalRequestedPrints = photoPaths.Count * copies;
            if (!CanPrint(sessionId, totalRequestedPrints))
            {
                result.Success = false;
                result.Message = "Print limit exceeded. Cannot print requested number of copies.";
                result.RemainingSessionPrints = GetRemainingSessionPrints(sessionId);
                result.RemainingEventPrints = GetRemainingEventPrints();
                return result;
            }
            
            try
            {
                imagesToPrint.Clear();
                
                // Add photos to print queue based on number of copies
                foreach (var photo in photoPaths)
                {
                    for (int i = 0; i < copies; i++)
                    {
                        imagesToPrint.Add(photo);
                    }
                }
                
                currentPrintIndex = 0;
                
                // Configure print settings
                printDocument.PrinterSettings.Copies = 1; // We handle copies manually
                
                // Store orientation setting to apply AFTER DEVMODE
                bool desiredLandscapeOrientation = _settingsService.PrintLandscape;
                bool isDuplicated4x6 = false;
                
                // Determine orientation based on format and duplication
                if (isOriginal2x6Format)
                {
                    // For 2x6, check if we're printing a duplicated 4x6 or original 2x6
                    // If the image is 1200x1800 (duplicated 4x6), we need PORTRAIT printing for proper 2-inch cuts
                    // If the image is 600x1800 (original 2x6), we need portrait printing
                    if (photoPaths != null && photoPaths.Count > 0)
                    {
                        try
                        {
                            using (var image = System.Drawing.Image.FromFile(photoPaths[0]))
                            {
                                // Check if this is a duplicated 4x6 (width around 1200) vs original 2x6 (width around 600)
                                isDuplicated4x6 = image.Width > 1000; // 4x6 would be ~1200 pixels wide at 300 DPI
                                System.Diagnostics.Debug.WriteLine($"ORIENTATION: Image is {image.Width}x{image.Height}, isDuplicated4x6={isDuplicated4x6}");
                            }
                        }
                        catch { }
                    }
                    
                    if (isDuplicated4x6)
                    {
                        // For duplicated 4x6 in portrait format (1200x1800), use PORTRAIT printing
                        // This ensures proper 2-inch cuts for photo strips when printed on 2x6 media
                        desiredLandscapeOrientation = false;
                        System.Diagnostics.Debug.WriteLine($"ORIENTATION: Will use PORTRAIT mode for duplicated 4x6 from 2x6 (ensures proper 2-inch cuts)");
                    }
                    else
                    {
                        // For original 2x6 strips, use portrait
                        desiredLandscapeOrientation = false;
                        System.Diagnostics.Debug.WriteLine($"ORIENTATION: Will use PORTRAIT mode for original 2x6 strip");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ORIENTATION: Will use default landscape setting: {desiredLandscapeOrientation}");
                }
                
                // Determine printer based on image size and pooling
                // If isOriginal2x6Format is true, we know it's a 2x6 strip regardless of the actual image dimensions
                string printerName;
                bool isStripFormat = isOriginal2x6Format;
                
                System.Diagnostics.Debug.WriteLine($"=== PRINTER ROUTING: isOriginal2x6Format={isOriginal2x6Format}, AutoRoutePrinter={_settingsService.AutoRoutePrinter}");
                
                if (isOriginal2x6Format && _settingsService.AutoRoutePrinter)
                {
                    // Use the 2x6 printer for original 2x6 templates
                    string configured2x6Printer = _settingsService.Printer2x6Name;
                    System.Diagnostics.Debug.WriteLine($"=== 2x6 ROUTING: Configured 2x6 printer = '{configured2x6Printer}'");
                    
                    if (!string.IsNullOrEmpty(configured2x6Printer))
                    {
                        // Use the specifically configured 2x6 printer
                        printerName = configured2x6Printer;
                        System.Diagnostics.Debug.WriteLine($"✅ SELECTED 2x6 PRINTER: {printerName}");
                    }
                    else
                    {
                        // No specific 2x6 printer configured - force strip format detection for pooling/routing
                        System.Diagnostics.Debug.WriteLine("⚠️ No 2x6 printer configured - forcing strip format for proper routing");
                        isStripFormat = true;
                        
                        // Use default printer but ensure it's treated as a strip format
                        printerName = _settingsService.PrinterName;
                        System.Diagnostics.Debug.WriteLine($"Using default printer with strip format for 2x6 template: {printerName}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ NOT using 2x6 routing - isOriginal2x6Format={isOriginal2x6Format}, AutoRoutePrinter={_settingsService.AutoRoutePrinter}");
                    
                    // Determine printer based on actual image size
                    printerName = DeterminePrinterByImageSize(photoPaths);
                    
                    // Check if it's actually a strip format (if not already determined)
                    if (!isOriginal2x6Format && photoPaths != null && photoPaths.Count > 0)
                    {
                        try
                        {
                            using (var image = System.Drawing.Image.FromFile(photoPaths[0]))
                            {
                                float aspectRatio = (float)image.Width / image.Height;
                                isStripFormat = aspectRatio < 0.5f;
                            }
                        }
                        catch { }
                    }
                }
                
                // Get pooled printer if pooling is enabled (but don't override if we already have a 2x6 printer selected)
                var poolManager = PrinterPoolManager.Instance;
                string pooledPrinter = poolManager.GetPooledPrinter(isStripFormat);
                if (!string.IsNullOrEmpty(pooledPrinter))
                {
                    // Only use pooled printer if this is NOT a 2x6 format with auto-routing enabled
                    // We preserve 2x6 routing even when no specific 2x6 printer is configured
                    if (!(isOriginal2x6Format && _settingsService.AutoRoutePrinter))
                    {
                        System.Diagnostics.Debug.WriteLine($"Using pooled printer: {pooledPrinter}");
                        printerName = pooledPrinter;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Keeping 2x6 printer selection, not using pooled printer");
                    }
                }
                
                // CRITICAL: Set the printer FIRST before applying settings
                if (!string.IsNullOrEmpty(printerName))
                {
                    printDocument.PrinterSettings.PrinterName = printerName;
                }
                
                // Apply saved printer driver settings (including DNP 2-inch cut)
                if (!string.IsNullOrEmpty(printerName) && printDocument.PrinterSettings.IsValid)
                {
                    // Determine which DEVMODE to use based on image format
                    string savedDriverSettings = "";
                    
                    // Check if we're using auto-routing and have format-specific settings
                    if (_settingsService.AutoRoutePrinter)
                    {
                        // Use the isOriginal2x6Format flag if provided, otherwise check actual image
                        bool is2x6Format = isOriginal2x6Format;
                        
                        // Only check actual image if we don't have the format flag
                        if (!isOriginal2x6Format && photoPaths != null && photoPaths.Count > 0)
                        {
                            try
                            {
                                using (var image = System.Drawing.Image.FromFile(photoPaths[0]))
                                {
                                    float aspectRatio = (float)image.Width / image.Height;
                                    is2x6Format = aspectRatio < 0.5f;
                                }
                            }
                            catch { }
                        }
                        
                        // Use appropriate DEVMODE for the format
                        if (is2x6Format)
                        {
                            savedDriverSettings = _settingsService.Printer2x6DriverSettings;
                            System.Diagnostics.Debug.WriteLine($"DEVMODE: Selected 2x6 printer DEVMODE settings (isOriginal2x6Format={isOriginal2x6Format})");
                            System.Diagnostics.Debug.WriteLine($"DEVMODE: 2x6 settings length: {savedDriverSettings?.Length ?? 0} bytes");
                        }
                        else
                        {
                            savedDriverSettings = _settingsService.Printer4x6DriverSettings;
                            System.Diagnostics.Debug.WriteLine($"DEVMODE: Selected 4x6 printer DEVMODE settings (isOriginal2x6Format={isOriginal2x6Format})");
                            System.Diagnostics.Debug.WriteLine($"DEVMODE: 4x6 settings length: {savedDriverSettings?.Length ?? 0} bytes");
                        }
                    }
                    
                    // Fallback to legacy single DEVMODE if format-specific not available
                    if (string.IsNullOrEmpty(savedDriverSettings))
                    {
                        savedDriverSettings = _settingsService.PrinterDriverSettings;
                        System.Diagnostics.Debug.WriteLine("Using legacy printer DEVMODE settings");
                    }
                    
                    // Apply the DEVMODE if available
                    if (!string.IsNullOrEmpty(savedDriverSettings))
                    {
                        System.Diagnostics.Debug.WriteLine($"Applying saved driver settings to printer: {printerName}");
                        
                        // CRITICAL FIX: Apply DEVMODE directly to the PrintDocument
                        bool devmodeApplied = false;
                        
                        // Check if it's raw Base64 DEVMODE data or JSON settings
                        if (savedDriverSettings.StartsWith("{") || savedDriverSettings.StartsWith("["))
                        {
                            // It's JSON - extract RawDriverSettings if available
                            try 
                            {
                                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(savedDriverSettings);
                                if (settings.RawDriverSettings != null)
                                {
                                    devmodeApplied = Pages.PhotoboothSettingsControl.ApplyDevModeToPrintDocument(
                                        printDocument, settings.RawDriverSettings.ToString());
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            // It's raw Base64 DEVMODE - apply directly to PrintDocument
                            devmodeApplied = Pages.PhotoboothSettingsControl.ApplyDevModeToPrintDocument(
                                printDocument, savedDriverSettings);
                        }
                        
                        if (!devmodeApplied)
                        {
                            // Fallback to the old method
                            Pages.PhotoboothSettingsControl.RestoreRawDriverSettings(printerName, savedDriverSettings);
                        }
                    }
                    
                    // Additionally, if DNP 2-inch cut is enabled, set appropriate paper size
                    if (printerName.ToLower().Contains("dnp") && _settingsService.Dnp2InchCut)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ensuring DNP 2-inch cut is enabled for printer: {printerName}");
                        
                        // Set paper size to 2x6 when using 2-inch cut
                        foreach (System.Drawing.Printing.PaperSize size in printDocument.PrinterSettings.PaperSizes)
                        {
                            // Look for 2x6 paper size (PC_2x6, 2x6, or dimensions close to 2x6 inches)
                            if (size.PaperName.Contains("2x6") || size.PaperName.Contains("PC_2x6") ||
                                (size.Width >= 190 && size.Width <= 210 && size.Height >= 590 && size.Height <= 610))
                            {
                                printDocument.DefaultPageSettings.PaperSize = size;
                                System.Diagnostics.Debug.WriteLine($"Set paper size to: {size.PaperName} ({size.Width}x{size.Height})");
                                break;
                            }
                        }
                    }
                }
                
                // Fallback handling if printer is not valid
                if (!string.IsNullOrEmpty(printerName) && !printDocument.PrinterSettings.IsValid)
                {
                    // Auto-select USB printer if configured printer is not available
                    string fallbackPrinter = AutoSelectUSBPrinter();
                    printDocument.PrinterSettings.PrinterName = fallbackPrinter;
                    
                    // Update settings with new selection
                    _settingsService.PrinterName = fallbackPrinter;
                    _settingsService.SaveSettings();
                    System.Diagnostics.Debug.WriteLine($"Printer not available, switched to: {fallbackPrinter}");
                }
                else if (string.IsNullOrEmpty(printerName))
                {
                    // No printer configured, auto-select USB printer
                    string autoSelectedPrinter = AutoSelectUSBPrinter();
                    printDocument.PrinterSettings.PrinterName = autoSelectedPrinter;
                    _settingsService.PrinterName = autoSelectedPrinter;
                    _settingsService.SaveSettings();
                    System.Diagnostics.Debug.WriteLine($"No printer configured, auto-selected: {autoSelectedPrinter}");
                }
                
                // Set paper size if configured
                string paperSize = _settingsService.PrintPaperSize;
                if (!string.IsNullOrEmpty(paperSize))
                {
                    foreach (PaperSize size in printDocument.PrinterSettings.PaperSizes)
                    {
                        if (size.PaperName.Contains(paperSize) || size.Kind.ToString().Contains(paperSize.Replace("x", "")))
                        {
                            printDocument.DefaultPageSettings.PaperSize = size;
                            break;
                        }
                    }
                }
                
                // Set print quality
                int printQuality = Properties.Settings.Default.PrintQuality;
                printDocument.DefaultPageSettings.PrinterResolution = new PrinterResolution 
                { 
                    X = printQuality, 
                    Y = printQuality 
                };
                
                // Set margins
                string marginStr = Properties.Settings.Default.PrintMargins;
                if (!string.IsNullOrEmpty(marginStr))
                {
                    var marginParts = marginStr.Split(',');
                    if (marginParts.Length == 4)
                    {
                        int left = int.Parse(marginParts[0]);
                        int top = int.Parse(marginParts[1]);
                        int right = int.Parse(marginParts[2]);
                        int bottom = int.Parse(marginParts[3]);
                        printDocument.DefaultPageSettings.Margins = new Margins(left, right, top, bottom);
                    }
                }
                
                // CRITICAL: Apply landscape orientation AFTER all DEVMODE settings
                // This must be done last as DEVMODE can override orientation
                printDocument.DefaultPageSettings.Landscape = desiredLandscapeOrientation;
                System.Diagnostics.Debug.WriteLine($"ORIENTATION FINAL: Set landscape to {desiredLandscapeOrientation} (AFTER DEVMODE)");
                
                // CRITICAL 2x6 FIX: Override orientation for 2x6 templates after DEVMODE
                if (isOriginal2x6Format)
                {
                    printDocument.DefaultPageSettings.Landscape = true; // Force landscape for 2x6 strips
                    System.Diagnostics.Debug.WriteLine($"🎯 2x6 OVERRIDE: Forced landscape=true for 2x6 template (was {desiredLandscapeOrientation})");
                }
                
                // Also ensure it's set on the printer settings
                if (printDocument.PrinterSettings != null && printDocument.PrinterSettings.DefaultPageSettings != null)
                {
                    bool finalLandscapeOrientation = isOriginal2x6Format ? true : desiredLandscapeOrientation;
                    printDocument.PrinterSettings.DefaultPageSettings.Landscape = finalLandscapeOrientation;
                    System.Diagnostics.Debug.WriteLine($"ORIENTATION: Also set printer settings landscape to {finalLandscapeOrientation}");
                }
                
                // Log the actual page settings that will be used
                System.Diagnostics.Debug.WriteLine($"ORIENTATION CHECK: DefaultPageSettings.Landscape = {printDocument.DefaultPageSettings.Landscape}");
                System.Diagnostics.Debug.WriteLine($"ORIENTATION CHECK: Page bounds = {printDocument.DefaultPageSettings.Bounds.Width}x{printDocument.DefaultPageSettings.Bounds.Height}");
                System.Diagnostics.Debug.WriteLine($"ORIENTATION CHECK: Printable area = {printDocument.DefaultPageSettings.PrintableArea.Width}x{printDocument.DefaultPageSettings.PrintableArea.Height}");
                
                // Show print dialog if configured
                if (Properties.Settings.Default.ShowPrintDialog)
                {
                    var printDialog = new System.Windows.Forms.PrintDialog();
                    printDialog.Document = printDocument;
                    printDialog.AllowSelection = false;
                    printDialog.AllowSomePages = false;
                    if (printDialog.ShowDialog() != DialogResult.OK)
                    {
                        result.Success = false;
                        result.Message = "Print cancelled by user.";
                        return result;
                    }
                }
                
                // Print the documents
                printDocument.Print();
                
                // Record print history
                RecordPrintHistory(photoPaths, sessionId, copies);
                
                result.Success = true;
                result.Message = $"Successfully printed {totalRequestedPrints} photo(s).";
                result.PrintedCount = totalRequestedPrints;
                result.RemainingSessionPrints = GetRemainingSessionPrints(sessionId);
                result.RemainingEventPrints = GetRemainingEventPrints();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Print error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Print error: {ex}");
            }
            
            return result;
        }

        private void RecordPrintHistory(List<string> photoPaths, string sessionId, int copies)
        {
            try
            {
                using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
                {
                    connection.Open();
                    
                    foreach (var photoPath in photoPaths)
                    {
                        // Check if this is a reprint
                        bool isReprint = false;
                        string checkQuery = "SELECT COUNT(*) FROM PrintHistory WHERE SessionId = @sessionId AND PhotoPath = @photoPath";
                        using (var checkCommand = new SQLiteCommand(checkQuery, connection))
                        {
                            checkCommand.Parameters.AddWithValue("@sessionId", sessionId);
                            checkCommand.Parameters.AddWithValue("@photoPath", photoPath);
                            isReprint = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
                        }
                        
                        // Insert print record
                        string insertQuery = @"
                            INSERT INTO PrintHistory (SessionId, PhotoPath, PrintCount, LastPrintTime, IsReprint)
                            VALUES (@sessionId, @photoPath, @printCount, @printTime, @isReprint)";
                        
                        using (var command = new SQLiteCommand(insertQuery, connection))
                        {
                            command.Parameters.AddWithValue("@sessionId", sessionId);
                            command.Parameters.AddWithValue("@photoPath", photoPath);
                            command.Parameters.AddWithValue("@printCount", copies);
                            command.Parameters.AddWithValue("@printTime", DateTime.Now);
                            command.Parameters.AddWithValue("@isReprint", isReprint ? 1 : 0);
                            command.ExecuteNonQuery();
                        }
                        
                        // Update session tracking
                        if (!sessionPrintTracking.ContainsKey(sessionId))
                        {
                            sessionPrintTracking[sessionId] = new PrintSessionInfo();
                        }
                        
                        if (!sessionPrintTracking[sessionId].PhotoPrintCounts.ContainsKey(photoPath))
                        {
                            sessionPrintTracking[sessionId].PhotoPrintCounts[photoPath] = 0;
                        }
                        
                        sessionPrintTracking[sessionId].PhotoPrintCounts[photoPath] += copies;
                        sessionPrintTracking[sessionId].TotalSessionPrints += copies;
                    }
                    
                    // Update event total
                    totalEventPrints += photoPaths.Count * copies;
                    
                    string updateEventQuery = @"
                        INSERT OR REPLACE INTO EventPrintSummary (EventDate, TotalPrints, LastUpdated)
                        VALUES (DATE('now'), @totalPrints, CURRENT_TIMESTAMP)";
                    
                    using (var command = new SQLiteCommand(updateEventQuery, connection))
                    {
                        command.Parameters.AddWithValue("@totalPrints", totalEventPrints);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error recording print history: {ex.Message}");
            }
        }

        private string DeterminePrinterByImageSize(List<string> photoPaths)
        {
            // Check if auto-routing is enabled
            if (!_settingsService.AutoRoutePrinter)
            {
                // Use legacy single printer selection
                return _settingsService.PrinterName;
            }
            
            // Get the first image to determine size
            if (photoPaths == null || photoPaths.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No photos to determine printer");
                return _settingsService.PrinterName;
            }
            
            try
            {
                string firstPhoto = photoPaths[0];
                using (var image = System.Drawing.Image.FromFile(firstPhoto))
                {
                    float aspectRatio = (float)image.Width / image.Height;
                    
                    // Determine if this is a photo strip (2x6) or standard photo format
                    // 2x6 strips have aspect ratio around 0.33 (2/6)
                    // 4x6 photos have aspect ratio around 0.67 (4/6)
                    // 5x7 photos have aspect ratio around 0.71 (5/7)
                    // 8x10 photos have aspect ratio around 0.80 (8/10)
                    // We'll use 0.5 as the threshold to detect strips
                    
                    bool isPhotoStrip = aspectRatio < 0.5f;
                    
                    System.Diagnostics.Debug.WriteLine($"Image dimensions: {image.Width}x{image.Height}, Aspect ratio: {aspectRatio:F2}");
                    
                    if (isPhotoStrip)
                    {
                        string stripPrinter = _settingsService.Printer2x6Name;
                        System.Diagnostics.Debug.WriteLine($"Detected photo strip format - using strip printer: {stripPrinter}");
                        
                        // Ensure 2-inch cut is enabled for strip prints
                        _settingsService.Dnp2InchCut = true;
                        
                        return stripPrinter;
                    }
                    else
                    {
                        string defaultPrinter = Properties.Settings.Default.Printer4x6Name;
                        
                        // Try to detect specific standard sizes
                        string detectedSize = "standard photo";
                        if (aspectRatio >= 0.65f && aspectRatio <= 0.69f)
                            detectedSize = "4x6";
                        else if (aspectRatio >= 0.70f && aspectRatio <= 0.72f)
                            detectedSize = "5x7";
                        else if (aspectRatio >= 0.79f && aspectRatio <= 0.81f)
                            detectedSize = "8x10";
                        else if (aspectRatio >= 0.99f && aspectRatio <= 1.01f)
                            detectedSize = "square";
                        
                        System.Diagnostics.Debug.WriteLine($"Detected {detectedSize} format - using default printer: {defaultPrinter}");
                        
                        // Disable 2-inch cut for standard photos
                        _settingsService.Dnp2InchCut = false;
                        
                        return defaultPrinter;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error determining image size: {ex.Message}");
                // Fallback to default printer
                return _settingsService.PrinterName;
            }
        }
        
        private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
        {
            if (currentPrintIndex < imagesToPrint.Count)
            {
                try
                {
                    using (var originalImage = Image.FromFile(imagesToPrint[currentPrintIndex]))
                    {
                        // Create a working copy of the image for potential rotation
                        Image imageToprint = originalImage;
                        bool disposeRotatedImage = false;
                        
                        // Get image aspect ratio for logging
                        float aspectRatio = (float)originalImage.Width / originalImage.Height;
                        System.Diagnostics.Debug.WriteLine($"ORIENTATION: Image aspect ratio: {aspectRatio:F3}");
                        
                        // SIMPLIFIED ORIENTATION MATCHING LOGIC
                        // Match template orientation with actual printer orientation
                        
                        // Determine actual printer orientation from page bounds
                        // This is more reliable than PageSettings.Landscape which might not reflect DEVMODE correctly
                        bool actualPrinterIsLandscape = e.PageBounds.Width > e.PageBounds.Height;
                        bool devmodeIsLandscape = e.PageSettings.Landscape;
                        bool imageIsLandscape = originalImage.Width > originalImage.Height;
                        
                        // Use actual page bounds orientation as it's most reliable
                        bool printerIsLandscape = actualPrinterIsLandscape;
                        
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: Image dimensions: {originalImage.Width}x{originalImage.Height}");
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: Page bounds: {e.PageBounds.Width}x{e.PageBounds.Height}");
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: PageSettings.Landscape: {devmodeIsLandscape}");
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: Actual orientation from bounds: {(actualPrinterIsLandscape ? "Landscape" : "Portrait")}");
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: Template is landscape: {imageIsLandscape}");
                        
                        // Determine if rotation is needed
                        bool needsRotation = false;
                        
                        // SIMPLE RULE: If template orientation doesn't match printer DEVMODE orientation, rotate
                        // This works for ALL paper sizes (4x6, 5x7, 8x10, etc.)
                        if (printerIsLandscape != imageIsLandscape)
                        {
                            needsRotation = true;
                            System.Diagnostics.Debug.WriteLine($"ORIENTATION MISMATCH: Template={(imageIsLandscape ? "Landscape" : "Portrait")}, DEVMODE={(printerIsLandscape ? "Landscape" : "Portrait")} - WILL ROTATE");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"ORIENTATION MATCH: Both are {(printerIsLandscape ? "Landscape" : "Portrait")} - NO ROTATION NEEDED");
                        }
                        
                        // Apply rotation if needed
                        if (needsRotation)
                        {
                            // Create a new bitmap for rotation
                            var rotatedBitmap = new Bitmap(originalImage.Height, originalImage.Width);
                            rotatedBitmap.SetResolution(originalImage.HorizontalResolution, originalImage.VerticalResolution);
                            
                            using (var g = Graphics.FromImage(rotatedBitmap))
                            {
                                g.TranslateTransform(rotatedBitmap.Width / 2, rotatedBitmap.Height / 2);
                                g.RotateTransform(90);
                                g.TranslateTransform(-originalImage.Width / 2, -originalImage.Height / 2);
                                g.DrawImage(originalImage, 0, 0, originalImage.Width, originalImage.Height);
                            }
                            
                            imageToprint = rotatedBitmap;
                            disposeRotatedImage = true;
                            System.Diagnostics.Debug.WriteLine($"Image rotated 90 degrees: New dimensions {imageToprint.Width}x{imageToprint.Height}");
                        }
                        
                        // Apply alignment adjustments based on printer type
                        double alignmentScaleX = 1.0;
                        double alignmentScaleY = 1.0;
                        int alignmentOffsetX = 0;
                        int alignmentOffsetY = 0;
                        
                        // Determine which alignment settings to use
                        bool is2x6Printer = false;
                        string currentPrinter = printDocument.PrinterSettings.PrinterName;
                        if (currentPrinter == _settingsService.Printer2x6Name && 
                            !string.IsNullOrEmpty(_settingsService.Printer2x6Name))
                        {
                            // Use 2x6 printer alignment settings (for 4x6 output)
                            alignmentScaleX = Properties.Settings.Default.Printer2x6ScaleX;
                            alignmentScaleY = Properties.Settings.Default.Printer2x6ScaleY;
                            alignmentOffsetX = Properties.Settings.Default.Printer2x6OffsetX;
                            alignmentOffsetY = Properties.Settings.Default.Printer2x6OffsetY;
                            is2x6Printer = true;
                            System.Diagnostics.Debug.WriteLine($"ALIGNMENT: Using 2x6 printer settings - ScaleX:{alignmentScaleX:F2}, ScaleY:{alignmentScaleY:F2}, OffsetX:{alignmentOffsetX}, OffsetY:{alignmentOffsetY}");
                        }
                        else
                        {
                            // Use default printer alignment settings
                            alignmentScaleX = Properties.Settings.Default.DefaultPrinterScaleX;
                            alignmentScaleY = Properties.Settings.Default.DefaultPrinterScaleY;
                            alignmentOffsetX = Properties.Settings.Default.DefaultPrinterOffsetX;
                            alignmentOffsetY = Properties.Settings.Default.DefaultPrinterOffsetY;
                            System.Diagnostics.Debug.WriteLine($"ALIGNMENT: Using default printer settings - ScaleX:{alignmentScaleX:F2}, ScaleY:{alignmentScaleY:F2}, OffsetX:{alignmentOffsetX}, OffsetY:{alignmentOffsetY}");
                        }
                        
                        // Calculate scaling to fit page with alignment adjustments
                        float baseScaleX = e.PageBounds.Width / (float)imageToprint.Width;
                        float baseScaleY = e.PageBounds.Height / (float)imageToprint.Height;
                        float baseScale = Math.Min(baseScaleX, baseScaleY);
                        
                        // Apply alignment scale adjustments
                        float scaleX = baseScale * (float)alignmentScaleX;
                        float scaleY = baseScale * (float)alignmentScaleY;
                        
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: Final image to print: {imageToprint.Width}x{imageToprint.Height}");
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: Base scale: {baseScale:F3}, Adjusted scales - X:{scaleX:F3}, Y:{scaleY:F3}");
                        
                        int scaledWidth = (int)(imageToprint.Width * scaleX);
                        int scaledHeight = (int)(imageToprint.Height * scaleY);
                        
                        // Center the image on the page with alignment offset adjustments
                        int x = (e.PageBounds.Width - scaledWidth) / 2 + alignmentOffsetX;
                        int y = (e.PageBounds.Height - scaledHeight) / 2 + alignmentOffsetY;
                        
                        System.Diagnostics.Debug.WriteLine($"PrintDocument_PrintPage: Position - X:{x}, Y:{y}, Width:{scaledWidth}, Height:{scaledHeight}");
                        
                        // Set high quality rendering
                        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        
                        e.Graphics.DrawImage(imageToprint, x, y, scaledWidth, scaledHeight);
                        
                        // Dispose rotated image if we created one
                        if (disposeRotatedImage)
                        {
                            imageToprint.Dispose();
                        }
                    }
                    
                    currentPrintIndex++;
                    e.HasMorePages = currentPrintIndex < imagesToPrint.Count;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error printing page: {ex.Message}");
                    e.HasMorePages = false;
                }
            }
            else
            {
                e.HasMorePages = false;
            }
        }

        public void ResetDailyLimits()
        {
            sessionPrintTracking.Clear();
            totalEventPrints = 0;
            
            try
            {
                using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
                {
                    connection.Open();
                    
                    // Reset event counter for new day
                    string resetQuery = "DELETE FROM EventPrintSummary WHERE EventDate < DATE('now')";
                    using (var command = new SQLiteCommand(resetQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting daily limits: {ex.Message}");
            }
        }

        public static List<string> GetAvailablePrinters()
        {
            var printers = new List<string>();
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                printers.Add(printer);
            }
            return printers;
        }

        public static string GetDefaultPrinter()
        {
            var settings = new PrinterSettings();
            return settings.PrinterName;
        }
        
        public string GetCurrentPrinterName()
        {
            // Get the currently configured printer name
            string printerName = _settingsService.PrinterName;
            
            // If no printer configured or invalid, get auto-selected USB printer
            if (string.IsNullOrEmpty(printerName) || !IsValidPrinter(printerName))
            {
                printerName = AutoSelectUSBPrinter();
            }
            
            return printerName;
        }
        
        public bool IsPrinterReady()
        {
            try
            {
                string printerName = GetCurrentPrinterName();
                if (string.IsNullOrEmpty(printerName))
                    return false;
                    
                return IsPrinterOnline(printerName);
            }
            catch
            {
                return false;
            }
        }
        
        private bool IsPrinterOnline(string printerName)
        {
            try
            {
                if (string.IsNullOrEmpty(printerName))
                    return false;
                
                // First check if printer is valid
                var settings = new PrinterSettings();
                settings.PrinterName = printerName;
                
                if (!settings.IsValid)
                    return false;
                
                // Check printer status using WMI
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name = '{printerName.Replace("\\", "\\\\")}'"))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        // Check PrinterStatus: 3 = Idle/Ready, 4 = Printing
                        int printerStatus = Convert.ToInt32(printer["PrinterStatus"] ?? 0);
                        bool isOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
                        
                        // Printer is online if it's not offline and status is idle (3) or printing (4)
                        return !isOffline && (printerStatus == 3 || printerStatus == 4 || printerStatus == 0);
                    }
                }
                
                // If we can't determine status via WMI, assume it's online if it's valid
                return settings.IsValid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking printer online status: {ex.Message}");
                // Fallback: if printer is valid, assume it's online
                try
                {
                    var settings = new PrinterSettings();
                    settings.PrinterName = printerName;
                    return settings.IsValid;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static List<PrinterInfo> GetUSBPrinters()
        {
            var usbPrinters = new List<PrinterInfo>();
            
            try
            {
                System.Diagnostics.Debug.WriteLine("Checking USB printers using WMI...");
                
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        try
                        {
                            // Get basic printer info
                            string printerName = printer["Name"]?.ToString() ?? "";
                            string portName = printer["PortName"]?.ToString() ?? "";
                            string driverName = printer["DriverName"]?.ToString() ?? "";
                            int printerStatus = Convert.ToInt32(printer["PrinterStatus"] ?? 0);
                            
                            if (string.IsNullOrEmpty(printerName)) continue;

                            // Check if it's a USB printer
                            bool isUSB = IsUSBPort(portName);
                            
                            if (isUSB)
                            {
                                bool isOnline = DeterminePrinterOnlineStatus(printerName);
                                string statusText = GetPrinterStatusText(printerStatus);
                                
                                var printerInfo = new PrinterInfo
                                {
                                    Name = printerName,
                                    PortName = portName,
                                    DriverName = driverName,
                                    Status = statusText,
                                    IsOnline = isOnline,
                                    IsUSB = true
                                };
                                
                                usbPrinters.Add(printerInfo);
                                System.Diagnostics.Debug.WriteLine($"USB Printer Found: {printerName}, Port: {portName}, Status: {statusText}, Online: {isOnline}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing printer: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WMI query failed: {ex.Message}");
                
                // Fallback: Try to get USB printers from installed printers list
                usbPrinters = GetUSBPrintersFallback();
            }
            
            return usbPrinters;
        }

        private static string GetPrinterStatusText(int statusCode)
        {
            switch (statusCode)
            {
                case 1: return "Other";
                case 2: return "Unknown";
                case 3: return "Idle";
                case 4: return "Printing";
                case 5: return "Warmup";
                case 6: return "Stopped";
                case 7: return "Offline";
                default: return "Ready";
            }
        }


        private static List<PrinterInfo> GetUSBPrintersFallback()
        {
            var usbPrinters = new List<PrinterInfo>();
            
            try
            {
                System.Diagnostics.Debug.WriteLine("Using fallback method to detect USB printers");
                
                foreach (string printerName in PrinterSettings.InstalledPrinters)
                {
                    try
                    {
                        var settings = new PrinterSettings();
                        settings.PrinterName = printerName;
                        
                        if (settings.IsValid)
                        {
                            // Try to determine if it's USB based on printer name patterns
                            bool isUSB = IsLikelyUSBPrinter(printerName);
                            
                            if (isUSB)
                            {
                                // Do a more thorough connectivity check
                                bool isActuallyOnline = DeterminePrinterOnlineStatus(printerName);
                                
                                var printerInfo = new PrinterInfo
                                {
                                    Name = printerName,
                                    PortName = "USB (Detected)", // Placeholder since we can't get actual port
                                    DriverName = "",
                                    Status = isActuallyOnline ? "Online" : "Offline",
                                    IsOnline = isActuallyOnline,
                                    IsUSB = true
                                };
                                
                                usbPrinters.Add(printerInfo);
                                System.Diagnostics.Debug.WriteLine($"Fallback detected USB printer: {printerName} (Online: {isActuallyOnline})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking printer {printerName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in fallback USB detection: {ex.Message}");
            }
            
            return usbPrinters;
        }

        private static bool IsLikelyUSBPrinter(string printerName)
        {
            if (string.IsNullOrEmpty(printerName))
                return false;

            var name = printerName.ToLower();
            
            // Exclude virtual/software printers
            if (name.Contains("microsoft") || name.Contains("pdf") || name.Contains("xps") ||
                name.Contains("onenote") || name.Contains("fax") || name.Contains("file") ||
                name.Contains("print to") || name.Contains("send to"))
                return false;

            // Exclude obvious network printers
            if (name.Contains("network") || name.Contains("ip") || name.Contains("wifi") ||
                name.Contains("wireless") || name.Contains("\\\\") || name.Contains("tcp"))
                return false;

            // Only include if it has strong USB/photo printer indicators
            bool hasStrongIndicator = name.Contains("usb") || name.Contains("dnp") || 
                                    name.Contains("selphy") || name.Contains("pictbridge");
            
            // Or if it's a known photo printer brand with local-style naming
            bool isPhotoprinter = (name.Contains("canon") || name.Contains("epson") || 
                                 name.Contains("kodak") || name.Contains("photo")) &&
                                 !name.Contains("series") && // Exclude generic series drivers
                                 !name.Contains("driver");   // Exclude generic drivers

            System.Diagnostics.Debug.WriteLine($"Printer {printerName} - StrongIndicator: {hasStrongIndicator}, PhotoPrinter: {isPhotoprinter}");
            
            return hasStrongIndicator || isPhotoprinter;
        }

        private static bool IsUSBPort(string portName)
        {
            if (string.IsNullOrEmpty(portName))
                return false;
                
            // Check for USB ports - using ToUpper for case insensitive comparison
            string upperPort = portName.ToUpper();
            return upperPort.Contains("USB") ||
                   upperPort.StartsWith("DOT4") ||
                   upperPort.StartsWith("USBPRINT");
        }

        private static bool DeterminePrinterOnlineStatus(string printerName)
        {
            try
            {
                // More thorough check for printer connectivity
                var settings = new PrinterSettings();
                settings.PrinterName = printerName;
                
                if (!settings.IsValid)
                {
                    System.Diagnostics.Debug.WriteLine($"Printer {printerName} settings invalid");
                    return false;
                }

                // Additional check: Try to access printer capabilities
                // This will fail if printer is not actually connected
                try
                {
                    var paperSizes = settings.PaperSizes;
                    var resolutions = settings.PrinterResolutions;
                    
                    // If we can access paper sizes and resolutions, printer is likely connected
                    bool hasCapabilities = paperSizes != null && paperSizes.Count > 0 && 
                                          resolutions != null && resolutions.Count > 0;
                    
                    System.Diagnostics.Debug.WriteLine($"Printer {printerName} - Valid: {settings.IsValid}, HasCapabilities: {hasCapabilities}, PaperSizes: {paperSizes?.Count}, Resolutions: {resolutions?.Count}");
                    return hasCapabilities;
                }
                catch (Exception capEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Printer {printerName} capabilities check failed: {capEx.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking printer {printerName} connectivity: {ex.Message}");
                return false;
            }
        }

        public static string AutoSelectUSBPrinter()
        {
            try
            {
                var usbPrinters = GetUSBPrinters();
                
                // Debug: Log all detected USB printers
                System.Diagnostics.Debug.WriteLine($"Found {usbPrinters.Count} USB printers:");
                foreach (var printer in usbPrinters)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {printer.Name} (Port: {printer.PortName}, Online: {printer.IsOnline})");
                }
                
                // Filter to exclude virtual/software printers
                var hardwareUSBPrinters = usbPrinters.Where(p => p.IsUSB && 
                    !p.Name.ToLower().Contains("microsoft") &&
                    !p.Name.ToLower().Contains("pdf") &&
                    !p.Name.ToLower().Contains("xps") &&
                    !p.Name.ToLower().Contains("onenote") &&
                    !p.Name.ToLower().Contains("fax")).ToList();
                
                System.Diagnostics.Debug.WriteLine($"Found {hardwareUSBPrinters.Count} hardware USB printers");
                
                if (!hardwareUSBPrinters.Any())
                {
                    System.Diagnostics.Debug.WriteLine("No hardware USB printers found, using system default");
                    return GetDefaultPrinter();
                }
                
                // Prioritize online printers, but don't exclude offline ones completely
                var onlineHardwareUSB = hardwareUSBPrinters.Where(p => p.IsOnline).ToList();
                var printersToSearch = onlineHardwareUSB.Any() ? onlineHardwareUSB : hardwareUSBPrinters;
                
                // Prioritize DNP printers for photobooth
                var dnpPrinter = printersToSearch.FirstOrDefault(p => 
                    p.Name.ToLower().Contains("dnp"));
                
                if (dnpPrinter != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-selected DNP printer: {dnpPrinter.Name} (Online: {dnpPrinter.IsOnline})");
                    return dnpPrinter.Name;
                }
                
                // Next priority: Photo printers
                var photoPrinter = printersToSearch.FirstOrDefault(p => 
                    p.Name.ToLower().Contains("photo") || 
                    p.Name.ToLower().Contains("selphy") ||
                    p.Name.ToLower().Contains("pictbridge") ||
                    p.Name.ToLower().Contains("canon") ||
                    p.Name.ToLower().Contains("epson") ||
                    p.Name.ToLower().Contains("kodak"));
                
                if (photoPrinter != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-selected photo printer: {photoPrinter.Name} (Online: {photoPrinter.IsOnline})");
                    return photoPrinter.Name;
                }
                
                // Fallback: First hardware USB printer (prefer online)
                var firstUSB = printersToSearch.First();
                System.Diagnostics.Debug.WriteLine($"Auto-selected first USB printer: {firstUSB.Name} (Online: {firstUSB.IsOnline})");
                return firstUSB.Name;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error auto-selecting USB printer: {ex.Message}");
                return GetDefaultPrinter();
            }
        }

        public static void RefreshPrinterSelection()
        {
            try
            {
                string currentPrinter = PrintSettingsService.Instance.PrinterName;
                string autoSelectedPrinter = AutoSelectUSBPrinter();
                
                // Auto-update if no printer is set or current printer is not available
                if (string.IsNullOrEmpty(currentPrinter) || !IsValidPrinter(currentPrinter))
                {
                    
                    PrintSettingsService.Instance.PrinterName = autoSelectedPrinter;
                    PrintSettingsService.Instance.SaveSettings();
                    System.Diagnostics.Debug.WriteLine($"Auto-updated printer to: {autoSelectedPrinter}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing printer selection: {ex.Message}");
            }
        }

        private static bool IsValidPrinter(string printerName)
        {
            if (string.IsNullOrEmpty(printerName))
                return false;
                
            try
            {
                var settings = new PrinterSettings();
                settings.PrinterName = printerName;
                return settings.IsValid;
            }
            catch
            {
                return false;
            }
        }

        public Dictionary<string, object> GetPrintStatistics()
        {
            var stats = new Dictionary<string, object>();
            stats["TotalEventPrints"] = totalEventPrints;
            stats["SessionCount"] = sessionPrintTracking.Count;
            stats["TotalSessions"] = sessionPrintTracking.Keys.ToList();
            
            var sessionStats = new List<Dictionary<string, object>>();
            foreach (var session in sessionPrintTracking)
            {
                var sessionInfo = new Dictionary<string, object>();
                sessionInfo["SessionId"] = session.Key;
                sessionInfo["TotalPrints"] = session.Value.TotalSessionPrints;
                sessionInfo["UniquePhotos"] = session.Value.PhotoPrintCounts.Count;
                sessionStats.Add(sessionInfo);
            }
            stats["SessionDetails"] = sessionStats;
            
            return stats;
        }
    }

    public class PrintSessionInfo
    {
        public Dictionary<string, int> PhotoPrintCounts { get; set; }
        public int TotalSessionPrints { get; set; }

        public PrintSessionInfo()
        {
            PhotoPrintCounts = new Dictionary<string, int>();
            TotalSessionPrints = 0;
        }
    }

    public class PrintResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int PrintedCount { get; set; }
        public int RemainingSessionPrints { get; set; }
        public int RemainingEventPrints { get; set; }
    }

    public class PrinterInfo
    {
        public string Name { get; set; }
        public string PortName { get; set; }
        public string DriverName { get; set; }
        public string Status { get; set; }
        public bool IsOnline { get; set; }
        public bool IsUSB { get; set; }
        
        public string DisplayText
        {
            get
            {
                var status = IsOnline ? "✓ Online" : "✗ Offline";
                var connection = IsUSB ? "USB" : "Network";
                return $"{Name} ({connection} - {status})";
            }
        }
    }
}