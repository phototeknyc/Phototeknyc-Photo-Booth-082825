using System;
using System.Drawing.Printing;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for managing print-related settings with automatic persistence
    /// Centralizes all print settings access and ensures proper saving
    /// </summary>
    public class PrintSettingsService
    {
        private static PrintSettingsService _instance;
        private static readonly object _lock = new object();

        public static PrintSettingsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PrintSettingsService();
                        }
                    }
                }
                return _instance;
            }
        }

        private PrintSettingsService()
        {
        }

        #region Print Limits
        
        /// <summary>
        /// Maximum prints allowed per session (0 = unlimited)
        /// </summary>
        public int MaxSessionPrints
        {
            get => Properties.Settings.Default.MaxSessionPrints;
            set
            {
                if (Properties.Settings.Default.MaxSessionPrints != value)
                {
                    Properties.Settings.Default.MaxSessionPrints = Math.Max(0, value);
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Maximum prints allowed per event/day (0 = unlimited)
        /// </summary>
        public int MaxEventPrints
        {
            get => Properties.Settings.Default.MaxEventPrints;
            set
            {
                if (Properties.Settings.Default.MaxEventPrints != value)
                {
                    Properties.Settings.Default.MaxEventPrints = Math.Max(0, value);
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Print Control Settings

        /// <summary>
        /// Whether printing functionality is enabled
        /// </summary>
        public bool EnablePrinting
        {
            get => Properties.Settings.Default.EnablePrinting;
            set
            {
                if (Properties.Settings.Default.EnablePrinting != value)
                {
                    Properties.Settings.Default.EnablePrinting = value;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Whether to show the print button in UI
        /// </summary>
        public bool ShowPrintButton
        {
            get => Properties.Settings.Default.ShowPrintButton;
            set
            {
                if (Properties.Settings.Default.ShowPrintButton != value)
                {
                    Properties.Settings.Default.ShowPrintButton = value;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Whether to allow reprinting of photos
        /// </summary>
        public bool AllowReprints
        {
            get => Properties.Settings.Default.AllowReprints;
            set
            {
                if (Properties.Settings.Default.AllowReprints != value)
                {
                    Properties.Settings.Default.AllowReprints = value;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Default number of copies to print
        /// </summary>
        public int DefaultPrintCopies
        {
            get => Properties.Settings.Default.DefaultPrintCopies;
            set
            {
                if (Properties.Settings.Default.DefaultPrintCopies != value)
                {
                    Properties.Settings.Default.DefaultPrintCopies = Math.Max(1, value);
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Printer Configuration

        /// <summary>
        /// Primary printer name
        /// </summary>
        public string PrinterName
        {
            get => Properties.Settings.Default.PrinterName ?? string.Empty;
            set
            {
                if (Properties.Settings.Default.PrinterName != value)
                {
                    Properties.Settings.Default.PrinterName = value ?? string.Empty;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// 2x6 strip printer name
        /// </summary>
        public string Printer2x6Name
        {
            get => Properties.Settings.Default.Printer2x6Name ?? string.Empty;
            set
            {
                if (Properties.Settings.Default.Printer2x6Name != value)
                {
                    Properties.Settings.Default.Printer2x6Name = value ?? string.Empty;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// 4x6 printer name
        /// </summary>
        public string Printer4x6Name
        {
            get => Properties.Settings.Default.Printer4x6Name ?? string.Empty;
            set
            {
                if (Properties.Settings.Default.Printer4x6Name != value)
                {
                    Properties.Settings.Default.Printer4x6Name = value ?? string.Empty;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Whether to auto-route prints based on image size
        /// </summary>
        public bool AutoRoutePrinter
        {
            get => Properties.Settings.Default.AutoRoutePrinter;
            set
            {
                if (Properties.Settings.Default.AutoRoutePrinter != value)
                {
                    Properties.Settings.Default.AutoRoutePrinter = value;
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Print Quality Settings

        /// <summary>
        /// Print paper size (e.g., "4x6", "2x6")
        /// </summary>
        public string PrintPaperSize
        {
            get => Properties.Settings.Default.PrintPaperSize ?? "4x6";
            set
            {
                if (Properties.Settings.Default.PrintPaperSize != value)
                {
                    Properties.Settings.Default.PrintPaperSize = value ?? "4x6";
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Print quality DPI
        /// </summary>
        public int PrintQuality
        {
            get => Properties.Settings.Default.PrintQuality;
            set
            {
                if (Properties.Settings.Default.PrintQuality != value)
                {
                    Properties.Settings.Default.PrintQuality = Math.Max(150, value);
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Whether to print in landscape orientation
        /// </summary>
        public bool PrintLandscape
        {
            get => Properties.Settings.Default.PrintLandscape;
            set
            {
                if (Properties.Settings.Default.PrintLandscape != value)
                {
                    Properties.Settings.Default.PrintLandscape = value;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// DNP 2 inch cut setting
        /// </summary>
        public bool Dnp2InchCut
        {
            get => Properties.Settings.Default.Dnp2InchCut;
            set
            {
                if (Properties.Settings.Default.Dnp2InchCut != value)
                {
                    Properties.Settings.Default.Dnp2InchCut = value;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Printer 4x6 driver settings
        /// </summary>
        public string Printer4x6DriverSettings
        {
            get => Properties.Settings.Default.Printer4x6DriverSettings ?? string.Empty;
            set
            {
                if (Properties.Settings.Default.Printer4x6DriverSettings != value)
                {
                    Properties.Settings.Default.Printer4x6DriverSettings = value ?? string.Empty;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Printer 2x6 driver settings
        /// </summary>
        public string Printer2x6DriverSettings
        {
            get => Properties.Settings.Default.Printer2x6DriverSettings ?? string.Empty;
            set
            {
                if (Properties.Settings.Default.Printer2x6DriverSettings != value)
                {
                    Properties.Settings.Default.Printer2x6DriverSettings = value ?? string.Empty;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Default printer driver settings
        /// </summary>
        public string PrinterDriverSettings
        {
            get => Properties.Settings.Default.PrinterDriverSettings ?? string.Empty;
            set
            {
                if (Properties.Settings.Default.PrinterDriverSettings != value)
                {
                    Properties.Settings.Default.PrinterDriverSettings = value ?? string.Empty;
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Print Modal Settings

        /// <summary>
        /// Whether to show the print copies selection modal
        /// </summary>
        public bool ShowPrintCopiesModal
        {
            get => Properties.Settings.Default.ShowPrintCopiesModal;
            set
            {
                if (Properties.Settings.Default.ShowPrintCopiesModal != value)
                {
                    Properties.Settings.Default.ShowPrintCopiesModal = value;
                    SaveSettings();
                }
            }
        }

        /// <summary>
        /// Maximum copies available in print selection modal
        /// </summary>
        public int MaxCopiesInModal
        {
            get => Properties.Settings.Default.MaxCopiesInModal;
            set
            {
                if (Properties.Settings.Default.MaxCopiesInModal != value)
                {
                    Properties.Settings.Default.MaxCopiesInModal = Math.Max(1, Math.Min(10, value));
                    SaveSettings();
                }
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Save settings to persistent storage
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrintSettingsService: Error saving settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if session print limit is unlimited
        /// </summary>
        public bool HasUnlimitedSessionPrints => MaxSessionPrints <= 0;

        /// <summary>
        /// Check if event print limit is unlimited
        /// </summary>
        public bool HasUnlimitedEventPrints => MaxEventPrints <= 0;

        /// <summary>
        /// Get all available printers
        /// </summary>
        public string[] GetAvailablePrinters()
        {
            try
            {
                var printers = new string[PrinterSettings.InstalledPrinters.Count];
                PrinterSettings.InstalledPrinters.CopyTo(printers, 0);
                return printers;
            }
            catch
            {
                return new string[0];
            }
        }

        /// <summary>
        /// Validate if a printer name exists
        /// </summary>
        public bool IsPrinterValid(string printerName)
        {
            if (string.IsNullOrEmpty(printerName))
                return false;

            try
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (string.Equals(printer, printerName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the appropriate printer for a given image type
        /// </summary>
        public string GetPrinterForImageType(bool is2x6Strip)
        {
            if (!AutoRoutePrinter)
                return PrinterName;

            string targetPrinter = is2x6Strip ? Printer2x6Name : Printer4x6Name;
            
            // Fall back to primary printer if specific printer not configured or invalid
            if (string.IsNullOrEmpty(targetPrinter) || !IsPrinterValid(targetPrinter))
                return PrinterName;

            return targetPrinter;
        }

        #endregion
    }
}
