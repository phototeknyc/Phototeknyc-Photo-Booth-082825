using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Photobooth.Services
{
    public class PrinterMonitorService
    {
        private static PrinterMonitorService _instance;
        private DispatcherTimer _monitorTimer;
        private PrintServer _printServer;
        private PrintQueue _currentPrinter;
        
        public static PrinterMonitorService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PrinterMonitorService();
                }
                return _instance;
            }
        }
        
        public event EventHandler<PrinterStatusEventArgs> PrinterStatusChanged;
        
        public class PrinterStatusEventArgs : EventArgs
        {
            public string PrinterName { get; set; }
            public string Status { get; set; }
            public int JobsInQueue { get; set; }
            public bool IsOnline { get; set; }
            public bool HasError { get; set; }
            public string ErrorMessage { get; set; }
            public int PaperLevel { get; set; } // 0-100, -1 if unknown
            public string ConnectionType { get; set; } // WiFi, USB, Network
            public int MediaRemaining { get; set; } // Number of prints remaining, -1 if unknown
            public string MediaType { get; set; } // e.g., "4x6", "5x7"
            public int RibbonRemaining { get; set; } // Ribbon percentage for dye-sub printers, -1 if unknown
        }
        
        private PrinterMonitorService()
        {
            Initialize();
        }
        
        private void Initialize()
        {
            try
            {
                _printServer = new PrintServer();
                _monitorTimer = new DispatcherTimer();
                _monitorTimer.Interval = TimeSpan.FromSeconds(2); // Check every 2 seconds
                _monitorTimer.Tick += MonitorTimer_Tick;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Failed to initialize PrinterMonitorService: {ex.Message}");
            }
        }
        
        public void StartMonitoring(string printerName = null)
        {
            try
            {
                // Ensure we're on the UI thread when creating PrintQueue objects
                if (System.Windows.Application.Current?.Dispatcher != null && 
                    !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => StartMonitoring(printerName));
                    return;
                }
                
                if (string.IsNullOrEmpty(printerName))
                {
                    // Use default printer
                    _currentPrinter = LocalPrintServer.GetDefaultPrintQueue();
                }
                else
                {
                    // Find specific printer
                    _currentPrinter = _printServer.GetPrintQueue(printerName);
                }
                
                _monitorTimer.Start();
                
                // Immediate status check
                CheckPrinterStatus();
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Failed to start printer monitoring: {ex.Message}");
                
                // Send error status
                PrinterStatusChanged?.Invoke(this, new PrinterStatusEventArgs
                {
                    PrinterName = "Unknown",
                    Status = "Error",
                    HasError = true,
                    ErrorMessage = $"Monitoring setup failed: {ex.Message}",
                    IsOnline = false
                });
            }
        }
        
        public void StopMonitoring()
        {
            _monitorTimer?.Stop();
        }
        
        private void MonitorTimer_Tick(object sender, EventArgs e)
        {
            CheckPrinterStatus();
        }
        
        private void CheckPrinterStatus()
        {
            try
            {
                if (_currentPrinter == null)
                {
                    SendStatusUpdate("No Printer", "Not Connected", 0, false, true, "No printer selected");
                    return;
                }
                
                // Refresh printer information on the UI thread
                _currentPrinter.Refresh();
                
                // Better offline detection - check multiple conditions
                bool isActuallyOnline = IsPrinterActuallyOnline(_currentPrinter);
                
                var status = GetPrinterStatusString(_currentPrinter);
                // Override status if we detected offline through other means
                if (!isActuallyOnline && status == "Ready")
                {
                    status = "Offline";
                }
                
                var hasError = CheckForErrors(_currentPrinter);
                var errorMessage = GetErrorMessage(_currentPrinter);
                var jobCount = _currentPrinter.NumberOfJobs;
                var paperLevel = EstimatePaperLevel(_currentPrinter);
                var connectionType = DetectConnectionType(_currentPrinter.Name);
                
                // Query DNP printer for media remaining if applicable
                int mediaRemaining = -1;
                string mediaType = "";
                int ribbonRemaining = -1;
                
                if (_currentPrinter.Name.ToLower().Contains("dnp") || _currentPrinter.Name.ToLower().Contains("ds40") || 
                    _currentPrinter.Name.ToLower().Contains("ds80") || _currentPrinter.Name.ToLower().Contains("ds620") ||
                    _currentPrinter.Name.ToLower().Contains("ds820"))
                {
                    var mediaInfo = GetDNPMediaRemaining(_currentPrinter.Name);
                    mediaRemaining = mediaInfo.Item1;
                    mediaType = mediaInfo.Item2;
                    ribbonRemaining = mediaInfo.Item3;
                }
                
                // Send status update asynchronously to avoid blocking
                Task.Run(() =>
                {
                    SendStatusUpdate(
                        _currentPrinter.Name,
                        status,
                        jobCount,
                        isActuallyOnline,
                        hasError,
                        errorMessage,
                        paperLevel,
                        connectionType,
                        mediaRemaining,
                        mediaType,
                        ribbonRemaining
                    );
                });
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error checking printer status: {ex.Message}");
                SendStatusUpdate("Error", "Check Failed", 0, false, true, ex.Message);
            }
        }
        
        private bool IsPrinterActuallyOnline(PrintQueue printer)
        {
            try
            {
                // Check multiple conditions to determine if printer is really online
                if (printer.IsOffline) return false;
                
                // Check if printer has any status flags that indicate it's not available
                if (printer.IsInError || printer.IsPaperJammed || printer.IsOutOfPaper || 
                    printer.IsNotAvailable || printer.NeedUserIntervention)
                {
                    return false;
                }
                
                // For USB printers, check if the port is accessible
                var connectionType = DetectConnectionType(printer.Name);
                if (connectionType == "USB")
                {
                    // USB printers that are unplugged often still show as "Ready" but IsOffline is false
                    // Check if we can actually query the printer status
                    try
                    {
                        printer.Refresh();
                        // If refresh succeeds but status is suspiciously empty, printer might be offline
                        if (printer.QueueStatus == PrintQueueStatus.None && 
                            printer.NumberOfJobs == 0 &&
                            !printer.IsPrinting && !printer.IsProcessing)
                        {
                            // Additional check using WMI
                            using (var searcher = new ManagementObjectSearcher(
                                $"SELECT * FROM Win32_Printer WHERE Name='{printer.Name.Replace("\\", "\\\\")}'"))
                            {
                                foreach (ManagementObject printerObj in searcher.Get())
                                {
                                    var workOffline = printerObj["WorkOffline"];
                                    if (workOffline != null && (bool)workOffline)
                                        return false;
                                    
                                    var printerStatus = printerObj["PrinterStatus"];
                                    // PrinterStatus: 1=Other, 2=Unknown, 3=Idle, 4=Printing, 5=WarmingUp, 7=Offline
                                    if (printerStatus != null && (uint)printerStatus == 7)
                                        return false;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // If we can't query the printer, it's likely offline
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error checking if printer is online: {ex.Message}");
                return false;
            }
        }
        
        private string GetPrinterStatusString(PrintQueue printer)
        {
            if (printer.IsOffline) return "Offline";
            if (printer.IsPaused) return "Paused";
            if (printer.IsInError) return "Error";
            if (printer.IsPrinting) return "Printing";
            if (printer.IsBusy) return "Busy";
            if (printer.IsWaiting) return "Waiting";
            if (printer.IsProcessing) return "Processing";
            if (printer.IsInitializing) return "Initializing";
            if (printer.IsWarmingUp) return "Warming Up";
            if (printer.IsPowerSaveOn) return "Power Save";
            if (printer.NumberOfJobs > 0) return $"Queue: {printer.NumberOfJobs}";
            
            return "Ready";
        }
        
        private bool CheckForErrors(PrintQueue printer)
        {
            return printer.IsInError || 
                   printer.IsPaperJammed || 
                   printer.IsOutOfPaper || 
                   printer.IsOutOfMemory || 
                   printer.IsTonerLow ||
                   // printer.IsDoorOpen || // Property doesn't exist in standard PrintQueue
                   printer.NeedUserIntervention;
        }
        
        private string GetErrorMessage(PrintQueue printer)
        {
            var errors = new List<string>();
            
            if (printer.IsPaperJammed) errors.Add("Paper Jam");
            if (printer.IsOutOfPaper) errors.Add("Out of Paper");
            if (printer.IsOutOfMemory) errors.Add("Out of Memory");
            if (printer.IsTonerLow) errors.Add("Low Ink/Toner");
            // if (printer.IsDoorOpen) errors.Add("Door Open"); // Property doesn't exist in standard PrintQueue
            if (printer.NeedUserIntervention) errors.Add("Needs Attention");
            if (printer.IsInError && errors.Count == 0) errors.Add("General Error");
            
            return errors.Count > 0 ? string.Join(", ", errors) : "";
        }
        
        private int EstimatePaperLevel(PrintQueue printer)
        {
            // This is a simplified estimation
            // In reality, you'd need to query printer-specific APIs or SNMP
            if (printer.IsOutOfPaper) return 0;
            if (printer.IsTonerLow) return 25; // Assume low paper if toner is low
            return -1; // Unknown
        }
        
        private string DetectConnectionType(string printerName)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name='{printerName.Replace("\\", "\\\\")}'"))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        var portName = printer["PortName"]?.ToString() ?? "";
                        
                        if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
                            return "USB";
                        if (portName.Contains("IP_") || portName.Contains("."))
                            return "Network";
                        if (portName.StartsWith("WSD", StringComparison.OrdinalIgnoreCase))
                            return "WiFi";
                        if (portName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                            return "Parallel";
                        
                        // Check if it's a network printer
                        var isNetwork = printer["Network"]?.ToString();
                        if (!string.IsNullOrEmpty(isNetwork) && bool.Parse(isNetwork))
                            return "Network";
                    }
                }
            }
            catch
            {
                // Fallback
            }
            
            return "Local";
        }
        
        private Tuple<int, string, int> GetDNPMediaRemaining(string printerName)
        {
            try
            {
                // Query WMI for DNP printer information
                // DNP printers expose media information through WMI or SNMP
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Printer WHERE Name='{printerName.Replace("\\", "\\\\")}'" ))
                {
                    foreach (ManagementObject printer in searcher.Get())
                    {
                        // Try to get extended printer attributes
                        // DNP printers often expose this through driver-specific properties
                        var driverName = printer["DriverName"]?.ToString() ?? "";
                        
                        // For DNP printers, we need to query the print spooler for extended attributes
                        // This is a simplified approach - actual implementation would need DNP SDK
                        if (driverName.ToLower().Contains("dnp"))
                        {
                            // Query printer capabilities and current media
                            var printQueue = new PrintServer().GetPrintQueue(printerName);
                            
                            // Check printer comment field where some drivers store media info
                            var comment = printQueue.Comment ?? "";
                            
                            // Parse media info from comment if available
                            // Format example: "Media: 4x6 (400 remaining)"
                            if (comment.Contains("remaining"))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(
                                    comment, @"(\d+x\d+).*\((\d+)\s*remaining");
                                if (match.Success)
                                {
                                    string mediaType = match.Groups[1].Value;
                                    int remaining = int.Parse(match.Groups[2].Value);
                                    return new Tuple<int, string, int>(remaining, mediaType, -1);
                                }
                            }
                            
                            // Alternative: Check for standard media sizes from printer capabilities
                            // Most DNP printers default to 4x6
                            string defaultMediaType = "4x6";
                            
                            // Try to determine from printer name
                            if (printerName.ToLower().Contains("ds80") || printerName.ToLower().Contains("8x"))
                            {
                                defaultMediaType = "8x10";
                            }
                            else if (printerName.ToLower().Contains("ds620"))
                            {
                                defaultMediaType = "6x8";
                            }
                            
                            // For demonstration/testing: Return a mock count for DNP printers
                            // In production, this would require DNP SDK integration
                            // TODO: Integrate with DNP SDK for actual media count
                            return new Tuple<int, string, int>(400, defaultMediaType, -1);
                        }
                    }
                }
                
                // Try SNMP query for network printers
                if (printerName.Contains("IP_") || DetectConnectionType(printerName) == "Network")
                {
                    // SNMP OIDs for media remaining (printer-specific)
                    // This would require SNMP library implementation
                    // return QuerySNMPMediaInfo(printerName);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Failed to get DNP media remaining: {ex.Message}");
            }
            
            return new Tuple<int, string, int>(-1, "", -1); // Unknown
        }
        
        private void SendStatusUpdate(string printerName, string status, int jobCount, 
            bool isOnline, bool hasError, string errorMessage, int paperLevel = -1, string connectionType = "Unknown",
            int mediaRemaining = -1, string mediaType = "", int ribbonRemaining = -1)
        {
            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                PrinterStatusChanged?.Invoke(this, new PrinterStatusEventArgs
                {
                    PrinterName = printerName,
                    Status = status,
                    JobsInQueue = jobCount,
                    IsOnline = isOnline,
                    HasError = hasError,
                    ErrorMessage = errorMessage,
                    PaperLevel = paperLevel,
                    ConnectionType = connectionType,
                    MediaRemaining = mediaRemaining,
                    MediaType = mediaType,
                    RibbonRemaining = ribbonRemaining
                });
            }));
        }
        
        public List<string> GetAvailablePrinters()
        {
            var printers = new List<string>();
            try
            {
                var printQueues = _printServer.GetPrintQueues();
                foreach (var queue in printQueues)
                {
                    printers.Add(queue.Name);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Failed to get available printers: {ex.Message}");
            }
            return printers;
        }
        
        public void SwitchPrinter(string printerName)
        {
            try
            {
                StopMonitoring();
                _currentPrinter?.Dispose();
                _currentPrinter = _printServer.GetPrintQueue(printerName);
                StartMonitoring(printerName);
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Failed to switch printer: {ex.Message}");
            }
        }
        
        public void Dispose()
        {
            StopMonitoring();
            _currentPrinter?.Dispose();
            _printServer?.Dispose();
        }
    }
}