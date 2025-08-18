using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CameraControl.Devices.Classes;
using static CameraControl.Devices.Sony.SonySDKWrapper;

namespace CameraControl.Devices.Sony
{
    public class SonyUSBProvider
    {
        private static bool _sdkInitialized = false;
        private static readonly object _initLock = new object();

        public static List<DeviceDescriptor> GetConnectedCameras()
        {
            var devices = new List<DeviceDescriptor>();

            try
            {
                // Initialize SDK if needed
                lock (_initLock)
                {
                    if (!_sdkInitialized)
                    {
                        Log.Debug("Attempting to initialize Sony SDK...");
                        try
                        {
                            if (!SonySDKWrapper.Init())
                            {
                                Log.Error("Failed to initialize Sony SDK - Init() returned false");
                                Log.Error("Make sure Sony camera is in PC Remote mode and USB drivers are installed");
                                return devices;
                            }
                            _sdkInitialized = true;
                            Log.Debug("Sony SDK initialized successfully");
                        }
                        catch (DllNotFoundException dllEx)
                        {
                            Log.Error($"Sony SDK DLL not found: {dllEx.Message}");
                            Log.Error("Ensure Cr_Core.dll and dependencies are in the application directory");
                            return devices;
                        }
                        catch (Exception initEx)
                        {
                            Log.Error($"Exception initializing Sony SDK: {initEx.Message}", initEx);
                            return devices;
                        }
                    }
                }

                // Enumerate connected cameras
                IntPtr cameraEnumPtr;
                Log.Debug("Calling Sony SDK EnumCameraObjects with 10 second timeout...");
                Log.Debug($"Sony SDK: Looking for cameras with VID=054C (Sony vendor ID)");
                var result = SonySDKWrapper.EnumCameraObjects(out cameraEnumPtr, 10); // 10 second timeout
                
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to enumerate Sony cameras: {GetErrorMessage(result)} (Error code: 0x{((int)result):X})");
                    
                    // Provide specific guidance based on error code
                    if ((int)result == 0x8703 || result == CrError.CrError_NoDevice)
                    {
                        Log.Error("No Sony cameras detected in PC Remote mode.");
                        Log.Error("To use Sony camera with this application:");
                        Log.Error("1. Connect camera via USB cable");
                        Log.Error("2. Turn on the camera");
                        Log.Error("3. Set camera to 'PC Remote' mode (usually in Settings -> USB Connection)");
                        Log.Error("4. Install Sony camera USB drivers if not already installed");
                    }
                    else if (result == CrError.CrError_NotLoaded)
                    {
                        Log.Error("Sony SDK components not properly loaded. Check that all required DLLs are present.");
                    }
                    return devices;
                }

                if (cameraEnumPtr == IntPtr.Zero)
                {
                    Log.Debug("No Sony cameras found");
                    return devices;
                }

                // Parse camera information
                // The actual structure depends on SDK implementation
                // This is a simplified version
                devices.AddRange(ParseCameraEnum(cameraEnumPtr));

                // Free enumeration resources if needed
                // SDK should provide a release function for this

            }
            catch (Exception ex)
            {
                Log.Error("Error enumerating Sony USB cameras", ex);
            }

            return devices;
        }

        private static List<DeviceDescriptor> ParseCameraEnum(IntPtr enumPtr)
        {
            var devices = new List<DeviceDescriptor>();

            try
            {
                // This would need to be implemented based on the actual SDK structure
                // For now, we'll create a sample implementation
                
                // Read camera count (assuming first int is count)
                int cameraCount = Marshal.ReadInt32(enumPtr);
                
                if (cameraCount > 0)
                {
                    // Move to first camera info
                    IntPtr currentPtr = new IntPtr(enumPtr.ToInt64() + sizeof(int));
                    int cameraInfoSize = Marshal.SizeOf(typeof(CrCameraObjectInfo));

                    for (int i = 0; i < cameraCount && i < 10; i++) // Limit to 10 cameras for safety
                    {
                        try
                        {
                            CrCameraObjectInfo cameraInfo = (CrCameraObjectInfo)Marshal.PtrToStructure(currentPtr, typeof(CrCameraObjectInfo));
                            
                            var descriptor = new DeviceDescriptor
                            {
                                WpdId = $"Sony\\USB\\{cameraInfo.UsbPid:X4}",
                                SerialNumber = GetSerialFromCameraInfo(cameraInfo),
                                CameraDevice = null // Will be set when device is created
                            };
                            descriptor.SetSonyDeviceInfo(currentPtr);

                            // Store model name for later use
                            descriptor.SetSonyModelName(cameraInfo.Model);
                            
                            devices.Add(descriptor);
                            Log.Debug($"Found Sony camera: {cameraInfo.Model} (PID: {cameraInfo.UsbPid:X4})");

                            // Move to next camera info
                            currentPtr = new IntPtr(currentPtr.ToInt64() + cameraInfoSize);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Error parsing camera info at index {i}", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error parsing Sony camera enumeration", ex);
            }

            return devices;
        }

        private static string GetSerialFromCameraInfo(CrCameraObjectInfo cameraInfo)
        {
            try
            {
                if (cameraInfo.Id != IntPtr.Zero && cameraInfo.IdSize > 0)
                {
                    byte[] idBytes = new byte[cameraInfo.IdSize];
                    Marshal.Copy(cameraInfo.Id, idBytes, 0, (int)cameraInfo.IdSize);
                    
                    // Convert to string (assuming it's a serial number)
                    return System.Text.Encoding.ASCII.GetString(idBytes).TrimEnd('\0');
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error getting serial number from camera info", ex);
            }

            // Generate a unique ID if serial is not available
            return $"SONY_{cameraInfo.UsbPid:X4}_{DateTime.Now.Ticks}";
        }

        public static bool IsSonyCamera(string deviceId)
        {
            // Check if a device ID represents a Sony camera
            // Sony USB vendor ID is typically 0x054C
            return deviceId != null && 
                   (deviceId.Contains("vid_054c") || 
                    deviceId.Contains("VID_054C") ||
                    deviceId.StartsWith("Sony\\"));
        }

        public static void Shutdown()
        {
            try
            {
                lock (_initLock)
                {
                    if (_sdkInitialized)
                    {
                        SonySDKWrapper.Release();
                        _sdkInitialized = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error shutting down Sony SDK", ex);
            }
        }
    }

    // Extension to DeviceDescriptor to hold Sony-specific info
    public static class DeviceDescriptorSonyExtensions
    {
        private static readonly Dictionary<DeviceDescriptor, IntPtr> _sonyDeviceInfoMap = 
            new Dictionary<DeviceDescriptor, IntPtr>();
        private static readonly Dictionary<DeviceDescriptor, string> _sonyModelNameMap = 
            new Dictionary<DeviceDescriptor, string>();

        public static IntPtr GetSonyDeviceInfo(this DeviceDescriptor descriptor)
        {
            return _sonyDeviceInfoMap.ContainsKey(descriptor) ? _sonyDeviceInfoMap[descriptor] : IntPtr.Zero;
        }

        public static void SetSonyDeviceInfo(this DeviceDescriptor descriptor, IntPtr value)
        {
            _sonyDeviceInfoMap[descriptor] = value;
        }
        
        public static string GetSonyModelName(this DeviceDescriptor descriptor)
        {
            return _sonyModelNameMap.ContainsKey(descriptor) ? _sonyModelNameMap[descriptor] : "Sony Camera";
        }

        public static void SetSonyModelName(this DeviceDescriptor descriptor, string value)
        {
            _sonyModelNameMap[descriptor] = value;
        }
    }
}