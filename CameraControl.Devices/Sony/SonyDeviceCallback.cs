using System;
using System.Runtime.InteropServices;

namespace CameraControl.Devices.Sony
{
    /// <summary>
    /// Implementation of IDeviceCallback for Sony SDK
    /// This class creates a COM-compatible callback interface
    /// </summary>
    public class SonyDeviceCallback
    {
        private IntPtr _callbackPtr;
        private IntPtr _vtablePtr;
        private GCHandle _thisHandle;
        private GCHandle _vtableHandle;
        
        // IMPORTANT: Keep delegate references to prevent garbage collection
        private OnConnectedDelegate _onConnectedDelegate;
        private OnDisconnectedDelegate _onDisconnectedDelegate;
        private OnPropertyChangedDelegate _onPropertyChangedDelegate;
        private OnPropertyChangedCodesDelegate _onPropertyChangedCodesDelegate;
        private OnLvPropertyChangedDelegate _onLvPropertyChangedDelegate;
        private OnLvPropertyChangedCodesDelegate _onLvPropertyChangedCodesDelegate;
        private OnCompleteDownloadDelegate _onCompleteDownloadDelegate;
        private OnNotifyContentsTransferDelegate _onNotifyContentsTransferDelegate;
        private OnWarningDelegate _onWarningDelegate;
        private OnWarningExtDelegate _onWarningExtDelegate;
        private OnErrorDelegate _onErrorDelegate;
        private OnNotifyFTPTransferResultDelegate _onNotifyFTPTransferResultDelegate;
        private OnNotifyRemoteTransferResultDelegate _onNotifyRemoteTransferResultDelegate;
        private OnNotifyRemoteTransferResult2Delegate _onNotifyRemoteTransferResult2Delegate;
        private OnNotifyRemoteTransferContentsListChangedDelegate _onNotifyRemoteTransferContentsListChangedDelegate;
        private OnNotifyRemoteFirmwareUpdateResultDelegate _onNotifyRemoteFirmwareUpdateResultDelegate;
        private OnReceivePlaybackTimeCodeDelegate _onReceivePlaybackTimeCodeDelegate;
        private OnReceivePlaybackDataDelegate _onReceivePlaybackDataDelegate;
        private OnNotifyMonitorUpdatedDelegate _onNotifyMonitorUpdatedDelegate;
        
        // Event handlers
        public event Action<uint> OnConnected;
        public event Action<uint> OnDisconnected;
        public event Action OnPropertyChanged;
        public event Action OnLvPropertyChanged;
        public event Action<string> OnCompleteDownload;
        public event Action<uint, uint, string> OnNotifyContentsTransfer;
        public event Action<uint> OnWarning;
        public event Action<uint> OnError;
        public event Action<uint, uint, uint> OnNotifyRemoteTransferContentsListChanged;
        
        // Virtual function table structure matching IDeviceCallback
        [StructLayout(LayoutKind.Sequential)]
        private struct VTable
        {
            public IntPtr OnConnected;
            public IntPtr OnDisconnected;
            public IntPtr OnPropertyChanged;
            public IntPtr OnPropertyChangedCodes;
            public IntPtr OnLvPropertyChanged;
            public IntPtr OnLvPropertyChangedCodes;
            public IntPtr OnCompleteDownload;
            public IntPtr OnNotifyContentsTransfer;
            public IntPtr OnWarning;
            public IntPtr OnWarningExt;
            public IntPtr OnError;
            public IntPtr OnNotifyFTPTransferResult;
            public IntPtr OnNotifyRemoteTransferResult;
            public IntPtr OnNotifyRemoteTransferResult2;
            public IntPtr OnNotifyRemoteTransferContentsListChanged;
            public IntPtr OnNotifyRemoteFirmwareUpdateResult;
            public IntPtr OnReceivePlaybackTimeCode;
            public IntPtr OnReceivePlaybackData;
            public IntPtr OnNotifyMonitorUpdated;
        }
        
        // Object structure (vtable pointer + this pointer)
        [StructLayout(LayoutKind.Sequential)]
        private struct CallbackObject
        {
            public IntPtr VTablePtr;
        }
        
        public SonyDeviceCallback()
        {
            CreateCallback();
        }
        
        private void CreateCallback()
        {
            // Create delegates and store them to prevent garbage collection
            _onConnectedDelegate = new OnConnectedDelegate(OnConnectedCallback);
            _onDisconnectedDelegate = new OnDisconnectedDelegate(OnDisconnectedCallback);
            _onPropertyChangedDelegate = new OnPropertyChangedDelegate(OnPropertyChangedCallback);
            _onPropertyChangedCodesDelegate = new OnPropertyChangedCodesDelegate(OnPropertyChangedCodesCallback);
            _onLvPropertyChangedDelegate = new OnLvPropertyChangedDelegate(OnLvPropertyChangedCallback);
            _onLvPropertyChangedCodesDelegate = new OnLvPropertyChangedCodesDelegate(OnLvPropertyChangedCodesCallback);
            _onCompleteDownloadDelegate = new OnCompleteDownloadDelegate(OnCompleteDownloadCallback);
            _onNotifyContentsTransferDelegate = new OnNotifyContentsTransferDelegate(OnNotifyContentsTransferCallback);
            _onWarningDelegate = new OnWarningDelegate(OnWarningCallback);
            _onWarningExtDelegate = new OnWarningExtDelegate(OnWarningExtCallback);
            _onErrorDelegate = new OnErrorDelegate(OnErrorCallback);
            _onNotifyFTPTransferResultDelegate = new OnNotifyFTPTransferResultDelegate(OnNotifyFTPTransferResultCallback);
            _onNotifyRemoteTransferResultDelegate = new OnNotifyRemoteTransferResultDelegate(OnNotifyRemoteTransferResultCallback);
            _onNotifyRemoteTransferResult2Delegate = new OnNotifyRemoteTransferResult2Delegate(OnNotifyRemoteTransferResult2Callback);
            _onNotifyRemoteTransferContentsListChangedDelegate = new OnNotifyRemoteTransferContentsListChangedDelegate(OnNotifyRemoteTransferContentsListChangedCallback);
            _onNotifyRemoteFirmwareUpdateResultDelegate = new OnNotifyRemoteFirmwareUpdateResultDelegate(OnNotifyRemoteFirmwareUpdateResultCallback);
            _onReceivePlaybackTimeCodeDelegate = new OnReceivePlaybackTimeCodeDelegate(OnReceivePlaybackTimeCodeCallback);
            _onReceivePlaybackDataDelegate = new OnReceivePlaybackDataDelegate(OnReceivePlaybackDataCallback);
            _onNotifyMonitorUpdatedDelegate = new OnNotifyMonitorUpdatedDelegate(OnNotifyMonitorUpdatedCallback);
            
            // Create virtual function table using stored delegates
            var vtable = new VTable
            {
                OnConnected = Marshal.GetFunctionPointerForDelegate(_onConnectedDelegate),
                OnDisconnected = Marshal.GetFunctionPointerForDelegate(_onDisconnectedDelegate),
                OnPropertyChanged = Marshal.GetFunctionPointerForDelegate(_onPropertyChangedDelegate),
                OnPropertyChangedCodes = Marshal.GetFunctionPointerForDelegate(_onPropertyChangedCodesDelegate),
                OnLvPropertyChanged = Marshal.GetFunctionPointerForDelegate(_onLvPropertyChangedDelegate),
                OnLvPropertyChangedCodes = Marshal.GetFunctionPointerForDelegate(_onLvPropertyChangedCodesDelegate),
                OnCompleteDownload = Marshal.GetFunctionPointerForDelegate(_onCompleteDownloadDelegate),
                OnNotifyContentsTransfer = Marshal.GetFunctionPointerForDelegate(_onNotifyContentsTransferDelegate),
                OnWarning = Marshal.GetFunctionPointerForDelegate(_onWarningDelegate),
                OnWarningExt = Marshal.GetFunctionPointerForDelegate(_onWarningExtDelegate),
                OnError = Marshal.GetFunctionPointerForDelegate(_onErrorDelegate),
                OnNotifyFTPTransferResult = Marshal.GetFunctionPointerForDelegate(_onNotifyFTPTransferResultDelegate),
                OnNotifyRemoteTransferResult = Marshal.GetFunctionPointerForDelegate(_onNotifyRemoteTransferResultDelegate),
                OnNotifyRemoteTransferResult2 = Marshal.GetFunctionPointerForDelegate(_onNotifyRemoteTransferResult2Delegate),
                OnNotifyRemoteTransferContentsListChanged = Marshal.GetFunctionPointerForDelegate(_onNotifyRemoteTransferContentsListChangedDelegate),
                OnNotifyRemoteFirmwareUpdateResult = Marshal.GetFunctionPointerForDelegate(_onNotifyRemoteFirmwareUpdateResultDelegate),
                OnReceivePlaybackTimeCode = Marshal.GetFunctionPointerForDelegate(_onReceivePlaybackTimeCodeDelegate),
                OnReceivePlaybackData = Marshal.GetFunctionPointerForDelegate(_onReceivePlaybackDataDelegate),
                OnNotifyMonitorUpdated = Marshal.GetFunctionPointerForDelegate(_onNotifyMonitorUpdatedDelegate)
            };
            
            // Allocate memory for vtable
            _vtablePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(VTable)));
            Marshal.StructureToPtr(vtable, _vtablePtr, false);
            _vtableHandle = GCHandle.Alloc(vtable, GCHandleType.Pinned);
            
            // Create callback object
            var callbackObj = new CallbackObject { VTablePtr = _vtablePtr };
            _callbackPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(CallbackObject)));
            Marshal.StructureToPtr(callbackObj, _callbackPtr, false);
            
            // Keep reference to this object
            _thisHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        }
        
        public IntPtr GetCallbackPtr()
        {
            return _callbackPtr;
        }
        
        public void Dispose()
        {
            if (_callbackPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_callbackPtr);
                _callbackPtr = IntPtr.Zero;
            }
            
            if (_vtablePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_vtablePtr);
                _vtablePtr = IntPtr.Zero;
            }
            
            if (_thisHandle.IsAllocated)
                _thisHandle.Free();
            
            if (_vtableHandle.IsAllocated)
                _vtableHandle.Free();
        }
        
        #region Callback Methods
        
        private void OnConnectedCallback(IntPtr thisPtr, uint version)
        {
            OnConnected?.Invoke(version);
        }
        
        private void OnDisconnectedCallback(IntPtr thisPtr, uint error)
        {
            OnDisconnected?.Invoke(error);
        }
        
        private void OnPropertyChangedCallback(IntPtr thisPtr)
        {
            OnPropertyChanged?.Invoke();
        }
        
        private void OnPropertyChangedCodesCallback(IntPtr thisPtr, uint num, IntPtr codes)
        {
            // Handle property codes if needed
            OnPropertyChanged?.Invoke();
        }
        
        private void OnLvPropertyChangedCallback(IntPtr thisPtr)
        {
            OnLvPropertyChanged?.Invoke();
        }
        
        private void OnLvPropertyChangedCodesCallback(IntPtr thisPtr, uint num, IntPtr codes)
        {
            OnLvPropertyChanged?.Invoke();
        }
        
        private void OnCompleteDownloadCallback(IntPtr thisPtr, IntPtr filename, uint type)
        {
            string file = filename != IntPtr.Zero ? Marshal.PtrToStringUni(filename) : string.Empty;
            OnCompleteDownload?.Invoke(file);
        }
        
        private void OnNotifyContentsTransferCallback(IntPtr thisPtr, uint notify, uint handle, IntPtr filename)
        {
            string file = filename != IntPtr.Zero ? Marshal.PtrToStringUni(filename) : string.Empty;
            OnNotifyContentsTransfer?.Invoke(notify, handle, file);
        }
        
        private void OnWarningCallback(IntPtr thisPtr, uint warning)
        {
            OnWarning?.Invoke(warning);
        }
        
        private void OnWarningExtCallback(IntPtr thisPtr, uint warning, int param1, int param2, int param3)
        {
            OnWarning?.Invoke(warning);
        }
        
        private void OnErrorCallback(IntPtr thisPtr, uint error)
        {
            OnError?.Invoke(error);
        }
        
        private void OnNotifyFTPTransferResultCallback(IntPtr thisPtr, uint notify, uint numOfSuccess, uint numOfFail)
        {
            // Handle FTP transfer if needed
        }
        
        private void OnNotifyRemoteTransferResultCallback(IntPtr thisPtr, uint notify, uint per, IntPtr filename)
        {
            // Handle remote transfer if needed
        }
        
        private void OnNotifyRemoteTransferResult2Callback(IntPtr thisPtr, uint notify, uint per, IntPtr data, ulong size)
        {
            // Handle remote transfer if needed
        }
        
        private void OnNotifyRemoteTransferContentsListChangedCallback(IntPtr thisPtr, uint notify, uint slotNumber, uint addSize)
        {
            // Handle contents list change if needed
            OnNotifyRemoteTransferContentsListChanged?.Invoke(notify, slotNumber, addSize);
        }
        
        private void OnNotifyRemoteFirmwareUpdateResultCallback(IntPtr thisPtr, uint notify, IntPtr param)
        {
            // Handle firmware update if needed
        }
        
        private void OnReceivePlaybackTimeCodeCallback(IntPtr thisPtr, uint timeCode)
        {
            // Handle playback time code if needed
        }
        
        private void OnReceivePlaybackDataCallback(IntPtr thisPtr, byte mediaType, int dataSize, IntPtr data, 
            long pts, long dts, int param1, int param2)
        {
            // Handle playback data if needed
        }
        
        private void OnNotifyMonitorUpdatedCallback(IntPtr thisPtr, uint type, uint frameNo)
        {
            // Handle monitor update if needed
        }
        
        #endregion
        
        #region Delegate Definitions
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnConnectedDelegate(IntPtr thisPtr, uint version);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnDisconnectedDelegate(IntPtr thisPtr, uint error);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnPropertyChangedDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnPropertyChangedCodesDelegate(IntPtr thisPtr, uint num, IntPtr codes);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnLvPropertyChangedDelegate(IntPtr thisPtr);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnLvPropertyChangedCodesDelegate(IntPtr thisPtr, uint num, IntPtr codes);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnCompleteDownloadDelegate(IntPtr thisPtr, IntPtr filename, uint type);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnNotifyContentsTransferDelegate(IntPtr thisPtr, uint notify, uint handle, IntPtr filename);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnWarningDelegate(IntPtr thisPtr, uint warning);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnWarningExtDelegate(IntPtr thisPtr, uint warning, int param1, int param2, int param3);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnErrorDelegate(IntPtr thisPtr, uint error);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnNotifyFTPTransferResultDelegate(IntPtr thisPtr, uint notify, uint numOfSuccess, uint numOfFail);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnNotifyRemoteTransferResultDelegate(IntPtr thisPtr, uint notify, uint per, IntPtr filename);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnNotifyRemoteTransferResult2Delegate(IntPtr thisPtr, uint notify, uint per, IntPtr data, ulong size);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnNotifyRemoteTransferContentsListChangedDelegate(IntPtr thisPtr, uint notify, uint slotNumber, uint addSize);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnNotifyRemoteFirmwareUpdateResultDelegate(IntPtr thisPtr, uint notify, IntPtr param);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnReceivePlaybackTimeCodeDelegate(IntPtr thisPtr, uint timeCode);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnReceivePlaybackDataDelegate(IntPtr thisPtr, byte mediaType, int dataSize, IntPtr data, 
            long pts, long dts, int param1, int param2);
        
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void OnNotifyMonitorUpdatedDelegate(IntPtr thisPtr, uint type, uint frameNo);
        
        #endregion
    }
}