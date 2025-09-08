using System;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    /// <summary>
    /// Coordinates video recording with live view mode following clean architecture
    /// This service manages the interaction between VideoRecordingService and VideoModeLiveViewService
    /// </summary>
    public class VideoRecordingCoordinatorService : INotifyPropertyChanged
    {
        #region Singleton
        private static VideoRecordingCoordinatorService _instance;
        public static VideoRecordingCoordinatorService Instance => 
            _instance ?? (_instance = new VideoRecordingCoordinatorService());
        #endregion

        #region Services
        private readonly VideoRecordingService _recordingService;
        private readonly VideoModeLiveViewService _liveViewService;
        #endregion

        #region Properties
        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (_isRecording != value)
                {
                    _isRecording = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isVideoModeActive;
        public bool IsVideoModeActive
        {
            get => _isVideoModeActive;
            private set
            {
                if (_isVideoModeActive != value)
                {
                    _isVideoModeActive = value;
                    OnPropertyChanged();
                }
            }
        }

        private TimeSpan _recordingDuration;
        public TimeSpan RecordingDuration
        {
            get => _recordingDuration;
            private set
            {
                if (_recordingDuration != value)
                {
                    _recordingDuration = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _recordingStatus;
        public string RecordingStatus
        {
            get => _recordingStatus;
            private set
            {
                if (_recordingStatus != value)
                {
                    _recordingStatus = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        private string _currentVideoPath;
        public string CurrentVideoPath
        {
            get => _currentVideoPath;
            private set
            {
                if (_currentVideoPath != value)
                {
                    _currentVideoPath = value;
                    OnPropertyChanged();
                }
            }
        }

        #region Events
        public event EventHandler<VideoRecordingEventArgs> RecordingStarted;
        public event EventHandler<VideoRecordingEventArgs> RecordingStopped;
        public event EventHandler<VideoRecordingEventArgs> RecordingProgress;
        public event EventHandler<string> RecordingError;
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion

        #region Constructor
        private VideoRecordingCoordinatorService()
        {
            _recordingService = new VideoRecordingService();
            _liveViewService = VideoModeLiveViewService.Instance;
            
            // Subscribe to recording service events
            _recordingService.RecordingStarted += OnRecordingStarted;
            _recordingService.RecordingStopped += OnRecordingCompleted;
            _recordingService.RecordingProgress += OnRecordingProgress;
            _recordingService.RecordingError += OnRecordingError;
            
            Log.Debug("VideoRecordingCoordinatorService: Initialized");
        }
        #endregion

        #region Public Methods
        
        /// <summary>
        /// Start a video recording session with live view
        /// </summary>
        public async Task<bool> StartVideoSessionAsync(string outputPath = null, Database.EventData currentEvent = null, int sessionId = 0, string sessionGuid = null)
        {
            try
            {
                Log.Debug($"VideoRecordingCoordinatorService: Starting video session (sessionId: {sessionId}, sessionGuid: {sessionGuid})");
                
                // Check if already recording to prevent duplicate attempts
                if (IsRecording)
                {
                    Log.Debug("VideoRecordingCoordinatorService: Already recording, ignoring duplicate start request");
                    return true; // Return true since recording is already active
                }
                
                // Check if camera is available
                var cameraManager = CameraSessionManager.Instance.DeviceManager;
                var camera = cameraManager?.SelectedCameraDevice;
                if (camera == null)
                {
                    Log.Error("VideoRecordingCoordinatorService: No camera available");
                    RecordingError?.Invoke(this, "No camera connected");
                    return false;
                }
                
                // Clear any camera busy state before starting
                camera.IsBusy = false;
                
                // Initialize recording service with camera
                _recordingService.Initialize(camera);
                
                // Step 1: Enable video mode live view for real-time preview
                Log.Debug("VideoRecordingCoordinatorService: Enabling video mode live view");
                bool liveViewStarted = await _liveViewService.StartVideoModeLiveView();
                if (!liveViewStarted)
                {
                    Log.Error("VideoRecordingCoordinatorService: Failed to start video mode live view");
                    RecordingError?.Invoke(this, "Failed to enable video preview mode");
                    return false;
                }
                
                IsVideoModeActive = true;
                RecordingStatus = "Video mode active - Ready to record";
                
                // Step 2: Clear camera busy state again after live view start
                camera.IsBusy = false;
                
                // Step 3: Give camera time to stabilize and clear any pending operations
                await Task.Delay(500);
                
                // Step 4: Final camera state check before recording
                if (camera.IsBusy)
                {
                    Log.Debug("VideoRecordingCoordinatorService: Camera still busy, waiting...");
                    await Task.Delay(1000);
                    camera.IsBusy = false;
                }
                
                // Step 5: Set session info if provided
                if (sessionId > 0 && !string.IsNullOrEmpty(sessionGuid))
                {
                    Log.Debug($"VideoRecordingCoordinatorService: Setting session info - ID: {sessionId}, GUID: {sessionGuid}");
                    _recordingService.SetSessionInfo(sessionId, sessionGuid);
                }
                else
                {
                    Log.Debug($"VideoRecordingCoordinatorService: No session info to set - ID: {sessionId}, GUID: {sessionGuid}");
                }
                
                // Step 6: Start actual recording
                Log.Debug("VideoRecordingCoordinatorService: Starting video recording");
                bool recordingStarted = await _recordingService.StartRecordingAsync(outputPath, currentEvent);
                if (!recordingStarted)
                {
                    Log.Error("VideoRecordingCoordinatorService: Failed to start recording");
                    // Clean up: disable video mode if recording failed
                    await _liveViewService.StopVideoModeLiveView();
                    IsVideoModeActive = false;
                    RecordingError?.Invoke(this, "Failed to start video recording");
                    return false;
                }
                
                IsRecording = true;
                RecordingStatus = "Recording...";
                Log.Debug("VideoRecordingCoordinatorService: Video recording started successfully");
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"VideoRecordingCoordinatorService: Error starting video session - {ex.Message}");
                RecordingError?.Invoke(this, $"Video session error: {ex.Message}");
                
                // Clean up on error
                IsRecording = false;
                IsVideoModeActive = false;
                await _liveViewService.StopVideoModeLiveView();
                
                return false;
            }
        }
        
        /// <summary>
        /// Stop the current video recording session
        /// </summary>
        public async Task<bool> StopVideoSessionAsync()
        {
            try
            {
                Log.Debug("VideoRecordingCoordinatorService: Stopping video session");
                
                bool success = true;
                
                // Step 1: Stop recording if active
                if (IsRecording)
                {
                    Log.Debug("VideoRecordingCoordinatorService: Stopping video recording");
                    var result = await _recordingService.StopRecordingAsync();
                    CurrentVideoPath = result; // Store the video path
                    success = !string.IsNullOrEmpty(result);
                    IsRecording = false;
                }
                
                // Step 2: Stop video mode live view
                if (IsVideoModeActive)
                {
                    Log.Debug("VideoRecordingCoordinatorService: Stopping video mode live view");
                    await Task.Delay(500); // Give camera time to finalize recording
                    await _liveViewService.StopVideoModeLiveView();
                    IsVideoModeActive = false;
                }
                
                RecordingStatus = "Video session stopped";
                Log.Debug($"VideoRecordingCoordinatorService: Video session stopped - Success: {success}");
                
                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"VideoRecordingCoordinatorService: Error stopping video session - {ex.Message}");
                RecordingError?.Invoke(this, $"Error stopping video: {ex.Message}");
                
                // Force cleanup
                IsRecording = false;
                IsVideoModeActive = false;
                
                return false;
            }
        }
        
        /// <summary>
        /// Enable video mode preview without recording
        /// </summary>
        public async Task<bool> EnableVideoPreviewAsync()
        {
            try
            {
                Log.Debug("VideoRecordingCoordinatorService: Enabling video preview mode");
                
                // Just enable video mode live view without starting recording
                bool success = await _liveViewService.StartVideoModeLiveView();
                if (success)
                {
                    IsVideoModeActive = true;
                    RecordingStatus = "Video preview active";
                    Log.Debug("VideoRecordingCoordinatorService: Video preview enabled");
                }
                else
                {
                    Log.Error("VideoRecordingCoordinatorService: Failed to enable video preview");
                    RecordingError?.Invoke(this, "Failed to enable video preview");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"VideoRecordingCoordinatorService: Error enabling video preview - {ex.Message}");
                RecordingError?.Invoke(this, $"Video preview error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Disable video mode preview
        /// </summary>
        public async Task<bool> DisableVideoPreviewAsync()
        {
            try
            {
                Log.Debug("VideoRecordingCoordinatorService: Disabling video preview mode");
                
                if (IsRecording)
                {
                    Log.Error("VideoRecordingCoordinatorService: Cannot disable preview while recording");
                    return false;
                }
                
                bool success = await _liveViewService.StopVideoModeLiveView();
                if (success)
                {
                    IsVideoModeActive = false;
                    RecordingStatus = "Video preview disabled";
                    Log.Debug("VideoRecordingCoordinatorService: Video preview disabled");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Log.Error($"VideoRecordingCoordinatorService: Error disabling video preview - {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Toggle between recording and not recording while maintaining video mode
        /// </summary>
        public async Task<bool> ToggleRecordingAsync(string outputPath = null)
        {
            if (IsRecording)
            {
                // Stop recording but keep video mode active
                Log.Debug("VideoRecordingCoordinatorService: Stopping recording (keeping video mode)");
                var result = await _recordingService.StopRecordingAsync();
                bool success = !string.IsNullOrEmpty(result);
                IsRecording = false;
                RecordingStatus = "Video mode active - Recording stopped";
                return success;
            }
            else
            {
                // Ensure video mode is active first
                if (!IsVideoModeActive)
                {
                    Log.Debug("VideoRecordingCoordinatorService: Video mode not active, enabling it first");
                    bool previewEnabled = await EnableVideoPreviewAsync();
                    if (!previewEnabled)
                    {
                        return false;
                    }
                    await Task.Delay(500); // Let mode stabilize
                }
                
                // Start recording
                Log.Debug("VideoRecordingCoordinatorService: Starting recording");
                bool success = await _recordingService.StartRecordingAsync(outputPath);
                if (success)
                {
                    IsRecording = true;
                    RecordingStatus = "Recording...";
                }
                return success;
            }
        }
        
        #endregion

        #region Event Handlers
        
        private void OnRecordingStarted(object sender, VideoRecordingEventArgs e)
        {
            RecordingDuration = TimeSpan.Zero;
            RecordingStarted?.Invoke(this, e);
            Log.Debug($"VideoRecordingCoordinatorService: Recording started");
        }
        
        private void OnRecordingCompleted(object sender, VideoRecordingEventArgs e)
        {
            IsRecording = false;
            RecordingStatus = $"Recording completed - Duration: {e.Duration:mm\\:ss}";
            
            Log.Debug($"VideoRecordingCoordinatorService: Recording completed - Duration: {e.Duration}");
            Log.Debug($"VideoRecordingCoordinatorService: About to fire RecordingStopped event to {RecordingStopped?.GetInvocationList()?.Length ?? 0} subscribers");
            
            RecordingStopped?.Invoke(this, e);
            
            Log.Debug($"VideoRecordingCoordinatorService: RecordingStopped event fired successfully");
        }
        
        private void OnRecordingProgress(object sender, TimeSpan e)
        {
            RecordingDuration = e;
            RecordingProgress?.Invoke(this, new VideoRecordingEventArgs { Duration = e });
        }
        
        private void OnRecordingError(object sender, string error)
        {
            IsRecording = false;
            RecordingStatus = $"Error: {error}";
            RecordingError?.Invoke(this, error);
            Log.Error($"VideoRecordingCoordinatorService: Recording error - {error}");
        }
        
        #endregion
        
        #region INotifyPropertyChanged
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
        
        #region Cleanup
        public void Dispose()
        {
            if (_recordingService != null)
            {
                _recordingService.RecordingStarted -= OnRecordingStarted;
                _recordingService.RecordingStopped -= OnRecordingCompleted;
                _recordingService.RecordingProgress -= OnRecordingProgress;
                _recordingService.RecordingError -= OnRecordingError;
            }
            
            Log.Debug("VideoRecordingCoordinatorService: Disposed");
        }
        #endregion
    }
}