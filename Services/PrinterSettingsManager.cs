using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml.Serialization;
using Microsoft.Win32;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Manages persistent printer settings that survive application restarts
    /// Saves printer configurations including orientation, paper size, margins, etc.
    /// </summary>
    public class PrinterSettingsManager
    {
        private static PrinterSettingsManager _instance;
        public static PrinterSettingsManager Instance => _instance ?? (_instance = new PrinterSettingsManager());
        
        private readonly string settingsDirectory;
        private readonly string registryPath = @"SOFTWARE\Photobooth\PrinterSettings";
        
        public PrinterSettingsManager()
        {
            // Store settings in AppData
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            settingsDirectory = Path.Combine(appData, "Photobooth", "PrinterProfiles");
            Directory.CreateDirectory(settingsDirectory);
        }
        
        /// <summary>
        /// Printer profile containing all settings
        /// </summary>
        [Serializable]
        public class PrinterProfile
        {
            public string PrinterName { get; set; }
            public string ProfileName { get; set; }
            public string PaperSize { get; set; }
            public int PaperWidth { get; set; }  // in hundredths of an inch
            public int PaperHeight { get; set; } // in hundredths of an inch
            public bool Landscape { get; set; }
            public int Copies { get; set; }
            public int Quality { get; set; }  // Changed from PrinterResolutionKind enum to int
            public int ResolutionX { get; set; }
            public int ResolutionY { get; set; }
            public int Duplex { get; set; }  // Changed from DuplexMode enum to int
            public int PaperSource { get; set; }  // Changed from PaperSourceMode enum to int
            public int MarginLeft { get; set; }
            public int MarginTop { get; set; }
            public int MarginRight { get; set; }
            public int MarginBottom { get; set; }
            public string DevModeDataBase64 { get; set; } // Store raw DEVMODE as Base64 string for XML serialization
            public DateTime LastModified { get; set; }
            
            // Additional settings for our specific needs
            public bool SkipDevMode { get; set; } // Whether to skip DEVMODE loading
            public bool ForceOrientation { get; set; } // Force orientation regardless of image
            public string CustomPaperName { get; set; } // For custom paper sizes like "4x6"
            public bool Enable2InchCut { get; set; } // Enable 2 inch cut setting for photo strips
            
            // Default constructor to ensure all properties have valid defaults
            public PrinterProfile()
            {
                Copies = 1;
                Quality = 0;
                Duplex = 0;
                PaperSource = 0;
                SkipDevMode = false; // Apply DEVMODE by default to preserve 2 inch cut settings
                Enable2InchCut = false; // Default to disabled
                LastModified = DateTime.Now;
            }
        }
        
        public enum DuplexMode
        {
            Simplex = 1,
            Horizontal = 2,
            Vertical = 3
        }
        
        public enum PaperSourceMode
        {
            Auto = 0,
            Manual = 1,
            Tray1 = 2,
            Tray2 = 3,
            Tray3 = 4
        }
        
        /// <summary>
        /// Save current printer settings to a profile
        /// </summary>
        public bool SavePrinterProfile(PrintDocument printDoc, string profileName, bool enable2InchCut = false)
        {
            try
            {
                var profile = new PrinterProfile
                {
                    PrinterName = printDoc.PrinterSettings.PrinterName,
                    ProfileName = profileName,
                    PaperSize = printDoc.DefaultPageSettings.PaperSize.PaperName,
                    PaperWidth = printDoc.DefaultPageSettings.PaperSize.Width,
                    PaperHeight = printDoc.DefaultPageSettings.PaperSize.Height,
                    Landscape = printDoc.DefaultPageSettings.Landscape,
                    Copies = printDoc.PrinterSettings.Copies,
                    Enable2InchCut = enable2InchCut,
                    MarginLeft = printDoc.DefaultPageSettings.Margins.Left,
                    MarginTop = printDoc.DefaultPageSettings.Margins.Top,
                    MarginRight = printDoc.DefaultPageSettings.Margins.Right,
                    MarginBottom = printDoc.DefaultPageSettings.Margins.Bottom,
                    LastModified = DateTime.Now
                };
                
                // Get resolution settings
                if (printDoc.DefaultPageSettings.PrinterResolution != null)
                {
                    profile.Quality = (int)printDoc.DefaultPageSettings.PrinterResolution.Kind;
                    profile.ResolutionX = printDoc.DefaultPageSettings.PrinterResolution.X;
                    profile.ResolutionY = printDoc.DefaultPageSettings.PrinterResolution.Y;
                }
                
                // Save DEVMODE data which includes 2 inch cut setting
                byte[] devModeData = GetDevModeData(printDoc.PrinterSettings);
                if (devModeData != null && devModeData.Length > 0)
                {
                    profile.DevModeDataBase64 = Convert.ToBase64String(devModeData);
                }
                
                // Save to XML file
                string fileName = SanitizeFileName($"{profileName}_{printDoc.PrinterSettings.PrinterName}.xml");
                string filePath = Path.Combine(settingsDirectory, fileName);
                
                Log.Debug($"PrinterSettingsManager: Attempting to save profile to: {filePath}");
                
                // Ensure directory exists
                Directory.CreateDirectory(settingsDirectory);
                
                using (var writer = new StreamWriter(filePath))
                {
                    var serializer = new XmlSerializer(typeof(PrinterProfile));
                    serializer.Serialize(writer, profile);
                }
                
                Log.Debug($"PrinterSettingsManager: XML file saved successfully");
                
                // Also save to registry for quick access
                SaveToRegistry(profile);
                
                Log.Debug($"PrinterSettingsManager: Saved profile '{profileName}' for printer '{printDoc.PrinterSettings.PrinterName}'");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"PrinterSettingsManager: Failed to save profile: {ex.Message}");
                Log.Error($"PrinterSettingsManager: Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Log.Error($"PrinterSettingsManager: Inner exception: {ex.InnerException.Message}");
                }
                throw; // Re-throw to let caller handle it
            }
        }
        
        /// <summary>
        /// Load and apply a printer profile
        /// </summary>
        public bool LoadPrinterProfile(PrintDocument printDoc, string profileName)
        {
            try
            {
                string fileName = SanitizeFileName($"{profileName}_{printDoc.PrinterSettings.PrinterName}.xml");
                string filePath = Path.Combine(settingsDirectory, fileName);
                
                if (!File.Exists(filePath))
                {
                    // Try loading from registry
                    var profile = LoadFromRegistry(printDoc.PrinterSettings.PrinterName, profileName);
                    if (profile != null)
                    {
                        ApplyProfile(printDoc, profile);
                        return true;
                    }
                    
                    Log.Debug($"PrinterSettingsManager: Profile '{profileName}' not found at {filePath}");
                    return false;
                }
                
                // Check if file is empty or corrupted
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    Log.Error($"PrinterSettingsManager: Profile file is empty: {filePath}");
                    // Delete the empty file
                    try { File.Delete(filePath); } catch { }
                    return false;
                }
                
                PrinterProfile loadedProfile;
                try
                {
                    using (var reader = new StreamReader(filePath))
                    {
                        var serializer = new XmlSerializer(typeof(PrinterProfile));
                        loadedProfile = (PrinterProfile)serializer.Deserialize(reader);
                    }
                }
                catch (InvalidOperationException xmlEx)
                {
                    Log.Error($"PrinterSettingsManager: XML deserialization failed: {xmlEx.Message}");
                    // Try to delete corrupted file
                    try { File.Delete(filePath); } catch { }
                    return false;
                }
                
                ApplyProfile(printDoc, loadedProfile);
                
                Log.Debug($"PrinterSettingsManager: Loaded profile '{profileName}' for printer '{printDoc.PrinterSettings.PrinterName}'");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"PrinterSettingsManager: Failed to load profile: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Apply profile settings to PrintDocument
        /// </summary>
        private void ApplyProfile(PrintDocument printDoc, PrinterProfile profile)
        {
            // Apply paper size
            foreach (PaperSize size in printDoc.PrinterSettings.PaperSizes)
            {
                if (size.PaperName == profile.PaperSize || 
                    (size.Width == profile.PaperWidth && size.Height == profile.PaperHeight))
                {
                    printDoc.DefaultPageSettings.PaperSize = size;
                    break;
                }
            }
            
            // Apply orientation
            printDoc.DefaultPageSettings.Landscape = profile.Landscape;
            
            // Apply copies
            printDoc.PrinterSettings.Copies = (short)profile.Copies;
            
            // Apply margins
            printDoc.DefaultPageSettings.Margins = new Margins(
                profile.MarginLeft, 
                profile.MarginRight, 
                profile.MarginTop, 
                profile.MarginBottom
            );
            
            // Apply resolution if specified
            if (profile.ResolutionX > 0 && profile.ResolutionY > 0)
            {
                foreach (PrinterResolution res in printDoc.PrinterSettings.PrinterResolutions)
                {
                    if (res.X == profile.ResolutionX && res.Y == profile.ResolutionY)
                    {
                        printDoc.DefaultPageSettings.PrinterResolution = res;
                        break;
                    }
                }
            }
            else if (profile.Quality > 0)
            {
                // Try to set by quality kind
                PrinterResolutionKind kind = (PrinterResolutionKind)profile.Quality;
                foreach (PrinterResolution res in printDoc.PrinterSettings.PrinterResolutions)
                {
                    if (res.Kind == kind)
                    {
                        printDoc.DefaultPageSettings.PrinterResolution = res;
                        break;
                    }
                }
            }
            
            // Apply DEVMODE if available and not skipped (includes 2 inch cut setting)
            if (!string.IsNullOrEmpty(profile.DevModeDataBase64) && !profile.SkipDevMode)
            {
                try
                {
                    byte[] devModeData = Convert.FromBase64String(profile.DevModeDataBase64);
                    if (devModeData != null && devModeData.Length > 0)
                    {
                        SetDevModeData(printDoc.PrinterSettings, devModeData);
                        // Also apply to page settings
                        SetDevModeData(printDoc.DefaultPageSettings, devModeData);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to restore DEVMODE data: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Create a simplified profile for specific paper size
        /// </summary>
        public PrinterProfile CreateSimpleProfile(string printerName, string paperSize, bool landscape)
        {
            var profile = new PrinterProfile
            {
                PrinterName = printerName,
                ProfileName = $"{paperSize}_{(landscape ? "Landscape" : "Portrait")}",
                CustomPaperName = paperSize,
                Landscape = landscape,
                ForceOrientation = true,
                SkipDevMode = false, // Apply DEVMODE to preserve 2 inch cut settings
                Copies = 1,
                LastModified = DateTime.Now
            };
            
            // Set standard sizes
            switch (paperSize.ToLower())
            {
                case "4x6":
                    profile.PaperWidth = 400;  // 4 inches
                    profile.PaperHeight = 600; // 6 inches
                    profile.PaperSize = "4x6";
                    break;
                case "2x6":
                    profile.PaperWidth = 200;  // 2 inches
                    profile.PaperHeight = 600; // 6 inches
                    profile.PaperSize = "2x6";
                    break;
                case "5x7":
                    profile.PaperWidth = 500;  // 5 inches
                    profile.PaperHeight = 700; // 7 inches
                    profile.PaperSize = "5x7";
                    break;
                case "8x10":
                    profile.PaperWidth = 800;  // 8 inches
                    profile.PaperHeight = 1000; // 10 inches
                    profile.PaperSize = "8x10";
                    break;
            }
            
            return profile;
        }
        
        /// <summary>
        /// Get DEVMODE data from printer settings
        /// </summary>
        private byte[] GetDevModeData(PrinterSettings printerSettings)
        {
            try
            {
                IntPtr hDevMode = printerSettings.GetHdevmode();
                IntPtr pDevMode = GlobalLock(hDevMode);
                int size = GlobalSize(hDevMode).ToInt32();
                byte[] devModeData = new byte[size];
                Marshal.Copy(pDevMode, devModeData, 0, size);
                GlobalUnlock(hDevMode);
                GlobalFree(hDevMode);
                return devModeData;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Set DEVMODE data to printer settings
        /// </summary>
        private void SetDevModeData(PrinterSettings printerSettings, byte[] devModeData)
        {
            try
            {
                IntPtr hDevMode = GlobalAlloc(GMEM_MOVEABLE, devModeData.Length);
                IntPtr pDevMode = GlobalLock(hDevMode);
                Marshal.Copy(devModeData, 0, pDevMode, devModeData.Length);
                GlobalUnlock(hDevMode);
                printerSettings.SetHdevmode(hDevMode);
                GlobalFree(hDevMode);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to set DEVMODE to PrinterSettings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Set DEVMODE data to page settings
        /// </summary>
        private void SetDevModeData(PageSettings pageSettings, byte[] devModeData)
        {
            try
            {
                IntPtr hDevMode = GlobalAlloc(GMEM_MOVEABLE, devModeData.Length);
                IntPtr pDevMode = GlobalLock(hDevMode);
                Marshal.Copy(devModeData, 0, pDevMode, devModeData.Length);
                GlobalUnlock(hDevMode);
                pageSettings.SetHdevmode(hDevMode);
                GlobalFree(hDevMode);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to set DEVMODE to PageSettings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save profile to registry for persistence
        /// </summary>
        private void SaveToRegistry(PrinterProfile profile)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey($@"{registryPath}\{profile.PrinterName}\{profile.ProfileName}"))
                {
                    key.SetValue("PaperSize", profile.PaperSize ?? "");
                    key.SetValue("PaperWidth", profile.PaperWidth);
                    key.SetValue("PaperHeight", profile.PaperHeight);
                    key.SetValue("Landscape", profile.Landscape ? 1 : 0);
                    key.SetValue("Copies", profile.Copies);
                    key.SetValue("Quality", profile.Quality);
                    key.SetValue("ResolutionX", profile.ResolutionX);
                    key.SetValue("ResolutionY", profile.ResolutionY);
                    key.SetValue("ForceOrientation", profile.ForceOrientation ? 1 : 0);
                    key.SetValue("SkipDevMode", profile.SkipDevMode ? 1 : 0);
                    key.SetValue("Enable2InchCut", profile.Enable2InchCut ? 1 : 0);
                    key.SetValue("LastModified", profile.LastModified.Ticks);
                    
                    if (!string.IsNullOrEmpty(profile.DevModeDataBase64))
                    {
                        // Convert Base64 to byte array for registry storage
                        byte[] devModeData = Convert.FromBase64String(profile.DevModeDataBase64);
                        key.SetValue("DevModeData", devModeData, RegistryValueKind.Binary);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save to registry: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load profile from registry
        /// </summary>
        private PrinterProfile LoadFromRegistry(string printerName, string profileName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey($@"{registryPath}\{printerName}\{profileName}"))
                {
                    if (key == null) return null;
                    
                    var profile = new PrinterProfile
                    {
                        PrinterName = printerName,
                        ProfileName = profileName,
                        PaperSize = key.GetValue("PaperSize", "").ToString(),
                        PaperWidth = (int)key.GetValue("PaperWidth", 0),
                        PaperHeight = (int)key.GetValue("PaperHeight", 0),
                        Landscape = (int)key.GetValue("Landscape", 0) == 1,
                        Copies = (int)key.GetValue("Copies", 1),
                        Quality = (int)key.GetValue("Quality", 0),
                        ResolutionX = (int)key.GetValue("ResolutionX", 0),
                        ResolutionY = (int)key.GetValue("ResolutionY", 0),
                        ForceOrientation = (int)key.GetValue("ForceOrientation", 0) == 1,
                        SkipDevMode = (int)key.GetValue("SkipDevMode", 0) == 1,
                        Enable2InchCut = (int)key.GetValue("Enable2InchCut", 0) == 1
                    };
                    
                    if (key.GetValue("DevModeData") is byte[] devModeData)
                    {
                        // Convert byte array to Base64 for profile
                        profile.DevModeDataBase64 = Convert.ToBase64String(devModeData);
                    }
                    
                    return profile;
                }
            }
            catch
            {
                return null;
            }
        }
        
        private string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
        
        // P/Invoke declarations
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);
        
        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint uFlags, int dwBytes);
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalSize(IntPtr hMem);
        
        private const uint GMEM_MOVEABLE = 0x0002;
    }
}