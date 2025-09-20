using CameraControl.Devices;
using CameraControl.Devices.Classes;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;
using Photobooth.Services;

namespace Photobooth.Pages
{
    public partial class CameraSettings : Page
    {
        // Use singleton camera manager to maintain session across screens
        public CameraDeviceManager DeviceManager => CameraSessionManager.Instance.DeviceManager;
        private string testPhotosFolder;
        private bool isCapturing = false;
        
        // Video mode settings storage
        private class VideoModeSettings
        {
            public string ISO { get; set; } = "Auto";
            public string Aperture { get; set; } = "Auto";
            public string ShutterSpeed { get; set; } = "Auto";
            public string WhiteBalance { get; set; } = "Auto";
            public string FocusMode { get; set; } = "Continuous AF";
            public string ExposureCompensation { get; set; } = "0";
            public string FrameRate { get; set; } = "30 fps";
            public string VideoQuality { get; set; } = "High (ALL-I)";
        }
        
        private VideoModeSettings videoSettings = new VideoModeSettings();
        private VideoModeSettings savedPhotoSettings = new VideoModeSettings(); // To store photo settings
        private bool isInVideoMode = false;
        
        // Live view for video settings
        private DispatcherTimer videoLiveViewTimer;
        private bool isVideoLiveViewActive = false;

        public CameraSettings()
        {
            InitializeComponent();
            
            // Use singleton camera manager - don't create new instance
            // Event subscriptions moved to Loaded event to prevent duplicates

            // Set up test photos folder
            testPhotosFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CameraSettingsTest");
            if (!Directory.Exists(testPhotosFolder))
            {
                Directory.CreateDirectory(testPhotosFolder);
            }

            Loaded += CameraSettings_Loaded;
            Unloaded += CameraSettings_Unloaded;
            
            // Add keyboard shortcuts for quick adjustments
            this.PreviewKeyDown += CameraSettings_PreviewKeyDown;
        }

        private void CameraSettings_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop live view if active
            if (isVideoLiveViewActive)
            {
                StopVideoLiveView();
            }
            
            // Unsubscribe from camera events to prevent duplicate handlers
            DeviceManager.CameraSelected -= DeviceManager_CameraSelected;
            DeviceManager.CameraConnected -= DeviceManager_CameraConnected;
            DeviceManager.CameraDisconnected -= DeviceManager_CameraDisconnected;
            DeviceManager.PhotoCaptured -= DeviceManager_PhotoCaptured;
            
            // Cleanup camera for screen change using singleton manager
            CameraSessionManager.Instance.CleanupCameraForScreenChange();
        }

        private void CameraSettings_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Subscribe to camera events (will be unsubscribed in Unloaded)
                DeviceManager.CameraSelected += DeviceManager_CameraSelected;
                DeviceManager.CameraConnected += DeviceManager_CameraConnected;
                DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;
                DeviceManager.PhotoCaptured += DeviceManager_PhotoCaptured;
                
                // Prepare camera using singleton manager
                CameraSessionManager.Instance.PrepareCameraForUse();
                
                DeviceManager.ConnectToCamera();
                RefreshCameraStatus();
                LoadCurrentSettings();
                PopulateIsoComboBox();
                
                // Initialize video settings UI
                InitializeVideoSettings();
                
                // Wire up video settings event handlers
                WireUpVideoSettingsHandlers();
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error connecting to camera: {ex.Message}";
            }
        }

        private void RefreshCameraStatus()
        {
            if (DeviceManager.SelectedCameraDevice != null)
            {
                cameraStatusText.Text = $"Connected: {DeviceManager.SelectedCameraDevice.DeviceName}";
                EnableControls(true);
            }
            else
            {
                cameraStatusText.Text = "No camera connected";
                EnableControls(false);
            }
        }

        private void EnableControls(bool enabled)
        {
            // Enable/disable all setting controls
            isoPrevButton.IsEnabled = enabled;
            isoNextButton.IsEnabled = enabled;
            aperturePrevButton.IsEnabled = enabled;
            apertureNextButton.IsEnabled = enabled;
            shutterPrevButton.IsEnabled = enabled;
            shutterNextButton.IsEnabled = enabled;
            wbPrevButton.IsEnabled = enabled;
            wbNextButton.IsEnabled = enabled;
            focusPrevButton.IsEnabled = enabled;
            focusNextButton.IsEnabled = enabled;
            exposurePrevButton.IsEnabled = enabled;
            exposureNextButton.IsEnabled = enabled;
            modePrevButton.IsEnabled = enabled;
            modeNextButton.IsEnabled = enabled;
            qualityPrevButton.IsEnabled = enabled;
            qualityNextButton.IsEnabled = enabled;
        }

        private void LoadCurrentSettings()
        {
            if (DeviceManager.SelectedCameraDevice == null)
                return;

            try
            {
                var camera = DeviceManager.SelectedCameraDevice;

                // Load current values
                UpdateSettingDisplay(camera.IsoNumber, isoValueText);
                UpdateSettingDisplay(camera.FNumber, apertureValueText);
                UpdateSettingDisplay(camera.ShutterSpeed, shutterValueText);
                UpdateSettingDisplay(camera.WhiteBalance, wbValueText);
                UpdateSettingDisplay(camera.FocusMode, focusValueText);
                UpdateSettingDisplay(camera.ExposureCompensation, exposureValueText);
                UpdateSettingDisplay(camera.Mode, modeValueText);
                UpdateSettingDisplay(camera.CompressionSetting, qualityValueText);
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error loading settings: {ex.Message}";
            }
        }

        private void UpdateSettingDisplay(PropertyValue<long> property, TextBlock textBlock)
        {
            try
            {
                if (property != null && property.Available)
                {
                    textBlock.Text = property.Value ?? "N/A";
                }
                else
                {
                    textBlock.Text = "N/A";
                }
            }
            catch
            {
                textBlock.Text = "N/A";
            }
        }

        // Event handlers for camera connection
        private void DeviceManager_CameraConnected(ICameraDevice cameraDevice)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshCameraStatus();
                LoadCurrentSettings();
                PopulateIsoComboBox();
            });
        }

        private void DeviceManager_CameraDisconnected(ICameraDevice cameraDevice)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshCameraStatus();
            });
        }

        private void DeviceManager_CameraSelected(ICameraDevice oldcameraDevice, ICameraDevice newcameraDevice)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshCameraStatus();
                LoadCurrentSettings();
                PopulateIsoComboBox();
            });
        }

        private void PopulateIsoComboBox()
        {
            try
            {
                var camera = DeviceManager.SelectedCameraDevice;
                if (camera?.IsoNumber != null && camera.IsoNumber.Available)
                {
                    var values = camera.IsoNumber.Values; // typically a list of strings
                    isoComboBox.ItemsSource = values;
                    // Select current value if present
                    var current = camera.IsoNumber.Value;
                    if (!string.IsNullOrEmpty(current))
                    {
                        isoComboBox.SelectedItem = current;
                    }
                }
                else
                {
                    isoComboBox.ItemsSource = null;
                }
            }
            catch { /* ignore UI population errors */ }
        }

        private void IsoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selected = isoComboBox.SelectedItem as string;
                if (string.IsNullOrEmpty(selected)) return;

                // Prefer the video LV service if active to avoid conflicts
                var lvService = VideoModeLiveViewService.Instance;
                if (lvService != null && lvService.IsVideoModeActive)
                {
                    lvService.SetISO(selected);
                }
                else
                {
                    var camera = DeviceManager.SelectedCameraDevice;
                    if (camera?.IsoNumber != null && camera.IsoNumber.IsEnabled)
                    {
                        // Apply directly when not in video mode
                        camera.IsoNumber.SetValue(selected);
                    }
                }

                // Reflect in the text display
                isoValueText.Text = selected;
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Failed to set ISO: {ex.Message}";
            }
        }

        // ISO Controls
        private void IsoPrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.IsoNumber, isoValueText, false);
        }

        private void IsoNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.IsoNumber, isoValueText, true);
        }

        // Aperture Controls
        private void AperturePrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.FNumber, apertureValueText, false);
        }

        private void ApertureNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.FNumber, apertureValueText, true);
        }

        // Shutter Speed Controls
        private void ShutterPrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.ShutterSpeed, shutterValueText, false);
        }

        private void ShutterNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.ShutterSpeed, shutterValueText, true);
        }

        // White Balance Controls
        private void WhiteBalancePrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.WhiteBalance, wbValueText, false);
        }

        private void WhiteBalanceNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.WhiteBalance, wbValueText, true);
        }

        // Focus Mode Controls
        private void FocusPrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.FocusMode, focusValueText, false);
        }

        private void FocusNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.FocusMode, focusValueText, true);
        }

        // Exposure Compensation Controls
        private void ExposurePrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.ExposureCompensation, exposureValueText, false);
        }

        private void ExposureNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.ExposureCompensation, exposureValueText, true);
        }

        // Camera Mode Controls
        private void ModePrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.Mode, modeValueText, false);
        }

        private void ModeNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.Mode, modeValueText, true);
        }

        // Image Quality Controls
        private void QualityPrev_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.CompressionSetting, qualityValueText, false);
        }

        private void QualityNext_Click(object sender, RoutedEventArgs e)
        {
            ChangeSetting(DeviceManager.SelectedCameraDevice?.CompressionSetting, qualityValueText, true);
        }

        // Generic method to change settings
        private void ChangeSetting(PropertyValue<long> property, TextBlock displayText, bool next)
        {
            if (property == null || !property.Available)
            {
                cameraStatusText.Text = "Setting not available on this camera";
                return;
            }

            try
            {
                if (next)
                {
                    property.NextValue();
                }
                else
                {
                    property.PrevValue();
                }

                displayText.Text = property.Value ?? "N/A";
                cameraStatusText.Text = $"Setting changed to: {property.Value}";

                // Auto preview if enabled
                if (autoPreviewCheckBox.IsChecked == true && !isCapturing)
                {
                    Task.Delay(500).ContinueWith(_ => 
                    {
                        Dispatcher.Invoke(() => TestCapture_Click(null, null));
                    });
                }
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error changing setting: {ex.Message}";
            }
        }

        // Test Capture functionality
        private void TestCapture_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceManager.SelectedCameraDevice == null)
            {
                testStatusText.Text = "No camera connected";
                return;
            }

            if (isCapturing)
            {
                testStatusText.Text = "Capture in progress...";
                return;
            }

            try
            {
                isCapturing = true;
                testCaptureButton.IsEnabled = false;
                testStatusText.Text = "Capturing test image...";
                
                // Use the same proven capture approach as PhotoboothTouch
                DeviceManager.SelectedCameraDevice.CapturePhoto();
                
                // Timeout protection
                Task.Delay(8000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (isCapturing)
                        {
                            isCapturing = false;
                            testCaptureButton.IsEnabled = true;
                            testStatusText.Text = "Capture timeout - please try again";
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                isCapturing = false;
                testCaptureButton.IsEnabled = true;
                testStatusText.Text = $"Capture error: {ex.Message}";
            }
        }

        // Handle photo capture completion
        private void DeviceManager_PhotoCaptured(object sender, PhotoCapturedEventArgs eventArgs)
        {
            if (!isCapturing) return; // Ignore if not our capture

            try
            {
                string fileName = Path.Combine(testPhotosFolder, 
                    $"Test_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(eventArgs.FileName)}");

                if (!Directory.Exists(Path.GetDirectoryName(fileName)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                }

                // Transfer the file using Canon SDK
                eventArgs.CameraDevice.TransferFile(eventArgs.Handle, fileName);
                eventArgs.CameraDevice.IsBusy = false;

                if (File.Exists(fileName))
                {
                    Dispatcher.Invoke(() =>
                    {
                        DisplayTestImage(fileName);
                        isCapturing = false;
                        testCaptureButton.IsEnabled = true;
                        testStatusText.Text = $"Test capture successful - {Path.GetFileName(fileName)}";
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        isCapturing = false;
                        testCaptureButton.IsEnabled = true;
                        testStatusText.Text = "Capture failed - file not created";
                    });
                }
            }
            catch (Exception ex)
            {
                eventArgs.CameraDevice.IsBusy = false;
                Dispatcher.Invoke(() =>
                {
                    isCapturing = false;
                    testCaptureButton.IsEnabled = true;
                    testStatusText.Text = $"Transfer error: {ex.Message}";
                });
            }
        }

        private void DisplayTestImage(string imagePath)
        {
            try
            {
                // Load and display the image
                var bitmap = new BitmapImage(new Uri(imagePath));
                testImage.Source = bitmap;
                testImage.Visibility = Visibility.Visible;
                imagePlaceholder.Visibility = Visibility.Collapsed;
                imageInfoPanel.Visibility = Visibility.Visible;

                // Get file info
                var fileInfo = new FileInfo(imagePath);

                // Display capture information
                captureTimeText.Text = $"Captured: {DateTime.Now:HH:mm:ss}";
                
                // Get current camera settings for display
                var camera = DeviceManager.SelectedCameraDevice;
                var settingsInfo = "";
                if (camera != null)
                {
                    if (camera.IsoNumber?.Available == true) settingsInfo += $"ISO {camera.IsoNumber.Value} ";
                    if (camera.FNumber?.Available == true) settingsInfo += $"f/{camera.FNumber.Value} ";
                    if (camera.ShutterSpeed?.Available == true) settingsInfo += $"{camera.ShutterSpeed.Value} ";
                    if (camera.WhiteBalance?.Available == true) settingsInfo += $"WB:{camera.WhiteBalance.Value}";
                }
                imageSettingsText.Text = settingsInfo.Trim();
                
                imageSizeText.Text = $"Size: {fileInfo.Length / 1024:N0} KB ({bitmap.PixelWidth}×{bitmap.PixelHeight})";
            }
            catch (Exception ex)
            {
                testStatusText.Text = $"Display error: {ex.Message}";
            }
        }

        // Action buttons
        private void RefreshSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DeviceManager.ConnectToCamera();
                RefreshCameraStatus();
                LoadCurrentSettings();
                cameraStatusText.Text = "Settings refreshed successfully";
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error refreshing: {ex.Message}";
            }
        }

        private void ResetToAuto_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceManager.SelectedCameraDevice == null)
            {
                cameraStatusText.Text = "No camera connected";
                return;
            }

            try
            {
                var camera = DeviceManager.SelectedCameraDevice;

                // Try to set common settings to automatic values
                // Note: Exact "Auto" values depend on the camera model
                if (camera.Mode.Available)
                {
                    // Try to find and set Auto mode
                    SetToAutoIfAvailable(camera.Mode, new[] { "Auto", "AUTO", "A", "Program Auto" });
                }

                if (camera.IsoNumber.Available)
                {
                    SetToAutoIfAvailable(camera.IsoNumber, new[] { "Auto", "AUTO", "A" });
                }

                if (camera.WhiteBalance.Available)
                {
                    SetToAutoIfAvailable(camera.WhiteBalance, new[] { "Auto", "AUTO", "AWB", "Auto WB" });
                }

                if (camera.FocusMode.Available)
                {
                    SetToAutoIfAvailable(camera.FocusMode, new[] { "Auto", "AUTO", "AF", "Single" });
                }

                LoadCurrentSettings();
                cameraStatusText.Text = "Reset to automatic settings completed";
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error resetting: {ex.Message}";
            }
        }

        private void SetToAutoIfAvailable(PropertyValue<long> property, string[] autoValues)
        {
            if (property == null || !property.Available || property.Values == null)
                return;

            foreach (string autoValue in autoValues)
            {
                if (property.Values.Contains(autoValue))
                {
                    property.Value = autoValue;
                    break;
                }
            }
        }
        
        #region Video Mode Settings
        
        private void InitializeVideoSettings()
        {
            // Load video settings from application settings
            LoadVideoSettingsFromStorage();
            
            // Initialize video settings display
            if (videoIsoValueText != null) videoIsoValueText.Text = videoSettings.ISO;
            if (videoApertureValueText != null) videoApertureValueText.Text = videoSettings.Aperture;
            if (videoShutterValueText != null) videoShutterValueText.Text = videoSettings.ShutterSpeed;
            if (videoWbValueText != null) videoWbValueText.Text = videoSettings.WhiteBalance;
            if (videoFocusValueText != null) videoFocusValueText.Text = videoSettings.FocusMode;
            if (videoExposureValueText != null) videoExposureValueText.Text = videoSettings.ExposureCompensation;
            if (videoFrameRateValueText != null) videoFrameRateValueText.Text = videoSettings.FrameRate;
            if (videoQualityValueText != null) videoQualityValueText.Text = videoSettings.VideoQuality;
        }
        
        private void LoadVideoSettingsFromStorage()
        {
            try
            {
                // Load settings from Properties.Settings
                videoSettings.ISO = Properties.Settings.Default.VideoISO ?? "Auto";
                videoSettings.Aperture = Properties.Settings.Default.VideoAperture ?? "Auto";
                videoSettings.ShutterSpeed = Properties.Settings.Default.VideoShutterSpeed ?? "Auto";
                videoSettings.WhiteBalance = Properties.Settings.Default.VideoWhiteBalance ?? "Auto";
                videoSettings.FocusMode = Properties.Settings.Default.VideoFocusMode ?? "Continuous AF";
                videoSettings.ExposureCompensation = Properties.Settings.Default.VideoExposureCompensation ?? "0";
                videoSettings.FrameRate = Properties.Settings.Default.VideoFrameRate ?? "30 fps";
                videoSettings.VideoQuality = Properties.Settings.Default.VideoQuality ?? "High (ALL-I)";
            }
            catch (Exception ex)
            {
                // If loading fails, keep default values
                System.Diagnostics.Debug.WriteLine($"Error loading video settings: {ex.Message}");
            }
        }
        
        private void SaveVideoSettingsToStorage()
        {
            try
            {
                // Save settings to Properties.Settings
                Properties.Settings.Default.VideoISO = videoSettings.ISO;
                Properties.Settings.Default.VideoAperture = videoSettings.Aperture;
                Properties.Settings.Default.VideoShutterSpeed = videoSettings.ShutterSpeed;
                Properties.Settings.Default.VideoWhiteBalance = videoSettings.WhiteBalance;
                Properties.Settings.Default.VideoFocusMode = videoSettings.FocusMode;
                Properties.Settings.Default.VideoExposureCompensation = videoSettings.ExposureCompensation;
                Properties.Settings.Default.VideoFrameRate = videoSettings.FrameRate;
                Properties.Settings.Default.VideoQuality = videoSettings.VideoQuality;
                
                // Persist to disk
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving video settings: {ex.Message}");
            }
        }
        
        private void WireUpVideoSettingsHandlers()
        {
            // ISO handlers
            if (videoIsoPrevButton != null)
            {
                videoIsoPrevButton.Click += (s, e) => ChangeVideoSetting("ISO", false);
            }
            if (videoIsoNextButton != null)
            {
                videoIsoNextButton.Click += (s, e) => ChangeVideoSetting("ISO", true);
            }
            
            // Aperture handlers
            if (videoAperturePrevButton != null)
            {
                videoAperturePrevButton.Click += (s, e) => ChangeVideoSetting("Aperture", false);
            }
            if (videoApertureNextButton != null)
            {
                videoApertureNextButton.Click += (s, e) => ChangeVideoSetting("Aperture", true);
            }
            
            // Shutter Speed handlers
            if (videoShutterPrevButton != null)
            {
                videoShutterPrevButton.Click += (s, e) => ChangeVideoSetting("ShutterSpeed", false);
            }
            if (videoShutterNextButton != null)
            {
                videoShutterNextButton.Click += (s, e) => ChangeVideoSetting("ShutterSpeed", true);
            }
            
            // White Balance handlers
            if (videoWbPrevButton != null)
            {
                videoWbPrevButton.Click += (s, e) => ChangeVideoSetting("WhiteBalance", false);
            }
            if (videoWbNextButton != null)
            {
                videoWbNextButton.Click += (s, e) => ChangeVideoSetting("WhiteBalance", true);
            }
            
            // Focus Mode handlers
            if (videoFocusPrevButton != null)
            {
                videoFocusPrevButton.Click += (s, e) => ChangeVideoSetting("FocusMode", false);
            }
            if (videoFocusNextButton != null)
            {
                videoFocusNextButton.Click += (s, e) => ChangeVideoSetting("FocusMode", true);
            }
            
            // Exposure Compensation handlers
            if (videoExposurePrevButton != null)
            {
                videoExposurePrevButton.Click += (s, e) => ChangeVideoSetting("ExposureCompensation", false);
            }
            if (videoExposureNextButton != null)
            {
                videoExposureNextButton.Click += (s, e) => ChangeVideoSetting("ExposureCompensation", true);
            }
            
            // Frame Rate handlers
            if (videoFrameRatePrevButton != null)
            {
                videoFrameRatePrevButton.Click += (s, e) => ChangeVideoSetting("FrameRate", false);
            }
            if (videoFrameRateNextButton != null)
            {
                videoFrameRateNextButton.Click += (s, e) => ChangeVideoSetting("FrameRate", true);
            }
            
            // Video Quality handlers
            if (videoQualityPrevButton != null)
            {
                videoQualityPrevButton.Click += (s, e) => ChangeVideoSetting("VideoQuality", false);
            }
            if (videoQualityNextButton != null)
            {
                videoQualityNextButton.Click += (s, e) => ChangeVideoSetting("VideoQuality", true);
            }
            
            // Test Video button
            if (testVideoButton != null)
            {
                testVideoButton.Click += TestVideoCapture_Click;
            }
            
            // Save Video Settings button
            if (saveVideoSettingsButton != null)
            {
                saveVideoSettingsButton.Click += SaveVideoSettings_Click;
            }
            
            // Reset Video Settings button
            if (resetVideoSettingsButton != null)
            {
                resetVideoSettingsButton.Click += ResetVideoSettings_Click;
            }
        }
        
        private void ChangeVideoSetting(string settingName, bool increment)
        {
            if (DeviceManager.SelectedCameraDevice == null)
            {
                cameraStatusText.Text = "No camera connected";
                return;
            }
            
            try
            {
                PropertyValue<long> property = null;
                TextBlock displayText = null;
                
                // Map setting name to camera property and UI element
                switch (settingName)
                {
                    case "ISO":
                        property = DeviceManager.SelectedCameraDevice.IsoNumber;
                        displayText = videoIsoValueText;
                        break;
                    case "Aperture":
                        property = DeviceManager.SelectedCameraDevice.FNumber;
                        displayText = videoApertureValueText;
                        break;
                    case "ShutterSpeed":
                        property = DeviceManager.SelectedCameraDevice.ShutterSpeed;
                        displayText = videoShutterValueText;
                        break;
                    case "WhiteBalance":
                        property = DeviceManager.SelectedCameraDevice.WhiteBalance;
                        displayText = videoWbValueText;
                        break;
                    case "FocusMode":
                        property = DeviceManager.SelectedCameraDevice.FocusMode;
                        displayText = videoFocusValueText;
                        break;
                    case "ExposureCompensation":
                        property = DeviceManager.SelectedCameraDevice.ExposureCompensation;
                        displayText = videoExposureValueText;
                        break;
                    case "FrameRate":
                        // Frame rate might need special handling for video mode
                        displayText = videoFrameRateValueText;
                        // For now, just cycle through common frame rates
                        string[] frameRates = { "24 fps", "25 fps", "30 fps", "60 fps" };
                        int currentIndex = Array.IndexOf(frameRates, videoSettings.FrameRate);
                        if (increment)
                        {
                            currentIndex = (currentIndex + 1) % frameRates.Length;
                        }
                        else
                        {
                            currentIndex = (currentIndex - 1 + frameRates.Length) % frameRates.Length;
                        }
                        videoSettings.FrameRate = frameRates[currentIndex];
                        displayText.Text = videoSettings.FrameRate;
                        SaveVideoSettingsToStorage(); // Save frame rate change
                        cameraStatusText.Text = $"Video frame rate changed to: {videoSettings.FrameRate}";
                        return;
                    case "VideoQuality":
                        displayText = videoQualityValueText;
                        // Video quality settings
                        string[] qualities = { "Low (IPB)", "Medium (IPB)", "High (ALL-I)" };
                        int qualityIndex = Array.IndexOf(qualities, videoSettings.VideoQuality);
                        if (increment)
                        {
                            qualityIndex = (qualityIndex + 1) % qualities.Length;
                        }
                        else
                        {
                            qualityIndex = (qualityIndex - 1 + qualities.Length) % qualities.Length;
                        }
                        videoSettings.VideoQuality = qualities[qualityIndex];
                        displayText.Text = videoSettings.VideoQuality;
                        SaveVideoSettingsToStorage(); // Save video quality change
                        cameraStatusText.Text = $"Video quality changed to: {videoSettings.VideoQuality}";
                        return;
                }
                
                if (property != null && property.IsEnabled && displayText != null)
                {
                    // Store current value for video mode
                    string previousValue = property.Value;
                    
                    // Change the setting on the camera immediately for real-time feedback
                    if (increment)
                    {
                        property.NextValue();
                    }
                    else
                    {
                        property.PrevValue();
                    }
                    
                    // Force camera to apply the change immediately if in live view
                    if (isVideoLiveViewActive && DeviceManager.SelectedCameraDevice != null)
                    {
                        // For exposure triangle settings (ISO, Aperture, Shutter Speed), 
                        // we need to ensure they're applied properly during live view
                        Task.Run(() =>
                        {
                            try
                            {
                                Thread.Sleep(50); // Brief delay for camera to register the change
                                
                                // Force the setting to be re-applied
                                Dispatcher.Invoke(() =>
                                {
                                    // Call SetValue() to trigger the ValueChanged event again
                                    property.SetValue();
                                    
                                    // Log for debugging
                                    System.Diagnostics.Debug.WriteLine($"Re-applied {settingName} = {property.Value} during live view");
                                });
                                
                                // For exposure settings, we might need to briefly pause live view
                                if (settingName == "ISO" || settingName == "Aperture" || settingName == "ShutterSpeed")
                                {
                                    Thread.Sleep(100); // Give camera time to apply the setting
                                    
                                    // Force a live view frame grab to ensure the setting is applied
                                    try
                                    {
                                        var liveViewData = DeviceManager.SelectedCameraDevice.GetLiveViewImage();
                                        System.Diagnostics.Debug.WriteLine($"Forced frame grab after {settingName} change");
                                    }
                                    catch { }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error applying {settingName} in live view: {ex.Message}");
                            }
                        });
                    }
                    
                    // Store the new value for video mode
                    string newValue = property.Value;
                    displayText.Text = newValue;
                    
                    // Store in video settings
                    switch (settingName)
                    {
                        case "ISO":
                            videoSettings.ISO = newValue;
                            break;
                        case "Aperture":
                            videoSettings.Aperture = newValue;
                            break;
                        case "ShutterSpeed":
                            videoSettings.ShutterSpeed = newValue;
                            break;
                        case "WhiteBalance":
                            videoSettings.WhiteBalance = newValue;
                            break;
                        case "FocusMode":
                            videoSettings.FocusMode = newValue;
                            break;
                        case "ExposureCompensation":
                            videoSettings.ExposureCompensation = newValue;
                            break;
                    }
                    
                    cameraStatusText.Text = $"Video setting {settingName} changed to: {newValue}";
                    
                    // Auto-save video settings after each change
                    SaveVideoSettingsToStorage();
                    
                    // Update live view info if active
                    if (isVideoLiveViewActive)
                    {
                        UpdateVideoLiveViewInfo();
                    }
                }
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error changing video setting: {ex.Message}";
            }
        }
        
        private void TestVideoCapture_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceManager.SelectedCameraDevice == null)
            {
                MessageBox.Show("No camera connected", "Video Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // Apply video settings to camera
                ApplyVideoSettingsToCamera();
                
                // Start video recording
                DeviceManager.SelectedCameraDevice.StartRecordMovie();
                
                // Record for a few seconds
                Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Stop recording
                            DeviceManager.SelectedCameraDevice.StopRecordMovie();
                            
                            // Restore photo settings after video recording
                            RestorePhotoSettings();
                            isInVideoMode = false;
                            
                            // The video will be handled by PhotoCaptured event
                            MessageBox.Show("Video test capture completed!", "Video Test", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error stopping video: {ex.Message}", "Video Test Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting video: {ex.Message}", "Video Test Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SaveVideoSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save video settings to persistent storage
                SaveVideoSettingsToStorage();
                
                MessageBox.Show("Video settings saved successfully!\n\n" +
                    $"ISO: {videoSettings.ISO}\n" +
                    $"Aperture: {videoSettings.Aperture}\n" +
                    $"Shutter Speed: {videoSettings.ShutterSpeed}\n" +
                    $"White Balance: {videoSettings.WhiteBalance}\n" +
                    $"Focus Mode: {videoSettings.FocusMode}\n" +
                    $"Exposure Comp: {videoSettings.ExposureCompensation}\n" +
                    $"Frame Rate: {videoSettings.FrameRate}\n" +
                    $"Quality: {videoSettings.VideoQuality}",
                    "Video Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving video settings: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ResetVideoSettings_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset all video settings to defaults?", "Reset Video Settings", 
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                videoSettings = new VideoModeSettings();
                SaveVideoSettingsToStorage(); // Save the reset values
                InitializeVideoSettings();
                MessageBox.Show("Video settings reset to defaults", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        
        private void SaveCurrentPhotoSettings()
        {
            if (DeviceManager.SelectedCameraDevice == null) return;
            
            try
            {
                var camera = DeviceManager.SelectedCameraDevice;
                
                // Save current camera settings as photo settings
                savedPhotoSettings.ISO = camera.IsoNumber?.Value ?? "Auto";
                savedPhotoSettings.Aperture = camera.FNumber?.Value ?? "Auto";
                savedPhotoSettings.ShutterSpeed = camera.ShutterSpeed?.Value ?? "Auto";
                savedPhotoSettings.WhiteBalance = camera.WhiteBalance?.Value ?? "Auto";
                savedPhotoSettings.FocusMode = camera.FocusMode?.Value ?? "Auto";
                savedPhotoSettings.ExposureCompensation = camera.ExposureCompensation?.Value ?? "0";
                
                System.Diagnostics.Debug.WriteLine($"Saved photo settings - ISO: {savedPhotoSettings.ISO}, Aperture: {savedPhotoSettings.Aperture}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving photo settings: {ex.Message}");
            }
        }
        
        private void RestorePhotoSettings()
        {
            if (DeviceManager.SelectedCameraDevice == null) return;
            
            try
            {
                var camera = DeviceManager.SelectedCameraDevice;
                
                // Restore saved photo settings
                if (camera.IsoNumber != null && camera.IsoNumber.IsEnabled)
                {
                    SetPropertyToValue(camera.IsoNumber, savedPhotoSettings.ISO);
                }
                
                if (camera.FNumber != null && camera.FNumber.IsEnabled)
                {
                    SetPropertyToValue(camera.FNumber, savedPhotoSettings.Aperture);
                }
                
                if (camera.ShutterSpeed != null && camera.ShutterSpeed.IsEnabled)
                {
                    SetPropertyToValue(camera.ShutterSpeed, savedPhotoSettings.ShutterSpeed);
                }
                
                if (camera.WhiteBalance != null && camera.WhiteBalance.IsEnabled)
                {
                    SetPropertyToValue(camera.WhiteBalance, savedPhotoSettings.WhiteBalance);
                }
                
                if (camera.FocusMode != null && camera.FocusMode.IsEnabled)
                {
                    SetPropertyToValue(camera.FocusMode, savedPhotoSettings.FocusMode);
                }
                
                if (camera.ExposureCompensation != null && camera.ExposureCompensation.IsEnabled)
                {
                    SetPropertyToValue(camera.ExposureCompensation, savedPhotoSettings.ExposureCompensation);
                }
                
                System.Diagnostics.Debug.WriteLine($"Restored photo settings - ISO: {savedPhotoSettings.ISO}, Aperture: {savedPhotoSettings.Aperture}");
                
                // Update the photo settings UI if we're on that tab
                LoadCurrentSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring photo settings: {ex.Message}");
            }
        }
        
        private void ApplyVideoSettingsToCamera()
        {
            if (DeviceManager.SelectedCameraDevice == null) return;
            
            try
            {
                // Only save photo settings if we're not already in video mode
                if (!isInVideoMode)
                {
                    SaveCurrentPhotoSettings();
                }
                
                var camera = DeviceManager.SelectedCameraDevice;
                
                // Apply each video setting to the camera
                // Note: The actual implementation depends on the camera's capabilities
                // and whether it supports these settings in video mode
                
                // ISO
                if (videoSettings.ISO != "Auto" && camera.IsoNumber != null && camera.IsoNumber.IsEnabled)
                {
                    SetPropertyToValue(camera.IsoNumber, videoSettings.ISO);
                }
                
                // Aperture
                if (videoSettings.Aperture != "Auto" && camera.FNumber != null && camera.FNumber.IsEnabled)
                {
                    SetPropertyToValue(camera.FNumber, videoSettings.Aperture);
                }
                
                // Shutter Speed
                if (videoSettings.ShutterSpeed != "Auto" && camera.ShutterSpeed != null && camera.ShutterSpeed.IsEnabled)
                {
                    SetPropertyToValue(camera.ShutterSpeed, videoSettings.ShutterSpeed);
                }
                
                // White Balance
                if (videoSettings.WhiteBalance != "Auto" && camera.WhiteBalance != null && camera.WhiteBalance.IsEnabled)
                {
                    SetPropertyToValue(camera.WhiteBalance, videoSettings.WhiteBalance);
                }
                
                // Focus Mode
                if (camera.FocusMode != null && camera.FocusMode.IsEnabled)
                {
                    SetPropertyToValue(camera.FocusMode, videoSettings.FocusMode);
                }
                
                // Exposure Compensation
                if (camera.ExposureCompensation != null && camera.ExposureCompensation.IsEnabled)
                {
                    SetPropertyToValue(camera.ExposureCompensation, videoSettings.ExposureCompensation);
                }
                
                isInVideoMode = true;
                System.Diagnostics.Debug.WriteLine("Applied video settings to camera");
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error applying video settings: {ex.Message}";
            }
        }
        
        private void SetPropertyToValue(PropertyValue<long> property, string targetValue)
        {
            if (property == null || !property.IsEnabled) return;
            
            // Find the target value in the property's available values
            foreach (var value in property.Values)
            {
                if (value == targetValue)
                {
                    property.Value = targetValue;
                    break;
                }
            }
        }
        
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            
            var selectedTab = e.AddedItems[0] as TabItem;
            if (selectedTab == null) return;
            
            var header = selectedTab.Header?.ToString() ?? "";
            
            System.Diagnostics.Debug.WriteLine($"Tab switched to: {header}");
            
            if (header.Contains("Video"))
            {
                // Switching to Video Settings tab
                if (!isInVideoMode)
                {
                    System.Diagnostics.Debug.WriteLine("Switching to Video tab - applying video settings");
                    ApplyVideoSettingsToCamera();
                    
                    // Auto-start live view when switching to video tab
                    if (!isVideoLiveViewActive && liveViewToggleButton != null)
                    {
                        liveViewToggleButton.IsChecked = true;
                    }
                }
            }
            else if (header.Contains("Photo"))
            {
                // Switching to Photo Settings tab
                if (isInVideoMode)
                {
                    // Stop live view if active
                    if (isVideoLiveViewActive && liveViewToggleButton != null)
                    {
                        liveViewToggleButton.IsChecked = false;
                    }
                    
                    System.Diagnostics.Debug.WriteLine("Switching to Photo tab - restoring photo settings");
                    RestorePhotoSettings();
                    isInVideoMode = false;
                }
            }
        }
        
        // Live View Methods for Video Settings
        private void LiveViewToggle_Checked(object sender, RoutedEventArgs e)
        {
            StartVideoLiveView();
        }
        
        private void LiveViewToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            StopVideoLiveView();
        }
        
        private void StartVideoLiveView()
        {
            if (DeviceManager.SelectedCameraDevice == null)
            {
                cameraStatusText.Text = "No camera connected for live view";
                if (liveViewToggleButton != null)
                    liveViewToggleButton.IsChecked = false;
                return;
            }
            
            try
            {
                // Apply video settings before starting live view
                if (!isInVideoMode)
                {
                    ApplyVideoSettingsToCamera();
                }
                
                // Start live view
                DeviceManager.SelectedCameraDevice.StartLiveView();
                
                // Initialize timer for live view updates
                if (videoLiveViewTimer == null)
                {
                    videoLiveViewTimer = new DispatcherTimer();
                    videoLiveViewTimer.Interval = TimeSpan.FromMilliseconds(50); // 20 FPS for smoother real-time updates
                    videoLiveViewTimer.Tick += VideoLiveViewTimer_Tick;
                }
                
                videoLiveViewTimer.Start();
                isVideoLiveViewActive = true;
                
                // Update UI
                if (videoPlaceholder != null)
                    videoPlaceholder.Visibility = Visibility.Collapsed;
                if (liveViewInfoPanel != null)
                    liveViewInfoPanel.Visibility = Visibility.Visible;
                if (liveViewToggleButton != null)
                    liveViewToggleButton.Content = "⏹️ Stop Live View";
                
                // Update live view info text
                UpdateVideoLiveViewInfo();
                
                cameraStatusText.Text = "Live view started with video settings";
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error starting live view: {ex.Message}";
                if (liveViewToggleButton != null)
                    liveViewToggleButton.IsChecked = false;
            }
        }
        
        private void StopVideoLiveView()
        {
            try
            {
                if (videoLiveViewTimer != null)
                {
                    videoLiveViewTimer.Stop();
                }
                
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    DeviceManager.SelectedCameraDevice.StopLiveView();
                }
                
                isVideoLiveViewActive = false;
                
                // Update UI
                if (videoPlaceholder != null)
                    videoPlaceholder.Visibility = Visibility.Visible;
                if (liveViewInfoPanel != null)
                    liveViewInfoPanel.Visibility = Visibility.Collapsed;
                if (videoLiveViewImage != null)
                    videoLiveViewImage.Source = null;
                if (liveViewToggleButton != null)
                    liveViewToggleButton.Content = "📷 Start Live View";
                
                cameraStatusText.Text = "Live view stopped";
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = $"Error stopping live view: {ex.Message}";
            }
        }
        
        private void VideoLiveViewTimer_Tick(object sender, EventArgs e)
        {
            if (!isVideoLiveViewActive || DeviceManager.SelectedCameraDevice == null)
                return;
                
            try
            {
                var liveViewData = DeviceManager.SelectedCameraDevice.GetLiveViewImage();
                if (liveViewData != null && liveViewData.ImageData != null)
                {
                    using (var stream = new MemoryStream(liveViewData.ImageData))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze(); // Important for cross-thread operations
                        
                        if (videoLiveViewImage != null)
                            videoLiveViewImage.Source = bitmap;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Live view update error: {ex.Message}");
            }
        }
        
        private void UpdateVideoLiveViewInfo()
        {
            if (videoSettingsLiveText == null) return;
            
            var info = new System.Text.StringBuilder();
            info.AppendLine($"ISO: {videoSettings.ISO}");
            info.AppendLine($"Aperture: {videoSettings.Aperture}");
            info.AppendLine($"Shutter: {videoSettings.ShutterSpeed}");
            info.AppendLine($"WB: {videoSettings.WhiteBalance}");
            
            videoSettingsLiveText.Text = info.ToString();
        }
        
        // Keyboard shortcuts for quick adjustments during live view
        private void CameraSettings_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Only process if we're in video tab and live view is active
            if (!isInVideoMode || !isVideoLiveViewActive) return;
            
            bool handled = false;
            
            switch (e.Key)
            {
                // ISO adjustments (I key + arrows)
                case System.Windows.Input.Key.I:
                    if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up))
                    {
                        ChangeVideoSetting("ISO", true);
                        handled = true;
                    }
                    else if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down))
                    {
                        ChangeVideoSetting("ISO", false);
                        handled = true;
                    }
                    break;
                    
                // Aperture adjustments (A key + arrows)
                case System.Windows.Input.Key.A:
                    if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up))
                    {
                        ChangeVideoSetting("Aperture", true);
                        handled = true;
                    }
                    else if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down))
                    {
                        ChangeVideoSetting("Aperture", false);
                        handled = true;
                    }
                    break;
                    
                // Shutter speed adjustments (S key + arrows)
                case System.Windows.Input.Key.S:
                    if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up))
                    {
                        ChangeVideoSetting("ShutterSpeed", true);
                        handled = true;
                    }
                    else if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down))
                    {
                        ChangeVideoSetting("ShutterSpeed", false);
                        handled = true;
                    }
                    break;
                    
                // Exposure compensation (E key + arrows)
                case System.Windows.Input.Key.E:
                    if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up))
                    {
                        ChangeVideoSetting("ExposureCompensation", true);
                        handled = true;
                    }
                    else if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down))
                    {
                        ChangeVideoSetting("ExposureCompensation", false);
                        handled = true;
                    }
                    break;
                    
                // White balance (W key + arrows)
                case System.Windows.Input.Key.W:
                    if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up))
                    {
                        ChangeVideoSetting("WhiteBalance", true);
                        handled = true;
                    }
                    else if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down))
                    {
                        ChangeVideoSetting("WhiteBalance", false);
                        handled = true;
                    }
                    break;
            }
            
            if (handled)
            {
                e.Handled = true;
                
                // Show keyboard shortcut hint in status
                cameraStatusText.Text = "Keyboard shortcuts: I+↑↓ (ISO), A+↑↓ (Aperture), S+↑↓ (Shutter), E+↑↓ (Exp.Comp), W+↑↓ (WB)";
            }
        }
        
        #endregion
    }
}
