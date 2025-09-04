using System;
using System.Collections.Generic;
using System.Linq;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Live View Camera Control Service
    /// Provides real-time camera setting adjustments that affect live view display
    /// Independent controls for live view without affecting final photo capture settings
    /// </summary>
    public class LiveViewCameraControlService
    {
        #region Singleton
        private static LiveViewCameraControlService _instance;
        private static readonly object _lock = new object();

        public static LiveViewCameraControlService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new LiveViewCameraControlService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Properties
        private ICameraDevice _currentCamera;
        private Dictionary<string, string> _originalSettings;
        private bool _isLiveViewControlActive = false;

        /// <summary>
        /// Enable/disable live view camera controls
        /// </summary>
        public bool IsEnabled 
        { 
            get => _isLiveViewControlActive;
            set 
            {
                if (_isLiveViewControlActive != value)
                {
                    _isLiveViewControlActive = value;
                    if (value)
                        ActivateLiveViewControls();
                    else
                        RestoreOriginalSettings();
                }
            }
        }

        /// <summary>
        /// Current ISO setting for live view
        /// </summary>
        public string CurrentISO { get; private set; } = "Auto";

        /// <summary>
        /// Current aperture setting for live view  
        /// </summary>
        public string CurrentAperture { get; private set; } = "Auto";

        /// <summary>
        /// Current shutter speed for live view
        /// </summary>
        public string CurrentShutterSpeed { get; private set; } = "Auto";

        /// <summary>
        /// Current white balance for live view
        /// </summary>
        public string CurrentWhiteBalance { get; private set; } = "Auto";

        /// <summary>
        /// Current exposure compensation for live view
        /// </summary>
        public string CurrentExposureCompensation { get; private set; } = "0";
        #endregion

        #region Events
        /// <summary>
        /// Fired when live view camera settings change
        /// </summary>
        public event EventHandler<LiveViewSettingChangedEventArgs> SettingChanged;
        #endregion

        #region Constructor
        private LiveViewCameraControlService()
        {
            _originalSettings = new Dictionary<string, string>();
            InitializeCamera();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Set ISO for live view
        /// </summary>
        public void SetISO(string isoValue)
        {
            if (!_isLiveViewControlActive)
            {
                DebugService.LogDebug($"LiveViewCameraControl: ISO change ignored - service not active");
                return;
            }
            
            try
            {
                if (_currentCamera?.IsoNumber != null)
                {
                    var oldValue = _currentCamera.IsoNumber.Value;
                    _currentCamera.IsoNumber.SetValue(isoValue);
                    CurrentISO = isoValue;
                    DebugService.LogDebug($"LiveViewCameraControl: ISO changed from {oldValue} to {isoValue} - Camera: {_currentCamera.DeviceName}");
                    OnSettingChanged("ISO", isoValue);
                }
                else
                {
                    DebugService.LogError($"LiveViewCameraControl: Cannot set ISO - Camera or IsoNumber property is null");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error setting ISO to {isoValue}: {ex.Message}");
            }
        }

        /// <summary>
        /// Set aperture for live view
        /// </summary>
        public void SetAperture(string apertureValue)
        {
            if (!_isLiveViewControlActive) return;
            
            try
            {
                if (_currentCamera?.FNumber != null)
                {
                    // Remove 'f/' prefix if present
                    var cleanValue = apertureValue.Replace("f/", "");
                    _currentCamera.FNumber.SetValue(cleanValue);
                    CurrentAperture = apertureValue;
                    DebugService.LogDebug($"LiveViewCameraControl: Aperture set to {apertureValue}");
                    OnSettingChanged("Aperture", apertureValue);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error setting aperture: {ex.Message}");
            }
        }

        /// <summary>
        /// Set shutter speed for live view
        /// </summary>
        public void SetShutterSpeed(string shutterValue)
        {
            if (!_isLiveViewControlActive) return;
            
            try
            {
                if (_currentCamera?.ShutterSpeed != null)
                {
                    _currentCamera.ShutterSpeed.SetValue(shutterValue);
                    CurrentShutterSpeed = shutterValue;
                    DebugService.LogDebug($"LiveViewCameraControl: Shutter speed set to {shutterValue}");
                    OnSettingChanged("ShutterSpeed", shutterValue);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error setting shutter speed: {ex.Message}");
            }
        }

        /// <summary>
        /// Set white balance for live view
        /// </summary>
        public void SetWhiteBalance(string wbValue)
        {
            if (!_isLiveViewControlActive) return;
            
            try
            {
                if (_currentCamera?.WhiteBalance != null)
                {
                    _currentCamera.WhiteBalance.SetValue(wbValue);
                    CurrentWhiteBalance = wbValue;
                    DebugService.LogDebug($"LiveViewCameraControl: White balance set to {wbValue}");
                    OnSettingChanged("WhiteBalance", wbValue);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error setting white balance: {ex.Message}");
            }
        }

        /// <summary>
        /// Set exposure compensation for live view
        /// </summary>
        public void SetExposureCompensation(string compensationValue)
        {
            if (!_isLiveViewControlActive) return;
            
            try
            {
                if (_currentCamera?.ExposureCompensation != null)
                {
                    _currentCamera.ExposureCompensation.SetValue(compensationValue);
                    CurrentExposureCompensation = compensationValue;
                    DebugService.LogDebug($"LiveViewCameraControl: Exposure compensation set to {compensationValue}");
                    OnSettingChanged("ExposureCompensation", compensationValue);
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error setting exposure compensation: {ex.Message}");
            }
        }

        /// <summary>
        /// Get available ISO values from camera
        /// </summary>
        public List<string> GetAvailableISOValues()
        {
            try
            {
                if (_currentCamera?.IsoNumber?.Values != null)
                {
                    return _currentCamera.IsoNumber.Values.ToList();
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error getting ISO values: {ex.Message}");
            }
            return new List<string> { "Auto", "100", "200", "400", "800", "1600", "3200", "6400" };
        }

        /// <summary>
        /// Get available aperture values from camera
        /// </summary>
        public List<string> GetAvailableApertureValues()
        {
            try
            {
                if (_currentCamera?.FNumber?.Values != null)
                {
                    return _currentCamera.FNumber.Values.Select(v => $"f/{v}").ToList();
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error getting aperture values: {ex.Message}");
            }
            return new List<string> { "Auto", "f/1.4", "f/2.0", "f/2.8", "f/4.0", "f/5.6", "f/8.0", "f/11", "f/16", "f/22" };
        }

        /// <summary>
        /// Get available shutter speed values from camera
        /// </summary>
        public List<string> GetAvailableShutterSpeedValues()
        {
            try
            {
                if (_currentCamera?.ShutterSpeed?.Values != null)
                {
                    return _currentCamera.ShutterSpeed.Values.ToList();
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error getting shutter speed values: {ex.Message}");
            }
            return new List<string> { "Auto", "1/500", "1/250", "1/125", "1/60", "1/30", "1/15", "1/8", "1/4", "1/2", "1" };
        }

        /// <summary>
        /// Get available white balance values from camera
        /// </summary>
        public List<string> GetAvailableWhiteBalanceValues()
        {
            try
            {
                if (_currentCamera?.WhiteBalance?.Values != null)
                {
                    return _currentCamera.WhiteBalance.Values.ToList();
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error getting white balance values: {ex.Message}");
            }
            return new List<string> { "Auto", "Daylight", "Cloudy", "Tungsten", "Fluorescent", "Flash" };
        }

        /// <summary>
        /// Get available exposure compensation values from camera
        /// </summary>
        public List<string> GetAvailableExposureCompensationValues()
        {
            try
            {
                if (_currentCamera?.ExposureCompensation?.Values != null)
                {
                    return _currentCamera.ExposureCompensation.Values.ToList();
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error getting exposure compensation values: {ex.Message}");
            }
            return new List<string> { "-2", "-1.7", "-1.3", "-1", "-0.7", "-0.3", "0", "+0.3", "+0.7", "+1", "+1.3", "+1.7", "+2" };
        }

        /// <summary>
        /// Update camera reference to synchronize with overlay
        /// </summary>
        public void UpdateCameraReference(ICameraDevice camera)
        {
            try
            {
                _currentCamera = camera;
                if (_currentCamera != null)
                {
                    DebugService.LogDebug($"LiveViewCameraControl: Camera reference updated to {_currentCamera.DeviceName}");
                }
                else
                {
                    DebugService.LogDebug("LiveViewCameraControl: Camera reference cleared");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error updating camera reference: {ex.Message}");
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
                    DebugService.LogDebug($"LiveViewCameraControl: Initialized with camera {_currentCamera.DeviceName}");
                }
                else
                {
                    DebugService.LogDebug("LiveViewCameraControl: No camera available");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error initializing camera: {ex.Message}");
            }
        }

        private void ActivateLiveViewControls()
        {
            try
            {
                // Store original settings before making changes
                StoreOriginalSettings();
                DebugService.LogDebug("LiveViewCameraControl: Live view controls activated");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error activating controls: {ex.Message}");
            }
        }

        private void StoreOriginalSettings()
        {
            try
            {
                if (_currentCamera != null)
                {
                    _originalSettings.Clear();
                    
                    if (_currentCamera.IsoNumber != null)
                        _originalSettings["ISO"] = _currentCamera.IsoNumber.Value;
                    
                    if (_currentCamera.FNumber != null)
                        _originalSettings["Aperture"] = _currentCamera.FNumber.Value;
                    
                    if (_currentCamera.ShutterSpeed != null)
                        _originalSettings["ShutterSpeed"] = _currentCamera.ShutterSpeed.Value;
                    
                    if (_currentCamera.WhiteBalance != null)
                        _originalSettings["WhiteBalance"] = _currentCamera.WhiteBalance.Value;
                    
                    if (_currentCamera.ExposureCompensation != null)
                        _originalSettings["ExposureCompensation"] = _currentCamera.ExposureCompensation.Value;

                    DebugService.LogDebug("LiveViewCameraControl: Original settings stored");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error storing original settings: {ex.Message}");
            }
        }

        private void RestoreOriginalSettings()
        {
            try
            {
                if (_currentCamera != null && _originalSettings.Count > 0)
                {
                    foreach (var setting in _originalSettings)
                    {
                        switch (setting.Key)
                        {
                            case "ISO":
                                if (_currentCamera.IsoNumber != null)
                                    _currentCamera.IsoNumber.SetValue(setting.Value);
                                break;
                            case "Aperture":
                                if (_currentCamera.FNumber != null)
                                    _currentCamera.FNumber.SetValue(setting.Value);
                                break;
                            case "ShutterSpeed":
                                if (_currentCamera.ShutterSpeed != null)
                                    _currentCamera.ShutterSpeed.SetValue(setting.Value);
                                break;
                            case "WhiteBalance":
                                if (_currentCamera.WhiteBalance != null)
                                    _currentCamera.WhiteBalance.SetValue(setting.Value);
                                break;
                            case "ExposureCompensation":
                                if (_currentCamera.ExposureCompensation != null)
                                    _currentCamera.ExposureCompensation.SetValue(setting.Value);
                                break;
                        }
                    }
                    DebugService.LogDebug("LiveViewCameraControl: Original settings restored");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LiveViewCameraControl: Error restoring original settings: {ex.Message}");
            }
        }

        private void OnSettingChanged(string settingName, string value)
        {
            SettingChanged?.Invoke(this, new LiveViewSettingChangedEventArgs
            {
                SettingName = settingName,
                Value = value
            });
        }
        #endregion
    }

    /// <summary>
    /// Event arguments for live view setting changes
    /// </summary>
    public class LiveViewSettingChangedEventArgs : EventArgs
    {
        public string SettingName { get; set; }
        public string Value { get; set; }
    }
}