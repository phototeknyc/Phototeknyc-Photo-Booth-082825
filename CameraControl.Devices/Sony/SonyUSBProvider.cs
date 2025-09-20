using System;
using System.Collections.Generic;
using System.Management;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CameraControl.Devices.Classes;

namespace CameraControl.Devices.Sony
{
    /// <summary>
    /// Provider for Sony USB cameras using Camera Remote SDK v2.0
    /// </summary>
    public static class SonyUSBProvider
    {
        private static bool _sdkInitialized = false;
        private static readonly object _lockObject = new object();
        public static CrCameraDeviceModel? PreferredDirectModel { get; set; } = null;
        
        /// <summary>
        /// Get Sony USB device serial numbers from system
        /// </summary>
        private static List<string> GetSonyUSBSerialNumbers()
        {
            var serialNumbers = new List<string>();
            Log.Debug("Sony USB: Starting USB device detection...");
            
            try
            {
                Log.Debug("Sony USB: Creating WMI searcher...");
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_054C%'"))
                {
                    Log.Debug("Sony USB: Executing WMI query...");
                    var devices = searcher.Get();
                    Log.Debug($"Sony USB: WMI query returned {devices.Count} devices");
                    
                    foreach (ManagementObject device in devices)
                    {
                        try
                        {
                            var deviceId = device["DeviceID"] as string;
                            var name = device["Name"] as string;
                            
                            Log.Debug($"Sony USB: Processing device: {name} - {deviceId}");
                            
                            // Look for Sony USB devices (VID_054C)
                            if (!string.IsNullOrEmpty(deviceId) && deviceId.Contains("VID_054C"))
                            {
                                Log.Debug($"Sony USB: Found Sony device: {name} - {deviceId}");
                                
                                // Extract serial number from device ID
                                // Format is typically USB\VID_054C&PID_XXXX\SerialNumber
                                var parts = deviceId.Split('\\');
                                Log.Debug($"Sony USB: Device ID parts: {string.Join(" | ", parts)}");
                                
                                if (parts.Length >= 3)
                                {
                                    var serialPart = parts[2];
                                    Log.Debug($"Sony USB: Serial part: '{serialPart}'");
                                    
                                    if (!serialPart.Contains("&") && serialPart.Length > 0)
                                    {
                                        serialNumbers.Add(serialPart);
                                        Log.Debug($"Sony USB: Added serial number: {serialPart}");
                                    }
                                    else
                                    {
                                        Log.Debug($"Sony USB: Skipping serial part (contains & or empty): '{serialPart}'");
                                    }
                                }
                                else
                                {
                                    Log.Debug($"Sony USB: Insufficient device ID parts: {parts.Length}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Sony USB: Error processing individual device: {ex.Message}");
                        }
                    }
                }
                
                Log.Debug($"Sony USB: USB detection completed. Found {serialNumbers.Count} serial numbers total");
            }
            catch (Exception ex)
            {
                Log.Error($"Sony USB: Error detecting USB devices: {ex.Message}", ex);
            }
            
            return serialNumbers;
        }

        /// <summary>
        /// Get list of connected Sony cameras
        /// </summary>
        public static List<DeviceDescriptor> GetConnectedCameras()
        {
            var devices = new List<DeviceDescriptor>();
            
            try
            {
                lock (_lockObject)
                {
                    // Initialize SDK if not already done
                    if (!_sdkInitialized)
                    {
                        Log.Debug("Sony USB: Initializing Sony SDK...");

                        // Attempt to set DLL search directory for Sony SDK resiliency
                        try
                        {
                            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            var crsdkPath = System.IO.Path.Combine(baseDir, "sonysdk", "external", "crsdk");
                            var adapterPath = System.IO.Path.Combine(baseDir, "sonysdk", "external", "crsdk", "CrAdapter");
                            // Only call if folders exist
                            if (Directory.Exists(crsdkPath)) SonySDKWrapper.TrySetDllDirectory(crsdkPath);
                            if (Directory.Exists(adapterPath)) SonySDKWrapper.TrySetDllDirectory(adapterPath);
                            Log.Debug($"Sony USB: SetDllDirectory applied for '{crsdkPath}' and '{adapterPath}' if present");
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"Sony USB: SetDllDirectory failed (non-fatal): {ex.Message}");
                        }
                        
                        // Get SDK version
                        uint version = SonySDKWrapper.GetSDKVersion();
                        int major = (int)((version & 0xFF000000) >> 24);
                        int minor = (int)((version & 0x00FF0000) >> 16);
                        int patch = (int)((version & 0x0000FF00) >> 8);
                        Log.Debug($"Sony SDK version: {major}.{minor}.{patch:D2}");
                        
                        // Try initializing with different log levels
                        bool initResult = SonySDKWrapper.Init(0); // Standard init
                        if (!initResult)
                        {
                            Log.Debug("Sony USB: Standard init failed, trying with logging enabled...");
                            initResult = SonySDKWrapper.Init(1); // Try with logging
                        }
                        
                        if (!initResult)
                        {
                            Log.Error("Sony USB: Failed to initialize Sony SDK");
                            return devices;
                        }
                        
                        _sdkInitialized = true;
                        Log.Debug("Sony USB: SDK initialized successfully");
                        
                        // Add a small delay to ensure SDK is fully ready
                        Thread.Sleep(100);
                    }
                }
                
                // Try direct FX3 camera creation first (bypasses enumeration)
                Log.Debug("Sony USB: Attempting direct camera creation (preferred model first if set)...");
                Log.Debug("Sony USB: DEBUG - About to call GetSonyUSBSerialNumbers()");
                
                // Get Sony USB device serial numbers from system
                var sonySerialNumbers = GetSonyUSBSerialNumbers();
                Log.Debug("Sony USB: DEBUG - GetSonyUSBSerialNumbers() returned");
                Log.Debug($"Sony USB: Found {sonySerialNumbers.Count} Sony USB devices");
                
                // If no Sony devices found, return empty list
                if (sonySerialNumbers.Count == 0)
                {
                    Log.Debug("Sony USB: No Sony devices detected via USB enumeration");
                    return devices; // Return empty list, no Sony cameras connected
                }
                
                Log.Debug($"Sony USB: About to process {sonySerialNumbers.Count} serial numbers");
                foreach (var serialNumber in sonySerialNumbers)
                {
                    Log.Debug($"Sony USB: Trying direct creation with serial: {serialNumber}");
                    IntPtr cameraObjectPtr;
                    
                    try
                    {
                        // Convert serial number to IntPtr for native call
                        // Sony SDK expects UTF-16 (wide characters) on Windows
                        var serialBytes = System.Text.Encoding.Unicode.GetBytes(serialNumber + '\0'); // null terminated
                        var serialPtr = Marshal.AllocHGlobal(serialBytes.Length);
                        Marshal.Copy(serialBytes, 0, serialPtr, serialBytes.Length);
                        
                        try
                        {
                            // Add timeout handling to prevent hanging
                            var task = Task.Run(() =>
                            {
                                IntPtr ptr;
                                CrError createResult;
                                // Try preferred model first if configured
                                if (PreferredDirectModel.HasValue)
                                {
                                    createResult = SonySDKWrapper.CreateCameraObjectInfoUSBConnection(
                                        out ptr,
                                        PreferredDirectModel.Value,
                                        serialPtr);
                                }
                                else
                                {
                                    createResult = SonySDKWrapper.CreateCameraObjectInfoUSBConnection(
                                        out ptr,
                                        CrCameraDeviceModel.CrCameraDeviceModel_ILME_FX3,
                                        serialPtr);
                                }
                                return new { Result = createResult, Pointer = ptr };
                            });
                    
                            if (!task.Wait(5000)) // 5 second timeout
                            {
                                Log.Error($"Sony USB: Direct camera creation timed out for serial: {serialNumber}");
                                continue; // Try next serial number
                            }
                            
                            var taskResult = task.Result;
                            var directResult = taskResult.Result;
                            cameraObjectPtr = taskResult.Pointer;
                            
                            Log.Debug($"Sony USB: Direct creation returned: {SonySDKWrapper.GetErrorMessage(directResult)} (0x{(int)directResult:X4})");
                            Log.Debug($"Sony USB: Camera object pointer: {cameraObjectPtr}");
                            
                            if (SonySDKWrapper.IsSuccess(directResult) && cameraObjectPtr != IntPtr.Zero)
                            {
                                Log.Debug($"Sony USB: Direct FX3 creation successful with serial: {serialNumber}!");
                                
                                // Create device descriptor for the direct camera
                                var descriptor = new DeviceDescriptor
                                {
                                    WpdId = $"Sony\\USB\\0DA3\\{serialNumber}",
                                    SerialNumber = serialNumber,
                                    CameraDevice = null
                                };
                                
                                // Store camera info for later use
                                var cameraInfo = new CrCameraObjectInfo(cameraObjectPtr);
                                descriptor.SetSonyCameraInfo(cameraInfo);
                                // Set model name based on preference if available, else generic
                                if (PreferredDirectModel.HasValue)
                                    descriptor.SetSonyModelName(PreferredDirectModel.Value.ToString());
                                else
                                    descriptor.SetSonyModelName("Sony Camera");
                                
                                devices.Add(descriptor);
                                Log.Debug($"Sony USB: FX3 camera added via direct creation with serial: {serialNumber}");
                                return devices; // Success - return immediately
                            }
                            else
                            {
                                Log.Debug($"Sony USB: Direct creation failed for serial {serialNumber}: {SonySDKWrapper.GetErrorMessage(directResult)}");
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(serialPtr); // Always free allocated memory
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Sony USB: Exception during direct camera creation with serial {serialNumber}: {ex.Message}", ex);
                    }
                }
                
                // Fall back to enumeration if direct creation fails
                Log.Debug("Sony USB: Falling back to enumeration...");
                IntPtr enumPtr;
                var enumResult = SonySDKWrapper.EnumCameraObjects(out enumPtr, 10); // Extended timeout
                
                Log.Error($"Sony USB: Enumeration result: {SonySDKWrapper.GetErrorMessage(enumResult)} (0x{(int)enumResult:X4})");
                Log.Error($"Sony USB: Raw result value: {enumResult}");
                Log.Error($"Sony USB: EnumPtr value: {enumPtr}");
                
                if (!SonySDKWrapper.IsSuccess(enumResult))
                {
                    if (enumResult == CrError.CrError_Adaptor_NoDevice)
                    {
                        Log.Debug("Sony USB: No Sony cameras detected - camera may not be in PC Remote mode");
                        Log.Debug("Sony USB: Please check: 1) Camera is in PC Remote mode, 2) USB cable connected, 3) Camera powered on");
                    }
                    else
                    {
                        Log.Error($"Sony USB: Failed to enumerate cameras: {SonySDKWrapper.GetErrorMessage(enumResult)} (0x{(int)enumResult:X4})");
                        Log.Debug("Sony USB: This may indicate an SDK or driver issue");
                    }
                    return devices;
                }
                
                if (enumPtr == IntPtr.Zero)
                {
                    Log.Debug("Sony USB: Enumeration returned null pointer");
                    return devices;
                }
                
                // Parse enumeration results
                var enumObj = new CrEnumCameraObjectInfo(enumPtr);
                try
                {
                    uint count = enumObj.GetCount();
                    Log.Debug($"Sony USB: Found {count} camera(s)");
                    
                    for (uint i = 0; i < count; i++)
                    {
                        var cameraInfo = enumObj.GetCameraObjectInfo(i);
                        if (cameraInfo != null)
                        {
                            try
                            {
                                string model = cameraInfo.GetModel();
                                string name = cameraInfo.GetName();
                                ushort pid = cameraInfo.GetUsbPid();
                                string connectionType = cameraInfo.GetConnectionTypeName();
                                
                                // Only process USB cameras
                                if (connectionType == "USB" || connectionType == "DIRECT")
                                {
                                    Log.Debug($"Sony USB: Found {model} - {name} (PID: 0x{pid:X4})");
                                    
                                    // Get unique ID
                                    string uniqueId = GetCameraUniqueId(cameraInfo);
                                    
                                    var descriptor = new DeviceDescriptor
                                    {
                                        WpdId = $"Sony\\USB\\{pid:X4}\\{uniqueId}",
                                        SerialNumber = uniqueId,
                                        CameraDevice = null // Will be set when camera is initialized
                                    };
                                    
                                    // Store camera info for later use
                                    descriptor.SetSonyCameraInfo(cameraInfo);
                                    descriptor.SetSonyModelName(model);
                                    
                                    devices.Add(descriptor);
                                }
                                else
                                {
                                    Log.Debug($"Sony USB: Skipping non-USB camera: {model} ({connectionType})");
                                    cameraInfo.Release();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Sony USB: Error processing camera {i}", ex);
                                cameraInfo?.Release();
                            }
                        }
                    }
                }
                finally
                {
                    enumObj.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Sony USB: Error enumerating cameras", ex);
            }
            
            return devices;
        }
        
        /// <summary>
        /// Get unique identifier for camera
        /// </summary>
        private static string GetCameraUniqueId(CrCameraObjectInfo cameraInfo)
        {
            try
            {
                IntPtr idPtr = cameraInfo.GetId();
                uint idSize = cameraInfo.GetIdSize();
                
                if (idPtr != IntPtr.Zero && idSize > 0)
                {
                    byte[] idBytes = new byte[idSize];
                    Marshal.Copy(idPtr, idBytes, 0, (int)idSize);
                    
                    // Convert to hex string
                    var hexString = BitConverter.ToString(idBytes).Replace("-", "");
                    return hexString;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Sony USB: Error getting camera ID: {ex.Message}");
            }
            
            // Fallback to model + timestamp
            return $"{cameraInfo.GetModel()}_{DateTime.Now.Ticks}";
        }
        
        /// <summary>
        /// Check if a device ID represents a Sony camera
        /// </summary>
        public static bool IsSonyCamera(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return false;
            
            // Sony USB vendor ID is 0x054C
            return deviceId.Contains("vid_054c") || 
                   deviceId.Contains("VID_054C") ||
                   deviceId.StartsWith("Sony\\");
        }
        
        /// <summary>
        /// Shutdown SDK when done
        /// </summary>
        public static void Shutdown()
        {
            lock (_lockObject)
            {
                if (_sdkInitialized)
                {
                    try
                    {
                        Log.Debug("Sony USB: Releasing SDK...");
                        SonySDKWrapper.Release();
                        _sdkInitialized = false;
                        Log.Debug("Sony USB: SDK released");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Sony USB: Error releasing SDK", ex);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Extensions to store Sony-specific information in DeviceDescriptor
    /// </summary>
    public static class DeviceDescriptorSonyExtensions
    {
        private static readonly Dictionary<DeviceDescriptor, CrCameraObjectInfo> _cameraInfoMap = 
            new Dictionary<DeviceDescriptor, CrCameraObjectInfo>();
        private static readonly Dictionary<DeviceDescriptor, string> _modelNameMap = 
            new Dictionary<DeviceDescriptor, string>();
        
        public static CrCameraObjectInfo GetSonyCameraInfo(this DeviceDescriptor descriptor)
        {
            return _cameraInfoMap.ContainsKey(descriptor) ? _cameraInfoMap[descriptor] : null;
        }
        
        public static void SetSonyCameraInfo(this DeviceDescriptor descriptor, CrCameraObjectInfo info)
        {
            _cameraInfoMap[descriptor] = info;
        }
        
        public static string GetSonyModelName(this DeviceDescriptor descriptor)
        {
            return _modelNameMap.ContainsKey(descriptor) ? _modelNameMap[descriptor] : "Sony Camera";
        }
        
        public static void SetSonyModelName(this DeviceDescriptor descriptor, string modelName)
        {
            _modelNameMap[descriptor] = modelName;
        }
        
        public static void CleanupSonyInfo(this DeviceDescriptor descriptor)
        {
            if (_cameraInfoMap.ContainsKey(descriptor))
            {
                var info = _cameraInfoMap[descriptor];
                info?.Release();
                _cameraInfoMap.Remove(descriptor);
            }
            _modelNameMap.Remove(descriptor);
        }
    }
}
