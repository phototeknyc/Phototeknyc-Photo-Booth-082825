using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to manage all application settings following clean architecture
    /// </summary>
    public class SettingsManagementService : INotifyPropertyChanged
    {
        #region Singleton
        private static SettingsManagementService _instance;
        private static readonly object _lock = new object();
        
        public static SettingsManagementService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SettingsManagementService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion
        
        #region Events
        public event EventHandler<SettingChangedEventArgs> SettingChanged;
        public event EventHandler SettingsSaved;
        public event EventHandler SettingsReset;
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion
        
        #region Setting Categories
        public class SessionSettings
        {
            public int CountdownSeconds { get; set; }
            public bool ShowCountdown { get; set; }
            public int PhotoDisplayDuration { get; set; }
            public int DelayBetweenPhotos { get; set; }
            public bool AutoClearSession { get; set; }
            public int AutoClearTimeout { get; set; }
            public bool AutoShowQRCode { get; set; }
            public bool ShowSessionPrompts { get; set; }
            public int NumberOfPhotos { get; set; }
            public bool RequireEventSelection { get; set; }
        }
        
        public class CameraSettings
        {
            public bool PhotographerMode { get; set; }
            public bool MirrorLiveView { get; set; }
            public int CameraRotation { get; set; }
            public bool EnableIdleLiveView { get; set; }
            public int LiveViewFrameRate { get; set; }
            public string CameraModel { get; set; }
            public bool AutoFocusEnabled { get; set; }
        }
        
        public class DisplaySettings
        {
            public bool FullscreenMode { get; set; }
            public bool HideCursor { get; set; }
            public double ButtonSizeScale { get; set; }
            public string Theme { get; set; }
            public int ScreenBrightness { get; set; }
        }
        
        public class PrintSettings
        {
            public bool EnablePrinting { get; set; }
            public bool ShowPrintButton { get; set; }
            public int DefaultCopies { get; set; }
            public int MaxCopies { get; set; }
            public int MaxPrintsPerSession { get; set; }
            public string PrinterName { get; set; }
            public bool AutoPrint { get; set; }
        }
        
        public class FilterSettings
        {
            public bool EnableFilters { get; set; }
            public int DefaultFilter { get; set; }
            public double FilterIntensity { get; set; }
            public bool AllowFilterChange { get; set; }
            public bool ShowFilterPreview { get; set; }
            public bool AutoApplyFilter { get; set; }
            public bool BeautyModeEnabled { get; set; }
            public double BeautyModeIntensity { get; set; }
        }
        
        public class SharingSettings
        {
            public bool EnableSharing { get; set; }
            public bool EnableQRCode { get; set; }
            public bool EnableEmail { get; set; }
            public bool EnableSMS { get; set; }
            public string CloudProvider { get; set; }
            public string CloudApiKey { get; set; }
            public bool AutoUpload { get; set; }
        }
        
        public class SecuritySettings
        {
            public bool EnableLockFeature { get; set; }
            public int AutoLockTimeout { get; set; }
            public string LockPin { get; set; }
            public string LockMessage { get; set; }
            public bool RequirePinForSettings { get; set; }
        }
        
        public class RetakeSettings
        {
            public bool EnableRetake { get; set; }
            public int RetakeTimeout { get; set; }
            public bool AllowMultipleRetakes { get; set; }
            public int MaxRetakesPerPhoto { get; set; }
        }
        
        public class StorageSettings
        {
            public string PhotoLocation { get; set; }
            public string SessionFolder { get; set; }
            public bool OrganizeByDate { get; set; }
            public bool OrganizeByEvent { get; set; }
            public bool AutoBackup { get; set; }
            public int MaxStorageSizeGB { get; set; }
            public bool AutoCleanupOldFiles { get; set; }
            public int KeepFilesForDays { get; set; }
        }
        #endregion
        
        #region Properties
        private SessionSettings _sessionSettings;
        private CameraSettings _cameraSettings;
        private DisplaySettings _displaySettings;
        private PrintSettings _printSettings;
        private FilterSettings _filterSettings;
        private SharingSettings _sharingSettings;
        private SecuritySettings _securitySettings;
        private RetakeSettings _retakeSettings;
        private StorageSettings _storageSettings;
        
        public SessionSettings Session
        {
            get => _sessionSettings;
            set { _sessionSettings = value; OnPropertyChanged(); }
        }
        
        public CameraSettings Camera
        {
            get => _cameraSettings;
            set { _cameraSettings = value; OnPropertyChanged(); }
        }
        
        public DisplaySettings Display
        {
            get => _displaySettings;
            set { _displaySettings = value; OnPropertyChanged(); }
        }
        
        public PrintSettings Print
        {
            get => _printSettings;
            set { _printSettings = value; OnPropertyChanged(); }
        }
        
        // Alias for consistency with category name
        public PrintSettings Printing
        {
            get => _printSettings;
            set { _printSettings = value; OnPropertyChanged(); }
        }
        
        public FilterSettings Filters
        {
            get => _filterSettings;
            set { _filterSettings = value; OnPropertyChanged(); }
        }
        
        public SharingSettings Sharing
        {
            get => _sharingSettings;
            set { _sharingSettings = value; OnPropertyChanged(); }
        }
        
        public SecuritySettings Security
        {
            get => _securitySettings;
            set { _securitySettings = value; OnPropertyChanged(); }
        }
        
        public RetakeSettings Retake
        {
            get => _retakeSettings;
            set { _retakeSettings = value; OnPropertyChanged(); }
        }
        
        public StorageSettings Storage
        {
            get => _storageSettings;
            set { _storageSettings = value; OnPropertyChanged(); }
        }
        #endregion
        
        private SettingsManagementService()
        {
            LoadSettings();
        }
        
        /// <summary>
        /// Load all settings from Properties.Settings.Default
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                Log.Debug("SettingsManagementService: Loading settings");
                
                // Session Settings
                _sessionSettings = new SessionSettings
                {
                    CountdownSeconds = Properties.Settings.Default.CountdownSeconds,
                    ShowCountdown = Properties.Settings.Default.ShowCountdown,
                    PhotoDisplayDuration = Properties.Settings.Default.PhotoDisplayDuration,
                    DelayBetweenPhotos = Properties.Settings.Default.DelayBetweenPhotos,
                    AutoClearSession = Properties.Settings.Default.AutoClearSession,
                    AutoClearTimeout = Properties.Settings.Default.AutoClearTimeout,
                    AutoShowQRCode = Properties.Settings.Default.AutoShowQRCode,
                    ShowSessionPrompts = Properties.Settings.Default.ShowSessionPrompts,
                    NumberOfPhotos = 3,
                    RequireEventSelection = false
                };
                
                // Camera Settings
                _cameraSettings = new CameraSettings
                {
                    PhotographerMode = Properties.Settings.Default.PhotographerMode,
                    MirrorLiveView = Properties.Settings.Default.MirrorLiveView,
                    CameraRotation = Properties.Settings.Default.CameraRotation,
                    EnableIdleLiveView = Properties.Settings.Default.EnableIdleLiveView,
                    LiveViewFrameRate = Properties.Settings.Default.LiveViewFrameRate
                };
                
                // Display Settings
                _displaySettings = new DisplaySettings
                {
                    FullscreenMode = Properties.Settings.Default.FullscreenMode,
                    HideCursor = Properties.Settings.Default.HideCursor,
                    ButtonSizeScale = Properties.Settings.Default.ButtonSizeScale
                };
                
                // Print Settings
                _printSettings = new PrintSettings
                {
                    EnablePrinting = Properties.Settings.Default.EnablePrinting,
                    ShowPrintButton = Properties.Settings.Default.ShowPrintButton,
                    DefaultCopies = Properties.Settings.Default.DefaultPrintCopies,
                    MaxCopies = Properties.Settings.Default.MaxCopiesInModal,
                    MaxPrintsPerSession = Properties.Settings.Default.MaxSessionPrints
                };
                
                // Filter Settings
                _filterSettings = new FilterSettings
                {
                    EnableFilters = Properties.Settings.Default.EnableFilters,
                    DefaultFilter = Properties.Settings.Default.DefaultFilter,
                    FilterIntensity = Properties.Settings.Default.FilterIntensity,
                    AllowFilterChange = Properties.Settings.Default.AllowFilterChange,
                    ShowFilterPreview = Properties.Settings.Default.ShowFilterPreview,
                    AutoApplyFilter = Properties.Settings.Default.AutoApplyFilter,
                    BeautyModeEnabled = Properties.Settings.Default.BeautyModeEnabled,
                    BeautyModeIntensity = Properties.Settings.Default.BeautyModeIntensity
                };
                
                // Security Settings
                _securitySettings = new SecuritySettings
                {
                    EnableLockFeature = Properties.Settings.Default.EnableLockFeature,
                    AutoLockTimeout = Properties.Settings.Default.AutoLockTimeout,
                    LockPin = Properties.Settings.Default.LockPin,
                    LockMessage = "Enter PIN to unlock" // TODO: Properties.Settings.Default.LockMessage
                };
                
                // Retake Settings
                _retakeSettings = new RetakeSettings
                {
                    EnableRetake = Properties.Settings.Default.EnableRetake,
                    RetakeTimeout = Properties.Settings.Default.RetakeTimeout,
                    AllowMultipleRetakes = Properties.Settings.Default.AllowMultipleRetakes
                };
                
                // Storage Settings
                _storageSettings = new StorageSettings
                {
                    PhotoLocation = Properties.Settings.Default.PhotoLocation,
                    SessionFolder = Properties.Settings.Default.PhotoLocation ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    OrganizeByDate = true,
                    OrganizeByEvent = false
                };
                
                // Sharing Settings
                _sharingSettings = new SharingSettings
                {
                    EnableSharing = true,
                    EnableQRCode = true,
                    EnableEmail = false,
                    EnableSMS = false
                };
                
                Log.Debug("SettingsManagementService: Settings loaded successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsManagementService: Failed to load settings: {ex.Message}");
                LoadDefaults();
            }
        }
        
        /// <summary>
        /// Save all settings to Properties.Settings.Default
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                Log.Debug("SettingsManagementService: Saving settings");
                
                // Session Settings
                Properties.Settings.Default.CountdownSeconds = _sessionSettings.CountdownSeconds;
                Properties.Settings.Default.ShowCountdown = _sessionSettings.ShowCountdown;
                Properties.Settings.Default.PhotoDisplayDuration = _sessionSettings.PhotoDisplayDuration;
                Properties.Settings.Default.DelayBetweenPhotos = _sessionSettings.DelayBetweenPhotos;
                Properties.Settings.Default.AutoClearSession = _sessionSettings.AutoClearSession;
                Properties.Settings.Default.AutoClearTimeout = _sessionSettings.AutoClearTimeout;
                Properties.Settings.Default.AutoShowQRCode = _sessionSettings.AutoShowQRCode;
                Properties.Settings.Default.ShowSessionPrompts = _sessionSettings.ShowSessionPrompts;
                // NumberOfPhotos and RequireEventSelection might be stored elsewhere or calculated
                
                // Camera Settings
                Properties.Settings.Default.PhotographerMode = _cameraSettings.PhotographerMode;
                Properties.Settings.Default.MirrorLiveView = _cameraSettings.MirrorLiveView;
                Properties.Settings.Default.CameraRotation = _cameraSettings.CameraRotation;
                Properties.Settings.Default.EnableIdleLiveView = _cameraSettings.EnableIdleLiveView;
                Properties.Settings.Default.LiveViewFrameRate = _cameraSettings.LiveViewFrameRate;
                
                // Display Settings
                Properties.Settings.Default.FullscreenMode = _displaySettings.FullscreenMode;
                Properties.Settings.Default.HideCursor = _displaySettings.HideCursor;
                Properties.Settings.Default.ButtonSizeScale = _displaySettings.ButtonSizeScale;
                
                // Print Settings
                Properties.Settings.Default.EnablePrinting = _printSettings.EnablePrinting;
                Properties.Settings.Default.ShowPrintButton = _printSettings.ShowPrintButton;
                Properties.Settings.Default.DefaultPrintCopies = _printSettings.DefaultCopies;
                Properties.Settings.Default.MaxCopiesInModal = _printSettings.MaxCopies;
                Properties.Settings.Default.MaxSessionPrints = _printSettings.MaxPrintsPerSession;
                
                // Filter Settings
                Properties.Settings.Default.EnableFilters = _filterSettings.EnableFilters;
                Properties.Settings.Default.DefaultFilter = _filterSettings.DefaultFilter;
                Properties.Settings.Default.FilterIntensity = (int)_filterSettings.FilterIntensity;
                Properties.Settings.Default.AllowFilterChange = _filterSettings.AllowFilterChange;
                Properties.Settings.Default.ShowFilterPreview = _filterSettings.ShowFilterPreview;
                Properties.Settings.Default.AutoApplyFilter = _filterSettings.AutoApplyFilter;
                Properties.Settings.Default.BeautyModeEnabled = _filterSettings.BeautyModeEnabled;
                Properties.Settings.Default.BeautyModeIntensity = (int)_filterSettings.BeautyModeIntensity;
                
                // Security Settings
                Properties.Settings.Default.EnableLockFeature = _securitySettings.EnableLockFeature;
                Properties.Settings.Default.AutoLockTimeout = _securitySettings.AutoLockTimeout;
                if (!string.IsNullOrEmpty(_securitySettings.LockPin))
                    Properties.Settings.Default.LockPin = _securitySettings.LockPin;
                // TODO: Save LockMessage when property is available
                // if (!string.IsNullOrEmpty(_securitySettings.LockMessage))
                //     Properties.Settings.Default.LockMessage = _securitySettings.LockMessage;
                
                // Retake Settings
                Properties.Settings.Default.EnableRetake = _retakeSettings.EnableRetake;
                Properties.Settings.Default.RetakeTimeout = _retakeSettings.RetakeTimeout;
                Properties.Settings.Default.AllowMultipleRetakes = _retakeSettings.AllowMultipleRetakes;
                
                // Storage Settings
                Properties.Settings.Default.PhotoLocation = _storageSettings.PhotoLocation;
                // SessionFolder uses PhotoLocation, OrganizeByDate and OrganizeByEvent might be stored elsewhere
                
                // Sharing Settings
                // Sharing settings might be stored elsewhere or use different property names
                
                // Save to disk
                Properties.Settings.Default.Save();
                
                Log.Debug("SettingsManagementService: Settings saved successfully");
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsManagementService: Failed to save settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update a specific setting and auto-save
        /// </summary>
        public void UpdateSetting(string category, string settingName, object value)
        {
            try
            {
                Log.Debug($"SettingsManagementService: Updating {category}.{settingName} to {value} (Type: {value?.GetType()?.Name})");

                // Special handling for auto focus settings which are in Properties.Settings.Default
                if (category == "Camera" && (settingName == "EnableAutoFocus" || settingName == "AutoFocusDelay"))
                {
                    if (settingName == "EnableAutoFocus")
                    {
                        Properties.Settings.Default.EnableAutoFocus = Convert.ToBoolean(value);
                    }
                    else if (settingName == "AutoFocusDelay")
                    {
                        Properties.Settings.Default.AutoFocusDelay = Convert.ToInt32(value);
                    }

                    Properties.Settings.Default.Save();

                    // Notify
                    SettingChanged?.Invoke(this, new SettingChangedEventArgs
                    {
                        Category = category,
                        SettingName = settingName,
                        OldValue = null,
                        NewValue = value
                    });

                    Log.Debug($"SettingsManagementService: Successfully updated {settingName} to {value}");
                    return;
                }

                // Use reflection to update the property
                var categoryProperty = GetType().GetProperty(category);
                if (categoryProperty != null)
                {
                    var categoryObject = categoryProperty.GetValue(this);
                    if (categoryObject == null)
                    {
                        Log.Error($"SettingsManagementService: Category object is null for {category}");
                        return;
                    }

                    Log.Debug($"SettingsManagementService: Found category object for {category}, looking for property {settingName}");
                    var settingProperty = categoryObject.GetType().GetProperty(settingName);
                    if (settingProperty != null)
                    {
                        var oldValue = settingProperty.GetValue(categoryObject);
                        
                        // Convert value to the correct type for the property
                        var targetType = settingProperty.PropertyType;
                        var convertedValue = value;
                        
                        try
                        {
                            if (targetType == typeof(int) && value is double)
                            {
                                convertedValue = Convert.ToInt32(value);
                            }
                            else if (targetType == typeof(bool))
                            {
                                convertedValue = Convert.ToBoolean(value);
                            }
                            else if (targetType == typeof(double))
                            {
                                convertedValue = Convert.ToDouble(value);
                            }
                            else if (targetType == typeof(string))
                            {
                                convertedValue = value?.ToString();
                            }
                            
                            settingProperty.SetValue(categoryObject, convertedValue);
                            Log.Debug($"SettingsManagementService: Successfully set {settingName} to {convertedValue} (converted from {value})");
                        }
                        catch (Exception convEx)
                        {
                            Log.Error($"SettingsManagementService: Type conversion failed for {settingName}: {convEx.Message}");
                            return;
                        }
                        
                        // Auto-save
                        SaveSettings();
                        
                        // Notify
                        SettingChanged?.Invoke(this, new SettingChangedEventArgs
                        {
                            Category = category,
                            SettingName = settingName,
                            OldValue = oldValue,
                            NewValue = value
                        });
                    }
                    else
                    {
                        Log.Error($"SettingsManagementService: Property '{settingName}' not found in {category}");
                    }
                }
                else
                {
                    Log.Error($"SettingsManagementService: Category '{category}' not found");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsManagementService: Failed to update setting: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reset all settings to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            try
            {
                Log.Debug("SettingsManagementService: Resetting to defaults");
                
                Properties.Settings.Default.Reset();
                Properties.Settings.Default.Save();
                
                LoadSettings();
                
                SettingsReset?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error($"SettingsManagementService: Failed to reset settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load default values
        /// </summary>
        private void LoadDefaults()
        {
            _sessionSettings = new SessionSettings
            {
                CountdownSeconds = 5,
                ShowCountdown = true,
                PhotoDisplayDuration = 3,
                DelayBetweenPhotos = 2,
                AutoClearSession = true,
                AutoClearTimeout = 60,
                ShowSessionPrompts = true,
                NumberOfPhotos = 4,
                RequireEventSelection = false
            };
            
            _cameraSettings = new CameraSettings
            {
                PhotographerMode = false,
                MirrorLiveView = true,
                CameraRotation = 0,
                EnableIdleLiveView = true,
                LiveViewFrameRate = 30
            };
            
            _displaySettings = new DisplaySettings
            {
                FullscreenMode = true,
                HideCursor = true,
                ButtonSizeScale = 1.0
            };
            
            _printSettings = new PrintSettings
            {
                EnablePrinting = true,
                ShowPrintButton = true,
                DefaultCopies = 1,
                MaxCopies = 5,
                MaxPrintsPerSession = 10
            };
            
            _filterSettings = new FilterSettings
            {
                EnableFilters = true,
                DefaultFilter = 0,
                FilterIntensity = 100,
                AllowFilterChange = true,
                ShowFilterPreview = true,
                AutoApplyFilter = false,
                BeautyModeEnabled = false,
                BeautyModeIntensity = 50
            };
            
            _securitySettings = new SecuritySettings
            {
                EnableLockFeature = false,
                AutoLockTimeout = 300
            };
            
            _retakeSettings = new RetakeSettings
            {
                EnableRetake = true,
                RetakeTimeout = 30,
                AllowMultipleRetakes = false
            };
            
            _storageSettings = new StorageSettings
            {
                PhotoLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Photobooth Sessions"),
                OrganizeByDate = true,
                OrganizeByEvent = false
            };
            
            _sharingSettings = new SharingSettings
            {
                EnableSharing = true,
                EnableQRCode = true,
                EnableEmail = false,
                EnableSMS = false
            };
        }
        
        /// <summary>
        /// Get all settings as categorized dictionary for UI display
        /// </summary>
        public Dictionary<string, List<SettingItem>> GetCategorizedSettings()
        {
            var categories = new Dictionary<string, List<SettingItem>>();
            
            // Session Settings - Name must match property names for reflection to work
            categories["Session"] = new List<SettingItem>
            {
                new SettingItem { Name = "CountdownSeconds", DisplayName = "Countdown", Value = Session.CountdownSeconds, Type = SettingType.Slider, Min = 1, Max = 10, Unit = "seconds" },
                new SettingItem { Name = "ShowCountdown", DisplayName = "Show Countdown", Value = Session.ShowCountdown, Type = SettingType.Toggle },
                new SettingItem { Name = "PhotoDisplayDuration", DisplayName = "Photo Display", Value = Session.PhotoDisplayDuration, Type = SettingType.Slider, Min = 1, Max = 10, Unit = "seconds" },
                new SettingItem { Name = "DelayBetweenPhotos", DisplayName = "Delay Between", Value = Session.DelayBetweenPhotos, Type = SettingType.Slider, Min = 0, Max = 10, Unit = "seconds" },
                new SettingItem { Name = "AutoClearSession", DisplayName = "Auto Clear", Value = Session.AutoClearSession, Type = SettingType.Toggle },
                new SettingItem { Name = "AutoClearTimeout", DisplayName = "Clear Timeout", Value = Session.AutoClearTimeout, Type = SettingType.Slider, Min = 30, Max = 300, Unit = "seconds" },
                new SettingItem { Name = "AutoShowQRCode", DisplayName = "Auto Show QR Code", Value = Session.AutoShowQRCode, Type = SettingType.Toggle },
                new SettingItem { Name = "NumberOfPhotos", DisplayName = "Number of Photos", Value = Session.NumberOfPhotos, Type = SettingType.Slider, Min = 1, Max = 10 }
            };
            
            // Camera Settings - Name must match property names for reflection to work
            categories["Camera"] = new List<SettingItem>
            {
                new SettingItem { Name = "PhotographerMode", DisplayName = "Photographer Mode", Value = Camera.PhotographerMode, Type = SettingType.Toggle },
                new SettingItem { Name = "MirrorLiveView", DisplayName = "Mirror Live View", Value = Camera.MirrorLiveView, Type = SettingType.Toggle },
                new SettingItem { Name = "CameraRotation", DisplayName = "Camera Rotation", Value = Camera.CameraRotation, Type = SettingType.Dropdown,
                    DropdownOptions = new List<DropdownOption>
                    {
                        new DropdownOption { Display = "No Rotation", Value = 0 },
                        new DropdownOption { Display = "90° Clockwise", Value = 90 },
                        new DropdownOption { Display = "180° (Upside Down)", Value = 180 },
                        new DropdownOption { Display = "90° Counter-Clockwise", Value = 270 }
                    }
                },
                new SettingItem { Name = "EnableIdleLiveView", DisplayName = "Idle Live View", Value = Camera.EnableIdleLiveView, Type = SettingType.Toggle },
                new SettingItem { Name = "LiveViewFrameRate", DisplayName = "Frame Rate", Value = Camera.LiveViewFrameRate, Type = SettingType.Slider, Min = 10, Max = 60, Unit = "FPS" },
                new SettingItem { Name = "EnableAutoFocus", DisplayName = "Auto Focus", Value = Properties.Settings.Default.EnableAutoFocus, Type = SettingType.Toggle },
                new SettingItem { Name = "AutoFocusDelay", DisplayName = "Auto Focus Delay", Value = Properties.Settings.Default.AutoFocusDelay, Type = SettingType.Slider, Min = 0, Max = 500, Unit = "ms" }
            };
            
            // Print Settings - Name must match property names for reflection to work
            categories["Printing"] = new List<SettingItem>
            {
                new SettingItem { Name = "EnablePrinting", DisplayName = "Enable Printing", Value = Printing.EnablePrinting, Type = SettingType.Toggle },
                new SettingItem { Name = "ShowPrintButton", DisplayName = "Show Print Button", Value = Printing.ShowPrintButton, Type = SettingType.Toggle },
                new SettingItem { Name = "DefaultCopies", DisplayName = "Default Copies", Value = Printing.DefaultCopies, Type = SettingType.Slider, Min = 1, Max = 10 },
                new SettingItem { Name = "MaxCopies", DisplayName = "Max Copies", Value = Printing.MaxCopies, Type = SettingType.Slider, Min = 1, Max = 20 },
                new SettingItem { Name = "MaxPrintsPerSession", DisplayName = "Max Per Session", Value = Printing.MaxPrintsPerSession, Type = SettingType.Slider, Min = 0, Max = 50 }
            };
            
            // Filter Settings - Name must match property names for reflection to work
            categories["Filters"] = new List<SettingItem>
            {
                new SettingItem { Name = "EnableFilters", DisplayName = "Enable Filters", Value = Filters.EnableFilters, Type = SettingType.Toggle },
                new SettingItem { Name = "FilterIntensity", DisplayName = "Filter Intensity", Value = Filters.FilterIntensity, Type = SettingType.Slider, Min = 0, Max = 100, Unit = "%" },
                new SettingItem { Name = "AllowFilterChange", DisplayName = "Allow Change", Value = Filters.AllowFilterChange, Type = SettingType.Toggle },
                new SettingItem { Name = "ShowFilterPreview", DisplayName = "Show Preview", Value = Filters.ShowFilterPreview, Type = SettingType.Toggle },
                new SettingItem { Name = "AutoApplyFilter", DisplayName = "Auto Apply", Value = Filters.AutoApplyFilter, Type = SettingType.Toggle },
                new SettingItem { Name = "BeautyModeEnabled", DisplayName = "Beauty Mode", Value = Filters.BeautyModeEnabled, Type = SettingType.Toggle },
                new SettingItem { Name = "BeautyModeIntensity", DisplayName = "Beauty Intensity", Value = Filters.BeautyModeIntensity, Type = SettingType.Slider, Min = 0, Max = 100, Unit = "%" }
            };
            
            // Display Settings - Name must match property names for reflection to work
            categories["Display"] = new List<SettingItem>
            {
                new SettingItem { Name = "FullscreenMode", DisplayName = "Fullscreen", Value = Display.FullscreenMode, Type = SettingType.Toggle },
                new SettingItem { Name = "HideCursor", DisplayName = "Hide Cursor", Value = Display.HideCursor, Type = SettingType.Toggle },
                new SettingItem { Name = "ButtonSizeScale", DisplayName = "Button Size", Value = Display.ButtonSizeScale * 100, Type = SettingType.Slider, Min = 50, Max = 200, Unit = "%" }
            };
            
            // Retake Settings - Name must match property names for reflection to work
            categories["Retake"] = new List<SettingItem>
            {
                new SettingItem { Name = "EnableRetake", DisplayName = "Enable Retake", Value = Retake.EnableRetake, Type = SettingType.Toggle },
                new SettingItem { Name = "RetakeTimeout", DisplayName = "Retake Timeout", Value = Retake.RetakeTimeout, Type = SettingType.Slider, Min = 10, Max = 60, Unit = "seconds" },
                new SettingItem { Name = "AllowMultipleRetakes", DisplayName = "Multiple Retakes", Value = Retake.AllowMultipleRetakes, Type = SettingType.Toggle }
            };
            
            // Storage Settings - Name must match property names for reflection to work
            categories["Storage"] = new List<SettingItem>
            {
                new SettingItem { Name = "PhotoLocation", DisplayName = "Photo Location", Value = Storage.PhotoLocation, Type = SettingType.Text },
                new SettingItem { Name = "SessionFolder", DisplayName = "Session Folder", Value = Storage.SessionFolder, Type = SettingType.Text },
                new SettingItem { Name = "OrganizeByDate", DisplayName = "Organize by Date", Value = Storage.OrganizeByDate, Type = SettingType.Toggle },
                new SettingItem { Name = "OrganizeByEvent", DisplayName = "Organize by Event", Value = Storage.OrganizeByEvent, Type = SettingType.Toggle },
                new SettingItem { Name = "AutoBackup", DisplayName = "Auto Backup", Value = Storage.AutoBackup, Type = SettingType.Toggle }
            };
            
            // Sharing Settings - Name must match property names for reflection to work
            categories["Sharing"] = new List<SettingItem>
            {
                new SettingItem { Name = "EnableSharing", DisplayName = "Enable Sharing", Value = Sharing.EnableSharing, Type = SettingType.Toggle },
                new SettingItem { Name = "EnableQRCode", DisplayName = "QR Code", Value = Sharing.EnableQRCode, Type = SettingType.Toggle },
                new SettingItem { Name = "EnableEmail", DisplayName = "Email", Value = Sharing.EnableEmail, Type = SettingType.Toggle },
                new SettingItem { Name = "EnableSMS", DisplayName = "SMS", Value = Sharing.EnableSMS, Type = SettingType.Toggle }
            };
            
            // Security Settings - Name must match property names for reflection to work
            categories["Security"] = new List<SettingItem>
            {
                new SettingItem { Name = "EnableLockFeature", DisplayName = "Enable Lock", Value = Security.EnableLockFeature, Type = SettingType.Toggle },
                new SettingItem { Name = "AutoLockTimeout", DisplayName = "Lock Timeout", Value = Security.AutoLockTimeout, Type = SettingType.Slider, Min = 60, Max = 600, Unit = "seconds" }
            };
            
            return categories;
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    #region Supporting Classes
    public class SettingChangedEventArgs : EventArgs
    {
        public string Category { get; set; }
        public string SettingName { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
    }
    
    public class SettingItem
    {
        public string Name { get; set; }  // Property name for reflection
        public string DisplayName { get; set; }  // Display name for UI
        public string Description { get; set; }
        public object Value { get; set; }
        public SettingType Type { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public string Unit { get; set; }
        public List<string> Options { get; set; }
        public List<DropdownOption> DropdownOptions { get; set; }  // For dropdown with display/value pairs
        public string Category { get; set; }
        public string Icon { get; set; }
    }

    public class DropdownOption
    {
        public string Display { get; set; }
        public object Value { get; set; }
    }

    public enum SettingType
    {
        Toggle,
        Slider,
        Dropdown,
        Text,
        Number,
        Color,
        File,
        Folder
    }
    #endregion
}