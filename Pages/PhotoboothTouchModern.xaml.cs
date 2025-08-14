using CameraControl.Devices;
using CameraControl.Devices.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using GraphicsUnit = System.Drawing.GraphicsUnit;
using CompositingMode = System.Drawing.Drawing2D.CompositingMode;
using CompositingQuality = System.Drawing.Drawing2D.CompositingQuality;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;
using System.Drawing;
using System.Windows.Interop;
using Photobooth.Services;
using Photobooth.Database;
using System.Linq;
using Photobooth.Controls;
using System.ComponentModel;

namespace Photobooth.Pages
{
    public partial class PhotoboothTouchModern : Page
    {
        private System.Threading.CancellationTokenSource currentCaptureToken;
        // Use singleton camera manager to maintain session across screens
        public CameraDeviceManager DeviceManager => CameraSessionManager.Instance.DeviceManager;
        public string FolderForPhotos { get; set; }
        
        private DispatcherTimer liveViewTimer;
        private DispatcherTimer countdownTimer;
        private int countdownSeconds;
        private int currentCountdown;
        private int photoCount = 0;
        private bool isCapturing = false;
        private DateTime lastCaptureTime = DateTime.MinValue;
        
        // Event/Template workflow properties
        private EventData currentEvent;
        private TemplateData currentTemplate;
        private PhotoboothService photoboothService;
        private int totalPhotosNeeded = 1;
        private int currentPhotoIndex = 0;
        private List<string> capturedPhotoPaths = new List<string>();
        
        // Photo strip display
        private System.Collections.ObjectModel.ObservableCollection<BitmapImage> photoStripImages = 
            new System.Collections.ObjectModel.ObservableCollection<BitmapImage>();
        private System.Collections.ObjectModel.ObservableCollection<PhotoStripItem> photoStripItems = 
            new System.Collections.ObjectModel.ObservableCollection<PhotoStripItem>();
        
        // Retake functionality
        private DispatcherTimer retakeReviewTimer;
        
        // Auto-clear session timer
        private DispatcherTimer autoClearTimer;
        
        // Print functionality
        private string lastProcessedImagePath;
        private string lastProcessedImagePathForPrinting; // Separate path for 4x6 version if needed
        private bool lastProcessedWas2x6Template; // Track if original was 2x6 for proper printer routing
        private int retakeTimeRemaining;
        private int photoIndexToRetake = -1;
        private bool isRetakingPhoto = false;
        private System.Collections.ObjectModel.ObservableCollection<RetakePhotoItem> retakePhotos = 
            new System.Collections.ObjectModel.ObservableCollection<RetakePhotoItem>();
        
        // Event selection properties
        private EventService eventService;
        private TemplateDatabase database;
        private List<EventData> availableEvents;
        private List<TemplateData> availableTemplates;
        private EventData selectedEventForOverlay;
        private TemplateData selectedTemplateForOverlay;
        private bool isSelectingTemplateForSession = false;
        
        // Filter service - using hybrid Magick.NET/GDI+ service for best performance
        private PhotoFilterServiceHybrid filterService;
        
        // Printer monitoring
        private PrinterMonitorService printerMonitor;
        
        // Database session tracking
        private int? currentDatabaseSessionId = null;
        private string currentSessionGuid = null;
        private List<int> currentSessionPhotoIds = new List<int>();

        public PhotoboothTouchModern()
        {
            InitializeComponent();
            
            // Use singleton camera manager - don't create new instance
            // Event subscriptions moved to Loaded event to prevent duplicates

            // Initialize services
            photoboothService = new PhotoboothService();
            eventService = new EventService();
            database = new TemplateDatabase();
            // Use hybrid filter service for best performance with Magick.NET + GDI+ fallback
            filterService = new PhotoFilterServiceHybrid();
            
            // Run database cleanup for sessions older than 24 hours
            database.RunPeriodicCleanup();
            
            // Initialize printer monitoring
            printerMonitor = PrinterMonitorService.Instance;

            // Set up photo folder for photobooth
            FolderForPhotos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");
            if (!Directory.Exists(FolderForPhotos))
            {
                Directory.CreateDirectory(FolderForPhotos);
            }

            // Initialize timers
            InitializeTimers();
            
            // Set up logging
            Log.LogError += Log_LogMessage;
            Log.LogDebug += Log_LogMessage;
            Log.LogInfo += Log_LogMessage;

            // Load timing settings from configuration
            countdownSeconds = Properties.Settings.Default.CountdownSeconds;
            currentCountdown = countdownSeconds;
            
            // Initialize UI
            countdownSecondsDisplay.Text = $"{countdownSeconds}s";
            photoCountText.Text = photoCount.ToString();
            
            // Bind photo strip to collection
            photoStripControl.ItemsSource = photoStripItems;
            
            // Bind retake grid to collection
            retakePhotoGrid.ItemsSource = retakePhotos;
            
            Loaded += PhotoboothTouchModern_Loaded;
            Unloaded += PhotoboothTouchModern_Unloaded;
        }

        private void CloseAllOverlays()
        {
            // Close all overlays
            if (cameraSettingsOverlay != null && cameraSettingsOverlay.Visibility == Visibility.Visible)
            {
                cameraSettingsOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed camera settings overlay");
            }
            
            if (eventSelectionOverlay != null && eventSelectionOverlay.Visibility == Visibility.Visible)
            {
                eventSelectionOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed event selection overlay");
            }
            
            if (retakeReviewOverlay != null && retakeReviewOverlay.Visibility == Visibility.Visible)
            {
                retakeReviewOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed retake review overlay");
            }
            
            if (filterSelectionOverlay != null && filterSelectionOverlay.Visibility == Visibility.Visible)
            {
                filterSelectionOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed filter selection overlay");
            }
            
            if (galleryOverlay != null && galleryOverlay.Visibility == Visibility.Visible)
            {
                galleryOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed gallery overlay");
            }
            
            if (postSessionFilterOverlay != null && postSessionFilterOverlay.Visibility == Visibility.Visible)
            {
                postSessionFilterOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed post session filter overlay");
            }
            
            if (pinEntryOverlay != null && pinEntryOverlay.Visibility == Visibility.Visible)
            {
                pinEntryOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed PIN entry overlay");
            }
            
            if (videoPlayerOverlay != null && videoPlayerOverlay.Visibility == Visibility.Visible)
            {
                videoPlayerOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed video player overlay");
            }
            
            if (modernSettingsOverlay != null && modernSettingsOverlay.Visibility == Visibility.Visible)
            {
                modernSettingsOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("CloseAllOverlays: Closed modern settings overlay");
            }
        }
        
        private void PhotoboothTouchModern_Unloaded(object sender, RoutedEventArgs e)
        {
            Log.Debug("PhotoboothTouch_Unloaded: Page is being unloaded, cleaning up camera resources");
            
            try
            {
                // Close any open overlays
                CloseAllOverlays();
                
                // Unsubscribe from camera events to prevent duplicate handlers
                DeviceManager.CameraSelected -= DeviceManager_CameraSelected;
                DeviceManager.CameraConnected -= DeviceManager_CameraConnected;
                DeviceManager.PhotoCaptured -= DeviceManager_PhotoCaptured;
                DeviceManager.CameraDisconnected -= DeviceManager_CameraDisconnected;
                Log.Debug("PhotoboothTouch_Unloaded: Unsubscribed from camera events");
                
                // Stop printer monitoring
                printerMonitor.PrinterStatusChanged -= OnPrinterStatusChanged;
                printerMonitor.StopMonitoring();
                Log.Debug("PhotoboothTouch_Unloaded: Stopped printer monitoring");
                
                // Stop any ongoing photo sequence
                StopPhotoSequence();
                
                // Use singleton manager to cleanup without destroying session
                CameraSessionManager.Instance.CleanupCameraForScreenChange();
                
                // Clean up timers
                if (liveViewTimer != null)
                {
                    liveViewTimer.Stop();
                    liveViewTimer.Tick -= LiveViewTimer_Tick;
                }
                
                if (countdownTimer != null)
                {
                    countdownTimer.Stop();
                    countdownTimer.Tick -= CountdownTimer_Tick;
                }
                
                if (retakeReviewTimer != null)
                {
                    retakeReviewTimer.Stop();
                    retakeReviewTimer.Tick -= RetakeReviewTimer_Tick;
                }
                
                // Cancel any ongoing operations
                if (currentCaptureToken != null)
                {
                    currentCaptureToken.Cancel();
                    currentCaptureToken.Dispose();
                    currentCaptureToken = null;
                }
                
                Log.Debug("PhotoboothTouch_Unloaded: Cleanup completed");
            }
            catch (Exception ex)
            {
                Log.Error("PhotoboothTouch_Unloaded: Error during cleanup", ex);
            }
        }

        private void InitializeTimers()
        {
            // Live view timer (30 FPS)
            liveViewTimer = new DispatcherTimer();
            liveViewTimer.Tick += LiveViewTimer_Tick;
            liveViewTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / 30);

            // Countdown timer (1 second intervals)
            countdownTimer = new DispatcherTimer();
            countdownTimer.Tick += CountdownTimer_Tick;
            countdownTimer.Interval = new TimeSpan(0, 0, 1);
            
            // Retake review timer (1 second intervals)
            retakeReviewTimer = new DispatcherTimer();
            retakeReviewTimer.Tick += RetakeReviewTimer_Tick;
            retakeReviewTimer.Interval = new TimeSpan(0, 0, 1);
            
            // Auto-clear timer (1 second intervals)
            autoClearTimer = new DispatcherTimer();
            autoClearTimer.Tick += AutoClearTimer_Tick;
            autoClearTimer.Interval = new TimeSpan(0, 0, 1);
        }

        private void PhotoboothTouchModern_Loaded(object sender, RoutedEventArgs e)
        {
            Log.Debug("PhotoboothTouch_Loaded: Page loaded, initializing camera");
            
            // Subscribe to camera events (will be unsubscribed in Unloaded)
            DeviceManager.CameraSelected += DeviceManager_CameraSelected;
            DeviceManager.CameraConnected += DeviceManager_CameraConnected;
            DeviceManager.PhotoCaptured += DeviceManager_PhotoCaptured;
            DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;
            Log.Debug("PhotoboothTouch_Loaded: Subscribed to camera events");
            
            // Subscribe to printer status events
            printerMonitor.PrinterStatusChanged += OnPrinterStatusChanged;
            printerMonitor.StartMonitoring();
            Log.Debug("PhotoboothTouch_Loaded: Started printer monitoring");
            
            // Prepare camera for use using singleton manager
            CameraSessionManager.Instance.PrepareCameraForUse();
            
            // Reset state when page loads
            isCapturing = false;
            countdownOverlay.Visibility = Visibility.Collapsed;
            
            // Show start button initially only if we have a template selected
            if (startButtonOverlay != null)
            {
                // Show the start button if a template is already selected (for first use)
                startButtonOverlay.Visibility = (currentTemplate != null) ? Visibility.Visible : Visibility.Collapsed;
            }
            if (stopSessionButton != null)
                stopSessionButton.Visibility = Visibility.Collapsed;
            
            // Stop any timers that might be running
            if (liveViewTimer != null)
                liveViewTimer.Stop();
            if (countdownTimer != null)
                countdownTimer.Stop();
            
            // Camera is managed by singleton, just log the state
            if (DeviceManager?.SelectedCameraDevice != null)
            {
                Log.Debug($"PhotoboothTouch_Loaded: Camera ready - {DeviceManager.SelectedCameraDevice.DeviceName}");
                Log.Debug($"PhotoboothTouch_Loaded: Camera IsBusy: {DeviceManager.SelectedCameraDevice.IsBusy}");
            }
            else
            {
                Log.Debug("PhotoboothTouch_Loaded: No camera selected");
            }
            
            // Check for event/template workflow data
            LoadEventTemplateWorkflow();
            
            // Initialize printer status
            CheckPrinterStatus();
            
            // Check if interface should be locked on startup
            if (Properties.Settings.Default.EnableLockFeature)
            {
                // Start in locked state
                _isLocked = true;
                if (lockButton != null)
                {
                    lockButton.Content = "🔒";
                    lockButton.ToolTip = "Unlock Interface";
                }
                
                // Disable critical controls
                DisableCriticalControls();
                
                // Ensure navbar is hidden
                if (bottomControlBar != null)
                {
                    bottomControlBar.Visibility = Visibility.Collapsed;
                    if (bottomBarToggleChevron != null)
                    {
                        bottomBarToggleChevron.Text = "⌃"; // Up chevron
                    }
                }
                
                Log.Debug("PhotoboothTouch_Loaded: Interface started in locked state");
            }
            
            // Use exact same synchronous approach as working Camera.xaml.cs
            try
            {
                // Wait a moment to ensure previous page has released camera
                System.Threading.Thread.Sleep(500);
                
                DeviceManager.ConnectToCamera();
                RefreshDisplay();
                
                // Handle live view based on idle setting
                if (DeviceManager?.SelectedCameraDevice != null)
                {
                    if (Properties.Settings.Default.EnableIdleLiveView)
                    {
                        // Start live view if idle live view is enabled
                        try
                        {
                            liveViewTimer.Start();
                            DeviceManager.SelectedCameraDevice.StartLiveView();
                            Log.Debug("PhotoboothTouch_Loaded: Started idle live view");
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"PhotoboothTouch_Loaded: Failed to start idle live view: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Stop live view if it might be running and idle live view is disabled
                        try
                        {
                            liveViewTimer.Stop();
                            DeviceManager.SelectedCameraDevice.StopLiveView();
                            Log.Debug("PhotoboothTouch_Loaded: Stopped live view (idle live view disabled)");
                        }
                        catch 
                        { 
                            // Expected if live view wasn't running
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                cameraStatusText.Text = "Camera error";
                statusText.Text = "Camera connection failed";
                Log.Error("PhotoboothTouch: Camera connection failed", ex);
            }
        }
        
        private void LoadEventTemplateWorkflow()
        {
            DebugService.LogDebug("PhotoboothTouch_Loaded called");
            
            // Check if this page was launched via event workflow
            currentEvent = PhotoboothService.CurrentEvent;
            currentTemplate = PhotoboothService.CurrentTemplate;
            
            DebugService.LogDebug($"PhotoboothTouch_Loaded: currentEvent={currentEvent?.Name ?? "null"}, currentTemplate={currentTemplate?.Name ?? "null"}");
            
            if (currentEvent != null && currentTemplate != null)
            {
                // Don't create database session yet - wait until first photo is captured
                // CreateDatabaseSession(); // Moved to first photo capture
                
                // Update UI with event information
                statusText.Text = $"Event: {currentEvent.Name} - Template: {currentTemplate.Name}";
                
                // Get photo count from template
                totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                currentPhotoIndex = 0;
                UpdatePhotoStripPlaceholders();
                
                // Update folder path to include event name
                string eventFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                    "Photobooth", SanitizeFileName(currentEvent.Name));
                if (!Directory.Exists(eventFolder))
                {
                    Directory.CreateDirectory(eventFolder);
                }
                FolderForPhotos = eventFolder;
                
                Log.Debug($"PhotoboothTouch: Loaded event '{currentEvent.Name}' with template '{currentTemplate.Name}' requiring {totalPhotosNeeded} photos");
            }
            else if (currentEvent != null)
            {
                // Event selected but no template - load templates
                DebugService.LogDebug($"Event selected but no template, loading templates for: {currentEvent.Name}");
                availableTemplates = eventService.GetEventTemplates(currentEvent.Id);
                
                if (availableTemplates != null && availableTemplates.Count == 1)
                {
                    // Only one template - auto-select it
                    currentTemplate = availableTemplates[0];
                    totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                    currentPhotoIndex = 0;
                    UpdatePhotoStripPlaceholders();
                    statusText.Text = $"Event: {currentEvent.Name} - Template: {currentTemplate.Name}";
                    
                    // Update folder path
                    string eventFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                        "Photobooth", SanitizeFileName(currentEvent.Name));
                    if (!Directory.Exists(eventFolder))
                    {
                        Directory.CreateDirectory(eventFolder);
                    }
                    FolderForPhotos = eventFolder;
                    
                    Log.Debug($"Auto-selected single template: {currentTemplate.Name}");
                }
                else if (availableTemplates != null && availableTemplates.Count > 1)
                {
                    // Multiple templates - user will select when pressing START
                    statusText.Text = $"Event: {currentEvent.Name} - Touch START to select template";
                    
                    // Update folder path for event
                    string eventFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                        "Photobooth", SanitizeFileName(currentEvent.Name));
                    if (!Directory.Exists(eventFolder))
                    {
                        Directory.CreateDirectory(eventFolder);
                    }
                    FolderForPhotos = eventFolder;
                    
                    Log.Debug($"Event has {availableTemplates.Count} templates available");
                }
                else
                {
                    // No templates
                    statusText.Text = $"Event: {currentEvent.Name} - No templates available";
                    Log.Debug($"No templates found for event");
                }
            }
            else
            {
                // No event/template selected - show selection overlay
                ShowEventSelectionOverlay();
            }
        }
        
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
        }

        private void RefreshDisplay()
        {
            if (DeviceManager.SelectedCameraDevice != null)
            {
                cameraStatusText.Text = $"Connected: {DeviceManager.SelectedCameraDevice.DeviceName}";
                DeviceManager.SelectedCameraDevice.CaptureInSdRam = true;
                
                // Update status based on event workflow
                if (currentEvent != null && currentTemplate != null)
                {
                    statusText.Text = $"Event: {currentEvent.Name} - Ready for photo {currentPhotoIndex + 1} of {totalPhotosNeeded}";
                }
                else
                {
                    statusText.Text = "Camera ready - Touch START to begin";
                }
                
                // Debug: Check if events are properly connected
                Log.Debug($"PhotoboothTouch: Camera connected, DeviceManager has {DeviceManager.ConnectedDevices.Count} devices");
                Log.Debug($"PhotoboothTouch: Camera type: {DeviceManager.SelectedCameraDevice.GetType().Name}");
            }
            else
            {
                cameraStatusText.Text = "No camera found";
                if (currentEvent != null)
                {
                    statusText.Text = $"Event: {currentEvent.Name} - Please connect a camera";
                }
                else
                {
                    statusText.Text = "Please connect a camera";
                }
                Log.Debug("PhotoboothTouch: No camera device found");
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // Handle both Button and Border click events
            if (sender is Border)
            {
                Log.Debug("StartButton_Click: Triggered from centered touch button");
            }
            
            if (DeviceManager.SelectedCameraDevice == null)
            {
                statusText.Text = "No camera connected";
                return;
            }

            if (isCapturing)
                return;
            
            // Hide the centered start button
            if (startButtonOverlay != null)
            {
                startButtonOverlay.Visibility = Visibility.Collapsed;
            }
            
            // Show the stop button in top-right
            if (stopSessionButton != null)
            {
                stopSessionButton.Visibility = Visibility.Visible;
            }

            // Critical: Enforce minimum time between captures to prevent Canon SDK issues
            var timeSinceLastCapture = DateTime.Now - lastCaptureTime;
            if (timeSinceLastCapture.TotalMilliseconds < 6000) // 6 seconds minimum for multi-photo sequences
            {
                var remainingTime = 6000 - (int)timeSinceLastCapture.TotalMilliseconds;
                statusText.Text = $"Please wait {remainingTime / 1000 + 1} seconds between photos";
                
                // Start a timer to update the message
                Task.Delay(remainingTime).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!isCapturing)
                        {
                            statusText.Text = "Touch START to take another photo";
                        }
                    });
                });
                return;
            }

            // Check if we need to select a template for this session
            if (currentEvent != null && currentPhotoIndex == 0)
            {
                // Check if we need to select a template
                if (currentTemplate == null)
                {
                    // No template selected yet - check if we need to show selection
                    if (availableTemplates != null && availableTemplates.Count > 1)
                    {
                        // Multiple templates available - show selection
                        ShowTemplateSelectionForSession();
                        return;
                    }
                    else if (availableTemplates != null && availableTemplates.Count == 1)
                    {
                        // Single template - use it automatically
                        currentTemplate = availableTemplates[0];
                        totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                        UpdatePhotoStripPlaceholders();
                    }
                }
                // If template is already selected, continue with the session
            }

            StartPhotoSequence();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // If this is the first photo and no photos have been captured yet, stop the entire session
            if (currentPhotoIndex == 0 && capturedPhotoPaths.Count == 0)
            {
                Log.Debug("StopButton_Click: No photos captured yet, stopping entire session");
                StopPhotoSequence();
                
                statusText.Text = "Session cancelled";
                return;
            }
            
            // Otherwise, only abort the current photo countdown and restart it
            Log.Debug($"StopButton_Click: Aborting current photo {currentPhotoIndex + 1} of {totalPhotosNeeded}, will restart countdown");
            
            // Stop the countdown timer
            countdownTimer.Stop();
            countdownOverlay.Visibility = Visibility.Collapsed;
            
            // Cancel any pending capture
            currentCaptureToken?.Cancel();
            
            // Update status
            if (currentEvent != null && totalPhotosNeeded > 1)
            {
                statusText.Text = $"Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Restarting countdown...";
            }
            else
            {
                statusText.Text = "Restarting countdown...";
            }
            
            // Restart the countdown for the same photo after a brief delay
            Task.Delay(1000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log.Debug("StopButton_Click: Restarting countdown for same photo");
                    StartCountdown();
                });
            });
        }

        private async void StartPhotoSequence()
        {
            Log.Debug("=== START PHOTO SEQUENCE ===");
            Log.Debug($"StartPhotoSequence: Called at {DateTime.Now:HH:mm:ss.fff}");
            Log.Debug($"StartPhotoSequence: Current photo {currentPhotoIndex + 1} of {totalPhotosNeeded}");
            Log.Debug($"StartPhotoSequence: isCapturing before: {isCapturing}");
            
            // If this is the first photo in a sequence, clear the captured photos list and strip
            if (currentPhotoIndex == 0)
            {
                capturedPhotoPaths.Clear();
                photoStripImages.Clear();
                photoStripItems.Clear();
                
                // Clear previous processed image paths
                lastProcessedImagePath = null;
                lastProcessedImagePathForPrinting = null;
                lastProcessedWas2x6Template = false;
                
                // Hide print button when starting a new session
                HidePrintButton();
                
                // Add placeholder boxes for all photos needed
                UpdatePhotoStripPlaceholders();
                
                Log.Debug($"StartPhotoSequence: Cleared capturedPhotoPaths list and photo strip (starting new sequence)");
            }
            
            try
            {
                isCapturing = true;
                
                // Show stop button when starting sequence
                if (stopSessionButton != null)
                {
                    stopSessionButton.Visibility = Visibility.Visible;
                    Log.Debug("StartPhotoSequence: Showing stop button");
                }
                
                // Hide session loaded indicator and navigation when starting new capture
                if (sessionLoadedIndicator != null)
                    sessionLoadedIndicator.Visibility = Visibility.Collapsed;
                if (composedImageNavigation != null)
                    composedImageNavigation.Visibility = Visibility.Collapsed;
                if (photoViewModeIndicator != null)
                    photoViewModeIndicator.Visibility = Visibility.Collapsed;
                
                // Clear loaded session data
                loadedComposedImages = null;
                currentComposedImageIndex = 0;
                
                statusText.Text = "Preparing camera...";
                Log.Debug("StartPhotoSequence: Set isCapturing=true, disabled start button");
                
                // Critical: Ensure camera is ready and not busy from previous capture
                await Task.Run(() =>
                {
                    try
                    {
                        Log.Debug($"StartPhotoSequence: Camera initial state - IsBusy: {DeviceManager.SelectedCameraDevice?.IsBusy}");
                        
                        // Wait for camera to be completely ready
                        int retryCount = 0;
                        while (DeviceManager.SelectedCameraDevice.IsBusy && retryCount < 20)
                        {
                            Log.Debug($"StartPhotoSequence: Camera still busy, waiting... retry {retryCount}");
                            Thread.Sleep(100);
                            retryCount++;
                        }
                        
                        if (DeviceManager.SelectedCameraDevice.IsBusy)
                        {
                            Log.Error("StartPhotoSequence: Camera still busy after waiting 2 seconds");
                            throw new Exception("Camera is busy");
                        }
                        
                        Log.Debug("StartPhotoSequence: Camera not busy, proceeding with live view setup");
                        
                        // Force stop any existing live view first
                        try
                        {
                            Log.Debug("StartPhotoSequence: Stopping existing live view");
                            DeviceManager.SelectedCameraDevice.StopLiveView();
                            Thread.Sleep(200); // Let it fully stop
                            Log.Debug("StartPhotoSequence: StopLiveView completed");
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("StartPhotoSequence: StopLiveView failed (probably not running): " + ex.Message);
                        }
                        
                        // Now start fresh live view
                        Log.Debug("StartPhotoSequence: Starting fresh live view");
                        DeviceManager.SelectedCameraDevice.StartLiveView();
                        Log.Debug("StartPhotoSequence: StartLiveView completed successfully");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("StartPhotoSequence: Failed to start live view", ex);
                        throw;
                    }
                });
                
                liveViewTimer.Start();
                statusText.Text = "Live view active - Starting countdown...";
                
                // Wait a moment for live view to stabilize
                await Task.Delay(1000);
                
                // Start countdown - ensure this always happens
                Log.Debug("StartPhotoSequence: About to call StartCountdown()");
                
                // Force countdown to start on UI thread
                Dispatcher.Invoke(() =>
                {
                    StartCountdown();
                });
                
                Log.Debug("StartPhotoSequence: StartCountdown() call completed");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to start photo sequence", ex);
                Dispatcher.Invoke(() =>
                {
                    statusText.Text = "Camera not ready - Please try again";
                    StopPhotoSequence();
                });
            }
        }

        private void StartCountdown()
        {
            Log.Debug($"StartCountdown: Called at {DateTime.Now:HH:mm:ss.fff}");
            Log.Debug($"StartCountdown: countdownSeconds={countdownSeconds}, currentPhotoIndex={currentPhotoIndex}");
            
            // Show stop button during countdown so user can abort
            if (stopSessionButton != null)
            {
                stopSessionButton.Visibility = Visibility.Visible;
                Log.Debug("StartCountdown: Showing stop button for countdown abort");
            }
            
            // Check if countdown is enabled
            bool showCountdown = Properties.Settings.Default.ShowCountdown;
            Log.Debug($"StartCountdown: ShowCountdown setting = {showCountdown}");
            
            if (!showCountdown)
            {
                // Skip countdown and capture immediately
                Log.Debug("StartCountdown: Countdown disabled, capturing immediately");
                statusText.Text = "Taking photo...";
                Task.Delay(500).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        CapturePhoto();
                    });
                });
                return;
            }
            
            // Ensure timer is completely stopped and reset
            countdownTimer.Stop();
            Log.Debug($"StartCountdown: Timer stopped, IsEnabled={countdownTimer.IsEnabled}");
            
            currentCountdown = countdownSeconds;
            countdownText.Text = currentCountdown.ToString();
            countdownOverlay.Visibility = Visibility.Visible;
            
            // Start timer with explicit state check
            countdownTimer.Start();
            Log.Debug($"StartCountdown: Timer started, currentCountdown={currentCountdown}, overlay visible={countdownOverlay.Visibility}");
            Log.Debug($"StartCountdown: Timer IsEnabled after start={countdownTimer.IsEnabled}");
            
            // Update countdown message based on event workflow
            if (currentEvent != null && totalPhotosNeeded > 1)
            {
                statusText.Text = $"Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Get ready! {currentCountdown}";
            }
            else
            {
                statusText.Text = $"Get ready! {currentCountdown}";
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            Log.Debug($"CountdownTimer_Tick: currentCountdown before decrement={currentCountdown}");
            currentCountdown--;
            
            if (currentCountdown > 0)
            {
                countdownText.Text = currentCountdown.ToString();
                
                // Update countdown message based on event workflow
                if (currentEvent != null && totalPhotosNeeded > 1)
                {
                    statusText.Text = $"Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Get ready! {currentCountdown}";
                }
                else
                {
                    statusText.Text = $"Get ready! {currentCountdown}";
                }
            }
            else
            {
                // Time to capture - stop the countdown timer immediately
                Log.Debug("CountdownTimer_Tick: Countdown reached 0, triggering capture");
                countdownTimer.Stop();
                countdownText.Text = "SMILE!";
                
                if (currentEvent != null && totalPhotosNeeded > 1)
                {
                    statusText.Text = $"Taking photo {currentPhotoIndex + 1} of {totalPhotosNeeded}...";
                }
                else
                {
                    statusText.Text = "Taking photo...";
                }
                
                // Capture after a brief moment
                Log.Debug("CountdownTimer_Tick: Starting 500ms delay before CapturePhoto()");
                Task.Delay(500).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Log.Debug("CountdownTimer_Tick: 500ms delay complete, calling CapturePhoto()");
                        CapturePhoto();
                    });
                });
            }
        }

        private void CapturePhoto()
        {
            statusText.Text = "Taking photo...";
            Log.Debug($"=== CAPTURE DEBUG START - Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} ===");
            Log.Debug($"PhotoboothTouch: CapturePhoto called, camera type: {DeviceManager.SelectedCameraDevice?.GetType().Name}");
            Log.Debug($"PhotoboothTouch: Camera IsBusy: {DeviceManager.SelectedCameraDevice?.IsBusy}");
            Log.Debug($"PhotoboothTouch: Camera IsConnected: {DeviceManager.SelectedCameraDevice?.IsConnected}");
            Log.Debug($"PhotoboothTouch: CaptureInSdRam setting: {DeviceManager.SelectedCameraDevice?.CaptureInSdRam}");
            Log.Debug($"PhotoboothTouch: isCapturing flag: {isCapturing}");
            Log.Debug($"PhotoboothTouch: Current time: {DateTime.Now:HH:mm:ss.fff}");
            Log.Debug($"PhotoboothTouch: Time since last capture: {(DateTime.Now - lastCaptureTime).TotalMilliseconds}ms");
            
            // Cancel any previous capture timeouts
            currentCaptureToken?.Cancel();
            currentCaptureToken = new System.Threading.CancellationTokenSource();
            
            // Reset camera busy state if stuck
            if (DeviceManager.SelectedCameraDevice?.IsBusy == true)
            {
                Log.Debug("CapturePhoto: Camera is busy, attempting to reset");
                DeviceManager.SelectedCameraDevice.IsBusy = false;
                Thread.Sleep(200); // Brief delay to allow reset
            }
            var token = currentCaptureToken.Token;
            
            // Stop live view timer before capture (like real camera behavior)
            liveViewTimer.Stop();
            Log.Debug("PhotoboothTouch: Stopped live view timer for capture");
            
            // Use exact same approach as working Camera.xaml.cs
            Capture();
            
            // Keep timeout as backup in case event doesn't fire - use cancellation token
            var currentCaptureTime = DateTime.Now;
            Task.Delay(15000, token).ContinueWith(task =>  // Increased timeout to 15 seconds
            {
                if (!task.IsCanceled)
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Only timeout if this is still the current capture attempt
                        if (isCapturing && countdownOverlay.Visibility == Visibility.Visible)
                        {
                            Log.Debug($"PhotoboothTouch: *** TIMEOUT OCCURRED *** - Canon event may have failed after {(DateTime.Now - currentCaptureTime).TotalMilliseconds}ms");
                            Log.Debug($"PhotoboothTouch: Camera IsBusy at timeout: {DeviceManager.SelectedCameraDevice?.IsBusy}");
                            Log.Debug($"PhotoboothTouch: Camera IsConnected at timeout: {DeviceManager.SelectedCameraDevice?.IsConnected}");
                            
                            // Try to recover camera state
                            try
                            {
                                if (DeviceManager.SelectedCameraDevice?.IsBusy == true)
                                {
                                    DeviceManager.SelectedCameraDevice.IsBusy = false;
                                }
                                
                                // Restart live view
                                DeviceManager.SelectedCameraDevice?.StopLiveView();
                                Thread.Sleep(500);
                                DeviceManager.SelectedCameraDevice?.StartLiveView();
                                liveViewTimer.Start();
                            }
                            catch (Exception ex)
                            {
                                Log.Error("Failed to recover camera after timeout", ex);
                            }
                            
                            statusText.Text = "Photo capture timeout - Camera reset, please try again";
                            StopPhotoSequence();
                        }
                    });
                }
                else
                {
                    Log.Debug("PhotoboothTouch: Capture timeout was cancelled - new capture started");
                }
            }, TaskScheduler.Default);
        }

        private void Capture()
        {
            Log.Debug("PhotoboothTouch: Entering Capture() method");
            bool retry;
            int retryCount = 0;
            do
            {
                retry = false;
                try
                {
                    Log.Debug($"PhotoboothTouch: Calling CapturePhoto() - attempt {retryCount + 1}");
                    DeviceManager.SelectedCameraDevice.CapturePhoto();
                    Log.Debug("PhotoboothTouch: CapturePhoto() call completed successfully");
                }
                catch (DeviceException exception)
                {
                    Log.Debug($"PhotoboothTouch: DeviceException caught - ErrorCode: {exception.ErrorCode}, Message: {exception.Message}");
                    
                    // if device is busy retry after a progressive delay
                    if (exception.ErrorCode == ErrorCodes.MTP_Device_Busy ||
                        exception.ErrorCode == ErrorCodes.ERROR_BUSY)
                    {
                        retryCount++;
                        
                        // Progressive delay: start with 200ms, increase each retry
                        int delay = Math.Min(200 * retryCount, 1000);
                        Log.Debug($"PhotoboothTouch: Device busy, retry #{retryCount} after {delay}ms");
                        
                        // Try to reset busy flag after a few attempts
                        if (retryCount > 5 && DeviceManager.SelectedCameraDevice?.IsBusy == true)
                        {
                            Log.Debug("PhotoboothTouch: Forcing IsBusy reset during retry");
                            DeviceManager.SelectedCameraDevice.IsBusy = false;
                        }
                        
                        // Prevent infinite loop - increased to 20 retries
                        if (retryCount > 20)
                        {
                            Log.Error("PhotoboothTouch: Too many busy retries, giving up");
                            
                            // Final attempt to reset camera
                            if (DeviceManager.SelectedCameraDevice != null)
                            {
                                DeviceManager.SelectedCameraDevice.IsBusy = false;
                            }
                            
                            Dispatcher.Invoke(() =>
                            {
                                statusText.Text = "Camera too busy - Please try again";
                                StopPhotoSequence();
                            });
                            break;
                        }
                        
                        Thread.Sleep(delay);  // Use progressive delay
                        retry = true;
                    }
                    else
                    {
                        Log.Error("PhotoboothTouch: Capture device exception: " + exception.Message);
                        Dispatcher.Invoke(() =>
                        {
                            statusText.Text = "Capture error: " + exception.Message;
                            StopPhotoSequence();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("PhotoboothTouch: Capture general exception: " + ex.Message);
                    Dispatcher.Invoke(() =>
                    {
                        statusText.Text = "Capture error: " + ex.Message;
                        StopPhotoSequence();
                    });
                }

            } while (retry);
            
            Log.Debug("PhotoboothTouch: Exiting Capture() method");
        }

        private void UpdatePhotoStripPlaceholders(bool preserveExisting = false)
        {
            if (!preserveExisting)
            {
                photoStripItems.Clear();
            }
            
            if (preserveExisting)
            {
                // Don't clear if we're preserving existing photos
                return;
            }
            
            // Add placeholder items for all photos needed
            for (int i = 0; i < totalPhotosNeeded; i++)
            {
                photoStripItems.Add(new PhotoStripItem 
                { 
                    PhotoNumber = i + 1,
                    IsPlaceholder = true,
                    Image = null
                });
            }
        }
        
        private void StopPhotoSequence()
        {
            isCapturing = false;
            countdownTimer.Stop();
            liveViewTimer.Stop();
            countdownOverlay.Visibility = Visibility.Collapsed;
            
            // Cancel any pending capture timeouts
            currentCaptureToken?.Cancel();
            
            // Don't show start button here - only show after successful composition
            // Hide the stop button
            if (stopSessionButton != null)
            {
                stopSessionButton.Visibility = Visibility.Collapsed;
            }
            
            // Note: Removed old button enable/disable since they don't exist in bottom bar anymore
            
            try
            {
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    DeviceManager.SelectedCameraDevice.StopLiveView();
                    // Reset IsBusy flag after stopping
                    DeviceManager.SelectedCameraDevice.IsBusy = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to stop live view", ex);
                // Still try to reset IsBusy flag even if stop fails
                if (DeviceManager?.SelectedCameraDevice != null)
                {
                    DeviceManager.SelectedCameraDevice.IsBusy = false;
                }
            }
            
            // Don't override status text - let the caller set appropriate message
        }
        
        private void HandlePhotoSequenceProgress(string capturedFileName)
        {
            Log.Debug("=== HANDLE PHOTO SEQUENCE PROGRESS ===");
            Log.Debug($"HandlePhotoSequenceProgress: Called at {DateTime.Now:HH:mm:ss.fff}");
            Log.Debug($"HandlePhotoSequenceProgress: capturedFileName={capturedFileName}");
            Log.Debug($"HandlePhotoSequenceProgress: currentPhotoIndex={currentPhotoIndex}, totalPhotosNeeded={totalPhotosNeeded}");
            Log.Debug($"HandlePhotoSequenceProgress: currentEvent={currentEvent?.Name}, currentTemplate={currentTemplate?.Name}");
            
            if (currentEvent != null && currentTemplate != null)
            {
                Log.Debug($"HandlePhotoSequenceProgress: In event mode - checking if {currentPhotoIndex} < {totalPhotosNeeded}");
                
                // Check if we need more photos for this template
                if (currentPhotoIndex < totalPhotosNeeded)
                {
                    Log.Debug($"HandlePhotoSequenceProgress: More photos needed ({currentPhotoIndex} of {totalPhotosNeeded})");
                    
                    // Check if Photographer Mode is enabled
                    bool photographerMode = Properties.Settings.Default.PhotographerMode;
                    Log.Debug($"HandlePhotoSequenceProgress: PhotographerMode = {photographerMode}");
                    
                    if (photographerMode)
                    {
                        // Photographer Mode - wait for manual trigger
                        Log.Debug("HandlePhotoSequenceProgress: Photographer Mode enabled - waiting for manual trigger");
                        statusText.Text = $"Photo {currentPhotoIndex} saved! Ready for photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Press camera trigger when ready";
                        
                        // Stop current photo sequence to reset camera state
                        Log.Debug("HandlePhotoSequenceProgress: Calling StopPhotoSequence() for Photographer Mode");
                        StopPhotoSequence();
                        
                        // The next photo will be triggered when the photographer presses the camera button
                        // The camera's photo capture event will still fire and be handled normally
                    }
                    else
                    {
                        // Normal auto-progression mode
                        // More photos needed - stop current sequence and prepare for next
                        statusText.Text = $"Photo {currentPhotoIndex} saved! Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} starting soon...";
                        
                        // Stop current photo sequence to reset camera state
                        Log.Debug("HandlePhotoSequenceProgress: Calling StopPhotoSequence()");
                        StopPhotoSequence();
                        
                        // Preview delay before next photo
                        int displayDuration = Properties.Settings.Default.PhotoDisplayDuration * 1000; // Convert to milliseconds
                        Log.Debug($"HandlePhotoSequenceProgress: Starting {displayDuration}ms delay before next photo");
                        Task.Delay(displayDuration).ContinueWith(_ =>
                        {
                            Log.Debug($"HandlePhotoSequenceProgress: {displayDuration}ms delay complete, checking camera state");
                            Dispatcher.Invoke(async () =>
                            {
                                Log.Debug($"HandlePhotoSequenceProgress: Camera state - Device!=null: {DeviceManager.SelectedCameraDevice != null}, IsBusy: {DeviceManager.SelectedCameraDevice?.IsBusy}");
                                
                                // Check if camera is available and not busy
                                if (DeviceManager.SelectedCameraDevice != null && !DeviceManager.SelectedCameraDevice.IsBusy)
                                {
                                    Log.Debug($"HandlePhotoSequenceProgress: Camera ready - starting photo {currentPhotoIndex + 1} of {totalPhotosNeeded}");
                                    statusText.Text = $"Starting photo {currentPhotoIndex + 1} of {totalPhotosNeeded}...";
                                    
                                    // Start the next photo sequence normally
                                    StartPhotoSequence();
                                }
                                else if (DeviceManager.SelectedCameraDevice != null && DeviceManager.SelectedCameraDevice.IsBusy)
                                {
                                    Log.Debug("HandlePhotoSequenceProgress: Camera busy - attempting reset");
                                    // Camera busy - try simple reset without full reconnection
                                    statusText.Text = "Camera busy - resetting...";
                                    
                                    try
                                    {
                                        DeviceManager.SelectedCameraDevice.IsBusy = false;
                                        Log.Debug("HandlePhotoSequenceProgress: Set IsBusy=false, waiting 1 second");
                                        await Task.Delay(1000);
                                        Log.Debug("HandlePhotoSequenceProgress: Starting photo sequence after busy reset");
                                        StartPhotoSequence();
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("HandlePhotoSequenceProgress: Camera reset failed during multi-photo", ex);
                                        statusText.Text = $"Camera reset failed - Touch START for photo {currentPhotoIndex + 1} of {totalPhotosNeeded}";
                                        // Show start button for retry
                                        if (startButtonOverlay != null)
                                            startButtonOverlay.Visibility = Visibility.Visible;
                                        if (stopSessionButton != null)
                                            stopSessionButton.Visibility = Visibility.Collapsed;
                                    }
                                }
                                else
                                {
                                    Log.Debug("HandlePhotoSequenceProgress: No camera - showing manual prompt");
                                    // No camera - show manual prompt
                                    statusText.Text = $"Camera not connected - Touch START for photo {currentPhotoIndex + 1} of {totalPhotosNeeded}";
                                    // Show start button for retry
                                    if (startButtonOverlay != null)
                                        startButtonOverlay.Visibility = Visibility.Visible;
                                    if (stopSessionButton != null)
                                        stopSessionButton.Visibility = Visibility.Collapsed;
                                }
                            });
                        });
                    }
                }
                else
                {
                    // All photos for template completed
                    statusText.Text = $"All {totalPhotosNeeded} photos captured!";
                    
                    // Show retake review or process template
                    ShowRetakeReview();
                }
            }
            else
            {
                // Standard single photo mode
                statusText.Text = $"Photo saved! File: {Path.GetFileName(capturedFileName)}";
                
                Task.Delay(2000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!isCapturing)
                        {
                            StopPhotoSequence();
                            statusText.Text = "Touch START to take another photo";
                        }
                    });
                });
            }
        }

        private void LiveViewTimer_Tick(object sender, EventArgs e)
        {
            if (DeviceManager.SelectedCameraDevice == null)
                return;

            try
            {
                LiveViewData liveViewData = DeviceManager.SelectedCameraDevice.GetLiveViewImage();
                
                if (liveViewData?.ImageData != null)
                {
                    var bitmap = new Bitmap(new MemoryStream(
                        liveViewData.ImageData,
                        liveViewData.ImageDataPosition,
                        liveViewData.ImageData.Length - liveViewData.ImageDataPosition));
                    
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        bitmap.GetHbitmap(),
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    liveViewImage.Source = bitmapSource;
                }
            }
            catch
            {
                // Ignore live view errors
            }
        }

        private void IncreaseCountdown_Click(object sender, RoutedEventArgs e)
        {
            if (countdownSeconds < 10)
            {
                countdownSeconds++;
                countdownSecondsDisplay.Text = $"{countdownSeconds}s";
                
                // Save to settings
                Properties.Settings.Default.CountdownSeconds = countdownSeconds;
                Properties.Settings.Default.Save();
            }
        }

        private void DecreaseCountdown_Click(object sender, RoutedEventArgs e)
        {
            if (countdownSeconds > 1)
            {
                countdownSeconds--;
                countdownSecondsDisplay.Text = $"{countdownSeconds}s";
                
                // Save to settings
                Properties.Settings.Default.CountdownSeconds = countdownSeconds;
                Properties.Settings.Default.Save();
            }
        }
        
        private void ToggleBottomBar_Click(object sender, MouseButtonEventArgs e)
        {
            if (bottomControlBar.Visibility == Visibility.Collapsed)
            {
                // Check if locked and PIN is enabled before showing the navbar
                if (Properties.Settings.Default.EnableLockFeature && _isLocked)
                {
                    Log.Debug("ToggleBottomBar_Click: Interface is locked, showing PIN entry");
                    _pendingActionAfterUnlock = () => 
                    {
                        bottomControlBar.Visibility = Visibility.Visible;
                        bottomBarToggleChevron.Text = "⌄"; // Down chevron
                    };
                    ShowPinEntryDialog();
                    return;
                }
                
                bottomControlBar.Visibility = Visibility.Visible;
                bottomBarToggleChevron.Text = "⌄"; // Down chevron
            }
            else
            {
                bottomControlBar.Visibility = Visibility.Collapsed;
                bottomBarToggleChevron.Text = "⌃"; // Up chevron
            }
        }

        // Event Handlers
        private void DeviceManager_CameraConnected(ICameraDevice cameraDevice)
        {
            Dispatcher.Invoke(() =>
            {
                cameraStatusText.Text = $"Connected: {cameraDevice.DeviceName}";
                
                // Only start live view if we're idle and the setting allows it
                // Live view will always work during active sessions
                if (!isCapturing && Properties.Settings.Default.EnableIdleLiveView)
                {
                    try
                    {
                        liveViewTimer.Start();
                        DeviceManager.SelectedCameraDevice?.StartLiveView();
                        Log.Debug("DeviceManager_CameraConnected: Started idle live view");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"DeviceManager_CameraConnected: Failed to start idle live view: {ex.Message}");
                    }
                }
                
                if (currentEvent != null && currentTemplate != null)
                {
                    statusText.Text = $"Event: {currentEvent.Name} - Ready for photo {currentPhotoIndex + 1} of {totalPhotosNeeded}";
                }
                else if (currentEvent != null)
                {
                    statusText.Text = $"Event: {currentEvent.Name} - Touch START to select template";
                }
                else
                {
                    statusText.Text = "Camera ready - Touch START to begin";
                }
            });
        }

        private void DeviceManager_CameraDisconnected(ICameraDevice cameraDevice)
        {
            Dispatcher.Invoke(() =>
            {
                cameraStatusText.Text = "Camera disconnected";
                
                StopPhotoSequence();
                
                if (currentEvent != null)
                {
                    statusText.Text = $"Event: {currentEvent.Name} - Please connect a camera";
                }
                else
                {
                    statusText.Text = "Please connect a camera";
                }
            });
        }

        private void DeviceManager_CameraSelected(ICameraDevice oldcameraDevice, ICameraDevice newcameraDevice)
        {
            // Handle camera selection changes
        }

        private void DeviceManager_PhotoCaptured(object sender, PhotoCapturedEventArgs eventArgs)
        {
            Log.Debug("=== PHOTO CAPTURED EVENT FIRED ===");
            Log.Debug($"PhotoboothTouch: DeviceManager_PhotoCaptured event fired at {DateTime.Now:HH:mm:ss.fff}!");
            Log.Debug($"PhotoboothTouch: EventArgs - Device={eventArgs?.CameraDevice?.GetType().Name}, FileName={eventArgs?.FileName}, Handle={eventArgs?.Handle?.GetType().Name}");
            Log.Debug($"PhotoboothTouch: Sender type: {sender?.GetType().Name}");
            Log.Debug($"PhotoboothTouch: Current photo index: {currentPhotoIndex}, Total needed: {totalPhotosNeeded}");
            
            // Use the exact same approach as working Camera.xaml.cs
            PhotoCaptured(eventArgs);
        }

        private void PhotoCaptured(object o)
        {
            // Skip if we're capturing a test photo for the settings modal
            if (_isCapturingTestPhoto)
            {
                Log.Debug("PhotoCaptured: Skipping - test photo capture in progress");
                return;
            }
            
            Log.Debug("=== ENTERING PHOTOCAPTURED METHOD ===");
            PhotoCapturedEventArgs eventArgs = o as PhotoCapturedEventArgs;
            if (eventArgs == null)
            {
                Log.Error("PhotoCaptured: eventArgs is null");
                Dispatcher.Invoke(() =>
                {
                    statusText.Text = "Photo capture failed - no data";
                    StopPhotoSequence();
                });
                return;
            }
            
            Log.Debug($"PhotoCaptured: Processing at {DateTime.Now:HH:mm:ss.fff}");
            Log.Debug($"PhotoCaptured: Device={eventArgs.CameraDevice?.GetType().Name}, FileName={eventArgs.FileName}, Handle={eventArgs.Handle?.GetType().Name}");
            Log.Debug($"PhotoCaptured: Camera IsBusy before processing: {eventArgs.CameraDevice?.IsBusy}");
            Log.Debug($"PhotoCaptured: Current photo index before increment: {currentPhotoIndex}");
            
            try
            {
                string fileName = Path.Combine(FolderForPhotos, Path.GetFileName(eventArgs.FileName));
                Log.Debug($"PhotoCaptured: Target file path={fileName}");
                
                // if file exist try to generate a new filename to prevent file lost. 
                // This useful when camera is set to record in ram the the all file names are same.
                if (File.Exists(fileName))
                {
                    fileName = StaticHelper.GetUniqueFilename(
                        Path.GetDirectoryName(fileName) + "\\" + Path.GetFileNameWithoutExtension(fileName) + "_", 0,
                        Path.GetExtension(fileName));
                    Log.Debug($"PhotoCaptured: Using unique filename={fileName}");
                }

                // check the folder of filename, if not found create it
                if (!Directory.Exists(Path.GetDirectoryName(fileName)))
                {
                    Log.Debug($"PhotoCaptured: Creating directory={Path.GetDirectoryName(fileName)}");
                    Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                }
                
                Log.Debug("PhotoCaptured: Starting TransferFile");
                eventArgs.CameraDevice.TransferFile(eventArgs.Handle, fileName);
                Log.Debug("PhotoCaptured: TransferFile completed");
                
                // CRITICAL: Release the camera resource after file transfer
                // This is required by ICameraDevice interface to free camera for next capture
                try
                {
                    eventArgs.CameraDevice.ReleaseResurce(eventArgs.Handle);
                    Log.Debug("PhotoCaptured: ReleaseResurce completed");
                }
                catch (Exception releaseEx)
                {
                    Log.Error("PhotoCaptured: Failed to release camera resource", releaseEx);
                }
                
                if (File.Exists(fileName))
                {
                    // Marshal UI update to UI thread
                    Dispatcher.Invoke(() => 
                    {
                        photoCount++;
                        currentPhotoIndex++;
                        photoCountText.Text = $"Photos: {photoCount}";
                        
                        // Handle retake or normal capture
                        if (isRetakingPhoto && photoIndexToRetake >= 0)
                        {
                            // Replace the specific photo for retake
                            if (photoIndexToRetake < capturedPhotoPaths.Count)
                            {
                                capturedPhotoPaths[photoIndexToRetake] = fileName;
                                Log.Debug($"PhotoCaptured: Replaced photo {photoIndexToRetake + 1} with retake: {fileName}");
                                
                                // Don't increment currentPhotoIndex for retakes
                                currentPhotoIndex--; // Cancel the increment that happened above
                            }
                        }
                        else
                        {
                            // Add captured photo to list for template composition
                            capturedPhotoPaths.Add(fileName);
                            Log.Debug($"PhotoCaptured: Added photo {currentPhotoIndex} to list: {fileName}");
                            
                            // Save photo to database
                            SavePhotoToDatabase(fileName, currentPhotoIndex, "Original");
                        }
                        
                        // Add to photo strip
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(fileName);
                            bitmap.DecodePixelWidth = 240; // Thumbnail size for performance
                            bitmap.EndInit();
                            bitmap.Freeze(); // Make it thread-safe
                            
                            // Handle retake or normal capture for photo strip
                            if (isRetakingPhoto && photoIndexToRetake >= 0)
                            {
                                // Replace the photo in the strip for retake
                                if (photoIndexToRetake < photoStripImages.Count)
                                {
                                    photoStripImages[photoIndexToRetake] = bitmap;
                                }
                                if (photoIndexToRetake < photoStripItems.Count)
                                {
                                    photoStripItems[photoIndexToRetake].Image = bitmap;
                                    photoStripItems[photoIndexToRetake].IsPlaceholder = false;
                                }
                            }
                            else
                            {
                                // Normal capture - add to strip
                                photoStripImages.Add(bitmap);
                                
                                // Update the photo strip item
                                if (currentPhotoIndex - 1 < photoStripItems.Count)
                                {
                                    photoStripItems[currentPhotoIndex - 1].Image = bitmap;
                                    photoStripItems[currentPhotoIndex - 1].IsPlaceholder = false;
                                    photoStripItems[currentPhotoIndex - 1].ItemType = "Photo";
                                    photoStripItems[currentPhotoIndex - 1].FilePath = fileName;
                                }
                            }
                            
                            Log.Debug($"PhotoCaptured: Added thumbnail to photo strip");
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"PhotoCaptured: Failed to add to photo strip: {ex.Message}");
                        }
                        
                        // Show captured image
                        liveViewImage.Source = (new ImageSourceConverter()).ConvertFromString(fileName) as ImageSource;
                        
                        // Ensure countdown overlay is hidden
                        countdownOverlay.Visibility = Visibility.Collapsed;
                        
                        // Handle retake completion or normal sequence
                        if (isRetakingPhoto)
                        {
                            // Reset retake flags
                            isRetakingPhoto = false;
                            photoIndexToRetake = -1;
                            
                            // Show review again with updated photo
                            ShowRetakeReview();
                        }
                        else
                        {
                            // Handle event workflow photo sequence
                            HandlePhotoSequenceProgress(fileName);
                        }
                    });
                    Log.Debug($"PhotoCaptured: Image saved successfully, size={new FileInfo(fileName).Length} bytes");
                    
                    // the IsBusy may used internally, if file transfer is done should set to false  
                    eventArgs.CameraDevice.IsBusy = false;
                    
                    // Record when this capture completed
                    lastCaptureTime = DateTime.Now;
                }
                else
                {
                    Log.Error($"PhotoCaptured: File was not created: {fileName}");
                    
                    // Still need to release resource even if file creation failed
                    try
                    {
                        eventArgs.CameraDevice.ReleaseResurce(eventArgs.Handle);
                        Log.Debug("PhotoCaptured: ReleaseResurce completed (after file error)");
                    }
                    catch (Exception releaseEx)
                    {
                        Log.Error("PhotoCaptured: Failed to release camera resource after file error", releaseEx);
                    }
                    
                    eventArgs.CameraDevice.IsBusy = false;
                    Dispatcher.Invoke(() =>
                    {
                        statusText.Text = $"Photo transfer failed - file not found: {fileName}";
                        StopPhotoSequence();
                    });
                }
            }
            catch (Exception exception)
            {
                Log.Error("PhotoCaptured: Exception occurred", exception);
                
                // Always try to release resource in case of errors
                try
                {
                    eventArgs.CameraDevice.ReleaseResurce(eventArgs.Handle);
                    Log.Debug("PhotoCaptured: ReleaseResurce completed (after exception)");
                }
                catch (Exception releaseEx)
                {
                    Log.Error("PhotoCaptured: Failed to release camera resource after exception", releaseEx);
                }
                
                eventArgs.CameraDevice.IsBusy = false;
                Dispatcher.Invoke(() =>
                {
                    statusText.Text = $"Photo save error: {exception.Message}";
                    StopPhotoSequence();
                });
            }
        }


        private void Log_LogMessage(LogEventArgs e)
        {
            // Output to Visual Studio Debug Output window
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {e.Message}");
            
            // Also output to console for command line debugging
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {e.Message}");
            
            // Include exception details if present
            if (e.Exception != null)
            {
                var exceptionMsg = $"[{DateTime.Now:HH:mm:ss.fff}] EXCEPTION: {e.Exception.Message}\n{e.Exception.StackTrace}";
                System.Diagnostics.Debug.WriteLine(exceptionMsg);
                Console.WriteLine(exceptionMsg);
            }
        }

        private async Task<string> ComposeTemplateWithPhotos()
        {
            try
            {
                if (currentTemplate == null || capturedPhotoPaths.Count == 0)
                {
                    Log.Error("ComposeTemplateWithPhotos: No template or photos available");
                    return null;
                }
                
                Log.Debug($"ComposeTemplateWithPhotos: Starting composition with {capturedPhotoPaths.Count} photos");
                Log.Debug($"ComposeTemplateWithPhotos: Template ID={currentTemplate.Id}, Name={currentTemplate.Name}");
                
                // Load template data and canvas items from database
                var canvasItems = database.GetCanvasItems(currentTemplate.Id);
                if (canvasItems == null || canvasItems.Count == 0)
                {
                    Log.Error($"ComposeTemplateWithPhotos: No canvas items found for template {currentTemplate.Id}");
                    return null;
                }
                
                // Use template dimensions from database
                int templateWidth = (int)currentTemplate.CanvasWidth;
                int templateHeight = (int)currentTemplate.CanvasHeight;
                
                // Create a new bitmap for the final composition
                var finalBitmap = new System.Drawing.Bitmap(templateWidth, templateHeight);
                using (var graphics = Graphics.FromImage(finalBitmap))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    
                    // CRITICAL: Set text rendering hints for smooth, non-jagged text
                    // Use ClearType for the best text quality
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    graphics.TextContrast = 0; // Use default ClearType contrast
                    
                    // Set background color
                    if (!string.IsNullOrEmpty(currentTemplate.BackgroundColor))
                    {
                        try
                        {
                            var bgColor = ColorTranslator.FromHtml(currentTemplate.BackgroundColor);
                            graphics.Clear(bgColor);
                        }
                        catch
                        {
                            graphics.Clear(System.Drawing.Color.White);
                        }
                    }
                    else
                    {
                        graphics.Clear(System.Drawing.Color.White);
                    }
                    
                    // Process canvas items in Z-order  
                    var sortedItems = canvasItems.OrderBy(item => item.ZIndex).ToList();
                    
                    Log.Debug($"ComposeTemplateWithPhotos: Processing {sortedItems.Count} canvas items");
                    
                    foreach (var item in sortedItems)
                    {
                        var destRect = new Rectangle(
                            (int)item.X,
                            (int)item.Y,
                            (int)item.Width,
                            (int)item.Height);
                        
                        Log.Debug($"ComposeTemplateWithPhotos: Processing item - Type={item.ItemType}, Name={item.Name}, ZIndex={item.ZIndex}, Rotation={item.Rotation}, PlaceholderNo={item.PlaceholderNumber}, ImagePath={item.ImagePath}");
                        
                        // Additional debug logging for placeholders
                        if (item.ItemType == "PlaceholderCanvasItem" || item.ItemType == "Placeholder")
                        {
                            Log.Debug($"ComposeTemplateWithPhotos: Found placeholder - PlaceholderNumber={item.PlaceholderNumber}, capturedPhotoPaths.Count={capturedPhotoPaths.Count}");
                            for (int i = 0; i < capturedPhotoPaths.Count; i++)
                            {
                                Log.Debug($"ComposeTemplateWithPhotos: capturedPhotoPaths[{i}] = {capturedPhotoPaths[i]}");
                            }
                        }
                        
                        // Save graphics state before applying rotation
                        var savedState = graphics.Save();
                        
                        // Apply rotation if needed
                        if (Math.Abs(item.Rotation) > 0.01) // Only rotate if rotation is significant
                        {
                            // Calculate center point of the item
                            float centerX = destRect.X + destRect.Width / 2f;
                            float centerY = destRect.Y + destRect.Height / 2f;
                            
                            // Translate to center, rotate, then translate back
                            graphics.TranslateTransform(centerX, centerY);
                            graphics.RotateTransform((float)item.Rotation);
                            graphics.TranslateTransform(-centerX, -centerY);
                        }
                        
                        if (item.ItemType == "PlaceholderCanvasItem" || item.ItemType == "Placeholder")
                        {
                            // This is a photo placeholder - insert captured photo
                            // Match photo to placeholder by PlaceholderNumber
                            int placeholderIndex = (item.PlaceholderNumber ?? 1) - 1; // PlaceholderNumber is 1-based
                            Log.Debug($"ComposeTemplateWithPhotos: Placeholder check - PlaceholderNumber={item.PlaceholderNumber}, placeholderIndex={placeholderIndex}, capturedPhotoPaths.Count={capturedPhotoPaths.Count}");
                            
                            if (placeholderIndex >= 0 && placeholderIndex < capturedPhotoPaths.Count)
                            {
                                var photoPath = capturedPhotoPaths[placeholderIndex];
                                Log.Debug($"ComposeTemplateWithPhotos: Inserting photo into placeholder {item.PlaceholderNumber} from {photoPath}");
                                
                                if (File.Exists(photoPath))
                                {
                                    using (var photo = System.Drawing.Image.FromFile(photoPath))
                                    {
                                        // Apply border/outline if specified
                                        if (item.HasOutline && item.OutlineThickness > 0 && !string.IsNullOrEmpty(item.OutlineColor))
                                        {
                                            try
                                            {
                                                var borderColor = ColorTranslator.FromHtml(item.OutlineColor);
                                                using (var pen = new System.Drawing.Pen(borderColor, (float)item.OutlineThickness))
                                                {
                                                    graphics.DrawRectangle(pen, destRect);
                                                }
                                            }
                                            catch { }
                                        }
                                        
                                        // Draw the photo - preserve aspect ratio to fill placeholder
                                        var sourceAspect = (float)photo.Width / photo.Height;
                                        var destAspect = (float)destRect.Width / destRect.Height;
                                        
                                        Rectangle sourceRect;
                                        if (sourceAspect > destAspect)
                                        {
                                            // Photo is wider - crop sides
                                            int cropWidth = (int)(photo.Height * destAspect);
                                            int xOffset = (photo.Width - cropWidth) / 2;
                                            sourceRect = new Rectangle(xOffset, 0, cropWidth, photo.Height);
                                        }
                                        else
                                        {
                                            // Photo is taller - crop top/bottom
                                            int cropHeight = (int)(photo.Width / destAspect);
                                            int yOffset = (photo.Height - cropHeight) / 2;
                                            sourceRect = new Rectangle(0, yOffset, photo.Width, cropHeight);
                                        }
                                        
                                        graphics.DrawImage(photo, destRect, sourceRect, GraphicsUnit.Pixel);
                                        Log.Debug($"ComposeTemplateWithPhotos: Successfully drew photo into placeholder {item.PlaceholderNumber}");
                                    }
                                }
                                else
                                {
                                    Log.Error($"ComposeTemplateWithPhotos: Photo file not found: {photoPath}");
                                }
                            }
                            else
                            {
                                Log.Debug($"ComposeTemplateWithPhotos: Skipping placeholder {item.PlaceholderNumber} - no photo available (placeholderIndex={placeholderIndex}, capturedPhotoPaths.Count={capturedPhotoPaths.Count})");
                            }
                        }
                        else if ((item.ItemType == "ImageCanvasItem" || item.ItemType == "Image") && !string.IsNullOrEmpty(item.ImagePath))
                        {
                            // Load image from organized asset folder
                            try
                            {
                                // Convert URI to local file path if needed
                                string imagePath = item.ImagePath;
                                if (imagePath.StartsWith("file:///"))
                                {
                                    imagePath = new Uri(imagePath).LocalPath;
                                }
                                else if (imagePath.StartsWith("file://"))
                                {
                                    imagePath = imagePath.Substring(7);
                                }
                                
                                Log.Debug($"ComposeTemplateWithPhotos: Checking image path: {imagePath}");
                                
                                if (File.Exists(imagePath))
                                {
                                    using (var img = System.Drawing.Image.FromFile(imagePath))
                                    {
                                        var originalComposite = graphics.CompositingMode;
                                        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                                        
                                        graphics.DrawImage(img, destRect);
                                        Log.Debug($"ComposeTemplateWithPhotos: Drew image from path {imagePath}");
                                        
                                        graphics.CompositingMode = originalComposite;
                                    }
                                }
                                else
                                {
                                    Log.Debug($"ComposeTemplateWithPhotos: Image file not found: {imagePath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug($"ComposeTemplateWithPhotos: Failed to draw image from path: {ex.Message}");
                            }
                        }
                        else if (item.ItemType == "Text")
                        {
                            // Render text item
                            try
                            {
                                if (!string.IsNullOrEmpty(item.Text))
                                {
                                    // Parse font style
                                    var fontStyle = System.Drawing.FontStyle.Regular;
                                    if (item.IsBold) fontStyle |= System.Drawing.FontStyle.Bold;
                                    if (item.IsItalic) fontStyle |= System.Drawing.FontStyle.Italic;
                                    if (item.IsUnderlined) fontStyle |= System.Drawing.FontStyle.Underline;
                                    
                                    // Create font
                                    // WPF font sizes are in points (typography standard)
                                    // GDI+ also uses points, so we can use the value directly
                                    // However, we need to scale down to match visual appearance
                                    var wpfFontSize = (float)(item.FontSize ?? 12);
                                    var fontFamily = item.FontFamily ?? "Arial";
                                    
                                    // Apply scaling factor to match WPF's visual rendering
                                    // This factor accounts for the difference in how WPF and GDI+ render fonts
                                    var scaledFontSize = wpfFontSize * 0.65f;
                                    
                                    using (var font = new System.Drawing.Font(fontFamily, scaledFontSize, fontStyle, GraphicsUnit.Point))
                                    {
                                        // Parse text color
                                        var textColor = System.Drawing.Color.Black;
                                        if (!string.IsNullOrEmpty(item.TextColor))
                                        {
                                            try
                                            {
                                                textColor = ColorTranslator.FromHtml(item.TextColor);
                                            }
                                            catch
                                            {
                                                textColor = System.Drawing.Color.Black;
                                            }
                                        }
                                        
                                        using (var brush = new SolidBrush(textColor))
                                        {
                                            // Draw shadow if enabled
                                            if (item.HasShadow && !string.IsNullOrEmpty(item.ShadowColor))
                                            {
                                                try
                                                {
                                                    var shadowColor = ColorTranslator.FromHtml(item.ShadowColor);
                                                    using (var shadowBrush = new SolidBrush(System.Drawing.Color.FromArgb(128, shadowColor)))
                                                    {
                                                        var shadowRect = new RectangleF(
                                                            destRect.X + (float)item.ShadowOffsetX,
                                                            destRect.Y + (float)item.ShadowOffsetY,
                                                            destRect.Width,
                                                            destRect.Height);
                                                        graphics.DrawString(item.Text, font, shadowBrush, shadowRect);
                                                    }
                                                }
                                                catch { }
                                            }
                                            
                                            // Draw outline if enabled
                                            if (item.HasOutline && item.OutlineThickness > 0 && !string.IsNullOrEmpty(item.OutlineColor))
                                            {
                                                try
                                                {
                                                    var outlineColor = ColorTranslator.FromHtml(item.OutlineColor);
                                                    using (var outlinePen = new System.Drawing.Pen(outlineColor, (float)item.OutlineThickness))
                                                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                                                    {
                                                        // Set pen properties for smooth outlines
                                                        outlinePen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                                                        outlinePen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                                                        outlinePen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                                                        
                                                        // Use EmSize in world units (convert points to pixels for the graphics context)
                                                        var emSize = scaledFontSize * graphics.DpiY / 72f;
                                                        
                                                        // Create format with NoWrap to match the main text rendering
                                                        var outlineFormat = new StringFormat();
                                                        if (item.TextAlignment == "Center")
                                                            outlineFormat.Alignment = StringAlignment.Center;
                                                        else if (item.TextAlignment == "Right")
                                                            outlineFormat.Alignment = StringAlignment.Far;
                                                        else
                                                            outlineFormat.Alignment = StringAlignment.Near;
                                                        outlineFormat.FormatFlags = StringFormatFlags.NoWrap;
                                                        outlineFormat.LineAlignment = StringAlignment.Near;
                                                        
                                                        // Use the exact destRect for consistent positioning
                                                        path.AddString(item.Text, font.FontFamily, (int)font.Style, 
                                                            emSize, destRect, outlineFormat);
                                                        graphics.DrawPath(outlinePen, path);
                                                    }
                                                }
                                                catch { }
                                            }
                                            
                                            // Draw the text
                                            var format = new StringFormat();
                                            if (item.TextAlignment == "Center")
                                                format.Alignment = StringAlignment.Center;
                                            else if (item.TextAlignment == "Right")
                                                format.Alignment = StringAlignment.Far;
                                            else
                                                format.Alignment = StringAlignment.Near;
                                            
                                            // Disable wrapping
                                            format.FormatFlags = StringFormatFlags.NoWrap;
                                            format.LineAlignment = StringAlignment.Near;
                                            
                                            // Draw text at the exact position stored in the template
                                            graphics.DrawString(item.Text, font, brush, destRect, format);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug($"ComposeTemplateWithPhotos: Failed to draw text: {ex.Message}");
                            }
                        }
                        else if (item.ItemType == "Shape")
                        {
                            // Render shape item
                            try
                            {
                                // Parse fill color
                                System.Drawing.Brush fillBrush = null;
                                if (!item.HasNoFill && !string.IsNullOrEmpty(item.FillColor))
                                {
                                    try
                                    {
                                        var fillColor = ColorTranslator.FromHtml(item.FillColor);
                                        fillBrush = new SolidBrush(fillColor);
                                    }
                                    catch
                                    {
                                        fillBrush = new SolidBrush(System.Drawing.Color.Gray);
                                    }
                                }
                                
                                // Parse stroke color and thickness
                                System.Drawing.Pen strokePen = null;
                                if (!item.HasNoStroke && !string.IsNullOrEmpty(item.StrokeColor))
                                {
                                    try
                                    {
                                        var strokeColor = ColorTranslator.FromHtml(item.StrokeColor);
                                        strokePen = new System.Drawing.Pen(strokeColor, (float)(item.StrokeThickness > 0 ? item.StrokeThickness : 1));
                                    }
                                    catch
                                    {
                                        strokePen = new System.Drawing.Pen(System.Drawing.Color.Black, 1);
                                    }
                                }
                                
                                // Draw based on shape type
                                if (item.ShapeType == "Rectangle")
                                {
                                    if (fillBrush != null)
                                        graphics.FillRectangle(fillBrush, destRect);
                                    if (strokePen != null)
                                        graphics.DrawRectangle(strokePen, destRect);
                                }
                                else if (item.ShapeType == "Ellipse" || item.ShapeType == "Circle")
                                {
                                    if (fillBrush != null)
                                        graphics.FillEllipse(fillBrush, destRect);
                                    if (strokePen != null)
                                        graphics.DrawEllipse(strokePen, destRect);
                                }
                                else if (item.ShapeType == "Line")
                                {
                                    if (strokePen != null)
                                    {
                                        graphics.DrawLine(strokePen, 
                                            destRect.X, destRect.Y, 
                                            destRect.X + destRect.Width, destRect.Y + destRect.Height);
                                    }
                                }
                                
                                // Cleanup
                                fillBrush?.Dispose();
                                strokePen?.Dispose();
                            }
                            catch (Exception ex)
                            {
                                Log.Debug($"ComposeTemplateWithPhotos: Failed to draw shape: {ex.Message}");
                            }
                        }
                        
                        // Restore graphics state after drawing each item (resets rotation)
                        graphics.Restore(savedState);
                    }
                }
                
                // Check if this is a 2x6 template and needs duplication for 4x6 printing
                bool is2x6Template = Is2x6Template(templateWidth, templateHeight);
                lastProcessedWas2x6Template = is2x6Template; // Store for printer routing
                
                // Check setting for 2x6 duplication behavior
                bool duplicate2x6To4x6 = Properties.Settings.Default.Duplicate2x6To4x6;
                
                // Save the final composed image
                string outputDir = Path.Combine(FolderForPhotos, "Composed");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string outputPath = Path.Combine(outputDir, $"{currentEvent.Name}_{currentTemplate.Name}_{timestamp}.jpg");
                
                // Save as high-quality JPEG
                var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                    .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    Encoder.Quality, 95L);
                
                // Always save the original composed image for display
                finalBitmap.Save(outputPath, jpegEncoder, encoderParams);
                Log.Debug($"ComposeTemplateWithPhotos: Saved original composed image to {outputPath}");
                
                // Store the display path
                lastProcessedImagePath = outputPath;
                
                // Save composed image to database
                string outputFormat = is2x6Template ? "2x6" : "4x6";
                SaveComposedImageToDatabase(outputPath, outputFormat);
                
                // Add composed image to photo strip (must be on UI thread) - use BeginInvoke to avoid deadlock
                Dispatcher.BeginInvoke(new Action(() => AddComposedImageToPhotoStrip(outputPath)));
                
                Log.Debug("ComposeTemplateWithPhotos: After Dispatcher.BeginInvoke call");
                
                // If it's a 2x6 template and duplication is enabled, create a 4x6 version for printing only
                if (is2x6Template && duplicate2x6To4x6)
                {
                    Log.Debug($"ComposeTemplateWithPhotos: Detected 2x6 template ({templateWidth}x{templateHeight}), creating hidden 4x6 version for printing");
                    
                    using (var duplicatedBitmap = Create4x6From2x6(finalBitmap))
                    {
                        string printPath = Path.Combine(outputDir, $"{currentEvent.Name}_{currentTemplate.Name}_{timestamp}_4x6_print.jpg");
                        duplicatedBitmap.Save(printPath, jpegEncoder, encoderParams);
                        lastProcessedImagePathForPrinting = printPath;
                        Log.Debug($"ComposeTemplateWithPhotos: Saved 4x6 print version to {printPath}");
                    }
                }
                else
                {
                    // For non-2x6 templates or when duplication is disabled, use the same image for printing
                    lastProcessedImagePathForPrinting = outputPath;
                    if (is2x6Template)
                    {
                        Log.Debug($"ComposeTemplateWithPhotos: Detected 2x6 template, keeping as single 2x6 for printing (duplication disabled)");
                    }
                }
                
                finalBitmap.Dispose();
                
                Log.Debug($"ComposeTemplateWithPhotos: About to return outputPath: '{outputPath}'");
                Log.Debug($"ComposeTemplateWithPhotos: File exists at return: {File.Exists(outputPath)}");
                
                return outputPath; // Return the original for display
            }
            catch (Exception ex)
            {
                Log.Error("ComposeTemplateWithPhotos: Failed to compose template", ex);
                return null;
            }
        }
        
        private bool Is2x6Template(int width, int height)
        {
            // Check if dimensions match 2x6 at common DPI (300 DPI)
            // 2x6 inches at 300 DPI = 600x1800 pixels
            // Allow some tolerance for different DPI settings
            
            float aspectRatio = (float)width / height;
            float expectedRatio = 2.0f / 6.0f; // 0.333
            
            // Check if aspect ratio matches 2:6 (with 5% tolerance)
            bool ratioMatches = Math.Abs(aspectRatio - expectedRatio) < 0.02f;
            
            // Also check for common 2x6 pixel dimensions at various DPI
            bool dimensionsMatch = 
                (width >= 590 && width <= 610 && height >= 1790 && height <= 1810) || // 300 DPI
                (width >= 295 && width <= 305 && height >= 895 && height <= 905) ||   // 150 DPI
                (width >= 196 && width <= 204 && height >= 596 && height <= 604);     // 100 DPI
            
            Log.Debug($"Is2x6Template: Width={width}, Height={height}, Ratio={aspectRatio:F3}, RatioMatches={ratioMatches}, DimensionsMatch={dimensionsMatch}");
            
            return ratioMatches || dimensionsMatch;
        }
        
        private System.Drawing.Bitmap Create4x6From2x6(System.Drawing.Bitmap source2x6)
        {
            try
            {
                // Check if we should keep portrait orientation for strip printers
                bool keepPortraitForStrips = Properties.Settings.Default.AutoRoutePrinter && 
                                           !string.IsNullOrEmpty(Properties.Settings.Default.Printer2x6Name);
                
                if (keepPortraitForStrips)
                {
                    // Keep portrait orientation: place two 2x6 strips side by side vertically (still 2" wide x 6" tall, but with both strips)
                    // This creates a 4x6 in portrait orientation (1200x1800 at 300 DPI)
                    int outputWidth = source2x6.Width * 2;  // Double the width (2" becomes 4")
                    int outputHeight = source2x6.Height;    // Keep same height (6")
                    
                    Log.Debug($"Create4x6From2x6: Creating portrait {outputWidth}x{outputHeight} from {source2x6.Width}x{source2x6.Height}");
                    
                    var output4x6 = new System.Drawing.Bitmap(outputWidth, outputHeight);
                    output4x6.SetResolution(source2x6.HorizontalResolution, source2x6.VerticalResolution);
                    
                    using (var graphics = Graphics.FromImage(output4x6))
                    {
                        // Set high quality rendering
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        
                        // Fill background (usually white for photo prints)
                        graphics.Clear(System.Drawing.Color.White);
                        
                        // Place two strips side by side (no rotation needed)
                        // First strip on the left
                        graphics.DrawImage(source2x6, 0, 0, source2x6.Width, source2x6.Height);
                        
                        // Second strip on the right
                        graphics.DrawImage(source2x6, source2x6.Width, 0, source2x6.Width, source2x6.Height);
                        
                        Log.Debug($"Create4x6From2x6: Successfully created 4x6 portrait composite with duplicated 2x6 strips");
                    }
                    
                    return output4x6;
                }
                else
                {
                    // Original landscape orientation for standard 4x6 printers
                    // Rotate and place side by side horizontally (creates 1800x1200 at 300 DPI)
                    int outputWidth = source2x6.Height;  // 6 inches becomes width
                    int outputHeight = source2x6.Width * 2; // 2 inches doubled to 4 inches
                    
                    Log.Debug($"Create4x6From2x6: Creating landscape {outputWidth}x{outputHeight} from {source2x6.Width}x{source2x6.Height}");
                    
                    var output4x6 = new System.Drawing.Bitmap(outputWidth, outputHeight);
                    output4x6.SetResolution(source2x6.HorizontalResolution, source2x6.VerticalResolution);
                    
                    using (var graphics = Graphics.FromImage(output4x6))
                    {
                        // Set high quality rendering
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        
                        // Fill background (usually white for photo prints)
                        graphics.Clear(System.Drawing.Color.White);
                        
                        // Rotate 90 degrees and place side by side
                        // First strip (top half) - rotate 90 degrees clockwise
                        graphics.TranslateTransform(0, source2x6.Width);
                        graphics.RotateTransform(-90);
                        graphics.DrawImage(source2x6, 0, 0, source2x6.Width, source2x6.Height);
                        graphics.ResetTransform();
                        
                        // Second strip (bottom half) - rotate 90 degrees clockwise
                        graphics.TranslateTransform(0, source2x6.Width * 2);
                        graphics.RotateTransform(-90);
                        graphics.DrawImage(source2x6, 0, 0, source2x6.Width, source2x6.Height);
                        graphics.ResetTransform();
                        
                        Log.Debug($"Create4x6From2x6: Successfully created 4x6 landscape composite with duplicated 2x6 strips");
                    }
                    
                    return output4x6;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Create4x6From2x6: Failed to create 4x6 from 2x6: {ex.Message}");
                // Return original if conversion fails
                return source2x6;
            }
        }
        
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("HomeButton_Click: User clicked home, navigating back");
            
            // Close any open overlays
            CloseAllOverlays();
            
            // Navigate back to home/event selection
            var parentWindow = Window.GetWindow(this);
            if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
            {
                surfaceWindow.NavigateBack();
            }
            else if (parentWindow is PhotoBoothWindow photoBoothWindow)
            {
                photoBoothWindow.frame.Navigate(new Uri("Pages/MainPage.xaml", UriKind.Relative));
            }
            else if (parentWindow != null)
            {
                // Standalone full screen window - reopen the main surface window and close this one
                var mainWindow = new SurfacePhotoBoothWindow();
                mainWindow.Show();
                parentWindow.Close();
            }
        }
        
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("ExitButton_Click: User clicked exit, performing cleanup");
            
            // Close any open overlays
            CloseAllOverlays();
            
            // Stop any ongoing operations
            StopPhotoSequence();
            
            // Force camera cleanup
            if (DeviceManager?.SelectedCameraDevice != null)
            {
                try
                {
                    DeviceManager.SelectedCameraDevice.StopLiveView();
                }
                catch { }
                
                // Force reset IsBusy
                DeviceManager.SelectedCameraDevice.IsBusy = false;
                Log.Debug("ExitButton_Click: Force reset camera IsBusy to false");
            }
            
            // Reset all session state
            currentEvent = null;
            currentTemplate = null;
            currentPhotoIndex = 0;
            totalPhotosNeeded = 0;
            capturedPhotoPaths.Clear();
            availableEvents = null;
            availableTemplates = null;
            
            // Clear static references
            PhotoboothService.CurrentEvent = null;
            PhotoboothService.CurrentTemplate = null;
            
            Log.Debug("ExitButton_Click: Navigating back to home page");
            
            // Navigate back to home page instead of closing
            var parentWindow = Window.GetWindow(this);
            if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
            {
                surfaceWindow.NavigateBack();
            }
            else if (parentWindow is PhotoBoothWindow photoBoothWindow)
            {
                // If it's the regular PhotoBoothWindow, navigate to main page
                photoBoothWindow.frame.Navigate(new Uri("Pages/MainPage.xaml", UriKind.Relative));
            }
            else if (parentWindow != null)
            {
                // Standalone full screen window - reopen the main surface window and close this one
                var mainWindow = new SurfacePhotoBoothWindow();
                mainWindow.Show();
                parentWindow.Close();
            }
        }
        
        private async Task ResetCameraForNextCapture()
        {
            if (DeviceManager.SelectedCameraDevice == null)
                return;
                
            try
            {
                Log.Debug("ResetCameraForNextCapture: Starting camera reset");
                
                // Force stop any existing live view
                try
                {
                    DeviceManager.SelectedCameraDevice.StopLiveView();
                    await Task.Delay(500); // Wait for stop to complete
                    Log.Debug("ResetCameraForNextCapture: StopLiveView completed");
                }
                catch (Exception ex)
                {
                    Log.Debug($"ResetCameraForNextCapture: StopLiveView failed (may not be running): {ex.Message}");
                }
                
                // Check if camera is stuck in busy state (often happens after settings changes)
                if (DeviceManager.SelectedCameraDevice.IsBusy)
                {
                    Log.Debug("ResetCameraForNextCapture: Camera is busy, attempting full reconnection");
                    
                    // Try to reconnect camera to clear busy state
                    await ReconnectCamera();
                }
                
                // Ensure camera is not busy and shutter is released
                int resetRetries = 0;
                while (DeviceManager.SelectedCameraDevice?.IsBusy == true && resetRetries < 10)
                {
                    Log.Debug($"ResetCameraForNextCapture: Camera still busy, waiting... retry {resetRetries}");
                    await Task.Delay(500);
                    resetRetries++;
                    
                    // If still stuck after 3 retries, try reconnection
                    if (resetRetries == 3)
                    {
                        Log.Debug("ResetCameraForNextCapture: Attempting reconnection due to persistent busy state");
                        await ReconnectCamera();
                    }
                }
                
                // Force clear busy state if still stuck
                if (DeviceManager.SelectedCameraDevice?.IsBusy == true)
                {
                    Log.Debug("ResetCameraForNextCapture: Force clearing busy state");
                    DeviceManager.SelectedCameraDevice.IsBusy = false;
                }
                
                // Additional delay to ensure shutter mechanism is fully reset
                await Task.Delay(500);
                
                Log.Debug("ResetCameraForNextCapture: Camera reset completed");
            }
            catch (Exception ex)
            {
                Log.Error("ResetCameraForNextCapture: Error during camera reset", ex);
                
                // As last resort, try reconnection
                try
                {
                    await ReconnectCamera();
                }
                catch (Exception reconnectEx)
                {
                    Log.Error("ResetCameraForNextCapture: Reconnection also failed", reconnectEx);
                }
            }
        }
        
        private async Task ReconnectCamera()
        {
            try
            {
                Log.Debug("ReconnectCamera: Starting camera reconnection");
                
                var currentDevice = DeviceManager.SelectedCameraDevice;
                if (currentDevice != null)
                {
                    // Disconnect current camera
                    try
                    {
                        currentDevice.StopLiveView();
                        await Task.Delay(300);
                        currentDevice.Close();
                        await Task.Delay(500);
                        Log.Debug("ReconnectCamera: Camera disconnected");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"ReconnectCamera: Disconnect failed: {ex.Message}");
                    }
                }
                
                // Reconnect using the same method as initial connection
                DeviceManager.ConnectToCamera();
                await Task.Delay(1000); // Wait for connection to establish
                
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    DeviceManager.SelectedCameraDevice.CaptureInSdRam = true;
                    Log.Debug("ReconnectCamera: Camera reconnected successfully");
                    
                    // Update UI
                    Dispatcher.Invoke(() =>
                    {
                        cameraStatusText.Text = $"Reconnected: {DeviceManager.SelectedCameraDevice.DeviceName}";
                    });
                }
                else
                {
                    Log.Error("ReconnectCamera: Failed to reconnect camera");
                }
            }
            catch (Exception ex)
            {
                Log.Error("ReconnectCamera: Error during camera reconnection", ex);
            }
        }
        
        // Camera and Event Controls
        private async void ResetCameraButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                statusText.Text = "Resetting camera connection...";
                resetCameraButton.IsEnabled = false;
                
                await ReconnectCamera();
                
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    RefreshDisplay();
                    statusText.Text = "Camera reset successfully!";
                }
                else
                {
                    statusText.Text = "Camera reset failed - please check connection";
                }
                
                // Re-enable button after a delay
                await Task.Delay(2000);
                statusText.Text = "Touch START to begin";
            }
            catch (Exception ex)
            {
                Log.Error("Manual camera reset failed", ex);
                statusText.Text = "Camera reset failed - see logs for details";
            }
            finally
            {
                resetCameraButton.IsEnabled = true;
            }
        }
        
        // Event Selection Overlay Methods
        private void EventSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if locked and PIN is enabled
            if (Properties.Settings.Default.EnableLockFeature && _isLocked)
            {
                Log.Debug("EventSettingsButton_Click: Interface is locked, showing PIN entry");
                _pendingActionAfterUnlock = () => 
                {
                    // Clear current event to allow selecting a new one
                    currentEvent = null;
                    currentTemplate = null;
                    currentPhotoIndex = 0;
                    totalPhotosNeeded = 0;
                    capturedPhotoPaths.Clear();
                    
                    ShowEventSelectionOverlay();
                };
                ShowPinEntryDialog();
                return;
            }
            
            // Clear current event to allow selecting a new one
            currentEvent = null;
            currentTemplate = null;
            currentPhotoIndex = 0;
            totalPhotosNeeded = 0;
            capturedPhotoPaths.Clear();
            
            ShowEventSelectionOverlay();
        }
        
        private void ModernSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if locked and PIN is enabled
            if (Properties.Settings.Default.EnableLockFeature && _isLocked)
            {
                Log.Debug("ModernSettingsButton_Click: Interface is locked, showing PIN entry");
                _pendingActionAfterUnlock = () => 
                {
                    Log.Debug("ModernSettingsButton_Click: Opening modern settings overlay in fullscreen");
                    modernSettingsOverlay.Visibility = Visibility.Visible;
                };
                ShowPinEntryDialog();
                return;
            }
            
            Log.Debug("ModernSettingsButton_Click: Opening modern settings overlay in fullscreen");
            
            // Show the modern settings overlay in fullscreen
            modernSettingsOverlay.Visibility = Visibility.Visible;
        }
        
        private void CloseModernSettings_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("CloseModernSettings_Click: Closing modern settings overlay");
            modernSettingsOverlay.Visibility = Visibility.Collapsed;
        }
        
        private void ShowEventSelectionOverlay()
        {
            LoadAvailableEvents();
            eventSelectionOverlay.Visibility = Visibility.Visible;
            isSelectingTemplateForSession = false;
            
            // Reset the title back to event selection
            if (overlayTitleText != null)
            {
                overlayTitleText.Text = "Select Event";
            }
            
            // Make sure events list is visible
            eventsListControl.Visibility = Visibility.Visible;
        }
        
        private void ShowEventTemplateSelection()
        {
            // Used when returning to selection after a session completes
            ShowEventSelectionOverlay();
        }
        
        private void ShowTemplateSelectionForSession()
        {
            // This is specifically for selecting a template during a photobooth session
            // when the event has multiple templates
            
            if (currentEvent == null) return;
            
            // Load templates for the current event
            availableTemplates = eventService.GetEventTemplates(currentEvent.Id);
            
            // Update UI to show only templates (hide events)
            eventsListControl.Visibility = Visibility.Collapsed;
            templatesListControl.ItemsSource = availableTemplates;
            templatesListControl.Visibility = Visibility.Visible;
            
            // Update the title to show template selection
            if (overlayTitleText != null)
            {
                overlayTitleText.Text = $"Select Template for {currentEvent.Name}";
            }
            
            // Update cancel button text for template selection
            cancelSelectionButton.Content = "✕ Back";
            
            // Show the overlay with templates only
            eventSelectionOverlay.Visibility = Visibility.Visible;
            
            // Set flag to know we're in template selection mode
            isSelectingTemplateForSession = true;
        }
        
        private void LoadAvailableEvents()
        {
            try
            {
                availableEvents = eventService.GetAllEvents();
                eventsListControl.ItemsSource = availableEvents;
                
                // Clear template selection when events change
                availableTemplates = new List<TemplateData>();
                templatesListControl.ItemsSource = availableTemplates;
                selectedEventForOverlay = null;
                selectedTemplateForOverlay = null;
                UpdateConfirmButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load events", ex);
                statusText.Text = "Error loading events";
            }
        }
        
        private void SelectEvent(EventData eventData)
        {
            selectedEventForOverlay = eventData;
            currentEvent = eventData;
            
            // Load templates for selected event
            LoadAvailableTemplates(eventData.Id);
            
            // Go straight to photobooth screen with this event
            // Templates will be selected on the photobooth screen itself
            eventSelectionOverlay.Visibility = Visibility.Collapsed;
            
            if (availableTemplates != null && availableTemplates.Count > 0)
            {
                if (availableTemplates.Count == 1)
                {
                    // Only one template - auto-select it
                    currentTemplate = availableTemplates[0];
                    totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                    currentPhotoIndex = 0;
                    
                    // Show start button when template is auto-selected
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Visible;
                    UpdatePhotoStripPlaceholders();
                    statusText.Text = $"Event: {currentEvent.Name} - Ready to start";
                    Log.Debug($"Auto-selected single template: {currentTemplate.Name} for event: {currentEvent.Name}");
                }
                else
                {
                    // Multiple templates - will show template selection on photobooth screen
                    statusText.Text = $"Event: {currentEvent.Name} - Touch START to select template";
                    Log.Debug($"Event has {availableTemplates.Count} templates - will show selection on START");
                }
            }
            else
            {
                // No templates available
                statusText.Text = "No templates available for this event";
            }
        }
        
        private void LoadAvailableTemplates(int eventId)
        {
            try
            {
                availableTemplates = eventService.GetEventTemplates(eventId);
                templatesListControl.ItemsSource = availableTemplates;
                
                // Clear template selection
                selectedTemplateForOverlay = null;
                UpdateConfirmButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load templates", ex);
                statusText.Text = "Error loading templates";
            }
        }
        
        private void SelectTemplate(TemplateData templateData)
        {
            selectedTemplateForOverlay = templateData;
            
            // Update UI to show selection
            HighlightSelectedTemplate(templateData);
            UpdateConfirmButtonState();
        }
        
        private void HighlightSelectedEvent(EventData eventData)
        {
            // Update visual selection - could add selected state styling
            // For now, this is a placeholder for visual feedback
        }
        
        private void HighlightSelectedTemplate(TemplateData templateData)
        {
            // Update visual selection - could add selected state styling
            // For now, this is a placeholder for visual feedback
        }
        
        private void UpdateConfirmButtonState()
        {
            // This method is no longer needed since we removed the confirm button
            // Keeping it empty for backward compatibility
        }
        
        private void ConfirmSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (isSelectingTemplateForSession)
            {
                // Template-only selection for session
                if (selectedTemplateForOverlay != null)
                {
                    currentTemplate = selectedTemplateForOverlay;
                    
                    // Get photo count from template
                    totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                    currentPhotoIndex = 0;
                    UpdatePhotoStripPlaceholders();
                    
                    // Hide overlay
                    eventSelectionOverlay.Visibility = Visibility.Collapsed;
                    
                    // Show start button when template is selected
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Visible;
                    
                    // Restore events list visibility for next time
                    eventsListControl.Visibility = Visibility.Visible;
                    
                    // Clear flags and selections
                    isSelectingTemplateForSession = false;
                    selectedTemplateForOverlay = null;
                    
                    statusText.Text = $"Template: {currentTemplate.Name} selected - Starting capture...";
                    
                    // Start the photo sequence
                    StartPhotoSequence();
                }
            }
            else if (selectedEventForOverlay != null && selectedTemplateForOverlay != null)
            {
                // Full event and template selection
                currentEvent = selectedEventForOverlay;
                currentTemplate = selectedTemplateForOverlay;
                
                // Don't create database session yet - wait until first photo is captured
                // CreateDatabaseSession(); // Moved to first photo capture
                
                // Get photo count from template
                totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                currentPhotoIndex = 0;
                UpdatePhotoStripPlaceholders();
                
                // Update folder path to include event name
                string eventFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                    "Photobooth", SanitizeFileName(currentEvent.Name));
                if (!Directory.Exists(eventFolder))
                {
                    Directory.CreateDirectory(eventFolder);
                }
                FolderForPhotos = eventFolder;
                
                // Update UI
                RefreshDisplay();
                
                // Hide overlay
                eventSelectionOverlay.Visibility = Visibility.Collapsed;
                
                // Show start button when event and template are selected
                if (startButtonOverlay != null)
                    startButtonOverlay.Visibility = Visibility.Visible;
                
                statusText.Text = $"Event: {currentEvent.Name} - Template: {currentTemplate.Name} selected";
                
                Log.Debug($"PhotoboothTouch: Selected event '{currentEvent.Name}' with template '{currentTemplate.Name}' requiring {totalPhotosNeeded} photos");
            }
        }
        
        private void CancelSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (isSelectingTemplateForSession)
            {
                // We're in template selection mode - just hide the overlay
                eventSelectionOverlay.Visibility = Visibility.Collapsed;
                isSelectingTemplateForSession = false;
                
                // Restore UI for next time
                eventsListControl.Visibility = Visibility.Visible;
                // confirmSelectionButton.Visibility = Visibility.Visible; // Button removed from UI
                cancelSelectionButton.Content = "✕ Close";
            }
            else
            {
                // Regular event selection cancel
                eventSelectionOverlay.Visibility = Visibility.Collapsed;
            }
            
            // Clear temporary selections
            selectedEventForOverlay = null;
            selectedTemplateForOverlay = null;
        }
        
        // Handle event selection from overlay
        private void OnEventSelected(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var eventData = border?.Tag as EventData;
            if (eventData != null)
            {
                SelectEvent(eventData);
            }
        }
        
        // Handle template selection from overlay  
        private void OnTemplateSelected(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var templateData = border?.Tag as TemplateData;
            if (templateData != null)
            {
                // When in template selection mode for a session
                if (isSelectingTemplateForSession)
                {
                    // Direct selection - start immediately
                    currentTemplate = templateData;
                    
                    // Get photo count from template
                    totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                    currentPhotoIndex = 0;
                    UpdatePhotoStripPlaceholders();
                    
                    // Hide overlay
                    eventSelectionOverlay.Visibility = Visibility.Collapsed;
                    
                    // Show start button when template is selected
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Visible;
                    
                    // Restore UI for next time
                    eventsListControl.Visibility = Visibility.Visible;
                    // confirmSelectionButton.Visibility = Visibility.Visible; // Button removed from UI
                    cancelSelectionButton.Content = "✕ Close";
                    
                    // Clear flags
                    isSelectingTemplateForSession = false;
                    selectedTemplateForOverlay = null;
                    
                    statusText.Text = $"Template: {currentTemplate.Name} selected - Starting capture...";
                    
                    // Start the photo sequence immediately
                    StartPhotoSequence();
                }
                else
                {
                    // Normal selection mode - just select the template
                    SelectTemplate(templateData);
                }
            }
        }
        
        #region Camera Settings Modal
        
        private bool _isLoadingSettings = false; // Flag to prevent infinite recursion
        
        
        private void CameraSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if locked and PIN is enabled
            if (Properties.Settings.Default.EnableLockFeature && _isLocked)
            {
                Log.Debug("CameraSettingsButton_Click: Interface is locked, showing PIN entry");
                _pendingActionAfterUnlock = () => 
                {
                    Log.Debug("CameraSettingsButton_Click: Opening camera settings modal");
                    
                    // Load current camera settings
                    LoadCameraSettings();
                    
                    // Show the overlay
                    cameraSettingsOverlay.Visibility = Visibility.Visible;
                };
                ShowPinEntryDialog();
                return;
            }
            
            Log.Debug("CameraSettingsButton_Click: Opening camera settings modal");
            
            // Load current camera settings
            LoadCameraSettings();
            
            // Show the overlay
            cameraSettingsOverlay.Visibility = Visibility.Visible;
        }
        
        private void CloseCameraSettings_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("CloseCameraSettings_Click: Closing camera settings modal");
            cameraSettingsOverlay.Visibility = Visibility.Collapsed;
        }
        
        private void LoadCameraSettings()
        {
            try
            {
                _isLoadingSettings = true; // Set flag to prevent recursion
                
                // Load available cameras
                cameraComboBox.Items.Clear();
                foreach (var camera in DeviceManager.ConnectedDevices)
                {
                    cameraComboBox.Items.Add(camera.DeviceName);
                }
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    cameraComboBox.SelectedItem = DeviceManager.SelectedCameraDevice.DeviceName;
                }
                
                // Load ISO values
                isoComboBox.Items.Clear();
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    var isoValues = DeviceManager.SelectedCameraDevice.IsoNumber?.Values;
                    if (isoValues != null)
                    {
                        foreach (var iso in isoValues)
                        {
                            isoComboBox.Items.Add(iso);
                        }
                        isoComboBox.SelectedItem = DeviceManager.SelectedCameraDevice.IsoNumber?.Value;
                    }
                }
                
                // Load Aperture values
                apertureComboBox.Items.Clear();
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    var apertureValues = DeviceManager.SelectedCameraDevice.FNumber?.Values;
                    if (apertureValues != null)
                    {
                        foreach (var aperture in apertureValues)
                        {
                            apertureComboBox.Items.Add(aperture);
                        }
                        apertureComboBox.SelectedItem = DeviceManager.SelectedCameraDevice.FNumber?.Value;
                    }
                }
                
                // Load Shutter Speed values
                shutterSpeedComboBox.Items.Clear();
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    var shutterValues = DeviceManager.SelectedCameraDevice.ShutterSpeed?.Values;
                    if (shutterValues != null)
                    {
                        foreach (var shutter in shutterValues)
                        {
                            shutterSpeedComboBox.Items.Add(shutter);
                        }
                        shutterSpeedComboBox.SelectedItem = DeviceManager.SelectedCameraDevice.ShutterSpeed?.Value;
                    }
                }
                
                // Load White Balance values
                whiteBalanceComboBox.Items.Clear();
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    var wbValues = DeviceManager.SelectedCameraDevice.WhiteBalance?.Values;
                    if (wbValues != null)
                    {
                        foreach (var wb in wbValues)
                        {
                            whiteBalanceComboBox.Items.Add(wb);
                        }
                        whiteBalanceComboBox.SelectedItem = DeviceManager.SelectedCameraDevice.WhiteBalance?.Value;
                    }
                }
                
                // Load capture mode
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    captureInSdRamCheckBox.IsChecked = DeviceManager.SelectedCameraDevice.CaptureInSdRam;
                }
            }
            catch (Exception ex)
            {
                Log.Error("LoadCameraSettings: Failed to load camera settings", ex);
            }
            finally
            {
                _isLoadingSettings = false; // Clear flag after loading
            }
        }
        
        private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're loading settings to prevent infinite recursion
            if (_isLoadingSettings) return;
            
            try
            {
                if (cameraComboBox.SelectedIndex >= 0 && cameraComboBox.SelectedIndex < DeviceManager.ConnectedDevices.Count)
                {
                    DeviceManager.SelectedCameraDevice = DeviceManager.ConnectedDevices[cameraComboBox.SelectedIndex];
                    LoadCameraSettings(); // Reload settings for new camera
                }
            }
            catch (Exception ex)
            {
                Log.Error("CameraComboBox_SelectionChanged: Failed to change camera", ex);
            }
        }
        
        private void IsoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're loading settings
            if (_isLoadingSettings) return;
            
            try
            {
                if (DeviceManager.SelectedCameraDevice?.IsoNumber != null && isoComboBox.SelectedItem != null)
                {
                    DeviceManager.SelectedCameraDevice.IsoNumber.SetValue(isoComboBox.SelectedItem.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.Error("IsoComboBox_SelectionChanged: Failed to set ISO", ex);
            }
        }
        
        private void ApertureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're loading settings
            if (_isLoadingSettings) return;
            
            try
            {
                if (DeviceManager.SelectedCameraDevice?.FNumber != null && apertureComboBox.SelectedItem != null)
                {
                    DeviceManager.SelectedCameraDevice.FNumber.SetValue(apertureComboBox.SelectedItem.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.Error("ApertureComboBox_SelectionChanged: Failed to set aperture", ex);
            }
        }
        
        private void ShutterSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're loading settings
            if (_isLoadingSettings) return;
            
            try
            {
                if (DeviceManager.SelectedCameraDevice?.ShutterSpeed != null && shutterSpeedComboBox.SelectedItem != null)
                {
                    DeviceManager.SelectedCameraDevice.ShutterSpeed.SetValue(shutterSpeedComboBox.SelectedItem.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.Error("ShutterSpeedComboBox_SelectionChanged: Failed to set shutter speed", ex);
            }
        }
        
        private void WhiteBalanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if we're loading settings
            if (_isLoadingSettings) return;
            
            try
            {
                if (DeviceManager.SelectedCameraDevice?.WhiteBalance != null && whiteBalanceComboBox.SelectedItem != null)
                {
                    DeviceManager.SelectedCameraDevice.WhiteBalance.SetValue(whiteBalanceComboBox.SelectedItem.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.Error("WhiteBalanceComboBox_SelectionChanged: Failed to set white balance", ex);
            }
        }
        
        private void CaptureInSdRamCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    DeviceManager.SelectedCameraDevice.CaptureInSdRam = captureInSdRamCheckBox.IsChecked ?? false;
                }
            }
            catch (Exception ex)
            {
                Log.Error("CaptureInSdRamCheckBox_Changed: Failed to set capture mode", ex);
            }
        }
        
        private bool _isCapturingTestPhoto = false;
        private string _lastTestPhotoPath = null;
        
        private void TestCaptureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isCapturingTestPhoto)
                {
                    Log.Debug("TestCaptureButton_Click: Already capturing, ignoring");
                    return;
                }
                
                Log.Debug("TestCaptureButton_Click: Taking test photo");
                
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    _isCapturingTestPhoto = true;
                    
                    // Show progress indicator
                    testPhotoProgressBar.Visibility = Visibility.Visible;
                    testPhotoPlaceholderText.Text = "Capturing...";
                    testPhotoImage.Visibility = Visibility.Collapsed;
                    
                    // Subscribe to photo captured event temporarily
                    PhotoCapturedEventArgs capturedEventArgs = null;
                    PhotoCapturedEventHandler handler = null;
                    handler = (s, args) =>
                    {
                        capturedEventArgs = args;
                        DeviceManager.PhotoCaptured -= handler;
                        
                        // Handle the captured photo on UI thread
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            HandleTestPhotoCaptured(args);
                        }));
                    };
                    
                    DeviceManager.PhotoCaptured += handler;
                    
                    // Capture the photo
                    DeviceManager.SelectedCameraDevice.CapturePhoto();
                    
                    // Set a timeout to clean up if photo doesn't arrive
                    Task.Delay(10000).ContinueWith(t =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_isCapturingTestPhoto)
                            {
                                _isCapturingTestPhoto = false;
                                testPhotoProgressBar.Visibility = Visibility.Collapsed;
                                testPhotoPlaceholderText.Text = "Test Photo Preview";
                                DeviceManager.PhotoCaptured -= handler;
                            }
                        }));
                    });
                }
                else
                {
                    MessageBox.Show("No camera selected", "Test Capture", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _isCapturingTestPhoto = false;
                testPhotoProgressBar.Visibility = Visibility.Collapsed;
                testPhotoPlaceholderText.Text = "Test Photo Preview";
                
                Log.Error("TestCaptureButton_Click: Failed to capture test photo", ex);
                MessageBox.Show($"Failed to capture test photo: {ex.Message}", "Test Capture", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void HandleTestPhotoCaptured(PhotoCapturedEventArgs eventArgs)
        {
            try
            {
                // Only handle if we're actually capturing a test photo
                if (!_isCapturingTestPhoto) return;
                
                Log.Debug("HandleTestPhotoCaptured: Processing test photo");
                
                // Create a temp file for the test photo
                string tempFolder = Path.Combine(Path.GetTempPath(), "PhotoboothTest");
                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }
                
                string fileName = Path.Combine(tempFolder, $"test_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                
                // Transfer the file
                eventArgs.CameraDevice.TransferFile(eventArgs.Handle, fileName);
                
                // Release camera resource
                try
                {
                    eventArgs.CameraDevice.ReleaseResurce(eventArgs.Handle);
                }
                catch { }
                
                // Clear busy flag
                eventArgs.CameraDevice.IsBusy = false;
                
                // Display the photo if it was saved successfully
                if (File.Exists(fileName))
                {
                    Log.Debug($"HandleTestPhotoCaptured: Test photo saved to {fileName}");
                    _lastTestPhotoPath = fileName;
                    
                    // Load and display the image
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(fileName);
                    bitmap.EndInit();
                    
                    testPhotoImage.Source = bitmap;
                    testPhotoImage.Visibility = Visibility.Visible;
                    testPhotoPlaceholderText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    Log.Error($"HandleTestPhotoCaptured: File was not created: {fileName}");
                    testPhotoPlaceholderText.Text = "Failed to save photo";
                }
            }
            catch (Exception ex)
            {
                Log.Error("HandleTestPhotoCaptured: Failed to process test photo", ex);
                testPhotoPlaceholderText.Text = "Error processing photo";
            }
            finally
            {
                _isCapturingTestPhoto = false;
                testPhotoProgressBar.Visibility = Visibility.Collapsed;
            }
        }
        
        #endregion
        
        #region Retake Functionality
        
        private void ShowRetakeReview()
        {
            // Check if retake is enabled in settings
            if (!Properties.Settings.Default.EnableRetake)
            {
                // Check if we should show filter selection without retake
                // Filters are now handled in post-session overlay, skip the old filter review
                if (Properties.Settings.Default.EnableFilters)
                {
                    // Apply default filter without UI
                    if (Properties.Settings.Default.DefaultFilter > 0)
                    {
                        FilterType defaultFilter = (FilterType)Properties.Settings.Default.DefaultFilter;
                        
                        // Initialize the filter control even though it won't be shown
                        if (filterSelectionControl != null)
                        {
                            filterSelectionControl.SetSelectedFilter(defaultFilter);
                        }
                        
                        Log.Debug($"ShowRetakeReview: Retake disabled, set default filter: {defaultFilter}");
                    }
                    else
                    {
                        Log.Debug("ShowRetakeReview: Retake disabled, filters enabled but no default filter set");
                    }
                }
                else
                {
                    Log.Debug("ShowRetakeReview: Retake disabled, filters disabled");
                }
                
                // Skip directly to processing
                ProcessTemplateWithPhotos();
                return;
            }
            
            Log.Debug("ShowRetakeReview: Showing retake review overlay");
            
            // Populate retake photos collection
            retakePhotos.Clear();
            for (int i = 0; i < capturedPhotoPaths.Count; i++)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(capturedPhotoPaths[i]);
                bitmap.EndInit();
                bitmap.Freeze();
                
                retakePhotos.Add(new RetakePhotoItem
                {
                    Image = bitmap,
                    Label = $"Photo {i + 1}",
                    PhotoIndex = i,
                    FilePath = capturedPhotoPaths[i]
                });
            }
            
            // Filter selection is now handled in post-session overlay - always keep old controls hidden
            filterSectionContainer.Visibility = Visibility.Collapsed;
            if (enableFiltersCheckBox != null)
                enableFiltersCheckBox.Visibility = Visibility.Collapsed;
            if (filterSelectionControl != null)
                filterSelectionControl.Visibility = Visibility.Collapsed;
            
            // Set up and start timer
            retakeTimeRemaining = Properties.Settings.Default.RetakeTimeout;
            retakeTimerText.Text = retakeTimeRemaining.ToString();
            retakeReviewTimer.Start();
            
            // Show the overlay
            retakeReviewOverlay.Visibility = Visibility.Visible;
        }
        
        private void RetakeReviewTimer_Tick(object sender, EventArgs e)
        {
            retakeTimeRemaining--;
            retakeTimerText.Text = retakeTimeRemaining.ToString();
            
            if (retakeTimeRemaining <= 0)
            {
                // Time's up - proceed with template
                retakeReviewTimer.Stop();
                ContinueFromRetake_Click(null, null);
            }
        }
        
        private async void RetakePhoto_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border?.Tag is int photoIndex)
            {
                Log.Debug($"RetakePhoto_Click: Retaking photo {photoIndex + 1}");
                
                // Stop the review timer
                retakeReviewTimer.Stop();
                
                // Hide the overlay
                retakeReviewOverlay.Visibility = Visibility.Collapsed;
                
                // Set up for retake
                photoIndexToRetake = photoIndex;
                isRetakingPhoto = true;
                
                // Update UI
                statusText.Text = $"Retaking photo {photoIndex + 1} of {totalPhotosNeeded}";
                
                // Prepare camera and start countdown
                await PrepareForRetake();
            }
        }
        
        private async Task PrepareForRetake()
        {
            try
            {
                Log.Debug("PrepareForRetake: Preparing camera for retake");
                
                // Set capture state
                isCapturing = true;
                
                // Hide start button, show stop button for retake
                if (startButtonOverlay != null)
                    startButtonOverlay.Visibility = Visibility.Collapsed;
                if (stopSessionButton != null)
                    stopSessionButton.Visibility = Visibility.Visible;
                
                statusText.Text = "Preparing camera for retake...";
                
                // Ensure camera is ready
                await Task.Run(() =>
                {
                    try
                    {
                        Log.Debug($"PrepareForRetake: Camera initial state - IsBusy: {DeviceManager.SelectedCameraDevice?.IsBusy}");
                        
                        // Force reset busy flag if needed
                        if (DeviceManager.SelectedCameraDevice.IsBusy)
                        {
                            Log.Debug("PrepareForRetake: Camera is busy, forcing reset");
                            DeviceManager.SelectedCameraDevice.IsBusy = false;
                            Thread.Sleep(500); // Give it time to reset
                        }
                        
                        // Stop and restart live view to ensure clean state
                        try
                        {
                            Log.Debug("PrepareForRetake: Stopping live view");
                            DeviceManager.SelectedCameraDevice.StopLiveView();
                            Thread.Sleep(500); // Let it fully stop
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"PrepareForRetake: StopLiveView failed: {ex.Message}");
                        }
                        
                        // Start fresh live view
                        Log.Debug("PrepareForRetake: Starting fresh live view");
                        DeviceManager.SelectedCameraDevice.StartLiveView();
                        Log.Debug("PrepareForRetake: Live view started successfully");
                    }
                    catch (Exception ex)
                    {
                        Log.Error("PrepareForRetake: Failed to prepare camera", ex);
                        throw;
                    }
                });
                
                // Start live view timer
                liveViewTimer.Start();
                statusText.Text = $"Retaking photo {photoIndexToRetake + 1} - Starting countdown...";
                
                // Wait for live view to stabilize
                await Task.Delay(1000);
                
                // Start countdown
                StartCountdown();
            }
            catch (Exception ex)
            {
                Log.Error("PrepareForRetake: Failed to prepare for retake", ex);
                
                // Reset retake state
                isRetakingPhoto = false;
                photoIndexToRetake = -1;
                
                // Show error and return to review
                statusText.Text = "Camera error - Unable to retake photo";
                await Task.Delay(2000);
                
                // Show review again
                ShowRetakeReview();
            }
        }
        
        private void SkipRetakeReview_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("SkipRetakeReview_Click: Skipping retake review");
            
            // Stop timer
            retakeReviewTimer.Stop();
            
            // Hide overlay
            retakeReviewOverlay.Visibility = Visibility.Collapsed;
            
            // Process template
            ProcessTemplateWithPhotos();
        }
        
        private void ShowFilterOnlyReview()
        {
            Log.Debug("ShowFilterOnlyReview: Showing filter selection without retake options");
            
            // Clear retake photos collection to hide the grid
            retakePhotos.Clear();
            
            // Show filter section
            filterSectionContainer.Visibility = Visibility.Visible;
            enableFiltersCheckBox.Visibility = Visibility.Collapsed; // Hide the checkbox since filters are already enabled
            filterSelectionControl.Visibility = Visibility.Visible;
            
            // Load the first photo for preview
            if (capturedPhotoPaths.Count > 0)
            {
                Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await filterSelectionControl.SetSourceImage(capturedPhotoPaths[0]);
                    });
                });
            }
            
            // Set a shorter timer for filter selection (10 seconds)
            retakeTimeRemaining = 10;
            retakeTimerText.Text = retakeTimeRemaining.ToString();
            retakeReviewTimer.Start();
            
            // Show the overlay
            retakeReviewOverlay.Visibility = Visibility.Visible;
        }
        
        private void ContinueFromRetake_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("ContinueFromRetake_Click: Continuing with current photos");
            
            // Stop timer
            retakeReviewTimer.Stop();
            
            // Hide overlay
            retakeReviewOverlay.Visibility = Visibility.Collapsed;
            
            // Process template
            ProcessTemplateWithPhotos();
        }
        
        private void ProcessTemplateWithPhotos()
        {
            statusText.Text = $"Processing template with {capturedPhotoPaths.Count} photos...";
            
            // Check if filters are enabled and we should show the filter selection
            // For now, always show filter selection when filters are enabled
            if (Properties.Settings.Default.EnableFilters)
            {
                Log.Debug("ProcessTemplateWithPhotos: Filters enabled with selection UI - showing filter overlay");
                // Show filter selection overlay on UI thread
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowPostSessionFilterOverlay();
                }));
                return; // The filter selection will call ProcessTemplateWithPhotosInternal when done
            }
            
            Task.Run(async () =>
            {
                try
                {
                    Log.Debug($"ProcessTemplateWithPhotos: Processing {capturedPhotoPaths.Count} photos into template");
                    
                    // Apply filters only if enabled in settings (but no UI selection)
                    if (Properties.Settings.Default.EnableFilters)
                    {
                        FilterType selectedFilter = FilterType.None;
                        
                        // If no filter selected, use default filter from settings
                        if (selectedFilter == FilterType.None && Properties.Settings.Default.DefaultFilter > 0)
                        {
                            selectedFilter = (FilterType)Properties.Settings.Default.DefaultFilter;
                            Log.Debug($"ProcessTemplateWithPhotos: Using default filter from settings: {selectedFilter}");
                        }
                        
                        if (selectedFilter != FilterType.None)
                        {
                            Log.Debug($"Applying {selectedFilter} filter to photos");
                            statusText.Dispatcher.Invoke(() => statusText.Text = "Applying filters...");
                            
                            // Apply filter to each photo
                            List<string> filteredPaths = new List<string>();
                            for (int i = 0; i < capturedPhotoPaths.Count; i++)
                            {
                                string filteredPath = await ApplyFilterToPhoto(capturedPhotoPaths[i], selectedFilter, false);
                                filteredPaths.Add(filteredPath);
                            }
                            
                            // Update paths to use filtered versions
                            capturedPhotoPaths = filteredPaths;
                        }
                    }
                    else
                    {
                        Log.Debug("Filters disabled, processing without filters");
                    }
                    
                    // Process the template with the captured photos
                    string processedImagePath = await ComposeTemplateWithPhotos();
                    
                    Log.Debug($"ProcessTemplateWithPhotos: ComposeTemplateWithPhotos returned: '{processedImagePath}'");
                    Log.Debug($"ProcessTemplateWithPhotos: File exists check: {(processedImagePath != null ? File.Exists(processedImagePath).ToString() : "null path")}");
                    
                    if (!string.IsNullOrEmpty(processedImagePath) && File.Exists(processedImagePath))
                    {
                        Log.Debug($"ProcessTemplateWithPhotos: Template processed successfully: {processedImagePath}");
                        
                        Dispatcher.Invoke(() =>
                        {
                            // Show the processed image (always show the original, not the 4x6 duplicate)
                            liveViewImage.Source = new BitmapImage(new Uri(processedImagePath));
                            statusText.Text = "Photos processed successfully!";
                            
                            // Show print button
                            printButton.Visibility = Visibility.Visible;
                            
                            // Show Done button
                            if (doneButton != null)
                            {
                                doneButton.Visibility = Visibility.Visible;
                            }
                            
                            // Don't show start button yet - wait for Done button or auto-clear
                            
                            // Hide stop button since session is complete
                            if (stopSessionButton != null)
                            {
                                stopSessionButton.Visibility = Visibility.Collapsed;
                            }
                            
                            // Optional: Auto-stop after processing (with delay)
                            Task.Delay(3000).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    StopPhotoSequence();
                                    statusText.Text = "Session complete - Touch START for new session";
                                    
                                    // Show Done button if enabled
                                    if (doneButton != null)
                                    {
                                        doneButton.Visibility = Visibility.Visible;
                                    }
                                    
                                    // Start auto-clear timer if enabled
                                    StartAutoClearTimer();
                                });
                            });
                        });
                    }
                    else
                    {
                        Log.Error("ProcessTemplateWithPhotos: No processed image returned");
                        Dispatcher.Invoke(() =>
                        {
                            statusText.Text = "Failed to process template";
                            StopPhotoSequence();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("ProcessTemplateWithPhotos: Failed to process template", ex);
                    Dispatcher.Invoke(() =>
                    {
                        statusText.Text = $"Template processing error: {ex.Message}";
                        StopPhotoSequence();
                    });
                }
            });
        }
        
        private async void ShowPostSessionFilterOverlay()
        {
            // Prepare filter items with preview from first captured photo
            if (capturedPhotoPaths.Count > 0)
            {
                try
                {
                    Log.Debug($"ShowPostSessionFilterOverlay: Starting filter preview generation with {capturedPhotoPaths.Count} photos");
                    statusText.Text = "Preparing filter options...";
                    
                    // Ensure live view is visible for preview
                    liveViewImage.Visibility = Visibility.Visible;
                    
                    // Show overlay immediately with loading state
                    postSessionFilterOverlay.Visibility = Visibility.Visible;
                    Log.Debug($"ShowPostSessionFilterOverlay: Set overlay to visible, actual visibility: {postSessionFilterOverlay.Visibility}");
                    
                    // Generate filter previews directly on UI thread with proper async handling
                    try
                    {
                        Log.Debug($"ShowPostSessionFilterOverlay: Generating previews for photo: {capturedPhotoPaths[0]}");
                        await GenerateFilterPreviews(capturedPhotoPaths[0]);
                        statusText.Text = "Select a filter to preview";
                        Log.Debug("ShowPostSessionFilterOverlay: Filter previews generated successfully");
                        
                        // Ensure overlay is visible after preview generation
                        Log.Debug($"ShowPostSessionFilterOverlay: Overlay visibility = {postSessionFilterOverlay.Visibility}");
                        if (postSessionFilterOverlay.Visibility != Visibility.Visible)
                        {
                            postSessionFilterOverlay.Visibility = Visibility.Visible;
                            Log.Debug("ShowPostSessionFilterOverlay: Forced overlay to visible");
                        }
                        
                        // Force UI update and check again after small delay
                        await Task.Delay(100);
                        postSessionFilterOverlay.UpdateLayout();
                        Log.Debug($"ShowPostSessionFilterOverlay: After UpdateLayout, visibility = {postSessionFilterOverlay.Visibility}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"ShowPostSessionFilterOverlay: Error generating previews: {ex.Message}", ex);
                        statusText.Text = "Error loading filters - proceeding without filters";
                        postSessionFilterOverlay.Visibility = Visibility.Collapsed;
                        ProcessTemplateWithPhotosInternal();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"ShowPostSessionFilterOverlay: Fatal error: {ex.Message}", ex);
                    statusText.Text = "Error - proceeding without filters";
                    postSessionFilterOverlay.Visibility = Visibility.Collapsed;
                    ProcessTemplateWithPhotosInternal();
                }
            }
            else
            {
                Log.Debug("ShowPostSessionFilterOverlay: No photos to filter");
                // No photos to filter, proceed directly
                ProcessTemplateWithPhotosInternal();
            }
        }
        
        private async Task GenerateFilterPreviews(string samplePhotoPath)
        {
            var filterItems = new System.Collections.ObjectModel.ObservableCollection<Photobooth.Controls.FilterItem>();
            
            // Get enabled filters from settings at method level
            var enabledFilters = GetEnabledFiltersFromSettings();
            Log.Debug($"GenerateFilterPreviews: Found {enabledFilters.Length} enabled filters: {string.Join(", ", enabledFilters)}");
            
            // If no filters are enabled, hide overlay and proceed directly
            if (enabledFilters.Length == 0)
            {
                Log.Debug("GenerateFilterPreviews: No filters enabled, proceeding without filter selection");
                postSessionFilterOverlay.Visibility = Visibility.Collapsed;
                ProcessTemplateWithPhotosInternal();
                return;
            }
            
            try
            {
                Log.Debug($"GenerateFilterPreviews: Loading original image from {samplePhotoPath}");
                
                // Check if file exists
                if (!File.Exists(samplePhotoPath))
                {
                    Log.Error($"GenerateFilterPreviews: Sample photo not found: {samplePhotoPath}");
                    throw new FileNotFoundException("Sample photo not found", samplePhotoPath);
                }
                
                // Load original image
                var originalImage = new BitmapImage();
                originalImage.BeginInit();
                originalImage.CacheOption = BitmapCacheOption.OnLoad;
                originalImage.UriSource = new Uri(samplePhotoPath);
                originalImage.DecodePixelWidth = 800; // Limit size for performance
                originalImage.EndInit();
                originalImage.Freeze();
                
                // Add "No Filter" option
                var noFilterItem = new Photobooth.Controls.FilterItem
                {
                    Name = "No Filter",
                    FilterType = FilterType.None,
                    PreviewImage = originalImage,
                    IsSelected = true
                };
                filterItems.Add(noFilterItem);
                
                // Show initial preview in live view
                liveViewImage.Source = originalImage;
                statusText.Text = "Select a filter to preview";
                
                Log.Debug("GenerateFilterPreviews: Original image loaded, generating filter previews");
                
                // Start with just the first few enabled filters for instant loading
                var instantFilters = enabledFilters.Take(4).ToArray();
                
                Log.Debug($"GenerateFilterPreviews: Starting parallel generation of {instantFilters.Length} filters");
                
                // Generate all filters in parallel for much faster loading
                var filterTasks = instantFilters.Select(async filterType =>
                {
                    try
                    {
                        Log.Debug($"GenerateFilterPreviews: Starting {filterType}");
                        
                        // Reduced timeout for faster response
                        var filterTask = ApplyFilterToPhoto(samplePhotoPath, filterType, true);
                        var timeoutTask = Task.Delay(2000); // Reduced to 2 seconds
                        
                        var completedTask = await Task.WhenAny(filterTask, timeoutTask);
                        
                        if (completedTask == filterTask)
                        {
                            string previewPath = await filterTask;
                            
                            if (!string.IsNullOrEmpty(previewPath) && File.Exists(previewPath))
                            {
                                var previewImage = new BitmapImage();
                                previewImage.BeginInit();
                                previewImage.CacheOption = BitmapCacheOption.OnLoad;
                                previewImage.UriSource = new Uri(previewPath);
                                previewImage.DecodePixelWidth = 300; // Even smaller for faster loading
                                previewImage.EndInit();
                                previewImage.Freeze();
                                
                                var filterItem = new Photobooth.Controls.FilterItem
                                {
                                    Name = GetFilterDisplayName(filterType),
                                    FilterType = filterType,
                                    PreviewImage = previewImage,
                                    IsSelected = false
                                };
                                
                                Log.Debug($"GenerateFilterPreviews: Completed {filterType}");
                                return filterItem;
                            }
                        }
                        else
                        {
                            Log.Debug($"GenerateFilterPreviews: Timeout for {filterType}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"GenerateFilterPreviews: Failed {filterType}: {ex.Message}");
                    }
                    return null;
                }).ToArray();
                
                // Wait for all parallel tasks to complete
                var results = await Task.WhenAll(filterTasks);
                
                // Add successful results to the collection
                foreach (var result in results)
                {
                    if (result != null)
                    {
                        filterItems.Add(result);
                    }
                }
                
                Log.Debug($"GenerateFilterPreviews: Parallel generation completed, got {filterItems.Count - 1} filter previews");
                
                Log.Debug($"GenerateFilterPreviews: Generated {filterItems.Count} filter previews");
            }
            catch (Exception ex)
            {
                Log.Error($"GenerateFilterPreviews: Critical error: {ex.Message}", ex);
                // At minimum, add the no-filter option
                if (filterItems.Count == 0)
                {
                    try
                    {
                        filterItems.Add(new Photobooth.Controls.FilterItem
                        {
                            Name = "No Filter",
                            FilterType = FilterType.None,
                            PreviewImage = new BitmapImage(new Uri(samplePhotoPath)),
                            IsSelected = true
                        });
                    }
                    catch
                    {
                        // If even this fails, create a minimal item
                        filterItems.Add(new Photobooth.Controls.FilterItem
                        {
                            Name = "No Filter",
                            FilterType = FilterType.None,
                            PreviewImage = null,
                            IsSelected = true
                        });
                    }
                }
            }
            
            postSessionFilterControl.ItemsSource = filterItems;
            Log.Debug($"GenerateFilterPreviews: Set ItemsSource with {filterItems.Count} items");
            
            // Force update the UI
            postSessionFilterControl.UpdateLayout();
            Log.Debug($"GenerateFilterPreviews: UpdateLayout called");
            
            // Load additional filters in background for more options
            var remainingFilters = enabledFilters.Skip(4).ToArray();
            if (remainingFilters.Length > 0)
            {
                Task.Run(async () =>
                {
                    await LoadAdditionalFilters(samplePhotoPath, filterItems, remainingFilters);
                });
            }
        }
        
        private FilterType[] GetEnabledFiltersFromSettings()
        {
            var enabledFiltersString = Properties.Settings.Default.EnabledFilters;
            var enabledFiltersList = new List<FilterType>();
            
            // If no enabled filters configured, use default popular filters
            if (string.IsNullOrEmpty(enabledFiltersString))
            {
                return new[] 
                { 
                    FilterType.BlackAndWhite,  
                    FilterType.Glamour,        
                    FilterType.Vintage,        
                    FilterType.Sepia,
                    FilterType.Warm,
                    FilterType.Cool,
                    FilterType.Vivid,
                    FilterType.Soft
                };
            }
            
            // Parse the enabled filters string (comma-separated filter names)
            var filterNames = enabledFiltersString.Split(',', ';');
            foreach (var filterName in filterNames)
            {
                var trimmedName = filterName.Trim();
                if (Enum.TryParse<FilterType>(trimmedName, true, out var filterType))
                {
                    enabledFiltersList.Add(filterType);
                }
            }
            
            // If parsing failed or no valid filters, use defaults
            if (enabledFiltersList.Count == 0)
            {
                return new[] 
                { 
                    FilterType.BlackAndWhite,  
                    FilterType.Glamour,        
                    FilterType.Vintage,        
                    FilterType.Sepia 
                };
            }
            
            return enabledFiltersList.ToArray();
        }
        
        private async Task LoadAdditionalFilters(string samplePhotoPath, System.Collections.ObjectModel.ObservableCollection<Photobooth.Controls.FilterItem> filterItems, FilterType[] remainingFilters)
        {
            try
            {
                Log.Debug($"LoadAdditionalFilters: Loading {remainingFilters.Length} additional filters in background");
                
                // Generate remaining filters in smaller batches to avoid overwhelming the UI
                const int batchSize = 2;
                for (int i = 0; i < remainingFilters.Length; i += batchSize)
                {
                    var batch = remainingFilters.Skip(i).Take(batchSize).ToArray();
                    var batchTasks = batch.Select(async filterType =>
                    {
                        try
                        {
                            Log.Debug($"LoadAdditionalFilters: Starting {filterType}");
                            
                            var filterTask = ApplyFilterToPhoto(samplePhotoPath, filterType, true);
                            var timeoutTask = Task.Delay(3000); // 3 second timeout for background loading
                            var completedTask = await Task.WhenAny(filterTask, timeoutTask);
                            
                            if (completedTask == filterTask)
                            {
                                var filteredImagePath = await filterTask;
                                if (!string.IsNullOrEmpty(filteredImagePath) && File.Exists(filteredImagePath))
                                {
                                    var previewImage = new BitmapImage();
                                    previewImage.BeginInit();
                                    previewImage.UriSource = new Uri(filteredImagePath);
                                    previewImage.CacheOption = BitmapCacheOption.OnLoad;
                                    previewImage.DecodePixelWidth = 300;
                                    previewImage.EndInit();
                                    previewImage.Freeze();
                                    
                                    var filterItem = new Photobooth.Controls.FilterItem
                                    {
                                        Name = GetFilterDisplayName(filterType),
                                        FilterType = filterType,
                                        PreviewImage = previewImage,
                                        IsSelected = false
                                    };
                                    
                                    Log.Debug($"LoadAdditionalFilters: Completed {filterType}");
                                    return filterItem;
                                }
                            }
                            else
                            {
                                Log.Debug($"LoadAdditionalFilters: Timeout for {filterType}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"LoadAdditionalFilters: Failed {filterType}: {ex.Message}");
                        }
                        return null;
                    }).ToArray();
                    
                    // Wait for this batch to complete
                    var batchResults = await Task.WhenAll(batchTasks);
                    
                    // Add successful results to the UI on the main thread
                    foreach (var result in batchResults)
                    {
                        if (result != null)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                filterItems.Add(result);
                                Log.Debug($"LoadAdditionalFilters: Added {result.FilterType} to UI");
                            });
                        }
                    }
                    
                    // Small delay between batches to keep UI responsive
                    await Task.Delay(500);
                }
                
                Log.Debug($"LoadAdditionalFilters: Completed loading all additional filters");
            }
            catch (Exception ex)
            {
                Log.Error($"LoadAdditionalFilters: Critical error: {ex.Message}", ex);
            }
        }
        
        private string GetFilterDisplayName(FilterType filterType)
        {
            // Convert filter type to friendly display name
            switch (filterType)
            {
                case FilterType.BlackAndWhite:
                    return "Black & White";
                case FilterType.Sepia:
                    return "Sepia Tone";
                case FilterType.Vintage:
                    return "Vintage";
                case FilterType.Glamour:
                    return "Glamour";
                case FilterType.Warm:
                    return "Warm";
                case FilterType.Cool:
                    return "Cool";
                case FilterType.HighContrast:
                    return "High Contrast";
                case FilterType.Soft:
                    return "Soft Focus";
                case FilterType.Vivid:
                    return "Vivid Colors";
                case FilterType.Custom:
                    return "Custom";
                default:
                    return filterType.ToString();
            }
        }
        
        private FilterType GetRandomEnabledFilter()
        {
            // Get the list of enabled filters from settings
            string enabledFiltersString = Properties.Settings.Default.EnabledFilters;
            List<FilterType> enabledFilters = new List<FilterType>();
            
            // If no specific filters are enabled in settings, use popular filters
            if (string.IsNullOrEmpty(enabledFiltersString))
            {
                // Popular filters that users love - weighted towards these
                enabledFilters.Add(FilterType.None);           // 10% chance - No Filter
                enabledFilters.Add(FilterType.BlackAndWhite);  // Popular - Classic B&W
                enabledFilters.Add(FilterType.BlackAndWhite);  // Double weight for B&W
                enabledFilters.Add(FilterType.Glamour);        // Popular - B&W Glamour
                enabledFilters.Add(FilterType.Glamour);        // Double weight for Glamour
                enabledFilters.Add(FilterType.Vintage);        // Popular - Instagram style
                enabledFilters.Add(FilterType.Vintage);        // Double weight for Vintage
                enabledFilters.Add(FilterType.Vivid);          // Popular - Bright colors
                enabledFilters.Add(FilterType.Sepia);          // Classic
                enabledFilters.Add(FilterType.Warm);           // Summer feel
                enabledFilters.Add(FilterType.Cool);           // Cool tones
                enabledFilters.Add(FilterType.Soft);           // Romantic soft focus
            }
            else
            {
                // Parse the comma-separated list of enabled filters
                string[] filterNames = enabledFiltersString.Split(',');
                foreach (string filterName in filterNames)
                {
                    if (Enum.TryParse<FilterType>(filterName.Trim(), out FilterType filter))
                    {
                        enabledFilters.Add(filter);
                    }
                }
                
                // If no valid filters were parsed, add at least "No Filter"
                if (enabledFilters.Count == 0)
                {
                    enabledFilters.Add(FilterType.None);
                }
            }
            
            // If DefaultFilter is set and AutoApplyFilters uses default only
            if (Properties.Settings.Default.DefaultFilter > 0 && !Properties.Settings.Default.AllowFilterChange)
            {
                return (FilterType)Properties.Settings.Default.DefaultFilter;
            }
            
            // Randomly select one filter from the enabled list
            Random random = new Random();
            int index = random.Next(enabledFilters.Count);
            return enabledFilters[index];
        }
        
        private void ProcessTemplateWithPhotosInternal(FilterType selectedFilter = FilterType.None)
        {
            statusText.Text = $"Processing template with {capturedPhotoPaths.Count} photos...";
            
            Task.Run(async () =>
            {
                try
                {
                    Log.Debug($"ProcessTemplateWithPhotosInternal: Processing {capturedPhotoPaths.Count} photos into template");
                    
                    // Apply filters if a filter was selected
                    if (selectedFilter != FilterType.None && Properties.Settings.Default.EnableFilters)
                    {
                        Log.Debug($"Applying {selectedFilter} filter to photos");
                        await Dispatcher.InvokeAsync(() => statusText.Text = "Applying filters...");
                        
                        // Apply filter to each photo
                        List<string> filteredPaths = new List<string>();
                        for (int i = 0; i < capturedPhotoPaths.Count; i++)
                        {
                            string filteredPath = await ApplyFilterToPhoto(capturedPhotoPaths[i], selectedFilter);
                            filteredPaths.Add(filteredPath);
                        }
                        
                        // Update paths to use filtered versions
                        capturedPhotoPaths = filteredPaths;
                    }
                    else
                    {
                        Log.Debug("No filter selected or filters disabled - skipping filter application");
                    }
                    
                    // Process the template with the captured photos
                    string processedImagePath = await ComposeTemplateWithPhotos();
                    
                    if (!string.IsNullOrEmpty(processedImagePath) && File.Exists(processedImagePath))
                    {
                        Log.Debug($"ProcessTemplateWithPhotos: Template processed successfully: {processedImagePath}");
                        
                        Dispatcher.Invoke(() =>
                        {
                            // Show the processed image (always show the original, not the 4x6 duplicate)
                            liveViewImage.Source = new BitmapImage(new Uri(processedImagePath));
                            statusText.Text = "Photos processed successfully!";
                            
                            // Note: lastProcessedImagePath and lastProcessedImagePathForPrinting are already set in ComposeTemplateWithPhotos
                            
                            // Show print button
                            ShowPrintButton();
                            
                            // Don't show start button yet - wait for Done button or auto-clear
                            
                            // Hide stop button since session is complete
                            if (stopSessionButton != null)
                            {
                                stopSessionButton.Visibility = Visibility.Collapsed;
                            }
                            
                            // Fallback: ensure button is visible even if animation fails
                            if (printButton != null)
                            {
                                printButton.Visibility = Visibility.Visible;
                                printButton.Opacity = 1;
                            }
                            
                            // Show Done button
                            if (doneButton != null)
                            {
                                doneButton.Visibility = Visibility.Visible;
                            }
                            
                            // Optional: Auto-stop after processing (with delay)
                            Task.Delay(3000).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    StopPhotoSequence();
                                    statusText.Text = "Session complete - Click photos to view or touch PRINT to print";
                                    
                                    // Show Done button if enabled
                                    if (doneButton != null)
                                    {
                                        doneButton.Visibility = Visibility.Visible;
                                    }
                                    
                                    // Start auto-clear timer if enabled
                                    StartAutoClearTimer();
                                    
                                    // Show photo view mode indicator
                                    if (photoViewModeIndicator != null && photoStripItems.Any(p => !p.IsPlaceholder))
                                    {
                                        photoViewModeIndicator.Visibility = Visibility.Visible;
                                    }
                                    
                                    // Automatically stop after processing template
                                    Task.Delay(500).ContinueWith(__ =>
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            // End the database session
                                            EndDatabaseSession();
                                            
                                            // Keep photos viewable - don't clear the strip
                                            currentPhotoIndex = 0;
                                            
                                            // Don't clear these to allow viewing:
                                            // capturedPhotoPaths.Clear();
                                            // photoStripImages.Clear();
                                            // photoStripItems.Clear();
                                            
                                            // Don't hide print button here - keep it visible for user to print
                                            // It will be hidden when they start a new session or print
                                            
                                            // Reset template but keep the event
                                            currentTemplate = null;
                                            totalPhotosNeeded = 0;
                                            
                                            // Check if event has multiple templates
                                            if (currentEvent != null && availableTemplates != null && availableTemplates.Count > 1)
                                            {
                                                // Show template selection for same event
                                                statusText.Text = $"Event: {currentEvent.Name} - Touch START to select another template";
                                            }
                                            else if (currentEvent != null && availableTemplates != null && availableTemplates.Count == 1)
                                            {
                                                // Single template - ready to start again
                                                currentTemplate = availableTemplates[0];
                                                totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                                                currentPhotoIndex = 0;
                                                UpdatePhotoStripPlaceholders(true); // Preserve existing photos
                                                statusText.Text = $"Event: {currentEvent.Name} - Touch START for another session";
                                            }
                                            else
                                            {
                                                // No event or templates - show event selection
                                                statusText.Text = "Touch Event Settings to select an event";
                                            }
                                        });
                                    });
                                });
                            });
                        });
                    }
                    else
                    {
                        Log.Error("ProcessTemplateWithPhotos: No processed image returned");
                        Dispatcher.Invoke(() =>
                        {
                            statusText.Text = "Failed to process template";
                            StopPhotoSequence();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("ProcessTemplateWithPhotos: Failed to process template", ex);
                    Dispatcher.Invoke(() =>
                    {
                        statusText.Text = $"Template processing error: {ex.Message}";
                        StopPhotoSequence();
                    });
                }
            });
        }
        
        #endregion
        
        #region Filter Functionality
        
        private void EnableFilters_Checked(object sender, RoutedEventArgs e)
        {
            if (filterSelectionControl != null)
            {
                filterSelectionControl.Visibility = Visibility.Visible;
                
                // Regenerate previews if we have photos
                if (capturedPhotoPaths.Count > 0)
                {
                    Task.Run(async () =>
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            await filterSelectionControl.SetSourceImage(capturedPhotoPaths[0]);
                        });
                    });
                }
            }
        }
        
        private void EnableFilters_Unchecked(object sender, RoutedEventArgs e)
        {
            if (filterSelectionControl != null)
            {
                filterSelectionControl.Visibility = Visibility.Collapsed;
                filterSelectionControl.SetSelectedFilter(FilterType.None);
            }
        }
        
        private async Task<string> ApplyFilterToPhoto(string inputPath, FilterType filterType, bool isPreview = false)
        {
            try
            {
                if (filterType == FilterType.None)
                    return inputPath;
                
                if (!File.Exists(inputPath))
                {
                    Log.Error($"ApplyFilterToPhoto: Input file not found: {inputPath}");
                    return inputPath;
                }
                
                string outputPath = Path.Combine(
                    Path.GetDirectoryName(inputPath),
                    isPreview 
                        ? $"{Path.GetFileNameWithoutExtension(inputPath)}_preview_{filterType}{Path.GetExtension(inputPath)}"
                        : $"{Path.GetFileNameWithoutExtension(inputPath)}_filtered{Path.GetExtension(inputPath)}"
                );
                
                // Check if preview already exists to save time
                if (isPreview && File.Exists(outputPath))
                {
                    Log.Debug($"ApplyFilterToPhoto: Using cached preview for {filterType}");
                    return outputPath;
                }
                
                float intensity = Properties.Settings.Default.FilterIntensity / 100f;
                
                if (filterService == null)
                {
                    Log.Error("ApplyFilterToPhoto: Filter service is not initialized");
                    return inputPath;
                }
                
                // Run filter processing on thread pool for non-blocking operation
                return await Task.Run(() => 
                    filterService.ApplyFilterToFile(inputPath, outputPath, filterType, intensity));
            }
            catch (Exception ex)
            {
                Log.Error($"ApplyFilterToPhoto: Failed to apply filter {filterType} to {inputPath}: {ex.Message}");
                return inputPath; // Return original if filter fails
            }
        }
        
        #endregion
        
        #region Printer Monitoring
        
        private void OnPrinterStatusChanged(object sender, PrinterMonitorService.PrinterStatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update printer name and connection type
                if (!string.IsNullOrEmpty(e.PrinterName))
                {
                    // Truncate printer name if too long
                    string displayName = e.PrinterName;
                    if (displayName.Length > 20)
                    {
                        displayName = displayName.Substring(0, 17) + "...";
                    }
                    printerNameText.Text = displayName;
                    
                    // Show connection type
                    printerConnectionText.Text = $" ({e.ConnectionType})";
                    
                    // Set connection text color based on status
                    if (e.IsOnline)
                    {
                        printerConnectionText.Foreground = new SolidColorBrush(Colors.LightGreen);
                    }
                    else
                    {
                        printerConnectionText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)); // Orange
                    }
                }
                else
                {
                    printerNameText.Text = "No Printer";
                    printerConnectionText.Text = " (Offline)";
                    printerConnectionText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0));
                }
                
                // Update status text and indicator
                printerStatusText.Text = e.Status;
                
                // Update status indicator and text color based on printer state
                if (e.HasError)
                {
                    printerStatusIndicator.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82)); // Red
                    printerStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82));
                    
                    // Show error message
                    if (!string.IsNullOrEmpty(e.ErrorMessage))
                    {
                        printerErrorText.Text = e.ErrorMessage;
                        printerErrorText.Visibility = Visibility.Visible;
                    }
                }
                else if (!e.IsOnline)
                {
                    printerStatusIndicator.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)); // Orange
                    printerStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0));
                    printerErrorText.Visibility = Visibility.Collapsed;
                }
                else if (e.Status == "Printing" || e.Status == "Processing")
                {
                    printerStatusIndicator.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // Blue
                    printerStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                    printerErrorText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    printerStatusIndicator.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green
                    printerStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                    printerErrorText.Visibility = Visibility.Collapsed;
                }
                
                // Update queue count
                printerQueueText.Text = e.JobsInQueue.ToString();
                
                // Add queue count to status if there are jobs
                if (e.JobsInQueue > 0 && !e.HasError)
                {
                    printerStatusText.Text = $"{e.Status}: {e.JobsInQueue}";
                }
            });
        }
        
        #endregion
        
        #region Public Methods for Event Selection
        
        public void SetEvent(EventData eventData)
        {
            currentEvent = eventData;
            PhotoboothService.CurrentEvent = eventData;
            
            if (eventData != null)
            {
                // Update folder path to include event name
                string eventFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                    "Photobooth", SanitizeFileName(eventData.Name));
                if (!Directory.Exists(eventFolder))
                {
                    Directory.CreateDirectory(eventFolder);
                }
                FolderForPhotos = eventFolder;
                
                // Load templates for this event
                availableTemplates = eventService.GetEventTemplates(eventData.Id);
                
                if (availableTemplates != null && availableTemplates.Count == 1)
                {
                    // Auto-select single template
                    SetTemplate(availableTemplates[0]);
                }
                else if (availableTemplates != null && availableTemplates.Count > 0)
                {
                    // Multiple templates - will be selected when START is pressed
                    statusText.Text = $"Event: {eventData.Name} - Touch START to select template";
                }
                else
                {
                    statusText.Text = $"Event: {eventData.Name} - No templates available";
                }
                
                Log.Debug($"SetEvent: Event '{eventData.Name}' loaded with {availableTemplates?.Count ?? 0} templates");
            }
        }
        
        public void SetTemplate(TemplateData templateData)
        {
            currentTemplate = templateData;
            PhotoboothService.CurrentTemplate = templateData;
            
            if (templateData != null)
            {
                totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(templateData);
                currentPhotoIndex = 0;
                capturedPhotoPaths.Clear();
                UpdatePhotoStripPlaceholders();
                
                if (currentEvent != null)
                {
                    statusText.Text = $"Event: {currentEvent.Name} - Template: {templateData.Name}";
                }
                else
                {
                    statusText.Text = $"Template: {templateData.Name} - Ready";
                }
                
                Log.Debug($"SetTemplate: Template '{templateData.Name}' set, needs {totalPhotosNeeded} photos");
            }
        }
        
        #endregion
        
        #region Lock/Unlock Features
        
        private bool _isLocked = false;
        private string _enteredPin = "";
        private Action _pendingActionAfterUnlock = null;
        
        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLocked)
            {
                // Lock the interface
                _isLocked = true;
                lockButton.Content = "🔒";
                lockButton.ToolTip = "Unlock Interface";
                
                // Disable critical controls
                DisableCriticalControls();
                
                // Hide the bottom navbar if it's visible and PIN is enabled
                if (Properties.Settings.Default.EnableLockFeature && bottomControlBar != null && bottomControlBar.Visibility == Visibility.Visible)
                {
                    bottomControlBar.Visibility = Visibility.Collapsed;
                    if (bottomBarToggleChevron != null)
                    {
                        bottomBarToggleChevron.Text = "⌃"; // Up chevron
                    }
                }
                
                // Show locked status
                statusText.Text = "Interface Locked - Click lock icon to unlock";
            }
            else
            {
                // Show PIN entry dialog
                ShowPinEntryDialog();
            }
        }
        
        private void DisableCriticalControls()
        {
            // Disable buttons that could change settings or exit
            exitButton.IsEnabled = false;
            homeButton.IsEnabled = false; // Disable home button when locked
            printButton.IsEnabled = false; // Disable print button when locked
            resetCameraButton.IsEnabled = false;
            cameraSettingsButton.IsEnabled = false;
            
            // Hide start button when locked
            if (startButtonOverlay != null)
                startButtonOverlay.Visibility = Visibility.Collapsed;
            galleryButton.IsEnabled = false;
            eventSettingsButton.IsEnabled = false;
        }
        
        private void EnableCriticalControls()
        {
            // Re-enable all controls
            exitButton.IsEnabled = true;
            homeButton.IsEnabled = true; // Re-enable home button when unlocked
            printButton.IsEnabled = true; // Re-enable print button when unlocked
            resetCameraButton.IsEnabled = true;
            cameraSettingsButton.IsEnabled = true;
            
            // Show start button when unlocked
            if (startButtonOverlay != null)
                startButtonOverlay.Visibility = Visibility.Visible;
            galleryButton.IsEnabled = true;
            eventSettingsButton.IsEnabled = true;
        }
        
        private void ShowPinEntryDialog()
        {
            _enteredPin = "";
            pinDisplayBox.Text = "";
            pinErrorText.Visibility = Visibility.Collapsed;
            pinEntryOverlay.Visibility = Visibility.Visible;
            UpdatePinDots();
        }
        
        private void PinPadButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && _enteredPin.Length < 6)
            {
                _enteredPin += button.Content.ToString();
                pinDisplayBox.Text = new string('●', _enteredPin.Length);
                UpdatePinDots();
            }
        }
        
        private void UpdatePinDots()
        {
            // Update visual PIN dots based on entered length
            pinDot1.Background = _enteredPin.Length >= 1 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 217, 255)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
            pinDot2.Background = _enteredPin.Length >= 2 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 217, 255)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
            pinDot3.Background = _enteredPin.Length >= 3 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 217, 255)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
            pinDot4.Background = _enteredPin.Length >= 4 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 217, 255)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
            pinDot5.Background = _enteredPin.Length >= 5 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 217, 255)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
            pinDot6.Background = _enteredPin.Length >= 6 ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 217, 255)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 255, 255, 255));
        }
        
        private void PinClearButton_Click(object sender, RoutedEventArgs e)
        {
            _enteredPin = "";
            pinDisplayBox.Text = "";
            pinErrorText.Visibility = Visibility.Collapsed;
            UpdatePinDots();
        }
        
        private void PinSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the parent window to validate PIN
            var parentWindow = Window.GetWindow(this);
            bool isValid = false;
            
            if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
            {
                isValid = surfaceWindow.ValidatePin(_enteredPin);
            }
            else
            {
                // Use PIN from settings
                string settingsPin = Properties.Settings.Default.LockPin;
                isValid = _enteredPin == settingsPin;
            }
            
            if (isValid)
            {
                // Unlock successful
                _isLocked = false;
                lockButton.Content = "🔓";
                lockButton.ToolTip = "Lock Interface";
                
                // Re-enable controls
                EnableCriticalControls();
                
                // Hide PIN dialog
                pinEntryOverlay.Visibility = Visibility.Collapsed;
                
                // Update status
                statusText.Text = "Interface Unlocked";
                
                // Execute pending action if any
                if (_pendingActionAfterUnlock != null)
                {
                    var action = _pendingActionAfterUnlock;
                    _pendingActionAfterUnlock = null;
                    action.Invoke();
                }
            }
            else
            {
                // Show error
                pinErrorText.Visibility = Visibility.Visible;
                _enteredPin = "";
                pinDisplayBox.Text = "";
            }
        }
        
        private void PinCancelButton_Click(object sender, RoutedEventArgs e)
        {
            pinEntryOverlay.Visibility = Visibility.Collapsed;
            _enteredPin = "";
            pinDisplayBox.Text = "";
        }
        
        #endregion
        
        #region Print Functionality
        
        private void CheckPrinterStatus()
        {
            try
            {
                var printService = PrintService.Instance;
                string printerName = printService.GetCurrentPrinterName();
                bool isReady = printService.IsPrinterReady();
                
                if (!string.IsNullOrEmpty(printerName))
                {
                    if (isReady)
                    {
                        printerStatusText.Text = $"🖨️ {printerName} Ready";
                        printerStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
                        Log.Debug($"CheckPrinterStatus: Printer {printerName} is ready");
                    }
                    else
                    {
                        printerStatusText.Text = $"🖨️ {printerName} Offline";
                        printerStatusText.Foreground = new SolidColorBrush(Colors.Orange);
                        Log.Debug($"CheckPrinterStatus: Printer {printerName} is offline");
                    }
                }
                else
                {
                    printerStatusText.Text = "🖨️ No Printer";
                    printerStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    Log.Debug("CheckPrinterStatus: No printer configured");
                }
                
                // Check print limits
                int remainingEventPrints = printService.GetRemainingEventPrints();
                if (remainingEventPrints < int.MaxValue && remainingEventPrints < 50)
                {
                    // Show warning if running low on prints
                    Log.Debug($"CheckPrinterStatus: Only {remainingEventPrints} event prints remaining");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CheckPrinterStatus: Error checking printer status: {ex.Message}");
                printerStatusText.Text = "🖨️ Error";
                printerStatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        
        private void ShowPrintButton()
        {
            Log.Debug("ShowPrintButton: Called - attempting to show print button");
            
            if (printButton != null)
            {
                Log.Debug($"ShowPrintButton: Print button found, current visibility: {printButton.Visibility}");
                
                // First set opacity to 0 for animation
                printButton.Opacity = 0;
                printButton.Visibility = Visibility.Visible;
                
                // Animate the print button appearance
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(500),
                    FillBehavior = FillBehavior.HoldEnd
                };
                
                fadeIn.Completed += (s, e) => 
                {
                    printButton.Opacity = 1; // Ensure opacity stays at 1
                    Log.Debug("ShowPrintButton: Animation completed, opacity set to 1");
                };
                
                printButton.BeginAnimation(Button.OpacityProperty, fadeIn);
                Log.Debug("ShowPrintButton: Print button made visible with animation");
            }
            else
            {
                Log.Error("ShowPrintButton: Print button is null!");
            }
        }
        
        private void HidePrintButton()
        {
            if (printButton != null)
            {
                printButton.Visibility = Visibility.Collapsed;
                printButton.Opacity = 1; // Reset opacity for next time
                Log.Debug("HidePrintButton: Print button hidden");
            }
        }
        
        private bool ShouldKeepPrintButtonVisible(string sessionId)
        {
            // Check if we should keep the print button visible based on remaining prints
            var printService = PrintService.Instance;
            int remainingSessionPrints = printService.GetRemainingSessionPrints(sessionId);
            int remainingEventPrints = printService.GetRemainingEventPrints();
            
            // Keep button visible if we have prints remaining
            bool hasRemainingPrints = remainingSessionPrints > 0 && remainingEventPrints > 0;
            
            // Also check if print limits are disabled (infinite prints)
            bool unlimitedSession = Properties.Settings.Default.MaxSessionPrints <= 0;
            bool unlimitedEvent = Properties.Settings.Default.MaxEventPrints <= 0;
            
            // Keep visible if we have remaining prints or if limits are disabled
            return hasRemainingPrints || (unlimitedSession && unlimitedEvent);
        }
        
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            // Use the print version if available, otherwise use the display version
            string imageToPrint = !string.IsNullOrEmpty(lastProcessedImagePathForPrinting) ? 
                lastProcessedImagePathForPrinting : lastProcessedImagePath;
            
            // Use the display version for the dialog preview
            string imageToPreview = lastProcessedImagePath;
                
            if (string.IsNullOrEmpty(imageToPrint) || !File.Exists(imageToPrint))
            {
                statusText.Text = "No image available to print";
                return;
            }
            
            try
            {
                Log.Debug($"PrintButton_Click: Opening print dialog for {imageToPrint}");
                if (imageToPrint != lastProcessedImagePath)
                {
                    Log.Debug($"PrintButton_Click: Will use 4x6 duplicated version for printing");
                }
                
                // Create session ID if not exists
                string sessionId = currentEvent != null ? 
                    $"{currentEvent.Id}_{DateTime.Now:yyyyMMdd_HHmmss}" : 
                    $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";
                
                // Show the print dialog with the display image for preview
                var printDialog = new Photobooth.PrintCopyDialog(imageToPreview, sessionId, lastProcessedWas2x6Template);
                
                if (printDialog.ShowDialog() == true && printDialog.PrintConfirmed)
                {
                    int copies = printDialog.SelectedCopies;
                    Log.Debug($"PrintButton_Click: User selected {copies} copies");
                    
                    statusText.Text = "Sending to printer...";
                    
                    // Disable print button to prevent multiple clicks
                    printButton.IsEnabled = false;
                    
                    // Use the PrintService to handle printing
                    var printService = PrintService.Instance;
                    
                    // Prepare photos for printing (use the print version)
                    var photoPaths = new List<string> { imageToPrint };
                    
                    // Print using the service - pass the original format information for proper routing
                    var result = printService.PrintPhotos(photoPaths, sessionId, copies, lastProcessedWas2x6Template);
                    
                    if (result.Success)
                    {
                        // For 2x6 duplicated to 4x6, show the actual strip count
                        string printMessage = lastProcessedWas2x6Template && Properties.Settings.Default.Duplicate2x6To4x6 ?
                            $"Photo sent to printer! ({copies} sheets, {copies * 2} strips)" :
                            $"Photo sent to printer! ({result.PrintedCount} copies)";
                        statusText.Text = printMessage;
                        
                        // Update remaining print counts
                        if (result.RemainingSessionPrints < int.MaxValue)
                        {
                            Log.Debug($"PrintButton_Click: Remaining session prints: {result.RemainingSessionPrints}");
                        }
                        if (result.RemainingEventPrints < int.MaxValue)
                        {
                            Log.Debug($"PrintButton_Click: Remaining event prints: {result.RemainingEventPrints}");
                        }
                        
                        // Log successful print
                        Log.Debug($"PrintButton_Click: Successfully printed {result.PrintedCount} copies");
                        
                        // Check if we should keep print button visible or hide it
                        Task.Delay(3000).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                // Only hide if no prints remaining
                                if (!ShouldKeepPrintButtonVisible(sessionId))
                                {
                                    HidePrintButton();
                                    statusText.Text = "Print limit reached! Touch START for new session";
                                    Log.Debug($"PrintButton_Click: Hiding print button - no remaining prints");
                                }
                                else
                                {
                                    // Keep button visible and enabled for more prints
                                    printButton.IsEnabled = true;
                                    
                                    // Show remaining prints if limited
                                    if (result.RemainingSessionPrints < int.MaxValue)
                                    {
                                        statusText.Text = $"Print complete! {result.RemainingSessionPrints} prints remaining. Touch PRINT again or START for new session";
                                    }
                                    else
                                    {
                                        statusText.Text = "Print complete! Touch PRINT again or START for new session";
                                    }
                                    Log.Debug($"PrintButton_Click: Keeping print button visible - {result.RemainingSessionPrints} prints remaining");
                                }
                            });
                        });
                    }
                    else
                    {
                        statusText.Text = result.Message;
                        Log.Error($"PrintButton_Click: Print failed - {result.Message}");
                        printButton.IsEnabled = true;
                        
                        // Show error for a few seconds then restore
                        Task.Delay(3000).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                statusText.Text = "Touch PRINT to try again or START for new session";
                            });
                        });
                    }
                }
                else
                {
                    Log.Debug("PrintButton_Click: User cancelled print dialog");
                    statusText.Text = "Print cancelled";
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PrintButton_Click: Error printing image: {ex.Message}");
                statusText.Text = $"Print error: {ex.Message}";
                printButton.IsEnabled = true;
            }
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("DoneButton_Click: Clearing session");
            ClearSession();
        }
        
        private void AutoClearTimer_Tick(object sender, EventArgs e)
        {
            // This timer runs every second when active
            // We'll track elapsed time using a tag on the timer
            if (autoClearTimer.Tag == null)
            {
                autoClearTimer.Tag = 0;
            }
            
            int elapsedSeconds = (int)autoClearTimer.Tag;
            elapsedSeconds++;
            autoClearTimer.Tag = elapsedSeconds;
            
            int timeoutSeconds = Properties.Settings.Default.AutoClearTimeout;
            
            if (elapsedSeconds >= timeoutSeconds)
            {
                Log.Debug($"AutoClearTimer_Tick: Auto-clearing session after {timeoutSeconds} seconds");
                autoClearTimer.Stop();
                autoClearTimer.Tag = null;
                ClearSession();
            }
        }
        
        private void ClearSession()
        {
            Log.Debug("ClearSession: Clearing current session");
            
            // Stop any timers
            autoClearTimer.Stop();
            autoClearTimer.Tag = null;
            countdownTimer.Stop();
            retakeReviewTimer.Stop();
            
            // Clear photo collections
            capturedPhotoPaths.Clear();
            photoStripItems.Clear();
            retakePhotos.Clear();
            
            // Reset counters
            currentPhotoIndex = 0;
            photoCount = 0;
            photoIndexToRetake = -1;
            isRetakingPhoto = false;
            
            // Clear processed image paths
            lastProcessedImagePath = null;
            lastProcessedImagePathForPrinting = null;
            lastProcessedWas2x6Template = false;
            
            // Reset database session tracking
            currentDatabaseSessionId = null;
            currentSessionGuid = null;
            currentSessionPhotoIds.Clear();
            
            // Clear UI
            liveViewImage.Source = null;
            statusText.Text = "Touch START to begin";
            photoCountText.Text = "0";
            
            // Hide buttons that should only show during/after session
            if (printButton != null)
            {
                printButton.Visibility = Visibility.Collapsed;
            }
            if (doneButton != null)
            {
                doneButton.Visibility = Visibility.Collapsed;
            }
            if (stopSessionButton != null)
            {
                stopSessionButton.Visibility = Visibility.Collapsed;
            }
            
            // Show start button
            if (startButtonOverlay != null)
            {
                startButtonOverlay.Visibility = Visibility.Visible;
            }
            
            // Hide any overlays
            if (retakeReviewOverlay != null)
            {
                retakeReviewOverlay.Visibility = Visibility.Collapsed;
            }
            if (countdownOverlay != null)
            {
                countdownOverlay.Visibility = Visibility.Collapsed;
            }
            if (postSessionFilterOverlay != null)
            {
                postSessionFilterOverlay.Visibility = Visibility.Collapsed;
            }
            if (photoViewModeIndicator != null)
            {
                photoViewModeIndicator.Visibility = Visibility.Collapsed;
            }
            if (sessionLoadedIndicator != null)
            {
                sessionLoadedIndicator.Visibility = Visibility.Collapsed;
            }
            if (composedImageNavigation != null)
            {
                composedImageNavigation.Visibility = Visibility.Collapsed;
            }
            
            // Clear loaded session data
            loadedComposedImages = null;
            currentComposedImageIndex = 0;
            
            // Restart live view if camera is connected AND idle live view is enabled
            if (DeviceManager.SelectedCameraDevice != null && 
                DeviceManager.SelectedCameraDevice.IsConnected && 
                Properties.Settings.Default.EnableIdleLiveView)
            {
                try
                {
                    liveViewTimer.Start();
                    DeviceManager.SelectedCameraDevice.StartLiveView();
                    Log.Debug("ClearSession: Started idle live view");
                }
                catch (Exception ex)
                {
                    Log.Error("ClearSession: Failed to restart live view", ex);
                }
            }
            else if (DeviceManager.SelectedCameraDevice != null && 
                     DeviceManager.SelectedCameraDevice.IsConnected &&
                     !Properties.Settings.Default.EnableIdleLiveView)
            {
                // Stop live view if idle live view is disabled
                try
                {
                    liveViewTimer.Stop();
                    DeviceManager.SelectedCameraDevice.StopLiveView();
                    Log.Debug("ClearSession: Stopped live view (idle live view disabled)");
                }
                catch (Exception ex)
                {
                    Log.Debug($"ClearSession: Failed to stop live view: {ex.Message}");
                }
            }
            
            Log.Debug("ClearSession: Session cleared successfully");
        }
        
        private void StartAutoClearTimer()
        {
            if (Properties.Settings.Default.AutoClearSession)
            {
                Log.Debug($"StartAutoClearTimer: Starting auto-clear timer for {Properties.Settings.Default.AutoClearTimeout} seconds");
                autoClearTimer.Tag = 0;
                autoClearTimer.Start();
            }
        }

        #endregion

        #region Session Gallery Management
        
        private void GalleryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug("GalleryButton_Click: Opening session selection modal");
                
                // Set up modal event handlers
                SessionSelectionModal.SessionSelected -= OnSessionSelected;
                SessionSelectionModal.ModalClosed -= OnModalClosed;
                SessionSelectionModal.SessionSelected += OnSessionSelected;
                SessionSelectionModal.ModalClosed += OnModalClosed;
                
                // Show the modal with current event filter
                int? eventId = currentEvent?.Id;
                SessionSelectionModal.ShowModal(eventId);
            }
            catch (Exception ex)
            {
                Log.Error($"GalleryButton_Click: Error opening session selection: {ex.Message}");
                statusText.Text = $"Error opening gallery: {ex.Message}";
            }
        }
        
        private void OnSessionSelected(PhotoSessionData sessionData)
        {
            try
            {
                Log.Debug($"OnSessionSelected: Loading session {sessionData.SessionName} (ID: {sessionData.Id})");
                LoadSessionData(sessionData);
            }
            catch (Exception ex)
            {
                Log.Error($"OnSessionSelected: Error loading session: {ex.Message}");
                statusText.Text = $"Error loading session: {ex.Message}";
            }
        }
        
        private void OnModalClosed()
        {
            Log.Debug("OnModalClosed: Session selection modal closed");
        }
        
        private List<ComposedImageData> loadedComposedImages = null;
        private int currentComposedImageIndex = 0;
        
        private void LoadSessionData(PhotoSessionData sessionData)
        {
            try
            {
                // Set current session data
                currentEvent = database.GetEvent(sessionData.EventId);
                currentTemplate = database.GetTemplate(sessionData.TemplateId);
                
                if (currentEvent == null || currentTemplate == null)
                {
                    throw new Exception("Session references invalid event or template");
                }
                
                // Update UI to show loaded session
                statusText.Text = $"Loading session: {sessionData.SessionName} - {sessionData.EventName}...";
                
                // Load session photos and composed images
                var sessionPhotos = database.GetSessionPhotos(sessionData.Id);
                var composedImages = database.GetSessionComposedImages(sessionData.Id);
                
                Log.Debug($"LoadSessionData: Found {sessionPhotos.Count} photos and {composedImages.Count} composed images");
                
                // Display session info and load photo strip
                LoadPhotoStripFromSession(sessionPhotos, composedImages);
                
                // Update photo count
                photoCount = sessionPhotos.Count(p => p.PhotoType == "Original");
                photoCountText.Text = $"Photos: {photoCount}";
                
                // Enable print button if there are composed images
                if (composedImages.Any())
                {
                    var latestComposed = composedImages.OrderByDescending(c => c.CreatedDate).First();
                    lastProcessedImagePath = latestComposed.FilePath;
                    lastProcessedWas2x6Template = latestComposed.OutputFormat == "2x6";
                    
                    printButton.Visibility = Visibility.Visible;
                    printButton.IsEnabled = true;
                    
                    // Show Done button for loaded sessions
                    if (doneButton != null)
                    {
                        doneButton.Visibility = Visibility.Visible;
                    }
                    
                    // DON'T show start button yet - wait for Done button or auto-clear
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Collapsed;
                    }
                    
                    statusText.Text = $"Session loaded! {photoCount} photos, {composedImages.Count} layouts. Ready to reprint.";
                    
                    // Start auto-clear timer if enabled
                    StartAutoClearTimer();
                }
                else
                {
                    statusText.Text = $"Session loaded! {photoCount} photos. No composed layouts found.";
                    
                    // Show Done button even without composed images
                    if (doneButton != null)
                    {
                        doneButton.Visibility = Visibility.Visible;
                    }
                    
                    // DON'T show start button yet - wait for Done button or auto-clear
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Collapsed;
                    }
                    
                    // Start auto-clear timer if enabled
                    StartAutoClearTimer();
                }
                
                Log.Debug($"LoadSessionData: Session '{sessionData.SessionName}' loaded successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"LoadSessionData: Error loading session data: {ex.Message}");
                statusText.Text = $"Error loading session: {ex.Message}";
                throw;
            }
        }
        
        private void LoadPhotoStripFromSession(List<PhotoData> sessionPhotos, List<ComposedImageData> composedImages)
        {
            try
            {
                // Clear current photo strip
                photoStripImages.Clear();
                photoStripItems.Clear();
                capturedPhotoPaths.Clear();
                
                // Stop live view if camera is running
                try
                {
                    if (DeviceManager.SelectedCameraDevice != null)
                    {
                        DeviceManager.SelectedCameraDevice.StopLiveView();
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"LoadPhotoStripFromSession: StopLiveView failed (may not be running): {ex.Message}");
                }
                
                // Clear live view initially
                liveViewImage.Source = null;
                
                // Load original photos into photo strip
                var originalPhotos = sessionPhotos.Where(p => p.PhotoType == "Original")
                                                 .OrderBy(p => p.SequenceNumber)
                                                 .ToList();
                
                foreach (var photo in originalPhotos)
                {
                    if (System.IO.File.Exists(photo.FilePath))
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 150; // Thumbnail size
                            bitmap.UriSource = new Uri(photo.FilePath);
                            bitmap.EndInit();
                            bitmap.Freeze();
                            
                            photoStripImages.Add(bitmap);
                            capturedPhotoPaths.Add(photo.FilePath);
                            
                            // Add to photo strip items
                            var stripItem = new PhotoStripItem
                            {
                                Image = bitmap,
                                PhotoNumber = photo.SequenceNumber,
                                IsPlaceholder = false,
                                ItemType = "Photo",
                                FilePath = photo.FilePath  // Store the full path for loading full-size image
                            };
                            photoStripItems.Add(stripItem);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"LoadPhotoStripFromSession: Error loading photo {photo.FilePath}: {ex.Message}");
                        }
                    }
                }
                
                // Add composed images and GIFs to photo strip
                if (composedImages.Any())
                {
                    // Store composed images for navigation
                    loadedComposedImages = composedImages.OrderByDescending(c => c.CreatedDate).ToList();
                    currentComposedImageIndex = 0;
                    
                    // Add each composed image to the photo strip
                    foreach (var composedImage in composedImages)
                    {
                        if (System.IO.File.Exists(composedImage.FilePath))
                        {
                            try
                            {
                                // Check if it's a video/animation
                                bool isVideo = composedImage.OutputFormat == "MP4" || 
                                             composedImage.FilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                                bool isGif = composedImage.OutputFormat == "GIF" || 
                                           composedImage.FilePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
                                
                                BitmapImage bitmap = null;
                                
                                if (isVideo)
                                {
                                    // For MP4, try to use thumbnail or first photo
                                    if (!string.IsNullOrEmpty(composedImage.ThumbnailPath) && 
                                        System.IO.File.Exists(composedImage.ThumbnailPath))
                                    {
                                        bitmap = new BitmapImage();
                                        bitmap.BeginInit();
                                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                        bitmap.DecodePixelWidth = 150;
                                        bitmap.UriSource = new Uri(composedImage.ThumbnailPath);
                                        bitmap.EndInit();
                                        bitmap.Freeze();
                                    }
                                    else if (capturedPhotoPaths.Count > 0)
                                    {
                                        // Use first captured photo as thumbnail
                                        bitmap = new BitmapImage();
                                        bitmap.BeginInit();
                                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                        bitmap.DecodePixelWidth = 150;
                                        bitmap.UriSource = new Uri(capturedPhotoPaths[0]);
                                        bitmap.EndInit();
                                        bitmap.Freeze();
                                    }
                                    else
                                    {
                                        // Create placeholder
                                        bitmap = CreatePlaceholderBitmap("Video");
                                    }
                                }
                                else
                                {
                                    // For regular images and GIFs
                                    bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.DecodePixelWidth = 150; // Thumbnail size
                                    bitmap.UriSource = new Uri(composedImage.FilePath);
                                    bitmap.EndInit();
                                    bitmap.Freeze();
                                }
                                
                                string itemType = isVideo ? "VIDEO" : (isGif ? "GIF" : "Composed");
                                
                                var stripItem = new PhotoStripItem
                                {
                                    Image = bitmap,
                                    PhotoNumber = 0,
                                    IsPlaceholder = false,
                                    ItemType = itemType,
                                    FilePath = composedImage.FilePath
                                };
                                photoStripItems.Add(stripItem);
                                
                                Log.Debug($"LoadPhotoStripFromSession: Added {stripItem.ItemType} ({composedImage.OutputFormat}) to photo strip");
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"LoadPhotoStripFromSession: Error loading composed image {composedImage.FilePath}: {ex.Message}");
                            }
                        }
                    }
                    
                    // Display first composed image
                    DisplayComposedImage(currentComposedImageIndex);
                    
                    // Show/hide navigation controls based on count
                    if (composedImages.Count > 1 && composedImageNavigation != null)
                    {
                        composedImageNavigation.Visibility = Visibility.Visible;
                        UpdateComposedImageNavigationUI();
                    }
                    else if (composedImageNavigation != null)
                    {
                        composedImageNavigation.Visibility = Visibility.Collapsed;
                    }
                }
                
                Log.Debug($"LoadPhotoStripFromSession: Loaded {originalPhotos.Count} photos into strip");
            }
            catch (Exception ex)
            {
                Log.Error($"LoadPhotoStripFromSession: Error loading photo strip: {ex.Message}");
                throw;
            }
        }
        
        private void DisplayComposedImage(int index)
        {
            try
            {
                if (loadedComposedImages == null || index < 0 || index >= loadedComposedImages.Count)
                    return;
                
                var composedImage = loadedComposedImages[index];
                if (System.IO.File.Exists(composedImage.FilePath))
                {
                    var composedBitmap = new BitmapImage();
                    composedBitmap.BeginInit();
                    composedBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    // Don't decode to thumbnail size - load full image
                    // composedBitmap.DecodePixelWidth = removed to get full size
                    composedBitmap.UriSource = new Uri(composedImage.FilePath);
                    composedBitmap.EndInit();
                    composedBitmap.Freeze();
                    
                    // Show in live view area with fade-in animation
                    liveViewImage.Opacity = 0;
                    liveViewImage.Source = composedBitmap;
                    liveViewImage.Visibility = Visibility.Visible;
                    
                    // Hide countdown overlay if visible
                    if (countdownOverlay != null)
                        countdownOverlay.Visibility = Visibility.Collapsed;
                    
                    // Show session loaded indicator
                    if (sessionLoadedIndicator != null)
                    {
                        sessionLoadedIndicator.Visibility = Visibility.Visible;
                        if (sessionInfoText != null)
                        {
                            sessionInfoText.Text = $"Layout {index + 1} of {loadedComposedImages.Count}";
                        }
                    }
                    
                    // Animate fade-in
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(300)
                    };
                    liveViewImage.BeginAnimation(System.Windows.Controls.Image.OpacityProperty, fadeIn);
                    
                    // Update for printing
                    lastProcessedImagePath = composedImage.FilePath;
                    lastProcessedWas2x6Template = composedImage.OutputFormat == "2x6";
                    
                    Log.Debug($"DisplayComposedImage: Displayed composed image {index + 1}/{loadedComposedImages.Count}: {composedImage.FilePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DisplayComposedImage: Error displaying composed image at index {index}: {ex.Message}");
            }
        }
        
        private void UpdateComposedImageNavigationUI()
        {
            if (composedImageIndexText != null && loadedComposedImages != null)
            {
                composedImageIndexText.Text = $"{currentComposedImageIndex + 1} / {loadedComposedImages.Count}";
            }
            
            if (prevComposedButton != null)
            {
                prevComposedButton.IsEnabled = currentComposedImageIndex > 0;
                prevComposedButton.Opacity = prevComposedButton.IsEnabled ? 1.0 : 0.5;
            }
            
            if (nextComposedButton != null)
            {
                nextComposedButton.IsEnabled = loadedComposedImages != null && 
                                              currentComposedImageIndex < loadedComposedImages.Count - 1;
                nextComposedButton.Opacity = nextComposedButton.IsEnabled ? 1.0 : 0.5;
            }
        }
        
        private void PrevComposedImage_Click(object sender, RoutedEventArgs e)
        {
            if (loadedComposedImages != null && currentComposedImageIndex > 0)
            {
                currentComposedImageIndex--;
                DisplayComposedImage(currentComposedImageIndex);
                UpdateComposedImageNavigationUI();
            }
        }
        
        private void NextComposedImage_Click(object sender, RoutedEventArgs e)
        {
            if (loadedComposedImages != null && currentComposedImageIndex < loadedComposedImages.Count - 1)
            {
                currentComposedImageIndex++;
                DisplayComposedImage(currentComposedImageIndex);
                UpdateComposedImageNavigationUI();
            }
        }
        
        private void PhotoStripItem_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Border border && border.Tag is PhotoStripItem clickedItem)
                {
                    SelectPhotoStripItem(clickedItem);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoStripItem_Click: Error displaying photo: {ex.Message}");
            }
        }
        
        private void SelectPhotoStripItem(PhotoStripItem itemToSelect)
        {
            try
            {
                // Only allow selecting actual photos, not placeholders
                if (itemToSelect == null || itemToSelect.IsPlaceholder)
                    return;
                
                // Check if this is a video/animation
                if (itemToSelect.ItemType == "VIDEO" || itemToSelect.ItemType == "GIF")
                {
                    // Play the video in the overlay
                    PlayVideoInOverlay(itemToSelect.FilePath);
                    return;
                }
                
                // Clear previous selection
                foreach (var item in photoStripItems)
                {
                    item.IsSelected = false;
                }
                
                // Mark this item as selected
                itemToSelect.IsSelected = true;
                
                // Display the selected photo in the main view
                if (itemToSelect.Image != null)
                {
                    // Hide session indicators and navigation
                    if (sessionLoadedIndicator != null)
                        sessionLoadedIndicator.Visibility = Visibility.Collapsed;
                    if (composedImageNavigation != null)
                        composedImageNavigation.Visibility = Visibility.Collapsed;
                    
                    // Load full-size image from file path if available
                    BitmapImage fullSizeImage = null;
                    if (!string.IsNullOrEmpty(itemToSelect.FilePath) && File.Exists(itemToSelect.FilePath))
                    {
                        try
                        {
                            fullSizeImage = new BitmapImage();
                            fullSizeImage.BeginInit();
                            fullSizeImage.CacheOption = BitmapCacheOption.OnLoad;
                            // Load full size, not thumbnail
                            fullSizeImage.UriSource = new Uri(itemToSelect.FilePath);
                            fullSizeImage.EndInit();
                            fullSizeImage.Freeze();
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"SelectPhotoStripItem: Could not load full-size image, using thumbnail: {ex.Message}");
                            fullSizeImage = itemToSelect.Image; // Fall back to thumbnail
                        }
                    }
                    else
                    {
                        // For photos without FilePath, try to get from capturedPhotoPaths
                        if (itemToSelect.PhotoNumber > 0 && itemToSelect.PhotoNumber <= capturedPhotoPaths.Count)
                        {
                            string photoPath = capturedPhotoPaths[itemToSelect.PhotoNumber - 1];
                            if (File.Exists(photoPath))
                            {
                                try
                                {
                                    fullSizeImage = new BitmapImage();
                                    fullSizeImage.BeginInit();
                                    fullSizeImage.CacheOption = BitmapCacheOption.OnLoad;
                                    fullSizeImage.UriSource = new Uri(photoPath);
                                    fullSizeImage.EndInit();
                                    fullSizeImage.Freeze();
                                }
                                catch
                                {
                                    fullSizeImage = itemToSelect.Image; // Fall back to thumbnail
                                }
                            }
                        }
                        else
                        {
                            fullSizeImage = itemToSelect.Image; // Use thumbnail if no path available
                        }
                    }
                    
                    // Display the image with fade-in effect
                    liveViewImage.Opacity = 0;
                    liveViewImage.Source = fullSizeImage ?? itemToSelect.Image;
                    liveViewImage.Visibility = Visibility.Visible;
                    
                    // Animate fade-in
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    liveViewImage.BeginAnimation(System.Windows.Controls.Image.OpacityProperty, fadeIn);
                    
                    // Update status based on item type
                    string statusMessage = "";
                    switch (itemToSelect.ItemType)
                    {
                        case "Composed":
                            statusMessage = "Viewing composed layout";
                            break;
                        case "GIF":
                            statusMessage = "Viewing animated GIF";
                            break;
                        default:
                            int photoIndex = itemToSelect.PhotoNumber;
                            statusMessage = $"Viewing photo {photoIndex} of {photoStripItems.Count(p => !p.IsPlaceholder && p.ItemType == "Photo")}";
                            break;
                    }
                    statusText.Text = statusMessage;
                    
                    Log.Debug($"SelectPhotoStripItem: Displaying {itemToSelect.ItemType} in main view");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SelectPhotoStripItem: Error displaying item: {ex.Message}");
            }
        }
        
        private void AddComposedImageToPhotoStrip(string composedImagePath)
        {
            try
            {
                Log.Debug($"AddComposedImageToPhotoStrip: Starting - Path: {composedImagePath}");
                
                if (!System.IO.File.Exists(composedImagePath))
                {
                    Log.Error($"AddComposedImageToPhotoStrip: File does not exist: {composedImagePath}");
                    return;
                }
                
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 150; // Thumbnail size
                bitmap.UriSource = new Uri(composedImagePath);
                bitmap.EndInit();
                bitmap.Freeze();
                
                var composedItem = new PhotoStripItem
                {
                    Image = bitmap,
                    PhotoNumber = 0, // Special number for composed
                    IsPlaceholder = false,
                    ItemType = "Composed",
                    FilePath = composedImagePath
                };
                
                // Add to the end of photo strip
                photoStripItems.Add(composedItem);
                
                Log.Debug($"AddComposedImageToPhotoStrip: Successfully added composed image to photo strip. Total items: {photoStripItems.Count}");
                
                // Log all items in strip for debugging
                for (int i = 0; i < photoStripItems.Count; i++)
                {
                    var item = photoStripItems[i];
                    Log.Debug($"  Strip item {i}: Type={item.ItemType}, IsPlaceholder={item.IsPlaceholder}, PhotoNumber={item.PhotoNumber}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"AddComposedImageToPhotoStrip: Error adding composed image: {ex.Message}", ex);
            }
        }
        
        private void AddGifToPhotoStrip(string animationPath)
        {
            try
            {
                if (!System.IO.File.Exists(animationPath))
                    return;
                
                BitmapImage bitmap = null;
                string itemType = "GIF";
                
                // Check if it's an MP4 video
                if (animationPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    itemType = "VIDEO";
                    // For MP4, use the first captured photo as thumbnail
                    if (capturedPhotoPaths != null && capturedPhotoPaths.Count > 0)
                    {
                        try
                        {
                            bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 150; // Thumbnail size
                            bitmap.UriSource = new Uri(capturedPhotoPaths[0]); // Use first photo as thumbnail
                            bitmap.EndInit();
                            bitmap.Freeze();
                        }
                        catch
                        {
                            // If first photo fails, try a placeholder
                            bitmap = CreatePlaceholderBitmap("Video");
                        }
                    }
                    else
                    {
                        // Create a simple placeholder for video
                        bitmap = CreatePlaceholderBitmap("Video");
                    }
                }
                else
                {
                    // For GIF, we'll show the first frame as thumbnail
                    try
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 150; // Thumbnail size
                        bitmap.UriSource = new Uri(animationPath);
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                    catch
                    {
                        // If GIF loading fails, use first photo
                        if (capturedPhotoPaths != null && capturedPhotoPaths.Count > 0)
                        {
                            bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = 150;
                            bitmap.UriSource = new Uri(capturedPhotoPaths[0]);
                            bitmap.EndInit();
                            bitmap.Freeze();
                        }
                    }
                }
                
                if (bitmap != null)
                {
                    var animationItem = new PhotoStripItem
                    {
                        Image = bitmap,
                        PhotoNumber = 0, // Special number for animation
                        IsPlaceholder = false,
                        ItemType = itemType,
                        FilePath = animationPath
                    };
                    
                    // Add to the end of photo strip
                    photoStripItems.Add(animationItem);
                    
                    Log.Debug($"AddGifToPhotoStrip: Added {itemType} to photo strip");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"AddGifToPhotoStrip: Error adding animation: {ex.Message}");
            }
        }
        
        private void PlayVideoInOverlay(string videoPath)
        {
            try
            {
                if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                    return;
                
                // Show the overlay
                if (videoPlayerOverlay != null)
                {
                    videoPlayerOverlay.Visibility = Visibility.Visible;
                    
                    // Load and play the video
                    if (videoPlayer != null)
                    {
                        videoPlayer.Source = new Uri(videoPath);
                        videoPlayer.Play();
                        
                        // Update play/pause button
                        if (playPauseButton != null)
                            playPauseButton.Content = "⏸️";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PlayVideoInOverlay: Error playing video: {ex.Message}");
            }
        }
        
        private void CloseVideoPlayer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (videoPlayer != null)
                {
                    videoPlayer.Stop();
                    videoPlayer.Source = null;
                }
                
                if (videoPlayerOverlay != null)
                    videoPlayerOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error($"CloseVideoPlayer_Click: Error: {ex.Message}");
            }
        }
        
        private void VideoPlayerOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Close overlay when clicking outside the video player
            if (e.OriginalSource == sender)
            {
                CloseVideoPlayer_Click(null, null);
            }
        }
        
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (videoPlayer != null)
                {
                    if (videoPlayer.Position < videoPlayer.NaturalDuration.TimeSpan && 
                        videoPlayer.NaturalDuration.HasTimeSpan)
                    {
                        // Toggle play/pause
                        if (playPauseButton?.Content?.ToString() == "▶️")
                        {
                            videoPlayer.Play();
                            playPauseButton.Content = "⏸️";
                        }
                        else
                        {
                            videoPlayer.Pause();
                            if (playPauseButton != null)
                                playPauseButton.Content = "▶️";
                        }
                    }
                    else
                    {
                        // Restart from beginning
                        videoPlayer.Position = TimeSpan.Zero;
                        videoPlayer.Play();
                        if (playPauseButton != null)
                            playPauseButton.Content = "⏸️";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PlayPauseButton_Click: Error: {ex.Message}");
            }
        }
        
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (videoPlayer != null)
                {
                    videoPlayer.Position = TimeSpan.Zero;
                    videoPlayer.Play();
                    
                    if (playPauseButton != null)
                        playPauseButton.Content = "⏸️";
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RestartButton_Click: Error: {ex.Message}");
            }
        }
        
        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Loop the video
                if (videoPlayer != null)
                {
                    videoPlayer.Position = TimeSpan.Zero;
                    videoPlayer.Play();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"VideoPlayer_MediaEnded: Error: {ex.Message}");
            }
        }
        
        private BitmapImage CreatePlaceholderBitmap(string text)
        {
            try
            {
                // Create a simple text-based placeholder
                var renderTarget = new RenderTargetBitmap(150, 150, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawRectangle(System.Windows.Media.Brushes.DarkGray, null, new Rect(0, 0, 150, 150));
                    var formattedText = new FormattedText(
                        text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Arial"),
                        24,
                        System.Windows.Media.Brushes.White,
                        96);
                    context.DrawText(formattedText, new System.Windows.Point(75 - formattedText.Width / 2, 75 - formattedText.Height / 2));
                }
                renderTarget.Render(visual);
                
                // Convert to BitmapImage
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderTarget));
                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    stream.Position = 0;
                    
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
        
        #region Database Session Management
        
        private void CreateDatabaseSession()
        {
            try
            {
                if (currentEvent == null || currentTemplate == null)
                {
                    Log.Debug("CreateDatabaseSession: Cannot create session - missing event or template");
                    return;
                }
                
                if (currentDatabaseSessionId.HasValue)
                {
                    Log.Debug("CreateDatabaseSession: Database session already exists");
                    return;
                }
                
                string sessionName = $"{currentEvent.Name}_{currentTemplate.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
                currentDatabaseSessionId = database.CreatePhotoSession(currentEvent.Id, currentTemplate.Id, sessionName);
                currentSessionGuid = System.Guid.NewGuid().ToString();
                currentSessionPhotoIds.Clear();
                
                Log.Debug($"CreateDatabaseSession: Created session ID {currentDatabaseSessionId} with GUID {currentSessionGuid}");
            }
            catch (Exception ex)
            {
                Log.Error($"CreateDatabaseSession: Failed to create database session: {ex.Message}");
            }
        }
        
        private void SavePhotoToDatabase(string filePath, int sequenceNumber, string photoType = "Original")
        {
            try
            {
                if (!currentDatabaseSessionId.HasValue)
                {
                    Log.Debug("SavePhotoToDatabase: No active session, creating one");
                    CreateDatabaseSession();
                }
                
                if (!currentDatabaseSessionId.HasValue)
                {
                    Log.Error("SavePhotoToDatabase: Failed to create session");
                    return;
                }
                
                var photoData = new PhotoData
                {
                    SessionId = currentDatabaseSessionId.Value,
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileSize = new FileInfo(filePath).Length,
                    PhotoType = photoType,
                    SequenceNumber = sequenceNumber,
                    CreatedDate = DateTime.Now,
                    ThumbnailPath = GenerateThumbnailPath(filePath),
                    IsActive = true
                };
                
                int photoId = database.SavePhoto(photoData);
                currentSessionPhotoIds.Add(photoId);
                
                Log.Debug($"SavePhotoToDatabase: Saved photo {photoId} - {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Log.Error($"SavePhotoToDatabase: Failed to save photo: {ex.Message}");
            }
        }
        
        private void SaveComposedImageToDatabase(string filePath, string outputFormat = "4x6")
        {
            try
            {
                if (!currentDatabaseSessionId.HasValue)
                {
                    Log.Error("SaveComposedImageToDatabase: No active session");
                    return;
                }
                
                var composedImageData = new ComposedImageData
                {
                    SessionId = currentDatabaseSessionId.Value,
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileSize = new FileInfo(filePath).Length,
                    TemplateId = currentTemplate.Id,
                    OutputFormat = outputFormat,
                    CreatedDate = DateTime.Now,
                    ThumbnailPath = GenerateThumbnailForComposedImage(filePath),
                    IsActive = true
                };
                
                int composedImageId = database.SaveComposedImage(composedImageData);
                
                // Link this composed image to all photos in the current session
                if (currentSessionPhotoIds.Count > 0)
                {
                    database.LinkPhotosToComposedImage(composedImageId, currentSessionPhotoIds);
                    Log.Debug($"SaveComposedImageToDatabase: Linked composed image {composedImageId} to {currentSessionPhotoIds.Count} photos");
                }
                
                Log.Debug($"SaveComposedImageToDatabase: Saved composed image {composedImageId} - {Path.GetFileName(filePath)}");
                
                // Generate GIF in background without blocking (fire and forget)
                if (Properties.Settings.Default.EnableGifGeneration && capturedPhotoPaths.Count > 1)
                {
                    Log.Debug("SaveComposedImageToDatabase: Starting GIF generation in background");
                    Task.Run(() =>
                    {
                        try
                        {
                            GenerateAndSaveSessionGif();
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Background GIF generation failed: {ex.Message}");
                        }
                    });
                }
                else
                {
                    Log.Debug("SaveComposedImageToDatabase: GIF generation disabled or insufficient photos");
                }
                
                Log.Debug("SaveComposedImageToDatabase: Method completing");
            }
            catch (Exception ex)
            {
                Log.Error($"SaveComposedImageToDatabase: Failed to save composed image: {ex.Message}");
            }
        }
        
        private string GenerateThumbnailPath(string originalPath)
        {
            string directory = Path.GetDirectoryName(originalPath);
            string filename = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);
            return Path.Combine(directory, "Thumbnails", $"{filename}_thumb{extension}");
        }
        
        private string GenerateThumbnailForComposedImage(string composedImagePath)
        {
            try
            {
                string thumbnailPath = GenerateThumbnailPath(composedImagePath);
                string thumbnailDir = Path.GetDirectoryName(thumbnailPath);
                
                // Create thumbnail directory if it doesn't exist
                if (!Directory.Exists(thumbnailDir))
                {
                    Directory.CreateDirectory(thumbnailDir);
                }
                
                // Generate thumbnail from composed image (200x200 pixels)
                var originalImage = new BitmapImage(new Uri(composedImagePath));
                var thumbnail = CreateThumbnail(originalImage, 200, 200);
                SaveThumbnail(thumbnail, thumbnailPath);
                Log.Debug($"GenerateThumbnailForComposedImage: Created thumbnail {thumbnailPath}");
                
                return thumbnailPath;
            }
            catch (Exception ex)
            {
                Log.Error($"GenerateThumbnailForComposedImage: Failed to create thumbnail: {ex.Message}");
                return null;
            }
        }
        
        private BitmapImage CreateThumbnail(BitmapImage source, int maxWidth, int maxHeight)
        {
            // Calculate thumbnail size maintaining aspect ratio
            double sourceAspect = (double)source.PixelWidth / source.PixelHeight;
            int thumbWidth, thumbHeight;
            
            if (sourceAspect > 1)
            {
                thumbWidth = maxWidth;
                thumbHeight = (int)(maxWidth / sourceAspect);
            }
            else
            {
                thumbHeight = maxHeight;
                thumbWidth = (int)(maxHeight * sourceAspect);
            }
            
            // Create thumbnail
            var thumbnail = new TransformedBitmap(source, new ScaleTransform(
                (double)thumbWidth / source.PixelWidth,
                (double)thumbHeight / source.PixelHeight));
            
            // Convert to BitmapImage
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;
                
                var result = new BitmapImage();
                result.BeginInit();
                result.StreamSource = stream;
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.EndInit();
                result.Freeze();
                
                return result;
            }
        }
        
        private void SaveThumbnail(BitmapImage thumbnail, string path)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            
            using (var fileStream = new FileStream(path, FileMode.Create))
            {
                encoder.Save(fileStream);
            }
        }
        
        private static readonly object gifGenerationLock = new object();
        private static bool isGeneratingGif = false;
        
        private void GenerateAndSaveSessionGif()
        {
            // Prevent concurrent executions
            lock (gifGenerationLock)
            {
                if (isGeneratingGif)
                {
                    Log.Debug("GenerateAndSaveSessionGif: Already generating, skipping duplicate call");
                    return;
                }
                isGeneratingGif = true;
            }
            
            try
            {
                if (capturedPhotoPaths == null || capturedPhotoPaths.Count < 2)
                {
                    Log.Debug("GenerateAndSaveSessionGif: Not enough photos for animation");
                    return;
                }
                
                if (!isGeneratingGif) return; // Double check after lock
                Log.Debug($"GenerateAndSaveSessionGif: Starting MP4 video generation with {capturedPhotoPaths.Count} photos");
                
                // Create output path for MP4
                string outputDir = Path.Combine(FolderForPhotos, "Animations");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"); // Added milliseconds to prevent conflicts
                string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8); // Extra uniqueness
                string videoPath = Path.Combine(outputDir, $"Animation_{timestamp}_{uniqueId}.mp4");
                
                // Try MP4 generation first (much faster and better quality)
                try
                {
                    string generatedPath = VideoGenerationService.GenerateLoopingMP4(
                        capturedPhotoPaths, 
                        videoPath, 
                        400); // 400ms per frame for smoother GIF-like animation
                    
                    if (!string.IsNullOrEmpty(generatedPath) && File.Exists(generatedPath))
                    {
                        Log.Debug($"GenerateAndSaveSessionGif: MP4 video created at {generatedPath}");
                        
                        // Save to database
                        SaveAnimationToDatabase(generatedPath, "MP4");
                        
                        // Quick UI update
                        Dispatcher.BeginInvoke(new Action(() => 
                        {
                            try
                            {
                                AddGifToPhotoStrip(generatedPath);
                                Log.Debug("GenerateAndSaveSessionGif: Added video to UI");
                            }
                            catch { }
                        }));
                        
                        return; // Success with MP4
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"GenerateAndSaveSessionGif: MP4 generation failed: {ex.Message}");
                }
                
                // Fallback to GIF if MP4 fails (no FFmpeg)
                Log.Debug("GenerateAndSaveSessionGif: Falling back to GIF generation");
                string gifPath = Path.Combine(outputDir, $"Animation_{timestamp}_{uniqueId}.gif");
                
                try
                {
                    string generatedPath = GifGenerationService.GenerateSimpleAnimatedGif(
                        capturedPhotoPaths, 
                        gifPath, 
                        50,  // Fast frame rate
                        400, // Small width
                        300); // Small height
                    
                    if (!string.IsNullOrEmpty(generatedPath) && File.Exists(generatedPath))
                    {
                        Log.Debug($"GenerateAndSaveSessionGif: Fallback GIF created at {generatedPath}");
                        
                        // Save to database
                        SaveAnimationToDatabase(generatedPath, "GIF");
                        
                        Dispatcher.BeginInvoke(new Action(() => 
                        {
                            try
                            {
                                AddGifToPhotoStrip(generatedPath);
                            }
                            catch { }
                        }));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"GenerateAndSaveSessionGif: GIF fallback also failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GenerateAndSaveSessionGif: Error: {ex.Message}");
            }
            finally
            {
                // Reset the flag
                lock (gifGenerationLock)
                {
                    isGeneratingGif = false;
                }
            }
        }
        
        private void SaveAnimationToDatabase(string animationPath, string animationType)
        {
            try
            {
                if (string.IsNullOrEmpty(animationPath) || !File.Exists(animationPath))
                {
                    Log.Debug("SaveAnimationToDatabase: Invalid animation path");
                    return;
                }
                
                if (database == null)
                {
                    Log.Debug("SaveAnimationToDatabase: Database not initialized");
                    return;
                }
                
                // Generate thumbnail from first photo
                string thumbnailPath = null;
                if (capturedPhotoPaths != null && capturedPhotoPaths.Count > 0)
                {
                    try
                    {
                        thumbnailPath = GenerateThumbnailPath(capturedPhotoPaths[0]);
                        if (!File.Exists(thumbnailPath))
                        {
                            // Create thumbnail from first photo
                            using (var image = System.Drawing.Image.FromFile(capturedPhotoPaths[0]))
                            {
                                int thumbWidth = 150;
                                int thumbHeight = (int)(image.Height * (150.0 / image.Width));
                                using (var thumbnail = image.GetThumbnailImage(thumbWidth, thumbHeight, null, IntPtr.Zero))
                                {
                                    thumbnail.Save(thumbnailPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"SaveAnimationToDatabase: Failed to create thumbnail: {ex.Message}");
                    }
                }
                
                // Save to database as a composed image with animation type
                if (!currentDatabaseSessionId.HasValue)
                {
                    Log.Debug("SaveAnimationToDatabase: No active session");
                    return;
                }
                
                var animationData = new ComposedImageData
                {
                    SessionId = currentDatabaseSessionId.Value,
                    FilePath = animationPath,
                    FileName = Path.GetFileName(animationPath),
                    FileSize = new FileInfo(animationPath).Length,
                    TemplateId = currentTemplate?.Id ?? 0,
                    OutputFormat = animationType,  // "MP4" or "GIF"
                    CreatedDate = DateTime.Now,
                    ThumbnailPath = thumbnailPath ?? animationPath,
                    IsActive = true
                };
                
                int animationId = database.SaveComposedImage(animationData);
                
                // Link to the photos in this session
                if (animationId > 0 && currentSessionPhotoIds.Count > 0)
                {
                    database.LinkPhotosToComposedImage(animationId, currentSessionPhotoIds);
                    Log.Debug($"SaveAnimationToDatabase: Saved {animationType} animation ID {animationId} linked to {currentSessionPhotoIds.Count} photos");
                }
                else if (animationId <= 0)
                {
                    Log.Error($"SaveAnimationToDatabase: Failed to save to database - returned ID {animationId}");
                }
                
                Log.Debug($"SaveAnimationToDatabase: Animation saved to database - {Path.GetFileName(animationPath)}");
                
                // Debug: Check if it's actually in the database
                try
                {
                    var recentComposed = database.GetRecentComposedImages(10);
                    if (recentComposed != null && recentComposed.Count > 0)
                    {
                        Log.Debug($"SaveAnimationToDatabase: Recent composed images in DB:");
                        foreach (var img in recentComposed.Take(3))
                        {
                            Log.Debug($"  - ID:{img.Id} Type:{img.OutputFormat} File:{img.FileName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"SaveAnimationToDatabase: Could not query recent images: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SaveAnimationToDatabase: Failed to save animation: {ex.Message}");
            }
        }
        
        private void EndDatabaseSession()
        {
            try
            {
                if (currentDatabaseSessionId.HasValue)
                {
                    // Only end the session if it has photos
                    if (currentSessionPhotoIds.Count > 0)
                    {
                        database.EndPhotoSession(currentDatabaseSessionId.Value);
                        Log.Debug($"EndDatabaseSession: Ended session {currentDatabaseSessionId} with {currentSessionPhotoIds.Count} photos");
                    }
                    else
                    {
                        // Delete empty session from database
                        Log.Debug($"EndDatabaseSession: Deleting empty session {currentDatabaseSessionId}");
                        database.DeletePhotoSession(currentDatabaseSessionId.Value);
                    }
                    
                    currentDatabaseSessionId = null;
                    currentSessionGuid = null;
                    currentSessionPhotoIds.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EndDatabaseSession: Failed to end session: {ex.Message}");
            }
        }
        
        #endregion
        
        #endregion

        #region Fullscreen Filter Overlay Event Handlers

        private void CloseFilterOverlay_Click(object sender, RoutedEventArgs e)
        {
            filterSelectionOverlay.Visibility = Visibility.Collapsed;
        }

        private void FilterOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close overlay if clicked outside the content
            if (e.OriginalSource == sender)
            {
                filterSelectionOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void FullscreenFilterItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag != null)
            {
                // Update selection state for filter items
                foreach (var item in fullscreenFilterControl.Items)
                {
                    var filterItem = item as Photobooth.Controls.FilterItem;
                    if (filterItem != null)
                    {
                        filterItem.IsSelected = (item == border.Tag);
                    }
                }
                
                // Refresh the display
                fullscreenFilterControl.Items.Refresh();
            }
        }

        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            // Apply the selected filter
            foreach (var item in fullscreenFilterControl.Items)
            {
                var filterItem = item as Photobooth.Controls.FilterItem;
                if (filterItem != null && filterItem.IsSelected)
                {
                    // Apply filter logic here
                    ApplySelectedFilter(filterItem);
                    break;
                }
            }
            
            // Close overlay
            filterSelectionOverlay.Visibility = Visibility.Collapsed;
        }

        private void ApplySelectedFilter(dynamic filterItem)
        {
            // Implementation for applying the selected filter
            Log.Debug($"Applying filter: {filterItem.Name}");
            // Add your filter application logic here
        }

        #endregion

        #region Fullscreen Retake Overlay Event Handlers

        private void CloseRetakeOverlay_Click(object sender, RoutedEventArgs e)
        {
            retakeReviewOverlay.Visibility = Visibility.Collapsed;
            retakeReviewTimer?.Stop();
        }

        private void RetakeSelected_Click(object sender, RoutedEventArgs e)
        {
            // Get list of photos marked for retake
            var photosToRetake = new List<int>();
            
            foreach (var item in retakePhotoGrid.Items)
            {
                var photo = item as RetakePhotoItem;
                if (photo != null && photo.MarkedForRetake)
                {
                    photosToRetake.Add(photo.PhotoIndex);
                }
            }
            
            if (photosToRetake.Count > 0)
            {
                Log.Debug($"Retaking {photosToRetake.Count} photos");
                
                // Hide overlay
                retakeReviewOverlay.Visibility = Visibility.Collapsed;
                retakeReviewTimer?.Stop();
                
                // Start retake process for selected photos
                StartRetakeProcess(photosToRetake);
            }
            else
            {
                MessageBox.Show("Please select at least one photo to retake.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void StartRetakeProcess(List<int> photoIndices)
        {
            // Implementation for retaking specific photos
            // This would restart the capture process for the selected photo indices
            Log.Debug($"Starting retake process for photos: {string.Join(", ", photoIndices)}");
            
            // Store which photos need retaking
            photosToRetake = photoIndices;
            currentRetakeIndex = 0;
            
            // Start retaking the first photo
            if (photosToRetake.Count > 0)
            {
                StartRetakeCapture(photosToRetake[0]);
            }
        }

        private List<int> photosToRetake = new List<int>();
        private int currentRetakeIndex = 0;

        private void StartRetakeCapture(int photoIndex)
        {
            // Start capture for specific photo index
            statusText.Text = $"Retaking Photo {photoIndex + 1}";
            
            // Show countdown and capture
            StartCountdown();
        }
        
        private void UpdateStatusText(string text)
        {
            statusText.Text = text;
        }
        
        private void ShowCountdown()
        {
            StartCountdown();
        }
        
        #endregion
        
        #region Post-Session Filter Overlay Events
        
        private void PostSessionFilterOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Don't close when clicking inside the content
            if (e.OriginalSource == sender)
            {
                // Optional: close overlay when clicking outside
                // postSessionFilterOverlay.Visibility = Visibility.Collapsed;
            }
        }
        
        private void ClosePostSessionFilterOverlay_Click(object sender, RoutedEventArgs e)
        {
            postSessionFilterOverlay.Visibility = Visibility.Collapsed;
            // Proceed without filter
            ProcessTemplateWithPhotosInternal(FilterType.None);
        }
        
        private void PostSessionFilterItem_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border != null)
            {
                // Update selection state for filter items
                foreach (var item in postSessionFilterControl.Items)
                {
                    var filterItem = item as Photobooth.Controls.FilterItem;
                    if (filterItem != null)
                    {
                        filterItem.IsSelected = (item == border.Tag);
                    }
                }
                
                // Refresh the display
                postSessionFilterControl.Items.Refresh();
                
                // Show larger preview of selected filter in live view area
                var selectedItem = border.Tag as Photobooth.Controls.FilterItem;
                if (selectedItem != null)
                {
                    ShowLargeFilterPreview(selectedItem);
                }
            }
        }
        
        private void ShowLargeFilterPreview(Photobooth.Controls.FilterItem filterItem)
        {
            // Show the filter preview in the main live view area
            Log.Debug($"Selected filter: {filterItem.Name}");
            
            // Display the filtered preview in the live view
            if (filterItem.PreviewImage != null)
            {
                liveViewImage.Source = filterItem.PreviewImage;
                statusText.Text = $"Preview: {filterItem.Name} filter";
            }
            
            // If it's the "No Filter" option, show the original image
            if (filterItem.FilterType == FilterType.None && capturedPhotoPaths.Count > 0)
            {
                var originalImage = new BitmapImage(new Uri(capturedPhotoPaths[0]));
                liveViewImage.Source = originalImage;
                statusText.Text = "Preview: Original (No Filter)";
            }
        }
        
        private void ApplyPostSessionFilter_Click(object sender, RoutedEventArgs e)
        {
            // Get selected filter
            FilterType selectedFilter = FilterType.None;
            
            foreach (var item in postSessionFilterControl.Items)
            {
                var filterItem = item as Photobooth.Controls.FilterItem;
                if (filterItem != null && filterItem.IsSelected)
                {
                    selectedFilter = filterItem.FilterType;
                    break;
                }
            }
            
            // Hide overlay immediately so user sees action was taken
            postSessionFilterOverlay.Visibility = Visibility.Collapsed;
            
            // Process with selected filter
            ProcessTemplateWithPhotosInternal(selectedFilter);
        }
        
        private void SkipPostSessionFilter_Click(object sender, RoutedEventArgs e)
        {
            // Hide overlay
            postSessionFilterOverlay.Visibility = Visibility.Collapsed;
            
            // Process without filter
            ProcessTemplateWithPhotosInternal(FilterType.None);
        }

        #endregion
    }
}