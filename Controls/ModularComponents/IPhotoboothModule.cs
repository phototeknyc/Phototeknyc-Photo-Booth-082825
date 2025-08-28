using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Controls.ModularComponents
{

    /// <summary>
    /// Base interface for all photobooth module components
    /// </summary>
    public interface IPhotoboothModule
    {
        /// <summary>
        /// Module name for display and configuration
        /// </summary>
        string ModuleName { get; }
        
        /// <summary>
        /// Icon path for the module button
        /// </summary>
        string IconPath { get; }
        
        /// <summary>
        /// Whether this module is enabled in settings
        /// </summary>
        bool IsEnabled { get; set; }
        
        /// <summary>
        /// Whether the module is currently active/capturing
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Initialize the module with camera device
        /// </summary>
        void Initialize(ICameraDevice camera, string outputFolder);
        
        /// <summary>
        /// Start the module's capture process
        /// </summary>
        Task StartCapture();
        
        /// <summary>
        /// Stop/Cancel the module's capture process
        /// </summary>
        Task StopCapture();
        
        /// <summary>
        /// Clean up resources when module is disposed
        /// </summary>
        void Cleanup();
        
        /// <summary>
        /// Event raised when capture is completed
        /// </summary>
        event EventHandler<ModuleCaptureEventArgs> CaptureCompleted;
        
        /// <summary>
        /// Event raised when module status changes
        /// </summary>
        event EventHandler<ModuleStatusEventArgs> StatusChanged;
    }
    
    /// <summary>
    /// Event args for capture completion
    /// </summary>
    public class ModuleCaptureEventArgs : EventArgs
    {
        public string OutputPath { get; set; }
        public string ModuleName { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public object Data { get; set; } // Additional module-specific data
    }
    
    /// <summary>
    /// Event args for status changes
    /// </summary>
    public class ModuleStatusEventArgs : EventArgs
    {
        public string Status { get; set; }
        public int Progress { get; set; } // 0-100
        public string Message { get; set; }
    }
    
    /// <summary>
    /// Base class with common functionality for all modules
    /// </summary>
    public abstract class PhotoboothModuleBase : IPhotoboothModule
    {
        protected ICameraDevice _camera;
        protected CameraDeviceManager _deviceManager;
        protected string _outputFolder;
        protected bool _isActive;
        
        public abstract string ModuleName { get; }
        public abstract string IconPath { get; }
        
        public bool IsEnabled { get; set; }
        public bool IsActive => _isActive;
        
        public event EventHandler<ModuleCaptureEventArgs> CaptureCompleted;
        public event EventHandler<ModuleStatusEventArgs> StatusChanged;
        
        public virtual void Initialize(ICameraDevice camera, string outputFolder)
        {
            _camera = camera;
            _outputFolder = outputFolder;
            // Get the device manager from CameraSessionManager
            _deviceManager = Services.CameraSessionManager.Instance.DeviceManager;
        }
        
        public abstract Task StartCapture();
        public abstract Task StopCapture();
        
        public virtual void Cleanup()
        {
            _camera = null;
            _isActive = false;
        }
        
        protected virtual void OnCaptureCompleted(ModuleCaptureEventArgs e)
        {
            CaptureCompleted?.Invoke(this, e);
        }
        
        protected virtual void OnStatusChanged(ModuleStatusEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }
        
        protected void UpdateStatus(string status, int progress = 0, string message = null)
        {
            OnStatusChanged(new ModuleStatusEventArgs 
            { 
                Status = status, 
                Progress = progress, 
                Message = message 
            });
        }
    }
}