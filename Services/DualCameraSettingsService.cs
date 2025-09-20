using System;
using System.Collections.Generic;
using System.Linq;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Dual Camera Settings Service
    /// Manages two independent sets of camera settings:
    /// 1. Live View Settings - for preview and composition
    /// 2. Photo Capture Settings - for actual photography
    /// </summary>
    public class DualCameraSettingsService
    {
        #region Singleton
        private static DualCameraSettingsService _instance;
        private static readonly object _lock = new object();

        public static DualCameraSettingsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DualCameraSettingsService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Enums
        public enum SettingsMode
        {
            LiveView,
            PhotoCapture,
            Synchronized
        }

        public enum SyncDirection
        {
            LiveViewToPhoto,
            PhotoToLiveView,
            Bidirectional
        }
        #endregion

        #region Properties
        private ICameraDevice _currentCamera;
        private SettingsMode _currentMode = SettingsMode.PhotoCapture;
        private bool _autoSwitchEnabled = true;
        private bool _isLiveViewActive = false;

        /// <summary>
        /// Current active settings mode
        /// </summary>
        public SettingsMode CurrentMode 
        { 
            get => _currentMode;
            set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    OnModeChanged();
                }
            }
        }

        /// <summary>
        /// Enable automatic switching between modes based on camera state
        /// </summary>
        public bool AutoSwitchEnabled 
        { 
            get => _autoSwitchEnabled;
            set => _autoSwitchEnabled = value;
        }

        /// <summary>
        /// Live view specific settings
        /// </summary>
        public DualCameraSettings LiveViewSettings { get; private set; }

        /// <summary>
        /// Photo capture specific settings
        /// </summary>
        public DualCameraSettings PhotoCaptureSettings { get; private set; }

        /// <summary>
        /// Get the currently active settings based on mode
        /// </summary>
        public DualCameraSettings ActiveSettings
        {
            get
            {
                switch (_currentMode)
                {
                    case SettingsMode.LiveView:
                        return LiveViewSettings;
                    case SettingsMode.PhotoCapture:
                        return PhotoCaptureSettings;
                    case SettingsMode.Synchronized:
                        return PhotoCaptureSettings; // Use photo settings as primary in sync mode
                    default:
                        return PhotoCaptureSettings;
                }
            }
        }
        #endregion

        #region Events
        public event EventHandler<SettingsModeChangedEventArgs> ModeChanged;
        public event EventHandler<DualSettingEventArgs> SettingChanged;
        #endregion

        #region Constructor
        private DualCameraSettingsService()
        {
            LiveViewSettings = new DualCameraSettings("LiveView");
            PhotoCaptureSettings = new DualCameraSettings("PhotoCapture");
            
            InitializeDefaultSettings();
            InitializeCamera();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Apply live view settings to camera
        /// </summary>
        public void ApplyLiveViewSettings()
        {
            if (_currentCamera == null) return;

            try
            {
                DebugService.LogDebug("DualSettings: Applying live view settings to camera");
                
                ApplySettingsToCamera(LiveViewSettings);
                _isLiveViewActive = true;
                
                if (_autoSwitchEnabled)
                {
                    CurrentMode = SettingsMode.LiveView;
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"DualSettings: Error applying live view settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply photo capture settings to camera
        /// </summary>
        public void ApplyPhotoCaptureSettings()
        {
            if (_currentCamera == null) return;

            try
            {
                DebugService.LogDebug($"DualSettings: Applying photo capture settings to camera - ISO: {PhotoCaptureSettings.ISO}, Aperture: {PhotoCaptureSettings.Aperture}, Shutter: {PhotoCaptureSettings.ShutterSpeed}");

                ApplySettingsToCamera(PhotoCaptureSettings);
                _isLiveViewActive = false;
                
                if (_autoSwitchEnabled)
                {
                    CurrentMode = SettingsMode.PhotoCapture;
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"DualSettings: Error applying photo capture settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Store current camera settings to specified profile
        /// </summary>
        public void StoreCurrentSettingsTo(SettingsMode targetMode)
        {
            if (_currentCamera == null) return;

            try
            {
                var targetSettings = (targetMode == SettingsMode.LiveView) ? LiveViewSettings : PhotoCaptureSettings;
                
                // Read current camera settings
                if (_currentCamera.IsoNumber != null)
                    targetSettings.ISO = _currentCamera.IsoNumber.Value;
                    
                if (_currentCamera.FNumber != null)
                    targetSettings.Aperture = _currentCamera.FNumber.Value;
                    
                if (_currentCamera.ShutterSpeed != null)
                    targetSettings.ShutterSpeed = _currentCamera.ShutterSpeed.Value;
                    
                if (_currentCamera.WhiteBalance != null)
                    targetSettings.WhiteBalance = _currentCamera.WhiteBalance.Value;
                    
                if (_currentCamera.ExposureCompensation != null)
                    targetSettings.ExposureCompensation = _currentCamera.ExposureCompensation.Value;

                DebugService.LogDebug($"DualSettings: Stored current camera settings to {targetMode} profile");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"DualSettings: Error storing settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Synchronize settings between profiles
        /// </summary>
        public void SynchronizeSettings(SyncDirection direction)
        {
            try
            {
                switch (direction)
                {
                    case SyncDirection.LiveViewToPhoto:
                        PhotoCaptureSettings.CopyFrom(LiveViewSettings);
                        DebugService.LogDebug("DualSettings: Synchronized live view settings to photo capture");
                        break;
                        
                    case SyncDirection.PhotoToLiveView:
                        LiveViewSettings.CopyFrom(PhotoCaptureSettings);
                        DebugService.LogDebug("DualSettings: Synchronized photo capture settings to live view");
                        break;
                        
                    case SyncDirection.Bidirectional:
                        // Use whichever is currently active as source
                        if (_currentMode == SettingsMode.LiveView)
                        {
                            PhotoCaptureSettings.CopyFrom(LiveViewSettings);
                        }
                        else
                        {
                            LiveViewSettings.CopyFrom(PhotoCaptureSettings);
                        }
                        DebugService.LogDebug("DualSettings: Bidirectional synchronization completed");
                        break;
                }
                
                CurrentMode = SettingsMode.Synchronized;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"DualSettings: Error synchronizing settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Update camera reference
        /// </summary>
        public void UpdateCameraReference(ICameraDevice camera)
        {
            _currentCamera = camera;
            DebugService.LogDebug($"DualSettings: Camera reference updated to {camera?.DeviceName ?? "null"}");
        }

        /// <summary>
        /// Set individual setting for live view profile
        /// </summary>
        public void SetLiveViewSetting(string settingName, string value)
        {
            switch (settingName)
            {
                case "ISO":
                    LiveViewSettings.ISO = value;
                    break;
                case "Aperture":
                    LiveViewSettings.Aperture = value;
                    break;
                case "ShutterSpeed":
                    LiveViewSettings.ShutterSpeed = value;
                    break;
                case "WhiteBalance":
                    LiveViewSettings.WhiteBalance = value;
                    break;
                case "ExposureCompensation":
                    LiveViewSettings.ExposureCompensation = value;
                    break;
            }
            DebugService.LogDebug($"DualSettings: LiveView {settingName} set to {value}");
            OnSettingChanged(settingName, value, SettingsMode.LiveView);
        }

        /// <summary>
        /// Set individual setting for photo capture profile
        /// </summary>
        public void SetPhotoCaptureSetting(string settingName, string value)
        {
            switch (settingName)
            {
                case "ISO":
                    PhotoCaptureSettings.ISO = value;
                    break;
                case "Aperture":
                    PhotoCaptureSettings.Aperture = value;
                    break;
                case "ShutterSpeed":
                    PhotoCaptureSettings.ShutterSpeed = value;
                    break;
                case "WhiteBalance":
                    PhotoCaptureSettings.WhiteBalance = value;
                    break;
                case "ExposureCompensation":
                    PhotoCaptureSettings.ExposureCompensation = value;
                    break;
            }
            DebugService.LogDebug($"DualSettings: PhotoCapture {settingName} set to {value}");
            OnSettingChanged(settingName, value, SettingsMode.PhotoCapture);
        }

        /// <summary>
        /// Get preset configurations for different scenarios
        /// </summary>
        public void ApplyPreset(string presetName)
        {
            switch (presetName)
            {
                case "DarkVenueFlash":
                    // Live view settings for visibility
                    LiveViewSettings.ISO = "1600";
                    LiveViewSettings.Aperture = "2.8";
                    LiveViewSettings.ShutterSpeed = "1/30";
                    LiveViewSettings.ExposureCompensation = "+1";
                    
                    // Photo settings for flash
                    PhotoCaptureSettings.ISO = "400";
                    PhotoCaptureSettings.Aperture = "5.6";
                    PhotoCaptureSettings.ShutterSpeed = "1/125";
                    PhotoCaptureSettings.ExposureCompensation = "0";
                    break;
                    
                case "BrightVenue":
                    // Similar settings for both
                    LiveViewSettings.ISO = "200";
                    LiveViewSettings.Aperture = "4.0";
                    LiveViewSettings.ShutterSpeed = "1/250";
                    LiveViewSettings.ExposureCompensation = "0";
                    
                    PhotoCaptureSettings.CopyFrom(LiveViewSettings);
                    break;
                    
                case "Studio":
                    // Live view slightly brighter for modeling lights
                    LiveViewSettings.ISO = "400";
                    LiveViewSettings.Aperture = "5.6";
                    LiveViewSettings.ShutterSpeed = "1/60";
                    LiveViewSettings.ExposureCompensation = "0";
                    
                    // Photo optimized for strobes
                    PhotoCaptureSettings.ISO = "100";
                    PhotoCaptureSettings.Aperture = "8.0";
                    PhotoCaptureSettings.ShutterSpeed = "1/125";
                    PhotoCaptureSettings.ExposureCompensation = "0";
                    break;
            }
            
            DebugService.LogDebug($"DualSettings: Applied preset '{presetName}'");
        }
        #endregion

        #region Private Methods
        private void InitializeDefaultSettings()
        {
            // Try to load saved settings first
            if (!LoadSettingsFromStorage())
            {
                // Use defaults if no saved settings
                // Default live view settings (brighter for visibility)
                LiveViewSettings.ISO = "800";
                LiveViewSettings.Aperture = "2.8";
                LiveViewSettings.ShutterSpeed = "1/60";
                LiveViewSettings.WhiteBalance = "Auto";
                LiveViewSettings.ExposureCompensation = "0";

                // Default photo capture settings (optimized for flash)
                PhotoCaptureSettings.ISO = "200";
                PhotoCaptureSettings.Aperture = "5.6";
                PhotoCaptureSettings.ShutterSpeed = "1/125";
                PhotoCaptureSettings.WhiteBalance = "Flash";
                PhotoCaptureSettings.ExposureCompensation = "0";
            }
        }

        private bool LoadSettingsFromStorage()
        {
            try
            {
                var settings = Properties.Settings.Default;

                // Load Live View Settings
                if (!string.IsNullOrEmpty(settings.LiveViewISO))
                {
                    LiveViewSettings.ISO = settings.LiveViewISO;
                    DebugService.LogDebug($"Loaded Live View ISO from storage: {LiveViewSettings.ISO}");
                }
                if (!string.IsNullOrEmpty(settings.LiveViewAperture))
                {
                    LiveViewSettings.Aperture = settings.LiveViewAperture;
                    DebugService.LogDebug($"Loaded Live View Aperture from storage: {LiveViewSettings.Aperture}");
                }
                if (!string.IsNullOrEmpty(settings.LiveViewShutter))
                {
                    LiveViewSettings.ShutterSpeed = settings.LiveViewShutter;
                    DebugService.LogDebug($"Loaded Live View Shutter from storage: {LiveViewSettings.ShutterSpeed}");
                }

                // Load Photo Capture Settings
                if (!string.IsNullOrEmpty(settings.PhotoCaptureISO))
                {
                    PhotoCaptureSettings.ISO = settings.PhotoCaptureISO;
                    DebugService.LogDebug($"Loaded Photo Capture ISO from storage: {PhotoCaptureSettings.ISO}");
                }
                if (!string.IsNullOrEmpty(settings.PhotoCaptureAperture))
                {
                    PhotoCaptureSettings.Aperture = settings.PhotoCaptureAperture;
                    DebugService.LogDebug($"Loaded Photo Capture Aperture from storage: {PhotoCaptureSettings.Aperture}");
                }
                if (!string.IsNullOrEmpty(settings.PhotoCaptureShutter))
                {
                    PhotoCaptureSettings.ShutterSpeed = settings.PhotoCaptureShutter;
                    DebugService.LogDebug($"Loaded Photo Capture Shutter from storage: {PhotoCaptureSettings.ShutterSpeed}");
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SaveSettingsToStorage()
        {
            try
            {
                var settings = Properties.Settings.Default;

                // Save Live View Settings
                settings.LiveViewISO = LiveViewSettings.ISO;
                settings.LiveViewAperture = LiveViewSettings.Aperture;
                settings.LiveViewShutter = LiveViewSettings.ShutterSpeed;

                // Save Photo Capture Settings
                settings.PhotoCaptureISO = PhotoCaptureSettings.ISO;
                settings.PhotoCaptureAperture = PhotoCaptureSettings.Aperture;
                settings.PhotoCaptureShutter = PhotoCaptureSettings.ShutterSpeed;

                settings.Save();
                DebugService.LogDebug("DualSettings: Settings saved to storage");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"DualSettings: Error saving settings: {ex.Message}");
            }
        }

        private void InitializeCamera()
        {
            try
            {
                var sessionManager = CameraSessionManager.Instance;
                _currentCamera = sessionManager?.DeviceManager?.SelectedCameraDevice;
                
                if (_currentCamera != null)
                {
                    DebugService.LogDebug($"DualSettings: Initialized with camera {_currentCamera.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"DualSettings: Error initializing camera: {ex.Message}");
            }
        }

        private void ApplySettingsToCamera(DualCameraSettings settings)
        {
            if (_currentCamera == null || settings == null) return;

            try
            {
                // Apply each setting if the camera property is available
                if (_currentCamera.IsoNumber != null && !string.IsNullOrEmpty(settings.ISO))
                {
                    _currentCamera.IsoNumber.SetValue(settings.ISO);
                }
                
                if (_currentCamera.FNumber != null && !string.IsNullOrEmpty(settings.Aperture))
                {
                    _currentCamera.FNumber.SetValue(settings.Aperture);
                }
                
                if (_currentCamera.ShutterSpeed != null && !string.IsNullOrEmpty(settings.ShutterSpeed))
                {
                    DebugService.LogDebug($"DualSettings: Setting ShutterSpeed to {settings.ShutterSpeed}");
                    DebugService.LogDebug($"DualSettings: ShutterSpeed IsEnabled={_currentCamera.ShutterSpeed.IsEnabled}, Available={_currentCamera.ShutterSpeed.Available}");

                    // Check if the value exists in available values
                    if (_currentCamera.ShutterSpeed.Values != null && _currentCamera.ShutterSpeed.Values.Contains(settings.ShutterSpeed))
                    {
                        DebugService.LogDebug($"DualSettings: About to call SetValue with ShutterSpeed: {settings.ShutterSpeed}");
                        _currentCamera.ShutterSpeed.SetValue(settings.ShutterSpeed);
                        DebugService.LogDebug($"DualSettings: ShutterSpeed SetValue called successfully with {settings.ShutterSpeed}");

                        // Verify it was set
                        var currentValue = _currentCamera.ShutterSpeed.Value;
                        DebugService.LogDebug($"DualSettings: Current ShutterSpeed after setting: {currentValue}");
                    }
                    else
                    {
                        DebugService.LogDebug($"DualSettings: ShutterSpeed value '{settings.ShutterSpeed}' not found in available values");
                        if (_currentCamera.ShutterSpeed.Values != null)
                        {
                            DebugService.LogDebug($"DualSettings: Available shutter speeds: {string.Join(", ", _currentCamera.ShutterSpeed.Values.Take(5))}...");
                        }
                    }
                }
                else
                {
                    if (_currentCamera.ShutterSpeed == null)
                        DebugService.LogDebug("DualSettings: ShutterSpeed property is null on camera");
                    if (string.IsNullOrEmpty(settings.ShutterSpeed))
                        DebugService.LogDebug("DualSettings: ShutterSpeed setting value is empty");
                }
                
                if (_currentCamera.WhiteBalance != null && !string.IsNullOrEmpty(settings.WhiteBalance))
                {
                    _currentCamera.WhiteBalance.SetValue(settings.WhiteBalance);
                }
                
                if (_currentCamera.ExposureCompensation != null && !string.IsNullOrEmpty(settings.ExposureCompensation))
                {
                    _currentCamera.ExposureCompensation.SetValue(settings.ExposureCompensation);
                }
                
                DebugService.LogDebug($"DualSettings: Applied {settings.Name} settings to camera");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"DualSettings: Error applying settings to camera: {ex.Message}");
            }
        }

        private void OnModeChanged()
        {
            ModeChanged?.Invoke(this, new SettingsModeChangedEventArgs
            {
                NewMode = _currentMode,
                IsLiveViewActive = _isLiveViewActive
            });
        }

        private void OnSettingChanged(string settingName, string value, SettingsMode mode)
        {
            SettingChanged?.Invoke(this, new DualSettingEventArgs
            {
                SettingName = settingName,
                SettingValue = value,
                TargetMode = mode
            });
        }
        #endregion
    }

    /// <summary>
    /// Camera settings container
    /// </summary>
    public class DualCameraSettings
    {
        public string Name { get; set; }
        public string ISO { get; set; }
        public string Aperture { get; set; }
        public string ShutterSpeed { get; set; }
        public string WhiteBalance { get; set; }
        public string ExposureCompensation { get; set; }
        public Dictionary<string, string> CustomSettings { get; set; }

        public DualCameraSettings(string name)
        {
            Name = name;
            CustomSettings = new Dictionary<string, string>();
        }

        public void CopyFrom(DualCameraSettings source)
        {
            if (source == null) return;
            
            ISO = source.ISO;
            Aperture = source.Aperture;
            ShutterSpeed = source.ShutterSpeed;
            WhiteBalance = source.WhiteBalance;
            ExposureCompensation = source.ExposureCompensation;
            CustomSettings = new Dictionary<string, string>(source.CustomSettings);
        }
    }

    /// <summary>
    /// Event arguments for mode changes
    /// </summary>
    public class SettingsModeChangedEventArgs : EventArgs
    {
        public DualCameraSettingsService.SettingsMode NewMode { get; set; }
        public bool IsLiveViewActive { get; set; }
    }

    /// <summary>
    /// Event arguments for dual setting changes
    /// </summary>
    public class DualSettingChangedEventArgs : EventArgs
    {
        public string ProfileName { get; set; }
        public string SettingName { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// Event arguments for setting changes
    /// </summary>
    public class DualSettingEventArgs : EventArgs
    {
        public string SettingName { get; set; }
        public string SettingValue { get; set; }
        public DualCameraSettingsService.SettingsMode TargetMode { get; set; }
    }
}