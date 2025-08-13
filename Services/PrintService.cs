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
            if (!Properties.Settings.Default.EnablePrinting)
                return false;
            
            // Check event limit
            int maxEventPrints = Properties.Settings.Default.MaxEventPrints;
            if (maxEventPrints > 0 && totalEventPrints + requestedCopies > maxEventPrints)
                return false;
            
            // Check session limit
            int maxSessionPrints = Properties.Settings.Default.MaxSessionPrints;
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
            int maxSessionPrints = Properties.Settings.Default.MaxSessionPrints;
            if (maxSessionPrints <= 0)
                return int.MaxValue; // Unlimited
            
            int used = GetSessionPrintCount(sessionId);
            return Math.Max(0, maxSessionPrints - used);
        }

        public int GetRemainingEventPrints()
        {
            int maxEventPrints = Properties.Settings.Default.MaxEventPrints;
            if (maxEventPrints <= 0)
                return int.MaxValue; // Unlimited
            
            return Math.Max(0, maxEventPrints - totalEventPrints);
        }

        public PrintResult PrintPhotos(List<string> photoPaths, string sessionId, int copies = 1)
        {
            var result = new PrintResult();
            
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
                printDocument.DefaultPageSettings.Landscape = Properties.Settings.Default.PrintLandscape;
                
                // Determine printer based on image size and pooling
                string printerName = DeterminePrinterByImageSize(photoPaths);
                
                // Check if printer pooling is enabled and get pooled printer
                bool isStripFormat = false;
                if (photoPaths != null && photoPaths.Count > 0)
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
                
                // Get pooled printer if pooling is enabled
                var poolManager = PrinterPoolManager.Instance;
                string pooledPrinter = poolManager.GetPooledPrinter(isStripFormat);
                if (!string.IsNullOrEmpty(pooledPrinter))
                {
                    System.Diagnostics.Debug.WriteLine($"Using pooled printer: {pooledPrinter}");
                    printerName = pooledPrinter;
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
                    if (Properties.Settings.Default.AutoRoutePrinter)
                    {
                        // Determine format based on first image
                        bool is2x6Format = false;
                        if (photoPaths != null && photoPaths.Count > 0)
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
                            savedDriverSettings = Properties.Settings.Default.Printer2x6DriverSettings;
                            System.Diagnostics.Debug.WriteLine("Using 2x6 printer DEVMODE settings");
                        }
                        else
                        {
                            savedDriverSettings = Properties.Settings.Default.Printer4x6DriverSettings;
                            System.Diagnostics.Debug.WriteLine("Using 4x6 printer DEVMODE settings");
                        }
                    }
                    
                    // Fallback to legacy single DEVMODE if format-specific not available
                    if (string.IsNullOrEmpty(savedDriverSettings))
                    {
                        savedDriverSettings = Properties.Settings.Default.PrinterDriverSettings;
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
                    if (printerName.ToLower().Contains("dnp") && Properties.Settings.Default.Dnp2InchCut)
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
                    Properties.Settings.Default.PrinterName = fallbackPrinter;
                    Properties.Settings.Default.Save();
                    System.Diagnostics.Debug.WriteLine($"Printer not available, switched to: {fallbackPrinter}");
                }
                else if (string.IsNullOrEmpty(printerName))
                {
                    // No printer configured, auto-select USB printer
                    string autoSelectedPrinter = AutoSelectUSBPrinter();
                    printDocument.PrinterSettings.PrinterName = autoSelectedPrinter;
                    Properties.Settings.Default.PrinterName = autoSelectedPrinter;
                    Properties.Settings.Default.Save();
                    System.Diagnostics.Debug.WriteLine($"No printer configured, auto-selected: {autoSelectedPrinter}");
                }
                
                // Set paper size if configured
                string paperSize = Properties.Settings.Default.PrintPaperSize;
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
            if (!Properties.Settings.Default.AutoRoutePrinter)
            {
                // Use legacy single printer selection
                return Properties.Settings.Default.PrinterName;
            }
            
            // Get the first image to determine size
            if (photoPaths == null || photoPaths.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No photos to determine printer");
                return Properties.Settings.Default.PrinterName;
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
                        string stripPrinter = Properties.Settings.Default.Printer2x6Name;
                        System.Diagnostics.Debug.WriteLine($"Detected photo strip format - using strip printer: {stripPrinter}");
                        
                        // Ensure 2-inch cut is enabled for strip prints
                        Properties.Settings.Default.Dnp2InchCut = true;
                        
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
                        Properties.Settings.Default.Dnp2InchCut = false;
                        
                        return defaultPrinter;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error determining image size: {ex.Message}");
                // Fallback to default printer
                return Properties.Settings.Default.PrinterName;
            }
        }
        
        private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
        {
            if (currentPrintIndex < imagesToPrint.Count)
            {
                try
                {
                    using (var image = Image.FromFile(imagesToPrint[currentPrintIndex]))
                    {
                        // Calculate scaling to fit page
                        float scaleX = e.PageBounds.Width / (float)image.Width;
                        float scaleY = e.PageBounds.Height / (float)image.Height;
                        float scale = Math.Min(scaleX, scaleY);
                        
                        int scaledWidth = (int)(image.Width * scale);
                        int scaledHeight = (int)(image.Height * scale);
                        
                        // Center the image on the page
                        int x = (e.PageBounds.Width - scaledWidth) / 2;
                        int y = (e.PageBounds.Height - scaledHeight) / 2;
                        
                        e.Graphics.DrawImage(image, x, y, scaledWidth, scaledHeight);
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
                string currentPrinter = Properties.Settings.Default.PrinterName;
                string autoSelectedPrinter = AutoSelectUSBPrinter();
                
                // Auto-update if no printer is set or current printer is not available
                if (string.IsNullOrEmpty(currentPrinter) || !IsValidPrinter(currentPrinter))
                {
                    Properties.Settings.Default.PrinterName = autoSelectedPrinter;
                    Properties.Settings.Default.Save();
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