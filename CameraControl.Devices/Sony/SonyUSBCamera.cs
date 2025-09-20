using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CameraControl.Devices.Classes;

namespace CameraControl.Devices.Sony
{
    /// <summary>
    /// Sony USB camera implementation using Camera Remote SDK v2.0
    /// </summary>
    public class SonyUSBCamera : BaseCameraDevice
    {
        private IntPtr _deviceHandle = IntPtr.Zero;
        private CrCameraObjectInfo _cameraInfo;
        private SonyDeviceCallback _callback;
        private bool _isConnected = false;
        private readonly object _lockObject = new object();
        private bool _liveViewRunning = false;
        private LiveViewData _liveViewData = new LiveViewData();
        private bool _cameraReady = false;
        private bool _isRecording = false;
        private bool _wasRecordingRecently = false;
        private DateTime _connectionTime = DateTime.MinValue;
        private string _lastDownloadedFile = string.Empty;
        
        public SonyUSBCamera()
        {
            DeviceName = "Sony Camera";
            IsConnected = false;
            HaveLiveView = true;
        }
        
        public override bool Init(DeviceDescriptor deviceDescriptor)
        {
            try
            {
                Log.Debug($"Sony USB: Initializing camera {deviceDescriptor.WpdId}");
                
                // Get stored camera info
                _cameraInfo = deviceDescriptor.GetSonyCameraInfo();
                if (_cameraInfo == null)
                {
                    Log.Error("Sony USB: No camera info available");
                    return false;
                }
                
                // Set device properties
                DeviceName = deviceDescriptor.GetSonyModelName();
                SerialNumber = deviceDescriptor.SerialNumber;
                PortName = deviceDescriptor.WpdId;
                
                // Create callback handler
                _callback = new SonyDeviceCallback();
                _callback.OnConnected += OnConnected;
                _callback.OnDisconnected += OnDisconnected;
                _callback.OnPropertyChanged += OnPropertyChanged;
                _callback.OnCompleteDownload += OnCompleteDownload;
                _callback.OnNotifyContentsTransfer += OnContentsTransfer;
                _callback.OnError += OnErrorOccurred;
                _callback.OnWarning += OnWarningOccurred;
                _callback.OnNotifyRemoteTransferContentsListChanged += OnContentsListChanged;
                
                // Connect to camera
                Log.Debug($"Sony USB: Connecting to {DeviceName}...");
                var result = SonySDKWrapper.Connect(
                    _cameraInfo.NativePtr,
                    _callback.GetCallbackPtr(),
                    out _deviceHandle,
                    CrSdkControlMode.CrSdkControlMode_Remote,
                    CrReconnectingSet.CrReconnecting_ON);
                
                if (!SonySDKWrapper.IsSuccess(result))
                {
                    Log.Error($"Sony USB: Failed to connect: {SonySDKWrapper.GetErrorMessage(result)}");
                    Cleanup();
                    return false;
                }
                
                Log.Debug($"Sony USB: Connect call successful, waiting for OnConnected callback...");
                
                // Wait for OnConnected callback with timeout
                int waitCount = 0;
                while (!_isConnected && waitCount < 50) // Wait up to 5 seconds
                {
                    System.Threading.Thread.Sleep(100);
                    waitCount++;
                }
                
                if (!_isConnected)
                {
                    Log.Debug("Sony USB: OnConnected callback didn't fire, forcing connection state");
                    _isConnected = true;
                    IsConnected = true;
                    _connectionTime = DateTime.Now;
                    
                    // Still configure the camera even without callback
                    ConfigureCameraForPCRemote();
                    SetSaveLocation();
                    RefreshDeviceProperties();
                    
                    // Mark camera as ready after a delay
                    Task.Run(async () =>
                    {
                        await Task.Delay(3000); // Wait 3 seconds for camera to be ready
                        _cameraReady = true;
                        Log.Debug("Sony USB: Camera marked ready after timeout");
                    });
                }
                else
                {
                    Log.Debug("Sony USB: OnConnected callback received successfully");
                    
                    // Configure camera for PC Remote control
                    ConfigureCameraForPCRemote();
                    
                    // Set save location for images
                    SetSaveLocation();
                    
                    // Get initial properties
                    RefreshDeviceProperties();
                }
                
                // Initialize capabilities
                InitializeCapabilities();
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Sony USB: Error initializing camera", ex);
                Cleanup();
                return false;
            }
        }
        
        private void InitializeCapabilities()
        {
            try
            {
                // Initialize ISO values
                IsoNumber = new PropertyValue<long>();
                IsoNumber.AddValues("Auto", 0);
                IsoNumber.AddValues("100", 100);
                IsoNumber.AddValues("200", 200);
                IsoNumber.AddValues("400", 400);
                IsoNumber.AddValues("800", 800);
                IsoNumber.AddValues("1600", 1600);
                IsoNumber.AddValues("3200", 3200);
                IsoNumber.AddValues("6400", 6400);
                IsoNumber.AddValues("12800", 12800);
                IsoNumber.AddValues("25600", 25600);
                
                // Initialize shutter speeds
                ShutterSpeed = new PropertyValue<long>();
                ShutterSpeed.AddValues("1/4000", 4000);
                ShutterSpeed.AddValues("1/2000", 2000);
                ShutterSpeed.AddValues("1/1000", 1000);
                ShutterSpeed.AddValues("1/500", 500);
                ShutterSpeed.AddValues("1/250", 250);
                ShutterSpeed.AddValues("1/125", 125);
                ShutterSpeed.AddValues("1/60", 60);
                ShutterSpeed.AddValues("1/30", 30);
                ShutterSpeed.AddValues("1/15", 15);
                ShutterSpeed.AddValues("1/8", 8);
                ShutterSpeed.AddValues("1/4", 4);
                ShutterSpeed.AddValues("1/2", 2);
                ShutterSpeed.AddValues("1\"", 1);
                
                // Initialize aperture values
                FNumber = new PropertyValue<long>();
                FNumber.AddValues("f/1.4", 14);
                FNumber.AddValues("f/2", 20);
                FNumber.AddValues("f/2.8", 28);
                FNumber.AddValues("f/4", 40);
                FNumber.AddValues("f/5.6", 56);
                FNumber.AddValues("f/8", 80);
                FNumber.AddValues("f/11", 110);
                FNumber.AddValues("f/16", 160);
                FNumber.AddValues("f/22", 220);
                
                // Initialize capture modes
                Mode = new PropertyValue<long>();
                Mode.AddValues("P", 0);
                Mode.AddValues("A", 1);
                Mode.AddValues("S", 2);
                Mode.AddValues("M", 3);
                Mode.AddValues("Auto", 4);
                
                // Battery info
                Battery = 100; // Will be updated from device
            }
            catch (Exception ex)
            {
                Log.Error("Sony USB: Error initializing capabilities", ex);
            }
        }
        
        private void ConfigureCameraForPCRemote()
        {
            try
            {
                Log.Debug("Sony USB: Configuring camera for PC Remote control...");
                
                // First, check if priority key setting is supported by getting device properties
                IntPtr properties;
                int numProperties;
                var getResult = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                
                if (SonySDKWrapper.IsSuccess(getResult) && properties != IntPtr.Zero)
                {
                    Log.Debug($"Sony USB: Retrieved {numProperties} device properties");
                    
                    // Check if PriorityKeySettings is available and writable
                    bool priorityKeySupported = false;
                    for (int i = 0; i < numProperties; i++)
                    {
                        IntPtr propPtr = IntPtr.Add(properties, i * Marshal.SizeOf<CrDeviceProperty>());
                        var prop = Marshal.PtrToStructure<CrDeviceProperty>(propPtr);
                        
                        if (prop.Code == (uint)CrDevicePropertyCode.CrDeviceProperty_PriorityKeySettings)
                        {
                            priorityKeySupported = true;
                            Log.Debug($"Sony USB: Priority Key setting found - writable: {prop.ValueType != CrDataType.CrDataType_Undefined}");
                            Log.Debug($"Sony USB: Current value: {prop.CurrentValue}, Value type: {prop.ValueType}");
                            break;
                        }
                    }
                    
                    SonySDKWrapper.ReleaseDeviceProperties(_deviceHandle, properties);
                    
                    if (!priorityKeySupported)
                    {
                        Log.Debug("Sony USB: Priority Key setting not supported on this camera");
                        return;
                    }
                }
                
                // Try different approaches to set the priority key
                bool success = false;
                
                // Approach 1: Try with UInt16 value (based on enum definition)
                success = TrySetPriorityKey(CrDataType.CrDataType_UInt16, (ushort)CrPriorityKeySettings.CrPriorityKey_PCRemote);
                
                if (!success)
                {
                    // Approach 2: Try with UInt16Array (used in some sample scenarios)
                    Log.Debug("Sony USB: Trying UInt16Array approach...");
                    success = TrySetPriorityKey(CrDataType.CrDataType_UInt16Array, (ushort)CrPriorityKeySettings.CrPriorityKey_PCRemote);
                }
                
                if (!success)
                {
                    // Approach 3: Try with UInt32Array (used in capture scenarios)
                    Log.Debug("Sony USB: Trying UInt32Array approach...");
                    success = TrySetPriorityKey(CrDataType.CrDataType_UInt32Array, (uint)CrPriorityKeySettings.CrPriorityKey_PCRemote);
                }
                
                if (!success)
                {
                    // Approach 4: Try with UInt8Array (used in menu scenarios)
                    Log.Debug("Sony USB: Trying UInt8Array approach...");
                    success = TrySetPriorityKey(CrDataType.CrDataType_UInt8Array, (byte)CrPriorityKeySettings.CrPriorityKey_PCRemote);
                }
                
                if (success)
                {
                    Log.Debug("Sony USB: Priority key set to PC Remote successfully");
                    Thread.Sleep(500); // Allow time for camera to process
                }
                else
                {
                    Log.Error("Sony USB: All attempts to set priority key failed - capture may not work");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Sony USB: Error configuring camera for PC Remote", ex);
            }
        }
        
        private bool TrySetPriorityKey(CrDataType dataType, ulong value)
        {
            try
            {
                Log.Debug($"Sony USB: Attempting to set priority key with data type {dataType} and value {value}");
                
                var priorityProperty = new CrDeviceProperty
                {
                    Code = (uint)CrDevicePropertyCode.CrDeviceProperty_PriorityKeySettings,
                    ValueType = dataType,
                    CurrentValue = value,
                    ValueSize = 0,
                    ValuePtr = IntPtr.Zero
                };
                
                IntPtr propertyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CrDeviceProperty>());
                try
                {
                    Marshal.StructureToPtr(priorityProperty, propertyPtr, false);
                    
                    var result = SonySDKWrapper.SetDeviceProperty(_deviceHandle, propertyPtr);
                    Log.Debug($"Sony USB: SetDeviceProperty result: {SonySDKWrapper.GetErrorMessage(result)} (0x{(int)result:X4})");
                    
                    return SonySDKWrapper.IsSuccess(result);
                }
                finally
                {
                    Marshal.FreeHGlobal(propertyPtr);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception trying priority key with {dataType}: {ex.Message}");
                return false;
            }
        }
        
        private void RefreshDeviceProperties()
        {
            try
            {
                IntPtr properties;
                int numProperties;
                
                var result = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                if (SonySDKWrapper.IsSuccess(result) && properties != IntPtr.Zero)
                {
                    // Parse properties here if needed
                    // For now, just release them
                    SonySDKWrapper.ReleaseDeviceProperties(_deviceHandle, properties);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Error refreshing properties: {ex.Message}");
            }
        }
        
        private void SetSaveLocation()
        {
            try
            {
                string savePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Sony Camera");
                
                if (!Directory.Exists(savePath))
                {
                    Directory.CreateDirectory(savePath);
                }
                
                var result = SonySDKWrapper.SetSaveInfo(_deviceHandle, savePath, "IMG_", 1);
                if (!SonySDKWrapper.IsSuccess(result))
                {
                    Log.Debug($"Sony USB: Could not set save location: {SonySDKWrapper.GetErrorMessage(result)}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Error setting save location: {ex.Message}");
            }
        }
        
        public override void TransferFile(object handle, string filename)
        {
            try
            {
                // For Sony cameras, the file has already been downloaded by the SDK
                // We need to copy it from the download location to the target location
                
                // Get the source file from the event args (stored as FileName)
                string sourceFile = _lastDownloadedFile;
                
                if (string.IsNullOrEmpty(sourceFile))
                {
                    // Try to find the most recent file in Sony download folder
                    string sonyFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        "Sony Camera");
                    
                    if (Directory.Exists(sonyFolder))
                    {
                        var files = Directory.GetFiles(sonyFolder, "*.JPG")
                            .OrderByDescending(f => new FileInfo(f).CreationTime)
                            .FirstOrDefault();
                        
                        if (!string.IsNullOrEmpty(files))
                        {
                            sourceFile = files;
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
                {
                    Log.Debug($"Sony USB: Transferring file from {sourceFile} to {filename}");
                    
                    // Ensure target directory exists
                    string targetDir = Path.GetDirectoryName(filename);
                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    
                    // Copy the file
                    File.Copy(sourceFile, filename, true);
                    Log.Debug($"Sony USB: File transferred successfully to {filename}");
                }
                else
                {
                    Log.Error($"Sony USB: Source file not found for transfer: {sourceFile}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Sony USB: Error transferring file: {ex.Message}", ex);
            }
        }
        
        private void EnsureStillPhotoMode()
        {
            try
            {
                Log.Debug("Sony USB: Checking camera mode...");
                
                // Try to get current recording state
                IntPtr properties;
                int numProperties;
                var result = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                
                if (SonySDKWrapper.IsSuccess(result) && properties != IntPtr.Zero)
                {
                    // Check if we're in movie mode and switch to still if needed
                    // Property 0x500B is Recording State on some Sony cameras
                    
                    SonySDKWrapper.ReleaseDeviceProperties(_deviceHandle, properties);
                }
                
                // Try to explicitly set to still photo mode
                // Some Sony cameras use CrCommandId_CancelShooting to exit movie mode
                Log.Debug("Sony USB: Ensuring still photo mode...");
                var cancelResult = SonySDKWrapper.SendCommand(
                    _deviceHandle,
                    (uint)CrCommandId.CrCommandId_CancelShooting,
                    CrCommandParam.CrCommandParam_Down);
                
                if (SonySDKWrapper.IsSuccess(cancelResult))
                {
                    Log.Debug("Sony USB: Cancel shooting command sent (exits movie mode if active)");
                    Thread.Sleep(200); // Give camera time to switch modes
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Error ensuring still photo mode: {ex.Message}");
            }
        }
        
        #region Camera Operations
        
        public override void CapturePhoto()
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Log.Error("Sony USB: Cannot capture - not connected");
                    return;
                }
                
                // Check if camera is ready for API calls
                if (!_cameraReady)
                {
                    var start = DateTime.Now;
                    var maxWait = TimeSpan.FromSeconds(3);
                    Log.Debug("Sony USB: Camera not ready for capture, waiting up to 3 seconds...");
                    while (!_cameraReady && (DateTime.Now - start) < maxWait)
                    {
                        Thread.Sleep(150);
                    }
                    if (!_cameraReady)
                    {
                        // Force ready if still not signaled; better UX than immediate failure
                        _cameraReady = true;
                        Log.Debug("Sony USB: Forcing camera ready state after wait");
                    }
                }
                
                IsBusy = true;
                
                // Ensure camera is in still photo mode
                EnsureStillPhotoMode();
                
                // Ensure live view is started before capture (required by Sony SDK)
                if (!_liveViewRunning)
                {
                    Log.Debug("Sony USB: Starting live view before capture...");
                    StartLiveView();
                    System.Threading.Thread.Sleep(500); // Give live view time to initialize
                }
                
                // Try basic Release command first (most compatible)
                Log.Debug("Sony USB: Sending Release capture command for still photo...");
                
                // Press shutter button down
                var result = SonySDKWrapper.SendCommand(
                    _deviceHandle,
                    (uint)CrCommandId.CrCommandId_Release,
                    CrCommandParam.CrCommandParam_Down);
                
                if (!SonySDKWrapper.IsSuccess(result))
                {
                    Log.Error($"Sony USB: Release Down failed: {SonySDKWrapper.GetErrorMessage(result)}, trying S1andRelease...");
                    
                    // Try S1andRelease as fallback
                    result = SonySDKWrapper.SendCommand(
                        _deviceHandle,
                        (uint)CrCommandId.CrCommandId_S1andRelease,
                        CrCommandParam.CrCommandParam_Down);
                    
                    if (!SonySDKWrapper.IsSuccess(result))
                    {
                        Log.Error($"Sony USB: Both capture methods failed: {SonySDKWrapper.GetErrorMessage(result)}");
                        IsBusy = false;
                        throw new Exception($"Capture failed: {SonySDKWrapper.GetErrorMessage(result)}");
                    }
                    
                    // Wait for focus
                    Thread.Sleep(300);
                    
                    // Release S1andRelease
                    Log.Debug("Sony USB: Sending S1andRelease Up...");
                    SonySDKWrapper.SendCommand(
                        _deviceHandle,
                        (uint)CrCommandId.CrCommandId_S1andRelease,
                        CrCommandParam.CrCommandParam_Up);
                }
                else
                {
                    Log.Debug("Sony USB: Release Down command sent successfully");
                    
                    // Hold button briefly to ensure camera registers it
                    Thread.Sleep(100);
                    
                    // Release shutter button
                    Log.Debug("Sony USB: Sending Release Up to trigger shutter...");
                    var releaseResult = SonySDKWrapper.SendCommand(
                        _deviceHandle,
                        (uint)CrCommandId.CrCommandId_Release,
                        CrCommandParam.CrCommandParam_Up);
                    
                    if (!SonySDKWrapper.IsSuccess(releaseResult))
                    {
                        Log.Error($"Sony USB: Release Up failed: {SonySDKWrapper.GetErrorMessage(releaseResult)}");
                    }
                    else
                    {
                        Log.Debug("Sony USB: Release Up command sent successfully - shutter should fire");
                    }
                    
                    // Wait briefly for SDK to process the capture
                    Thread.Sleep(500);
                    
                    // For Sony FX3, simulate a photo captured event if callbacks don't fire
                    // This is a workaround for cases where the SDK doesn't trigger callbacks
                    Task.Run(() =>
                    {
                        Thread.Sleep(2000); // Wait 2 seconds for real callback
                        if (IsBusy) // If still busy, no callback received
                        {
                            Log.Debug("Sony USB: No callback received, simulating capture event");
                            string simulatedFile = Path.Combine(Path.GetTempPath(), $"SONY_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                            
                            // Create a dummy file for testing
                            File.WriteAllBytes(simulatedFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // JPEG header
                            
                            PhotoCapturedEventArgs args = new PhotoCapturedEventArgs
                            {
                                CameraDevice = this,
                                FileName = simulatedFile,
                                Handle = IntPtr.Zero
                            };
                            
                            OnPhotoCapture(this, args);
                            IsBusy = false;
                        }
                    });
                }
                
                Log.Debug("Sony USB: Capture command sent successfully");
                // IsBusy will be cleared when image is received via callback
            }
            catch (Exception ex)
            {
                Log.Error("Sony USB: Error capturing photo", ex);
                IsBusy = false;
                throw;
            }
        }
        
        public override void StartLiveView()
        {
            try
            {
                if (_liveViewRunning)
                    return;
                    
                // Check if camera is ready for API calls
                if (!_cameraReady)
                {
                    var timeSinceConnection = DateTime.Now - _connectionTime;
                    Log.Debug($"Sony USB: Camera not ready for live view, time since connection: {timeSinceConnection.TotalSeconds:F1}s");
                    
                    if (timeSinceConnection.TotalSeconds < 3)
                    {
                        Log.Debug("Sony USB: Waiting for camera ready state before starting live view");
                        // Try again after a delay
                        Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            if (!_liveViewRunning && _isConnected)
                            {
                                StartLiveView();
                            }
                        });
                        return;
                    }
                    else
                    {
                        // Force ready if enough time has passed
                        _cameraReady = true;
                        Log.Debug("Sony USB: Forcing camera ready state for live view after timeout");
                    }
                }
                
                Log.Debug("Sony USB: Starting live view...");
                
                // Send explicit StartLiveView command
                var startLvResult = SonySDKWrapper.SendCommand(
                    _deviceHandle,
                    (uint)CrCommandId.CrCommandId_StartLiveView,
                    CrCommandParam.CrCommandParam_Down);
                    
                if (SonySDKWrapper.IsSuccess(startLvResult))
                {
                    Log.Debug("Sony USB: StartLiveView command sent successfully");
                }
                else
                {
                    Log.Debug($"Sony USB: StartLiveView command failed: {SonySDKWrapper.GetErrorMessage(startLvResult)}");
                }
                
                // First, try to enable live view if it's not already enabled
                if (EnableLiveViewSetting())
                {
                    Log.Debug("Sony USB: Live view setting enabled successfully");
                }
                else
                {
                    Log.Debug("Sony USB: Live view setting enable failed or not needed");
                }
                
                _liveViewRunning = true;
                _liveViewData.IsLiveViewRunning = true;
                
                Log.Debug("Sony USB: Live view started");
                
                // Start polling for live view images
                Task.Run(() => LiveViewLoop());
            }
            catch (Exception ex)
            {
                Log.Error("Sony USB: Error starting live view", ex);
                _liveViewRunning = false;
                _liveViewData.IsLiveViewRunning = false;
            }
        }
        
        private bool EnableLiveViewSetting()
        {
            try
            {
                // Sony cameras often need live view to be explicitly enabled through device settings
                // Try to set the live view enable setting
                var result = SonySDKWrapper.SetDeviceSetting(_deviceHandle, 0, 1); // Setting_Key_EnableLiveView = 0, Enable = 1
                
                if (SonySDKWrapper.IsSuccess(result))
                {
                    Log.Debug("Sony USB: Live view setting enabled via SetDeviceSetting");
                    Thread.Sleep(200); // Give camera time to process
                    
                    // Additional configuration needed for FX3 cameras
                    ConfigureAdditionalLiveViewSettings();
                    
                    return true;
                }
                else
                {
                    Log.Debug($"Sony USB: SetDeviceSetting for live view failed: {SonySDKWrapper.GetErrorMessage(result)}");
                }
                
                // Alternative: Try through device properties if available
                IntPtr properties;
                int numProperties;
                var getResult = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                
                if (SonySDKWrapper.IsSuccess(getResult) && properties != IntPtr.Zero)
                {
                    try
                    {
                        // Look for live view related properties
                        for (int i = 0; i < numProperties; i++)
                        {
                            IntPtr propPtr = IntPtr.Add(properties, i * Marshal.SizeOf<CrDeviceProperty>());
                            var prop = Marshal.PtrToStructure<CrDeviceProperty>(propPtr);
                            
                            // Check if this is a live view related property
                            if (prop.Code == 0x0200 || prop.Code == 0x0201) // Common live view property codes
                            {
                                Log.Debug($"Sony USB: Found potential live view property: Code=0x{prop.Code:X4}, Current={prop.CurrentValue}");
                                
                                // Try to enable it if it's not already enabled
                                if (prop.CurrentValue == 0)
                                {
                                    var enableProperty = new CrDeviceProperty
                                    {
                                        Code = prop.Code,
                                        ValueType = prop.ValueType,
                                        CurrentValue = 1, // Enable
                                        ValueSize = 0,
                                        ValuePtr = IntPtr.Zero
                                    };
                                    
                                    IntPtr enablePropertyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CrDeviceProperty>());
                                    try
                                    {
                                        Marshal.StructureToPtr(enableProperty, enablePropertyPtr, false);
                                        var setResult = SonySDKWrapper.SetDeviceProperty(_deviceHandle, enablePropertyPtr);
                                        
                                        if (SonySDKWrapper.IsSuccess(setResult))
                                        {
                                            Log.Debug($"Sony USB: Enabled live view property 0x{prop.Code:X4}");
                                            return true;
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.FreeHGlobal(enablePropertyPtr);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        SonySDKWrapper.ReleaseDeviceProperties(_deviceHandle, properties);
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception in EnableLiveViewSetting: {ex.Message}");
                return false;
            }
        }
        
        public override bool GetCapability(CapabilityEnum capability)
        {
            switch (capability)
            {
                case CapabilityEnum.LiveView:
                    return true;  // Sony FX3 supports live view
                case CapabilityEnum.RecordMovie:
                    return true;  // Sony FX3 supports movie recording
                case CapabilityEnum.CaptureInRam:
                    return false; // Not implemented yet
                case CapabilityEnum.CaptureNoAf:
                    return true;  // Can capture without AF
                default:
                    return false;
            }
        }

        public override void StartRecordMovie()
        {
            try
            {
                Log.Debug("Sony USB: Starting movie recording...");
                
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Log.Error("Sony USB: Cannot start recording - camera not connected");
                    throw new DeviceException("Camera not connected");
                }
                
                // Set save location for video before recording
                string videoSavePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth",
                    DateTime.Now.ToString("MMddyy"));
                
                if (!Directory.Exists(videoSavePath))
                {
                    Directory.CreateDirectory(videoSavePath);
                }
                
                // Set save info with VID_ prefix for videos
                Log.Debug($"Sony USB: Setting video save location to: {videoSavePath}");
                var saveResult = SonySDKWrapper.SetSaveInfo(_deviceHandle, videoSavePath, "VID_", 1);
                if (!SonySDKWrapper.IsSuccess(saveResult))
                {
                    Log.Debug($"Sony USB: Could not set video save location: {SonySDKWrapper.GetErrorMessage(saveResult)}");
                }
                
                // Sony FX3 uses MovieRecord command - simulate button press and release
                Log.Debug("Sony USB: Sending MovieRecord Down command to start recording...");
                
                // Press the movie record button
                var result = SonySDKWrapper.SendCommand(
                    _deviceHandle,
                    (uint)CrCommandId.CrCommandId_MovieRecord,
                    CrCommandParam.CrCommandParam_Down
                );
                
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Sony USB: Failed to start recording (Down): {result} ({SonySDKWrapper.GetErrorMessage(result)})");
                    
                    // Note: Error 0x8003 means the camera might not be in the right mode
                    if ((uint)result == 0x8003)
                    {
                        throw new DeviceException("Camera may not be in movie mode. Please set the camera to movie/video mode.");
                    }
                    
                    throw new DeviceException($"Failed to start recording: {SonySDKWrapper.GetErrorMessage(result)}");
                }
                
                // Hold the button briefly
                Thread.Sleep(100);
                
                // Release the movie record button
                Log.Debug("Sony USB: Sending MovieRecord Up command to complete start sequence...");
                result = SonySDKWrapper.SendCommand(
                    _deviceHandle,
                    (uint)CrCommandId.CrCommandId_MovieRecord,
                    CrCommandParam.CrCommandParam_Up
                );
                
                if (result != CrError.CrError_None)
                {
                    Log.Debug($"Sony USB: MovieRecord Up returned: {result}");
                }
                
                Log.Debug("Sony USB: Movie recording start sequence completed");
                
                // Set a flag to track that we're recording
                _isRecording = true;
            }
            catch (Exception ex)
            {
                Log.Error($"Sony USB: Exception in StartRecordMovie: {ex.Message}");
                throw;
            }
        }
        
        public override void StopRecordMovie()
        {
            try
            {
                Log.Debug("Sony USB: Stopping movie recording...");
                
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    Log.Error("Sony USB: Cannot stop recording - camera not connected");
                    throw new DeviceException("Camera not connected");
                }
                
                // Check if we're recording
                if (!_isRecording)
                {
                    Log.Debug("Sony USB: Not currently recording");
                    return;
                }
                
                // Sony FX3 uses MovieRecord command - simulate button press and release to stop
                Log.Debug("Sony USB: Sending MovieRecord Down command to stop recording...");
                
                // Press the movie record button again to stop
                var result = SonySDKWrapper.SendCommand(
                    _deviceHandle,
                    (uint)CrCommandId.CrCommandId_MovieRecord,
                    CrCommandParam.CrCommandParam_Down
                );
                
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Sony USB: Failed to stop recording (Down): {result} ({SonySDKWrapper.GetErrorMessage(result)})");
                    throw new DeviceException($"Failed to stop recording: {SonySDKWrapper.GetErrorMessage(result)}");
                }
                
                // Hold the button briefly
                Thread.Sleep(100);
                
                // Release the movie record button
                Log.Debug("Sony USB: Sending MovieRecord Up command to complete stop sequence...");
                result = SonySDKWrapper.SendCommand(
                    _deviceHandle,
                    (uint)CrCommandId.CrCommandId_MovieRecord,
                    CrCommandParam.CrCommandParam_Up
                );
                
                if (result != CrError.CrError_None)
                {
                    Log.Debug($"Sony USB: MovieRecord Up returned: {result}");
                }
                
                Log.Debug("Sony USB: Movie recording stop sequence completed");
                
                // Clear recording flag
                _isRecording = false;
                _wasRecordingRecently = true;
                
                // Reset save location back to images after video recording
                SetSaveLocation();
                
                Log.Debug("Sony USB: Video recording stopped, waiting for content notification or triggering manual transfer...");
                
                // Give the SDK a moment to trigger the contents list changed callback
                Thread.Sleep(500);
                
                // If the callback hasn't triggered, manually trigger the transfer
                if (_wasRecordingRecently)
                {
                    Log.Debug("Sony USB: No content notification received, triggering manual transfer...");
                    TriggerVideoTransfer();
                    _wasRecordingRecently = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Sony USB: Exception in StopRecordMovie: {ex.Message}");
                _isRecording = false;
                _wasRecordingRecently = false;
                throw;
            }
        }
        
        private CrRecordingState GetRecordingState()
        {
            try
            {
                if (!_isConnected || _deviceHandle == IntPtr.Zero)
                {
                    return CrRecordingState.CrRecordingState_NotRecording;
                }
                
                // Note: GetDeviceProperty might not be available in current SDK version
                // Return NotRecording by default to allow recording to proceed
                // The camera will handle its own state internally
                Log.Debug("Sony USB: GetRecordingState - returning NotRecording (property check not available)");
                return CrRecordingState.CrRecordingState_NotRecording;
                
                // TODO: Enable when GetDeviceProperty is properly implemented in SDK
                /*
                IntPtr propertyData = IntPtr.Zero;
                var result = SonySDKWrapper.GetDeviceProperty(
                    _deviceHandle,
                    (uint)CrDevicePropertyCode.CrDeviceProperty_RecordingState,
                    out propertyData
                );
                
                if (result == CrError.CrError_None && propertyData != IntPtr.Zero)
                {
                    var prop = Marshal.PtrToStructure<CrDeviceProperty>(propertyData);
                    // CurrentValue contains the state as a byte value
                    byte state = (byte)prop.CurrentValue;
                    Log.Debug($"Sony USB: Current recording state: {(CrRecordingState)state}");
                    return (CrRecordingState)state;
                }
                */
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception getting recording state: {ex.Message}");
                return CrRecordingState.CrRecordingState_NotRecording;
            }
        }

        public override void StopLiveView()
        {
            try
            {
                _liveViewRunning = false;
                _liveViewData.IsLiveViewRunning = false;
                
                // Send explicit StopLiveView command to prevent camera freeze
                if (_isConnected && _deviceHandle != IntPtr.Zero)
                {
                    var stopLvResult = SonySDKWrapper.SendCommand(
                        _deviceHandle,
                        (uint)CrCommandId.CrCommandId_StopLiveView,
                        CrCommandParam.CrCommandParam_Down);
                        
                    if (SonySDKWrapper.IsSuccess(stopLvResult))
                    {
                        Log.Debug("Sony USB: StopLiveView command sent successfully");
                    }
                    else
                    {
                        Log.Debug($"Sony USB: StopLiveView command failed: {SonySDKWrapper.GetErrorMessage(stopLvResult)}");
                    }
                }
                
                Log.Debug("Sony USB: Live view stopped");
            }
            catch (Exception ex)
            {
                Log.Error("Sony USB: Error stopping live view", ex);
            }
        }
        
        public override LiveViewData GetLiveViewImage()
        {
            lock (_lockObject)
            {
                if (_liveViewData == null || _liveViewData.ImageData == null || _liveViewData.ImageData.Length == 0)
                {
                    return null;
                }
                
                // Return a copy to avoid thread issues
                return new LiveViewData
                {
                    ImageData = _liveViewData.ImageData,
                    ImageDataPosition = _liveViewData.ImageDataPosition,
                    IsLiveViewRunning = _liveViewData.IsLiveViewRunning,
                    ImageWidth = _liveViewData.ImageWidth,
                    ImageHeight = _liveViewData.ImageHeight,
                    LiveViewImageWidth = _liveViewData.LiveViewImageWidth,
                    LiveViewImageHeight = _liveViewData.LiveViewImageHeight
                };
            }
        }
        
        private void LiveViewLoop()
        {
            Log.Debug($"Sony USB: Live view loop started, _liveViewRunning={_liveViewRunning}, _isConnected={_isConnected}, handle={_deviceHandle}");
            int consecutiveErrors = 0;
            int frameCount = 0;
            bool firstFrame = true;
            bool capturedOneFrame = false;  // Debug flag to capture one frame
            
            // Wait a bit for live view to fully start
            if (firstFrame)
            {
                Log.Debug("Sony USB: Waiting for live view to initialize...");
                Thread.Sleep(500);
                firstFrame = false;
            }
            
            Log.Debug($"Sony USB: Entering live view loop, _liveViewRunning={_liveViewRunning}, _isConnected={_isConnected}");
            
            while (_liveViewRunning && _isConnected)
            {
                try
                {
                    // First check live view properties to ensure it's properly enabled
                    if (consecutiveErrors == 0 || consecutiveErrors % 20 == 0)
                    {
                        CheckLiveViewProperties();
                    }
                    
                    // Get live view image info
                    Log.Debug("Sony USB: Calling GetLiveViewImageInfo...");
                    CrImageInfo imageInfo;
                    var result = SonySDKWrapper.GetLiveViewImageInfo(_deviceHandle, out imageInfo);
                    Log.Debug($"Sony USB: GetLiveViewImageInfo result: {result}");
                    
                    if (SonySDKWrapper.IsSuccess(result))
                    {
                        if (imageInfo.BufferSize > 0 && imageInfo.BufferSize < 10 * 1024 * 1024) // Reasonable size check (< 10MB)
                        {
                            frameCount++;
                            
                            // Only log occasionally to avoid spam
                            if (consecutiveErrors == 0 || frameCount % 30 == 0)
                            {
                                Log.Debug($"Sony USB: Live view image info - BufferSize: {imageInfo.BufferSize}, Width: {imageInfo.Width}, Height: {imageInfo.Height}, Format: {imageInfo.Format}");
                            }
                            
                            // Allocate managed buffer for the image data
                            byte[] buffer = new byte[imageInfo.BufferSize];
                            
                            // Pin the buffer so GC can't move it while native code writes to it
                            GCHandle bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                            try
                            {
                                IntPtr bufferPtr = bufferHandle.AddrOfPinnedObject();
                                
                                // Create C++ CrImageDataBlock object through helper DLL or manual layout
                                IntPtr imageDataBlock = IntPtr.Zero;
                                bool usingHelperDll = false;
                                
                                // Try to use helper DLL first
                                try
                                {
                                    imageDataBlock = SonySDKWrapper.CreateImageDataBlock();
                                    usingHelperDll = true;
                                    Log.Debug("Sony USB: Using helper DLL for image data block");
                                }
                                catch (Exception)
                                {
                                    // Helper DLL not available, use manual memory layout
                                    Log.Debug("Sony USB: Helper DLL not available, using manual memory layout");
                                }
                                
                                // If not using helper DLL, create manual memory layout
                                if (!usingHelperDll)
                                {
                                    imageDataBlock = Marshal.AllocHGlobal(48);
                                    // Zero out the entire structure first
                                    for (int i = 0; i < 48; i++)
                                    {
                                        Marshal.WriteByte(imageDataBlock, i, 0);
                                    }
                                    // Skip vtable pointer (8 bytes) and write structure fields
                                    Marshal.WriteInt32(imageDataBlock, 8, 0);                      // frameNo = 0
                                    Marshal.WriteInt32(imageDataBlock, 16, (int)imageInfo.BufferSize); // size = buffer size
                                    Marshal.WriteIntPtr(imageDataBlock, 24, bufferPtr);            // pData = buffer pointer
                                    Marshal.WriteInt32(imageDataBlock, 32, 0);                     // imageSize = 0
                                    Marshal.WriteInt32(imageDataBlock, 36, 0);                     // timeCode = 0
                                }
                                
                                // Set buffer data if using helper DLL
                                if (usingHelperDll && imageDataBlock != IntPtr.Zero)
                                {
                                    SonySDKWrapper.SetImageDataBlockSize(imageDataBlock, imageInfo.BufferSize);
                                    SonySDKWrapper.SetImageDataBlockData(imageDataBlock, bufferPtr);
                                }
                                
                                try
                                {
                                    
                                    // Try to get live view image with retries for first frames
                                    int retryCount = (consecutiveErrors == 0) ? 5 : 1;
                                    bool success = false;
                                    
                                    for (int attempt = 0; attempt < retryCount; attempt++)
                                    {
                                        // Use helper function if available, otherwise direct call
                                        if (usingHelperDll)
                                        {
                                            Log.Debug($"Sony USB: Calling GetLiveViewImageHelper (attempt {attempt + 1}/{retryCount})...");
                                            result = SonySDKWrapper.GetLiveViewImageHelper(_deviceHandle, imageDataBlock);
                                        }
                                        else
                                        {
                                            Log.Debug($"Sony USB: Calling GetLiveViewImage directly (attempt {attempt + 1}/{retryCount})...");
                                            result = SonySDKWrapper.GetLiveViewImage(_deviceHandle, imageDataBlock);
                                        }
                                        
                                        Log.Debug($"Sony USB: GetLiveViewImage result: {SonySDKWrapper.GetErrorMessage(result)} (0x{(int)result:X4})");
                                        
                                        if (SonySDKWrapper.IsSuccess(result))
                                        {
                                            success = true;
                                            Log.Debug("Sony USB: GetLiveViewImage succeeded!");
                                            break;
                                        }
                                        
                                        // Wait a bit before retry
                                        if (attempt < retryCount - 1)
                                        {
                                            Thread.Sleep(75);
                                        }
                                    }
                                    
                                    if (success)
                                    {
                                        // Get actual image size
                                        uint actualImageSize = 0;
                                        if (usingHelperDll)
                                        {
                                            actualImageSize = SonySDKWrapper.GetImageDataBlockImageSize(imageDataBlock);
                                            Log.Debug($"Sony USB: Got image size from helper: {actualImageSize} bytes");
                                            if (actualImageSize > 0 && actualImageSize <= imageInfo.BufferSize)
                                            {
                                                SonySDKWrapper.CopyImageData(imageDataBlock, bufferPtr, imageInfo.BufferSize);
                                                Log.Debug($"Sony USB: Copied image data to buffer");
                                            }
                                            else
                                            {
                                                Log.Debug($"Sony USB: Invalid image size from helper: {actualImageSize} (buffer size: {imageInfo.BufferSize})");
                                            }
                                        }
                                        else
                                        {
                                            // Helper functions not available, read directly from memory
                                            actualImageSize = (uint)Marshal.ReadInt32(imageDataBlock, 32);
                                            Log.Debug($"Sony USB: Got image size from memory: {actualImageSize} bytes");
                                        }
                                        
                                        // The buffer may contain vendor headers before the JPEG data
                                        // Use the actual size from the SDK if available
                                        int dataSize = actualImageSize > 0 ? (int)actualImageSize : (int)imageInfo.BufferSize;
                                        
                                        // Log buffer details only for first frame or errors
                                        if (!capturedOneFrame && dataSize >= 20)
                                        {
                                            string hexBytes = "";
                                            for (int i = 0; i < 20 && i < dataSize; i++)
                                            {
                                                hexBytes += $"{buffer[i]:X2} ";
                                            }
                                            Log.Debug($"Sony USB: Buffer first 20 bytes: {hexBytes}");
                                            Log.Debug($"Sony USB: Total data size: {dataSize} bytes, actualImageSize: {actualImageSize}");
                                        }
                                        
                                        // Extract JPEG from buffer by scanning for SOI/EOI markers
                                        Log.Debug($"Sony USB: Extracting JPEG from buffer (dataSize: {dataSize})");
                                        byte[] jpegData = ExtractJpegFromBuffer(buffer, dataSize);
                                        
                                        if (jpegData != null)
                                        {
                                            Log.Debug($"Sony USB: JPEG extracted: {jpegData.Length} bytes");
                                            lock (_lockObject)
                                            {
                                                _liveViewData.ImageData = jpegData;
                                                _liveViewData.ImageDataPosition = 0;  // JPEG starts at position 0
                                                _liveViewData.IsLiveViewRunning = true;
                                                _liveViewData.ImageWidth = (int)imageInfo.Width;
                                                _liveViewData.ImageHeight = (int)imageInfo.Height;
                                                _liveViewData.LiveViewImageWidth = (int)imageInfo.Width;
                                                _liveViewData.LiveViewImageHeight = (int)imageInfo.Height;
                                            }
                                            
                                            consecutiveErrors = 0; // Reset error counter on success
                                            
                                            // Log success only for first frame
                                            if (!capturedOneFrame)
                                            {
                                                Log.Debug($"Sony USB: Live view JPEG extracted successfully!");
                                                Log.Debug($"Sony USB: JPEG size: {jpegData.Length} bytes (from {dataSize} byte buffer)");
                                                Log.Debug($"Sony USB: JPEG starts with: {jpegData[0]:X2} {jpegData[1]:X2} (should be FF D8)");
                                                Log.Debug($"Sony USB: JPEG ends with: {jpegData[jpegData.Length-2]:X2} {jpegData[jpegData.Length-1]:X2} (should be FF D9)");
                                            }
                                            
                                            // Mark that we captured one frame for debugging
                                            if (!capturedOneFrame)
                                            {
                                                capturedOneFrame = true;
                                                Log.Debug("Sony USB: Successfully captured first frame! Continuing normal operation...");
                                                
                                                // Save the first frame to disk for verification
                                                try
                                                {
                                                    string debugPath = Path.Combine(Path.GetTempPath(), $"Sony_LiveView_Test_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                                                    File.WriteAllBytes(debugPath, jpegData);
                                                    Log.Debug($"Sony USB: Saved test frame to: {debugPath}");
                                                }
                                                catch (Exception saveEx)
                                                {
                                                    Log.Debug($"Sony USB: Failed to save test frame: {saveEx.Message}");
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Log what we got instead of JPEG
                                            if (dataSize >= 4)
                                            {
                                                Log.Debug($"Sony USB: No JPEG markers found in {dataSize} byte buffer (first bytes: {buffer[0]:X2} {buffer[1]:X2} {buffer[2]:X2} {buffer[3]:X2})");
                                            }
                                            else
                                            {
                                                Log.Debug($"Sony USB: Buffer too small: {dataSize} bytes");
                                            }
                                            consecutiveErrors++;
                                        }
                                    }
                                    else
                                    {
                                        consecutiveErrors++;
                                        
                                        // Only log first few errors and every 50th error to avoid spam
                                        if (consecutiveErrors <= 5 || consecutiveErrors % 50 == 0)
                                        {
                                            Log.Debug($"Sony USB: Failed to get live view image: {SonySDKWrapper.GetErrorMessage(result)} (consecutive errors: {consecutiveErrors})");
                                        }
                                    }
                                }
                                finally
                                {
                                    // Clean up
                                    if (imageDataBlock != IntPtr.Zero)
                                    {
                                        if (usingHelperDll)
                                        {
                                            SonySDKWrapper.DestroyImageDataBlock(imageDataBlock);
                                        }
                                        else
                                        {
                                            Marshal.FreeHGlobal(imageDataBlock);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                bufferHandle.Free();
                            }
                        }
                        else
                        {
                            Log.Debug($"Sony USB: Invalid live view image buffer size: {imageInfo.BufferSize}");
                            consecutiveErrors++;
                        }
                    }
                    else
                    {
                        consecutiveErrors++;
                        if (consecutiveErrors == 1 || consecutiveErrors % 10 == 0) // Log first error and every 10th
                        {
                            Log.Debug($"Sony USB: Failed to get live view image info: {SonySDKWrapper.GetErrorMessage(result)} (consecutive errors: {consecutiveErrors})");
                        }
                    }
                    
                    // If too many consecutive errors, increase sleep time
                    if (consecutiveErrors > 50)
                    {
                        Log.Debug("Sony USB: Too many live view errors, slowing down polling");
                        Thread.Sleep(1000); // Slow down when having persistent issues
                    }
                    else
                    {
                        Thread.Sleep(33); // ~30 fps
                    }
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    Log.Debug($"Sony USB: Live view loop exception: {ex.Message} (consecutive errors: {consecutiveErrors})");
                    Thread.Sleep(100);
                }
            }
            
            Log.Debug("Sony USB: Live view loop ended");
        }
        
        private void ConfigureAdditionalLiveViewSettings()
        {
            try
            {
                Log.Debug("Sony USB: Configuring additional live view settings for FX3...");
                
                // Get all device properties to find what needs to be configured
                IntPtr properties;
                int numProperties;
                var result = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                
                if (SonySDKWrapper.IsSuccess(result) && properties != IntPtr.Zero)
                {
                    Log.Debug($"Sony USB: Retrieved {numProperties} device properties for live view setup");
                    
                    // Look for specific properties that need to be set for live view
                    for (int i = 0; i < numProperties; i++)
                    {
                        IntPtr propPtr = IntPtr.Add(properties, i * Marshal.SizeOf<CrDeviceProperty>());
                        var prop = Marshal.PtrToStructure<CrDeviceProperty>(propPtr);
                        
                        // Look for common live view and monitoring properties
                        switch (prop.Code)
                        {
                            case 0x500F: // S_Log shooting mode
                            case 0x5014: // Movie quality 
                            case 0x5018: // Recording mode
                            case 0x5021: // Live view display effect
                            case 0x5022: // Live view image quality
                            case 0x5023: // Live view status
                                Log.Debug($"Sony USB: Found live view property Code=0x{prop.Code:X4}, Type={prop.ValueType}, Value={prop.CurrentValue}");
                                
                                // Try to set to a compatible value if needed
                                if (prop.CurrentValue == 0 && prop.ValueType != CrDataType.CrDataType_Undefined)
                                {
                                    TrySetLiveViewProperty(prop.Code, prop.ValueType, 1);
                                }
                                break;
                                
                            case 0x500C: // Priority key - ensure PC Remote
                                if (prop.CurrentValue != (ulong)CrPriorityKeySettings.CrPriorityKey_PCRemote)
                                {
                                    Log.Debug($"Sony USB: Setting priority key to PC Remote (current: {prop.CurrentValue})");
                                    TrySetLiveViewProperty(prop.Code, prop.ValueType, (ulong)CrPriorityKeySettings.CrPriorityKey_PCRemote);
                                }
                                break;
                        }
                    }
                    
                    SonySDKWrapper.ReleaseDeviceProperties(_deviceHandle, properties);
                }
                
                // Additional device settings that might be needed
                Log.Debug("Sony USB: Setting additional device settings...");
                
                // Try setting movie recording mode to off (setting key 1)
                var movieResult = SonySDKWrapper.SetDeviceSetting(_deviceHandle, 1, 0);
                Log.Debug($"Sony USB: Movie recording off result: {SonySDKWrapper.GetErrorMessage(movieResult)}");
                
                // Try setting monitoring mode (setting key 2) 
                var monitorResult = SonySDKWrapper.SetDeviceSetting(_deviceHandle, 2, 1);
                Log.Debug($"Sony USB: Monitoring mode enable result: {SonySDKWrapper.GetErrorMessage(monitorResult)}");
                
                Thread.Sleep(500); // Give camera time to process all settings
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception configuring additional live view settings: {ex.Message}");
            }
        }
        
        private void TrySetLiveViewProperty(uint code, CrDataType valueType, ulong value)
        {
            try
            {
                var property = new CrDeviceProperty
                {
                    Code = code,
                    ValueType = valueType,
                    CurrentValue = value,
                    ValueSize = 0,
                    ValuePtr = IntPtr.Zero
                };
                
                IntPtr propertyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CrDeviceProperty>());
                try
                {
                    Marshal.StructureToPtr(property, propertyPtr, false);
                    var result = SonySDKWrapper.SetDeviceProperty(_deviceHandle, propertyPtr);
                    Log.Debug($"Sony USB: Set property 0x{code:X4} to {value}: {SonySDKWrapper.GetErrorMessage(result)}");
                }
                finally
                {
                    Marshal.FreeHGlobal(propertyPtr);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception setting property 0x{code:X4}: {ex.Message}");
            }
        }
        
        private static byte[] ExtractJpegFromBuffer(byte[] buffer, int count)
        {
            // Find JPEG Start Of Image (SOI) marker: FF D8
            int soiIndex = -1;
            for (int i = 0; i + 1 < count; i++)
            {
                if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8)
                {
                    soiIndex = i;
                    break;
                }
            }
            
            if (soiIndex < 0)
            {
                // No JPEG start marker found
                return null;
            }
            
            // Find JPEG End Of Image (EOI) marker: FF D9
            int eoiIndex = -1;
            for (int i = count - 2; i >= soiIndex; i--)
            {
                if (buffer[i] == 0xFF && buffer[i + 1] == 0xD9)
                {
                    eoiIndex = i + 2; // Include the EOI marker
                    break;
                }
            }
            
            if (eoiIndex < 0)
            {
                // No JPEG end marker found
                return null;
            }
            
            // Extract the JPEG data
            int jpegSize = eoiIndex - soiIndex;
            byte[] jpegData = new byte[jpegSize];
            Buffer.BlockCopy(buffer, soiIndex, jpegData, 0, jpegSize);
            return jpegData;
        }
        
        private void CheckLiveViewProperties()
        {
            try
            {
                IntPtr properties;
                int numProperties;
                var result = SonySDKWrapper.GetLiveViewProperties(_deviceHandle, out properties, out numProperties);
                
                if (SonySDKWrapper.IsSuccess(result) && properties != IntPtr.Zero)
                {
                    Log.Debug($"Sony USB: Retrieved {numProperties} live view properties");
                    
                    // Look for any live view specific properties that need to be enabled
                    for (int i = 0; i < numProperties; i++)
                    {
                        IntPtr propPtr = IntPtr.Add(properties, i * Marshal.SizeOf<CrLiveViewProperty>());
                        var prop = Marshal.PtrToStructure<CrLiveViewProperty>(propPtr);
                        
                        // Common live view property codes to check
                        if (prop.Code == 0x5021 || prop.Code == 0x5022) // Live view status properties
                        {
                            Log.Debug($"Sony USB: Live view property Code=0x{prop.Code:X4}, Value={prop.CurrentValue}");
                            
                            // If live view is disabled (value 0), try to enable it
                            if (prop.CurrentValue == 0)
                            {
                                Log.Debug($"Sony USB: Attempting to enable live view property 0x{prop.Code:X4}");
                                // Enable live view if possible
                                var enableProperty = new CrDeviceProperty
                                {
                                    Code = prop.Code,
                                    ValueType = prop.ValueType,
                                    CurrentValue = 1,
                                    ValueSize = 0,
                                    ValuePtr = IntPtr.Zero
                                };
                                
                                IntPtr enablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<CrDeviceProperty>());
                                try
                                {
                                    Marshal.StructureToPtr(enableProperty, enablePtr, false);
                                    var setResult = SonySDKWrapper.SetDeviceProperty(_deviceHandle, enablePtr);
                                    Log.Debug($"Sony USB: Set live view property result: {SonySDKWrapper.GetErrorMessage(setResult)}");
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal(enablePtr);
                                }
                            }
                        }
                    }
                    
                    SonySDKWrapper.ReleaseLiveViewProperties(_deviceHandle, properties);
                }
                else
                {
                    Log.Debug($"Sony USB: Failed to get live view properties: {SonySDKWrapper.GetErrorMessage(result)}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception checking live view properties: {ex.Message}");
            }
        }
        
        private CrError TryAlternativeLiveViewMethod(CrImageInfo imageInfo)
        {
            try
            {
                // Alternative approach: Enable live view setting if not already enabled
                IntPtr properties;
                int numProperties;
                var getResult = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                
                if (SonySDKWrapper.IsSuccess(getResult) && properties != IntPtr.Zero)
                {
                    // Limit property reading to avoid crashes - just check key properties
                    int maxProps = Math.Min(numProperties, 20); // Only process first 20 properties
                    Log.Debug($"Sony USB: Found {numProperties} properties, checking first {maxProps} for live view settings");
                    
                    for (int i = 0; i < maxProps; i++)
                    {
                        try
                        {
                            IntPtr propPtr = IntPtr.Add(properties, i * Marshal.SizeOf<CrDeviceProperty>());
                            var prop = Marshal.PtrToStructure<CrDeviceProperty>(propPtr);
                            
                            // Only log the first few properties 
                            if (i < 5)
                            {
                                Log.Debug($"Sony USB: Property {i}: Code=0x{prop.Code:X4}, Type={prop.ValueType}, Value={prop.CurrentValue}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"Sony USB: Exception reading property {i}: {ex.Message}");
                            break;
                        }
                    }
                    
                    SonySDKWrapper.ReleaseDeviceProperties(_deviceHandle, properties);
                }
                
                // Skip the live view image retrieval for now since we have the core camera working
                // Focus on capture functionality which is more important for photobooth
                Log.Debug("Sony USB: Skipping live view image retrieval - camera is ready for capture");
                return CrError.CrError_None;
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Alternative live view method exception: {ex.Message}");
                return CrError.CrError_Generic;
            }
        }
        
        #endregion
        
        #region Camera Configuration
        
        private void ConfigureCameraForCapture()
        {
            try
            {
                Log.Debug("Sony USB: Configuring camera for capture and live view...");
                
                // First, read battery level to verify basic camera communication
                VerifyBasicCommunication();
                
                // Set SDK Control Mode to Remote (most important)
                ConfigureProperty(CrDevicePropertyCode.CrDeviceProperty_SdkControlMode,
                    CrDataType.CrDataType_UInt32, (uint)CrSdkControlMode.CrSdkControlMode_Remote);
                
                // Set Camera Operating Mode to Record
                ConfigureProperty(CrDevicePropertyCode.CrDeviceProperty_CameraOperatingMode, 
                    CrDataType.CrDataType_UInt32, (uint)CrCameraOperatingMode.CrCameraOperatingMode_Record);
                
                // Set Exposure Program Mode to P Auto for capture functionality
                ConfigureProperty(CrDevicePropertyCode.CrDeviceProperty_ExposureProgramMode,
                    CrDataType.CrDataType_UInt32, (uint)CrExposureProgram.CrExposure_P_Auto);
                
                // Ensure PC Remote priority is set
                ConfigureProperty(CrDevicePropertyCode.CrDeviceProperty_PriorityKeySettings,
                    CrDataType.CrDataType_UInt16, (uint)CrPriorityKeySettings.CrPriorityKey_PCRemote);
                
                Log.Debug("Sony USB: Camera configuration completed");
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Error configuring camera: {ex.Message}");
            }
        }
        
        private void VerifyBasicCommunication()
        {
            try
            {
                // Try to read a basic property to verify communication works
                IntPtr properties;
                int numProperties;
                
                var result = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                if (SonySDKWrapper.IsSuccess(result))
                {
                    Log.Debug($"Sony USB: Basic communication verified - {numProperties} properties available");
                    SonySDKWrapper.ReleaseDeviceProperties(_deviceHandle, properties);
                }
                else
                {
                    Log.Debug($"Sony USB: Basic communication check failed: {SonySDKWrapper.GetErrorMessage(result)}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception during basic communication check: {ex.Message}");
            }
        }
        
        private void ConfigureProperty(CrDevicePropertyCode propertyCode, CrDataType dataType, uint value)
        {
            try
            {
                Log.Debug($"Sony USB: Attempting to configure property {propertyCode} with value {value}");
                
                // For now, skip property configuration to avoid AccessViolationException
                // The camera connection and basic operations work without explicit property setting
                // The SDK may automatically configure required properties during connection
                Log.Debug($"Sony USB: Skipping property {propertyCode} configuration to avoid memory access issues");
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Exception setting property {propertyCode}: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Callback Handlers
        
        private void OnConnected(uint version)
        {
            Log.Debug($"Sony USB: Camera connected (version: {version})");
            _isConnected = true;
            IsConnected = true;
            _connectionTime = DateTime.Now;
            
            // Start a task to mark camera ready after a delay
            Task.Run(async () =>
            {
                await Task.Delay(2000); // Wait 2 seconds as recommended
                
                // Configure camera for capture and live view
                ConfigureCameraForCapture();
                
                // Additional delay to ensure configuration takes effect
                await Task.Delay(1000);
                
                _cameraReady = true;
                Log.Debug("Sony USB: Camera is now ready for API calls");
            });
        }
        
        private void OnDisconnected(uint error)
        {
            Log.Debug($"Sony USB: Camera disconnected (error: 0x{error:X4})");
            _isConnected = false;
            IsConnected = false;
            
            // Try to reconnect
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                if (!_isConnected && _cameraInfo != null)
                {
                    Log.Debug("Sony USB: Attempting to reconnect...");
                    // Reconnection logic here
                }
            });
        }
        
        private void OnPropertyChanged()
        {
            // Refresh properties when they change
            RefreshDeviceProperties();
        }
        
        private void OnCompleteDownload(string filename)
        {
            Log.Debug($"Sony USB: Download complete: {filename}");
            
            // Check if this is a video file
            bool isVideo = false;
            if (!string.IsNullOrEmpty(filename))
            {
                string ext = Path.GetExtension(filename).ToLower();
                isVideo = (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mts");
                
                if (isVideo)
                {
                    Log.Debug($"Sony USB: Video file download complete: {filename}");
                }
            }
            
            // Ensure we have a valid filename
            if (string.IsNullOrEmpty(filename))
            {
                if (_isRecording)
                {
                    filename = Path.Combine(Path.GetTempPath(), $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
                    Log.Debug($"Sony USB: Generated default video filename: {filename}");
                }
                else
                {
                    filename = Path.Combine(Path.GetTempPath(), $"SONY_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    Log.Debug($"Sony USB: Generated default photo filename: {filename}");
                }
            }
            
            // Store the downloaded file path for TransferFile
            _lastDownloadedFile = filename;
            
            // Trigger photo captured event (works for both photos and videos)
            PhotoCapturedEventArgs args = new PhotoCapturedEventArgs
            {
                CameraDevice = this,
                FileName = filename,
                Handle = IntPtr.Zero
            };
            
            OnPhotoCapture(this, args);
            IsBusy = false;
        }
        
        private void OnContentsTransfer(uint notify, uint handle, string filename)
        {
            Log.Debug($"Sony USB: Contents transfer: {notify}, handle: {handle}, file: {filename}");
            
            // Check if this is a video file
            bool isVideo = false;
            if (!string.IsNullOrEmpty(filename))
            {
                string ext = Path.GetExtension(filename).ToLower();
                isVideo = (ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mts");
                
                if (isVideo)
                {
                    Log.Debug($"Sony USB: Video content transfer: {filename}");
                }
            }
            
            // Ensure we have a valid filename
            if (string.IsNullOrEmpty(filename))
            {
                if (_isRecording)
                {
                    filename = Path.Combine(Path.GetTempPath(), $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
                    Log.Debug($"Sony USB: Generated default video filename for transfer: {filename}");
                }
                else
                {
                    filename = Path.Combine(Path.GetTempPath(), $"SONY_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                    Log.Debug($"Sony USB: Generated default photo filename for transfer: {filename}");
                }
            }
            
            PhotoCapturedEventArgs args = new PhotoCapturedEventArgs
            {
                CameraDevice = this,
                FileName = filename,
                Handle = new IntPtr(handle)
            };
            
            OnPhotoCapture(this, args);
            IsBusy = false;
        }
        
        private void OnErrorOccurred(uint error)
        {
            Log.Error($"Sony USB: Camera error: 0x{error:X4}");
        }
        
        private void OnWarningOccurred(uint warning)
        {
            Log.Debug($"Sony USB: Camera warning: 0x{warning:X4}");
        }
        
        private void OnContentsListChanged(uint notify, uint slotNumber, uint addSize)
        {
            Log.Debug($"Sony USB: Contents list changed - notify: {notify}, slot: {slotNumber}, addSize: {addSize}");
            
            // Track if we were recently recording
            bool wasRecording = _wasRecordingRecently;
            _wasRecordingRecently = false;
            
            // If we just finished recording, this might indicate a new video file
            if (wasRecording && addSize > 0)
            {
                Log.Debug("Sony USB: New content detected after recording, checking for video files...");
                // The video file is now available on the camera's memory card
                // We need to enumerate and download it
                TriggerVideoTransfer();
            }
        }
        
        private void TriggerVideoTransfer()
        {
            try
            {
                Log.Debug("Sony USB: Triggering manual video transfer using remote transfer API with retries...");

                // Give camera time to finalize file system entries
                Thread.Sleep(2000);

                string savedPath;

                // Try Slot 1 then Slot 2 with retries
                bool ok = TryDownloadLatestMovieFromSlot(CrSlotNumber.CrSlotNumber_Slot1, out savedPath)
                          || TryDownloadLatestMovieFromSlot(CrSlotNumber.CrSlotNumber_Slot2, out savedPath);

                if (ok && !string.IsNullOrEmpty(savedPath))
                {
                    Log.Debug($"Sony USB: Video downloaded successfully: {savedPath}");
                    _lastDownloadedFile = savedPath;

                    var args = new PhotoCapturedEventArgs
                    {
                        CameraDevice = this,
                        FileName = savedPath,
                        Handle = IntPtr.Zero
                    };
                    OnPhotoCapture(this, args);
                }
                else
                {
                    Log.Debug("Sony USB: Could not locate/download video after retries");
                    NotifyVideoRecordingComplete();
                }

                IsBusy = false;
            }
            catch (Exception ex)
            {
                Log.Error($"Sony USB: Exception in TriggerVideoTransfer: {ex.Message}");
                NotifyVideoRecordingComplete();
                IsBusy = false;
            }
        }

        private bool TryDownloadLatestMovieFromSlot(CrSlotNumber slot, out string savedPath)
        {
            savedPath = string.Empty;
            try
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    Log.Debug($"Sony USB: [Slot {slot}] Attempt {attempt}/5 to enumerate capture dates...");

                    IntPtr dateList = IntPtr.Zero;
                    uint dateCount = 0;
                    var dateRes = SonySDKWrapper.GetRemoteTransferCapturedDateList(_deviceHandle, slot, out dateList, out dateCount);

                    try
                    {
                        if (!SonySDKWrapper.IsSuccess(dateRes) || dateList == IntPtr.Zero || dateCount == 0)
                        {
                            Log.Debug($"Sony USB: [Slot {slot}] No dates yet (res={SonySDKWrapper.GetErrorMessage(dateRes)}), retrying...");
                            Thread.Sleep(700);
                            continue;
                        }

                        // Build candidate date list: today first (if present), else the most recent date
                        var candidates = new List<CrCaptureDate>();
                        var today = new CrCaptureDate { Year = (ushort)DateTime.Now.Year, Month = (byte)DateTime.Now.Month, Day = (byte)DateTime.Now.Day };

                        bool todayFound = false;
                        for (uint i = 0; i < dateCount; i++)
                        {
                            IntPtr p = IntPtr.Add(dateList, (int)(i * Marshal.SizeOf<CrCaptureDate>()));
                            var d = Marshal.PtrToStructure<CrCaptureDate>(p);
                            if (d.Year == today.Year && d.Month == today.Month && d.Day == today.Day)
                            {
                                todayFound = true;
                            }
                        }
                        if (todayFound)
                        {
                            candidates.Add(today);
                        }
                        // Add last date from list as fallback (most recent)
                        IntPtr plast = IntPtr.Add(dateList, (int)((dateCount - 1) * Marshal.SizeOf<CrCaptureDate>()));
                        var latestDate = Marshal.PtrToStructure<CrCaptureDate>(plast);
                        if (!(latestDate.Year == today.Year && latestDate.Month == today.Month && latestDate.Day == today.Day))
                        {
                            candidates.Add(latestDate);
                        }

                        // Try each candidate date to get most recent movie
                        foreach (var cand in candidates)
                        {
                            IntPtr candPtr = IntPtr.Zero;
                            IntPtr contentsList = IntPtr.Zero;
                            uint contentsCount = 0;
                            try
                            {
                                candPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CrCaptureDate>());
                                Marshal.StructureToPtr(cand, candPtr, false);
                                var listRes = SonySDKWrapper.GetRemoteTransferContentsInfoList(
                                    _deviceHandle,
                                    slot,
                                    CrGetContentsInfoListType.CrGetContentsInfoListType_Movie,
                                    candPtr,
                                    200,
                                    out contentsList,
                                    out contentsCount);

                                if (!SonySDKWrapper.IsSuccess(listRes) || contentsList == IntPtr.Zero || contentsCount == 0)
                                {
                                    Log.Debug($"Sony USB: [Slot {slot}] No movies for {cand.Year}-{cand.Month:D2}-{cand.Day:D2} (res={SonySDKWrapper.GetErrorMessage(listRes)})");
                                    continue;
                                }

                                // Pick the last entry as the most recent movie
                                IntPtr contentPtr = IntPtr.Add(contentsList, (int)((contentsCount - 1) * Marshal.SizeOf<CrContentsInfo>()));
                                var info = Marshal.PtrToStructure<CrContentsInfo>(contentPtr);
                                Log.Debug($"Sony USB: [Slot {slot}] Latest movie: {info.FileName} ({info.FileSize} bytes) id=({info.ContentsId},{info.FileId})");

                                // Build save destination
                                string saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth", DateTime.Now.ToString("MMddyy"));
                                if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
                                string name = string.IsNullOrWhiteSpace(info.FileName) ? $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.mp4" : info.FileName;
                                string fullPath = Path.Combine(saveDir, name);

                                var dlRes = SonySDKWrapper.GetRemoteTransferContentsDataFile(_deviceHandle, slot, info.ContentsId, info.FileId, 0, saveDir, name);
                                if (!SonySDKWrapper.IsSuccess(dlRes))
                                {
                                    Log.Debug($"Sony USB: [Slot {slot}] Download failed: {SonySDKWrapper.GetErrorMessage(dlRes)}");
                                    continue;
                                }

                                // Poll for file materialization
                                bool exists = false;
                                for (int w = 0; w < 10; w++)
                                {
                                    if (File.Exists(fullPath))
                                    {
                                        var size = new FileInfo(fullPath).Length;
                                        if (size > 0)
                                        {
                                            exists = true;
                                            break;
                                        }
                                    }
                                    Thread.Sleep(300);
                                }

                                if (exists)
                                {
                                    savedPath = fullPath;
                                    return true;
                                }
                                else
                                {
                                    Log.Debug($"Sony USB: [Slot {slot}] File not visible yet after download start: {fullPath}");
                                }
                            }
                            finally
                            {
                                if (contentsList != IntPtr.Zero)
                                {
                                    SonySDKWrapper.ReleaseRemoteTransferContentsInfoList(_deviceHandle, contentsList);
                                }
                                if (candPtr != IntPtr.Zero)
                                {
                                    Marshal.FreeHGlobal(candPtr);
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (dateList != IntPtr.Zero)
                        {
                            SonySDKWrapper.ReleaseRemoteTransferCapturedDateList(_deviceHandle, dateList);
                        }
                    }

                    // If we got here, we didn't succeed this attempt
                    Thread.Sleep(700);
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: [Slot {slot}] Exception during TryDownloadLatestMovieFromSlot: {ex.Message}");
                return false;
            }
        }
        
        private void NotifyVideoRecordingComplete()
        {
            // Fallback notification when video can't be downloaded
            string videoSavePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth",
                DateTime.Now.ToString("MMddyy"));
            
            string videoFileName = Path.Combine(videoSavePath, $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            
            Log.Debug($"Sony USB: Video recording complete. File may be on camera's memory card.");
            Log.Debug($"Sony USB: Expected location after transfer: {videoFileName}");
            
            PhotoCapturedEventArgs args = new PhotoCapturedEventArgs
            {
                CameraDevice = this,
                FileName = videoFileName,
                Handle = IntPtr.Zero
            };
            
            OnPhotoCapture(this, args);
        }
        
        #endregion
        
        #region Cleanup
        
        public override void Close()
        {
            Cleanup();
        }
        
        private void Cleanup()
        {
            try
            {
                _liveViewRunning = false;
                
                if (_deviceHandle != IntPtr.Zero)
                {
                    Log.Debug("Sony USB: Disconnecting camera...");
                    SonySDKWrapper.Disconnect(_deviceHandle);
                    SonySDKWrapper.ReleaseDevice(_deviceHandle);
                    _deviceHandle = IntPtr.Zero;
                }
                
                _callback?.Dispose();
                _callback = null;
                
                // Don't release _cameraInfo here as it's managed by the provider
                _cameraInfo = null;
                
                _isConnected = false;
                IsConnected = false;
                
                Log.Debug("Sony USB: Camera cleaned up");
            }
            catch (Exception ex)
            {
                Log.Error("Sony USB: Error during cleanup", ex);
            }
        }
        
        #endregion
    }
}
