using System;
using System.Linq;
using System.Threading.Tasks;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Video Mode Live View Service
    /// Uses video mode to enable real-time camera setting adjustments during live view
    /// Provides immediate visual feedback for exposure changes in dark venues
    /// </summary>
    public class VideoModeLiveViewService
    {
        #region Singleton
        private static VideoModeLiveViewService _instance;
        private static readonly object _lock = new object();

        public static VideoModeLiveViewService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new VideoModeLiveViewService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Properties
        private ICameraDevice _currentCamera;
        private bool _isVideoModeActive = false;
        private bool _isLiveViewRunning = false;
        private VideoModeSettings _savedPhotoSettings;
        private VideoModeSettings _videoModeSettings;
        
        /// <summary>
        /// Is video mode currently active for live view
        /// </summary>
        public bool IsVideoModeActive => _isVideoModeActive;
        
        /// <summary>
        /// Is live view currently running
        /// </summary>
        public bool IsLiveViewRunning => _isLiveViewRunning;

        /// <summary>
        /// Enable/disable video mode live view
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// Current camera mode before switching
        /// </summary>
        public string OriginalCameraMode { get; private set; }
        #endregion

        #region Events
        public event EventHandler<VideoModeLiveViewEventArgs> ModeChanged;
        public event EventHandler<VideoModeLiveViewEventArgs> SettingChanged;
        #endregion

        #region Constructor
        private VideoModeLiveViewService()
        {
            _savedPhotoSettings = new VideoModeSettings { Name = "PhotoMode" };
            _videoModeSettings = new VideoModeSettings { Name = "VideoMode" };
            InitializeCamera();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Start video mode live view with real-time camera control
        /// </summary>
        public async Task<bool> StartVideoModeLiveView()
        {
            try
            {
                // Try to initialize camera if not already done
                if (_currentCamera == null)
                {
                    InitializeCamera();
                }
                
                if (_currentCamera == null)
                {
                    DebugService.LogError("VideoModeLiveView: No camera available after initialization attempt");
                    return false;
                }

                // Save current photo settings
                SaveCurrentPhotoSettings();

                DebugService.LogDebug($"VideoModeLiveView: Current camera: {_currentCamera.DeviceName}, Mode: {_currentCamera.Mode?.Value}");
                
                // Save the original camera mode before any potential changes
                if (_currentCamera.Mode != null)
                {
                    OriginalCameraMode = _currentCamera.Mode.Value;
                    DebugService.LogDebug($"VideoModeLiveView: Saved original camera mode: {OriginalCameraMode}");
                }
                
                // DON'T switch to movie mode - Canon T6 doesn't need it
                // Just ensure live view is running for video recording
                _isVideoModeActive = true;
                _isLiveViewRunning = true;
                IsEnabled = true; // Important: Mark service as enabled
                
                // Ensure live view is running if not already running
                // Don't start live view if it's already running from the UI timer
                if (_currentCamera.GetCapability(CapabilityEnum.LiveView))
                {
                    try
                    {
                        // Only start live view if it's not already active
                        // This prevents conflicts with the UI live view timer
                        var liveViewData = _currentCamera.GetLiveViewImage();
                        bool isLiveViewRunning = liveViewData?.IsLiveViewRunning == true;
                        
                        if (!isLiveViewRunning)
                        {
                            _currentCamera.StartLiveView();
                            DebugService.LogDebug("VideoModeLiveView: Live view started for video recording");
                        }
                        else
                        {
                            DebugService.LogDebug("VideoModeLiveView: Live view already running - using existing stream");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugService.LogDebug($"VideoModeLiveView: Live view may already be running: {ex.Message}");
                    }
                }
                
                // Log available properties
                LogAvailableProperties();
                
                DebugService.LogDebug("VideoModeLiveView: Ready for video recording (no mode switch needed)");
                OnModeChanged(true);
                return true;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error starting - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stop video mode live view and restore photo mode
        /// </summary>
        public async Task<bool> StopVideoModeLiveView()
        {
            try
            {
                if (!_isVideoModeActive)
                    return true;

                // Don't stop live view - let the UI timer continue to manage it
                // The UI live view timer should continue running after video recording
                if (_currentCamera != null && _isLiveViewRunning)
                {
                    DebugService.LogDebug("VideoModeLiveView: Keeping live view active for UI timer");
                    _isLiveViewRunning = false; // Mark service as not managing live view
                    // Note: Don't call _currentCamera.StopLiveView() to keep UI live view working
                }

                // Restore photo mode
                if (await RestorePhotoMode())
                {
                    // Restore saved photo settings
                    RestorePhotoSettings();
                    
                    _isVideoModeActive = false;
                    IsEnabled = false; // Important: Mark service as disabled
                    DebugService.LogDebug("VideoModeLiveView: Stopped and restored photo mode");
                    OnModeChanged(false);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error stopping - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Temporarily switch to photo mode for capture
        /// </summary>
        public async Task<bool> SwitchToPhotoModeForCapture()
        {
            try
            {
                DebugService.LogDebug($"VideoModeLiveView: SwitchToPhotoModeForCapture called - IsVideoModeActive={_isVideoModeActive}, IsEnabled={IsEnabled}, HasCamera={_currentCamera != null}");
                
                if (!_isVideoModeActive || _currentCamera == null)
                {
                    DebugService.LogDebug($"VideoModeLiveView: Not in video mode or no camera, skipping mode switch (IsEnabled={IsEnabled})");
                    return true; // Not an error, just not needed
                }

                DebugService.LogDebug($"VideoModeLiveView: Starting switch to photo mode - OriginalMode='{OriginalCameraMode}'");
                
                // Don't stop live view - just switch mode directly
                // Many cameras can switch modes without stopping live view
                _isLiveViewRunning = false; // Mark as stopped but don't actually stop
                
                // Switch back to photo mode
                DebugService.LogDebug("VideoModeLiveView: Calling RestorePhotoMode");
                bool restored = await RestorePhotoMode();
                DebugService.LogDebug($"VideoModeLiveView: RestorePhotoMode returned {restored}");
                
                if (restored)
                {
                    _isVideoModeActive = false; // Important: mark as no longer in video mode
                    // Note: We keep IsEnabled=true because video mode is still enabled, just temporarily in photo mode
                    // Don't restore settings - let camera use its current settings to avoid exceptions
                    // RestorePhotoSettings(); // SKIP THIS - causes delays and exceptions
                    DebugService.LogDebug($"VideoModeLiveView: Successfully switched to photo mode for capture - IsVideoModeActive={_isVideoModeActive}, IsEnabled={IsEnabled}");
                    return true;
                }
                
                DebugService.LogError("VideoModeLiveView: Failed to restore photo mode");
                return false;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error switching to photo mode for capture - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resume video mode after capture
        /// </summary>
        public async Task<bool> ResumeVideoModeAfterCapture()
        {
            try
            {
                DebugService.LogDebug($"VideoModeLiveView: ResumeVideoModeAfterCapture called - IsEnabled={IsEnabled}, IsVideoModeActive={_isVideoModeActive}, HasCamera={_currentCamera != null}");
                
                if (!IsEnabled || _currentCamera == null)
                {
                    DebugService.LogDebug($"VideoModeLiveView: Video mode not enabled or no camera, skipping resume (IsEnabled={IsEnabled})");
                    return true;
                }

                DebugService.LogDebug("VideoModeLiveView: Resuming video mode after capture");
                
                // NO DELAY - capture handles its own timing
                
                // Switch back to video mode
                if (await SwitchToVideoMode())
                {
                    _isVideoModeActive = true;
                    IsEnabled = true; // Important: Ensure IsEnabled is set when resuming
                    
                    // Restart live view
                    if (_currentCamera.GetCapability(CapabilityEnum.LiveView))
                    {
                        _currentCamera.StartLiveView();
                        _isLiveViewRunning = true;
                    }
                    
                    // Reapply video mode settings
                    ApplyVideoModeSettings();
                    
                    DebugService.LogDebug($"VideoModeLiveView: Resumed video mode after capture - IsVideoModeActive={_isVideoModeActive}, IsEnabled={IsEnabled}");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error resuming video mode after capture - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Apply stored video mode settings
        /// </summary>
        private void ApplyVideoModeSettings()
        {
            try
            {
                if (_currentCamera == null || _videoModeSettings == null) return;
                
                if (_currentCamera.IsoNumber != null && !string.IsNullOrEmpty(_videoModeSettings.ISO))
                {
                    if (_currentCamera.IsoNumber.IsEnabled)
                    {
                        _currentCamera.IsoNumber.SetValue(_videoModeSettings.ISO);
                    }
                }
                    
                if (_currentCamera.FNumber != null && !string.IsNullOrEmpty(_videoModeSettings.Aperture))
                {
                    if (_currentCamera.FNumber.IsEnabled)
                    {
                        _currentCamera.FNumber.SetValue(_videoModeSettings.Aperture);
                    }
                }
                    
                if (_currentCamera.ShutterSpeed != null && !string.IsNullOrEmpty(_videoModeSettings.ShutterSpeed))
                {
                    if (_currentCamera.ShutterSpeed.IsEnabled)
                    {
                        _currentCamera.ShutterSpeed.SetValue(_videoModeSettings.ShutterSpeed);
                    }
                }
                    
                DebugService.LogDebug("VideoModeLiveView: Applied video mode settings");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error applying video mode settings - {ex.Message}");
            }
        }

        /// <summary>
        /// Set ISO in real-time during video mode live view
        /// </summary>
        public void SetISO(string isoValue)
        {
            if (!_isVideoModeActive || _currentCamera?.IsoNumber == null) return;
            
            try
            {
                // Check if the property is settable in current mode
                if (_currentCamera.IsoNumber.IsEnabled)
                {
                    // Verify the value exists in allowed values
                    if (_currentCamera.IsoNumber.Values != null && 
                        _currentCamera.IsoNumber.Values.Contains(isoValue))
                    {
                        _currentCamera.IsoNumber.SetValue(isoValue);
                        _videoModeSettings.ISO = isoValue;
                        DebugService.LogDebug($"VideoModeLiveView: ISO set to {isoValue} (real-time)");
                        OnSettingChanged("ISO", isoValue);
                    }
                    else
                    {
                        DebugService.LogDebug($"VideoModeLiveView: ISO value {isoValue} not available in video mode");
                        if (_currentCamera.IsoNumber.Values != null)
                        {
                            DebugService.LogDebug($"Available ISO values: {string.Join(", ", _currentCamera.IsoNumber.Values)}");
                        }
                    }
                }
                else
                {
                    DebugService.LogDebug("VideoModeLiveView: ISO adjustment not available in video mode");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error setting ISO - {ex.Message}");
                // Don't crash on property errors in video mode
                _videoModeSettings.ISO = isoValue; // Still save the intended value
            }
        }

        /// <summary>
        /// Set Aperture in real-time during video mode live view
        /// </summary>
        public void SetAperture(string apertureValue)
        {
            if (!_isVideoModeActive || _currentCamera?.FNumber == null) return;
            
            try
            {
                // Check if the property is settable in current mode
                if (_currentCamera.FNumber.IsEnabled)
                {
                    if (_currentCamera.FNumber.Values != null && 
                        _currentCamera.FNumber.Values.Contains(apertureValue))
                    {
                        _currentCamera.FNumber.SetValue(apertureValue);
                        _videoModeSettings.Aperture = apertureValue;
                        DebugService.LogDebug($"VideoModeLiveView: Aperture set to {apertureValue} (real-time)");
                        OnSettingChanged("Aperture", apertureValue);
                    }
                    else
                    {
                        DebugService.LogDebug($"VideoModeLiveView: Aperture {apertureValue} not available in video mode");
                    }
                }
                else
                {
                    DebugService.LogDebug("VideoModeLiveView: Aperture adjustment not available in video mode");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error setting aperture - {ex.Message}");
                _videoModeSettings.Aperture = apertureValue; // Still save the intended value
            }
        }

        /// <summary>
        /// Set Shutter Speed in real-time during video mode live view
        /// </summary>
        public void SetShutterSpeed(string shutterValue)
        {
            if (!_isVideoModeActive || _currentCamera?.ShutterSpeed == null) return;
            
            try
            {
                // Check if the property is settable in current mode
                if (_currentCamera.ShutterSpeed.IsEnabled)
                {
                    if (_currentCamera.ShutterSpeed.Values != null && 
                        _currentCamera.ShutterSpeed.Values.Contains(shutterValue))
                    {
                        _currentCamera.ShutterSpeed.SetValue(shutterValue);
                        _videoModeSettings.ShutterSpeed = shutterValue;
                        DebugService.LogDebug($"VideoModeLiveView: Shutter speed set to {shutterValue} (real-time)");
                        OnSettingChanged("ShutterSpeed", shutterValue);
                    }
                    else
                    {
                        DebugService.LogDebug($"VideoModeLiveView: Shutter speed {shutterValue} not available in video mode");
                    }
                }
                else
                {
                    DebugService.LogDebug("VideoModeLiveView: Shutter speed adjustment not available in video mode");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error setting shutter speed - {ex.Message}");
                _videoModeSettings.ShutterSpeed = shutterValue; // Still save the intended value
            }
        }

        /// <summary>
        /// Set White Balance in real-time during video mode live view
        /// </summary>
        public void SetWhiteBalance(string wbValue)
        {
            if (!_isVideoModeActive || _currentCamera?.WhiteBalance == null) return;
            
            try
            {
                _currentCamera.WhiteBalance.SetValue(wbValue);
                _videoModeSettings.WhiteBalance = wbValue;
                DebugService.LogDebug($"VideoModeLiveView: White balance set to {wbValue} (real-time)");
                OnSettingChanged("WhiteBalance", wbValue);
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error setting white balance - {ex.Message}");
            }
        }

        /// <summary>
        /// Set Exposure Compensation in real-time during video mode live view
        /// </summary>
        public void SetExposureCompensation(string compensationValue)
        {
            if (!_isVideoModeActive || _currentCamera?.ExposureCompensation == null) return;
            
            try
            {
                _currentCamera.ExposureCompensation.SetValue(compensationValue);
                _videoModeSettings.ExposureCompensation = compensationValue;
                DebugService.LogDebug($"VideoModeLiveView: Exposure compensation set to {compensationValue} (real-time)");
                OnSettingChanged("ExposureCompensation", compensationValue);
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error setting exposure compensation - {ex.Message}");
            }
        }

        /// <summary>
        /// Update camera reference
        /// </summary>
        public void UpdateCameraReference(ICameraDevice camera)
        {
            _currentCamera = camera;
            DebugService.LogDebug($"VideoModeLiveView: Camera updated to {camera?.DeviceName ?? "null"}");
            DebugService.LogDebug($"VideoModeLiveView: Camera.Mode available: {camera?.Mode != null}");
            
            // If we have a camera but video mode is marked as active without proper initialization, fix it
            if (_isVideoModeActive && camera != null && string.IsNullOrEmpty(OriginalCameraMode))
            {
                DebugService.LogDebug("VideoModeLiveView: WARNING - Video mode marked as active but original mode not saved. Resetting.");
                _isVideoModeActive = false;
            }
        }

        /// <summary>
        /// Apply current video mode settings to photo mode (for capture)
        /// </summary>
        public void ApplyVideoSettingsToPhotoMode()
        {
            try
            {
                if (_videoModeSettings != null)
                {
                    DebugService.LogDebug("VideoModeLiveView: Applying video mode settings to photo capture");
                    // This would be called before photo capture to use the adjusted settings
                    var settingsService = CameraSettingsService.Instance;
                    settingsService.OnSettingChanged("ISO", _videoModeSettings.ISO);
                    settingsService.OnSettingChanged("Aperture", _videoModeSettings.Aperture);
                    settingsService.OnSettingChanged("ShutterSpeed", _videoModeSettings.ShutterSpeed);
                    settingsService.OnSettingChanged("WhiteBalance", _videoModeSettings.WhiteBalance);
                    settingsService.OnSettingChanged("ExposureCompensation", _videoModeSettings.ExposureCompensation);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error applying settings to photo mode - {ex.Message}");
            }
        }
        #endregion

        #region Private Methods
        private void InitializeCamera()
        {
            try
            {
                var sessionManager = CameraSessionManager.Instance;
                _currentCamera = sessionManager?.DeviceManager?.SelectedCameraDevice;
                
                if (_currentCamera != null)
                {
                    DebugService.LogDebug($"VideoModeLiveView: Initialized with camera {_currentCamera.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error initializing - {ex.Message}");
            }
        }

        private async Task<bool> SwitchToVideoMode()
        {
            try
            {
                if (_currentCamera?.Mode == null)
                {
                    DebugService.LogError("VideoModeLiveView: Camera mode property not available");
                    return false;
                }

                // Log all available modes for debugging
                if (_currentCamera.Mode.Values != null)
                {
                    var availableModes = string.Join(", ", _currentCamera.Mode.Values);
                    DebugService.LogDebug($"VideoModeLiveView: Available camera modes: {availableModes}");
                }

                // Save original mode
                OriginalCameraMode = _currentCamera.Mode.Value;
                DebugService.LogDebug($"VideoModeLiveView: Current mode: {OriginalCameraMode}");
                
                // Find video mode value (varies by camera model)
                // IMPORTANT: "Photo in Movie" (mode 21) is NOT video mode - it's for taking photos during video
                // We need pure Movie mode (mode 20 for Canon)
                var videoModeValue = _currentCamera.Mode.Values?.FirstOrDefault(v => 
                    v.Equals("Movie", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
                    v == "20" || // Canon SDK numeric value for movie mode
                    v == "movie" || // lowercase variant
                    // Only match "Movie" if it doesn't contain "Photo"
                    (v.ToLower() == "movie")); // Exact match only

                if (!string.IsNullOrEmpty(videoModeValue))
                {
                    DebugService.LogDebug($"VideoModeLiveView: Attempting to switch to video mode: '{videoModeValue}'");
                    _currentCamera.Mode.SetValue(videoModeValue);
                    // NO DELAY - instant mode switch
                    
                    // Verify the switch
                    var newMode = _currentCamera.Mode.Value;
                    DebugService.LogDebug($"VideoModeLiveView: Mode after switch: {newMode}");
                    
                    if (newMode == videoModeValue)
                    {
                        DebugService.LogDebug($"VideoModeLiveView: Successfully switched to video mode '{videoModeValue}'");
                        return true;
                    }
                    else
                    {
                        DebugService.LogError($"VideoModeLiveView: Mode switch failed. Expected: {videoModeValue}, Got: {newMode}");
                        return false;
                    }
                }
                else
                {
                    // No pure movie mode found - camera might need manual mode dial adjustment
                    DebugService.LogDebug("VideoModeLiveView: No pure video/movie mode found in SDK modes");
                    DebugService.LogDebug("VideoModeLiveView: Camera may require manual mode dial adjustment to MOVIE mode");
                    
                    // Check if camera is Canon T6/1300D which requires manual mode switching
                    if (_currentCamera.DeviceName?.Contains("T6") == true || 
                        _currentCamera.DeviceName?.Contains("1300D") == true ||
                        _currentCamera.DeviceName?.Contains("Rebel") == true)
                    {
                        DebugService.LogDebug("VideoModeLiveView: Canon T6/Rebel detected - requires physical mode dial set to MOVIE");
                        DebugService.LogDebug("VideoModeLiveView: Proceeding anyway - assuming camera is manually set to movie mode");
                        
                        // For Canon T6, we proceed even without mode switch as it needs manual adjustment
                        // The camera should be physically set to movie mode
                        return true;
                    }
                    
                    if (_currentCamera.Mode.Values != null)
                    {
                        DebugService.LogError($"VideoModeLiveView: Available modes: {string.Join(", ", _currentCamera.Mode.Values)}");
                    }
                    else
                    {
                        DebugService.LogError("VideoModeLiveView: No camera modes available");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error switching to video mode - {ex.Message}");
                DebugService.LogError($"VideoModeLiveView: Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task<bool> RestorePhotoMode()
        {
            try
            {
                DebugService.LogDebug($"VideoModeLiveView: RestorePhotoMode - Camera.Mode={_currentCamera?.Mode != null}, OriginalMode='{OriginalCameraMode}'");
                
                if (_currentCamera?.Mode == null)
                {
                    DebugService.LogError("VideoModeLiveView: Camera mode property is null, cannot restore");
                    return false;
                }
                
                if (string.IsNullOrEmpty(OriginalCameraMode))
                {
                    // If original mode wasn't saved, assume camera is already in correct mode
                    DebugService.LogDebug("VideoModeLiveView: Original camera mode not saved, assuming camera is already in correct mode");
                    return true;
                }

                DebugService.LogDebug($"VideoModeLiveView: Setting camera mode back to '{OriginalCameraMode}'");
                _currentCamera.Mode.SetValue(OriginalCameraMode);
                // NO DELAY - instant mode switch
                
                // Skip verification for speed - assume it worked
                DebugService.LogDebug($"VideoModeLiveView: Mode switch command sent to '{OriginalCameraMode}'");
                return true;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error restoring photo mode - {ex.Message}");
                DebugService.LogError($"VideoModeLiveView: Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private void SaveCurrentPhotoSettings()
        {
            try
            {
                if (_currentCamera == null) return;
                
                if (_currentCamera.IsoNumber != null)
                    _savedPhotoSettings.ISO = _currentCamera.IsoNumber.Value;
                    
                if (_currentCamera.FNumber != null)
                    _savedPhotoSettings.Aperture = _currentCamera.FNumber.Value;
                    
                if (_currentCamera.ShutterSpeed != null)
                    _savedPhotoSettings.ShutterSpeed = _currentCamera.ShutterSpeed.Value;
                    
                if (_currentCamera.WhiteBalance != null)
                    _savedPhotoSettings.WhiteBalance = _currentCamera.WhiteBalance.Value;
                    
                if (_currentCamera.ExposureCompensation != null)
                    _savedPhotoSettings.ExposureCompensation = _currentCamera.ExposureCompensation.Value;
                    
                DebugService.LogDebug("VideoModeLiveView: Saved current photo settings");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error saving photo settings - {ex.Message}");
            }
        }

        private void RestorePhotoSettings()
        {
            try
            {
                if (_currentCamera == null || _savedPhotoSettings == null) return;
                
                if (_currentCamera.IsoNumber != null && !string.IsNullOrEmpty(_savedPhotoSettings.ISO))
                    _currentCamera.IsoNumber.SetValue(_savedPhotoSettings.ISO);
                    
                if (_currentCamera.FNumber != null && !string.IsNullOrEmpty(_savedPhotoSettings.Aperture))
                    _currentCamera.FNumber.SetValue(_savedPhotoSettings.Aperture);
                    
                if (_currentCamera.ShutterSpeed != null && !string.IsNullOrEmpty(_savedPhotoSettings.ShutterSpeed))
                    _currentCamera.ShutterSpeed.SetValue(_savedPhotoSettings.ShutterSpeed);
                    
                if (_currentCamera.WhiteBalance != null && !string.IsNullOrEmpty(_savedPhotoSettings.WhiteBalance))
                    _currentCamera.WhiteBalance.SetValue(_savedPhotoSettings.WhiteBalance);
                    
                if (_currentCamera.ExposureCompensation != null && !string.IsNullOrEmpty(_savedPhotoSettings.ExposureCompensation))
                    _currentCamera.ExposureCompensation.SetValue(_savedPhotoSettings.ExposureCompensation);
                    
                DebugService.LogDebug("VideoModeLiveView: Restored photo settings");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error restoring photo settings - {ex.Message}");
            }
        }

        private void OnModeChanged(bool videoModeActive)
        {
            ModeChanged?.Invoke(this, new VideoModeLiveViewEventArgs
            {
                IsVideoModeActive = videoModeActive,
                EventType = videoModeActive ? "VideoModeActivated" : "PhotoModeRestored"
            });
        }

        private void OnSettingChanged(string settingName, string value)
        {
            SettingChanged?.Invoke(this, new VideoModeLiveViewEventArgs
            {
                IsVideoModeActive = _isVideoModeActive,
                EventType = "SettingChanged",
                SettingName = settingName,
                SettingValue = value
            });
        }

        private void LogAvailableProperties()
        {
            try
            {
                DebugService.LogDebug("=== Video Mode Property Availability ===");
                
                if (_currentCamera.IsoNumber != null)
                {
                    var isoValues = _currentCamera.IsoNumber.Values != null 
                        ? string.Join(",", _currentCamera.IsoNumber.Values.Take(5)) + "..."
                        : "N/A";
                    DebugService.LogDebug($"ISO: Enabled={_currentCamera.IsoNumber.IsEnabled}, " +
                        $"Current={_currentCamera.IsoNumber.Value}, " +
                        $"Values={isoValues}");
                }
                else
                {
                    DebugService.LogDebug("ISO: Not available");
                }
                
                if (_currentCamera.FNumber != null)
                {
                    DebugService.LogDebug($"Aperture: Enabled={_currentCamera.FNumber.IsEnabled}, " +
                        $"Current={_currentCamera.FNumber.Value}");
                }
                else
                {
                    DebugService.LogDebug("Aperture: Not available");
                }
                
                if (_currentCamera.ShutterSpeed != null)
                {
                    DebugService.LogDebug($"Shutter: Enabled={_currentCamera.ShutterSpeed.IsEnabled}, " +
                        $"Current={_currentCamera.ShutterSpeed.Value}");
                }
                else
                {
                    DebugService.LogDebug("Shutter: Not available");
                }
                
                if (_currentCamera.WhiteBalance != null)
                {
                    DebugService.LogDebug($"WB: Enabled={_currentCamera.WhiteBalance.IsEnabled}, " +
                        $"Current={_currentCamera.WhiteBalance.Value}");
                }
                else
                {
                    DebugService.LogDebug("WB: Not available");
                }
                
                if (_currentCamera.ExposureCompensation != null)
                {
                    DebugService.LogDebug($"Exp Comp: Enabled={_currentCamera.ExposureCompensation.IsEnabled}, " +
                        $"Current={_currentCamera.ExposureCompensation.Value}");
                }
                else
                {
                    DebugService.LogDebug("Exp Comp: Not available");
                }
                
                DebugService.LogDebug("=====================================");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"VideoModeLiveView: Error logging properties - {ex.Message}");
            }
        }
        #endregion
    }

    /// <summary>
    /// Event arguments for video mode live view events
    /// </summary>
    public class VideoModeLiveViewEventArgs : EventArgs
    {
        public bool IsVideoModeActive { get; set; }
        public string EventType { get; set; }
        public string SettingName { get; set; }
        public string SettingValue { get; set; }
    }

    /// <summary>
    /// Video mode settings container
    /// </summary>
    public class VideoModeSettings
    {
        public string Name { get; set; }
        public string ISO { get; set; }
        public string Aperture { get; set; }
        public string ShutterSpeed { get; set; }
        public string WhiteBalance { get; set; }
        public string ExposureCompensation { get; set; }
    }
}