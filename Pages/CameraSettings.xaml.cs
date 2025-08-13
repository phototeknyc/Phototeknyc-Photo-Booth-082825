using CameraControl.Devices;
using CameraControl.Devices.Classes;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
        }

        private void CameraSettings_Unloaded(object sender, RoutedEventArgs e)
        {
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
            });
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
    }
}