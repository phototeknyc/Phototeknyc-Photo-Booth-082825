using System;
using System.Runtime.InteropServices;

namespace CameraControl.Devices.Sony
{
    public static class SonySDKWrapper
    {
        private const string SONY_SDK_DLL = "Cr_Core.dll";
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Init")]
        public static extern bool Init(uint logtype = 0);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Release")]
        public static extern bool Release();
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetSDKVersion")]
        public static extern uint GetSDKVersion();
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "EnumCameraObjects")]
        public static extern CrError EnumCameraObjects(out IntPtr ppEnumCameraObjectInfo, byte timeInSec = 3);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "CreateCameraObjectInfoUSBConnection")]
        public static extern CrError CreateCameraObjectInfoUSBConnection(out IntPtr ppCameraObjectInfo, CrCameraDeviceModel model, IntPtr usbSerialNumber);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Connect")]
        public static extern CrError Connect(IntPtr pCameraObjectInfo, IntPtr callback, out IntPtr deviceHandle, 
            CrSdkControlMode openMode = CrSdkControlMode.CrSdkControlMode_Remote,
            CrReconnectingSet reconnect = CrReconnectingSet.CrReconnecting_ON,
            IntPtr userId = default(IntPtr),
            IntPtr userPassword = default(IntPtr),
            IntPtr fingerprint = default(IntPtr),
            uint fingerprintSize = 0);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Disconnect")]
        public static extern CrError Disconnect(IntPtr deviceHandle);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SendCommand")]
        public static extern CrError SendCommand(IntPtr deviceHandle, uint commandId, CrCommandParam commandParam);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ReleaseDevice")]
        public static extern CrError ReleaseDevice(IntPtr deviceHandle);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetDeviceProperties")]
        public static extern CrError GetDeviceProperties(IntPtr deviceHandle, out IntPtr properties, out int numOfProperties);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ReleaseDeviceProperties")]
        public static extern CrError ReleaseDeviceProperties(IntPtr deviceHandle, IntPtr properties);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetSaveInfo")]
        public static extern CrError SetSaveInfo(IntPtr deviceHandle, [MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] string prefix, int no);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetLiveViewImage")]
        public static extern CrError GetLiveViewImage(IntPtr deviceHandle, IntPtr imageData);
        
        // Alternative live view methods for C++ API compatibility
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetLiveViewProperties")]
        public static extern CrError GetLiveViewProperties(IntPtr deviceHandle, out IntPtr properties, out int numOfProperties);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ReleaseLiveViewProperties")]
        public static extern CrError ReleaseLiveViewProperties(IntPtr deviceHandle, IntPtr properties);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetLiveViewImageInfo")]
        public static extern CrError GetLiveViewImageInfo(IntPtr deviceHandle, out CrImageInfo info);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetDeviceProperty")]
        public static extern CrError SetDeviceProperty(IntPtr deviceHandle, IntPtr propertyData);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetDeviceProperty")]
        public static extern CrError GetDeviceProperty(IntPtr deviceHandle, uint propertyCode, out IntPtr propertyData);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetDeviceSetting")]
        public static extern CrError GetDeviceSetting(IntPtr deviceHandle, uint key, out uint value);
        
        [DllImport(SONY_SDK_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetDeviceSetting")]
        public static extern CrError SetDeviceSetting(IntPtr deviceHandle, uint key, uint value);
        
        public static string GetErrorMessage(CrError error)
        {
            switch (error)
            {
                case CrError.CrError_None:
                    return "Success";
                case CrError.CrError_Api_InvalidSerialNumber:
                    return "Invalid Serial Number - Camera requires valid USB serial number";
                case CrError.CrError_Api_Insufficient:
                    return "Insufficient Setup - Camera requires additional property configuration";
                case CrError.CrError_Api_OutOfModelList:
                    return "Model Not Supported - Camera model not recognized by SDK";
                case CrError.CrError_Adaptor_NoDevice:
                    return "No Device - Camera not detected or not in PC Remote mode";
                case CrError.CrError_Adaptor_EnumDevice:
                    return "Enumeration Failed - Error enumerating cameras";
                default:
                    return $"{error} (0x{(int)error:X4})";
            }
        }
        
        public static bool IsSuccess(CrError error)
        {
            return error == CrError.CrError_None;
        }
    }
    
    public enum CrError : int
    {
        CrError_None = 0x0000,
        CrError_Generic = 0x8000,
        CrError_Api = 0x8400,
        CrError_Api_Insufficient = 0x8402,
        CrError_Api_OutOfModelList = 0x8404,
        CrError_Api_InvalidSerialNumber = 0x8407,
        CrError_Adaptor_NoDevice = 0x8703,
        CrError_Adaptor_EnumDevice = 0x8708
    }
    
    public enum CrCommandParam : int
    {
        CrCommandParam_Down = 0,
        CrCommandParam_Up = 1
    }
    
    public enum CrSdkControlMode : uint
    {
        CrSdkControlMode_Remote = 0x00000000,
        CrSdkControlMode_ContentsTransfer = 0x00000001,
        CrSdkControlMode_RemoteTransfer = 0x00000002
    }
    
    public enum CrReconnectingSet : int
    {
        CrReconnecting_OFF = 0,
        CrReconnecting_ON = 1
    }
    
    public enum CrCommandId : uint
    {
        CrCommandId_Release = 0x0000,
        CrCommandId_MovieRecord = 0x0001,
        CrCommandId_CancelShooting = 0x0002,
        CrCommandId_MediaFormat = 0x0004,
        CrCommandId_MediaQuickFormat = 0x0005,
        CrCommandId_CancelMediaFormat = 0x0006,
        CrCommandId_S1andRelease = 0x0007,  // This is the proper still capture command!
        CrCommandId_CancelContentsTransfer = 0x0008,
        CrCommandId_MovieRecButtonToggle = 0x0014
    }
    
    public enum CrDevicePropertyCode : uint
    {
        CrDeviceProperty_ExposureProgramMode = 0x0106,
        CrDeviceProperty_PriorityKeySettings = 0x500C,
        CrDeviceProperty_SdkControlMode = 0x5022,
        CrDeviceProperty_RecordingState = 0x502D,
        CrDeviceProperty_CameraOperatingMode = 0x502F
    }
    
    public enum CrPriorityKeySettings : ushort
    {
        CrPriorityKey_CameraPosition = 0x0001,
        CrPriorityKey_PCRemote = 0x0002
    }
    
    public enum CrExposureProgram : uint
    {
        CrExposure_M_Manual = 0x0001,
        CrExposure_P_Auto = 0x0002,
        CrExposure_A_AperturePriority = 0x0003,
        CrExposure_S_ShutterSpeedPriority = 0x0004,
        CrExposure_Auto = 0x8013
    }
    
    public enum CrCameraOperatingMode : uint
    {
        CrCameraOperatingMode_Record = 0x01,
        CrCameraOperatingMode_Playback = 0x02
    }
    
    public enum CrRecordingState : byte
    {
        CrRecordingState_NotRecording = 0x00,
        CrRecordingState_Recording = 0x01
    }
    
    public enum CrDataType : uint
    {
        CrDataType_Undefined = 0x0000,
        CrDataType_UInt8 = 0x0001,
        CrDataType_UInt16 = 0x0002,
        CrDataType_UInt32 = 0x0003,
        CrDataType_UInt64 = 0x0004,
        CrDataType_UInt128 = 0x0005,
        CrDataType_SignBit = 0x1000,
        CrDataType_Int8 = CrDataType_SignBit | CrDataType_UInt8,
        CrDataType_Int16 = CrDataType_SignBit | CrDataType_UInt16,
        CrDataType_Int32 = CrDataType_SignBit | CrDataType_UInt32,
        CrDataType_Int64 = CrDataType_SignBit | CrDataType_UInt64,
        CrDataType_Int128 = CrDataType_SignBit | CrDataType_UInt128,
        CrDataType_ArrayBit = 0x2000,
        CrDataType_UInt8Array = CrDataType_ArrayBit | CrDataType_UInt8,
        CrDataType_UInt16Array = CrDataType_ArrayBit | CrDataType_UInt16,
        CrDataType_UInt32Array = CrDataType_ArrayBit | CrDataType_UInt32,
        CrDataType_UInt64Array = CrDataType_ArrayBit | CrDataType_UInt64,
        CrDataType_UInt128Array = CrDataType_ArrayBit | CrDataType_UInt128,
        CrDataType_Int8Array = CrDataType_ArrayBit | CrDataType_Int8,
        CrDataType_Int16Array = CrDataType_ArrayBit | CrDataType_Int16,
        CrDataType_Int32Array = CrDataType_ArrayBit | CrDataType_Int32,
        CrDataType_Int64Array = CrDataType_ArrayBit | CrDataType_Int64,
        CrDataType_Int128Array = CrDataType_ArrayBit | CrDataType_Int128,
        CrDataType_RangeBit = 0x4000,
        CrDataType_UInt8Range = CrDataType_RangeBit | CrDataType_UInt8,
        CrDataType_UInt16Range = CrDataType_RangeBit | CrDataType_UInt16,
        CrDataType_UInt32Range = CrDataType_RangeBit | CrDataType_UInt32,
        CrDataType_UInt64Range = CrDataType_RangeBit | CrDataType_UInt64,
        CrDataType_UInt128Range = CrDataType_RangeBit | CrDataType_UInt128,
        CrDataType_Int8Range = CrDataType_RangeBit | CrDataType_Int8,
        CrDataType_Int16Range = CrDataType_RangeBit | CrDataType_Int16,
        CrDataType_Int32Range = CrDataType_RangeBit | CrDataType_Int32,
        CrDataType_Int64Range = CrDataType_RangeBit | CrDataType_Int64,
        CrDataType_Int128Range = CrDataType_RangeBit | CrDataType_Int128,
        CrDataType_STR = 0xFFFF
    }
    
    public enum CrCameraDeviceModel : uint
    {
        CrCameraDeviceModel_ILCE_7RM4 = 0,
        CrCameraDeviceModel_ILCE_9M2 = 1,
        CrCameraDeviceModel_ILCE_7C = 2,
        CrCameraDeviceModel_ILCE_7SM3 = 3,
        CrCameraDeviceModel_ILCE_1 = 4,
        CrCameraDeviceModel_ILCE_7RM4A = 5,
        CrCameraDeviceModel_DSC_RX0M2 = 6,
        CrCameraDeviceModel_ILCE_7M4 = 7,
        CrCameraDeviceModel_ILME_FX3 = 8,
        CrCameraDeviceModel_ILME_FX30 = 9,
        CrCameraDeviceModel_ILME_FX6 = 10,
        CrCameraDeviceModel_ILCE_7RM5 = 11,
        CrCameraDeviceModel_ZV_E1 = 12,
        CrCameraDeviceModel_ILCE_6700 = 13,
        CrCameraDeviceModel_ILCE_7CM2 = 14,
        CrCameraDeviceModel_ILCE_7CR = 15,
        CrCameraDeviceModel_ILX_LR1 = 16,
        CrCameraDeviceModel_MPC_2610 = 17,
        CrCameraDeviceModel_ILCE_9M3 = 18,
        CrCameraDeviceModel_ZV_E10M2 = 19,
        CrCameraDeviceModel_PXW_Z200 = 20,
        CrCameraDeviceModel_HXR_NX800 = 21,
        CrCameraDeviceModel_ILCE_1M2 = 22,
        CrCameraDeviceModel_ILME_FX3A = 23,
        CrCameraDeviceModel_BRC_AM7 = 24,
        CrCameraDeviceModel_ILME_FR7 = 25,
        CrCameraDeviceModel_ILME_FX2 = 26
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CrImageInfo
    {
        public uint Width;
        public uint Height;
        public uint BufferSize;
        public byte Format;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CrImageDataBlock
    {
        public IntPtr Data;
        public uint Size;
        public uint FrameNo;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CrDeviceProperty
    {
        public uint Code;
        public CrDataType ValueType;
        public ulong CurrentValue;
        public uint ValueSize;
        public IntPtr ValuePtr;
        
        public static CrDeviceProperty Create(CrDevicePropertyCode code, CrDataType valueType, ulong value)
        {
            return new CrDeviceProperty
            {
                Code = (uint)code,
                ValueType = valueType,
                CurrentValue = value,
                ValueSize = 0,
                ValuePtr = IntPtr.Zero
            };
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct CrLiveViewProperty
    {
        public uint Code;
        public CrDataType ValueType;
        public ulong CurrentValue;
        public uint ValueSize;
        public IntPtr ValuePtr;
    }
}