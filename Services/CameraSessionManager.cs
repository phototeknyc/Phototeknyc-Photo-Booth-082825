using System;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Singleton manager to maintain camera session across the application
    /// Prevents Canon camera session corruption when switching between screens
    /// </summary>
    public class CameraSessionManager
    {
        private static CameraSessionManager _instance;
        private static readonly object _lock = new object();
        private CameraDeviceManager _deviceManager;
        private bool _isInitialized = false;

        private CameraSessionManager()
        {
            // Private constructor for singleton
        }

        public static CameraSessionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new CameraSessionManager();
                        }
                    }
                }
                return _instance;
            }
        }

        public CameraDeviceManager DeviceManager
        {
            get
            {
                EnsureInitialized();
                return _deviceManager;
            }
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                lock (_lock)
                {
                    if (!_isInitialized)
                    {
                        DebugService.LogDebug("CameraSessionManager: Initializing device manager");
                        
                        _deviceManager = new CameraDeviceManager();
                        _deviceManager.UseExperimentalDrivers = true;
                        _deviceManager.DisableNativeDrivers = false;
                        
                        // Connect to cameras
                        _deviceManager.ConnectToCamera();
                        
                        _isInitialized = true;
                        
                        DebugService.LogDebug($"CameraSessionManager: Initialized with {_deviceManager.ConnectedDevices.Count} devices");
                    }
                }
            }
        }

        /// <summary>
        /// Reset camera state without destroying the session
        /// </summary>
        public void ResetCameraState()
        {
            try
            {
                if (_deviceManager?.SelectedCameraDevice != null)
                {
                    DebugService.LogDebug("CameraSessionManager: Resetting camera state");
                    
                    // Stop live view if running
                    try
                    {
                        _deviceManager.SelectedCameraDevice.StopLiveView();
                    }
                    catch { }
                    
                    // Reset busy flag
                    _deviceManager.SelectedCameraDevice.IsBusy = false;
                    
                    DebugService.LogDebug("CameraSessionManager: Camera state reset complete");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"CameraSessionManager: Error resetting camera state: {ex.Message}");
            }
        }

        /// <summary>
        /// Prepare camera for use (called when entering a screen that uses camera)
        /// </summary>
        public void PrepareCameraForUse()
        {
            try
            {
                EnsureInitialized();
                ResetCameraState();
                
                if (_deviceManager?.SelectedCameraDevice != null)
                {
                    DebugService.LogDebug($"CameraSessionManager: Camera ready - {_deviceManager.SelectedCameraDevice.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"CameraSessionManager: Error preparing camera: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up camera when leaving a screen (but don't destroy session)
        /// </summary>
        public void CleanupCameraForScreenChange()
        {
            try
            {
                if (_deviceManager?.SelectedCameraDevice != null)
                {
                    DebugService.LogDebug("CameraSessionManager: Cleaning up for screen change");
                    
                    // Stop live view
                    try
                    {
                        _deviceManager.SelectedCameraDevice.StopLiveView();
                    }
                    catch { }
                    
                    // Reset busy flag but keep session alive
                    _deviceManager.SelectedCameraDevice.IsBusy = false;
                    
                    DebugService.LogDebug("CameraSessionManager: Cleanup complete, session maintained");
                }
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"CameraSessionManager: Error during cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Force reconnect camera (only use if session is truly broken)
        /// </summary>
        public void ForceReconnect()
        {
            try
            {
                DebugService.LogDebug("CameraSessionManager: Force reconnect requested");
                
                // Close existing session
                if (_deviceManager?.SelectedCameraDevice != null)
                {
                    try
                    {
                        _deviceManager.SelectedCameraDevice.Close();
                    }
                    catch { }
                }
                
                // Reset initialization flag
                _isInitialized = false;
                _deviceManager = null;
                
                // Reinitialize
                EnsureInitialized();
                
                DebugService.LogDebug("CameraSessionManager: Force reconnect complete");
            }
            catch (Exception ex)
            {
                DebugService.LogDebug($"CameraSessionManager: Force reconnect failed: {ex.Message}");
            }
        }
    }
}