using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Clean service that orchestrates the complete photobooth workflow
    /// Handles camera operations, countdown, capture sequencing, and coordination
    /// </summary>
    public class PhotoboothWorkflowService
    {
        #region Events
        public event EventHandler<CountdownEventArgs> CountdownStarted;
        public event EventHandler<CountdownEventArgs> CountdownTick;
        public event EventHandler CountdownCompleted;
        public event EventHandler<CaptureEventArgs> CaptureStarted;
        public event EventHandler<CaptureEventArgs> CaptureCompleted;
        public event EventHandler<PhotoDisplayEventArgs> PhotoDisplayRequested;
        public event EventHandler PhotoDisplayCompleted;
        public event EventHandler<WorkflowErrorEventArgs> WorkflowError;
        public event EventHandler<StatusEventArgs> StatusChanged;
        #endregion

        #region Services
        private readonly CameraSessionManager _cameraManager;
        private readonly PhotoboothSessionService _sessionService;
        #endregion

        #region Timers
        private DispatcherTimer _countdownTimer;
        private int _countdownValue;
        #endregion

        #region State
        private bool _isCapturing;
        private bool _isCountdownActive;
        private PhotoCapturedEventHandler _cameraCaptureHandler;
        #endregion

        #region Properties
        public ICameraDevice CurrentCamera => _cameraManager?.DeviceManager?.SelectedCameraDevice;
        public bool IsCapturing => _isCapturing;
        public bool IsCountdownActive => _isCountdownActive;
        #endregion

        public PhotoboothWorkflowService(CameraSessionManager cameraManager, PhotoboothSessionService sessionService)
        {
            _cameraManager = cameraManager ?? throw new ArgumentNullException(nameof(cameraManager));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));

            InitializeCountdownTimer();
            SetupCameraEventHandler();
            
            Log.Debug("===== PhotoboothWorkflowService INITIALIZED =====");
            Log.Debug($"Camera manager: {(_cameraManager != null ? "Available" : "NULL")}");
            Log.Debug($"Session service: {(_sessionService != null ? "Available" : "NULL")}");
            Log.Debug($"Camera capture handler: {(_cameraCaptureHandler != null ? "SET" : "NULL")}");
            
            // Subscribe to session events
            _sessionService.PhotoProcessed += OnSessionPhotoProcessed;
            _sessionService.SessionCompleted += OnSessionCompleted;
        }
        
        private void SetupCameraEventHandler()
        {
            // Create a persistent handler for camera photo capture
            _cameraCaptureHandler = (sender, args) =>
            {
                Log.Debug("===== CAMERA PHOTO CAPTURED EVENT FIRED =====");
                Log.Debug($"PhotoboothWorkflowService: Photo captured by camera - File: {args?.FileName}");
                Log.Debug($"PhotoboothWorkflowService: File exists: {System.IO.File.Exists(args?.FileName)}");
                OnCaptureCompleted(args);
            };
        }
        
        private void EnsureCameraEventSubscription()
        {
            var camera = CurrentCamera;
            if (camera != null && _cameraCaptureHandler != null)
            {
                // Remove any existing subscription to avoid duplicates
                camera.PhotoCaptured -= _cameraCaptureHandler;
                // Subscribe to capture events
                camera.PhotoCaptured += _cameraCaptureHandler;
                Log.Debug("PhotoboothWorkflowService: Ensured camera PhotoCaptured event subscription");
            }
        }


        /// <summary>
        /// Start the photo capture workflow with countdown
        /// </summary>
        public async Task<bool> StartPhotoCaptureWorkflowAsync()
        {
            try
            {
                if (_isCapturing)
                {
                    Log.Debug("PhotoboothWorkflowService: Capture already in progress");
                    return false;
                }

                if (!_sessionService.IsSessionActive)
                {
                    StatusChanged?.Invoke(this, new StatusEventArgs { Status = "No active session" });
                    return false;
                }

                var camera = CurrentCamera;
                if (camera?.IsConnected != true)
                {
                    StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Camera not connected" });
                    return false;
                }

                Log.Debug("===== STARTING PHOTO CAPTURE WORKFLOW =====");
                Log.Debug($"PhotoboothWorkflowService: Camera = {camera?.DeviceName}, Connected = {camera?.IsConnected}");
                Log.Debug($"PhotoboothWorkflowService: Session Active = {_sessionService.IsSessionActive}");
                
                // Check if video mode should be started (first photo of session only)
                // This happens when video mode was previously enabled but the service was restarted
                var videoModeService = VideoModeLiveViewService.Instance;
                
                // Note: We don't auto-start video mode here anymore as it should be 
                // explicitly enabled through settings or UI controls before starting a session
                
                // Ensure we're subscribed to camera events
                Log.Debug("PhotoboothWorkflowService: Ensuring camera event subscription...");
                EnsureCameraEventSubscription();
                
                _isCapturing = true;
                StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Preparing capture..." });

                // Check if photographer mode (skip countdown for ALL photos in photographer mode)
                bool photographerMode = Properties.Settings.Default.PhotographerMode;

                if (photographerMode)
                {
                    // In photographer mode, wait for manual trigger
                    Log.Debug("PhotoboothWorkflowService: Photographer mode - waiting for manual trigger");
                    StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Press camera trigger when ready" });
                    
                    // Don't capture here - wait for the trigger to fire PhotoCaptured event
                    // The _cameraCaptureHandler will process it when trigger is pressed
                    // Just keep the workflow active and waiting
                }
                else
                {
                    // Normal mode - do countdown then capture
                    await StartCountdownAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Failed to start capture workflow: {ex.Message}");
                WorkflowError?.Invoke(this, new WorkflowErrorEventArgs { Error = ex, Operation = "StartCaptureWorkflow" });
                _isCapturing = false;
                return false;
            }
        }

        /// <summary>
        /// Start countdown before capture
        /// </summary>
        public async Task StartCountdownAsync()
        {
            try
            {
                _countdownValue = Properties.Settings.Default.CountdownSeconds;
                _isCountdownActive = true;

                Log.Debug($"PhotoboothWorkflowService: Starting countdown from {_countdownValue}");

                CountdownStarted?.Invoke(this, new CountdownEventArgs 
                { 
                    CountdownValue = _countdownValue,
                    TotalSeconds = Properties.Settings.Default.CountdownSeconds
                });

                _countdownTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Failed to start countdown: {ex.Message}");
                WorkflowError?.Invoke(this, new WorkflowErrorEventArgs { Error = ex, Operation = "StartCountdown" });
                _isCapturing = false;
                _isCountdownActive = false;
            }
        }

        /// <summary>
        /// Cancel the current photo capture workflow and reset for retake
        /// </summary>
        public void CancelCurrentPhotoCapture()
        {
            try
            {
                Log.Debug("===== CANCELING CURRENT PHOTO CAPTURE ====");
                
                // Stop countdown timer if active
                if (_countdownTimer?.IsEnabled == true)
                {
                    _countdownTimer.Stop();
                    Log.Debug("PhotoboothWorkflowService: Countdown timer stopped");
                }
                
                // Reset workflow state
                _isCapturing = false;
                _isCountdownActive = false;
                _countdownValue = 0;
                
                // Restart live view if camera is connected
                var camera = CurrentCamera;
                if (camera?.IsConnected == true)
                {
                    camera.StartLiveView();
                    Log.Debug("PhotoboothWorkflowService: Live view restarted after cancel");
                }
                
                // Notify status change
                StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Photo capture canceled - Ready for retake" });
                
                Log.Debug("PhotoboothWorkflowService: Photo capture workflow canceled and reset for retake");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error canceling photo capture: {ex.Message}");
                WorkflowError?.Invoke(this, new WorkflowErrorEventArgs { Error = ex, Operation = "CancelPhotoCapture" });
            }
        }

        /// <summary>
        /// Wait for photographer to trigger capture manually
        /// </summary>
        private async Task WaitForPhotographerTriggerAsync()
        {
            try
            {
                var camera = CurrentCamera;
                if (camera == null) return;

                Log.Debug("PhotoboothWorkflowService: Waiting for photographer trigger");
                StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Waiting for photographer..." });

                // In photographer mode, the camera event handler is already set up
                // Just wait for the photographer to press the shutter button
                Log.Debug("PhotoboothWorkflowService: Ready for photographer to trigger capture");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Failed to set up photographer trigger: {ex.Message}");
                WorkflowError?.Invoke(this, new WorkflowErrorEventArgs { Error = ex, Operation = "WaitForTrigger" });
                _isCapturing = false;
            }
        }

        /// <summary>
        /// Execute the actual photo capture
        /// </summary>
        private async Task CapturePhotoAsync()
        {
            try
            {
                var camera = CurrentCamera;
                if (camera?.IsConnected != true)
                {
                    throw new InvalidOperationException("Camera not connected");
                }

                Log.Debug("PhotoboothWorkflowService: Executing photo capture");
                
                // Check if we need to switch from video mode to photo mode
                var videoModeService = VideoModeLiveViewService.Instance;
                Log.Debug($"PhotoboothWorkflowService: Video mode check - IsVideoModeActive={videoModeService.IsVideoModeActive}, IsEnabled={videoModeService.IsEnabled}");
                
                if (videoModeService.IsVideoModeActive)
                {
                    Log.Debug("PhotoboothWorkflowService: Video mode is active, switching to photo mode for capture");
                    
                    // Fire events first (UI updates)
                    CaptureStarted?.Invoke(this, new CaptureEventArgs 
                    { 
                        PhotoIndex = _sessionService.CurrentPhotoIndex + 1,
                        TotalPhotos = _sessionService.TotalPhotosRequired
                    });

                    StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Capturing..." });
                    
                    // Now switch mode and WAIT for it to complete before capture
                    bool switchResult = await videoModeService.SwitchToPhotoModeForCapture();
                    Log.Debug($"PhotoboothWorkflowService: Switch to photo mode result: {switchResult}");
                    
                    // Small delay to ensure mode is fully settled
                    await Task.Delay(20);
                }
                else
                {
                    // Stop live view during capture only if not in video mode
                    camera.StopLiveView();
                    
                    CaptureStarted?.Invoke(this, new CaptureEventArgs 
                    { 
                        PhotoIndex = _sessionService.CurrentPhotoIndex + 1,
                        TotalPhotos = _sessionService.TotalPhotosRequired
                    });

                    StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Capturing..." });
                }

                // Capture the photo - the camera's PhotoCaptured event will handle the result
                // The event subscription is already ensured in StartPhotoCaptureWorkflowAsync
                Log.Debug($"PhotoboothWorkflowService: About to call camera.CapturePhoto()");
                Log.Debug($"PhotoboothWorkflowService: Camera event handler is: {(_cameraCaptureHandler != null ? "SET" : "NULL")}");
                
                camera.CapturePhoto();

                Log.Debug($"PhotoboothWorkflowService: Photo capture initiated for photo {_sessionService.CurrentPhotoIndex + 1}");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Failed to capture photo: {ex.Message}");
                WorkflowError?.Invoke(this, new WorkflowErrorEventArgs { Error = ex, Operation = "CapturePhoto" });
                _isCapturing = false;
                
                // Restart live view on error
                _ = ResumeLiveViewAsync();
            }
        }

        #region Timer Event Handlers
        private void InitializeCountdownTimer()
        {
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        private async void CountdownTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _countdownValue--;
                
                if (_countdownValue > 0)
                {
                    CountdownTick?.Invoke(this, new CountdownEventArgs 
                    { 
                        CountdownValue = _countdownValue,
                        TotalSeconds = Properties.Settings.Default.CountdownSeconds
                    });
                }
                else
                {
                    _countdownTimer.Stop();
                    _isCountdownActive = false;
                    
                    CountdownCompleted?.Invoke(this, EventArgs.Empty);
                    await CapturePhotoAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error in countdown timer: {ex.Message}");
                _countdownTimer.Stop();
                _isCountdownActive = false;
                _isCapturing = false;
            }
        }
        #endregion

        #region Event Handlers
        private async void OnCaptureCompleted(PhotoCapturedEventArgs args)
        {
            try
            {
                Log.Debug("===== WORKFLOW: OnCaptureCompleted CALLED =====");
                Log.Debug($"PhotoboothWorkflowService: Photo capture completed - File: {args?.FileName}");
                Log.Debug($"PhotoboothWorkflowService: File exists: {System.IO.File.Exists(args?.FileName)}");
                
                CaptureCompleted?.Invoke(this, new CaptureEventArgs 
                { 
                    PhotoIndex = _sessionService.CurrentPhotoIndex + 1,
                    TotalPhotos = _sessionService.TotalPhotosRequired,
                    PhotoPath = args?.FileName
                });

                // Process the photo through the session service FIRST to get the actual saved path
                Log.Debug("★★★ PhotoboothWorkflowService: About to call ProcessCapturedPhotoAsync ★★★");
                Log.Debug($"  Photo path: {args.FileName}");
                Log.Debug($"  File exists: {System.IO.File.Exists(args.FileName)}");
                
                string processedPhotoPath = null;
                bool success = await _sessionService.ProcessCapturedPhotoAsync(args);
                
                if (success)
                {
                    Log.Debug("★★★ PhotoboothWorkflowService: Photo processed successfully ★★★");
                    
                    // Get the actual processed photo path from the session
                    var capturedPaths = _sessionService.CapturedPhotoPaths;
                    if (capturedPaths != null && capturedPaths.Count > 0)
                    {
                        processedPhotoPath = capturedPaths[capturedPaths.Count - 1]; // Get the last added photo
                        Log.Debug($"PhotoboothWorkflowService: Using processed photo path for display: {processedPhotoPath}");
                    }
                }
                else
                {
                    Log.Error("★★★ PhotoboothWorkflowService: Failed to process captured photo ★★★");
                    StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Photo processing failed" });
                }

                // Display the PROCESSED photo (not the raw camera file)
                
                // Clear the capturing flag before display so next capture can start
                _isCapturing = false;
                Log.Debug("PhotoboothWorkflowService: Cleared _isCapturing flag");
                if (!string.IsNullOrEmpty(processedPhotoPath) && System.IO.File.Exists(processedPhotoPath))
                {
                    Log.Debug($"PhotoboothWorkflowService: Calling DisplayCapturedPhotoAsync with processed path: {processedPhotoPath}");
                    await DisplayCapturedPhotoAsync(processedPhotoPath);
                    Log.Debug("PhotoboothWorkflowService: DisplayCapturedPhotoAsync completed");
                }
                else
                {
                    Log.Error($"PhotoboothWorkflowService: Cannot display photo - processed path is empty or file doesn't exist: {processedPhotoPath}");
                    // If we can't display the photo, we still need to resume live view
                    await ResumeLiveViewAsync();
                }

                
                // Live view will be resumed by DisplayCapturedPhotoAsync after the display duration
                // Don't start it here to avoid conflicts
                Log.Debug("PhotoboothWorkflowService: Live view will resume after photo display completes");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error processing completed capture: {ex.Message}");
                WorkflowError?.Invoke(this, new WorkflowErrorEventArgs { Error = ex, Operation = "ProcessCapture" });
                _isCapturing = false;
            }
        }

        /// <summary>
        /// Display captured photo for configured duration
        /// </summary>
        private async Task DisplayCapturedPhotoAsync(string photoPath)
        {
            try
            {
                int displayDuration = Properties.Settings.Default.PhotoDisplayDuration;
                if (displayDuration <= 0)
                {
                    Log.Debug("PhotoboothWorkflowService: Photo display duration is 0, skipping display");
                    // Still need to check for next photo
                    await ResumeLiveViewAsync();
                    Log.Debug("Camera live view restarted (no photo display)");
                    await CheckAndContinueWorkflow();
                    return;
                }

                Log.Debug($"PhotoboothWorkflowService: Requesting photo display for {displayDuration} seconds");
                Log.Debug($"Current time: {DateTime.Now:HH:mm:ss.fff}");
                
                // Stop live view before displaying photo
                CurrentCamera?.StopLiveView();
                Log.Debug("Camera live view stopped");
                
                // Request UI to display the photo
                PhotoDisplayRequested?.Invoke(this, new PhotoDisplayEventArgs 
                { 
                    PhotoPath = photoPath,
                    DisplayDuration = displayDuration
                });
                Log.Debug($"PhotoDisplayRequested event fired at {DateTime.Now:HH:mm:ss.fff}");

                // Wait for the display duration
                Log.Debug($"Starting {displayDuration} second wait...");
                await Task.Delay(displayDuration * 1000);
                Log.Debug($"Wait complete at {DateTime.Now:HH:mm:ss.fff}");

                // Notify that display is complete
                PhotoDisplayCompleted?.Invoke(this, EventArgs.Empty);
                Log.Debug("PhotoDisplayCompleted event fired");
                
                // Resume live view
                await ResumeLiveViewAsync();
                Log.Debug("Camera live view restarted");
                
                // Check if we need to continue with more photos
                await CheckAndContinueWorkflow();
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error displaying captured photo: {ex.Message}");
            }
        }
        
        private async Task CheckAndContinueWorkflow()
        {
            try
            {
                // Check if more photos are needed
                if (_sessionService.CurrentPhotoIndex < _sessionService.TotalPhotosRequired)
                {
                    Log.Debug($"PhotoboothWorkflowService: More photos needed ({_sessionService.CurrentPhotoIndex} of {_sessionService.TotalPhotosRequired})");
                    
                    // Check if photographer mode is enabled
                    bool photographerMode = Properties.Settings.Default.PhotographerMode;
                    
                    if (photographerMode && _sessionService.CurrentPhotoIndex > 0)
                    {
                        // In photographer mode after first photo, wait for manual trigger
                        Log.Debug("PhotoboothWorkflowService: Photographer mode - waiting for manual trigger");
                        StatusChanged?.Invoke(this, new StatusEventArgs 
                        { 
                            Status = $"Ready for photo {_sessionService.CurrentPhotoIndex + 1} of {_sessionService.TotalPhotosRequired} - Press camera trigger when ready"
                        });
                        // Don't auto-continue - wait for manual trigger
                    }
                    else
                    {
                        // Auto-continue to next photo
                        Log.Debug("PhotoboothWorkflowService: Starting next photo capture");
                        StatusChanged?.Invoke(this, new StatusEventArgs 
                        { 
                            Status = $"Preparing for photo {_sessionService.CurrentPhotoIndex + 1} of {_sessionService.TotalPhotosRequired}..."
                        });
                        
                        // Small delay before starting next countdown
                        await Task.Delay(1000);
                        
                        // Start the next photo capture with countdown
                        await StartPhotoCaptureWorkflowAsync();
                    }
                }
                else
                {
                    Log.Debug($"PhotoboothWorkflowService: All photos captured ({_sessionService.CurrentPhotoIndex} of {_sessionService.TotalPhotosRequired})");
                    // Session will be completed by the session service
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error checking workflow continuation: {ex.Message}");
                WorkflowError?.Invoke(this, new WorkflowErrorEventArgs { Error = ex, Operation = "CheckContinuation" });
            }
        }

        private void OnSessionPhotoProcessed(object sender, PhotoProcessedEventArgs e)
        {
            try
            {
                Log.Debug($"PhotoboothWorkflowService: Photo {e.PhotoIndex} of {e.TotalPhotos} processed");

                if (!e.IsComplete)
                {
                    StatusChanged?.Invoke(this, new StatusEventArgs 
                    { 
                        Status = $"Photo {e.PhotoIndex} captured! Get ready for photo {e.PhotoIndex + 1}..."
                    });
                }
                else
                {
                    StatusChanged?.Invoke(this, new StatusEventArgs { Status = "All photos captured! Processing..." });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error handling photo processed: {ex.Message}");
            }
        }

        private void OnSessionCompleted(object sender, SessionCompletedEventArgs e)
        {
            try
            {
                Log.Debug("PhotoboothWorkflowService: Session completed");
                StatusChanged?.Invoke(this, new StatusEventArgs { Status = "Session complete!" });
                _isCapturing = false;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error handling session completed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Resume live view, using video mode if it was previously active
        /// </summary>
        private async Task ResumeLiveViewAsync()
        {
            try
            {
                var videoModeService = VideoModeLiveViewService.Instance;
                
                // Only resume video mode if it was previously active before capture
                // Don't start it for the first time here - that should be done explicitly
                // BUT don't resume if we're in a photo session - wait until session completes
                if (videoModeService.IsEnabled && !videoModeService.IsVideoModeActive && !_sessionService.IsSessionActive)
                {
                    // Video mode is enabled but not active (was switched to photo mode for capture)
                    // AND no active session - safe to resume
                    Log.Debug($"PhotoboothWorkflowService: Resuming video mode after capture (IsEnabled={videoModeService.IsEnabled})");
                    _ = videoModeService.ResumeVideoModeAfterCapture();
                }
                else if (videoModeService.IsEnabled && !videoModeService.IsVideoModeActive && _sessionService.IsSessionActive)
                {
                    // In a photo session from video mode - don't resume video mode yet
                    Log.Debug("PhotoboothWorkflowService: Skipping video mode resume - photo session still active");
                    // Just start normal live view for now
                    CurrentCamera?.StartLiveView();
                }
                else if (!videoModeService.IsEnabled)
                {
                    // Normal live view resume when video mode is not enabled
                    Log.Debug("PhotoboothWorkflowService: Resuming normal live view (video mode not enabled)");
                    CurrentCamera?.StartLiveView();
                }
                else
                {
                    // Video mode is both enabled and active - shouldn't happen but handle it
                    Log.Debug($"PhotoboothWorkflowService: Video mode already active, starting live view");
                    CurrentCamera?.StartLiveView();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothWorkflowService: Error resuming live view: {ex.Message}");
                // Fallback to normal live view on error
                CurrentCamera?.StartLiveView();
            }
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            _countdownTimer?.Stop();

            // Unsubscribe from camera events
            if (_cameraCaptureHandler != null && CurrentCamera != null)
            {
                CurrentCamera.PhotoCaptured -= _cameraCaptureHandler;
            }
            
            if (_sessionService != null)
            {
                _sessionService.PhotoProcessed -= OnSessionPhotoProcessed;
                _sessionService.SessionCompleted -= OnSessionCompleted;
            }
        }
        #endregion
    }

    #region Event Args Classes
    public class CountdownEventArgs : EventArgs
    {
        public int CountdownValue { get; set; }
        public int TotalSeconds { get; set; }
    }

    public class CaptureEventArgs : EventArgs
    {
        public int PhotoIndex { get; set; }
        public int TotalPhotos { get; set; }
        public string PhotoPath { get; set; }
    }

    public class WorkflowErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
        public string Operation { get; set; }
    }

    public class StatusEventArgs : EventArgs
    {
        public string Status { get; set; }
    }
    
    public class PhotoDisplayEventArgs : EventArgs
    {
        public string PhotoPath { get; set; }
        public int DisplayDuration { get; set; }
    }
    #endregion
}