using System;
using System.Runtime.InteropServices;
using System.Text;
using CameraControl.Devices.Classes;

namespace CameraControl.Devices.Sony
{
    /// <summary>
    /// P/Invoke wrapper for Sony Camera Remote SDK
    /// </summary>
    public static class SonySDKWrapper
    {
        private const string SONY_SDK_DLL = "Cr_Core.dll";
        
        static SonySDKWrapper()
        {
            // Try to set DLL directory for Sony SDK
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
                
                // Check if DLLs are in subdirectory
                string sonySDKPath = System.IO.Path.Combine(assemblyDir, "sonysdk", "external", "crsdk");
                if (System.IO.Directory.Exists(sonySDKPath))
                {
                    SetDllDirectory(sonySDKPath);
                    Log.Debug($"Set Sony SDK DLL directory to: {sonySDKPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to set Sony SDK DLL directory", ex);
            }
        }
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        #region Enums and Structures

        public enum CrError : uint
        {
            CrError_None = 0x00000000,
            CrError_Generic = 0x00000001,
            CrError_NotSupported = 0x00000002,
            CrError_Canceled = 0x00000003,
            CrError_InvalidHandle = 0x00000004,
            CrError_InvalidParameter = 0x00000005,
            CrError_NotConnected = 0x00000006,
            CrError_DeviceBusy = 0x00000007,
            CrError_MemoryFull = 0x00000008,
            CrError_ConnectTimeOut = 0x00000009,
            CrError_ValueIsNotAvailable = 0x0000000A,
            CrError_ValueIsNotSet = 0x0000000B,
            CrError_Disconnected = 0x0000000C,
            CrError_PreviousRequestNotComplete = 0x0000000D,
            CrError_LensNotMounted = 0x0000000E,
            CrError_AdapterNotMounted = 0x0000000F,
            CrError_SystemNotSuspended = 0x00000010,
            CrError_FailedCommunication = 0x00000011,
            CrError_NotImplemented = 0x00000012,
            CrError_NotLoaded = 0x00000013,
            CrError_Invalid = 0x00000014,
            CrError_Max = 0x00000015,
            CrError_NoDevice = 0x00008703  // No device in PC Remote mode
        }

        public enum CrSdkControlMode : byte
        {
            CrSdkControlMode_Remote = 0,
            CrSdkControlMode_MassStorage = 1
        }

        public enum CrReconnectingSet : byte
        {
            CrReconnecting_OFF = 0,
            CrReconnecting_ON = 1
        }

        public enum CrCommandId : ushort
        {
            CrCommandId_Release = 0x0001,
            CrCommandId_MovieRecord = 0x0002,
            CrCommandId_DownloadFile = 0x0003,
            CrCommandId_Cancel = 0x0004
        }

        public enum CrDevicePropertyCode : uint
        {
            CrDevicePropertyCode_IsoSensitivity = 0x0001,
            CrDevicePropertyCode_FNumber = 0x0002,
            CrDevicePropertyCode_ShutterSpeed = 0x0003,
            CrDevicePropertyCode_ExposureProgramMode = 0x0004,
            CrDevicePropertyCode_WhiteBalance = 0x0005,
            CrDevicePropertyCode_FocusMode = 0x0006,
            CrDevicePropertyCode_MeteringMode = 0x0007,
            CrDevicePropertyCode_FlashMode = 0x0008,
            CrDevicePropertyCode_WirelessFlash = 0x0009,
            CrDevicePropertyCode_RedEyeReduction = 0x000A,
            CrDevicePropertyCode_DriveMode = 0x000B,
            CrDevicePropertyCode_DRO = 0x000C,
            CrDevicePropertyCode_ImageSize = 0x000D,
            CrDevicePropertyCode_AspectRatio = 0x000E,
            CrDevicePropertyCode_PictureEffect = 0x000F,
            CrDevicePropertyCode_FocusArea = 0x0010,
            CrDevicePropertyCode_CompressionFileFormat = 0x0011,
            CrDevicePropertyCode_MediaFormat = 0x0012,
            CrDevicePropertyCode_LiveView = 0x0013,
            CrDevicePropertyCode_BatteryLevel = 0x0014
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct CrCameraObjectInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Model;
            public ushort UsbPid;
            public uint IdType;
            public uint IdSize;
            public IntPtr Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ConnectTypeName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string AdaptorName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string PairingNecessity;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CrDeviceProperty
        {
            public CrDevicePropertyCode PropertyCode;
            public uint CurrentValue;
            public uint ValueSize;
            public IntPtr Values;
            public int NumValues;
            public byte GetEnableStatus;
            public byte SetEnableStatus;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CrLiveViewProperty
        {
            public byte GetEnableStatus;
            public byte SetEnableStatus;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CrImageDataBlock
        {
            public uint Size;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CrImageInfo
        {
            public uint Width;
            public uint Height;
            public uint Size;
            public byte Format;
        }

        #endregion

        #region Delegates

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void PropertyChangedCallback(IntPtr deviceHandle, IntPtr properties, int count);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void LiveViewCallback(IntPtr deviceHandle, IntPtr imageData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ObjectAddedCallback(IntPtr deviceHandle, IntPtr objectInfo);

        #endregion

        #region SDK Functions

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall, EntryPoint = "Init")]
        private static extern bool InitNative(uint logtype = 0);

        public static bool Init(uint logtype = 0)
        {
            try 
            {
                Log.Debug($"Sony SDK Init: Calling native Init with logtype={logtype}");
                bool result = InitNative(logtype);
                Log.Debug($"Sony SDK Init: Native Init returned {result}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"Sony SDK Init: Exception calling native Init: {ex.Message}", ex);
                return false;
            }
        }

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern bool Release();

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall, EntryPoint = "EnumCameraObjects")]
        private static extern CrError EnumCameraObjectsNative(out IntPtr ppEnumCameraObjectInfo, byte timeInSec = 3);
        
        public static CrError EnumCameraObjects(out IntPtr ppEnumCameraObjectInfo, byte timeInSec = 3)
        {
            ppEnumCameraObjectInfo = IntPtr.Zero;
            try
            {
                Log.Debug($"Sony SDK EnumCameraObjects: Calling native enum with timeout={timeInSec}s");
                CrError result = EnumCameraObjectsNative(out ppEnumCameraObjectInfo, timeInSec);
                Log.Debug($"Sony SDK EnumCameraObjects: Native enum returned {result}, ptr={ppEnumCameraObjectInfo}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"Sony SDK EnumCameraObjects: Exception: {ex.Message}", ex);
                return CrError.CrError_Generic;
            }
        }

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError Connect(
            IntPtr pCameraObjectInfo,
            IntPtr callback,
            out IntPtr deviceHandle,
            CrSdkControlMode openMode = CrSdkControlMode.CrSdkControlMode_Remote,
            CrReconnectingSet reconnect = CrReconnectingSet.CrReconnecting_ON,
            string userId = null,
            string userPassword = null,
            string fingerprint = null,
            uint fingerprintSize = 0);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError Disconnect(IntPtr deviceHandle);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError ReleaseDevice(IntPtr deviceHandle);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError GetDeviceProperties(
            IntPtr deviceHandle,
            out IntPtr properties,
            out int numOfProperties);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError SetDeviceProperty(
            IntPtr deviceHandle,
            ref CrDeviceProperty property);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError SendCommand(
            IntPtr deviceHandle,
            CrCommandId command,
            IntPtr param = default(IntPtr));

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError GetLiveViewImage(
            IntPtr deviceHandle,
            out CrImageDataBlock imageData);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError GetLiveViewProperties(
            IntPtr deviceHandle,
            out CrLiveViewProperty properties);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError StartLiveView(
            IntPtr deviceHandle,
            IntPtr liveViewCallback);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError StopLiveView(IntPtr deviceHandle);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError DownloadFile(
            IntPtr deviceHandle,
            IntPtr objectHandle,
            string filePath);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError GetEvent(
            IntPtr deviceHandle,
            out IntPtr eventData,
            out uint eventDataSize);

        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern CrError SetSaveInfo(
            IntPtr deviceHandle,
            string prefix,
            uint startNo,
            string destPath);

        #endregion

        #region Helper Functions

        public static string GetErrorMessage(CrError error)
        {
            switch (error)
            {
                case CrError.CrError_None:
                    return "No error";
                case CrError.CrError_Generic:
                    return "Generic error";
                case CrError.CrError_NotSupported:
                    return "Operation not supported";
                case CrError.CrError_Canceled:
                    return "Operation canceled";
                case CrError.CrError_InvalidHandle:
                    return "Invalid handle";
                case CrError.CrError_InvalidParameter:
                    return "Invalid parameter";
                case CrError.CrError_NotConnected:
                    return "Device not connected";
                case CrError.CrError_DeviceBusy:
                    return "Device busy";
                case CrError.CrError_MemoryFull:
                    return "Memory full";
                case CrError.CrError_ConnectTimeOut:
                    return "Connection timeout";
                case CrError.CrError_ValueIsNotAvailable:
                    return "Value is not available";
                case CrError.CrError_ValueIsNotSet:
                    return "Value is not set";
                case CrError.CrError_Disconnected:
                    return "Device disconnected";
                case CrError.CrError_PreviousRequestNotComplete:
                    return "Previous request not complete";
                case CrError.CrError_LensNotMounted:
                    return "Lens not mounted";
                case CrError.CrError_AdapterNotMounted:
                    return "Adapter not mounted";
                case CrError.CrError_SystemNotSuspended:
                    return "System not suspended";
                case CrError.CrError_FailedCommunication:
                    return "Communication failed";
                case CrError.CrError_NotImplemented:
                    return "Not implemented";
                case CrError.CrError_NotLoaded:
                    return "Not loaded";
                case CrError.CrError_Invalid:
                    return "Invalid";
                default:
                    return $"Unknown error ({error})";
            }
        }

        #endregion
    }
}