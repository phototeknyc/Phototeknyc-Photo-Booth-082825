using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CameraControl.Devices.Sony
{
    /// <summary>
    /// Interface to access camera information from SDK enumeration
    /// Matches ICrCameraObjectInfo from SDK
    /// </summary>
    public class CrCameraObjectInfo
    {
        private IntPtr _nativePtr;
        
        public CrCameraObjectInfo(IntPtr nativePtr)
        {
            _nativePtr = nativePtr;
        }
        
        public IntPtr NativePtr => _nativePtr;
        
        // Virtual function table for ICrCameraObjectInfo
        [StructLayout(LayoutKind.Sequential)]
        private struct VTable
        {
            public IntPtr Release;
            public IntPtr GetName;
            public IntPtr GetModel;
            public IntPtr GetUsbPid;
            public IntPtr GetIdType;
            public IntPtr GetIdSize;
            public IntPtr GetId;
            public IntPtr GetConnectionTypeName;
            public IntPtr GetAdaptorName;
            public IntPtr GetPairingNecessity;
            public IntPtr GetSSHsupport;
        }
        
        private VTable GetVTable()
        {
            IntPtr vtablePtr = Marshal.ReadIntPtr(_nativePtr);
            return (VTable)Marshal.PtrToStructure(vtablePtr, typeof(VTable));
        }
        
        public void Release()
        {
            if (_nativePtr != IntPtr.Zero)
            {
                var vtable = GetVTable();
                if (vtable.Release != IntPtr.Zero)
                {
                    var releaseFunc = (ReleaseDelegate)Marshal.GetDelegateForFunctionPointer(
                        vtable.Release, typeof(ReleaseDelegate));
                    releaseFunc(_nativePtr);
                }
                _nativePtr = IntPtr.Zero;
            }
        }
        
        public string GetName()
        {
            var vtable = GetVTable();
            if (vtable.GetName != IntPtr.Zero)
            {
                var func = (GetStringDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetName, typeof(GetStringDelegate));
                IntPtr strPtr = func(_nativePtr);
                if (strPtr != IntPtr.Zero)
                {
                    // Assuming wide char (Unicode) strings
                    return Marshal.PtrToStringUni(strPtr);
                }
            }
            return string.Empty;
        }
        
        public string GetModel()
        {
            var vtable = GetVTable();
            if (vtable.GetModel != IntPtr.Zero)
            {
                var func = (GetStringDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetModel, typeof(GetStringDelegate));
                IntPtr strPtr = func(_nativePtr);
                if (strPtr != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(strPtr);
                }
            }
            return string.Empty;
        }
        
        public ushort GetUsbPid()
        {
            var vtable = GetVTable();
            if (vtable.GetUsbPid != IntPtr.Zero)
            {
                var func = (GetUShortDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetUsbPid, typeof(GetUShortDelegate));
                return func(_nativePtr);
            }
            return 0;
        }
        
        public string GetConnectionTypeName()
        {
            var vtable = GetVTable();
            if (vtable.GetConnectionTypeName != IntPtr.Zero)
            {
                var func = (GetStringDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetConnectionTypeName, typeof(GetStringDelegate));
                IntPtr strPtr = func(_nativePtr);
                if (strPtr != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(strPtr);
                }
            }
            return string.Empty;
        }
        
        public IntPtr GetId()
        {
            var vtable = GetVTable();
            if (vtable.GetId != IntPtr.Zero)
            {
                var func = (GetPtrDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetId, typeof(GetPtrDelegate));
                return func(_nativePtr);
            }
            return IntPtr.Zero;
        }
        
        public uint GetIdSize()
        {
            var vtable = GetVTable();
            if (vtable.GetIdSize != IntPtr.Zero)
            {
                var func = (GetUIntDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetIdSize, typeof(GetUIntDelegate));
                return func(_nativePtr);
            }
            return 0;
        }
        
        // Delegate types for virtual functions
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void ReleaseDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr GetStringDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate ushort GetUShortDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate uint GetUIntDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr GetPtrDelegate(IntPtr thisPtr);
    }
    
    /// <summary>
    /// Interface to enumerate camera objects
    /// Matches ICrEnumCameraObjectInfo from SDK
    /// </summary>
    public class CrEnumCameraObjectInfo
    {
        private IntPtr _nativePtr;
        
        public CrEnumCameraObjectInfo(IntPtr nativePtr)
        {
            _nativePtr = nativePtr;
        }
        
        // Virtual function table
        [StructLayout(LayoutKind.Sequential)]
        private struct VTable
        {
            public IntPtr Release;
            public IntPtr GetCount;
            public IntPtr GetCameraObjectInfo;
        }
        
        private VTable GetVTable()
        {
            IntPtr vtablePtr = Marshal.ReadIntPtr(_nativePtr);
            return (VTable)Marshal.PtrToStructure(vtablePtr, typeof(VTable));
        }
        
        public void Release()
        {
            if (_nativePtr != IntPtr.Zero)
            {
                var vtable = GetVTable();
                if (vtable.Release != IntPtr.Zero)
                {
                    var releaseFunc = (ReleaseDelegate)Marshal.GetDelegateForFunctionPointer(
                        vtable.Release, typeof(ReleaseDelegate));
                    releaseFunc(_nativePtr);
                }
                _nativePtr = IntPtr.Zero;
            }
        }
        
        public uint GetCount()
        {
            var vtable = GetVTable();
            if (vtable.GetCount != IntPtr.Zero)
            {
                var func = (GetCountDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetCount, typeof(GetCountDelegate));
                return func(_nativePtr);
            }
            return 0;
        }
        
        public CrCameraObjectInfo GetCameraObjectInfo(uint index)
        {
            var vtable = GetVTable();
            if (vtable.GetCameraObjectInfo != IntPtr.Zero)
            {
                var func = (GetCameraObjectInfoDelegate)Marshal.GetDelegateForFunctionPointer(
                    vtable.GetCameraObjectInfo, typeof(GetCameraObjectInfoDelegate));
                IntPtr cameraInfoPtr = func(_nativePtr, index);
                if (cameraInfoPtr != IntPtr.Zero)
                {
                    return new CrCameraObjectInfo(cameraInfoPtr);
                }
            }
            return null;
        }
        
        // Delegate types
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void ReleaseDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate uint GetCountDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr GetCameraObjectInfoDelegate(IntPtr thisPtr, uint index);
    }
}