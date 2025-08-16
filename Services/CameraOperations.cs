using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Database;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles all camera-related operations including capture, live view, and device management
    /// </summary>
    public class CameraOperations
    {
        private readonly Pages.PhotoboothTouchModern _parent;
        private DispatcherTimer _liveViewTimer;
        private bool _freezeImage = false;
        private DateTime _timeFreezeImage = DateTime.Now;
        private BitmapSource _lastLiveBitmap;
        
        // Access DeviceManager through parent
        private CameraDeviceManager DeviceManager => _parent.DeviceManager;
        
        // UI Elements that need to be accessed
        private readonly Image liveViewImage;
        private readonly ComboBox cameraComboBox;
        private readonly CheckBox captureInSdRamCheckBox;
        private readonly CheckBox reviewPhotoCheckBox;
        
        // Settings
        private bool _captureInSdRam = false;
        private bool _reviewPhotos = false;
        
        public CameraOperations(Pages.PhotoboothTouchModern parent)
        {
            _parent = parent;
            
            // Get UI elements from parent
            liveViewImage = parent.FindName("liveViewImage") as Image;
            cameraComboBox = parent.FindName("cameraComboBox") as ComboBox;
            captureInSdRamCheckBox = parent.FindName("captureInSdRamCheckBox") as CheckBox;
            reviewPhotoCheckBox = parent.FindName("reviewPhotoCheckBox") as CheckBox;
            
            // Initialize live view timer
            InitializeLiveViewTimer();
        }
        
        /// <summary>
        /// Initialize the live view timer
        /// </summary>
        private void InitializeLiveViewTimer()
        {
            _liveViewTimer = new DispatcherTimer();
            _liveViewTimer.Interval = TimeSpan.FromMilliseconds(100); // Update every 100ms
            _liveViewTimer.Tick += LiveViewTimer_Tick;
        }
        
        /// <summary>
        /// Start the live view timer
        /// </summary>
        public void StartLiveViewTimer()
        {
            _liveViewTimer?.Start();
        }
        
        /// <summary>
        /// Stop the live view timer
        /// </summary>
        public void StopLiveViewTimer()
        {
            _liveViewTimer?.Stop();
        }
        
        /// <summary>
        /// Live view timer tick event
        /// </summary>
        private void LiveViewTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (_freezeImage)
                {
                    if ((DateTime.Now - _timeFreezeImage).TotalSeconds > 2)
                    {
                        _freezeImage = false;
                    }
                    return;
                }

                var device = DeviceManager.SelectedCameraDevice;
                
                if (device == null || !device.GetCapability(CapabilityEnum.LiveView))
                    return;

                LiveViewData liveViewData = null;
                
                try
                {
                    liveViewData = device.GetLiveViewImage();
                }
                catch (Exception ex)
                {
                    Log.Debug($"Error getting live view image: {ex.Message}");
                    return;
                }

                if (liveViewData != null && liveViewData.ImageData != null)
                {
                    try
                    {
                        using (var memoryStream = new MemoryStream(liveViewData.ImageData))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = memoryStream;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            
                            _lastLiveBitmap = bitmap;
                            
                            // Update UI on main thread
                            _parent.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (liveViewImage != null)
                                {
                                    liveViewImage.Source = bitmap;
                                }
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Error processing live view image: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LiveViewTimer_Tick error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Start live view for the current camera
        /// </summary>
        public void StartLiveView()
        {
            try
            {
                var device = DeviceManager.SelectedCameraDevice;
                if (device != null && device.GetCapability(CapabilityEnum.LiveView))
                {
                    device.StartLiveView();
                    // Don't start our own timer - PhotoboothTouchModern manages the timer directly
                    // StartLiveViewTimer();
                    Log.Debug("Live view started successfully");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start live view: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stop live view for the current camera
        /// </summary>
        public void StopLiveView()
        {
            try
            {
                // Don't stop our timer - PhotoboothTouchModern manages it
                // StopLiveViewTimer();
                
                var device = DeviceManager.SelectedCameraDevice;
                if (device != null && device.GetCapability(CapabilityEnum.LiveView))
                {
                    device.StopLiveView();
                    Log.Debug("Live view stopped successfully");
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"StopLiveView failed (may not be running): {ex.Message}");
            }
        }
        
        /// <summary>
        /// Capture a photo with the current camera
        /// </summary>
        public void CapturePhoto()
        {
            try
            {
                var device = DeviceManager.SelectedCameraDevice;
                if (device == null)
                {
                    Log.Error("No camera device selected");
                    return;
                }
                
                // Freeze the live view image briefly
                _freezeImage = true;
                _timeFreezeImage = DateTime.Now;
                
                // Set capture in SD RAM if enabled
                if (_captureInSdRam && device.GetCapability(CapabilityEnum.CaptureInRam))
                {
                    device.CaptureInSdRam = true;
                }
                
                // Capture the photo
                Log.Debug("Capturing photo...");
                device.CapturePhoto();
                
                Log.Debug("Photo capture initiated");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to capture photo: {ex.Message}");
                _freezeImage = false;
                throw;
            }
        }
        
        /// <summary>
        /// Handle camera connected event
        /// </summary>
        public void HandleCameraConnected(ICameraDevice cameraDevice)
        {
            Log.Debug($"Camera connected: {cameraDevice.DisplayName}");
            
            _parent.Dispatcher.Invoke(() =>
            {
                // Update camera list
                UpdateCameraList();
                
                // Select the connected camera
                if (cameraComboBox != null)
                {
                    cameraComboBox.SelectedItem = cameraDevice;
                }
                
                // Initialize camera settings
                InitializeCameraSettings(cameraDevice);
            });
        }
        
        /// <summary>
        /// Handle camera disconnected event
        /// </summary>
        public void HandleCameraDisconnected(ICameraDevice cameraDevice)
        {
            Log.Debug($"Camera disconnected: {cameraDevice.DisplayName}");
            
            _parent.Dispatcher.Invoke(() =>
            {
                UpdateCameraList();
                
                // If this was the selected camera, clear the selection
                if (DeviceManager.SelectedCameraDevice == cameraDevice)
                {
                    if (cameraComboBox != null)
                    {
                        cameraComboBox.SelectedIndex = -1;
                    }
                }
            });
        }
        
        /// <summary>
        /// Handle camera selection changed
        /// </summary>
        public void HandleCameraSelected(ICameraDevice oldDevice, ICameraDevice newDevice)
        {
            Log.Debug($"Camera selection changed from {oldDevice?.DisplayName} to {newDevice?.DisplayName}");
            
            // Stop live view on old device
            if (oldDevice != null)
            {
                try
                {
                    oldDevice.StopLiveView();
                }
                catch { }
            }
            
            // Start live view on new device
            if (newDevice != null)
            {
                try
                {
                    InitializeCameraSettings(newDevice);
                    newDevice.StartLiveView();
                    StartLiveViewTimer();
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to initialize new camera: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Initialize camera settings
        /// </summary>
        private void InitializeCameraSettings(ICameraDevice device)
        {
            if (device == null) return;
            
            try
            {
                // Set capture target to memory card by default
                device.CaptureInSdRam = _captureInSdRam;
                
                // Set other default settings
                device.IsoNumber.SetValue("Auto");
                device.FNumber.SetValue("5.6");
                device.ShutterSpeed.SetValue("1/125");
                
                Log.Debug($"Camera settings initialized for {device.DisplayName}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize camera settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update the camera list in the UI
        /// </summary>
        private void UpdateCameraList()
        {
            if (cameraComboBox == null) return;
            
            _parent.Dispatcher.Invoke(() =>
            {
                cameraComboBox.Items.Clear();
                
                foreach (var device in DeviceManager.ConnectedDevices)
                {
                    cameraComboBox.Items.Add(device);
                }
                
                // Select the current device if any
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    cameraComboBox.SelectedItem = DeviceManager.SelectedCameraDevice;
                }
            });
        }
        
        /// <summary>
        /// Reset camera for next capture
        /// </summary>
        public async Task ResetCameraForNextCapture()
        {
            try
            {
                Log.Debug("Resetting camera for next capture...");
                
                // Stop and restart live view
                StopLiveView();
                await Task.Delay(500); // Small delay to ensure camera is ready
                StartLiveView();
                
                Log.Debug("Camera reset completed");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to reset camera: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Test capture functionality
        /// </summary>
        public void TestCapture()
        {
            try
            {
                Log.Debug("Starting test capture...");
                CapturePhoto();
            }
            catch (Exception ex)
            {
                Log.Error($"Test capture failed: {ex.Message}");
                _parent.ShowSimpleMessage($"Test capture failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get/Set capture in SD RAM setting
        /// </summary>
        public bool CaptureInSdRam
        {
            get => _captureInSdRam;
            set
            {
                _captureInSdRam = value;
                var device = DeviceManager.SelectedCameraDevice;
                if (device != null && device.GetCapability(CapabilityEnum.CaptureInRam))
                {
                    device.CaptureInSdRam = value;
                }
            }
        }
        
        /// <summary>
        /// Get/Set review photos setting
        /// </summary>
        public bool ReviewPhotos
        {
            get => _reviewPhotos;
            set => _reviewPhotos = value;
        }
        
        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            StopLiveViewTimer();
            _liveViewTimer = null;
        }
    }
}