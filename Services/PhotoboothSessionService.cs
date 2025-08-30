using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Database;
using Photobooth.Models;

namespace Photobooth.Services
{
    /// <summary>
    /// Clean service that handles complete photobooth session workflow
    /// Encapsulates session management, photo capture coordination, and completion
    /// </summary>
    public class PhotoboothSessionService
    {
        #region Events
        public event EventHandler<SessionStartedEventArgs> SessionStarted;
        public event EventHandler<PhotoProcessedEventArgs> PhotoProcessed;
        public event EventHandler<SessionCompletedEventArgs> SessionCompleted;
        public event EventHandler<SessionErrorEventArgs> SessionError;
        public event EventHandler<EventArgs> SessionCleared;
        public event EventHandler<EventArgs> AutoClearTimerExpired;
        public event EventHandler<AnimationReadyEventArgs> AnimationReady;
        #endregion

        #region Services
        private readonly PhotoCaptureService _photoCaptureService;
        private readonly DatabaseOperations _databaseOperations;
        private readonly SessionManager _sessionManager;
        private readonly PhotoFilterServiceHybrid _filterService;
        #endregion

        #region State
        private string _currentSessionId;
        private List<string> _capturedPhotoPaths;
        private int _currentPhotoIndex;
        private int _totalPhotosRequired;
        private bool _isSessionActive;
        private EventData _currentEvent;
        private TemplateData _currentTemplate;
        private string _composedImagePath;
        private string _composedImagePrintPath; // Separate path for printing (e.g., 4x6 duplicate of 2x6)
        private string _gifPath;
        
        // Template format tracking for proper printer routing
        private bool _isCurrentTemplate2x6;
        
        // Filter selection
        private FilterType _selectedFilter = FilterType.None;
        
        // Auto-clear timer
        private DispatcherTimer _autoClearTimer;
        private int _autoClearElapsedSeconds;
        #endregion

        #region Properties
        public string CurrentSessionId => _currentSessionId;
        public List<string> CapturedPhotoPaths => new List<string>(_capturedPhotoPaths);
        public List<string> AllSessionFiles => GetAllSessionFiles();
        public int CurrentPhotoIndex => _currentPhotoIndex;
        public int TotalPhotosRequired => _totalPhotosRequired;
        public bool IsSessionActive => _isSessionActive;
        public EventData CurrentEvent => _currentEvent;
        public TemplateData CurrentTemplate => _currentTemplate;
        public bool IsCurrentTemplate2x6 => _isCurrentTemplate2x6;
        public string ComposedImagePath => _composedImagePath;
        public string ComposedImagePrintPath => _composedImagePrintPath; // Path to use for printing (may be 4x6 duplicate)
        public FilterType SelectedFilter => _selectedFilter;
        #endregion

        public PhotoboothSessionService()
        {
            _databaseOperations = new DatabaseOperations();
            _photoCaptureService = new PhotoCaptureService(_databaseOperations);
            _sessionManager = new SessionManager();
            _filterService = new PhotoFilterServiceHybrid();
            _capturedPhotoPaths = new List<string>();
            
            // Initialize auto-clear timer
            _autoClearTimer = new DispatcherTimer();
            _autoClearTimer.Interval = TimeSpan.FromSeconds(1);
            _autoClearTimer.Tick += AutoClearTimer_Tick;
            
            // Subscribe to session manager events
            _sessionManager.SessionStarted += OnSessionManagerSessionStarted;
            _sessionManager.SessionCompleted += OnSessionManagerSessionCompleted;
        }

        /// <summary>
        /// Start a new photo session
        /// </summary>
        public async Task<bool> StartSessionAsync(EventData eventData, TemplateData templateData, int totalPhotos)
        {
            try
            {
                if (_isSessionActive)
                {
                    throw new InvalidOperationException("Session already active");
                }

                // Initialize session
                _currentEvent = eventData;
                _currentTemplate = templateData;
                _totalPhotosRequired = totalPhotos;
                _currentPhotoIndex = 0;
                _capturedPhotoPaths.Clear();
                
                // Determine if this is a 2x6 template based on aspect ratio
                _isCurrentTemplate2x6 = false;
                if (_currentTemplate != null)
                {
                    double ratio = _currentTemplate.CanvasWidth / _currentTemplate.CanvasHeight;
                    _isCurrentTemplate2x6 = Math.Abs(ratio - 0.333) < 0.1; // 2x6 strip (2/6 = 0.333)
                    Log.Debug($"PhotoboothSessionService: Template aspect ratio: {ratio:F3}, Is2x6: {_isCurrentTemplate2x6}");
                }

                // Create database session
                _databaseOperations.CreateSession(eventData?.Id, templateData?.Id);
                _currentSessionId = _databaseOperations.CurrentSessionGuid?.ToString();

                _isSessionActive = true;
                Log.Debug($"★★★ Session marked as active - SessionId: {_currentSessionId}, _isSessionActive: {_isSessionActive}");

                // Notify session started
                SessionStarted?.Invoke(this, new SessionStartedEventArgs
                {
                    SessionId = _currentSessionId,
                    Event = _currentEvent,
                    Template = _currentTemplate,
                    TotalPhotos = _totalPhotosRequired
                });

                Log.Debug($"PhotoboothSessionService: Session started - ID: {_currentSessionId}, Photos: {_totalPhotosRequired}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothSessionService: Failed to start session: {ex.Message}");
                SessionError?.Invoke(this, new SessionErrorEventArgs { Error = ex, Operation = "StartSession" });
                return false;
            }
        }

        /// <summary>
        /// Process a captured photo and advance session
        /// </summary>
        public async Task<bool> ProcessCapturedPhotoAsync(PhotoCapturedEventArgs photoEventArgs)
        {
            try
            {
                if (!_isSessionActive)
                {
                    throw new InvalidOperationException("No active session");
                }

                Log.Debug($"PhotoboothSessionService: Processing photo {_currentPhotoIndex + 1} of {_totalPhotosRequired}");

                // Use PhotoCaptureService to process the photo with event context for proper folder structure
                string processedPhotoPath = _photoCaptureService.ProcessCapturedPhoto(photoEventArgs, _currentEvent);

                if (string.IsNullOrEmpty(processedPhotoPath))
                {
                    throw new Exception("PhotoCaptureService returned empty path");
                }
                
                // Apply Beauty Mode if enabled (before adding to session)
                if (Properties.Settings.Default.BeautyModeEnabled)
                {
                    Log.Debug($"Applying Beauty Mode to photo with intensity {Properties.Settings.Default.BeautyModeIntensity}");
                    BeautyModeService.Instance.ApplyBeautyMode(
                        processedPhotoPath, 
                        processedPhotoPath, 
                        Properties.Settings.Default.BeautyModeIntensity);
                }

                // Add to session tracking
                _capturedPhotoPaths.Add(processedPhotoPath);
                _currentPhotoIndex++;

                // Notify photo processed
                PhotoProcessed?.Invoke(this, new PhotoProcessedEventArgs
                {
                    SessionId = _currentSessionId,
                    PhotoPath = processedPhotoPath,
                    PhotoIndex = _currentPhotoIndex,
                    TotalPhotos = _totalPhotosRequired,
                    IsComplete = _currentPhotoIndex >= _totalPhotosRequired
                });

                Log.Debug($"PhotoboothSessionService: Photo {_currentPhotoIndex} processed successfully");

                // Check if session is complete
                if (_currentPhotoIndex >= _totalPhotosRequired)
                {
                    Log.Debug($"★★★ SESSION PHOTOS COMPLETE: Starting ProcessSessionPhotosAsync ★★★");
                    Log.Debug($"  Photo index: {_currentPhotoIndex}, Total required: {_totalPhotosRequired}");
                    // Process all captured photos (GIF generation only)
                    await ProcessSessionPhotosAsync();
                    // Don't complete yet - wait for page to compose template first
                    Log.Debug("PhotoboothSessionService: All photos captured, waiting for composition before completing");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothSessionService: Failed to process photo: {ex.Message}");
                SessionError?.Invoke(this, new SessionErrorEventArgs { Error = ex, Operation = "ProcessPhoto" });
                return false;
            }
        }

        /// <summary>
        /// Process session photos - compose template and generate GIF
        /// </summary>
        private async Task ProcessSessionPhotosAsync()
        {
            try
            {
                Log.Debug("PhotoboothSessionService: Processing session photos");
                
                // Note: Filter selection is handled by the UI layer before composition
                // The service focuses on GIF generation after photos are processed
                
                // Generate GIF/MP4 in background if enabled (non-blocking)
                if (Properties.Settings.Default.EnableGifGeneration && _capturedPhotoPaths.Count > 1)
                {
                    Log.Debug($"★★★ PhotoboothSessionService: Starting animated GIF/MP4 generation in background. Captured photos count: {_capturedPhotoPaths.Count}");
                    Log.Debug($"★★★ EnableGifGeneration setting: {Properties.Settings.Default.EnableGifGeneration}");
                    
                    // Fire and forget - generate animation in background and notify UI when ready
                    _ = Task.Run(async () => 
                    {
                        Log.Debug($"★★★ Background task started for animation generation");
                        try
                        {
                            _gifPath = await GenerateGifAsync();
                            if (!string.IsNullOrEmpty(_gifPath))
                            {
                                Log.Debug($"★★★ PhotoboothSessionService: Animation generated in background: {_gifPath}");
                                
                                // Notify UI that animation is ready (fire event)
                                Log.Debug($"★★★ Firing AnimationReady event for: {_gifPath}");
                                AnimationReady?.Invoke(this, new AnimationReadyEventArgs
                                {
                                    AnimationPath = _gifPath,
                                    SessionId = _currentSessionId
                                });
                                Log.Debug($"★★★ AnimationReady event fired");
                            }
                            else
                            {
                                Log.Debug($"★★★ Animation generation returned empty path");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"PhotoboothSessionService: Background animation generation failed: {ex.Message}");
                        }
                    });
                }
                
                // Note: Template composition is handled by PhotoProcessingOperations
                // which requires UI parent reference, so it's called from the page
                Log.Debug("PhotoboothSessionService: Template composition will be handled by the page");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothSessionService: Failed to process session photos: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Generate animated GIF/MP4 from captured photos (prefers MP4 for better quality)
        /// </summary>
        public async Task<string> GenerateGifAsync()
        {
            try
            {
                if (_capturedPhotoPaths == null || _capturedPhotoPaths.Count < 2)
                {
                    Log.Debug("PhotoboothSessionService: Not enough photos for animation generation");
                    return null;
                }
                
                // Use event-based folder structure - place animations in "animation" subfolder
                string outputDir = GetEventAnimationFolder();
                
                // Generate unique filename to prevent conflicts
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string sessionId = _currentSessionId ?? Guid.NewGuid().ToString("N").Substring(0, 8);
                
                // Try MP4 first (better quality, smaller file size)
                string mp4Path = Path.Combine(
                    outputDir,
                    $"animated_{timestamp}_{sessionId}.mp4"
                );
                
                // If file still exists, add random suffix
                if (File.Exists(mp4Path))
                {
                    string randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 4);
                    mp4Path = Path.Combine(
                        outputDir,
                        $"animated_{timestamp}_{sessionId}_{randomSuffix}.mp4"
                    );
                }
                
                // Try to generate MP4 first
                Log.Debug("PhotoboothSessionService: Attempting MP4 generation");
                int frameDelay = Properties.Settings.Default.GifFrameDelay;
                
                string result = await Task.Run(() => 
                    VideoGenerationService.GenerateLoopingMP4(
                        _capturedPhotoPaths,
                        mp4Path,
                        frameDelay
                    )
                );
                
                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                {
                    Log.Debug($"PhotoboothSessionService: MP4 generated successfully at {result}");
                    
                    // Save MP4 to database 
                    _databaseOperations.SaveAnimation(result, "MP4");
                    Log.Debug($"PhotoboothSessionService: Saved MP4 to database: {result}");
                    
                    return result;
                }
                
                // Fallback to GIF if MP4 fails (no FFmpeg)
                Log.Debug("PhotoboothSessionService: MP4 generation failed, falling back to GIF");
                string gifPath = Path.Combine(
                    outputDir,
                    $"animated_{timestamp}_{sessionId}.gif"
                );
                
                if (File.Exists(gifPath))
                {
                    string randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 4);
                    gifPath = Path.Combine(
                        outputDir,
                        $"animated_{timestamp}_{sessionId}_{randomSuffix}.gif"
                    );
                }
                
                int maxWidth = Properties.Settings.Default.GifMaxWidth;
                int maxHeight = Properties.Settings.Default.GifMaxHeight;
                int quality = Properties.Settings.Default.GifQuality;
                
                // Use GifGenerationService as fallback
                result = await Task.Run(() => 
                    GifGenerationService.GenerateAnimatedGif(
                        _capturedPhotoPaths, 
                        gifPath, 
                        frameDelay, 
                        maxWidth, 
                        maxHeight, 
                        quality
                    )
                );
                
                Log.Debug($"PhotoboothSessionService: Animation generated at {result}");
                
                // Save animation to database with correct format
                if (!string.IsNullOrEmpty(result) && File.Exists(result))
                {
                    // Determine format based on file extension
                    string format = Path.GetExtension(result).ToLower() == ".mp4" ? "MP4" : "GIF";
                    _databaseOperations.SaveAnimation(result, format);
                    Log.Debug($"PhotoboothSessionService: Saved {format} animation to database: {result}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothSessionService: Failed to generate GIF: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Get the generated GIF path
        /// </summary>
        public string GetGifPath()
        {
            return _gifPath;
        }
        
        /// <summary>
        /// Get all session files including photos, composed images, and animations
        /// </summary>
        private List<string> GetAllSessionFiles()
        {
            var allFiles = new List<string>();
            
            Log.Debug($"GetAllSessionFiles: Starting file collection");
            Log.Debug($"  - _capturedPhotoPaths count: {_capturedPhotoPaths?.Count ?? 0}");
            Log.Debug($"  - _composedImagePath: {_composedImagePath ?? "NULL"}");
            Log.Debug($"  - _gifPath: {_gifPath ?? "NULL"}");
            
            // Add original photos
            if (_capturedPhotoPaths != null)
            {
                allFiles.AddRange(_capturedPhotoPaths);
                Log.Debug($"  - Added {_capturedPhotoPaths.Count} original photos");
            }
            
            // Add composed image if exists
            if (!string.IsNullOrEmpty(_composedImagePath) && File.Exists(_composedImagePath))
            {
                allFiles.Add(_composedImagePath);
                Log.Debug($"  - Including composed image for upload: {_composedImagePath}");
            }
            else
            {
                Log.Debug($"  - Composed image NOT included: Path empty or file doesn't exist");
            }
            
            // Add GIF/MP4 if exists
            if (!string.IsNullOrEmpty(_gifPath) && File.Exists(_gifPath))
            {
                allFiles.Add(_gifPath);
                Log.Debug($"  - Including animation for upload: {_gifPath}");
            }
            else
            {
                Log.Debug($"  - Animation NOT included: Path empty or file doesn't exist");
            }
            
            Log.Debug($"GetAllSessionFiles: Total files to upload: {allFiles.Count}");
            foreach (var file in allFiles)
            {
                Log.Debug($"  - {file}");
            }
            
            return allFiles;
        }
        
        /// <summary>
        /// Set the composed image path (called from page after composition)
        /// </summary>
        public void SetComposedImagePath(string path)
        {
            _composedImagePath = path;
            
            // Save composed image to database
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _databaseOperations.SaveComposedImage(path, "4x6");
                Log.Debug($"PhotoboothSessionService: Saved composed image to database: {path}");
            }
        }
        
        /// <summary>
        /// Set both display and print paths (when they differ, e.g., 2x6 duplicated to 4x6)
        /// </summary>
        public void SetComposedImagePaths(string displayPath, string printPath)
        {
            Log.Debug($"★★★ SetComposedImagePaths CALLED ★★★");
            Log.Debug($"  - displayPath: {displayPath}");
            Log.Debug($"  - printPath: {printPath}");
            Log.Debug($"  - Paths are different: {displayPath != printPath}");
            
            _composedImagePath = displayPath;
            _composedImagePrintPath = printPath;
            
            // Verify what we stored
            Log.Debug($"  - STORED _composedImagePath: {_composedImagePath}");
            Log.Debug($"  - STORED _composedImagePrintPath: {_composedImagePrintPath}");
            Log.Debug($"  - _isCurrentTemplate2x6: {_isCurrentTemplate2x6}");
            
            // Save composed image to database
            if (!string.IsNullOrEmpty(displayPath) && File.Exists(displayPath))
            {
                string format = _isCurrentTemplate2x6 ? "2x6" : "4x6";
                _databaseOperations.SaveComposedImage(displayPath, format);
                Log.Debug($"  - Saved display version to database with format: {format}");
                
                // Also save the print version if it's different (e.g., 4x6 duplicate)
                if (!string.IsNullOrEmpty(printPath) && printPath != displayPath && File.Exists(printPath))
                {
                    _databaseOperations.SaveComposedImage(printPath, "4x6_print");
                    Log.Debug($"  - Also saved print version (4x6 duplicate) for reprints");
                }
            }
        }
        
        /// <summary>
        /// Set the selected filter for the current session
        /// </summary>
        public void SetSelectedFilter(FilterType filter)
        {
            Log.Debug($"PhotoboothSessionService: Setting filter to {filter}");
            _selectedFilter = filter;
        }
        
        /// <summary>
        /// Apply the selected filter to captured photos
        /// </summary>
        public async Task<List<string>> ApplyFilterToPhotosAsync()
        {
            try
            {
                if (_selectedFilter == FilterType.None || !Properties.Settings.Default.EnableFilters)
                {
                    Log.Debug("PhotoboothSessionService: No filter selected or filters disabled");
                    return _capturedPhotoPaths;
                }
                
                Log.Debug($"PhotoboothSessionService: Applying {_selectedFilter} filter to {_capturedPhotoPaths.Count} photos");
                
                var filteredPaths = new List<string>();
                foreach (var photoPath in _capturedPhotoPaths)
                {
                    if (!File.Exists(photoPath))
                    {
                        Log.Error($"Photo not found: {photoPath}");
                        filteredPaths.Add(photoPath);
                        continue;
                    }
                    
                    // Generate output path for filtered image
                    string outputDir = Path.GetDirectoryName(photoPath);
                    string outputFileName = $"{Path.GetFileNameWithoutExtension(photoPath)}_{_selectedFilter}.jpg";
                    string outputPath = Path.Combine(outputDir, outputFileName);
                    
                    // Apply filter using the filter service
                    string result = await Task.Run(() => 
                        _filterService.ApplyFilterToFile(photoPath, outputPath, _selectedFilter));
                    
                    filteredPaths.Add(result);
                    Log.Debug($"Applied filter to {photoPath} -> {result}");
                }
                
                // Update captured photo paths with filtered versions
                _capturedPhotoPaths = filteredPaths;
                return filteredPaths;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothSessionService: Failed to apply filters: {ex.Message}");
                return _capturedPhotoPaths; // Return original photos if filter fails
            }
        }
        
        /// <summary>
        /// Clear/reset the current session
        /// </summary>
        public void ClearSession()
        {
            Log.Debug($"★★★ PhotoboothSessionService: ClearSession called - Current SessionId: {_currentSessionId}, _isSessionActive: {_isSessionActive}");
            Log.Debug("PhotoboothSessionService: Clearing session");
            
            // Stop auto-clear timer
            StopAutoClearTimer();
            
            // Reset state
            _currentSessionId = null;
            _capturedPhotoPaths?.Clear();
            _currentPhotoIndex = 0;
            _totalPhotosRequired = 0;
            _isSessionActive = false;
            _currentEvent = null;
            _currentTemplate = null;
            _composedImagePath = null;
            _composedImagePrintPath = null;
            _gifPath = null;
            _isCurrentTemplate2x6 = false;
            _selectedFilter = FilterType.None;
            
            // Reset photo capture service
            _photoCaptureService?.ResetSession();
            
            // Notify listeners
            SessionCleared?.Invoke(this, EventArgs.Empty);
            
            Log.Debug("PhotoboothSessionService: Session cleared");
        }
        
        /// <summary>
        /// Start the auto-clear timer if enabled in settings
        /// </summary>
        public void StartAutoClearTimer()
        {
            if (Properties.Settings.Default.AutoClearSession)
            {
                Log.Debug($"Starting auto-clear timer for {Properties.Settings.Default.AutoClearTimeout} seconds");
                _autoClearElapsedSeconds = 0;
                _autoClearTimer.Start();
            }
        }
        
        /// <summary>
        /// Stop the auto-clear timer
        /// </summary>
        public void StopAutoClearTimer()
        {
            if (_autoClearTimer != null && _autoClearTimer.IsEnabled)
            {
                Log.Debug("Stopping auto-clear timer");
                _autoClearTimer.Stop();
                _autoClearElapsedSeconds = 0;
            }
        }
        
        /// <summary>
        /// Auto-clear timer tick event handler
        /// </summary>
        private void AutoClearTimer_Tick(object sender, EventArgs e)
        {
            _autoClearElapsedSeconds++;
            int timeoutSeconds = Properties.Settings.Default.AutoClearTimeout;
            
            Log.Debug($"Auto-clear timer: {_autoClearElapsedSeconds}/{timeoutSeconds} seconds");
            
            if (_autoClearElapsedSeconds >= timeoutSeconds)
            {
                Log.Debug($"Auto-clearing session after {timeoutSeconds} seconds");
                StopAutoClearTimer();
                
                // Notify listeners that timer expired
                AutoClearTimerExpired?.Invoke(this, EventArgs.Empty);
                
                // Clear the session
                ClearSession();
            }
        }
        
        /// <summary>
        /// Complete the current session
        /// </summary>
        public async Task<bool> CompleteSessionAsync()
        {
            try
            {
                Log.Debug($"★★★ CompleteSessionAsync called - _isSessionActive: {_isSessionActive}, SessionId: {_currentSessionId}");
                
                if (!_isSessionActive)
                {
                    Log.Debug("★★★ PhotoboothSessionService: No active session to complete - returning");
                    return true;
                }

                Log.Debug($"★★★ PhotoboothSessionService: Completing session {_currentSessionId} with {_capturedPhotoPaths.Count} photos");

                var completedSession = new CompletedSessionData
                {
                    SessionId = _currentSessionId,
                    Event = _currentEvent,
                    Template = _currentTemplate,
                    PhotoPaths = new List<string>(_capturedPhotoPaths),
                    ComposedImagePath = _composedImagePath,
                    GifPath = _gifPath,
                    CompletedAt = DateTime.Now
                };

                // Notify completion first (before clearing state so listeners can access session data)
                var subscribers = SessionCompleted?.GetInvocationList()?.Length ?? 0;
                Log.Debug($"★★★ PhotoboothSessionService: About to fire SessionCompleted event - Subscribers: {subscribers}");
                
                if (subscribers == 0)
                {
                    Log.Error("★★★ WARNING: No subscribers for SessionCompleted event!");
                }
                
                SessionCompleted?.Invoke(this, new SessionCompletedEventArgs
                {
                    CompletedSession = completedSession
                });

                Log.Debug("PhotoboothSessionService: Session completed successfully");
                
                // Note: Don't call ClearSession() here immediately as the UI needs to handle
                // the completion event first (GIF generation, template composition, etc.)
                // The session will be cleared either by auto-timer or manual clear
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothSessionService: Failed to complete session: {ex.Message}");
                SessionError?.Invoke(this, new SessionErrorEventArgs { Error = ex, Operation = "CompleteSession" });
                return false;
            }
        }

        /// <summary>
        /// Cancel the current session
        /// </summary>
        public void CancelSession()
        {
            try
            {
                if (_isSessionActive)
                {
                    Log.Debug($"PhotoboothSessionService: Cancelling session {_currentSessionId}");
                    
                    _isSessionActive = false;
                    _currentSessionId = null;
                    _currentEvent = null;
                    _currentTemplate = null;
                    _currentPhotoIndex = 0;
                    _totalPhotosRequired = 0;
                    _capturedPhotoPaths.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothSessionService: Error cancelling session: {ex.Message}");
            }
        }

        #region Session Manager Event Handlers
        private void OnSessionManagerSessionStarted(object sender, PhotoSession session)
        {
            Log.Debug($"PhotoboothSessionService: SessionManager session started: {session?.SessionId}");
        }

        private void OnSessionManagerSessionCompleted(object sender, PhotoSession session)
        {
            Log.Debug($"PhotoboothSessionService: SessionManager session completed: {session?.SessionId}");
        }
        #endregion

        /// <summary>
        /// Get the animation folder path for current event
        /// </summary>
        private string GetEventAnimationFolder()
        {
            string baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth"
            );
            
            string eventName = GetSafeEventName(_currentEvent);
            string animationFolder = Path.Combine(baseFolder, eventName, "animation");
            
            // Ensure directory exists
            Directory.CreateDirectory(animationFolder);
            
            return animationFolder;
        }
        
        /// <summary>
        /// Get the composed images folder path for current event
        /// </summary>
        private string GetEventComposedFolder()
        {
            string baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth"
            );
            
            string eventName = GetSafeEventName(_currentEvent);
            string composedFolder = Path.Combine(baseFolder, eventName, "composed");
            
            // Ensure directory exists
            Directory.CreateDirectory(composedFolder);
            
            return composedFolder;
        }
        
        /// <summary>
        /// Get safe folder name from event data
        /// </summary>
        private string GetSafeEventName(EventData eventData)
        {
            if (eventData?.Name != null && !string.IsNullOrWhiteSpace(eventData.Name))
            {
                // Clean event name for use as folder name
                string safeName = eventData.Name.Trim();
                
                // Remove invalid filename characters
                char[] invalidChars = Path.GetInvalidFileNameChars();
                foreach (char c in invalidChars)
                {
                    safeName = safeName.Replace(c, '_');
                }
                
                // Replace spaces with underscores and limit length
                safeName = safeName.Replace(' ', '_');
                if (safeName.Length > 50)
                {
                    safeName = safeName.Substring(0, 50);
                }
                
                return safeName;
            }
            
            // Fallback to date-based folder name
            return $"Event_{DateTime.Now:yyyy_MM_dd}";
        }

        #region Dispose
        public void Dispose()
        {
            if (_sessionManager != null)
            {
                _sessionManager.SessionStarted -= OnSessionManagerSessionStarted;
                _sessionManager.SessionCompleted -= OnSessionManagerSessionCompleted;
            }
        }
        #endregion
    }

    #region Event Args Classes
    public class SessionStartedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public EventData Event { get; set; }
        public TemplateData Template { get; set; }
        public int TotalPhotos { get; set; }
    }

    public class PhotoProcessedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string PhotoPath { get; set; }
        public int PhotoIndex { get; set; }
        public int TotalPhotos { get; set; }
        public bool IsComplete { get; set; }
    }

    public class SessionCompletedEventArgs : EventArgs
    {
        public CompletedSessionData CompletedSession { get; set; }
    }

    public class SessionErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
        public string Operation { get; set; }
    }

    public class AnimationReadyEventArgs : EventArgs
    {
        public string AnimationPath { get; set; }
        public string SessionId { get; set; }
    }

    public class CompletedSessionData
    {
        public string SessionId { get; set; }
        public EventData Event { get; set; }
        public TemplateData Template { get; set; }
        public List<string> PhotoPaths { get; set; }
        public string ComposedImagePath { get; set; }
        public string GifPath { get; set; }
        public DateTime CompletedAt { get; set; }
    }
    #endregion
}