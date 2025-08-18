using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CameraControl.Devices.Classes;
using PortableDeviceLib;
using static CameraControl.Devices.Sony.SonySDKWrapper;

namespace CameraControl.Devices.Sony
{
    public class SonyUSBCamera : BaseCameraDevice
    {
        private IntPtr _deviceHandle = IntPtr.Zero;
        private IntPtr _cameraInfo = IntPtr.Zero;
        private LiveViewData _liveViewData = new LiveViewData();
        private bool _liveViewRunning = false;
        private Thread _liveViewThread;
        private bool _shouldStopLiveView = false;
        private readonly object _lockObject = new object();
        private static bool _sdkInitialized = false;
        private LiveViewCallback _liveViewCallback;
        private PropertyChangedCallback _propertyCallback;
        private ObjectAddedCallback _objectAddedCallback;

        public SonyUSBCamera()
        {
            Capabilities.Add(CapabilityEnum.LiveView);
            Capabilities.Add(CapabilityEnum.RecordMovie);
            Capabilities.Add(CapabilityEnum.Zoom);
        }

        ~SonyUSBCamera()
        {
            try
            {
                if (_deviceHandle != IntPtr.Zero)
                {
                    Close();
                }
            }
            catch { }
        }

        public override bool Init(DeviceDescriptor deviceDescriptor)
        {
            try
            {
                // Initialize SDK if not already done
                if (!_sdkInitialized)
                {
                    if (!SonySDKWrapper.Init())
                    {
                        Log.Error("Failed to initialize Sony SDK");
                        return false;
                    }
                    _sdkInitialized = true;
                }

                // Store camera info for connection
                _cameraInfo = deviceDescriptor.GetSonyDeviceInfo();
                
                // Set up callbacks
                SetupCallbacks();

                // Connect to camera
                var result = SonySDKWrapper.Connect(
                    _cameraInfo,
                    IntPtr.Zero, // We'll implement callback interface later
                    out _deviceHandle,
                    CrSdkControlMode.CrSdkControlMode_Remote,
                    CrReconnectingSet.CrReconnecting_ON);

                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to connect to Sony camera: {GetErrorMessage(result)}");
                    return false;
                }

                DeviceName = deviceDescriptor.GetSonyModelName();
                SerialNumber = deviceDescriptor.SerialNumber;
                IsConnected = true;

                // Initialize properties
                InitializeProperties();
                
                // Get initial device properties
                RefreshDeviceProperties();

                Log.Debug($"Sony USB camera connected: {DeviceName}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Error initializing Sony USB camera", ex);
                return false;
            }
        }

        private void SetupCallbacks()
        {
            _liveViewCallback = OnLiveViewData;
            _propertyCallback = OnPropertyChanged;
            _objectAddedCallback = OnObjectAdded;
        }

        private void InitializeProperties()
        {
            // ISO
            IsoNumber = new PropertyValue<long>();
            IsoNumber.ValueChanged += (sender, key, val) => SetPropertyValue(CrDevicePropertyCode.CrDevicePropertyCode_IsoSensitivity, val);

            // Shutter Speed
            ShutterSpeed = new PropertyValue<long>();
            ShutterSpeed.ValueChanged += (sender, key, val) => SetPropertyValue(CrDevicePropertyCode.CrDevicePropertyCode_ShutterSpeed, val);

            // F-Number
            FNumber = new PropertyValue<long>();
            FNumber.ValueChanged += (sender, key, val) => SetPropertyValue(CrDevicePropertyCode.CrDevicePropertyCode_FNumber, val);

            // Exposure Mode
            Mode = new PropertyValue<long>();
            Mode.ValueChanged += (sender, key, val) => SetPropertyValue(CrDevicePropertyCode.CrDevicePropertyCode_ExposureProgramMode, val);

            // White Balance
            WhiteBalance = new PropertyValue<long>();
            WhiteBalance.ValueChanged += (sender, key, val) => SetPropertyValue(CrDevicePropertyCode.CrDevicePropertyCode_WhiteBalance, val);

            // Focus Mode
            FocusMode = new PropertyValue<long>();
            FocusMode.ValueChanged += (sender, key, val) => SetPropertyValue(CrDevicePropertyCode.CrDevicePropertyCode_FocusMode, val);

            // Compression/Quality
            CompressionSetting = new PropertyValue<long>();
            CompressionSetting.ValueChanged += (sender, key, val) => SetPropertyValue(CrDevicePropertyCode.CrDevicePropertyCode_CompressionFileFormat, val);

            // Battery Level is handled by base class as int

            // Exposure Compensation
            ExposureCompensation = new PropertyValue<long>();

            // Live View Zoom
            LiveViewImageZoomRatio = new PropertyValue<long>();
        }

        private void RefreshDeviceProperties()
        {
            try
            {
                IntPtr properties;
                int numProperties;
                
                var result = SonySDKWrapper.GetDeviceProperties(_deviceHandle, out properties, out numProperties);
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to get device properties: {GetErrorMessage(result)}");
                    return;
                }

                // Parse properties
                int propertySize = Marshal.SizeOf(typeof(CrDeviceProperty));
                for (int i = 0; i < numProperties; i++)
                {
                    IntPtr propertyPtr = new IntPtr(properties.ToInt64() + (i * propertySize));
                    CrDeviceProperty property = (CrDeviceProperty)Marshal.PtrToStructure(propertyPtr, typeof(CrDeviceProperty));
                    
                    UpdatePropertyFromDevice(property);
                }

                // Free the properties memory (SDK should provide a function for this)
                Marshal.FreeHGlobal(properties);
            }
            catch (Exception ex)
            {
                Log.Error("Error refreshing device properties", ex);
            }
        }

        private void UpdatePropertyFromDevice(CrDeviceProperty property)
        {
            try
            {
                PropertyValue<long> targetProperty = null;
                
                switch (property.PropertyCode)
                {
                    case CrDevicePropertyCode.CrDevicePropertyCode_IsoSensitivity:
                        targetProperty = IsoNumber;
                        break;
                    case CrDevicePropertyCode.CrDevicePropertyCode_ShutterSpeed:
                        targetProperty = ShutterSpeed;
                        break;
                    case CrDevicePropertyCode.CrDevicePropertyCode_FNumber:
                        targetProperty = FNumber;
                        break;
                    case CrDevicePropertyCode.CrDevicePropertyCode_ExposureProgramMode:
                        targetProperty = Mode;
                        break;
                    case CrDevicePropertyCode.CrDevicePropertyCode_WhiteBalance:
                        targetProperty = WhiteBalance;
                        break;
                    case CrDevicePropertyCode.CrDevicePropertyCode_FocusMode:
                        targetProperty = FocusMode;
                        break;
                    case CrDevicePropertyCode.CrDevicePropertyCode_CompressionFileFormat:
                        targetProperty = CompressionSetting;
                        break;
                    case CrDevicePropertyCode.CrDevicePropertyCode_BatteryLevel:
                        // Battery is an int property in base class, handle separately
                        Battery = (int)property.CurrentValue;
                        return;
                }

                if (targetProperty != null)
                {
                    // Clear existing values
                    targetProperty.Clear(false);
                    
                    // Add available values
                    if (property.NumValues > 0 && property.Values != IntPtr.Zero)
                    {
                        int valueSize = (int)property.ValueSize;
                        for (int i = 0; i < property.NumValues; i++)
                        {
                            IntPtr valuePtr = new IntPtr(property.Values.ToInt64() + (i * valueSize));
                            uint value = (uint)Marshal.ReadInt32(valuePtr);
                            string displayValue = ConvertPropertyValueToString(property.PropertyCode, value);
                            targetProperty.AddValues(displayValue, (long)value);
                        }
                    }
                    
                    // Set current value
                    targetProperty.SetValue(ConvertPropertyValueToString(property.PropertyCode, property.CurrentValue), false);
                    targetProperty.IsEnabled = property.SetEnableStatus > 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating property {property.PropertyCode}", ex);
            }
        }

        private string ConvertPropertyValueToString(CrDevicePropertyCode propertyCode, uint value)
        {
            // Convert Sony property values to display strings
            // This would need proper mapping based on Sony's documentation
            switch (propertyCode)
            {
                case CrDevicePropertyCode.CrDevicePropertyCode_IsoSensitivity:
                    return ConvertIsoValue(value);
                case CrDevicePropertyCode.CrDevicePropertyCode_ShutterSpeed:
                    return ConvertShutterSpeedValue(value);
                case CrDevicePropertyCode.CrDevicePropertyCode_FNumber:
                    return ConvertFNumberValue(value);
                case CrDevicePropertyCode.CrDevicePropertyCode_ExposureProgramMode:
                    return ConvertExposureModeValue(value);
                case CrDevicePropertyCode.CrDevicePropertyCode_WhiteBalance:
                    return ConvertWhiteBalanceValue(value);
                case CrDevicePropertyCode.CrDevicePropertyCode_FocusMode:
                    return ConvertFocusModeValue(value);
                default:
                    return value.ToString();
            }
        }

        private string ConvertIsoValue(uint value)
        {
            // Common ISO values - would need Sony's specific mapping
            Dictionary<uint, string> isoMap = new Dictionary<uint, string>
            {
                {0x00000032, "50"},
                {0x00000040, "64"},
                {0x00000050, "80"},
                {0x00000064, "100"},
                {0x0000007D, "125"},
                {0x000000A0, "160"},
                {0x000000C8, "200"},
                {0x000000FA, "250"},
                {0x00000140, "320"},
                {0x00000190, "400"},
                {0x000001F4, "500"},
                {0x00000280, "640"},
                {0x00000320, "800"},
                {0x00000400, "1000"},
                {0x00000500, "1250"},
                {0x00000640, "1600"},
                {0x00000800, "2000"},
                {0x00000A00, "2500"},
                {0x00000C80, "3200"},
                {0x00001900, "6400"},
                {0x00003200, "12800"},
                {0x00006400, "25600"},
                {0x0000C800, "51200"},
                {0x00019000, "102400"}
            };
            
            return isoMap.ContainsKey(value) ? isoMap[value] : $"ISO {value}";
        }

        private string ConvertShutterSpeedValue(uint value)
        {
            // Would need Sony's specific shutter speed mapping
            // This is a simplified example
            if (value == 0) return "Bulb";
            if (value < 10000) return $"1/{10000/value}";
            return $"{value/10000}\"";
        }

        private string ConvertFNumberValue(uint value)
        {
            // F-number is typically stored as value * 100
            double fNumber = value / 100.0;
            return $"f/{fNumber:F1}";
        }

        private string ConvertExposureModeValue(uint value)
        {
            Dictionary<uint, string> modeMap = new Dictionary<uint, string>
            {
                {0x00000001, "Manual"},
                {0x00000002, "Program Auto"},
                {0x00000003, "Aperture Priority"},
                {0x00000004, "Shutter Priority"},
                {0x00000005, "Creative Auto"},
                {0x00000006, "Action"},
                {0x00000007, "Portrait"},
                {0x00000008, "Landscape"},
                {0x00000009, "Macro"},
                {0x0000000A, "Sport"},
                {0x0000000B, "Night"},
                {0x0000000C, "Auto"},
                {0x0000000D, "Superior Auto"}
            };
            
            return modeMap.ContainsKey(value) ? modeMap[value] : $"Mode {value}";
        }

        private string ConvertWhiteBalanceValue(uint value)
        {
            Dictionary<uint, string> wbMap = new Dictionary<uint, string>
            {
                {0x00000001, "Auto"},
                {0x00000002, "Daylight"},
                {0x00000003, "Cloudy"},
                {0x00000004, "Tungsten"},
                {0x00000005, "Fluorescent"},
                {0x00000006, "Flash"},
                {0x00000007, "Shade"},
                {0x00000008, "Custom"},
                {0x00000009, "Kelvin"}
            };
            
            return wbMap.ContainsKey(value) ? wbMap[value] : $"WB {value}";
        }

        private string ConvertFocusModeValue(uint value)
        {
            Dictionary<uint, string> focusMap = new Dictionary<uint, string>
            {
                {0x00000001, "Manual"},
                {0x00000002, "Single AF"},
                {0x00000003, "Continuous AF"},
                {0x00000004, "Automatic AF"},
                {0x00000005, "DMF"}
            };
            
            return focusMap.ContainsKey(value) ? focusMap[value] : $"Focus {value}";
        }

        private void SetPropertyValue(CrDevicePropertyCode propertyCode, long value)
        {
            try
            {
                CrDeviceProperty property = new CrDeviceProperty
                {
                    PropertyCode = propertyCode,
                    CurrentValue = (uint)value,
                    ValueSize = 4
                };

                var result = SonySDKWrapper.SetDeviceProperty(_deviceHandle, ref property);
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to set property {propertyCode}: {GetErrorMessage(result)}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error setting property {propertyCode}", ex);
            }
        }

        public override void CapturePhoto()
        {
            try
            {
                IsBusy = true;
                
                var result = SonySDKWrapper.SendCommand(_deviceHandle, CrCommandId.CrCommandId_Release);
                if (result != CrError.CrError_None)
                {
                    throw new Exception($"Capture failed: {GetErrorMessage(result)}");
                }
                
                // The photo capture event will be handled by the callback
            }
            catch (Exception ex)
            {
                Log.Error("Error capturing photo", ex);
                IsBusy = false;
                throw;
            }
        }

        public override void CapturePhotoNoAf()
        {
            // For Sony cameras, we might need to temporarily disable AF
            CapturePhoto();
        }

        public override void StartLiveView()
        {
            try
            {
                if (_liveViewRunning)
                    return;

                _shouldStopLiveView = false;
                
                // Start live view with callback
                var result = SonySDKWrapper.StartLiveView(_deviceHandle, Marshal.GetFunctionPointerForDelegate(_liveViewCallback));
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to start live view: {GetErrorMessage(result)}");
                    return;
                }

                _liveViewRunning = true;
                _liveViewData.IsLiveViewRunning = true;

                // Start live view thread
                _liveViewThread = new Thread(LiveViewThread) { IsBackground = true };
                _liveViewThread.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Error starting live view", ex);
            }
        }

        public override void StopLiveView()
        {
            try
            {
                if (!_liveViewRunning)
                    return;

                _shouldStopLiveView = true;
                
                var result = SonySDKWrapper.StopLiveView(_deviceHandle);
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to stop live view: {GetErrorMessage(result)}");
                }

                _liveViewRunning = false;
                _liveViewData.IsLiveViewRunning = false;

                // Wait for thread to stop
                if (_liveViewThread != null)
                {
                    _liveViewThread.Join(1000);
                    _liveViewThread = null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error stopping live view", ex);
            }
        }

        private void LiveViewThread()
        {
            while (!_shouldStopLiveView && _liveViewRunning)
            {
                try
                {
                    CrImageDataBlock imageData;
                    var result = SonySDKWrapper.GetLiveViewImage(_deviceHandle, out imageData);
                    
                    if (result == CrError.CrError_None && imageData.Data != IntPtr.Zero && imageData.Size > 0)
                    {
                        lock (_lockObject)
                        {
                            // Copy image data
                            _liveViewData.ImageData = new byte[imageData.Size];
                            Marshal.Copy(imageData.Data, _liveViewData.ImageData, 0, (int)imageData.Size);
                            _liveViewData.IsLiveViewRunning = true;
                        }
                    }
                    
                    Thread.Sleep(30); // ~30 fps
                }
                catch (Exception ex)
                {
                    Log.Error("Error in live view thread", ex);
                }
            }
        }

        public override LiveViewData GetLiveViewImage()
        {
            lock (_lockObject)
            {
                return _liveViewData;
            }
        }

        public override void StartRecordMovie()
        {
            try
            {
                var result = SonySDKWrapper.SendCommand(_deviceHandle, CrCommandId.CrCommandId_MovieRecord);
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to start movie recording: {GetErrorMessage(result)}");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error starting movie recording", ex);
            }
        }

        public override void StopRecordMovie()
        {
            try
            {
                var result = SonySDKWrapper.SendCommand(_deviceHandle, CrCommandId.CrCommandId_MovieRecord);
                if (result != CrError.CrError_None)
                {
                    Log.Error($"Failed to stop movie recording: {GetErrorMessage(result)}");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error stopping movie recording", ex);
            }
        }

        public override void Focus(int x, int y)
        {
            // Would need to implement focus point setting via SDK
            Log.Debug($"Focus at {x}, {y} - not yet implemented for Sony USB");
        }

        public override void AutoFocus()
        {
            try
            {
                // Trigger AF - implementation depends on camera model
                Log.Debug("Auto focus triggered");
            }
            catch (Exception ex)
            {
                Log.Error("Error in auto focus", ex);
            }
        }

        public override void TransferFile(object o, string filename)
        {
            try
            {
                TransferProgress = 0;
                
                if (o is IntPtr)
                {
                    var result = SonySDKWrapper.DownloadFile(_deviceHandle, (IntPtr)o, filename);
                    if (result != CrError.CrError_None)
                    {
                        Log.Error($"Failed to download file: {GetErrorMessage(result)}");
                    }
                }
                
                TransferProgress = 100;
            }
            catch (Exception ex)
            {
                Log.Error("Error transferring file", ex);
            }
        }

        public override void Close()
        {
            try
            {
                if (_liveViewRunning)
                {
                    StopLiveView();
                }

                if (_deviceHandle != IntPtr.Zero)
                {
                    SonySDKWrapper.Disconnect(_deviceHandle);
                    SonySDKWrapper.ReleaseDevice(_deviceHandle);
                    _deviceHandle = IntPtr.Zero;
                }

                IsConnected = false;
            }
            catch (Exception ex)
            {
                Log.Error("Error closing Sony USB camera", ex);
            }
        }

        // Callback methods
        private void OnLiveViewData(IntPtr deviceHandle, IntPtr imageData)
        {
            // Handle live view data from callback
            try
            {
                if (imageData != IntPtr.Zero)
                {
                    CrImageDataBlock dataBlock = (CrImageDataBlock)Marshal.PtrToStructure(imageData, typeof(CrImageDataBlock));
                    
                    lock (_lockObject)
                    {
                        _liveViewData.ImageData = new byte[dataBlock.Size];
                        Marshal.Copy(dataBlock.Data, _liveViewData.ImageData, 0, (int)dataBlock.Size);
                        _liveViewData.IsLiveViewRunning = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error in live view callback", ex);
            }
        }

        private void OnPropertyChanged(IntPtr deviceHandle, IntPtr properties, int count)
        {
            // Handle property change notifications
            try
            {
                int propertySize = Marshal.SizeOf(typeof(CrDeviceProperty));
                for (int i = 0; i < count; i++)
                {
                    IntPtr propertyPtr = new IntPtr(properties.ToInt64() + (i * propertySize));
                    CrDeviceProperty property = (CrDeviceProperty)Marshal.PtrToStructure(propertyPtr, typeof(CrDeviceProperty));
                    UpdatePropertyFromDevice(property);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error in property changed callback", ex);
            }
        }

        private void OnObjectAdded(IntPtr deviceHandle, IntPtr objectInfo)
        {
            // Handle new photo/object notifications
            try
            {
                // Parse object info and trigger photo captured event
                PhotoCapturedEventArgs args = new PhotoCapturedEventArgs
                {
                    CameraDevice = this,
                    FileName = "Sony_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg",
                    Handle = objectInfo
                };
                
                OnPhotoCapture(this, args);
                IsBusy = false;
            }
            catch (Exception ex)
            {
                Log.Error("Error in object added callback", ex);
                IsBusy = false;
            }
        }

        public override string ToString()
        {
            return $"{base.ToString()}\n\tType: Sony USB Camera\n\tHandle: {_deviceHandle}";
        }
    }
}