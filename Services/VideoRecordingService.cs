using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Database;
using Photobooth.Properties;

namespace Photobooth.Services
{
    public class VideoRecordingService
    {
        private ICameraDevice _camera;
        private System.Timers.Timer _recordingTimer;
        private DateTime _recordingStartTime;
        private bool _isRecording;
        private string _currentVideoPath;
        private readonly PhotoboothModulesConfig _config;
        private string _originalCameraMode; // Store original mode to restore it
        private Database.EventData _currentEvent; // Store current event for folder structure
        private int _currentSessionId; // Store database session ID
        private string _currentSessionGuid; // Store session GUID for tracking
        private Database.TemplateDatabase _database; // Database instance
        
        // Store photo settings to restore after video
        private class PhotoSettings
        {
            public string ISO { get; set; }
            public string Aperture { get; set; }
            public string ShutterSpeed { get; set; }
            public string WhiteBalance { get; set; }
            public string FocusMode { get; set; }
            public string ExposureCompensation { get; set; }
        }
        private PhotoSettings _savedPhotoSettings;
        
        public event EventHandler<VideoRecordingEventArgs> RecordingStarted;
        public event EventHandler<VideoRecordingEventArgs> RecordingStopped;
        public event EventHandler<TimeSpan> RecordingProgress;
        public event EventHandler<string> RecordingError;
        
        public bool IsRecording => _isRecording;
        public TimeSpan ElapsedTime => _isRecording ? DateTime.Now - _recordingStartTime : TimeSpan.Zero;
        public TimeSpan MaxDuration => TimeSpan.FromSeconds(_config.VideoDuration);
        
        public VideoRecordingService()
        {
            _config = PhotoboothModulesConfig.Instance;
            _recordingTimer = new System.Timers.Timer(100); // Update every 100ms - balanced between responsiveness and performance
            _recordingTimer.Elapsed += OnRecordingTimerElapsed;
            _database = new Database.TemplateDatabase();
        }

        public void Initialize(ICameraDevice camera)
        {
            _camera = camera;
            
            // Don't automatically switch camera modes during initialization
            // This preserves the user's camera dial setting (M, Av, Tv, P, etc.)
            System.Diagnostics.Debug.WriteLine("[VIDEO] VideoRecordingService initialized, preserving current camera mode");
        }
        
        private async Task EnsurePhotoMode()
        {
            try
            {
                // Only switch modes for Canon cameras
                if (_camera != null && _camera.GetType().Name.Contains("Canon"))
                {
                    var modeProperty = _camera.Mode;
                    if (modeProperty != null)
                    {
                        string currentMode = modeProperty.Value;
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] EnsurePhotoMode: Current camera mode at startup: {currentMode}");
                        
                        // Show all available modes for debugging
                        if (modeProperty.Values != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] EnsurePhotoMode: Available modes: {string.Join(", ", modeProperty.Values)}");
                        }
                        
                        // Be very specific about what constitutes "movie mode" 
                        // Most Canon cameras use specific movie mode names, not just "movie"
                        bool isInMovieMode = currentMode != null && (
                            currentMode.ToLower().Contains("movie") ||
                            currentMode.ToLower().Contains("video") ||
                            currentMode.ToLower() == "movie" ||
                            currentMode.ToLower() == "video"
                        );
                        
                        if (isInMovieMode)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Camera is in movie/video mode at startup, switching to photo mode...");
                            
                            // Priority: Manual (M) first, then Program, then Av, then Tv as last resort
                            var photoMode = modeProperty.Values?.FirstOrDefault(v => 
                                v.Equals("M", StringComparison.OrdinalIgnoreCase)) ??
                            modeProperty.Values?.FirstOrDefault(v => 
                                v.ToLower().Contains("program")) ??
                            modeProperty.Values?.FirstOrDefault(v => 
                                v.ToLower().Contains("av")) ??
                            modeProperty.Values?.FirstOrDefault(v => 
                                v.ToLower().Contains("tv"));
                            
                            if (photoMode != null)
                            {
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] EnsurePhotoMode: Switching from {currentMode} to {photoMode}");
                                    modeProperty.SetValue(photoMode);
                                    await Task.Delay(500);
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] Successfully switched to photo mode on initialization");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Failed to switch to photo mode: {ex.Message}");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("[VIDEO] No suitable photo mode found");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera already in photo mode ({currentMode}), no change needed");
                            // Don't change anything if camera is already in a photo mode (M, Av, Tv, P, etc.)
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] EnsurePhotoMode: Camera mode property is null");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] EnsurePhotoMode: Not a Canon camera or camera is null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error ensuring photo mode: {ex.Message}");
            }
        }

        public void SetSessionInfo(int sessionId, string sessionGuid)
        {
            _currentSessionId = sessionId;
            _currentSessionGuid = sessionGuid;
            System.Diagnostics.Debug.WriteLine($"[VIDEO] *** SESSION INFO SET *** - ID: {sessionId}, GUID: {sessionGuid}");
        }
        
        public async Task<bool> StartRecordingAsync(string outputPath = null, Database.EventData currentEvent = null)
        {
            System.Diagnostics.Debug.WriteLine($"[VIDEO] StartRecordingAsync called. Camera: {_camera?.DeviceName ?? "NULL"}, Already recording: {_isRecording}");
            System.Diagnostics.Debug.WriteLine($"[VIDEO] Current session info - ID: {_currentSessionId}, GUID: {_currentSessionGuid}");
            
            // Store current event for use in other methods
            _currentEvent = currentEvent;
            
            // Reset stop flag for new recording
            _isStopping = false;
            
            if (_isRecording || _camera == null)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Cannot start - already recording or no camera");
                return false;
            }

            try
            {
                // Run diagnostic for Canon T6 if detected
                if (_camera?.DeviceName != null && 
                    (_camera.DeviceName.Contains("T6") || _camera.DeviceName.Contains("1300D") || _camera.DeviceName.Contains("Rebel")))
                {
                    await DiagnoseCanonT6VideoCapabilities();
                }
                
                // Check if camera supports video recording
                if (!IsVideoRecordingSupported())
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera does not support video recording");
                    RecordingError?.Invoke(this, "This camera does not support video recording");
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera supports video, will switch to movie mode now...");
                
                // Store the original camera mode before making any changes
                if (_camera?.Mode?.Value != null)
                {
                    _originalCameraMode = _camera.Mode.Value;
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Stored original camera mode: {_originalCameraMode}");
                }
                
                // Save current photo settings and apply video settings
                SavePhotoSettings();
                ApplyVideoSettings();
                
                // Try to switch camera to movie mode if it's a Canon camera
                await SwitchToMovieModeIfNeeded();
                
                // Generate output path if not provided
                if (string.IsNullOrEmpty(outputPath))
                {
                    // Videos are saved in event-based folder structure (same as photos)
                    // Use Pictures folder as primary location to match photo storage
                    string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    string photoboothDir = Path.Combine(picturesPath, "PhotoBooth");
                    
                    // If PhotoBooth directory doesn't exist in Pictures, try Documents as fallback
                    if (!Directory.Exists(photoboothDir))
                    {
                        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        photoboothDir = Path.Combine(documentsPath, "PhotoBooth");
                    }
                    
                    // Always use event-based folder structure
                    string videoDir;
                    string eventName;
                    
                    if (currentEvent != null && !string.IsNullOrEmpty(currentEvent.Name))
                    {
                        // Use provided event name directly - it's already a valid folder name
                        eventName = currentEvent.Name;
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Using provided event: {eventName}");
                    }
                    else
                    {
                        // Fallback to default event based on date - everything must be in an event
                        eventName = $"Event_{DateTime.Now:yyyy-MM-dd}";
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] No event provided, using default event: {eventName}");
                    }
                    
                    // Create event folder structure - all videos must be in event folders
                    string eventFolder = Path.Combine(photoboothDir, eventName);
                    videoDir = Path.Combine(eventFolder, "videos");
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Using event folder structure: {videoDir}");
                    
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Creating directory: {videoDir}");
                    
                    if (!Directory.Exists(videoDir))
                    {
                        Directory.CreateDirectory(videoDir);
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Directory created successfully");
                    }
                    
                    string fileName = $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    outputPath = Path.Combine(videoDir, fileName);
                    
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] *** VIDEO WILL BE SAVED TO: {outputPath} ***");
                }
                
                _currentVideoPath = outputPath;
                
                // Create database session for video (like photo sessions) ONLY if not already set
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Checking if need to create session - CurrentSessionId: {_currentSessionId}, CurrentEvent: {currentEvent?.Name}");
                
                if (currentEvent != null && _currentSessionId <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] !!! CREATING NEW SESSION (this should not happen if session was passed) !!!");
                    try
                    {
                        // Generate session GUID for tracking
                        _currentSessionGuid = Guid.NewGuid().ToString();
                        
                        // Get template ID (use default or first available)
                        int templateId = 1; // Default template ID
                        var templates = _database.GetAllTemplates();
                        if (templates != null && templates.Count > 0)
                        {
                            templateId = templates[0].Id;
                        }
                        
                        // Create photo session in database (videos and photos share same session table)
                        _currentSessionId = _database.CreatePhotoSession(
                            currentEvent.Id, 
                            templateId, 
                            $"Video Session {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                            _currentSessionGuid);
                        
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Created database session ID: {_currentSessionId}, GUID: {_currentSessionGuid}, EventId: {currentEvent.Id}, EventName: {currentEvent.Name}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Failed to create database session: {ex.Message}");
                        // Continue even if database fails
                    }
                }
                else if (_currentSessionId > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] *** USING EXISTING SESSION *** ID: {_currentSessionId}, GUID: {_currentSessionGuid}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] WARNING: No session will be created (no event or session already exists)");
                }
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Calling camera.StartRecordMovie()...");
                
                // NOTE: VideoRecordingCoordinatorService handles mode switching via VideoModeLiveViewService
                // We should NOT attempt mode switching here as it's already been done
                // The camera should already be in video mode by the time we get here
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera should already be in video mode via VideoModeLiveViewService");
                
                // Start recording on camera
                bool recordingStarted = false;
                await Task.Run(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Executing StartRecordMovie()...");
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera type: {_camera.GetType().FullName}");
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera device name: {_camera.DeviceName}");
                        
                        // For Canon cameras, we've fixed the LiveViewqueue processing issue
                        // The camera should be in Manual (M) mode on the physical dial
                        if (_camera.GetType().Name.Contains("Canon"))
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Canon camera detected");
                            System.Diagnostics.Debug.WriteLine("[VIDEO] IMPORTANT: Camera should be in Manual (M) mode on physical dial");
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Calling StartRecordMovie() - this will process the LiveViewqueue");
                            _camera.StartRecordMovie();
                            System.Diagnostics.Debug.WriteLine("[VIDEO] StartRecordMovie called - LiveViewqueue processed");
                        }
                        else
                        {
                            _camera.StartRecordMovie();
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] StartRecordMovie() returned - no exception");
                        recordingStarted = true;
                    }
                    catch (Exception ex) when (ex.GetType().Name == "EosPropertyException")
                    {
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] ERROR: Canon EosPropertyException - {ex.Message}");
                        // Canon cameras need to be in movie mode first
                        throw new InvalidOperationException(
                            "Camera may not be in video/movie mode. Please switch the camera to movie mode and try again.", ex);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] ERROR starting recording: {ex.GetType().Name} - {ex.Message}");
                        throw;
                    }
                });
                
                // Wait a bit to ensure recording actually started
                await Task.Delay(500);
                
                // Skip the immediate recording check for Canon cameras
                // The Canon T6 might report not recording even when it is
                // We'll rely on the file being created when we stop recording
                if (_camera.GetType().Name.Contains("Canon"))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Canon camera - skipping immediate recording status check");
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Will verify recording when stopped by checking for video file");
                }
                
                if (!recordingStarted)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] ERROR: Recording did not start");
                    RecordingError?.Invoke(this, "Failed to start video recording. Please ensure the camera is in movie mode.");
                    return false;
                }
                
                _isRecording = true;
                _recordingStartTime = DateTime.Now;
                _recordingTimer.Start();
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] *** RECORDING STARTED SUCCESSFULLY at {_recordingStartTime:HH:mm:ss} ***");
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Max duration: {MaxDuration.TotalSeconds} seconds");
                
                RecordingStarted?.Invoke(this, new VideoRecordingEventArgs
                {
                    FilePath = _currentVideoPath,
                    StartTime = _recordingStartTime
                });
                
                // Auto-stop timer is handled in OnRecordingTimerElapsed - no need for separate Task.Delay
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera status monitoring active - checking every 50ms");
                
                return true;
            }
            catch (Exception ex)
            {
                RecordingError?.Invoke(this, $"Failed to start recording: {ex.Message}");
                return false;
            }
        }

        private bool _isStopping = false; // Prevent multiple concurrent stops
        
        public async Task<string> StopRecordingAsync()
        {
            System.Diagnostics.Debug.WriteLine($"[VIDEO] StopRecordingAsync called. Recording: {_isRecording}, Stopping: {_isStopping}, Camera: {_camera?.DeviceName ?? "NULL"}");
            
            // CRITICAL: Prevent multiple concurrent stop operations
            if (_isStopping)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Already stopping recording, ignoring duplicate call");
                return _currentVideoPath;
            }
            
            if (!_isRecording || _camera == null)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Cannot stop - not recording or no camera");
                return null;
            }

            _isStopping = true; // Set flag to prevent concurrent stops
            
            try
            {
                var recordingDuration = DateTime.Now - _recordingStartTime;
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Stopping recording after {recordingDuration.TotalSeconds:F1} seconds");
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Expected file location: {_currentVideoPath}");
                
                // Stop recording on camera
                await Task.Run(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Calling camera.StopRecordMovie()...");
                        _camera.StopRecordMovie();
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] StopRecordMovie() completed");
                    }
                    catch (Exception ex) when (ex.GetType().Name == "EosPropertyException")
                    {
                        // Canon cameras sometimes fail to stop recording with property errors
                        // This is often because the recording has already stopped
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Canon stop recording property error (recording may have already stopped): {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail - recording may have stopped anyway
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Error stopping recording (may be normal): {ex.Message}");
                    }
                });
                
                // Stop timer and update state IMMEDIATELY after camera stops
                if (_recordingTimer.Enabled)
                {
                    _recordingTimer.Stop();
                    System.Diagnostics.Debug.WriteLine("[VIDEO] Recording timer stopped - camera recording stopped");
                }
                _isRecording = false;
                var duration = DateTime.Now - _recordingStartTime;
                
                // Fire the stopped event immediately so UI updates right away
                RecordingStopped?.Invoke(this, new VideoRecordingEventArgs
                {
                    FilePath = _currentVideoPath,
                    StartTime = _recordingStartTime,
                    Duration = duration
                });
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera recording stopped, UI notified. Duration: {duration.TotalSeconds:F1} seconds");
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Recording stopped. Duration: {duration.TotalSeconds:F1} seconds");
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Waiting for file to be written...");
                
                // Wait a bit for file to be written
                await Task.Delay(1000); // Increased delay for file writing
                
                // Check if file exists
                if (File.Exists(_currentVideoPath))
                {
                    var fileInfo = new FileInfo(_currentVideoPath);
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] *** SUCCESS! Video file created: {_currentVideoPath} ***");
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] File size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                    
                    // Compress video and save as webupload version
                    await CompressAndSaveWebUploadVersion(_currentVideoPath);
                    
                    // Save video path to database (like photos)
                    SaveVideoToDatabase(_currentVideoPath, fileInfo.Length, (int)duration.TotalSeconds);
                    
                    // Don't end the session here - it's managed by PhotoboothSessionService
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Video saved to database session {_currentSessionId}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] *** WARNING: Video file NOT found at: {_currentVideoPath} ***");
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera may be saving to memory card instead of computer");
                    
                    // Try to find videos in the parent directory structure
                    var videoDirectory = Path.GetDirectoryName(_currentVideoPath);
                    if (Directory.Exists(videoDirectory))
                    {
                        var videoFiles = Directory.GetFiles(videoDirectory, "*.mp4", SearchOption.AllDirectories);
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Searching for video files in {videoDirectory}:");
                        if (videoFiles.Length > 0)
                        {
                            foreach (var file in videoFiles.OrderByDescending(f => File.GetCreationTime(f)).Take(5))
                            {
                                var fileInfo = new FileInfo(file);
                                System.Diagnostics.Debug.WriteLine($"[VIDEO]   Found: {file} ({fileInfo.Length / 1024.0 / 1024.0:F2} MB, {File.GetCreationTime(file):HH:mm:ss})");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO]   No MP4 files found in video directory");
                        }
                    }
                    
                    // Check if Canon camera has a download method for videos
                    await CheckForCanonVideoDownload();
                }
                
                // Switch back to photo mode if needed
                await SwitchBackToPhotoModeIfNeeded();
                
                // Restore photo settings after video recording
                RestorePhotoSettings();
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] StopRecordingAsync completed. Returning path: {_currentVideoPath}");
                return _currentVideoPath;
            }
            catch (Exception ex)
            {
                RecordingError?.Invoke(this, $"Failed to stop recording: {ex.Message}");
                _isRecording = false;
                
                // Still return the path as the video file may have been created
                return _currentVideoPath;
            }
            finally
            {
                _isStopping = false; // Reset flag to allow future stop operations
                _isRecording = false; // Ensure recording state is false
                
                // Reset session info after recording completes to prevent reuse
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Resetting session info after recording (was ID: {_currentSessionId}, GUID: {_currentSessionGuid})");
                _currentSessionId = 0;
                _currentSessionGuid = null;
            }
        }


        private async Task SwitchToMovieModeIfNeeded()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Checking camera mode. Camera type: {_camera.GetType().Name}");
                
                // NOTE: Mode switching is handled by VideoModeLiveViewService in VideoRecordingCoordinatorService
                // We should NOT attempt any mode switching here as it will conflict with the proper mode switching
                // The camera should already be in video mode when we get here
                
                // Check if it's a Canon camera for save location configuration only
                if (_camera.GetType().Name.Contains("Canon"))
                {
                    // IMPORTANT: For Canon cameras, we need to configure save location to PC
                    // before starting video recording to ensure videos are saved to computer
                    ConfigureCanonSaveToPC();
                    
                    // Log the current mode for debugging only - don't try to change it
                    var modeProperty = _camera.Mode;
                    if (modeProperty != null)
                    {
                        string currentMode = modeProperty.Value;
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Current camera mode (should be video mode): {currentMode}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Not a Canon camera");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error checking/switching camera mode: {ex.Message}");
                // Continue anyway - the camera might work without switching
            }
        }
        
        private bool StartCanonRecordingUsingShutterButton()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Attempting Canon recording using virtual shutter button press (DSLRBooth method)...");
                
                var cameraType = _camera.GetType();
                
                // First check if it's a CanonSDKBase type camera
                if (cameraType.Name == "CanonSDKBase")
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] Detected CanonSDKBase camera, using button press to start recording...");
                    
                    // For video recording in movie mode, we need to send a HALF shutter button press
                    // In movie mode, half-press starts/stops recording (not full press which takes a photo)
                    var pressHalfButtonMethod = cameraType.GetMethod("PressHalfButton", 
                        BindingFlags.Public | BindingFlags.Instance);
                    
                    if (pressHalfButtonMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] Calling PressHalfButton() to start recording...");
                        pressHalfButtonMethod.Invoke(_camera, null);
                        System.Diagnostics.Debug.WriteLine("[VIDEO] PressHalfButton() called - recording should start");
                        
                        // Wait a moment then release the button
                        Thread.Sleep(500);
                        
                        var releaseButtonMethod = cameraType.GetMethod("ReleaseButton", 
                            BindingFlags.Public | BindingFlags.Instance);
                        if (releaseButtonMethod != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Releasing shutter button...");
                            releaseButtonMethod.Invoke(_camera, null);
                        }
                        
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] PressButton method not found, trying alternative...");
                        
                        // If PressButton not found, try calling ResetShutterButton then send complete press manually
                        var resetMethod = cameraType.GetMethod("ResetShutterButton", 
                            BindingFlags.NonPublic | BindingFlags.Instance) ??
                            cameraType.GetMethod("ResetShutterButton", 
                            BindingFlags.Public | BindingFlags.Instance);
                            
                        if (resetMethod != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Using ResetShutterButton + manual complete press...");
                            resetMethod.Invoke(_camera, null);
                            Thread.Sleep(100);
                            
                            // Now we need to access the Camera property to send the complete press
                            var innerCameraField = cameraType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (innerCameraField != null)
                            {
                                var canonCamera = innerCameraField.GetValue(_camera);
                                if (canonCamera != null)
                                {
                                    var sendCommandMethod = canonCamera.GetType().GetMethod("SendCommand",
                                        BindingFlags.Public | BindingFlags.Instance,
                                        null,
                                        new Type[] { typeof(uint), typeof(int) },
                                        null);
                                        
                                    if (sendCommandMethod != null)
                                    {
                                        // Send HALF shutter press for video (not complete press)
                                        const uint CameraCommand_PressShutterButton = 0x00000004;
                                        const int ShutterButton_Halfway = 0x00000001;
                                        const int ShutterButton_OFF = 0x00000000;
                                        
                                        System.Diagnostics.Debug.WriteLine("[VIDEO] Sending half shutter press for video...");
                                        sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_PressShutterButton, ShutterButton_Halfway });
                                        Thread.Sleep(500);
                                        
                                        System.Diagnostics.Debug.WriteLine("[VIDEO] Releasing shutter button...");
                                        sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_PressShutterButton, ShutterButton_OFF });
                                        System.Diagnostics.Debug.WriteLine("[VIDEO] Half press and release sent");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Otherwise try to access the internal Canon camera object
                var outerCameraField = cameraType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (outerCameraField != null)
                {
                    var canonCamera = outerCameraField.GetValue(_camera);
                    if (canonCamera != null)
                    {
                        var canonCameraType = canonCamera.GetType();
                        
                        System.Diagnostics.Debug.WriteLine("[VIDEO] Using shutter button press to start recording in movie mode...");
                        
                        // Get SendCommand method with specific parameter types
                        var sendCommandMethod = canonCameraType.GetMethod("SendCommand", 
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            new Type[] { typeof(uint), typeof(int) },
                            null);
                        
                        if (sendCommandMethod != null)
                        {
                            // Send autofocus command first (like DigiCamControl does)
                            const uint CameraCommand_DoEvfAf = 0x00000102;
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Sending autofocus command...");
                            sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_DoEvfAf, 0 });
                            Thread.Sleep(100);
                            
                            // Send virtual shutter button press to start recording
                            // This is what triggers recording when camera is in movie mode
                            const uint CameraCommand_PressShutterButton = 0x00000004;
                            const int ShutterButton_OFF = 0x00000000;
                            
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Sending shutter button OFF (reset) command...");
                            sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_PressShutterButton, ShutterButton_OFF });
                            Thread.Sleep(100);
                            
                            // The shutter button OFF command should trigger recording in movie mode
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Shutter button command sent - recording should start");
                            
                            return true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] SendCommand method not found");
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error in shutter button recording: {ex.Message}");
                return false;
            }
        }
        
        private bool StopCanonRecordingUsingShutterButton()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Stopping Canon recording using shutter button...");
                
                var cameraType = _camera.GetType();
                
                // First check if it's a CanonSDKBase type camera
                if (cameraType.Name == "CanonSDKBase")
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] Detected CanonSDKBase camera, using button release to stop...");
                    
                    // To stop recording, we need another half-press of the shutter button
                    var pressHalfButtonMethod = cameraType.GetMethod("PressHalfButton", 
                        BindingFlags.Public | BindingFlags.Instance);
                    
                    if (pressHalfButtonMethod != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] Calling PressHalfButton() to stop recording...");
                        pressHalfButtonMethod.Invoke(_camera, null);
                        System.Diagnostics.Debug.WriteLine("[VIDEO] PressHalfButton() called - recording should stop");
                        
                        // Wait a moment then release the button
                        Thread.Sleep(500);
                        
                        var releaseButtonMethod = cameraType.GetMethod("ReleaseButton", 
                            BindingFlags.Public | BindingFlags.Instance);
                        if (releaseButtonMethod != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Releasing shutter button...");
                            releaseButtonMethod.Invoke(_camera, null);
                        }
                        
                        return true;
                    }
                    else
                    {
                        // Fallback to ResetShutterButton if ReleaseButton not found
                        var resetShutterMethod = cameraType.GetMethod("ResetShutterButton", 
                            BindingFlags.NonPublic | BindingFlags.Instance) ??
                            cameraType.GetMethod("ResetShutterButton", 
                            BindingFlags.Public | BindingFlags.Instance);
                        
                        if (resetShutterMethod != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Using ResetShutterButton() to stop recording (fallback)...");
                            resetShutterMethod.Invoke(_camera, null);
                            System.Diagnostics.Debug.WriteLine("[VIDEO] ResetShutterButton() called - recording should stop");
                            return true;
                        }
                    }
                }
                
                // Otherwise try to access the internal Canon camera object
                var cameraField = cameraType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (cameraField != null)
                {
                    var canonCamera = cameraField.GetValue(_camera);
                    if (canonCamera != null)
                    {
                        var canonCameraType = canonCamera.GetType();
                        
                        // Get SendCommand method
                        var sendCommandMethod = canonCameraType.GetMethod("SendCommand", 
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            new Type[] { typeof(uint), typeof(int) },
                            null);
                        
                        if (sendCommandMethod != null)
                        {
                            // Send shutter button OFF command to stop recording
                            const uint CameraCommand_PressShutterButton = 0x00000004;
                            const int ShutterButton_OFF = 0x00000000;
                            
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Sending shutter button OFF to stop recording...");
                            sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_PressShutterButton, ShutterButton_OFF });
                            
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Recording stopped via shutter button");
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error stopping recording with shutter button: {ex.Message}");
                return false;
            }
        }
        
        private bool StartCanonRecordingDirect()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Attempting direct Canon recording commands...");
                
                var cameraType = _camera.GetType();
                var cameraField = cameraType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (cameraField != null)
                {
                    var canonCamera = cameraField.GetValue(_camera);
                    if (canonCamera != null)
                    {
                        var canonCameraType = canonCamera.GetType();
                        
                        // Try to execute recording commands directly without queuing
                        System.Diagnostics.Debug.WriteLine("[VIDEO] Executing direct recording sequence...");
                        
                        // Stop live view
                        var stopLiveViewMethod = canonCameraType.GetMethod("StopLiveView", BindingFlags.Public | BindingFlags.Instance);
                        if (stopLiveViewMethod != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Stopping live view...");
                            stopLiveViewMethod.Invoke(canonCamera, null);
                            Thread.Sleep(100);
                        }
                        
                        // Send MovieSelectSwON command - be specific about parameter types to avoid ambiguity
                        var sendCommandMethod = canonCameraType.GetMethod("SendCommand", 
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            new Type[] { typeof(uint), typeof(int) },
                            null);
                        
                        if (sendCommandMethod == null)
                        {
                            // Try with just uint parameter
                            sendCommandMethod = canonCameraType.GetMethod("SendCommand",
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new Type[] { typeof(uint) },
                                null);
                        }
                        
                        if (sendCommandMethod != null)
                        {
                            const uint CameraCommand_MovieSelectSwON = 0x00000217;
                            
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Sending MovieSelectSwON...");
                            var parameters = sendCommandMethod.GetParameters();
                            if (parameters.Length == 2)
                            {
                                sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_MovieSelectSwON, 0 });
                            }
                            else if (parameters.Length == 1)
                            {
                                sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_MovieSelectSwON });
                            }
                            Thread.Sleep(100);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] SendCommand method not found");
                        }
                        
                        // Start live view
                        var startLiveViewMethod = canonCameraType.GetMethod("StartLiveView", BindingFlags.Public | BindingFlags.Instance);
                        if (startLiveViewMethod != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Starting live view...");
                            startLiveViewMethod.Invoke(canonCamera, null);
                            Thread.Sleep(100);
                        }
                        
                        // Send autofocus command
                        if (sendCommandMethod != null)
                        {
                            const uint CameraCommand_DoEvfAf = 0x00000102;
                            
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Sending DoEvfAf...");
                            var parameters = sendCommandMethod.GetParameters();
                            if (parameters.Length == 2)
                            {
                                sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_DoEvfAf, 0 });
                            }
                            else if (parameters.Length == 1)
                            {
                                sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_DoEvfAf });
                            }
                            Thread.Sleep(100);
                        }
                        
                        // Set PropID_Record to 4 (start recording)
                        var setPropertyMethod = canonCameraType.GetMethod("SetPropertyIntegerData", BindingFlags.Public | BindingFlags.Instance);
                        if (setPropertyMethod != null)
                        {
                            const uint PropID_Record = 0x00000510;
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Setting PropID_Record to 4 (start recording)...");
                            
                            // Try multiple times for Canon R100
                            for (int attempt = 0; attempt < 3; attempt++)
                            {
                                setPropertyMethod.Invoke(canonCamera, new object[] { PropID_Record, (long)4 });
                                Thread.Sleep(200);
                                
                                // Check if recording actually started
                                var getPropertyMethod = canonCameraType.GetMethod("GetProperty", BindingFlags.Public | BindingFlags.Instance);
                                if (getPropertyMethod != null)
                                {
                                    var recordStatus = getPropertyMethod.Invoke(canonCamera, new object[] { PropID_Record });
                                    int status = recordStatus != null ? Convert.ToInt32(recordStatus) : 0;
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Attempt {attempt + 1}: PropID_Record status = {status}");
                                    
                                    if (status == 4)
                                    {
                                        System.Diagnostics.Debug.WriteLine("[VIDEO] Direct Canon recording started successfully!");
                                        return true;
                                    }
                                    else if (status == 3 && attempt == 0)
                                    {
                                        // Canon R100 might be in ready state, send additional trigger
                                        System.Diagnostics.Debug.WriteLine("[VIDEO] Canon R100 in ready state, sending additional triggers...");
                                        
                                        // Send DoEvfAf command
                                        if (sendCommandMethod != null)
                                        {
                                            const uint CameraCommand_DoEvfAf = 0x00000102;
                                            sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_DoEvfAf, 1 });
                                            Thread.Sleep(100);
                                        }
                                    }
                                }
                            }
                            
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Direct Canon recording commands completed but status not confirmed");
                            return false;
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error in StartCanonRecordingDirect: {ex.Message}");
                return false;
            }
        }
        
        private async Task<bool> CheckCanonRecordingStatus()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Checking if Canon camera is actually recording...");
                
                var cameraType = _camera.GetType();
                var cameraField = cameraType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (cameraField != null)
                {
                    var canonCamera = cameraField.GetValue(_camera);
                    if (canonCamera != null)
                    {
                        var canonCameraType = canonCamera.GetType();
                        var getPropertyMethod = canonCameraType.GetMethod("GetProperty", BindingFlags.Public | BindingFlags.Instance);
                        
                        if (getPropertyMethod != null)
                        {
                            try
                            {
                                // PropID_Record = 0x00000510
                                const uint PropID_Record = 0x00000510;
                                var recordStatus = getPropertyMethod.Invoke(canonCamera, new object[] { PropID_Record });
                                
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Canon PropID_Record status: {recordStatus}");
                                
                                // Value 4 means recording, 0 means not recording
                                // Canon R100 may return 3 when ready to record, need to trigger actual recording
                                int status = recordStatus != null ? Convert.ToInt32(recordStatus) : 0;
                                
                                if (status == 4)
                                {
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] Canon camera IS recording (status=4)");
                                    return true;
                                }
                                else if (status == 3)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Canon camera in READY state (status=3), attempting to trigger recording...");
                                    
                                    // Canon R100 needs an additional trigger to start recording
                                    // Try sending the record start command again
                                    try
                                    {
                                        var setPropertyMethod = canonCameraType.GetMethod("SetPropertyIntegerData", BindingFlags.Public | BindingFlags.Instance);
                                        if (setPropertyMethod != null)
                                        {
                                            System.Diagnostics.Debug.WriteLine("[VIDEO] Sending PropID_Record=4 to trigger actual recording...");
                                            setPropertyMethod.Invoke(canonCamera, new object[] { PropID_Record, (long)4 });
                                            Thread.Sleep(500);
                                            
                                            // Check status again
                                            var newStatus = getPropertyMethod.Invoke(canonCamera, new object[] { PropID_Record });
                                            if (newStatus != null && Convert.ToInt32(newStatus) == 4)
                                            {
                                                System.Diagnostics.Debug.WriteLine("[VIDEO] Canon camera NOW recording after trigger");
                                                return true;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Error triggering recording: {ex.Message}");
                                    }
                                    
                                    return false;
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Canon camera is NOT recording (status={recordStatus})");
                                    return false;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error checking record status: {ex.Message}");
                            }
                        }
                    }
                }
                
                // If we can't check, assume it's not recording
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error in CheckCanonRecordingStatus: {ex.Message}");
                return false;
            }
        }
        
        private async Task DiagnoseCanonT6VideoCapabilities()
        {
            System.Diagnostics.Debug.WriteLine("[VIDEO] ===== CANON T6 VIDEO DIAGNOSTIC =====");
            
            try
            {
                await Task.Run(() =>
                {
                    var cameraType = _camera.GetType();
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera type: {cameraType.Name}");
                    
                    // Get device name
                    if (_camera?.DeviceName != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera model: {_camera.DeviceName}");
                        
                        // Check if it's a T6/1300D
                        if (_camera.DeviceName.Contains("T6") || _camera.DeviceName.Contains("1300D") || 
                            _camera.DeviceName.Contains("Rebel"))
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] DETECTED: Canon EOS Rebel T6 (1300D)");
                            System.Diagnostics.Debug.WriteLine("[VIDEO] NOTE: This camera model has limited SDK video recording support");
                            System.Diagnostics.Debug.WriteLine("[VIDEO] WORKAROUND: Manual mode dial adjustment to MOVIE mode may be required");
                            System.Diagnostics.Debug.WriteLine("[VIDEO] ALTERNATIVE: Consider using external recording tools or HDMI capture");
                        }
                    }
                    
                    // Try to get the Canon camera object for more diagnostics
                    var cameraField = cameraType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (cameraField != null)
                    {
                        var canonCamera = cameraField.GetValue(_camera);
                        if (canonCamera != null)
                        {
                            var canonCameraType = canonCamera.GetType();
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Internal Canon camera type: {canonCameraType.Name}");
                            
                            // Check if it's an old Canon model
                            var isOldCanonMethod = canonCameraType.GetMethod("IsOldCanon", 
                                BindingFlags.Public | BindingFlags.Instance);
                            
                            if (isOldCanonMethod != null)
                            {
                                var isOld = isOldCanonMethod.Invoke(canonCamera, null);
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Is old Canon model: {isOld}");
                            }
                            
                            // Check current recording capabilities
                            var getPropertyMethod = canonCameraType.GetMethod("GetProperty", 
                                BindingFlags.Public | BindingFlags.Instance);
                            
                            if (getPropertyMethod != null)
                            {
                                try
                                {
                                    // Check if PropID_Record is available
                                    const uint PropID_Record = 0x00000510;
                                    var recordStatus = getPropertyMethod.Invoke(canonCamera, new object[] { PropID_Record });
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] PropID_Record current value: {recordStatus}");
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] PropID_Record is available - SDK video recording might work");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] PropID_Record not available: {ex.Message}");
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] WARNING: This camera may not support SDK video recording");
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] The Canon T6 requires the mode dial to be physically set to MOVIE mode");
                                }
                                
                                try
                                {
                                    // Check live view status
                                    const uint PropID_Evf_Mode = 0x00000500;
                                    var liveViewStatus = getPropertyMethod.Invoke(canonCamera, new object[] { PropID_Evf_Mode });
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Live view mode: {liveViewStatus}");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Cannot check live view: {ex.Message}");
                                }
                            }
                        }
                    }
                });
                
                System.Diagnostics.Debug.WriteLine("[VIDEO] ===== DIAGNOSTIC COMPLETE =====");
                System.Diagnostics.Debug.WriteLine("[VIDEO] RECOMMENDATION: For Canon T6, ensure:");
                System.Diagnostics.Debug.WriteLine("[VIDEO] 1. Camera mode dial is physically set to MOVIE mode (video camera icon)");
                System.Diagnostics.Debug.WriteLine("[VIDEO] 2. Live view is enabled before attempting to record");
                System.Diagnostics.Debug.WriteLine("[VIDEO] 3. Camera firmware is up to date");
                System.Diagnostics.Debug.WriteLine("[VIDEO] 4. Memory card has sufficient space and write speed");
                System.Diagnostics.Debug.WriteLine("[VIDEO] NOTE: The T6 may require physical button press on camera to start/stop recording");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Diagnostic error: {ex.Message}");
            }
        }
        
        private async Task EnsureCanonMovieMode()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Attempting to switch Canon camera to movie mode...");
                
                var cameraType = _camera.GetType();
                
                // Try to use reflection to call Canon-specific movie mode commands
                var sendCommandMethod = cameraType.GetMethod("SendCommand", BindingFlags.Public | BindingFlags.Instance);
                if (sendCommandMethod == null)
                {
                    // Try to find it on the base camera object
                    var cameraField = cameraType.GetField("Camera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (cameraField != null)
                    {
                        var canonCamera = cameraField.GetValue(_camera);
                        if (canonCamera != null)
                        {
                            var canonCameraType = canonCamera.GetType();
                            sendCommandMethod = canonCameraType.GetMethod("SendCommand", BindingFlags.Public | BindingFlags.Instance);
                            
                            if (sendCommandMethod != null)
                            {
                                System.Diagnostics.Debug.WriteLine("[VIDEO] Found SendCommand on Canon camera object");
                                
                                // Try to send MovieSelectSwON command (value is 0x00000217)
                                const uint CameraCommand_MovieSelectSwON = 0x00000217;
                                
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] Sending MovieSelectSwON command...");
                                    // SendCommand might need two parameters: command and parameter
                                    var parameters = sendCommandMethod.GetParameters();
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] SendCommand expects {parameters.Length} parameters");
                                    
                                    if (parameters.Length == 1)
                                    {
                                        sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_MovieSelectSwON });
                                    }
                                    else if (parameters.Length == 2)
                                    {
                                        // Second parameter is usually 0 for no additional parameter
                                        sendCommandMethod.Invoke(canonCamera, new object[] { CameraCommand_MovieSelectSwON, 0 });
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Unexpected parameter count for SendCommand: {parameters.Length}");
                                    }
                                    
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] MovieSelectSwON command sent");
                                    await Task.Delay(500);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Error sending MovieSelectSwON: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                
                // Also try to set the mode property if available
                var modeProperty = _camera.Mode;
                if (modeProperty != null)
                {
                    // Check if the property can be set by checking if it has values
                    if (modeProperty.Values != null && modeProperty.Values.Count > 0)
                    {
                        var movieModeValue = modeProperty.Values.FirstOrDefault(v => 
                            v.ToLower().Contains("movie") || v == "20");
                        
                        if (movieModeValue != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Setting mode property to: {movieModeValue}");
                            try
                            {
                                modeProperty.SetValue(movieModeValue);
                                await Task.Delay(500);
                                System.Diagnostics.Debug.WriteLine("[VIDEO] Mode property set successfully");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error setting mode property: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] Mode property is read-only or has no values");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("[VIDEO] Movie mode configuration completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error ensuring movie mode: {ex.Message}");
                // Continue anyway - the camera might work without explicit mode switching
            }
        }
        
        private void ConfigureCanonSaveToPC()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Configuring Canon camera to save videos to PC...");
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera type: {_camera.GetType().FullName}");
                
                // Use reflection to call Canon-specific methods for setting save location
                var cameraType = _camera.GetType();
                
                // List all methods to see what's available
                var methods = cameraType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Available methods on camera:");
                foreach (var method in methods.Where(m => m.Name.Contains("Save") || m.Name.Contains("Host")))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO]   - {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                }
                
                // Try to call SavePicturesToHost if it exists
                var savePicturesToHostMethod = cameraType.GetMethod("SavePicturesToHost");
                if (savePicturesToHostMethod != null)
                {
                    // Get the event-based video folder (same structure as above)
                    string photoboothDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        "PhotoBooth"
                    );
                    
                    // Use the same event logic as above
                    string eventName;
                    if (_currentEvent != null && !string.IsNullOrEmpty(_currentEvent.Name))
                    {
                        eventName = GetSafeEventName(_currentEvent.Name);
                    }
                    else
                    {
                        eventName = $"Event_{DateTime.Now:yyyy-MM-dd}";
                    }
                    
                    string eventFolder = Path.Combine(photoboothDir, eventName);
                    string videoDir = Path.Combine(eventFolder, "videos");
                    
                    if (!Directory.Exists(videoDir))
                    {
                        Directory.CreateDirectory(videoDir);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Setting Canon save location to: {videoDir}");
                    savePicturesToHostMethod.Invoke(_camera, new object[] { videoDir });
                    System.Diagnostics.Debug.WriteLine("[VIDEO] Canon camera configured to save to PC successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] SavePicturesToHost method not found on Canon camera");
                    
                    // Try alternative methods - check for any SaveTo property
                    var saveToProperty = cameraType.GetProperty("SaveTo");
                    if (saveToProperty != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] Found SaveTo property, attempting to set to Host");
                        try
                        {
                            // Try to set SaveTo to Host (value 2)
                            saveToProperty.SetValue(_camera, 2);
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Set SaveTo property to Host (2)");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Error setting SaveTo property: {ex.Message}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] No SaveTo property found either");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error configuring Canon save location: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Stack trace: {ex.StackTrace}");
                // Continue anyway - the default might work
            }
        }
        
        private async Task CheckForCanonVideoDownload()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Checking for Canon video files on memory card...");
                
                if (_camera.GetType().Name.Contains("Canon"))
                {
                    // Give the camera time to finish writing the video file
                    await Task.Delay(2000);
                    
                    // Try to download the most recent video file from the camera
                    var downloaded = await DownloadLatestVideoFromCanon();
                    
                    if (downloaded)
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] Successfully downloaded video from Canon camera");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[VIDEO] No new video files found on Canon camera");
                        
                        // Try alternative method - check if GetFile method exists
                        var cameraType = _camera.GetType();
                        var getFileMethod = cameraType.GetMethod("GetFile", BindingFlags.Public | BindingFlags.Instance);
                        
                        if (getFileMethod != null)
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Found GetFile method, attempting to use it");
                            
                            // Look for video files with common naming patterns
                            var possibleNames = new[]
                            {
                                $"MVI_{DateTime.Now:yyyyMMdd}*.MP4",
                                $"MVI_{DateTime.Now:yyyyMMdd}*.MOV",
                                "*.MP4",
                                "*.MOV"
                            };
                            
                            foreach (var pattern in possibleNames)
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Looking for files matching: {pattern}");
                                // Note: GetFile needs exact filename, so this is just for documentation
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error checking for Canon video download: {ex.Message}");
            }
        }
        
        private async Task<bool> DownloadLatestVideoFromCanon()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[VIDEO] Attempting to download latest video from Canon camera...");
                
                // Wait for camera to update its file list after recording
                await Task.Delay(2000);
                System.Diagnostics.Debug.WriteLine("[VIDEO] Refreshing camera file list...");
                
                // Get all objects from the camera's memory card
                var objects = _camera.GetObjects(null, false);
                
                if (objects == null || objects.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] No files found on camera");
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Found {objects.Count} files on camera");
                
                // First, let's see what files are actually on the camera
                System.Diagnostics.Debug.WriteLine("[VIDEO] Listing last 10 files on camera:");
                int startIndex = Math.Max(0, objects.Count - 10);
                for (int i = startIndex; i < objects.Count; i++)
                {
                    if (objects[i].FileName != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VIDEO]   - {objects[i].FileName}");
                    }
                }
                
                // Filter for video files (MP4, MOV, AVI, MTS, M2TS)
                // Canon cameras might use MTS or M2TS for AVCHD format
                var videoFiles = new List<DeviceObject>();
                foreach (var obj in objects)
                {
                    if (obj.FileName != null)
                    {
                        var fileName = obj.FileName.ToUpper();
                        var ext = Path.GetExtension(fileName);
                        
                        // Check for various video extensions and patterns
                        bool isVideo = ext == ".MP4" || ext == ".MOV" || ext == ".AVI" || 
                                      ext == ".MTS" || ext == ".M2TS" || ext == ".MPG" ||
                                      ext == ".MPEG" || ext == ".MXF" ||
                                      fileName.StartsWith("MVI_") || fileName.StartsWith("MOV_") ||
                                      fileName.StartsWith("VID_");
                        
                        if (isVideo)
                        {
                            videoFiles.Add(obj);
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Found video file: {obj.FileName}");
                        }
                    }
                }
                
                if (videoFiles.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] No video files found on camera");
                    return false;
                }
                
                // Sort video files alphabetically by filename - this will put the highest numbered file last
                videoFiles.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
                
                // Log all video files after sorting
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Found {videoFiles.Count} video files (sorted):");
                for (int i = 0; i < Math.Min(5, videoFiles.Count); i++)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO]   [{i}]: {videoFiles[i].FileName}");
                }
                
                // Get the most recent video file 
                // The video files are now sorted alphabetically, so the highest numbered file is the newest
                // MVI_9371 is newer than MVI_9370, so we need the LAST item in the sorted list
                var latestVideo = videoFiles[videoFiles.Count - 1];  // Take the last video, which is the newest
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Selected video for download: {latestVideo.FileName} (newest)");
                
                // Download the video file to our expected location
                if (!string.IsNullOrEmpty(_currentVideoPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Downloading {latestVideo.FileName} to {_currentVideoPath}...");
                    
                    await Task.Run(() =>
                    {
                        try
                        {
                            _camera.TransferFile(latestVideo.Handle, _currentVideoPath);
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Download completed successfully");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Error during file transfer: {ex.Message}");
                            
                            // Try alternative download using GetFile if available
                            var cameraType = _camera.GetType();
                            var getFileMethod = cameraType.GetMethod("GetFile", 
                                BindingFlags.Public | BindingFlags.Instance,
                                null,
                                new Type[] { typeof(string), typeof(string) },
                                null);
                            
                            if (getFileMethod != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Trying GetFile method with {latestVideo.FileName}");
                                getFileMethod.Invoke(_camera, new object[] { latestVideo.FileName, _currentVideoPath });
                            }
                        }
                    });
                    
                    // Check if the file was downloaded successfully
                    if (File.Exists(_currentVideoPath))
                    {
                        var fileInfo = new FileInfo(_currentVideoPath);
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] *** SUCCESS! Video downloaded: {_currentVideoPath} ***");
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] File size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
                        
                        // Generate thumbnail now that video exists
                        await GenerateVideoThumbnail(_currentVideoPath);
                        
                        // Compress and save webupload version
                        await CompressAndSaveWebUploadVersion(_currentVideoPath);
                        
                        // Save video path to database with duration
                        var duration = DateTime.Now - _recordingStartTime;
                        SaveVideoToDatabase(_currentVideoPath, fileInfo.Length, (int)duration.TotalSeconds);
                        
                        // Don't end the session here - it's managed by PhotoboothSessionService
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Video saved to database session {_currentSessionId}");
                        
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error downloading video from Canon: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Stack trace: {ex.StackTrace}");
                return false;
            }
        }
        
        private async Task SwitchBackToPhotoModeIfNeeded()
        {
            try
            {
                // Check if it's a Canon camera
                if (_camera.GetType().Name.Contains("Canon"))
                {
                    var modeProperty = _camera.Mode;
                    if (modeProperty != null)
                    {
                        // Check current mode first
                        var currentMode = modeProperty.Value;
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Current camera mode: {currentMode}");
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Original stored mode: {_originalCameraMode}");
                        
                        // Debug: Show all available modes
                        if (modeProperty.Values != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[VIDEO] Available camera modes: {string.Join(", ", modeProperty.Values)}");
                        }
                        
                        // If we have the original mode stored, try to restore it
                        if (!string.IsNullOrEmpty(_originalCameraMode))
                        {
                            // Check if the current mode is already the original mode
                            if (currentMode != null && currentMode.Equals(_originalCameraMode, StringComparison.OrdinalIgnoreCase))
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Camera already in original mode ({_originalCameraMode}), keeping it");
                                return;
                            }
                            
                            // Try to restore the original mode
                            if (modeProperty.Values?.Contains(_originalCameraMode) == true)
                            {
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Restoring original camera mode: {_originalCameraMode}");
                                    modeProperty.SetValue(_originalCameraMode);
                                    await Task.Delay(500);
                                    System.Diagnostics.Debug.WriteLine("[VIDEO] Successfully restored original camera mode");
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Failed to restore original mode: {ex.Message}");
                                    // Fall through to default mode selection
                                }
                            }
                        }
                        
                        // Fallback: If current mode is already Manual (M), don't change it
                        if (currentMode != null && currentMode.Equals("M", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] Camera is in Manual mode (M), keeping it");
                            return;
                        }
                        
                        // Priority order: Manual (M) > Program > Av > Tv
                        // Look for Manual mode first to preserve user's dial setting
                        var photoMode = modeProperty.Values?.FirstOrDefault(v => 
                            v.Equals("M", StringComparison.OrdinalIgnoreCase)) ??
                        modeProperty.Values?.FirstOrDefault(v => 
                            v.ToLower().Contains("program")) ??
                        modeProperty.Values?.FirstOrDefault(v => 
                            v.ToLower().Contains("av")) ??
                        modeProperty.Values?.FirstOrDefault(v => 
                            v.ToLower().Contains("tv"));
                        
                        if (photoMode != null)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Switching back to photo mode: {photoMode}");
                                modeProperty.SetValue(photoMode);
                                await Task.Delay(500);
                                System.Diagnostics.Debug.WriteLine("[VIDEO] Successfully switched back to photo mode");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[VIDEO] Failed to switch back to photo mode: {ex.Message}");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[VIDEO] No suitable photo mode found in available modes");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error switching back to photo mode: {ex.Message}");
            }
        }
        
        private bool IsVideoRecordingSupported()
        {
            try
            {
                // Check if camera has the StartRecordMovie method
                if (_camera == null)
                    return false;
                
                // For Canon cameras, check if it's a model that supports video
                string cameraName = _camera.GetType().Name;
                if (cameraName.Contains("Canon"))
                {
                    // Most modern Canon DSLRs support video, but some older ones don't
                    // You might want to check specific model capabilities here
                    string deviceName = _camera.DeviceName?.ToLower() ?? "";
                    
                    // These older models don't support video
                    string[] nonVideoModels = { "40d", "30d", "20d", "10d", "5d", "350d", "400d" };
                    foreach (var model in nonVideoModels)
                    {
                        if (deviceName.Contains(model))
                            return false;
                    }
                }
                
                // For other camera types, assume they support video if they have the method
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private void OnRecordingTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_isRecording && !_isStopping) // Check _isStopping to prevent multiple calls
            {
                // Check if we've reached max duration FIRST - this is the primary mechanism
                if (MaxDuration > TimeSpan.Zero && ElapsedTime >= MaxDuration)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] *** MAX DURATION REACHED ({MaxDuration.TotalSeconds}s) - STOPPING CAMERA ***");
                    // DO NOT set _isRecording = false here! Let StopRecordingAsync handle it
                    _ = StopRecordingAsync(); // This will handle stopping timer and setting flags
                    return;
                }
                
                // Check if camera has stopped recording on its own (secondary check for Canon cameras)
                if (!CheckCameraRecordingStatus())
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] *** CAMERA STOPPED RECORDING - SYNCING SERVICE STATE ***");
                    // DO NOT set _isRecording = false here! Let StopRecordingAsync handle it
                    _ = StopRecordingAsync();
                    return; // Don't send progress update if camera stopped
                }
                
                // Send progress update
                RecordingProgress?.Invoke(this, ElapsedTime);
            }
        }
        
        /// <summary>
        /// Check if the camera is actually still recording
        /// </summary>
        private bool CheckCameraRecordingStatus()
        {
            try
            {
                if (_camera == null) return false;
                
                // For Canon cameras, access the EosCamera.GetProperty method directly
                if (_camera.GetType().Name.Contains("Canon"))
                {
                    // Get the Camera property (EosCamera) from the Canon device
                    var cameraProperty = _camera.GetType().GetProperty("Camera", BindingFlags.Public | BindingFlags.Instance);
                    if (cameraProperty != null)
                    {
                        var eosCamera = cameraProperty.GetValue(_camera);
                        if (eosCamera != null)
                        {
                            // Call GetProperty on the EosCamera object
                            var getPropertyMethod = eosCamera.GetType().GetMethod("GetProperty", BindingFlags.Public | BindingFlags.Instance);
                            if (getPropertyMethod != null)
                            {
                                // Use Edsdk.PropID_Record constant (0x00000510)
                                const uint PropID_Record = 0x00000510;
                                var recordStatus = getPropertyMethod.Invoke(eosCamera, new object[] { PropID_Record });
                                int status = recordStatus != null ? Convert.ToInt32(recordStatus) : 0;
                                
                                // Canon T6: Status 4 = recording, Status 0 = not recording
                                bool isRecording = status == 4;
                                
                                // Only log when status changes to reduce noise
                                if (!isRecording)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[VIDEO] *** CAMERA STOPPED RECORDING - Status: {status} at {DateTime.Now:HH:mm:ss.fff} ***");
                                }
                                return isRecording;
                            }
                        }
                    }
                }
                
                // For other cameras or if property check fails, assume still recording
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error checking camera recording status: {ex.Message}");
                // If we can't check, assume still recording to avoid premature stop
                return true;
            }
        }
        
        private void SavePhotoSettings()
        {
            if (_camera == null) return;
            
            try
            {
                _savedPhotoSettings = new PhotoSettings
                {
                    ISO = _camera.IsoNumber?.Value ?? "Auto",
                    Aperture = _camera.FNumber?.Value ?? "Auto",
                    ShutterSpeed = _camera.ShutterSpeed?.Value ?? "Auto",
                    WhiteBalance = _camera.WhiteBalance?.Value ?? "Auto",
                    FocusMode = _camera.FocusMode?.Value ?? "Auto",
                    ExposureCompensation = _camera.ExposureCompensation?.Value ?? "0"
                };
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Saved photo settings - ISO: {_savedPhotoSettings.ISO}, Aperture: {_savedPhotoSettings.Aperture}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error saving photo settings: {ex.Message}");
            }
        }
        
        private void ApplyVideoSettings()
        {
            if (_camera == null) return;
            
            try
            {
                // Load video settings from Properties.Settings
                var settings = Settings.Default;
                
                // Apply each video setting if it's not "Auto"
                if (!string.IsNullOrEmpty(settings.VideoISO) && settings.VideoISO != "Auto")
                {
                    SetPropertyToValue(_camera.IsoNumber, settings.VideoISO);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoAperture) && settings.VideoAperture != "Auto")
                {
                    SetPropertyToValue(_camera.FNumber, settings.VideoAperture);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoShutterSpeed) && settings.VideoShutterSpeed != "Auto")
                {
                    SetPropertyToValue(_camera.ShutterSpeed, settings.VideoShutterSpeed);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoWhiteBalance) && settings.VideoWhiteBalance != "Auto")
                {
                    SetPropertyToValue(_camera.WhiteBalance, settings.VideoWhiteBalance);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoFocusMode))
                {
                    SetPropertyToValue(_camera.FocusMode, settings.VideoFocusMode);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoExposureCompensation))
                {
                    SetPropertyToValue(_camera.ExposureCompensation, settings.VideoExposureCompensation);
                }
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Applied video settings - ISO: {settings.VideoISO}, Aperture: {settings.VideoAperture}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error applying video settings: {ex.Message}");
            }
        }
        
        private void RestorePhotoSettings()
        {
            if (_camera == null || _savedPhotoSettings == null) return;
            
            try
            {
                SetPropertyToValue(_camera.IsoNumber, _savedPhotoSettings.ISO);
                SetPropertyToValue(_camera.FNumber, _savedPhotoSettings.Aperture);
                SetPropertyToValue(_camera.ShutterSpeed, _savedPhotoSettings.ShutterSpeed);
                SetPropertyToValue(_camera.WhiteBalance, _savedPhotoSettings.WhiteBalance);
                SetPropertyToValue(_camera.FocusMode, _savedPhotoSettings.FocusMode);
                SetPropertyToValue(_camera.ExposureCompensation, _savedPhotoSettings.ExposureCompensation);
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Restored photo settings - ISO: {_savedPhotoSettings.ISO}, Aperture: {_savedPhotoSettings.Aperture}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error restoring photo settings: {ex.Message}");
            }
        }
        
        private void SetPropertyToValue(PropertyValue<long> property, string targetValue)
        {
            if (property == null || !property.IsEnabled || string.IsNullOrEmpty(targetValue)) return;
            
            try
            {
                // Find the target value in the property's available values
                foreach (var value in property.Values)
                {
                    if (value == targetValue)
                    {
                        property.Value = targetValue;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error setting property value: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isRecording)
            {
                _ = StopRecordingAsync();
            }
            
            _recordingTimer?.Dispose();
        }
        
        /// <summary>
        /// Sanitize event name for safe folder creation
        /// </summary>
        private string GetSafeEventName(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return "Default_Event";
            
            // Remove invalid path characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string safe = eventName;
            
            foreach (char c in invalidChars)
            {
                safe = safe.Replace(c.ToString(), "");
            }
            
            // Also remove problematic characters
            safe = safe.Replace(":", "")
                      .Replace("*", "")
                      .Replace("?", "")
                      .Replace("\"", "")
                      .Replace("<", "")
                      .Replace(">", "")
                      .Replace("|", "")
                      .Replace("/", "_")
                      .Replace("\\", "_")
                      .Trim();
            
            // Ensure it's not empty after cleaning
            if (string.IsNullOrWhiteSpace(safe))
                safe = "Event";
            
            return safe;
        }
        
        /// <summary>
        /// Generate thumbnail for video
        /// </summary>
        private async Task<string> GenerateVideoThumbnail(string videoPath)
        {
            try
            {
                if (!File.Exists(videoPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Cannot generate thumbnail - video not found: {videoPath}");
                    return null;
                }
                
                // Check if VideoCompressionService is available (it has FFmpeg)
                var compressionService = VideoCompressionService.Instance;
                if (compressionService == null || !compressionService.IsFFmpegAvailable())
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] FFmpeg not available for thumbnail generation");
                    return null;
                }
                
                // Generate thumbnail path
                string videoDir = Path.GetDirectoryName(videoPath);
                string videoName = Path.GetFileNameWithoutExtension(videoPath);
                string thumbnailPath = Path.Combine(videoDir, $"{videoName}_thumb.jpg");
                
                // Check if thumbnail already exists
                if (File.Exists(thumbnailPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Thumbnail already exists: {thumbnailPath}");
                    return thumbnailPath;
                }
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Generating thumbnail for: {videoPath}");
                
                // Generate thumbnail at 2 seconds into the video
                string result = await compressionService.GenerateThumbnailAsync(videoPath, thumbnailPath, 2);
                
                if (!string.IsNullOrEmpty(result) && File.Exists(thumbnailPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Thumbnail generated successfully: {thumbnailPath}");
                    
                    // Update database with thumbnail path if we have a session
                    if (_currentSessionId > 0)
                    {
                        var fileInfo = new FileInfo(videoPath);
                        var duration = DateTime.Now - _recordingStartTime;
                        _database.UpdateSessionWithVideoData(_currentSessionId, videoPath, thumbnailPath, 
                            fileInfo.Length, (int)duration.TotalSeconds);
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Updated database with thumbnail path");
                    }
                    
                    return thumbnailPath;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] Thumbnail generation failed");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error generating thumbnail: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Compress video and save as webupload version
        /// </summary>
        private async Task CompressAndSaveWebUploadVersion(string originalVideoPath)
        {
            try
            {
                if (!File.Exists(originalVideoPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Cannot compress - original video not found: {originalVideoPath}");
                    return;
                }
                
                // Check if VideoCompressionService is available
                var compressionService = VideoCompressionService.Instance;
                if (compressionService == null)
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] VideoCompressionService not available");
                    return;
                }
                
                // Generate webupload filename in subfolder
                string directory = Path.GetDirectoryName(originalVideoPath);
                string eventFolder = Path.GetDirectoryName(directory); // Go up from videos folder to event folder
                string webUploadFolder = Path.Combine(eventFolder, "webupload");
                
                // Create webupload folder if it doesn't exist
                if (!Directory.Exists(webUploadFolder))
                {
                    Directory.CreateDirectory(webUploadFolder);
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Created webupload folder: {webUploadFolder}");
                }
                
                string filename = Path.GetFileNameWithoutExtension(originalVideoPath);
                string webUploadPath = Path.Combine(webUploadFolder, $"{filename}_webupload.mp4");
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Starting compression of {originalVideoPath}");
                System.Diagnostics.Debug.WriteLine($"[VIDEO] WebUpload output: {webUploadPath}");
                
                // Compress the video
                string compressedPath = await compressionService.CompressVideoAsync(originalVideoPath, webUploadPath);
                
                if (!string.IsNullOrEmpty(compressedPath) && File.Exists(webUploadPath))
                {
                    var originalSize = new FileInfo(originalVideoPath).Length;
                    var compressedSize = new FileInfo(webUploadPath).Length;
                    double compressionRatio = (1.0 - (double)compressedSize / originalSize) * 100;
                    
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Compression successful!");
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Original size: {originalSize / 1024.0 / 1024.0:F2} MB");
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Compressed size: {compressedSize / 1024.0 / 1024.0:F2} MB");
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Compression ratio: {compressionRatio:F1}%");
                    
                    // Also save compressed video path to database
                    if (_currentSessionId > 0)
                    {
                        SaveVideoToDatabase(webUploadPath, compressedSize, 0, "WebUpload");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] Compression failed or output file not created");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error compressing video: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save video path to database (like photos)
        /// </summary>
        private void SaveVideoToDatabase(string videoPath, long fileSize, int durationSeconds, string videoType = "Original")
        {
            try
            {
                if (_currentSessionId <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] No session ID - cannot save to database");
                    return;
                }
                
                if (!File.Exists(videoPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Video file not found, cannot save to database: {videoPath}");
                    return;
                }
                
                // Extract just the filename for database
                string fileName = Path.GetFileName(videoPath);
                
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Saving video to database - Session: {_currentSessionId}, File: {fileName}, Type: {videoType}");
                
                // Use the Photos table to store video path (videos and photos share same structure)
                // PhotoType field will indicate it's a video
                var photo = new Database.PhotoData
                {
                    SessionId = _currentSessionId,
                    FileName = fileName,
                    FilePath = videoPath,
                    FileSize = fileSize,
                    PhotoType = $"Video_{videoType}", // Mark as Video_Original or Video_WebUpload
                    SequenceNumber = 1,
                    IsActive = true
                };
                
                int photoId = _database.SavePhoto(photo);
                
                if (photoId > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[VIDEO] Successfully saved to database with ID: {photoId}");
                    
                    // Update session with video-specific data if it's the original video
                    if (videoType == "Original" && durationSeconds > 0)
                    {
                        // Generate thumbnail path
                        string thumbnailPath = Path.ChangeExtension(videoPath, ".jpg");
                        
                        // Update session with video metadata
                        _database.UpdateSessionWithVideoData(_currentSessionId, videoPath, thumbnailPath, fileSize, durationSeconds);
                        System.Diagnostics.Debug.WriteLine($"[VIDEO] Updated session with video metadata");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[VIDEO] Failed to save video to database");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIDEO] Error saving video to database: {ex.Message}");
            }
        }
    }

    public class VideoRecordingEventArgs : EventArgs
    {
        public string FilePath { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
    }
}