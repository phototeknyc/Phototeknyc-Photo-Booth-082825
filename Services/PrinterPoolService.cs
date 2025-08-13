using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing.Printing;
using System.Threading;

namespace Photobooth.Services
{
    public enum PoolingStrategy
    {
        RoundRobin,      // Distribute evenly in sequence
        LoadBalance,     // Send to printer with shortest queue
        FailoverOnly     // Use additional printers only if primary fails
    }

    public class PrinterPool
    {
        private readonly object _lockObject = new object();
        private int _currentPrinterIndex = 0;
        private Dictionary<string, PrinterStatus> _printerStatuses = new Dictionary<string, PrinterStatus>();
        private Dictionary<string, int> _printerJobCounts = new Dictionary<string, int>();

        public string PoolName { get; set; }
        public List<string> Printers { get; set; }
        public PoolingStrategy Strategy { get; set; }
        public bool IsEnabled { get; set; }

        public PrinterPool(string name)
        {
            PoolName = name;
            Printers = new List<string>();
            Strategy = PoolingStrategy.RoundRobin;
            IsEnabled = false;
        }

        public string GetNextPrinter()
        {
            lock (_lockObject)
            {
                if (!IsEnabled || Printers == null || Printers.Count == 0)
                    return null;

                // Remove any empty printer names
                var validPrinters = Printers.Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (validPrinters.Count == 0)
                    return null;

                switch (Strategy)
                {
                    case PoolingStrategy.RoundRobin:
                        return GetNextPrinterRoundRobin(validPrinters);

                    case PoolingStrategy.LoadBalance:
                        return GetNextPrinterLoadBalance(validPrinters);

                    case PoolingStrategy.FailoverOnly:
                        return GetNextPrinterFailover(validPrinters);

                    default:
                        return validPrinters.FirstOrDefault();
                }
            }
        }

        private string GetNextPrinterRoundRobin(List<string> validPrinters)
        {
            // Simple round-robin: cycle through all printers
            string selectedPrinter = null;
            int attempts = 0;

            while (selectedPrinter == null && attempts < validPrinters.Count)
            {
                selectedPrinter = validPrinters[_currentPrinterIndex % validPrinters.Count];
                
                // Check if printer is available
                if (IsPrinterAvailable(selectedPrinter))
                {
                    _currentPrinterIndex++;
                    IncrementJobCount(selectedPrinter);
                    System.Diagnostics.Debug.WriteLine($"Round-robin selected: {selectedPrinter} (Index: {_currentPrinterIndex - 1})");
                    return selectedPrinter;
                }

                // Printer not available, try next
                _currentPrinterIndex++;
                attempts++;
                selectedPrinter = null;
            }

            // No available printers
            return validPrinters.FirstOrDefault(); // Return first as fallback
        }

        private string GetNextPrinterLoadBalance(List<string> validPrinters)
        {
            // Select printer with least number of jobs
            string bestPrinter = null;
            int minJobs = int.MaxValue;

            foreach (var printer in validPrinters)
            {
                if (!IsPrinterAvailable(printer))
                    continue;

                int jobCount = GetJobCount(printer);
                if (jobCount < minJobs)
                {
                    minJobs = jobCount;
                    bestPrinter = printer;
                }
            }

            if (bestPrinter != null)
            {
                IncrementJobCount(bestPrinter);
                System.Diagnostics.Debug.WriteLine($"Load balance selected: {bestPrinter} (Jobs: {minJobs})");
            }

            return bestPrinter ?? validPrinters.FirstOrDefault();
        }

        private string GetNextPrinterFailover(List<string> validPrinters)
        {
            // Always use first printer unless it's unavailable
            foreach (var printer in validPrinters)
            {
                if (IsPrinterAvailable(printer))
                {
                    IncrementJobCount(printer);
                    System.Diagnostics.Debug.WriteLine($"Failover selected: {printer}");
                    return printer;
                }
            }

            return validPrinters.FirstOrDefault();
        }

        private bool IsPrinterAvailable(string printerName)
        {
            try
            {
                using (var printDoc = new PrintDocument())
                {
                    printDoc.PrinterSettings.PrinterName = printerName;
                    return printDoc.PrinterSettings.IsValid && !printDoc.PrinterSettings.IsDefaultPrinter;
                }
            }
            catch
            {
                return false;
            }
        }

        private int GetJobCount(string printerName)
        {
            if (_printerJobCounts.ContainsKey(printerName))
                return _printerJobCounts[printerName];
            return 0;
        }

        private void IncrementJobCount(string printerName)
        {
            if (!_printerJobCounts.ContainsKey(printerName))
                _printerJobCounts[printerName] = 0;
            _printerJobCounts[printerName]++;
        }

        public void DecrementJobCount(string printerName)
        {
            lock (_lockObject)
            {
                if (_printerJobCounts.ContainsKey(printerName) && _printerJobCounts[printerName] > 0)
                    _printerJobCounts[printerName]--;
            }
        }

        public void ResetJobCounts()
        {
            lock (_lockObject)
            {
                _printerJobCounts.Clear();
            }
        }

        public Dictionary<string, int> GetJobCounts()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, int>(_printerJobCounts);
            }
        }
    }

    public class PrinterStatus
    {
        public string PrinterName { get; set; }
        public bool IsOnline { get; set; }
        public int QueueLength { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public class PrinterPoolManager
    {
        private static PrinterPoolManager _instance;
        private static readonly object _instanceLock = new object();

        public PrinterPool DefaultPrinterPool { get; private set; }
        public PrinterPool StripPrinterPool { get; private set; }

        public static PrinterPoolManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PrinterPoolManager();
                        }
                    }
                }
                return _instance;
            }
        }

        private PrinterPoolManager()
        {
            DefaultPrinterPool = new PrinterPool("Default");
            StripPrinterPool = new PrinterPool("Strip");
            LoadPoolSettings();
        }

        public void LoadPoolSettings()
        {
            // Load default printer pool
            DefaultPrinterPool.IsEnabled = Properties.Settings.Default.DefaultPoolEnabled;
            DefaultPrinterPool.Strategy = (PoolingStrategy)Properties.Settings.Default.DefaultPoolStrategy;
            
            var defaultPrinters = new List<string>();
            if (!string.IsNullOrEmpty(Properties.Settings.Default.Printer4x6Name))
                defaultPrinters.Add(Properties.Settings.Default.Printer4x6Name);
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DefaultPool2))
                defaultPrinters.Add(Properties.Settings.Default.DefaultPool2);
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DefaultPool3))
                defaultPrinters.Add(Properties.Settings.Default.DefaultPool3);
            if (!string.IsNullOrEmpty(Properties.Settings.Default.DefaultPool4))
                defaultPrinters.Add(Properties.Settings.Default.DefaultPool4);
            
            DefaultPrinterPool.Printers = defaultPrinters;

            // Load strip printer pool
            StripPrinterPool.IsEnabled = Properties.Settings.Default.StripPoolEnabled;
            StripPrinterPool.Strategy = (PoolingStrategy)Properties.Settings.Default.StripPoolStrategy;
            
            var stripPrinters = new List<string>();
            if (!string.IsNullOrEmpty(Properties.Settings.Default.Printer2x6Name))
                stripPrinters.Add(Properties.Settings.Default.Printer2x6Name);
            if (!string.IsNullOrEmpty(Properties.Settings.Default.StripPool2))
                stripPrinters.Add(Properties.Settings.Default.StripPool2);
            if (!string.IsNullOrEmpty(Properties.Settings.Default.StripPool3))
                stripPrinters.Add(Properties.Settings.Default.StripPool3);
            if (!string.IsNullOrEmpty(Properties.Settings.Default.StripPool4))
                stripPrinters.Add(Properties.Settings.Default.StripPool4);
            
            StripPrinterPool.Printers = stripPrinters;
        }

        public void SavePoolSettings()
        {
            // Save default printer pool
            Properties.Settings.Default.DefaultPoolEnabled = DefaultPrinterPool.IsEnabled;
            Properties.Settings.Default.DefaultPoolStrategy = (int)DefaultPrinterPool.Strategy;
            
            // Save strip printer pool
            Properties.Settings.Default.StripPoolEnabled = StripPrinterPool.IsEnabled;
            Properties.Settings.Default.StripPoolStrategy = (int)StripPrinterPool.Strategy;
            
            Properties.Settings.Default.Save();
        }

        public string GetPooledPrinter(bool isStrip)
        {
            var pool = isStrip ? StripPrinterPool : DefaultPrinterPool;
            
            if (pool.IsEnabled)
            {
                string pooledPrinter = pool.GetNextPrinter();
                if (!string.IsNullOrEmpty(pooledPrinter))
                {
                    System.Diagnostics.Debug.WriteLine($"Using pooled printer: {pooledPrinter} from {pool.PoolName} pool");
                    return pooledPrinter;
                }
            }

            // Fallback to single printer
            return isStrip ? 
                Properties.Settings.Default.Printer2x6Name : 
                Properties.Settings.Default.Printer4x6Name;
        }

        public void PrintCompleted(string printerName, bool isStrip)
        {
            var pool = isStrip ? StripPrinterPool : DefaultPrinterPool;
            pool.DecrementJobCount(printerName);
        }

        public Dictionary<string, int> GetPoolStatus(bool isStrip)
        {
            var pool = isStrip ? StripPrinterPool : DefaultPrinterPool;
            return pool.GetJobCounts();
        }
    }
}