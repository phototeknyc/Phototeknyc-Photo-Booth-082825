using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using Photobooth.Services;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Controls
{
    public partial class CameraSettingsOverlay : UserControl
    {
        private CameraSettingsService _cameraSettingsService;
        private LiveViewEnhancementService _liveViewEnhancementService;
        private VideoModeLiveViewService _videoModeLiveViewService;
        private DualCameraSettingsService _dualSettingsService;
        private bool _isInitialized = false;
        private bool _autoSaveEnabled = true;
        private bool _isPopulatingExposureUI = false; // prevent re-entrancy while populating
        private bool _applyingMaxIsoBoost = false;     // prevent loops when boost adjusts selection
        private DispatcherTimer _liveViewTimer;
        private DispatcherTimer _autoRestartTimer;
        private bool _isLiveViewActive = false;
        private ICameraDevice _currentCamera;
        private bool _liveViewControlsEnabled = false;
        private bool _videoModeActive = false;
        private string _lastLvIso;
        private string _lastLvAperture;
        private string _lastLvShutter;

        // Persistence for Live View tab selections
        private class OverlayPersistedSettings
        {
            public string LiveViewISO { get; set; }
            public string LiveViewAperture { get; set; }
            public string LiveViewShutter { get; set; }
            public bool? MaxIsoBoost { get; set; }
        }

        private string GetOverlaySettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = System.IO.Path.Combine(appData, "Photobooth");
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            return System.IO.Path.Combine(dir, "overlay_settings.json");
        }

        private void SaveOverlaySettings()
        {
            try
            {
                var settings = new OverlayPersistedSettings
                {
                    LiveViewISO = LiveViewISOComboBox?.SelectedItem is string sIso ? sIso : (LiveViewISOComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                    LiveViewAperture = LiveViewApertureComboBox?.SelectedItem is string sAv ? sAv : (LiveViewApertureComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                    LiveViewShutter = LiveViewShutterComboBox?.SelectedItem is string sTv ? sTv : (LiveViewShutterComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                    MaxIsoBoost = MaxIsoBoostToggle?.IsChecked
                };
                var json = System.Text.Json.JsonSerializer.Serialize(settings);
                File.WriteAllText(GetOverlaySettingsPath(), json);
            }
            catch (Exception ex)
            {
                DebugService.LogError($"SaveOverlaySettings error: {ex.Message}");
            }
        }

        private void LoadOverlaySettingsAndApply()
        {
            try
            {
                var path = GetOverlaySettingsPath();
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var settings = System.Text.Json.JsonSerializer.Deserialize<OverlayPersistedSettings>(json);
                if (settings == null) return;

                _isPopulatingExposureUI = true;
                try
                {
                    if (!string.IsNullOrEmpty(settings.LiveViewISO))
                        SelectComboItemSafe(LiveViewISOComboBox, settings.LiveViewISO);
                    if (!string.IsNullOrEmpty(settings.LiveViewAperture))
                        SelectComboItemSafe(LiveViewApertureComboBox, settings.LiveViewAperture);
                    if (!string.IsNullOrEmpty(settings.LiveViewShutter))
                        SelectComboItemSafe(LiveViewShutterComboBox, settings.LiveViewShutter);
                }
                finally
                {
                    _isPopulatingExposureUI = false;
                }

                if (settings.MaxIsoBoost.HasValue && MaxIsoBoostToggle != null)
                    MaxIsoBoostToggle.IsChecked = settings.MaxIsoBoost.Value;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LoadOverlaySettings error: {ex.Message}");
            }
        }

        private void SelectComboItemSafe(ComboBox combo, string value)
        {
            try
            {
                if (combo == null || string.IsNullOrEmpty(value)) return;
                foreach (var item in combo.Items)
                {
                    if (item is string s && s == value)
                    { combo.SelectedItem = item; return; }
                    if (item is ComboBoxItem cbi && cbi.Content?.ToString() == value)
                    { combo.SelectedItem = cbi; return; }
                }
            }
            catch { }
        }

        public CameraSettingsOverlay()
        {
            // Initialize the services BEFORE InitializeComponent to ensure they're available for any events
            _cameraSettingsService = CameraSettingsService.Instance;
            _liveViewEnhancementService = new LiveViewEnhancementService();
            _videoModeLiveViewService = VideoModeLiveViewService.Instance;
            _dualSettingsService = DualCameraSettingsService.Instance;
            
            // Keep enhancement service disabled by default to avoid breaking existing live view
            _liveViewEnhancementService.IsEnabled = false;
            
            InitializeComponent();
            
            // Initialize live view timer
            InitializeLiveView();
            
            // Delay loading settings until the control is fully loaded
            this.Loaded += OnControlLoaded;
            this.Unloaded += OnControlUnloaded;
            
            // Register the overlay visibility callback
            _cameraSettingsService.RegisterOverlayVisibilityCallback(SetVisibility);
        }

        private void InitializeLiveView()
        {
            _liveViewTimer = new DispatcherTimer();
            _liveViewTimer.Interval = TimeSpan.FromMilliseconds(100); // 10 FPS for smooth preview
            _liveViewTimer.Tick += LiveViewTimer_Tick;
            
            // Initialize auto-restart timer for returning to live view after test photo
            _autoRestartTimer = new DispatcherTimer();
            _autoRestartTimer.Interval = TimeSpan.FromSeconds(3); // Show test photo for 3 seconds
            _autoRestartTimer.Tick += AutoRestartTimer_Tick;
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                LoadCameraSettings();
                _isInitialized = true;
                
                // Start live view when control loads
                var sessionManager = CameraSessionManager.Instance;
                _currentCamera = sessionManager?.DeviceManager?.SelectedCameraDevice;
                
                if (_currentCamera != null)
                {
                    StartLiveView();
                    
                    // Initialize video mode service with camera reference
                    if (_videoModeLiveViewService != null)
                    {
                        _videoModeLiveViewService.UpdateCameraReference(_currentCamera);
                        DebugService.LogDebug($"Video mode service initialized with camera: {_currentCamera.DeviceName}");
                    }
                    
                    // Initialize dual settings service
                    if (_dualSettingsService != null)
                    {
                        _dualSettingsService.UpdateCameraReference(_currentCamera);
                        // Don't apply settings on startup - they'll be applied when needed for capture
                        DebugService.LogDebug("Dual settings service initialized with camera reference");
                    }

                    // Set flag to prevent settings changes during UI population
                    _isPopulatingExposureUI = true;

                    // Populate Live View exposure choices from the camera
                    TryPopulateLiveViewIso();
                    TryPopulateLiveViewAperture();
                    TryPopulateLiveViewShutter();

                    // Populate Photo Capture exposure choices from the camera
                    TryPopulatePhotoCaptureIso();
                    TryPopulatePhotoCaptureAperture();
                    TryPopulatePhotoCaptureShutter();

                    // Apply persisted selections
                    LoadOverlaySettingsAndApply();
                    LoadLiveViewSettings();
                    LoadPhotoCaptureSettings();

                    // Clear flag after initialization
                    _isPopulatingExposureUI = false;

                    // Ensure Live View controls are properly enabled
                    UpdateExposureControlsAvailability();
                }
                else
                {
                    DebugService.LogDebug("WARNING: No camera available for video mode initialization");
                    // Disable video mode toggle if no camera
                    if (VideoModeLiveViewToggle != null)
                    {
                        VideoModeLiveViewToggle.IsEnabled = false;
                    }
                }
                
                // Restore video mode toggle state from settings
                RestoreVideoModeToggleState();
            }
        }
        
        private void RestoreVideoModeToggleState()
        {
            try
            {
                // Get saved state from settings
                bool savedVideoModeState = Properties.Settings.Default.VideoModeLiveViewEnabled;
                
                DebugService.LogDebug($"Restoring video mode toggle state: {savedVideoModeState}");
                
                // Apply the saved state to the toggle UI only - don't actually start video mode yet
                // The app should start in photo mode and only switch to video when needed
                if (VideoModeLiveViewToggle != null)
                {
                    VideoModeLiveViewToggle.IsChecked = savedVideoModeState;
                    
                    // Mark the service as enabled but don't start video mode
                    // Video mode will start when a session begins if this is true
                    if (savedVideoModeState)
                    {
                        _videoModeLiveViewService.IsEnabled = true;
                        DebugService.LogDebug("Video mode toggle restored (will activate on session start)");
                    }
                    else
                    {
                        _videoModeLiveViewService.IsEnabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error restoring video mode toggle state: {ex.Message}");
            }
        }

        private void OnControlUnloaded(object sender, RoutedEventArgs e)
        {
            StopLiveView();
        }

        private void LoadCameraSettings()
        {
            // Only load if the control is loaded
            if (this.IsLoaded && _cameraSettingsService != null)
            {
                _cameraSettingsService.LoadAvailableCameras(CameraComboBox);
                _cameraSettingsService.LoadCurrentSettings(this);
            }
        }

        private void SetVisibility(bool isVisible)
        {
            this.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            
            if (isVisible)
            {
                // Start live view when showing
                var sessionManager = CameraSessionManager.Instance;
                _currentCamera = sessionManager?.DeviceManager?.SelectedCameraDevice;
                if (_currentCamera != null)
                {
                    StartLiveView();
                }
            }
            else
            {
                // Stop live view when hiding
                StopLiveView();
            }
        }

        private void LiveViewTimer_Tick(object sender, EventArgs e)
        {
            if (_currentCamera == null || !_isLiveViewActive)
                return;

            try
            {
                // Get live view image from camera
                var liveViewData = _currentCamera.GetLiveViewImage();
                if (liveViewData != null && liveViewData.ImageData != null)
                {
                    byte[] processedImageData = liveViewData.ImageData;

                    // Apply visual enhancements if live view controls are enabled
                    if (_liveViewControlsEnabled && _liveViewEnhancementService != null && _liveViewEnhancementService.IsEnabled)
                    {
                        try
                        {
                            processedImageData = _liveViewEnhancementService.ProcessLiveViewImage(liveViewData.ImageData);
                            DebugService.LogDebug("Live view: Enhancement processing completed successfully");
                        }
                        catch (Exception enhancementEx)
                        {
                            // If enhancement fails, use original image and log error
                            processedImageData = liveViewData.ImageData;
                            DebugService.LogError($"Live view enhancement failed: {enhancementEx.Message}");
                        }
                    }
                    else
                    {
                        // Commented out to reduce debug spam
                        // DebugService.LogDebug($"Live view: Using original image (enhancement enabled: {_liveViewControlsEnabled && _liveViewEnhancementService?.IsEnabled == true})");
                    }

                    // Convert processed image to BitmapImage with proper disposal
                    BitmapImage bitmap;
                    using (var ms = new MemoryStream(processedImageData))
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }

                    // Update the live view image
                    LiveViewImage.Source = bitmap;
                    LiveViewImage.Visibility = Visibility.Visible;
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                    LiveViewStatus.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                // Log live view errors for debugging
                DebugService.LogError($"Live view error: {ex.Message}");
                
                // Show placeholder on error
                LiveViewImage.Visibility = Visibility.Collapsed;
                PreviewPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private void StartLiveView()
        {
            try
            {
                if (_currentCamera == null)
                    return;

                // Check if camera supports live view
                if (!_currentCamera.GetCapability(CapabilityEnum.LiveView))
                {
                    System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Camera does not support live view");
                    PreviewPlaceholder.Visibility = Visibility.Visible;
                    LiveViewImage.Visibility = Visibility.Collapsed;
                    LiveViewStatus.Visibility = Visibility.Collapsed;
                    return;
                }

                // Start live view on camera
                _currentCamera.StartLiveView();
                _isLiveViewActive = true;
                _liveViewTimer.Start();
                
                System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Live view started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Error starting live view: {ex.Message}");
                PreviewPlaceholder.Visibility = Visibility.Visible;
                LiveViewImage.Visibility = Visibility.Collapsed;
                LiveViewStatus.Visibility = Visibility.Collapsed;
            }
        }

        private void StopLiveView()
        {
            try
            {
                _liveViewTimer?.Stop();
                _autoRestartTimer?.Stop();
                _isLiveViewActive = false;

                if (_currentCamera != null)
                {
                    _currentCamera.StopLiveView();
                }
                
                LiveViewStatus.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Live view stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Error stopping live view: {ex.Message}");
            }
        }

        private void AutoRestartTimer_Tick(object sender, EventArgs e)
        {
            _autoRestartTimer.Stop();
            
            // Hide test image and restart live view
            TestImagePreview.Visibility = Visibility.Collapsed;
            TestImagePreview.Source = null; // Clear the image to free memory
            
            // Restart live view
            StartLiveView();
            
            System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Auto-restarted live view after test photo");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            StopLiveView();
            _cameraSettingsService?.HideOverlay();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            StopLiveView();
            _cameraSettingsService?.HideOverlay();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraSettingsService != null)
            {
                _cameraSettingsService.ApplySettings(this);
                _cameraSettingsService.HideOverlay();
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _cameraSettingsService?.ResetToDefaults(this);
        }

        private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Extra defensive null checks
            if (_cameraSettingsService == null || CameraComboBox == null || !_isInitialized)
                return;
                
            _cameraSettingsService.OnCameraChanged(CameraComboBox.SelectedItem);

            // Update current camera reference and repopulate ISO choices
            var sessionManager = CameraSessionManager.Instance;
            _currentCamera = sessionManager?.DeviceManager?.SelectedCameraDevice;
            TryPopulateLiveViewIso();
            TryPopulateLiveViewAperture();
            TryPopulateLiveViewShutter();
            LoadOverlaySettingsAndApply();
            UpdateExposureControlsAvailability();
        }

        private async void SettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            // Skip applying settings during UI population
            if (_isPopulatingExposureUI) return;

            try
            {
                if (PhotoCaptureTab != null && PhotoCaptureTab.IsSelected)
                {
                    _liveViewControlsEnabled = false;  // Disable live view controls
                    if (_videoModeLiveViewService != null && _videoModeLiveViewService.IsVideoModeActive)
                    {
                        await _videoModeLiveViewService.StopVideoModeLiveView();
                    }
                    if (VideoModeLiveViewToggle != null)
                    {
                        VideoModeLiveViewToggle.IsChecked = false;
                    }
                    UpdateControlStates(false);

                    // Don't apply settings during startup - only when user actually switches tabs
                    // The settings will be applied when actually capturing a photo
                    DebugService.LogDebug("Switched to Photo Capture tab - settings will be applied on capture");

                    DebugService.LogDebug("Switched to Photo Capture tab: camera restored to photo mode");
                }
                else if (LiveViewTab != null && LiveViewTab.IsSelected)
                {
                    if (VideoModeLiveViewToggle != null && VideoModeLiveViewToggle.IsChecked == true)
                    {
                        if (_videoModeLiveViewService != null && !_videoModeLiveViewService.IsVideoModeActive)
                        {
                            await _videoModeLiveViewService.StartVideoModeLiveView();
                        }
                        _liveViewControlsEnabled = true;  // Enable live view controls
                        UpdateControlStates(true);
                        DebugService.LogDebug("Switched to Live View tab: ensured video mode active");
                    }
                    else
                    {
                        // Video mode is not active, but we still need to enable the live view controls
                        _liveViewControlsEnabled = true;  // Enable live view controls
                        UpdateControlStates(false);
                        UpdateExposureControlsAvailability();

                        // Don't automatically apply settings - they'll be applied when the user changes them
                        DebugService.LogDebug("Switched to Live View tab: controls enabled without video mode");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"SettingsTabControl_SelectionChanged error: {ex.Message}");
            }
        }

        private void ImageQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Extra defensive null checks
            if (_cameraSettingsService == null || ImageQualityComboBox == null || !_isInitialized)
                return;
                
            if (ImageQualityComboBox.SelectedItem != null)
            {
                var content = (ImageQualityComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    _cameraSettingsService.OnSettingChanged("ImageQuality", content);
                    AutoSaveIfEnabled();
                }
            }
        }

        private void ISOComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're populating the UI
            if (_isPopulatingExposureUI)
                return;

            // Extra defensive null checks
            if (_cameraSettingsService == null || ISOComboBox == null || ISOValueText == null || !_isInitialized)
                return;
                
            if (ISOComboBox.SelectedItem != null)
            {
                // Support both string values (from ItemsSource) and ComboBoxItems (from XAML)
                string content = null;
                if (ISOComboBox.SelectedItem is string str)
                    content = str;
                else if (ISOComboBox.SelectedItem is ComboBoxItem item)
                    content = item.Content?.ToString();

                if (!string.IsNullOrEmpty(content))
                {
                    ISOValueText.Text = content;
                    _cameraSettingsService.OnSettingChanged("ISO", content);
                    
                    // Apply to video mode if active
                    if (_videoModeActive && _videoModeLiveViewService != null)
                    {
                        _videoModeLiveViewService.SetISO(content);
                    }
                    // Or apply visual simulation if enhancement is enabled
                    else if (_liveViewControlsEnabled && _liveViewEnhancementService != null)
                    {
                        if (int.TryParse(content, out int isoValue))
                        {
                            _liveViewEnhancementService.SetSimulatedISO(isoValue);
                        }
                    }
                    
                    // Update photo capture settings (this is the Photo Capture tab control)
                    if (_dualSettingsService != null)
                    {
                        _dualSettingsService.PhotoCaptureSettings.ISO = content;
                        _dualSettingsService.SaveSettingsToStorage();
                        DebugService.LogDebug($"Photo Capture ISO set to {content}");
                    }

                    AutoSaveIfEnabled();
                }
            }
        }

        private void ApertureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're populating the UI
            if (_isPopulatingExposureUI)
                return;

            // Extra defensive null checks
            if (_cameraSettingsService == null || ApertureComboBox == null || ApertureValueText == null || !_isInitialized)
                return;
                
            if (ApertureComboBox.SelectedItem != null)
            {
                // Support both string values (from ItemsSource) and ComboBoxItems (from XAML)
                string content = null;
                if (ApertureComboBox.SelectedItem is string str)
                    content = str;
                else if (ApertureComboBox.SelectedItem is ComboBoxItem item)
                    content = item.Content?.ToString();

                if (!string.IsNullOrEmpty(content))
                {
                    ApertureValueText.Text = content;
                    
                    // Handle based on current mode
                    if (_videoModeActive && _videoModeLiveViewService != null)
                    {
                        // In video mode, apply directly to camera for real-time effect
                        _videoModeLiveViewService.SetAperture(content);
                        DebugService.LogDebug($"Video mode: Aperture set to {content} (real-time)");
                    }
                    
                    // Update appropriate settings profile based on dual settings mode
                    if (_dualSettingsService != null)
                    {
                        switch (_dualSettingsService.CurrentMode)
                        {
                            case DualCameraSettingsService.SettingsMode.LiveView:
                                _dualSettingsService.SetLiveViewSetting("Aperture", content);
                                // Also apply visual simulation
                                if (_liveViewEnhancementService != null)
                                {
                                    var cleanValue = content.Replace("f/", "");
                                    if (double.TryParse(cleanValue, out double apertureValue))
                                    {
                                        _liveViewEnhancementService.SetSimulatedAperture(apertureValue);
                                    }
                                }
                                break;
                            case DualCameraSettingsService.SettingsMode.PhotoCapture:
                                _dualSettingsService.SetPhotoCaptureSetting("Aperture", content);
                                _cameraSettingsService.OnSettingChanged("Aperture", content);
                                break;
                            case DualCameraSettingsService.SettingsMode.Synchronized:
                                _dualSettingsService.SetLiveViewSetting("Aperture", content);
                                _dualSettingsService.SetPhotoCaptureSetting("Aperture", content);
                                _cameraSettingsService.OnSettingChanged("Aperture", content);
                                if (_liveViewEnhancementService != null)
                                {
                                    var cleanValue = content.Replace("f/", "");
                                    if (double.TryParse(cleanValue, out double apertureValue))
                                    {
                                        _liveViewEnhancementService.SetSimulatedAperture(apertureValue);
                                    }
                                }
                                break;
                        }
                    }
                    else
                    {
                        // Fallback to original behavior
                        _cameraSettingsService.OnSettingChanged("Aperture", content);
                    }

                    // Update photo capture settings (this is the Photo Capture tab control)
                    if (_dualSettingsService != null)
                    {
                        _dualSettingsService.PhotoCaptureSettings.Aperture = content;
                        _dualSettingsService.SaveSettingsToStorage();
                        DebugService.LogDebug($"Photo Capture Aperture set to {content}");
                    }

                    AutoSaveIfEnabled();
                }
            }
        }

        private void ShutterSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DebugService.LogDebug($"ShutterSpeedComboBox_SelectionChanged fired - _isInitialized={_isInitialized}, _isPopulatingExposureUI={_isPopulatingExposureUI}");

            // Skip if we're populating the UI
            if (_isPopulatingExposureUI)
            {
                DebugService.LogDebug("ShutterSpeed selection changed skipped - currently populating UI");
                return;
            }

            // Extra defensive null checks
            if (_cameraSettingsService == null || ShutterSpeedComboBox == null || ShutterSpeedValueText == null || !_isInitialized)
            {
                DebugService.LogDebug($"ShutterSpeed selection changed skipped - cameraSettingsService null: {_cameraSettingsService == null}, ComboBox null: {ShutterSpeedComboBox == null}, ValueText null: {ShutterSpeedValueText == null}, not initialized: {!_isInitialized}");
                return;
            }
                
            if (ShutterSpeedComboBox.SelectedItem != null)
            {
                DebugService.LogDebug($"ShutterSpeed SelectedItem type: {ShutterSpeedComboBox.SelectedItem.GetType().Name}, value: {ShutterSpeedComboBox.SelectedItem}");

                // Support both string values (from ItemsSource) and ComboBoxItems (from XAML)
                string content = null;
                if (ShutterSpeedComboBox.SelectedItem is string str)
                {
                    content = str;
                    DebugService.LogDebug($"ShutterSpeed selected as string: {content}");
                }
                else if (ShutterSpeedComboBox.SelectedItem is ComboBoxItem item)
                {
                    content = item.Content?.ToString();
                    DebugService.LogDebug($"ShutterSpeed selected as ComboBoxItem: {content}");
                }

                if (!string.IsNullOrEmpty(content))
                {
                    ShutterSpeedValueText.Text = content;
                    
                    // Handle based on current mode
                    if (_videoModeActive && _videoModeLiveViewService != null)
                    {
                        // In video mode, apply directly to camera for real-time effect
                        _videoModeLiveViewService.SetShutterSpeed(content);
                        DebugService.LogDebug($"Video mode: Shutter speed set to {content} (real-time)");
                    }
                    
                    // Update appropriate settings profile based on dual settings mode
                    if (_dualSettingsService != null)
                    {
                        switch (_dualSettingsService.CurrentMode)
                        {
                            case DualCameraSettingsService.SettingsMode.LiveView:
                                _dualSettingsService.SetLiveViewSetting("ShutterSpeed", content);
                                // Also apply visual simulation
                                if (_liveViewEnhancementService != null)
                                {
                                    _liveViewEnhancementService.SetSimulatedShutterSpeed(content);
                                }
                                break;
                            case DualCameraSettingsService.SettingsMode.PhotoCapture:
                                _dualSettingsService.SetPhotoCaptureSetting("ShutterSpeed", content);
                                _cameraSettingsService.OnSettingChanged("ShutterSpeed", content);
                                break;
                            case DualCameraSettingsService.SettingsMode.Synchronized:
                                _dualSettingsService.SetLiveViewSetting("ShutterSpeed", content);
                                _dualSettingsService.SetPhotoCaptureSetting("ShutterSpeed", content);
                                _cameraSettingsService.OnSettingChanged("ShutterSpeed", content);
                                if (_liveViewEnhancementService != null)
                                {
                                    _liveViewEnhancementService.SetSimulatedShutterSpeed(content);
                                }
                                break;
                        }
                    }
                    else
                    {
                        // Fallback to original behavior
                        _cameraSettingsService.OnSettingChanged("ShutterSpeed", content);
                    }

                    // Update photo capture settings (this is the Photo Capture tab control)
                    if (_dualSettingsService != null)
                    {
                        _dualSettingsService.PhotoCaptureSettings.ShutterSpeed = content;
                        _dualSettingsService.SaveSettingsToStorage();
                        DebugService.LogDebug($"Photo Capture Shutter Speed set to {content} and saved to storage");
                    }

                    AutoSaveIfEnabled();
                }
            }
        }

        private void WhiteBalanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Extra defensive null checks
            if (_cameraSettingsService == null || WhiteBalanceComboBox == null || WhiteBalanceValueText == null || !_isInitialized)
                return;
                
            if (WhiteBalanceComboBox.SelectedItem != null)
            {
                var content = (WhiteBalanceComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    WhiteBalanceValueText.Text = content;
                    
                    // Handle based on current mode
                    if (_videoModeActive && _videoModeLiveViewService != null)
                    {
                        // In video mode, apply directly to camera for real-time effect
                        _videoModeLiveViewService.SetWhiteBalance(content);
                        DebugService.LogDebug($"Video mode: White balance set to {content} (real-time)");
                    }
                    
                    // Update appropriate settings profile based on dual settings mode
                    if (_dualSettingsService != null)
                    {
                        switch (_dualSettingsService.CurrentMode)
                        {
                            case DualCameraSettingsService.SettingsMode.LiveView:
                                _dualSettingsService.SetLiveViewSetting("WhiteBalance", content);
                                // White balance can be applied directly even in photo mode
                                _cameraSettingsService.OnSettingChanged("WhiteBalance", content);
                                break;
                            case DualCameraSettingsService.SettingsMode.PhotoCapture:
                                _dualSettingsService.SetPhotoCaptureSetting("WhiteBalance", content);
                                _cameraSettingsService.OnSettingChanged("WhiteBalance", content);
                                break;
                            case DualCameraSettingsService.SettingsMode.Synchronized:
                                _dualSettingsService.SetLiveViewSetting("WhiteBalance", content);
                                _dualSettingsService.SetPhotoCaptureSetting("WhiteBalance", content);
                                _cameraSettingsService.OnSettingChanged("WhiteBalance", content);
                                break;
                        }
                    }
                    else
                    {
                        // Fallback to original behavior
                        _cameraSettingsService.OnSettingChanged("WhiteBalance", content);
                    }
                    
                    AutoSaveIfEnabled();
                }
            }
        }

        private void ExposureCompSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Extra defensive null checks
            if (_cameraSettingsService == null || ExposureCompSlider == null || ExposureCompValueText == null || !_isInitialized)
                return;
                
            var value = Math.Round(ExposureCompSlider.Value, 1);
            var displayValue = value >= 0 ? $"+{value} EV" : $"{value} EV";
            ExposureCompValueText.Text = displayValue;
            
            // Handle based on current mode
            if (_videoModeActive && _videoModeLiveViewService != null)
            {
                // In video mode, apply directly to camera for real-time effect
                _videoModeLiveViewService.SetExposureCompensation(value.ToString());
                DebugService.LogDebug($"Video mode: Exposure compensation set to {value:+0.0;-0.0;0} EV (real-time)");
            }
            
            // Update appropriate settings profile based on dual settings mode
            if (_dualSettingsService != null)
            {
                switch (_dualSettingsService.CurrentMode)
                {
                    case DualCameraSettingsService.SettingsMode.LiveView:
                        _dualSettingsService.SetLiveViewSetting("ExposureCompensation", value.ToString());
                        // Also apply visual simulation
                        if (_liveViewEnhancementService != null)
                        {
                            _liveViewEnhancementService.SetSimulatedExposureCompensation(value);
                        }
                        break;
                    case DualCameraSettingsService.SettingsMode.PhotoCapture:
                        _dualSettingsService.SetPhotoCaptureSetting("ExposureCompensation", value.ToString());
                        _cameraSettingsService.OnSettingChanged("ExposureCompensation", value.ToString());
                        break;
                    case DualCameraSettingsService.SettingsMode.Synchronized:
                        _dualSettingsService.SetLiveViewSetting("ExposureCompensation", value.ToString());
                        _dualSettingsService.SetPhotoCaptureSetting("ExposureCompensation", value.ToString());
                        _cameraSettingsService.OnSettingChanged("ExposureCompensation", value.ToString());
                        if (_liveViewEnhancementService != null)
                        {
                            _liveViewEnhancementService.SetSimulatedExposureCompensation(value);
                        }
                        break;
                }
            }
            else
            {
                // Fallback to original behavior
                _cameraSettingsService.OnSettingChanged("ExposureCompensation", value.ToString());
            }
            
            AutoSaveIfEnabled();
        }

        private void AutoSaveIfEnabled()
        {
            if (_autoSaveEnabled && _cameraSettingsService != null)
            {
                // Apply settings immediately when changed
                _cameraSettingsService.ApplySettings(this);
                System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Auto-saved camera settings");
            }
        }

        private async void TestPictureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Apply photo capture settings before taking test photo
                if (_dualSettingsService != null && _currentCamera != null)
                {
                    _dualSettingsService.UpdateCameraReference(_currentCamera);
                    _dualSettingsService.ApplyPhotoCaptureSettings();
                    DebugService.LogDebug($"Test Photo: Applied photo settings - ISO: {_dualSettingsService.PhotoCaptureSettings.ISO}, Aperture: {_dualSettingsService.PhotoCaptureSettings.Aperture}, Shutter: {_dualSettingsService.PhotoCaptureSettings.ShutterSpeed}");
                }

                // Get the camera session manager
                var sessionManager = CameraSessionManager.Instance;
                var camera = sessionManager?.DeviceManager?.SelectedCameraDevice;
                
                if (camera == null)
                {
                    MessageBox.Show("No camera connected. Please connect a camera first.", 
                        "Camera Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Stop live view and auto-restart timer before capture
                StopLiveView();
                _autoRestartTimer?.Stop();
                
                // Wait a moment to ensure live view is fully stopped and camera is ready for actual capture
                await System.Threading.Tasks.Task.Delay(500);

                // Disable button during capture
                if (LargeTestPictureButton != null)
                {
                    LargeTestPictureButton.IsEnabled = false;
                    LargeTestPictureButton.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = 
                        {
                            new TextBlock { Text = "📸", FontSize = 24, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = "CAPTURING...", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                        }
                    };
                }

                // Apply current settings first
                if (_cameraSettingsService != null)
                {
                    _cameraSettingsService.ApplySettings(this);
                }

                // Create a task completion source to wait for the photo
                var photoCompletionSource = new System.Threading.Tasks.TaskCompletionSource<string>();
                
                // Subscribe to PhotoCaptured event
                CameraControl.Devices.Classes.PhotoCapturedEventHandler captureHandler = null;
                captureHandler = new CameraControl.Devices.Classes.PhotoCapturedEventHandler((o, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] PhotoCaptured event received - File: {args?.FileName}");
                    
                    // Unsubscribe immediately
                    camera.PhotoCaptured -= captureHandler;
                    
                    try
                    {
                        // Create test photo folder
                        string testPhotosFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                            "PhotoBooth", "TestPhotos"
                        );
                        
                        if (!Directory.Exists(testPhotosFolder))
                        {
                            Directory.CreateDirectory(testPhotosFolder);
                        }
                        
                        // Create filename for test photo
                        string fileName = Path.Combine(testPhotosFolder, 
                            $"Test_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(args.FileName)}");
                        
                        System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Transferring file to: {fileName}");
                        
                        // Transfer the file using camera SDK (same as CameraSettings page)
                        args.CameraDevice.TransferFile(args.Handle, fileName);
                        args.CameraDevice.IsBusy = false;
                        
                        // Check if file was created
                        if (File.Exists(fileName))
                        {
                            System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] File transferred successfully: {fileName}");
                            photoCompletionSource.SetResult(fileName);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] File transfer failed - file not created");
                            photoCompletionSource.SetResult(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Transfer error: {ex.Message}");
                        if (args?.CameraDevice != null)
                        {
                            args.CameraDevice.IsBusy = false;
                        }
                        photoCompletionSource.SetResult(null);
                    }
                });
                
                // Subscribe to the event
                camera.PhotoCaptured += captureHandler;
                
                // Suppress insertion into main workflow for this test capture
                try { CameraSessionManager.Instance.SuppressNextCameraCaptureOnce(); } catch { }

                // Capture photo
                System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Calling camera.CapturePhoto()");
                camera.CapturePhoto();
                
                // Wait for the photo with timeout
                var timeoutTask = System.Threading.Tasks.Task.Delay(10000); // 10 second timeout
                var photoTask = photoCompletionSource.Task;
                
                var completedTask = await System.Threading.Tasks.Task.WhenAny(photoTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    // Timeout - unsubscribe and show message
                    camera.PhotoCaptured -= captureHandler;
                    System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Photo capture timed out");
                    MessageBox.Show("Photo capture timed out. Please check camera connection.", 
                        "Capture Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    // Got the photo path
                    string testPhotoPath = await photoTask;
                    
                    if (!string.IsNullOrEmpty(testPhotoPath) && File.Exists(testPhotoPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Displaying test photo: {testPhotoPath}");
                        UpdatePreviewImage(testPhotoPath);
                        // Don't restart live view - keep showing the test photo
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[CameraSettingsOverlay] Photo transfer failed");
                        MessageBox.Show("Failed to capture test photo. Please check camera connection and try again.", 
                            "Capture Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        // Restart live view since capture failed
                        StartLiveView();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing test photo: {ex.Message}", 
                    "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Test capture error: {ex}");
            }
            finally
            {
                // Re-enable button
                if (LargeTestPictureButton != null)
                {
                    LargeTestPictureButton.IsEnabled = true;
                    LargeTestPictureButton.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children = 
                        {
                            new TextBlock { Text = "📷", FontSize = 24, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = "TAKE TEST PICTURE", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                        }
                    };
                }
            }
        }

        private string GetLastCapturedPhoto()
        {
            try
            {
                // Check the session capture folder
                string captureFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "PhotoBooth", "Sessions", DateTime.Now.ToString("yyyy-MM-dd")
                );

                if (Directory.Exists(captureFolder))
                {
                    var files = Directory.GetFiles(captureFolder, "*.jpg")
                        .Concat(Directory.GetFiles(captureFolder, "*.jpeg"))
                        .Concat(Directory.GetFiles(captureFolder, "*.png"))
                        .Where(f => !f.Contains("_thumb") && 
                                   !f.Contains("_print") && 
                                   !f.Contains("template") && 
                                   !f.Contains("_with_template") &&
                                   !f.Contains("_final"))
                        .OrderByDescending(f => new FileInfo(f).CreationTime)
                        .FirstOrDefault();
                    
                    if (!string.IsNullOrEmpty(files))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Found test photo: {files}");
                        return files;
                    }
                }


                // Check today's folder with MMddyy format (e.g., 083025)
                string todayFolder = DateTime.Now.ToString("MMddyy");
                string todayPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "PhotoBooth", todayFolder
                );
                
                if (Directory.Exists(todayPath))
                {
                    // Look for actual photos, not thumbnails, templates, or print files
                    var files = Directory.GetFiles(todayPath, "*.jpg")
                        .Concat(Directory.GetFiles(todayPath, "*.jpeg"))
                        .Concat(Directory.GetFiles(todayPath, "*.png"))
                        .Where(f => !f.Contains("_thumb") && 
                                   !f.Contains("_print") && 
                                   !f.Contains("template") && 
                                   !f.Contains("_with_template") &&
                                   !f.Contains("_final") &&
                                   !Path.GetDirectoryName(f).EndsWith("print"))
                        .OrderByDescending(f => new FileInfo(f).CreationTime)
                        .FirstOrDefault();
                    
                    if (!string.IsNullOrEmpty(files))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Found test photo in today's folder: {files}");
                        return files;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Error finding last photo: {ex.Message}");
            }

            return null;
        }

        private string FindPhotoByName(string filename)
        {
            try
            {
                // Search multiple possible locations for the file
                var searchPaths = new List<string>();
                
                // Add today's folder
                string todayFolder = DateTime.Now.ToString("MMddyy");
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", todayFolder));
                
                // Add generic PhotoBooth folder
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth"));
                
                // Add Documents PhotoBooth folder
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhotoBooth"));
                
                // Add Desktop 
                searchPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                
                // Add Pictures folder
                searchPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                
                // Add temp folder (some cameras save here temporarily)
                searchPaths.Add(Path.GetTempPath());
                
                foreach (var searchPath in searchPaths)
                {
                    if (Directory.Exists(searchPath))
                    {
                        // Look for the file directly in this folder
                        string directPath = Path.Combine(searchPath, filename);
                        if (File.Exists(directPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Found photo at: {directPath}");
                            return directPath;
                        }
                        
                        // Search in subdirectories (1 level deep to avoid long searches)
                        try
                        {
                            var subDirs = Directory.GetDirectories(searchPath);
                            foreach (var subDir in subDirs)
                            {
                                string subPath = Path.Combine(subDir, filename);
                                if (File.Exists(subPath))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Found photo at: {subPath}");
                                    return subPath;
                                }
                            }
                        }
                        catch { } // Ignore access denied errors
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Could not find file: {filename}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Error in FindPhotoByName: {ex.Message}");
            }
            
            return null;
        }

        private string FindRecentPhoto()
        {
            try
            {
                // Search multiple possible locations for recent photos
                var searchPaths = new List<string>();
                
                // Add today's folder
                string todayFolder = DateTime.Now.ToString("MMddyy");
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", todayFolder));
                
                // Add generic PhotoBooth folder
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth"));
                
                // Add Documents PhotoBooth folder
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhotoBooth"));
                
                // Add Desktop as some cameras might save there
                searchPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                
                foreach (var searchPath in searchPaths)
                {
                    if (Directory.Exists(searchPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Searching in: {searchPath}");
                        
                        var recentFiles = Directory.GetFiles(searchPath, "*.jpg", SearchOption.AllDirectories)
                            .Concat(Directory.GetFiles(searchPath, "*.jpeg", SearchOption.AllDirectories))
                            .Where(f => !f.Contains("_thumb") && 
                                       !f.Contains("_print") && 
                                       !f.Contains("template") && 
                                       !f.Contains("_with_template") &&
                                       !f.Contains("_final"))
                            .Select(f => new FileInfo(f))
                            .Where(fi => fi.CreationTime > DateTime.Now.AddMinutes(-5)) // Only files from last 5 minutes
                            .OrderByDescending(fi => fi.CreationTime)
                            .FirstOrDefault();
                            
                        if (recentFiles != null)
                        {
                            return recentFiles.FullName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Error in FindRecentPhoto: {ex.Message}");
            }
            
            return null;
        }

        private void ReturnToLiveViewButton_Click(object sender, RoutedEventArgs e)
        {
            // Hide test image and return to live view
            if (TestImagePreview != null)
            {
                TestImagePreview.Visibility = Visibility.Collapsed;
            }
            
            // Return button removed - using simplified flow
            
            // Restart live view
            StartLiveView();
        }

        private void UpdatePreviewImage(string photoPath)
        {
            try
            {
                // Load the image
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(photoPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                // Update the UI - hide live view and show test image
                if (TestImagePreview != null)
                {
                    TestImagePreview.Source = bitmap;
                    TestImagePreview.Visibility = Visibility.Visible;
                }
                
                if (LiveViewImage != null)
                {
                    LiveViewImage.Visibility = Visibility.Collapsed;
                }
                
                if (LiveViewStatus != null)
                {
                    LiveViewStatus.Visibility = Visibility.Collapsed;
                }
                
                if (PreviewPlaceholder != null)
                {
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                }
                
                // Start timer to automatically return to live view after 3 seconds
                _autoRestartTimer?.Stop(); // Stop any existing timer
                _autoRestartTimer?.Start();
                
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Preview updated with: {photoPath}, will return to live view in 3 seconds");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSettingsOverlay] Error updating preview: {ex.Message}");
                MessageBox.Show($"Error displaying test photo: {ex.Message}", 
                    "Preview Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Try to restart live view if preview fails
                StartLiveView();
            }
        }
        
        #region Video Mode Live View Controls
        
        private async void VideoModeLiveViewToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if camera is available
                if (_currentCamera == null)
                {
                    DebugService.LogError("Cannot enable video mode: No camera connected");
                    VideoModeLiveViewToggle.IsChecked = false;
                    
                    // Show user feedback
                    if (VideoModeLiveViewToggle != null)
                    {
                        VideoModeLiveViewToggle.ToolTip = "No camera connected";
                    }
                    return;
                }
                
                // Update camera reference
                _videoModeLiveViewService.UpdateCameraReference(_currentCamera);
                _dualSettingsService.UpdateCameraReference(_currentCamera);
                
                DebugService.LogDebug($"Attempting to start video mode for camera: {_currentCamera.DeviceName}");
                
                // Start video mode live view
                if (await _videoModeLiveViewService.StartVideoModeLiveView())
                {
                    _videoModeActive = true;
                    _dualSettingsService.ApplyLiveViewSettings();
                    UpdateControlStates(true);
                    DebugService.LogDebug("Video mode live view enabled with real-time camera control");
                    
                    if (VideoModeLiveViewToggle != null)
                    {
                        VideoModeLiveViewToggle.ToolTip = "Video mode active - Real-time control enabled";
                    }
                    
                    // Save the enabled state
                    Properties.Settings.Default.VideoModeLiveViewEnabled = true;
                    Properties.Settings.Default.Save();
                    DebugService.LogDebug("Video mode toggle state saved: Enabled");
                }
                else
                {
                    // Failed to start, uncheck the toggle
                    VideoModeLiveViewToggle.IsChecked = false;
                    DebugService.LogError("Failed to enable video mode live view - camera may not support video mode");
                    
                    if (VideoModeLiveViewToggle != null)
                    {
                        VideoModeLiveViewToggle.ToolTip = "Camera does not support video mode";
                    }
                    
                    // Save the disabled state
                    Properties.Settings.Default.VideoModeLiveViewEnabled = false;
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                VideoModeLiveViewToggle.IsChecked = false;
                DebugService.LogError($"Error enabling video mode: {ex.Message}");
                
                if (VideoModeLiveViewToggle != null)
                {
                    VideoModeLiveViewToggle.ToolTip = $"Error: {ex.Message}";
                }
            }
        }

        private async void VideoModeLiveViewToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop video mode and restore photo mode
                if (await _videoModeLiveViewService.StopVideoModeLiveView())
                {
                    _videoModeActive = false;
                    _dualSettingsService.ApplyPhotoCaptureSettings();
                    UpdateControlStates(false);
                    DebugService.LogDebug("Video mode live view disabled, photo mode restored");
                    
                    // Save the disabled state
                    Properties.Settings.Default.VideoModeLiveViewEnabled = false;
                    Properties.Settings.Default.Save();
                    DebugService.LogDebug("Video mode toggle state saved: Disabled");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error disabling video mode: {ex.Message}");
            }
        }

        private void SettingsModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsModeCombo?.SelectedItem is ComboBoxItem item)
            {
                var mode = item.Tag?.ToString();
                switch (mode)
                {
                    case "LiveView":
                        _dualSettingsService.CurrentMode = DualCameraSettingsService.SettingsMode.LiveView;
                        DebugService.LogDebug("Switched to Live View settings mode");
                        break;
                    case "PhotoCapture":
                        _dualSettingsService.CurrentMode = DualCameraSettingsService.SettingsMode.PhotoCapture;
                        DebugService.LogDebug("Switched to Photo Capture settings mode");
                        break;
                    case "Synchronized":
                        _dualSettingsService.CurrentMode = DualCameraSettingsService.SettingsMode.Synchronized;
                        DebugService.LogDebug("Switched to Synchronized settings mode");
                        break;
                }
            }
        }

        private void ApplyToPhotoBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Apply current video mode settings to photo capture settings
                _videoModeLiveViewService.ApplyVideoSettingsToPhotoMode();
                DebugService.LogDebug("Applied video mode settings to photo capture");
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error applying settings: {ex.Message}");
            }
        }

        private void SyncSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Synchronize settings between profiles
                if (_dualSettingsService.CurrentMode == DualCameraSettingsService.SettingsMode.LiveView)
                {
                    _dualSettingsService.SynchronizeSettings(DualCameraSettingsService.SyncDirection.LiveViewToPhoto);
                    DebugService.LogDebug("Synchronized live view settings to photo capture");
                }
                else
                {
                    _dualSettingsService.SynchronizeSettings(DualCameraSettingsService.SyncDirection.PhotoToLiveView);
                    DebugService.LogDebug("Synchronized photo capture settings to live view");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Error synchronizing settings: {ex.Message}");
            }
        }

        private void UpdateControlStates(bool videoModeActive)
        {
            // Update UI based on video mode state
            ApplyToPhotoBtn.IsEnabled = videoModeActive;
            SyncSettingsBtn.IsEnabled = true;
            UpdateExposureControlsAvailability();
            
            // Update status text
            if (videoModeActive)
            {
                // Could update a status label if you have one
                DebugService.LogDebug("Controls updated for video mode");
            }
        }

        private void UpdateExposureControlsAvailability()
        {
            try
            {
                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;

                // Live view controls should always be available when on the Live View tab,
                // regardless of video mode status, so we can apply different settings for preview
                bool onLiveViewTab = LiveViewTab != null && LiveViewTab.IsSelected;

                // ISO
                bool isoEnabled = onLiveViewTab && cam?.IsoNumber != null && cam.IsoNumber.Available && cam.IsoNumber.IsEnabled;
                if (LiveViewISOComboBox != null)
                {
                    LiveViewISOComboBox.IsEnabled = isoEnabled;
                    LiveViewISOComboBox.ToolTip = isoEnabled ? null : "ISO not available";
                }
                if (MaxIsoBoostToggle != null)
                {
                    MaxIsoBoostToggle.IsEnabled = isoEnabled;
                }

                // Aperture
                bool avEnabled = onLiveViewTab && cam?.FNumber != null && cam.FNumber.Available && cam.FNumber.IsEnabled;
                if (LiveViewApertureComboBox != null)
                {
                    LiveViewApertureComboBox.IsEnabled = avEnabled;
                    LiveViewApertureComboBox.ToolTip = avEnabled ? null : "Aperture not available";
                }

                // Shutter
                bool tvEnabled = onLiveViewTab && cam?.ShutterSpeed != null && cam.ShutterSpeed.Available && cam.ShutterSpeed.IsEnabled;
                if (LiveViewShutterComboBox != null)
                {
                    LiveViewShutterComboBox.IsEnabled = tvEnabled;
                    LiveViewShutterComboBox.ToolTip = tvEnabled ? null : "Shutter not available";
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"UpdateExposureControlsAvailability error: {ex.Message}");
            }
        }

        #endregion

        // Live View Enhancement toggle removed

        #region Live View Tab Event Handlers
        
        private void LiveViewISOComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || LiveViewISOComboBox == null || _isPopulatingExposureUI) return;
            
            if (LiveViewISOComboBox.SelectedItem != null)
            {
                // Support both static ComboBoxItems and ItemsSource-bound string values
                string selectedIso = null;
                if (LiveViewISOComboBox.SelectedItem is string s)
                    selectedIso = s;
                else
                    selectedIso = (LiveViewISOComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

                if (!string.IsNullOrEmpty(selectedIso))
                {
                    LiveViewISOValueText.Text = selectedIso;
                    
                    // Do not auto-enable Live View enhancement
                    
                    // Update live view settings
                    if (_dualSettingsService != null)
                    {
                        _dualSettingsService.SetLiveViewSetting("ISO", selectedIso);
                        _dualSettingsService.SaveSettingsToStorage();

                        // Apply live view settings immediately when not in video mode
                        // This ensures the preview uses the selected settings
                        if (!_videoModeActive)
                        {
                            _dualSettingsService.ApplyLiveViewSettings();
                        }
                    }

                    // If video mode is active, apply in real-time
                    if (_videoModeActive && _videoModeLiveViewService != null)
                    {
                        _videoModeLiveViewService.SetISO(selectedIso);
                    }
                    
                    // Apply to visual enhancement
                    if (_liveViewEnhancementService != null && int.TryParse(selectedIso, out int isoValue))
                    {
                        _liveViewEnhancementService.SetSimulatedISO(isoValue);
                    }
                    
                    DebugService.LogDebug($"Live View ISO set to {selectedIso}");

                    // If Max ISO Boost toggle is on, ensure it stays at max
                    if (MaxIsoBoostToggle != null && MaxIsoBoostToggle.IsChecked == true)
                    {
                        // Re-apply the max value if user picks a lower value while boost is on
                        ApplyMaxIsoBoost(true);
                    }

                    // Persist setting immediately
                    AutoSaveIfEnabled();
                }
            }
        }

        private void TryPopulateLiveViewIso()
        {
            try
            {
                if (LiveViewISOComboBox == null || LiveViewISOValueText == null)
                    return;

                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;
                if (cam?.IsoNumber != null && cam.IsoNumber.Available)
                {
                    var values = cam.IsoNumber.Values; // typically list of strings
                    if (values != null && values.Any())
                    {
                        _isPopulatingExposureUI = true;
                        try
                        {
                            LiveViewISOComboBox.ItemsSource = null;
                            LiveViewISOComboBox.Items.Clear();
                            LiveViewISOComboBox.ItemsSource = values;

                            // Don't select any default - let LoadLiveViewSettings handle it
                            DebugService.LogDebug($"Populated Live View ISO with {values.Count()} values");
                        }
                        finally
                        {
                            _isPopulatingExposureUI = false;
                        }

                        // If Max ISO Boost is enabled, apply the max immediately
                        if (MaxIsoBoostToggle != null && MaxIsoBoostToggle.IsChecked == true)
                        {
                            Dispatcher.BeginInvoke(new Action(() => ApplyMaxIsoBoost(true)));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Populate Live View ISO failed: {ex.Message}");
            }
        }

        private string _previousLiveViewIso;

        private void MaxIsoBoostToggle_Checked(object sender, RoutedEventArgs e)
        {
            ApplyMaxIsoBoost(true);
            SaveOverlaySettings();
        }

        private void MaxIsoBoostToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplyMaxIsoBoost(false);
            SaveOverlaySettings();
        }

        private void ApplyMaxIsoBoost(bool enable)
        {
            try
            {
                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;
                if (cam?.IsoNumber == null || !cam.IsoNumber.Available || LiveViewISOComboBox == null)
                    return;

                var values = cam.IsoNumber.Values;
                if (values == null || values.Count == 0)
                    return;

                if (enable)
                {
                    // Save current selection if not already saved
                    if (string.IsNullOrEmpty(_previousLiveViewIso))
                    {
                        _previousLiveViewIso = cam.IsoNumber.Value ?? LiveViewISOValueText?.Text;
                    }
                    // Select the last (assumed highest) ISO value
                    var maxIso = values[values.Count - 1];
                    _applyingMaxIsoBoost = true;
                    LiveViewISOComboBox.SelectedItem = maxIso; // triggers selection changed -> applies to camera
                    _applyingMaxIsoBoost = false;
                    DebugService.LogDebug($"Max ISO Boost enabled: applied {maxIso}");
                }
                else
                {
                    // Restore previous selection if valid
                    var restore = _previousLiveViewIso;
                    _previousLiveViewIso = null;
                    if (!string.IsNullOrEmpty(restore) && values.Contains(restore))
                    {
                        _applyingMaxIsoBoost = true;
                        LiveViewISOComboBox.SelectedItem = restore;
                        _applyingMaxIsoBoost = false;
                        DebugService.LogDebug($"Max ISO Boost disabled: restored {restore}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"ApplyMaxIsoBoost error: {ex.Message}");
            }
        }
        
        private void LiveViewApertureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || LiveViewApertureComboBox == null || _isPopulatingExposureUI) return;
            
            if (LiveViewApertureComboBox.SelectedItem != null)
            {
                string selectedAv = null;
                if (LiveViewApertureComboBox.SelectedItem is string s)
                    selectedAv = s;
                else
                    selectedAv = (LiveViewApertureComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

                if (!string.IsNullOrEmpty(selectedAv))
                {
                    LiveViewApertureValueText.Text = selectedAv;
                    
                    // Update live view settings
                    if (_dualSettingsService != null)
                    {
                        _dualSettingsService.SetLiveViewSetting("Aperture", selectedAv);
                        _dualSettingsService.SaveSettingsToStorage();

                        // Apply live view settings immediately when not in video mode
                        // This ensures the preview uses the selected settings
                        if (!_videoModeActive)
                        {
                            _dualSettingsService.ApplyLiveViewSettings();
                        }
                    }

                    // If video mode is active, apply in real-time
                    if (_videoModeActive && _videoModeLiveViewService != null)
                    {
                        _videoModeLiveViewService.SetAperture(selectedAv);
                    }
                    
                    // Apply to visual enhancement
                    if (_liveViewEnhancementService != null)
                    {
                        var cleanValue = selectedAv.Replace("f/", "");
                        if (double.TryParse(cleanValue, out double apertureValue))
                        {
                            _liveViewEnhancementService.SetSimulatedAperture(apertureValue);
                        }
                    }
                    
                    DebugService.LogDebug($"Live View Aperture set to {selectedAv}");

                    // Persist setting immediately
                    AutoSaveIfEnabled();
                }
            }
        }
        
        private void LiveViewShutterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || LiveViewShutterComboBox == null || _isPopulatingExposureUI) return;
            
            if (LiveViewShutterComboBox.SelectedItem != null)
            {
                string selectedTv = null;
                if (LiveViewShutterComboBox.SelectedItem is string s)
                    selectedTv = s;
                else
                    selectedTv = (LiveViewShutterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

                if (!string.IsNullOrEmpty(selectedTv))
                {
                    LiveViewShutterValueText.Text = selectedTv;
                    
                    // Update live view settings
                    if (_dualSettingsService != null)
                    {
                        _dualSettingsService.SetLiveViewSetting("ShutterSpeed", selectedTv);
                        _dualSettingsService.SaveSettingsToStorage();

                        // Apply live view settings immediately when not in video mode
                        // This ensures the preview uses the selected settings
                        if (!_videoModeActive)
                        {
                            _dualSettingsService.ApplyLiveViewSettings();
                        }
                    }

                    // If video mode is active, apply in real-time
                    if (_videoModeActive && _videoModeLiveViewService != null)
                    {
                        _videoModeLiveViewService.SetShutterSpeed(selectedTv);
                    }
                    
                    // Apply to visual enhancement
                    if (_liveViewEnhancementService != null)
                    {
                        _liveViewEnhancementService.SetSimulatedShutterSpeed(selectedTv);
                    }
                    
                    DebugService.LogDebug($"Live View Shutter Speed set to {selectedTv}");

                    // Persist setting immediately
                    AutoSaveIfEnabled();
                }
            }
        }

        private void TryPopulateLiveViewAperture()
        {
            try
            {
                if (LiveViewApertureComboBox == null || LiveViewApertureValueText == null)
                    return;

                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;
                if (cam?.FNumber != null && cam.FNumber.Available)
                {
                    var values = cam.FNumber.Values;
                    if (values != null && values.Any())
                    {
                        LiveViewApertureComboBox.ItemsSource = null;
                        LiveViewApertureComboBox.Items.Clear();
                        LiveViewApertureComboBox.ItemsSource = values;

                        // Don't select any default - let LoadLiveViewSettings handle it
                        DebugService.LogDebug($"Populated Live View Aperture with {values.Count()} values");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Populate Live View Aperture failed: {ex.Message}");
            }
        }

        private void TryPopulatePhotoCaptureIso()
        {
            try
            {
                if (ISOComboBox == null || ISOValueText == null)
                    return;

                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;
                if (cam?.IsoNumber != null && cam.IsoNumber.Available)
                {
                    var values = cam.IsoNumber.Values;
                    if (values != null && values.Any())
                    {
                        // Temporarily disable events while populating
                        _isPopulatingExposureUI = true;

                        // Clear static items and use dynamic values
                        ISOComboBox.ItemsSource = null;
                        ISOComboBox.Items.Clear();
                        ISOComboBox.ItemsSource = values;

                        // Set default or current value
                        var current = cam.IsoNumber.Value;
                        if (!string.IsNullOrEmpty(current) && values.Contains(current))
                        {
                            ISOComboBox.SelectedItem = current;
                            ISOValueText.Text = current;
                        }
                        else if (values.Any())
                        {
                            ISOComboBox.SelectedIndex = 0;
                        }

                        _isPopulatingExposureUI = false;

                        DebugService.LogDebug($"Populated Photo Capture ISO with {values.Count()} values");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"TryPopulatePhotoCaptureIso error: {ex.Message}");
            }
        }

        private void TryPopulatePhotoCaptureAperture()
        {
            try
            {
                if (ApertureComboBox == null || ApertureValueText == null)
                    return;

                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;
                if (cam?.FNumber != null && cam.FNumber.Available)
                {
                    var values = cam.FNumber.Values;
                    if (values != null && values.Any())
                    {
                        // Temporarily disable events while populating
                        _isPopulatingExposureUI = true;

                        // Clear static items and use dynamic values
                        ApertureComboBox.ItemsSource = null;
                        ApertureComboBox.Items.Clear();
                        ApertureComboBox.ItemsSource = values;

                        var current = cam.FNumber.Value;
                        if (!string.IsNullOrEmpty(current) && values.Contains(current))
                        {
                            ApertureComboBox.SelectedItem = current;
                            ApertureValueText.Text = current;
                        }
                        else if (values.Any())
                        {
                            ApertureComboBox.SelectedIndex = 0;
                        }

                        _isPopulatingExposureUI = false;

                        DebugService.LogDebug($"Populated Photo Capture Aperture with {values.Count()} values");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"TryPopulatePhotoCaptureAperture error: {ex.Message}");
            }
        }

        private void TryPopulatePhotoCaptureShutter()
        {
            try
            {
                if (ShutterSpeedComboBox == null || ShutterSpeedValueText == null)
                    return;

                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;
                if (cam?.ShutterSpeed != null && cam.ShutterSpeed.Available)
                {
                    var values = cam.ShutterSpeed.Values;
                    if (values != null && values.Any())
                    {
                        DebugService.LogDebug($"TryPopulatePhotoCaptureShutter: Clearing combo box and populating with {values.Count()} values from camera");

                        // Temporarily disable events while populating
                        _isPopulatingExposureUI = true;

                        // Clear static items and use dynamic values
                        ShutterSpeedComboBox.ItemsSource = null;
                        ShutterSpeedComboBox.Items.Clear();
                        ShutterSpeedComboBox.ItemsSource = values;

                        var current = cam.ShutterSpeed.Value;
                        if (!string.IsNullOrEmpty(current) && values.Contains(current))
                        {
                            ShutterSpeedComboBox.SelectedItem = current;
                            ShutterSpeedValueText.Text = current;
                        }
                        else if (values.Any())
                        {
                            ShutterSpeedComboBox.SelectedIndex = 0;
                        }

                        _isPopulatingExposureUI = false;

                        DebugService.LogDebug($"Populated Photo Capture Shutter with {values.Count()} values: {string.Join(", ", values.Take(10))}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"TryPopulatePhotoCaptureShutter error: {ex.Message}");
            }
        }

        private void LoadLiveViewSettings()
        {
            try
            {
                if (_dualSettingsService == null) return;

                // Disable events while loading settings to prevent redundant camera updates
                _isPopulatingExposureUI = true;

                var liveViewSettings = _dualSettingsService.LiveViewSettings;

                // Load ISO setting
                if (!string.IsNullOrEmpty(liveViewSettings.ISO) && LiveViewISOComboBox != null)
                {
                    if (LiveViewISOComboBox.ItemsSource != null)
                    {
                        var values = LiveViewISOComboBox.ItemsSource as IEnumerable<string>;
                        if (values != null && values.Contains(liveViewSettings.ISO))
                        {
                            LiveViewISOComboBox.SelectedItem = liveViewSettings.ISO;
                            LiveViewISOValueText.Text = liveViewSettings.ISO;
                            DebugService.LogDebug($"Loaded Live View ISO: {liveViewSettings.ISO}");
                        }
                    }
                }

                // Load Aperture setting
                if (!string.IsNullOrEmpty(liveViewSettings.Aperture) && LiveViewApertureComboBox != null)
                {
                    if (LiveViewApertureComboBox.ItemsSource != null)
                    {
                        var values = LiveViewApertureComboBox.ItemsSource as IEnumerable<string>;
                        if (values != null && values.Contains(liveViewSettings.Aperture))
                        {
                            LiveViewApertureComboBox.SelectedItem = liveViewSettings.Aperture;
                            LiveViewApertureValueText.Text = liveViewSettings.Aperture;
                            DebugService.LogDebug($"Loaded Live View Aperture: {liveViewSettings.Aperture}");
                        }
                    }
                }

                // Load Shutter Speed setting
                if (!string.IsNullOrEmpty(liveViewSettings.ShutterSpeed) && LiveViewShutterComboBox != null)
                {
                    if (LiveViewShutterComboBox.ItemsSource != null)
                    {
                        var values = LiveViewShutterComboBox.ItemsSource as IEnumerable<string>;
                        if (values != null && values.Contains(liveViewSettings.ShutterSpeed))
                        {
                            LiveViewShutterComboBox.SelectedItem = liveViewSettings.ShutterSpeed;
                            LiveViewShutterValueText.Text = liveViewSettings.ShutterSpeed;
                            DebugService.LogDebug($"Loaded Live View Shutter: {liveViewSettings.ShutterSpeed}");
                        }
                    }
                }

                // Re-enable events after loading
                _isPopulatingExposureUI = false;
            }
            catch (Exception ex)
            {
                _isPopulatingExposureUI = false;
                DebugService.LogError($"LoadLiveViewSettings error: {ex.Message}");
            }
        }

        private void LoadPhotoCaptureSettings()
        {
            try
            {
                if (_dualSettingsService == null) return;

                // Disable events while loading settings to prevent redundant camera updates
                _isPopulatingExposureUI = true;

                var photoCaptureSettings = _dualSettingsService.PhotoCaptureSettings;

                // Load ISO setting
                if (!string.IsNullOrEmpty(photoCaptureSettings.ISO) && ISOComboBox != null)
                {
                    if (ISOComboBox.ItemsSource != null)
                    {
                        var values = ISOComboBox.ItemsSource as IEnumerable<string>;
                        if (values != null && values.Contains(photoCaptureSettings.ISO))
                        {
                            ISOComboBox.SelectedItem = photoCaptureSettings.ISO;
                            ISOValueText.Text = photoCaptureSettings.ISO;
                            DebugService.LogDebug($"Loaded Photo Capture ISO: {photoCaptureSettings.ISO}");
                        }
                    }
                }

                // Load Aperture setting
                if (!string.IsNullOrEmpty(photoCaptureSettings.Aperture) && ApertureComboBox != null)
                {
                    if (ApertureComboBox.ItemsSource != null)
                    {
                        var values = ApertureComboBox.ItemsSource as IEnumerable<string>;
                        if (values != null && values.Contains(photoCaptureSettings.Aperture))
                        {
                            ApertureComboBox.SelectedItem = photoCaptureSettings.Aperture;
                            ApertureValueText.Text = photoCaptureSettings.Aperture;
                            DebugService.LogDebug($"Loaded Photo Capture Aperture: {photoCaptureSettings.Aperture}");
                        }
                    }
                }

                // Load Shutter Speed setting
                if (!string.IsNullOrEmpty(photoCaptureSettings.ShutterSpeed) && ShutterSpeedComboBox != null)
                {
                    DebugService.LogDebug($"Attempting to load Photo Capture Shutter: {photoCaptureSettings.ShutterSpeed}");
                    if (ShutterSpeedComboBox.ItemsSource != null)
                    {
                        var values = ShutterSpeedComboBox.ItemsSource as IEnumerable<string>;
                        if (values != null)
                        {
                            DebugService.LogDebug($"Available shutter values: {string.Join(", ", values.Take(10))}");
                            if (values.Contains(photoCaptureSettings.ShutterSpeed))
                            {
                                ShutterSpeedComboBox.SelectedItem = photoCaptureSettings.ShutterSpeed;
                                ShutterSpeedValueText.Text = photoCaptureSettings.ShutterSpeed;
                                DebugService.LogDebug($"Successfully loaded Photo Capture Shutter: {photoCaptureSettings.ShutterSpeed}");
                            }
                            else
                            {
                                DebugService.LogDebug($"Warning: Saved shutter speed '{photoCaptureSettings.ShutterSpeed}' not found in available values");
                            }
                        }
                    }
                    else
                    {
                        DebugService.LogDebug("Warning: ShutterSpeedComboBox.ItemsSource is null");
                    }
                }
                else
                {
                    DebugService.LogDebug($"Not loading shutter - Empty: {string.IsNullOrEmpty(photoCaptureSettings.ShutterSpeed)}, ComboBox null: {ShutterSpeedComboBox == null}");
                }

                // Re-enable events after loading
                _isPopulatingExposureUI = false;
            }
            catch (Exception ex)
            {
                DebugService.LogError($"LoadPhotoCaptureSettings error: {ex.Message}");
            }
        }

        private void TryPopulateLiveViewShutter()
        {
            try
            {
                if (LiveViewShutterComboBox == null || LiveViewShutterValueText == null)
                    return;

                var cam = _currentCamera ?? CameraSessionManager.Instance?.DeviceManager?.SelectedCameraDevice;
                if (cam?.ShutterSpeed != null && cam.ShutterSpeed.Available)
                {
                    var values = cam.ShutterSpeed.Values;
                    if (values != null && values.Any())
                    {
                        LiveViewShutterComboBox.ItemsSource = null;
                        LiveViewShutterComboBox.Items.Clear();
                        LiveViewShutterComboBox.ItemsSource = values;

                        // Don't select any default - let LoadLiveViewSettings handle it
                        DebugService.LogDebug($"Populated Live View Shutter with {values.Count()} values");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugService.LogError($"Populate Live View Shutter failed: {ex.Message}");
            }
        }
        
        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || BrightnessSlider == null) return;
            
            var value = (int)Math.Round(BrightnessSlider.Value);
            BrightnessValueText.Text = value >= 0 ? $"+{value}" : value.ToString();
            
            // Apply brightness boost to live view enhancement
            if (_liveViewEnhancementService != null)
            {
                _liveViewEnhancementService.SetBrightness(value);
                DebugService.LogDebug($"Live View Brightness Boost set to {value}");
            }
        }
        
        #endregion
    }
}
