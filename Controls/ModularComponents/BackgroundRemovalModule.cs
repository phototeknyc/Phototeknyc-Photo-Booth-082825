using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using Photobooth.Services;

namespace Photobooth.Controls.ModularComponents
{
    /// <summary>
    /// Background Removal Module - Uses AI/ML to remove backgrounds and apply virtual backgrounds
    /// </summary>
    public class BackgroundRemovalModule : PhotoboothModuleBase
    {
        #region Properties

        public override string ModuleName => "Background Removal";
        public override string IconPath => "/Resources/Icons/background_removal.png";
        public string Description => "AI-powered background removal with virtual backgrounds";
        public string Icon => "🎭";
        public bool RequiresCamera => true;
        public int CaptureCount => 4; // Default to 4 photos
        public TimeSpan CaptureDelay => TimeSpan.FromSeconds(3); // Default to 3 seconds

        #endregion

        #region Private Fields

        private BackgroundRemovalService _removalService;
        private VirtualBackgroundService _backgroundService;
        private bool _isProcessing;
        private string _currentSessionId;
        private List<string> _capturedPhotos;
        private string _selectedBackground;
        private bool _liveViewRemovalEnabled;

        #endregion

        #region Events

        public event EventHandler<CaptureEventArgs> PhotoCaptured;
        public event EventHandler<SessionCompletedEventArgs> SessionCompleted;
        public event EventHandler<ErrorEventArgs> ErrorOccurred;

        #endregion

        #region Constructor

        public BackgroundRemovalModule()
        {
            _capturedPhotos = new List<string>();
            _liveViewRemovalEnabled = Properties.Settings.Default.EnableLiveViewBackgroundRemoval;
        }

        #endregion

        #region IPhotoboothModule Implementation

        public override void Initialize(ICameraDevice camera, string outputFolder)
        {
            try
            {
                _camera = camera;
                _outputFolder = outputFolder;

                // Initialize services
                _removalService = BackgroundRemovalService.Instance;
                _backgroundService = VirtualBackgroundService.Instance;

                // Load ML models asynchronously but don't await
                Task.Run(async () =>
                {
                    await _removalService.InitializeAsync();
                    await _backgroundService.LoadBackgroundsAsync();
                });

                // Set default background
                _selectedBackground = _backgroundService.GetDefaultBackground();

                Log.Debug($"BackgroundRemovalModule initialized with camera: {camera?.DeviceName}");

                base.Initialize(camera, outputFolder);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize BackgroundRemovalModule: {ex.Message}");
                ErrorOccurred?.Invoke(this, new ErrorEventArgs { Error = ex });
            }
        }

        public override async Task StartCapture()
        {
            await StartSession(Guid.NewGuid().ToString());
        }

        public override async Task StopCapture()
        {
            await EndSession();
        }

        public async Task<bool> StartSession(string sessionId)
        {
            try
            {
                if (_isProcessing)
                {
                    Log.Debug("BackgroundRemovalModule: Session already in progress");
                    return false;
                }

                _currentSessionId = sessionId;
                _capturedPhotos.Clear();
                _isProcessing = true;
                _isActive = true;

                // Start live view with background removal if enabled
                if (_liveViewRemovalEnabled && _camera != null)
                {
                    StartLiveViewBackgroundRemoval();
                }

                UpdateStatus("Session started with background removal", 0);

                Log.Debug($"BackgroundRemovalModule: Session {sessionId} started");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start background removal session: {ex.Message}");
                ErrorOccurred?.Invoke(this, new ErrorEventArgs { Error = ex });
                return false;
            }
        }

        public async Task<string> CapturePhoto()
        {
            try
            {
                if (!_isProcessing || _camera == null)
                {
                    throw new InvalidOperationException("Session not active or camera not initialized");
                }

                UpdateStatus("Capturing photo...", 25);

                // Capture photo from camera
                _camera.CapturePhoto();
                await Task.Delay(100); // Wait for camera to complete

                // For now, use a placeholder path - in production, track the output folder
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var captureResult = Path.Combine(_outputFolder, $"capture_{timestamp}.jpg");

                // Check if the camera saved a file (simplified check)
                if (!Directory.GetFiles(_outputFolder, "*.jpg").Any())
                {
                    throw new Exception("Failed to capture photo from camera");
                }

                // Get the most recent file
                var files = Directory.GetFiles(_outputFolder, "*.jpg")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToArray();

                if (files.Length > 0)
                {
                    captureResult = files[0];
                }

                // Process the captured image
                string processedPath = await ProcessCapturedPhoto(captureResult);

                _capturedPhotos.Add(processedPath);

                PhotoCaptured?.Invoke(this, new CaptureEventArgs
                {
                    PhotoPath = processedPath,
                    PhotoNumber = _capturedPhotos.Count,
                    Success = true
                });

                Log.Debug($"Photo captured and processed with background removal: {processedPath}");
                return processedPath;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to capture photo with background removal: {ex.Message}");
                ErrorOccurred?.Invoke(this, new ErrorEventArgs { Error = ex });
                throw;
            }
        }

        public async Task<bool> EndSession()
        {
            try
            {
                if (!_isProcessing)
                {
                    return false;
                }

                // Stop live view background removal
                StopLiveViewBackgroundRemoval();

                _isProcessing = false;
                _isActive = false;

                SessionCompleted?.Invoke(this, new SessionCompletedEventArgs
                {
                    SessionId = _currentSessionId,
                    ProcessedPhotos = _capturedPhotos,
                    Success = true
                });

                UpdateStatus("Session completed", 100);

                Log.Debug($"BackgroundRemovalModule: Session {_currentSessionId} ended");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to end background removal session: {ex.Message}");
                ErrorOccurred?.Invoke(this, new ErrorEventArgs { Error = ex });
                return false;
            }
        }

        public override void Cleanup()
        {
            try
            {
                StopLiveViewBackgroundRemoval();
                _removalService?.Dispose();
                _backgroundService?.Dispose();
                _capturedPhotos.Clear();
                _currentSessionId = null;
                _isProcessing = false;
                _isActive = false;

                Log.Debug("BackgroundRemovalModule cleaned up");
            }
            catch (Exception ex)
            {
                Log.Error($"Error during BackgroundRemovalModule cleanup: {ex.Message}");
            }
        }

        public Control GetConfigurationUI()
        {
            // Return configuration UI for background selection
            return new BackgroundSelectionControl(this);
        }

        public Dictionary<string, object> GetConfiguration()
        {
            return new Dictionary<string, object>
            {
                { "SelectedBackground", _selectedBackground },
                { "LiveViewRemovalEnabled", _liveViewRemovalEnabled },
                { "EdgeRefinement", Properties.Settings.Default.BackgroundRemovalEdgeRefinement },
                { "ProcessingQuality", Properties.Settings.Default.BackgroundRemovalQuality }
            };
        }

        public void ApplyConfiguration(Dictionary<string, object> config)
        {
            if (config.ContainsKey("SelectedBackground"))
                _selectedBackground = config["SelectedBackground"].ToString();

            if (config.ContainsKey("LiveViewRemovalEnabled"))
                _liveViewRemovalEnabled = (bool)config["LiveViewRemovalEnabled"];
        }

        #endregion

        #region Background Processing

        private async Task<string> ProcessCapturedPhoto(string originalPath)
        {
            try
            {
                UpdateStatus("Removing background...", 50);

                // Remove background using ML service
                var result = await _removalService.RemoveBackgroundAsync(originalPath,
                    BackgroundRemovalQuality.High);

                if (!result.Success)
                {
                    throw new Exception($"Background removal failed: {result.ErrorMessage}");
                }

                UpdateStatus("Applying virtual background...", 75);

                // Apply virtual background
                string finalPath = await _backgroundService.ApplyBackgroundAsync(
                    result.ProcessedImagePath,
                    result.MaskPath,
                    _selectedBackground,
                    _outputFolder);

                // Clean up intermediate files
                if (File.Exists(result.ProcessedImagePath))
                    File.Delete(result.ProcessedImagePath);
                if (File.Exists(result.MaskPath))
                    File.Delete(result.MaskPath);

                return finalPath;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to process photo with background removal: {ex.Message}");
                // Return original if processing fails
                return originalPath;
            }
        }

        #endregion

        #region Live View Background Removal

        private void StartLiveViewBackgroundRemoval()
        {
            try
            {
                if (!_liveViewRemovalEnabled || _camera == null)
                    return;

                // Subscribe to live view updates - not available in current camera interface
                // _camera.LiveViewUpdated += OnLiveViewUpdated;

                Log.Debug("Live view background removal started");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start live view background removal: {ex.Message}");
            }
        }

        private void StopLiveViewBackgroundRemoval()
        {
            try
            {
                // Unsubscribe from live view updates - not available in current camera interface
                // if (_camera != null)
                // {
                //     _camera.LiveViewUpdated -= OnLiveViewUpdated;
                // }

                Log.Debug("Live view background removal stopped");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to stop live view background removal: {ex.Message}");
            }
        }

        // Live view processing disabled until camera interface supports it
        /*
        private async void OnLiveViewUpdated(object sender, LiveViewEventArgs e)
        {
            try
            {
                if (!_liveViewRemovalEnabled || e.ImageData == null)
                    return;

                // Process live view frame with background removal
                var processedFrame = await _removalService.ProcessLiveViewFrameAsync(
                    e.ImageData,
                    e.Width,
                    e.Height);

                if (processedFrame != null)
                {
                    // Update live view with processed frame
                    e.ImageData = processedFrame;
                }
            }
            catch (Exception ex)
            {
                // Don't log every frame error to avoid spam
                if (DateTime.Now.Second % 10 == 0)
                {
                    Log.Debug($"Live view background removal error: {ex.Message}");
                }
            }
        }
        */

        #endregion

        #region Public Methods

        public void SetVirtualBackground(string backgroundPath)
        {
            _selectedBackground = backgroundPath;
            Log.Debug($"Virtual background changed to: {backgroundPath}");

            // Notify status change through base class
            UpdateStatus("Background changed", 0);
        }

        public void EnableLiveViewRemoval(bool enable)
        {
            _liveViewRemovalEnabled = enable;

            if (_isProcessing)
            {
                if (enable)
                    StartLiveViewBackgroundRemoval();
                else
                    StopLiveViewBackgroundRemoval();
            }
        }

        #endregion
    }

    #region Event Args Classes

    public class LiveViewEventArgs : EventArgs
    {
        public byte[] ImageData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class CaptureEventArgs : EventArgs
    {
        public string PhotoPath { get; set; }
        public int PhotoNumber { get; set; }
        public bool Success { get; set; }
    }

    public class SessionCompletedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public List<string> ProcessedPhotos { get; set; }
        public bool Success { get; set; }
    }

    public class ErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
    }

    #endregion
}