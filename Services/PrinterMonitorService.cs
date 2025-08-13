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
                
                var status = GetPrinterStatusString(_currentPrinter);
                var isOnline = !_currentPrinter.IsOffline;
                var hasError = CheckForErrors(_currentPrinter);
                var errorMessage = GetErrorMessage(_currentPrinter);
                var jobCount = _currentPrinter.NumberOfJobs;
                var paperLevel = EstimatePaperLevel(_currentPrinter);
                var connectionType = DetectConnectionType(_currentPrinter.Name);
                
                // Send status update asynchronously to avoid blocking
                Task.Run(() =>
                {
                    SendStatusUpdate(
                        _currentPrinter.Name,
                        status,
                        jobCount,
                        isOnline,
                        hasError,
                        errorMessage,
                        paperLevel,
                        connectionType
                    );
                });
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error checking printer status: {ex.Message}");
                SendStatusUpdate("Error", "Check Failed", 0, false, true, ex.Message);
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
        
        private void SendStatusUpdate(string printerName, string status, int jobCount, 
            bool isOnline, bool hasError, string errorMessage, int paperLevel = -1, string connectionType = "Unknown")
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
                    ConnectionType = connectionType
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