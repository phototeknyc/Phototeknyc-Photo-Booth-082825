using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    public class CameraSettingsService
    {
        private static CameraSettingsService _instance;
        private static readonly object _lock = new object();
        private ICameraDevice _currentCamera;
        private Dictionary<string, object> _pendingSettings;
        private Dictionary<string, object> _defaultSettings;
        private Action<bool> _overlayVisibilityCallback;
        private bool _isInitialized = false;

        public static CameraSettingsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new CameraSettingsService();
                        }
                    }
                }
                return _instance;
            }
        }

        private CameraSettingsService()
        {
            _pendingSettings = new Dictionary<string, object>();
            // IMPORTANT: Default to JPEG Fine, not RAW
            _defaultSettings = new Dictionary<string, object>
            {
                { "ImageQuality", "JPEG Fine" },  // Default to JPEG, not RAW
                { "ISO", "Auto" },
                { "Aperture", "Auto" },
                { "ShutterSpeed", "Auto" },
                { "WhiteBalance", "Auto" },
                { "ExposureCompensation", "0" },
                { "FocusMode", "Auto Focus (AF-S)" }
            };
        }

        public void RegisterOverlayVisibilityCallback(Action<bool> callback)
        {
            _overlayVisibilityCallback = callback;
        }

        public void ShowOverlay()
        {
            // Only initialize camera access when showing the overlay
            if (!_isInitialized)
            {
                InitializeCameraAccess();
            }
            _overlayVisibilityCallback?.Invoke(true);
        }

        public void HideOverlay()
        {
            _overlayVisibilityCallback?.Invoke(false);
        }

        private void InitializeCameraAccess()
        {
            try
            {
                // Only access camera when needed
                var sessionManager = CameraSessionManager.Instance;
                if (sessionManager?.DeviceManager != null)
                {
                    _isInitialized = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Error initializing camera access: {ex.Message}");
            }
        }

        public void LoadAvailableCameras(ComboBox cameraComboBox)
        {
            try
            {
                cameraComboBox.Items.Clear();
                
                // Only access cameras if initialized
                if (!_isInitialized)
                {
                    InitializeCameraAccess();
                }
                
                // Get the device manager from CameraSessionManager singleton
                var sessionManager = CameraSessionManager.Instance;
                var deviceManager = sessionManager?.DeviceManager;
                
                if (deviceManager != null && deviceManager.ConnectedDevices != null)
                {
                    foreach (var device in deviceManager.ConnectedDevices)
                    {
                        cameraComboBox.Items.Add(new ComboBoxItem
                        {
                            Content = device.DeviceName,
                            Tag = device
                        });
                    }

                    if (cameraComboBox.Items.Count > 0)
                    {
                        cameraComboBox.SelectedIndex = 0;
                        _currentCamera = (cameraComboBox.Items[0] as ComboBoxItem)?.Tag as ICameraDevice;
                    }
                }
                else
                {
                    cameraComboBox.Items.Add(new ComboBoxItem { Content = "No cameras detected" });
                    cameraComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Error loading cameras: {ex.Message}");
                cameraComboBox.Items.Add(new ComboBoxItem { Content = "Error loading cameras" });
                cameraComboBox.SelectedIndex = 0;
            }
        }

        public void LoadCurrentSettings(Controls.CameraSettingsOverlay overlay)
        {
            try
            {
                if (_currentCamera == null)
                {
                    ResetToDefaults(overlay);
                    return;
                }

                // Load Image Quality - ensure current setting is displayed
                if (_currentCamera.CompressionSetting != null)
                {
                    var currentQuality = _currentCamera.CompressionSetting.Value;
                    if (!string.IsNullOrEmpty(currentQuality))
                    {
                        // Map camera value to UI option
                        if (currentQuality.Contains("RAW") && currentQuality.Contains("JPEG"))
                            SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "RAW + JPEG Fine");
                        else if (currentQuality.Contains("RAW"))
                            SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "RAW");
                        else if (currentQuality.Contains("Fine"))
                            SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "JPEG Fine");
                        else if (currentQuality.Contains("Normal"))
                            SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "JPEG Normal");
                        else if (currentQuality.Contains("Basic"))
                            SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "JPEG Basic");
                        else
                            SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "JPEG Fine"); // Default to JPEG Fine
                    }
                    else
                    {
                        SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "JPEG Fine"); // Default to JPEG Fine
                    }
                }
                else
                {
                    SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "JPEG Fine"); // Default to JPEG Fine
                }

                // Load ISO values
                if (_currentCamera.IsoNumber != null && _currentCamera.IsoNumber.Values != null)
                {
                    overlay.ISOComboBox.Items.Clear();
                    overlay.ISOComboBox.Items.Add(new ComboBoxItem { Content = "Auto", IsSelected = true });
                    foreach (var iso in _currentCamera.IsoNumber.Values)
                    {
                        overlay.ISOComboBox.Items.Add(new ComboBoxItem { Content = iso });
                    }

                    if (!string.IsNullOrEmpty(_currentCamera.IsoNumber.Value))
                    {
                        SelectComboBoxItemByContent(overlay.ISOComboBox, _currentCamera.IsoNumber.Value);
                    }
                }

                // Load Aperture values
                if (_currentCamera.FNumber != null && _currentCamera.FNumber.Values != null)
                {
                    overlay.ApertureComboBox.Items.Clear();
                    overlay.ApertureComboBox.Items.Add(new ComboBoxItem { Content = "Auto", IsSelected = true });
                    foreach (var aperture in _currentCamera.FNumber.Values)
                    {
                        overlay.ApertureComboBox.Items.Add(new ComboBoxItem { Content = $"f/{aperture}" });
                    }

                    if (!string.IsNullOrEmpty(_currentCamera.FNumber.Value))
                    {
                        SelectComboBoxItemByContent(overlay.ApertureComboBox, $"f/{_currentCamera.FNumber.Value}");
                    }
                }

                // Load Shutter Speed values
                if (_currentCamera.ShutterSpeed != null && _currentCamera.ShutterSpeed.Values != null)
                {
                    overlay.ShutterSpeedComboBox.Items.Clear();
                    overlay.ShutterSpeedComboBox.Items.Add(new ComboBoxItem { Content = "Auto", IsSelected = true });
                    foreach (var speed in _currentCamera.ShutterSpeed.Values)
                    {
                        overlay.ShutterSpeedComboBox.Items.Add(new ComboBoxItem { Content = speed });
                    }

                    if (!string.IsNullOrEmpty(_currentCamera.ShutterSpeed.Value))
                    {
                        SelectComboBoxItemByContent(overlay.ShutterSpeedComboBox, _currentCamera.ShutterSpeed.Value);
                    }
                }

                // Load White Balance values
                if (_currentCamera.WhiteBalance != null && _currentCamera.WhiteBalance.Values != null)
                {
                    overlay.WhiteBalanceComboBox.Items.Clear();
                    overlay.WhiteBalanceComboBox.Items.Add(new ComboBoxItem { Content = "Auto", IsSelected = true });
                    foreach (var wb in _currentCamera.WhiteBalance.Values)
                    {
                        overlay.WhiteBalanceComboBox.Items.Add(new ComboBoxItem { Content = wb });
                    }

                    if (!string.IsNullOrEmpty(_currentCamera.WhiteBalance.Value))
                    {
                        SelectComboBoxItemByContent(overlay.WhiteBalanceComboBox, _currentCamera.WhiteBalance.Value);
                    }
                }

                // Load Exposure Compensation
                if (_currentCamera.ExposureCompensation != null)
                {
                    if (double.TryParse(_currentCamera.ExposureCompensation.Value, out double expValue))
                    {
                        overlay.ExposureCompSlider.Value = expValue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Error loading settings: {ex.Message}");
                ResetToDefaults(overlay);
            }
        }

        public void OnCameraChanged(object selectedItem)
        {
            var comboBoxItem = selectedItem as ComboBoxItem;
            if (comboBoxItem != null)
            {
                _currentCamera = comboBoxItem.Tag as ICameraDevice;
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Camera changed to: {comboBoxItem.Content}");
            }
        }

        public void OnSettingChanged(string settingName, string value)
        {
            _pendingSettings[settingName] = value;
            System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Setting changed: {settingName} = {value}");
        }

        public void ApplySettings(Controls.CameraSettingsOverlay overlay)
        {
            try
            {
                if (_currentCamera == null)
                {
                    System.Diagnostics.Debug.WriteLine("[CameraSettingsService] No camera connected");
                    return;
                }

                foreach (var setting in _pendingSettings)
                {
                    try
                    {
                        switch (setting.Key)
                        {
                            case "ImageQuality":
                                if (_currentCamera.CompressionSetting != null)
                                {
                                    var quality = setting.Value.ToString();
                                    // IMPORTANT: Map UI options to camera values
                                    if (quality == "RAW + JPEG Fine")
                                        _currentCamera.CompressionSetting.SetValue("RAW+L");
                                    else if (quality == "RAW")
                                        _currentCamera.CompressionSetting.SetValue("RAW");
                                    else if (quality == "JPEG Fine")
                                        _currentCamera.CompressionSetting.SetValue("JPEG Fine");
                                    else if (quality == "JPEG Normal")
                                        _currentCamera.CompressionSetting.SetValue("JPEG Normal");
                                    else if (quality == "JPEG Basic")
                                        _currentCamera.CompressionSetting.SetValue("JPEG Basic");
                                    
                                    System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Set image quality to: {quality}");
                                }
                                break;

                            case "ISO":
                                if (_currentCamera.IsoNumber != null && setting.Value.ToString() != "Auto")
                                {
                                    _currentCamera.IsoNumber.SetValue(setting.Value.ToString());
                                }
                                break;

                            case "Aperture":
                                if (_currentCamera.FNumber != null && setting.Value.ToString() != "Auto")
                                {
                                    var aperture = setting.Value.ToString().Replace("f/", "");
                                    _currentCamera.FNumber.SetValue(aperture);
                                }
                                break;

                            case "ShutterSpeed":
                                if (_currentCamera.ShutterSpeed != null && setting.Value.ToString() != "Auto")
                                {
                                    _currentCamera.ShutterSpeed.SetValue(setting.Value.ToString());
                                }
                                break;

                            case "WhiteBalance":
                                if (_currentCamera.WhiteBalance != null && setting.Value.ToString() != "Auto")
                                {
                                    _currentCamera.WhiteBalance.SetValue(setting.Value.ToString());
                                }
                                break;

                            case "ExposureCompensation":
                                if (_currentCamera.ExposureCompensation != null)
                                {
                                    _currentCamera.ExposureCompensation.SetValue(setting.Value.ToString());
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Error applying {setting.Key}: {ex.Message}");
                    }
                }

                _pendingSettings.Clear();
                System.Diagnostics.Debug.WriteLine("[CameraSettingsService] Settings applied successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Error applying settings: {ex.Message}");
                MessageBox.Show($"Error applying camera settings: {ex.Message}", "Camera Settings Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void ResetToDefaults(Controls.CameraSettingsOverlay overlay)
        {
            try
            {
                // IMPORTANT: Reset to JPEG Fine, not RAW
                SelectComboBoxItemByContent(overlay.ImageQualityComboBox, "JPEG Fine");
                SelectComboBoxItemByContent(overlay.ISOComboBox, "Auto");
                SelectComboBoxItemByContent(overlay.ApertureComboBox, "Auto");
                SelectComboBoxItemByContent(overlay.ShutterSpeedComboBox, "Auto");
                SelectComboBoxItemByContent(overlay.WhiteBalanceComboBox, "Auto");
                overlay.ExposureCompSlider.Value = 0;

                _pendingSettings.Clear();
                foreach (var setting in _defaultSettings)
                {
                    _pendingSettings[setting.Key] = setting.Value;
                }

                System.Diagnostics.Debug.WriteLine("[CameraSettingsService] Reset to defaults (JPEG Fine)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsService] Error resetting to defaults: {ex.Message}");
            }
        }

        private void SelectComboBoxItemByContent(ComboBox comboBox, string content)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content.ToString() == content)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }
    }
}