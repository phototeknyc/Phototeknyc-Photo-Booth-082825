using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool _isInitialized = false;
        private bool _autoSaveEnabled = true;
        private DispatcherTimer _liveViewTimer;
        private DispatcherTimer _autoRestartTimer;
        private bool _isLiveViewActive = false;
        private ICameraDevice _currentCamera;

        public CameraSettingsOverlay()
        {
            // Initialize the service BEFORE InitializeComponent to ensure it's available for any events
            _cameraSettingsService = CameraSettingsService.Instance;
            
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
                }
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
                    // Convert to BitmapImage
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(liveViewData.ImageData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // Update the live view image
                    LiveViewImage.Source = bitmap;
                    LiveViewImage.Visibility = Visibility.Visible;
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                    LiveViewStatus.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // Silently ignore live view errors to avoid spam
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
            // Extra defensive null checks
            if (_cameraSettingsService == null || ISOComboBox == null || ISOValueText == null || !_isInitialized)
                return;
                
            if (ISOComboBox.SelectedItem != null)
            {
                var content = (ISOComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    ISOValueText.Text = content;
                    _cameraSettingsService.OnSettingChanged("ISO", content);
                    AutoSaveIfEnabled();
                }
            }
        }

        private void ApertureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Extra defensive null checks
            if (_cameraSettingsService == null || ApertureComboBox == null || ApertureValueText == null || !_isInitialized)
                return;
                
            if (ApertureComboBox.SelectedItem != null)
            {
                var content = (ApertureComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    ApertureValueText.Text = content;
                    _cameraSettingsService.OnSettingChanged("Aperture", content);
                    AutoSaveIfEnabled();
                }
            }
        }

        private void ShutterSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Extra defensive null checks
            if (_cameraSettingsService == null || ShutterSpeedComboBox == null || ShutterSpeedValueText == null || !_isInitialized)
                return;
                
            if (ShutterSpeedComboBox.SelectedItem != null)
            {
                var content = (ShutterSpeedComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    ShutterSpeedValueText.Text = content;
                    _cameraSettingsService.OnSettingChanged("ShutterSpeed", content);
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
                    _cameraSettingsService.OnSettingChanged("WhiteBalance", content);
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
            _cameraSettingsService.OnSettingChanged("ExposureCompensation", value.ToString());
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
    }
}