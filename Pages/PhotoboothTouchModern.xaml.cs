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
using System.Diagnostics;
using System.Windows.Threading;
using Path = System.IO.Path;
using System.Drawing;
using System.Windows.Interop;
using Photobooth.Services;
using Photobooth.Database;
using Photobooth.Controls;
using System.ComponentModel;
using Photobooth.Models;
using System.Data.SQLite;

namespace Photobooth.Pages
{
    public partial class PhotoboothTouchModern : Page, INotifyPropertyChanged
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
        
        // Event/Template service
        private EventTemplateService eventTemplateService;
        private TemplateDatabase database; // Still needed for some operations
        
        // Filter service - using hybrid Magick.NET/GDI+ service for best performance
        private PhotoFilterServiceHybrid filterService;
        
        // Video/Photo settings management
        private class SavedPhotoSettings
        {
            public string ISO { get; set; }
            public string Aperture { get; set; }
            public string ShutterSpeed { get; set; }
            public string WhiteBalance { get; set; }
            public string FocusMode { get; set; }
            public string ExposureCompensation { get; set; }
        }
        private SavedPhotoSettings savedPhotoSettings;
        
        // Printing service
        private PrintingService printingService;
        
        // Database operations service
        private DatabaseOperations databaseOperations;
        
        // SMS phone pad tracking
        private string _smsPhoneNumber = "+1";
        
        // Cloud sharing services
        private SessionManager sessionManager;
        private SimpleShareService shareService;
        
        // Flipbook service
        private FlipbookService flipbookService;
        private bool isRecordingFlipbook = false;
        private DispatcherTimer flipbookTimer;
        private int flipbookElapsedSeconds = 0;
        // Removed unused fields - now managed by services
        
        // Refactored services
        
        // Video and Boomerang modules
        private VideoRecordingService videoService;
        private BoomerangService boomerangService;
        private PhotoboothModulesConfig modulesConfig;
        private bool isRecording = false;
        private bool isCapturingBoomerang = false;
        public bool IsRecording => isRecording;
        public bool IsCapturingBoomerang => isCapturingBoomerang;
        private SharingOperations sharingOperations;
        private CameraOperations cameraOperations;
        private PhotoProcessingOperations photoProcessingOperations;
        private PinLockService pinLockService;
        private PhotoCaptureService photoCaptureService;

        public PhotoboothTouchModern()
        {
            InitializeComponent();
            
            // Use singleton camera manager - don't create new instance
            // Event subscriptions moved to Loaded event to prevent duplicates

            // Initialize services
            photoboothService = new PhotoboothService();
            database = new TemplateDatabase();
            filterService = new PhotoFilterServiceHybrid();
            
            // Initialize live view timer
            liveViewTimer = new DispatcherTimer();
            liveViewTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 FPS
            liveViewTimer.Tick += LiveViewTimer_Tick;
            
            // Initialize cloud sharing services
            sessionManager = new SessionManager();
            shareService = new SimpleShareService();
            
            // Initialize refactored services
            databaseOperations = new DatabaseOperations();
            eventTemplateService = new EventTemplateService();
            photoCaptureService = new PhotoCaptureService(databaseOperations);
            printingService = new PrintingService();
            sharingOperations = new SharingOperations(this);
            cameraOperations = new CameraOperations(this);
            photoProcessingOperations = new PhotoProcessingOperations(this);
            // TODO: Update to use new PinLockService singleton pattern
            // pinLockService = new PinLockService(this);
            
            // Initialize video and boomerang modules
            modulesConfig = PhotoboothModulesConfig.Instance;
            videoService = new VideoRecordingService();
            boomerangService = new BoomerangService();
            
            // Printer monitoring now handled by PrintingService

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
            CloseOverlay(cameraSettingsOverlay, "camera settings");
            CloseOverlay(eventSelectionOverlay, "event selection");
            CloseOverlay(retakeReviewOverlay, "retake review");
            CloseOverlay(filterSelectionOverlay, "filter selection");
            CloseOverlay(galleryOverlay, "gallery");
            CloseOverlay(postSessionFilterOverlay, "post session filter");
            CloseOverlay(pinEntryOverlay, "PIN entry");
            CloseOverlay(videoPlayerOverlay, "video player");
            CloseOverlay(modernSettingsOverlay, "modern settings");
        }
        
        private void CloseOverlay(FrameworkElement overlay, string overlayName)
        {
            if (overlay != null && overlay.Visibility == Visibility.Visible)
            {
                overlay.Visibility = Visibility.Collapsed;
                Log.Debug($"CloseAllOverlays: Closed {overlayName} overlay");
            }
        }
        
        private void ShowOverlay(FrameworkElement overlay, string overlayName = null)
        {
            if (overlay != null)
            {
                overlay.Visibility = Visibility.Visible;
                if (!string.IsNullOrEmpty(overlayName))
                {
                    Log.Debug($"ShowOverlay: Showing {overlayName} overlay");
                }
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
                printingService.PrinterStatusChanged -= OnPrinterStatusChanged;
                printingService.StopMonitoring();
                Log.Debug("PhotoboothTouch_Unloaded: Unsubscribed from printer events and stopped monitoring");
                
                // Stop any ongoing photo sequence
                StopPhotoSequence();
                
                // Use singleton manager to cleanup without destroying session
                CameraSessionManager.Instance.CleanupCameraForScreenChange();
                
                // Clean up timers
                CleanupTimer(liveViewTimer, LiveViewTimer_Tick);
                CleanupTimer(countdownTimer, CountdownTimer_Tick);
                CleanupTimer(retakeReviewTimer, RetakeReviewTimer_Tick);
                
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
            liveViewTimer = CreateTimer(LiveViewTimer_Tick, TimeSpan.FromMilliseconds(1000 / 30));

            // Countdown timer (1 second intervals)
            countdownTimer = CreateTimer(CountdownTimer_Tick, TimeSpan.FromSeconds(1));
            
            // Retake review timer (1 second intervals)
            retakeReviewTimer = CreateTimer(RetakeReviewTimer_Tick, TimeSpan.FromSeconds(1));
            
            // Auto-clear timer (1 second intervals)
            autoClearTimer = CreateTimer(AutoClearTimer_Tick, TimeSpan.FromSeconds(1));
        }
        
        private DispatcherTimer CreateTimer(EventHandler tickHandler, TimeSpan interval)
        {
            var timer = new DispatcherTimer();
            timer.Tick += tickHandler;
            timer.Interval = interval;
            return timer;
        }
        
        private void CleanupTimer(DispatcherTimer timer, EventHandler tickHandler)
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Tick -= tickHandler;
            }
        }
        
        private BitmapImage CreateBitmapFromStream(MemoryStream stream)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        
        private BitmapImage CreateBitmapFromUri(string path, int? decodePixelWidth = null)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            if (decodePixelWidth.HasValue)
                bitmap.DecodePixelWidth = decodePixelWidth.Value;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void PhotoboothTouchModern_Loaded(object sender, RoutedEventArgs e)
        {
            Log.Debug("PhotoboothTouch_Loaded: Page loaded, initializing camera");
            
            // Custom UI layout removed - using default UI only
            
            // Subscribe to camera events (will be unsubscribed in Unloaded)
            DeviceManager.CameraSelected += DeviceManager_CameraSelected;
            DeviceManager.CameraConnected += DeviceManager_CameraConnected;
            DeviceManager.PhotoCaptured += DeviceManager_PhotoCaptured;
            DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;
            
            // Initialize video and boomerang services with camera
            if (DeviceManager.SelectedCameraDevice != null)
            {
                videoService.Initialize(DeviceManager.SelectedCameraDevice);
            }
            
            // Update button visibility based on settings
            UpdateModuleButtonVisibility();
            Log.Debug("PhotoboothTouch_Loaded: Subscribed to camera events");
            
            // Subscribe to printer status events
            printingService.PrinterStatusChanged += OnPrinterStatusChanged;
            printingService.StartMonitoring();
            Log.Debug("PhotoboothTouch_Loaded: Started printer monitoring and subscribed to status events");
            
            // Prepare camera for use using singleton manager
            CameraSessionManager.Instance.PrepareCameraForUse();
            
            // Reset state when page loads
            isCapturing = false;
            CloseOverlay(countdownOverlay, "countdown");
            
            // Update sharing buttons visibility
            UpdateSharingButtonsVisibility();
            
            // Start sync status monitoring
            StartSyncStatusMonitoring();
            
            // Show start button initially if we have a template selected OR if we have an event with templates
            if (startButtonOverlay != null)
            {
                // Show START button if:
                // 1. Template is already selected, OR
                // 2. Event is selected with available templates (for template selection)
                bool shouldShowStartButton = (currentTemplate != null) || 
                    (currentEvent != null && eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 0);
                
                startButtonOverlay.Visibility = shouldShowStartButton ? Visibility.Visible : Visibility.Collapsed;
                Log.Debug($"START BUTTON VISIBILITY: shouldShow={shouldShowStartButton}, currentTemplate={currentTemplate?.Name}, currentEvent={currentEvent?.Name}, templateCount={eventTemplateService.AvailableTemplates?.Count ?? 0}");
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
                
                // Critical controls disabled by PinLockService
                
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
            
            // Initialize cloud sync status
            UpdateCloudSyncStatus();
            
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
                            Log.Debug($"PhotoboothTouch_Loaded: About to start timer, IsEnabled={liveViewTimer?.IsEnabled}, Interval={liveViewTimer?.Interval}");
                            liveViewTimer.Start();
                            Log.Debug($"PhotoboothTouch_Loaded: Timer started, IsEnabled={liveViewTimer.IsEnabled}");
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
                UpdateStatusText("Camera connection failed");
                Log.Error("PhotoboothTouch: Camera connection failed", ex);
            }
        }
        
        private void UpdateStatusText(string message)
        {
            // Only show status messages if the setting is enabled
            if (Properties.Settings.Default.ShowSessionPrompts)
            {
                statusText.Text = message;
            }
            else
            {
                statusText.Text = "";
            }
        }
        
        private void UpdateCountdownStatusText(string message)
        {
            // Only show countdown status text if ShowCountdown setting is enabled
            if (Properties.Settings.Default.ShowCountdown)
            {
                statusText.Text = message;
            }
            else
            {
                // Don't show countdown text, but still allow other status messages
                statusText.Text = "";
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
                UpdateStatusText($"Event: {currentEvent.Name} - Template: {currentTemplate.Name}");
                
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
                eventTemplateService.LoadAvailableTemplates(currentEvent.Id);
                
                Log.Debug($"TEMPLATE DEBUG: Loaded {eventTemplateService.AvailableTemplates?.Count ?? 0} templates for event {currentEvent.Name}");
                
                if (eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count == 1)
                {
                    // Only one template - auto-select it
                    currentTemplate = eventTemplateService.AvailableTemplates[0];
                    totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                    currentPhotoIndex = 0;
                    UpdatePhotoStripPlaceholders();
                    UpdateStatusText($"Event: {currentEvent.Name} - Template: {currentTemplate.Name}");
                    
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
                else if (eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 1)
                {
                    // Multiple templates - user will select when pressing START
                    UpdateStatusText($"Event: {currentEvent.Name} - Touch START to select template");
                    
                    // Update folder path for event
                    string eventFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                        "Photobooth", SanitizeFileName(currentEvent.Name));
                    if (!Directory.Exists(eventFolder))
                    {
                        Directory.CreateDirectory(eventFolder);
                    }
                    FolderForPhotos = eventFolder;
                    
                    Log.Debug($"Event has {eventTemplateService.AvailableTemplates.Count} templates available");
                }
                else
                {
                    // No templates
                    UpdateStatusText($"Event: {currentEvent.Name} - No templates available");
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
                    UpdateStatusText($"Event: {currentEvent.Name} - Ready for photo {currentPhotoIndex + 1} of {totalPhotosNeeded}");
                }
                else
                {
                    UpdateStatusText("Camera ready - Touch START to begin");
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
                    UpdateStatusText($"Event: {currentEvent.Name} - Please connect a camera");
                }
                else
                {
                    UpdateStatusText("Please connect a camera");
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
            
            Log.Debug($"START BUTTON DEBUG: currentEvent={currentEvent?.Name}, currentTemplate={currentTemplate?.Name}, currentPhotoIndex={currentPhotoIndex}");
            
            if (DeviceManager.SelectedCameraDevice == null)
            {
                UpdateStatusText("No camera connected");
                return;
            }

            if (isCapturing)
                return;
            
            // Hide the centered start button
            if (startButtonOverlay != null)
            {
                CloseOverlay(startButtonOverlay, "start button");
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
                UpdateStatusText($"Please wait {remainingTime / 1000 + 1} seconds between photos");
                
                // Start a timer to update the message
                Task.Delay(remainingTime).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!isCapturing)
                        {
                            UpdateStatusText("Touch START to take another photo");
                        }
                    });
                });
                return;
            }

            // Check if we need to select a template for this session
            // Check if we need to select a template (regardless of currentPhotoIndex)
            if (currentTemplate == null)
            {
                Log.Debug($"START BUTTON DEBUG: No template selected, checking available templates. Count: {eventTemplateService.AvailableTemplates?.Count ?? 0}");
                
                // Ensure templates are loaded for current event
                if (currentEvent != null)
                {
                    Log.Debug($"START BUTTON DEBUG: Loading templates for event: {currentEvent.Name}");
                    eventTemplateService.LoadAvailableTemplates(currentEvent.Id);
                    Log.Debug($"START BUTTON DEBUG: After loading - template count: {eventTemplateService.AvailableTemplates?.Count ?? 0}");
                }
                
                // No template selected yet - check if we need to show selection
                if (eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 1)
                {
                    Log.Debug("START BUTTON DEBUG: Multiple templates available - showing selection");
                    // Multiple templates available - show selection
                    ShowTemplateSelectionForSession();
                    return;
                }
                else if (eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count == 1)
                {
                    // Single template - use it automatically
                    Log.Debug("START BUTTON DEBUG: Single template available - auto-selecting");
                    currentTemplate = eventTemplateService.AvailableTemplates[0];
                    totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                    currentPhotoIndex = 0; // Reset photo index for new session
                    UpdatePhotoStripPlaceholders();
                }
                else
                {
                    // No templates available or AvailableTemplates is null/empty
                    Log.Debug("START BUTTON DEBUG: No templates available, cannot start photo sequence");
                    UpdateStatusText("No templates available for this event");
                    
                    // Show start button again
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Visible;
                    }
                    return;
                }
            }
            else
            {
                Log.Debug($"START BUTTON DEBUG: Template already selected: {currentTemplate.Name}");
                // Reset photo index for new session if template is already selected
                currentPhotoIndex = 0;
            }

            // Only start photo sequence if we have a valid template
            if (currentTemplate == null)
            {
                Log.Debug("START BUTTON DEBUG: No template selected, cannot start photo sequence");
                UpdateStatusText("Please select a template first");
                
                // Show start button again
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
                return;
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
                
                // Reset to start state
                currentPhotoIndex = 0;
                totalPhotosNeeded = 0;
                capturedPhotoPaths.Clear();
                photoStripImages.Clear();
                photoStripItems.Clear();
                
                // End database session if one was started
                if (databaseOperations.CurrentSessionId.HasValue)
                {
                    databaseOperations.EndSession();
                }
                
                // Show the start button again
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
                
                // Hide stop button
                if (stopSessionButton != null)
                {
                    stopSessionButton.Visibility = Visibility.Collapsed;
                }
                
                // Clear photo strip UI
                UpdatePhotoStripPlaceholders();
                
                UpdateStatusText("Touch START to begin");
                return;
            }
            
            // Otherwise, only abort the current photo countdown and restart it
            Log.Debug($"StopButton_Click: Aborting current photo {currentPhotoIndex + 1} of {totalPhotosNeeded}, will restart countdown");
            
            // Stop the countdown timer
            countdownTimer.Stop();
            CloseOverlay(countdownOverlay, "countdown");
            
            // Cancel any pending capture
            currentCaptureToken?.Cancel();
            
            // Update status
            if (currentEvent != null && totalPhotosNeeded > 1)
            {
                UpdateStatusText($"Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Restarting countdown...");
            }
            else
            {
                UpdateStatusText("Restarting countdown...");
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
                photoCaptureService.ResetSession();
                photoStripImages.Clear();
                photoStripItems.Clear();
                
                // Clear previous processed image paths
                lastProcessedImagePath = null;
                lastProcessedImagePathForPrinting = null;
                lastProcessedWas2x6Template = false;
                
                // Clear sharing state
                currentShareResult = null;
                
                // Hide print button when starting a new session
                HidePrintButton();
                
                // Add placeholder boxes for all photos needed
                UpdatePhotoStripPlaceholders();
                
                // CRITICAL: Create database session at start of photo sequence
                CreateDatabaseSession();
                
                Log.Debug($"StartPhotoSequence: Cleared capturedPhotoPaths list and photo strip (starting new sequence)");
            }
            
            try
            {
                isCapturing = true;
                
                // Hide sharing buttons during capture
                UpdateSharingButtonsVisibility();
                
                // Show stop button when starting sequence
                if (stopSessionButton != null)
                {
                    stopSessionButton.Visibility = Visibility.Visible;
                    Log.Debug("StartPhotoSequence: Showing stop button");
                }
                
                // Hide start button during session
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Collapsed;
                    Log.Debug("StartPhotoSequence: Hiding start button");
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
                
                UpdateStatusText("Preparing camera...");
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
                
                // Check if photographer mode is enabled
                bool photographerMode = Properties.Settings.Default.PhotographerMode;
                Log.Debug($"StartPhotoSequence: PhotographerMode = {photographerMode}");
                
                if (photographerMode)
                {
                    // Photographer mode - don't start countdown, wait for trigger
                    UpdateStatusText($"Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Press camera trigger when ready");
                    Log.Debug("StartPhotoSequence: Photographer mode enabled - waiting for manual trigger");
                    
                    // Stop live view to release camera for trigger
                    try
                    {
                        Log.Debug("StartPhotoSequence: Stopping live view to release camera trigger");
                        DeviceManager.SelectedCameraDevice.StopLiveView();
                        liveViewTimer.Stop();
                        
                        // Reset IsBusy flag to allow trigger
                        DeviceManager.SelectedCameraDevice.IsBusy = false;
                        Log.Debug("StartPhotoSequence: Live view stopped, IsBusy set to false - trigger should be ready");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"StartPhotoSequence: Error stopping live view for photographer mode: {ex.Message}");
                    }
                    
                    // Hide countdown overlay since we're waiting for manual trigger
                    if (countdownOverlay != null)
                    {
                        countdownOverlay.Visibility = Visibility.Collapsed;
                    }
                    
                    // Keep stop button visible so session can be cancelled
                    if (stopSessionButton != null)
                    {
                        stopSessionButton.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    // Normal mode - start countdown
                    UpdateStatusText("Live view active - Starting countdown...");
                    
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
            }
            catch (Exception ex)
            {
                Log.Error("Failed to start photo sequence", ex);
                Dispatcher.Invoke(() =>
                {
                    UpdateStatusText("Camera not ready - Please try again");
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
            
            // Note: We now interpret ShowCountdown as controlling only the status text countdown,
            // not the main visual countdown overlay. The overlay stays visible.
            
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
            
            // Update initial countdown status text
            if (currentEvent != null && totalPhotosNeeded > 1)
            {
                UpdateCountdownStatusText($"Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Get ready! {currentCountdown}");
            }
            else
            {
                UpdateCountdownStatusText($"Get ready! {currentCountdown}");
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            Log.Debug($"CountdownTimer_Tick: currentCountdown before decrement={currentCountdown}");
            currentCountdown--;
            
            if (currentCountdown > 0)
            {
                countdownText.Text = currentCountdown.ToString();
                
                // Update countdown status text
                if (currentEvent != null && totalPhotosNeeded > 1)
                {
                    UpdateCountdownStatusText($"Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} - Get ready! {currentCountdown}");
                }
                else
                {
                    UpdateCountdownStatusText($"Get ready! {currentCountdown}");
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
                    UpdateStatusText($"Taking photo {currentPhotoIndex + 1} of {totalPhotosNeeded}...");
                }
                else
                {
                    UpdateStatusText("Taking photo...");
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
            UpdateStatusText("Taking photo...");
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
                            
                            UpdateStatusText("Photo capture timeout - Camera reset, please try again");
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
                    Log.Debug($"PhotoboothTouch: DeviceException caught - ErrorCode: {exception.ErrorCode:X}, Message: {exception.Message}");
                    
                    // Check for Canon-specific error codes
                    if ((uint)exception.ErrorCode == 0x00008D01) // AutoFocus Failed
                    {
                        Log.Debug("PhotoboothTouch: Canon AutoFocus failed (8D01) - attempting without autofocus");
                        // Don't throw - the photo may still have been taken
                        // Wait for the PhotoCaptured event
                        Thread.Sleep(500);
                        retry = false;
                    }
                    // if device is busy retry after a progressive delay
                    else if (exception.ErrorCode == ErrorCodes.MTP_Device_Busy ||
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
                                UpdateStatusText("Camera too busy - Please try again");
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
                            UpdateStatusText("Capture error: " + exception.Message);
                            StopPhotoSequence();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"PhotoboothTouch: Capture general exception: {ex.GetType().Name}: {ex.Message}");
                    
                    // Check if it's a Canon-specific exception wrapped in a general exception
                    if (ex.Message.Contains("8D01") || ex.Message.Contains("Canon error"))
                    {
                        Log.Debug("PhotoboothTouch: Canon error detected in general exception - photo may still be captured");
                        // Don't stop the sequence - wait for PhotoCaptured event
                        Thread.Sleep(1000);
                        retry = false;
                    }
                    else
                    {
                        Log.Error($"PhotoboothTouch: Stack trace: {ex.StackTrace}");
                        Dispatcher.Invoke(() =>
                        {
                            UpdateStatusText("Capture error: " + ex.Message);
                            StopPhotoSequence();
                        });
                    }
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
            
            // Show/hide the photo strip container based on whether we have photos to show
            if (photoStripContainer != null)
            {
                photoStripContainer.Visibility = (totalPhotosNeeded > 0) ? Visibility.Visible : Visibility.Collapsed;
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
            
            // Show sharing buttons again when capture stops
            UpdateSharingButtonsVisibility();
            liveViewTimer.Stop();
            CloseOverlay(countdownOverlay, "countdown");
            
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
                                    UpdateStatusText("Camera busy - resetting...");
                                    
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
                            UpdateStatusText("Touch START to take another photo");
                        }
                    });
                });
            }
        }

        private void LiveViewTimer_Tick(object sender, EventArgs e)
        {
            // We keep this simple implementation here instead of using CameraOperations service
            // because the capture flow in PhotoboothTouchModern needs direct control over the timer
            // for starting/stopping during capture sequences
            try
            {
                var device = DeviceManager.SelectedCameraDevice;
                
                if (device == null)
                {
                    Log.Debug("LiveViewTimer_Tick: No camera device selected");
                    return;
                }
                
                if (!device.GetCapability(CapabilityEnum.LiveView))
                {
                    Log.Debug($"LiveViewTimer_Tick: Camera {device.DeviceName} doesn't support live view");
                    return;
                }

                LiveViewData liveViewData = null;
                
                try
                {
                    liveViewData = device.GetLiveViewImage();
                    if (liveViewData == null)
                    {
                        Log.Debug($"LiveViewTimer: GetLiveViewImage returned null");
                    }
                    else if (liveViewData.ImageData == null)
                    {
                        Log.Debug($"LiveViewTimer: GetLiveViewImage returned data but ImageData is null");
                    }
                    else if (liveViewData.ImageData.Length == 0)
                    {
                        Log.Debug($"LiveViewTimer: GetLiveViewImage returned data but ImageData is empty");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"Error getting live view image: {ex.Message}");
                    return;
                }

                if (liveViewData != null && liveViewData.ImageData != null && liveViewData.ImageData.Length > 0)
                {
                    Log.Debug($"LiveViewTimer: Got live view data with {liveViewData.ImageData.Length} bytes, ImageDataPosition={liveViewData.ImageDataPosition}");
                    try
                    {
                        // For Sony cameras, the JPEG data starts at ImageDataPosition
                        // For other cameras, ImageDataPosition is typically 0
                        int startPos = liveViewData.ImageDataPosition;
                        int dataLength = liveViewData.ImageData.Length - startPos;
                        
                        if (dataLength <= 0)
                        {
                            // Fallback for cameras that don't set ImageDataPosition
                            startPos = 0;
                            dataLength = liveViewData.ImageData.Length;
                        }
                        
                        Log.Debug($"LiveViewTimer: Creating bitmap from {dataLength} bytes starting at position {startPos}");
                        using (var memoryStream = new MemoryStream(liveViewData.ImageData, startPos, dataLength))
                        {
                            var bitmap = CreateBitmapFromStream(memoryStream);
                            Log.Debug($"LiveViewTimer: Bitmap created successfully: {bitmap != null}, Width={bitmap?.PixelWidth}, Height={bitmap?.PixelHeight}");
                            
                            // Update UI on main thread
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (liveViewImage != null)
                                {
                                    liveViewImage.Source = bitmap;
                                    Log.Debug($"LiveViewTimer: Updated liveViewImage.Source");
                                }
                                else
                                {
                                    Log.Debug($"LiveViewTimer: liveViewImage is null - cannot display!");
                                }
                            }));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Error processing live view image: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"LiveViewTimer_Tick error: {ex.Message}");
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
                    // TODO: Update to new PinLockService - pinLockService.ShowPinEntryDialog();
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
                        // Don't use cameraOperations.StartLiveView() as it starts its own timer
                        // We need direct control of the timer in PhotoboothTouchModern
                        DeviceManager.SelectedCameraDevice.StartLiveView();
                        liveViewTimer.Start();
                        Log.Debug("DeviceManager_CameraConnected: Started idle live view");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"DeviceManager_CameraConnected: Failed to start idle live view: {ex.Message}");
                    }
                }
                
                if (currentEvent != null && currentTemplate != null)
                {
                    UpdateStatusText($"Event: {currentEvent.Name} - Ready for photo {currentPhotoIndex + 1} of {totalPhotosNeeded}");
                }
                else if (currentEvent != null)
                {
                    UpdateStatusText($"Event: {currentEvent.Name} - Touch START to select template");
                }
                else
                {
                    UpdateStatusText("Camera ready - Touch START to begin");
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
                    UpdateStatusText($"Event: {currentEvent.Name} - Please connect a camera");
                }
                else
                {
                    UpdateStatusText("Please connect a camera");
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
                    UpdateStatusText("Photo capture failed - no data");
                    StopPhotoSequence();
                });
                return;
            }
            
            // Check if we're in photographer mode and waiting for a photo
            bool photographerMode = Properties.Settings.Default.PhotographerMode;
            Log.Debug($"PhotoCaptured: PhotographerMode = {photographerMode}, isCapturing = {isCapturing}");
            
            // In photographer mode, ensure we're in a capture session
            if (photographerMode && isCapturing)
            {
                // Hide countdown overlay if it's visible (since we're capturing via trigger)
                Dispatcher.Invoke(() =>
                {
                    if (countdownOverlay != null && countdownOverlay.Visibility == Visibility.Visible)
                    {
                        countdownOverlay.Visibility = Visibility.Collapsed;
                        Log.Debug("PhotoCaptured: Hidden countdown overlay for photographer mode capture");
                    }
                });
            }
            
            try
            {
                // Use PhotoCaptureService to process the photo
                string fileName = null;
                try
                {
                    fileName = photoCaptureService.ProcessCapturedPhoto(eventArgs);
                }
                catch (InvalidOperationException ex)
                {
                    Log.Error($"PhotoCaptured: Failed to retrieve photo - {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatusText($"Failed to retrieve captured photo: {ex.Message}");
                        
                        // Try to recover by restarting live view
                        try
                        {
                            DeviceManager.SelectedCameraDevice?.StopLiveView();
                            Thread.Sleep(500);
                            DeviceManager.SelectedCameraDevice?.StartLiveView();
                            liveViewTimer.Start();
                        }
                        catch { }
                        
                        StopPhotoSequence();
                    });
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error($"PhotoCaptured: Unexpected error - {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatusText($"Photo capture error: {ex.Message}");
                        StopPhotoSequence();
                    });
                    return;
                }
                
                if (fileName != null && File.Exists(fileName))
                {
                    // Marshal UI update to UI thread
                    Dispatcher.Invoke(() => 
                    {
                        // Update UI counters
                        photoCount = photoCaptureService.PhotoCount;
                        currentPhotoIndex = photoCaptureService.CurrentPhotoIndex;
                        photoCountText.Text = $"Photos: {photoCount}";
                        
                        // Update local tracking from service
                        capturedPhotoPaths = photoCaptureService.CapturedPhotoPaths;
                        isRetakingPhoto = photoCaptureService.IsRetakingPhoto;
                        photoIndexToRetake = photoCaptureService.PhotoIndexToRetake;
                        
                        // Determine file type once for use in multiple places
                        var fileExtension = System.IO.Path.GetExtension(fileName).ToLower();
                        
                        // Add to photo strip
                        try
                        {
                            bool isVideo = (fileExtension == ".mp4" || fileExtension == ".mov" || fileExtension == ".avi");
                            
                            if (isVideo)
                            {
                                // It's a video - don't try to create bitmap thumbnail
                                Log.Debug($"PhotoCaptured: Video file captured, skipping photo strip thumbnail");
                                
                                // Mark the strip item as a video
                                if (currentPhotoIndex - 1 >= 0 && currentPhotoIndex - 1 < photoStripItems.Count)
                                {
                                    photoStripItems[currentPhotoIndex - 1].IsPlaceholder = false;
                                    photoStripItems[currentPhotoIndex - 1].ItemType = "Video";
                                    photoStripItems[currentPhotoIndex - 1].FilePath = fileName;
                                    // Could set a video icon/placeholder here if needed
                                }
                            }
                            else
                            {
                                // It's a photo - create thumbnail normally
                                var bitmap = photoCaptureService.CreatePhotoThumbnail(fileName, 240);
                                
                                // Handle retake or normal capture for photo strip
                                if (photoCaptureService.IsRetakingPhoto && photoCaptureService.PhotoIndexToRetake >= 0)
                                {
                                    // Replace the photo in the strip for retake
                                    int retakeIndex = photoCaptureService.PhotoIndexToRetake;
                                    if (retakeIndex < photoStripImages.Count)
                                    {
                                        photoStripImages[retakeIndex] = bitmap;
                                    }
                                    if (retakeIndex < photoStripItems.Count)
                                    {
                                        photoStripItems[retakeIndex].Image = bitmap;
                                        photoStripItems[retakeIndex].IsPlaceholder = false;
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
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"PhotoCaptured: Failed to add to photo strip: {ex.Message}");
                        }
                        
                        // Show captured image or video
                        if (fileExtension == ".mp4" || fileExtension == ".mov" || fileExtension == ".avi")
                        {
                            // It's a video file - don't try to display it as an image
                            Log.Debug($"PhotoCaptured: Video file captured: {fileName}");
                            // You could show a video thumbnail or placeholder here
                            // For now, just skip showing it in the live view
                        }
                        else
                        {
                            // It's an image file - display it normally
                            liveViewImage.Source = (new ImageSourceConverter()).ConvertFromString(fileName) as ImageSource;
                        }
                        
                        // Ensure countdown overlay is hidden
                        CloseOverlay(countdownOverlay, "countdown");
                        
                        // Handle retake completion or normal sequence
                        if (photoCaptureService.IsRetakingPhoto)
                        {
                            // Show review again with updated photo
                            ShowRetakeReview();
                        }
                        else
                        {
                            // Only handle photo sequence progression for actual photos, not videos
                            bool isVideo = (fileExtension == ".mp4" || fileExtension == ".mov" || fileExtension == ".avi");
                            if (!isVideo)
                            {
                                // Handle event workflow photo sequence for photos only
                                HandlePhotoSequenceProgress(fileName);
                            }
                            else
                            {
                                Log.Debug($"PhotoCaptured: Video file captured, skipping photo sequence logic");
                                
                                // Check if this is a boomerang, flipbook, or regular video
                                if (isCapturingBoomerang)
                                {
                                    // This is a boomerang video - process it
                                    statusText.Text = "Processing boomerang...";
                                    Log.Debug($"PhotoCaptured: Boomerang video captured: {fileName}");
                                    Log.Debug($"PhotoCaptured: isCapturingBoomerang=true, processing boomerang");
                                    
                                    // Don't display raw video - wait for processed boomerang MP4
                                    ProcessBoomerangVideo(fileName);
                                }
                                else if (isRecordingFlipbook)
                                {
                                    // This is a flipbook video - process it
                                    statusText.Text = "Processing flipbook...";
                                    Log.Debug($"PhotoCaptured: Flipbook video captured: {fileName}");
                                    Log.Debug($"PhotoCaptured: isRecordingFlipbook=true, processing flipbook");
                                    
                                    // Don't display raw video or add to strip - wait for processed MP4
                                    // The processed MP4 will be displayed by ProcessFlipbookVideo
                                    ProcessFlipbookVideo(fileName);
                                }
                                else
                                {
                                    // This is a regular video recording
                                    statusText.Text = "Video captured successfully!";
                                    Log.Debug($"PhotoCaptured: Regular video captured: {fileName}");
                                    Log.Debug($"PhotoCaptured: isRecordingFlipbook={isRecordingFlipbook}, isCapturingBoomerang={isCapturingBoomerang}, isRecording={isRecording}");
                                    
                                    // Display the video in the live view screen
                                    DisplayVideoInLiveView(fileName);
                                }
                                
                                // Ensure camera busy state is cleared after video display
                                Log.Debug($"PhotoCaptured: Ensuring camera IsBusy is false after video display");
                                if (eventArgs.CameraDevice != null)
                                {
                                    eventArgs.CameraDevice.IsBusy = false;
                                    Log.Debug($"PhotoCaptured: Set camera IsBusy = false after video display");
                                }
                            }
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


        private void Log_LogMessage(CameraControl.Devices.Classes.LogEventArgs e)
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
            Log.Debug($"★★★ COMPOSE WRAPPER DEBUG: About to call PhotoProcessingOperations.ComposeTemplateWithPhotos ★★★");
            Log.Debug($"★★★ COMPOSE WRAPPER DEBUG: currentTemplate={currentTemplate?.Name}, capturedPhotoPaths.Count={capturedPhotoPaths?.Count} ★★★");
            
            // Delegate to photo processing service
            return await photoProcessingOperations.ComposeTemplateWithPhotos(
                currentTemplate, capturedPhotoPaths, currentEvent, database);
        }
        // Removed old ComposeTemplateWithPhotos implementation - now in PhotoProcessingOperations
        
        
        
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
            photoCaptureService.ResetSession();
            eventTemplateService.ClearSelections();
            
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
                UpdateStatusText("Resetting camera connection...");
                resetCameraButton.IsEnabled = false;
                
                await ReconnectCamera();
                
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    RefreshDisplay();
                    UpdateStatusText("Camera reset successfully!");
                }
                else
                {
                    UpdateStatusText("Camera reset failed - please check connection");
                }
                
                // Re-enable button after a delay
                await Task.Delay(2000);
                UpdateStatusText("Touch START to begin");
            }
            catch (Exception ex)
            {
                Log.Error("Manual camera reset failed", ex);
                UpdateStatusText("Camera reset failed - see logs for details");
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
                    photoCaptureService.ResetSession();
                    
                    ShowEventSelectionOverlay();
                };
                // TODO: Update to new PinLockService - pinLockService.ShowPinEntryDialog();
                return;
            }
            
            // Clear current event to allow selecting a new one
            currentEvent = null;
            currentTemplate = null;
            currentPhotoIndex = 0;
            totalPhotosNeeded = 0;
            photoCaptureService.ResetSession();
            
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
                // TODO: Update to new PinLockService - pinLockService.ShowPinEntryDialog();
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
            // Template selection workflow completed
            
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
            eventTemplateService.LoadAvailableTemplates(currentEvent.Id);
            
            // Update UI to show only templates (hide events)
            eventsListControl.Visibility = Visibility.Collapsed;
            templatesListControl.ItemsSource = eventTemplateService.AvailableTemplates;
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
            
            // Hide start button during template selection
            if (startButtonOverlay != null)
            {
                startButtonOverlay.Visibility = Visibility.Collapsed;
                Log.Debug("ShowTemplateSelectionForSession: Hiding start button");
            }
            
            // CRITICAL FIX: Set flag so template clicks will start session immediately
            eventTemplateService.SelectedEventForOverlay = currentEvent;
            Log.Debug($"★★★ TEMPLATE SESSION MODE: Set SelectedEventForOverlay = {currentEvent?.Name}");
        }
        
        private void LoadAvailableEvents()
        {
            try
            {
                eventTemplateService.LoadAvailableEvents();
                eventsListControl.ItemsSource = eventTemplateService.AvailableEvents;
                
                // Clear template selection when events change
                // Templates managed by service
                templatesListControl.ItemsSource = eventTemplateService.AvailableTemplates;
                eventTemplateService.SelectedEventForOverlay = null;
                eventTemplateService.SelectedTemplateForOverlay = null;
                UpdateConfirmButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load events", ex);
                UpdateStatusText("Error loading events");
            }
        }
        
        private void SelectEvent(EventData eventData)
        {
            eventTemplateService.SelectEvent(eventData);
            currentEvent = eventData;
            
            // Load templates for selected event
            eventTemplateService.LoadAvailableTemplates(eventData.Id);
            
            Log.Debug($"EVENT SELECT DEBUG: Selected '{eventData.Name}', loaded {eventTemplateService.AvailableTemplates?.Count ?? 0} templates");
            
            // CRITICAL: Force START button visibility check after event selection
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool shouldShowStartButton = (currentTemplate != null) || 
                    (currentEvent != null && eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 0);
                
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = shouldShowStartButton ? Visibility.Visible : Visibility.Collapsed;
                    Log.Debug($"★★★ FORCED START BUTTON: shouldShow={shouldShowStartButton}, visible={startButtonOverlay.Visibility}");
                }
            }));
            
            // Go straight to photobooth screen with this event
            // Templates will be selected on the photobooth screen itself
            eventSelectionOverlay.Visibility = Visibility.Collapsed;
            
            if (eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 0)
            {
                if (eventTemplateService.AvailableTemplates.Count == 1)
                {
                    // Only one template - auto-select it
                    currentTemplate = eventTemplateService.AvailableTemplates[0];
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
                    // CRITICAL: Show the START button so user can begin template selection
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Visible;
                    
                    UpdateStatusText($"Event: {currentEvent.Name} - Touch START to select template");
                    Log.Debug($"Event has {eventTemplateService.AvailableTemplates.Count} templates - will show selection on START");
                }
            }
            else
            {
                // No templates available
                UpdateStatusText("No templates available for this event");
            }
        }
        
        private void LoadAvailableTemplates(int eventId)
        {
            try
            {
                eventTemplateService.LoadAvailableTemplates(eventId);
                templatesListControl.ItemsSource = eventTemplateService.AvailableTemplates;
                
                // Clear template selection
                eventTemplateService.SelectedTemplateForOverlay = null;
                UpdateConfirmButtonState();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load templates", ex);
                UpdateStatusText("Error loading templates");
            }
        }
        
        private void SelectTemplate(TemplateData templateData)
        {
            eventTemplateService.SelectedTemplateForOverlay = templateData;
            
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
            if (eventTemplateService.SelectedTemplateForOverlay != null)
            {
                // Template-only selection for session
                if (eventTemplateService.SelectedTemplateForOverlay != null)
                {
                    currentTemplate = eventTemplateService.SelectedTemplateForOverlay;
                    
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
                    // Template selection workflow completed
                    eventTemplateService.SelectedTemplateForOverlay = null;
                    
                    statusText.Text = $"Template: {currentTemplate.Name} selected - Starting capture...";
                    
                    // Start the photo sequence
                    StartPhotoSequence();
                }
            }
            else if (eventTemplateService.SelectedEventForOverlay != null && eventTemplateService.SelectedTemplateForOverlay != null)
            {
                // Full event and template selection
                currentEvent = eventTemplateService.SelectedEventForOverlay;
                currentTemplate = eventTemplateService.SelectedTemplateForOverlay;
                
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
            if (eventTemplateService.SelectedTemplateForOverlay != null)
            {
                // We're in template selection mode - just hide the overlay
                eventSelectionOverlay.Visibility = Visibility.Collapsed;
                // Template selection workflow completed
                
                // Restore UI for next time
                eventsListControl.Visibility = Visibility.Visible;
                // confirmSelectionButton.Visibility = Visibility.Visible; // Button removed from UI
                cancelSelectionButton.Content = "✕ Close";
                
                // Show the start button again since user cancelled template selection
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // Regular event selection cancel
                eventSelectionOverlay.Visibility = Visibility.Collapsed;
                
                // Show the start button again since user cancelled
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
            }
            
            // Clear temporary selections
            eventTemplateService.ClearSelections();
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
        private TemplateData previewingTemplate = null;
        
        private void OnTemplateSelected(object sender, MouseButtonEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("★★★ OnTemplateSelected METHOD CALLED ★★★");
                Log.Debug($"★★★ TEMPLATE SELECTION: OnTemplateSelected called");
                
                var border = sender as Border;
                var templateData = border?.Tag as TemplateData;
                
                Log.Debug($"★★★ TEMPLATE SELECTION: border={border != null}, templateData={templateData?.Name}");
                
                if (templateData != null)
                {
                    // Show large preview instead of immediately selecting
                    ShowTemplatePreview(templateData);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"★★★ OnTemplateSelected EXCEPTION: {ex.Message}");
                Log.Error($"OnTemplateSelected failed: {ex.Message}", ex);
            }
        }
        
        private void ShowTemplatePreview(TemplateData templateData)
        {
            try
            {
                previewingTemplate = templateData;
                
                // Update preview UI
                if (previewTemplateName != null)
                    previewTemplateName.Text = templateData.Name ?? "Template";
                
                if (previewTemplateInfo != null)
                {
                    int photoCount = photoboothService.GetTemplatePhotoCount(templateData);
                    string dimensions = $"{templateData.CanvasWidth:F0} x {templateData.CanvasHeight:F0}";
                    previewTemplateInfo.Text = $"{photoCount} photos • {dimensions}";
                }
                
                // Load the thumbnail image in larger size
                if (largeTemplatePreview != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(templateData.ThumbnailImagePath) && 
                            System.IO.File.Exists(templateData.ThumbnailImagePath))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(templateData.ThumbnailImagePath, UriKind.Absolute);
                            bitmap.EndInit();
                            largeTemplatePreview.Source = bitmap;
                        }
                        else if (!string.IsNullOrEmpty(templateData.BackgroundImagePath) && 
                                System.IO.File.Exists(templateData.BackgroundImagePath))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(templateData.BackgroundImagePath, UriKind.Absolute);
                            bitmap.EndInit();
                            largeTemplatePreview.Source = bitmap;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to load template preview image: {ex.Message}");
                    }
                }
                
                // Show the preview overlay
                if (templatePreviewOverlay != null)
                    templatePreviewOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Error($"ShowTemplatePreview failed: {ex.Message}", ex);
            }
        }
        
        private void CloseTemplatePreview_Click(object sender, RoutedEventArgs e)
        {
            if (templatePreviewOverlay != null)
                templatePreviewOverlay.Visibility = Visibility.Collapsed;
            previewingTemplate = null;
        }
        
        private void PreviewBorder_Click(object sender, MouseButtonEventArgs e)
        {
            // Prevent closing when clicking on the preview border itself
            e.Handled = true;
        }
        
        private void SelectTemplateFromPreview_Click(object sender, RoutedEventArgs e)
        {
            if (previewingTemplate != null)
            {
                // Close preview
                if (templatePreviewOverlay != null)
                    templatePreviewOverlay.Visibility = Visibility.Collapsed;
                
                Log.Debug($"★★★ TEMPLATE SELECTION: Selected template '{previewingTemplate.Name}' from preview");
                Log.Debug($"★★★ TEMPLATE SELECTION: SelectedEventForOverlay = {eventTemplateService.SelectedEventForOverlay?.Name ?? "NULL"}");
                
                // When in template selection mode for a session
                if (eventTemplateService.SelectedEventForOverlay != null)
                {
                    Log.Debug($"★★★ TEMPLATE SELECTION: Taking DIRECT START path - starting photo sequence immediately");
                    // CRITICAL: Set currentEvent for database session creation
                    currentEvent = eventTemplateService.SelectedEventForOverlay;
                    currentTemplate = previewingTemplate;
                    
                    Log.Debug($"★★★ MULTI-TEMPLATE SESSION: Set currentEvent={currentEvent?.Name}, currentTemplate={currentTemplate?.Name}");
                    
                    // Get photo count from template
                    totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                    currentPhotoIndex = 0;
                    UpdatePhotoStripPlaceholders();
                    
                    // Hide overlay
                    eventSelectionOverlay.Visibility = Visibility.Collapsed;
                    
                    // Hide start button since session is starting
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Collapsed;
                    
                    // Restore UI for next time
                    eventsListControl.Visibility = Visibility.Visible;
                    cancelSelectionButton.Content = "✕ Close";
                    
                    // Clear flags
                    eventTemplateService.SelectedTemplateForOverlay = null;
                    
                    statusText.Text = $"Template: {currentTemplate.Name} selected - Starting capture...";
                    
                    // Start the photo sequence immediately
                    StartPhotoSequence();
                }
                else
                {
                    Log.Debug($"★★★ TEMPLATE SELECTION: Taking NORMAL SELECTION path - just selecting template");
                    // Normal selection mode - just select the template
                    SelectTemplate(previewingTemplate);
                }
                
                previewingTemplate = null;
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
                // TODO: Update to new PinLockService - pinLockService.ShowPinEntryDialog();
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
                    testPhotoImage.Source = CreateBitmapFromUri(fileName);
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
                retakePhotos.Add(new RetakePhotoItem
                {
                    Image = CreateBitmapFromUri(photoCaptureService.CapturedPhotoPaths[i]),
                    Label = $"Photo {i + 1}",
                    PhotoIndex = i,
                    FilePath = photoCaptureService.CapturedPhotoPaths[i]
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
                photoCaptureService.StartRetake(photoIndex);
                
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
                    CloseOverlay(startButtonOverlay, "start button");
                if (stopSessionButton != null)
                    stopSessionButton.Visibility = Visibility.Visible;
                
                UpdateStatusText("Preparing camera for retake...");
                
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
                // Retake flags managed by service
                
                // Show error and return to review
                UpdateStatusText("Camera error - Unable to retake photo");
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
                        await filterSelectionControl.SetSourceImage(photoCaptureService.CapturedPhotoPaths[0]);
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
                            statusText.Dispatcher.Invoke(() => UpdateStatusText("Applying filters..."));
                            
                            // Apply filter to each photo
                            List<string> filteredPaths = new List<string>();
                            for (int i = 0; i < capturedPhotoPaths.Count; i++)
                            {
                                string filteredPath = await photoProcessingOperations.ApplyFilterToPhoto(photoCaptureService.CapturedPhotoPaths[i], selectedFilter, false);
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
                        
                        // Auto-upload the processed image and photos to cloud
                        await AutoUploadSessionPhotos(processedImagePath);
                        
                        Dispatcher.Invoke(() =>
                        {
                            // Show the processed image (always show the original, not the 4x6 duplicate)
                            liveViewImage.Source = new BitmapImage(new Uri(processedImagePath));
                            UpdateStatusText("Photos processed successfully!");
                            
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
                                    UpdateStatusText("Session complete - Click Done or wait for timeout");
                                    
                                    // Hide the start button when session completes
                                    if (startButtonOverlay != null)
                                    {
                                        startButtonOverlay.Visibility = Visibility.Collapsed;
                                        Log.Debug("Session complete: Hiding start button");
                                    }
                                    
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
                            UpdateStatusText("Failed to process template");
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
                    UpdateStatusText("Preparing filter options...");
                    
                    // Ensure live view is visible for preview
                    liveViewImage.Visibility = Visibility.Visible;
                    
                    // Show overlay immediately with loading state
                    postSessionFilterOverlay.Visibility = Visibility.Visible;
                    Log.Debug($"ShowPostSessionFilterOverlay: Set overlay to visible, actual visibility: {postSessionFilterOverlay.Visibility}");
                    
                    // Generate filter previews directly on UI thread with proper async handling
                    try
                    {
                        Log.Debug($"ShowPostSessionFilterOverlay: Generating previews for photo: {photoCaptureService.CapturedPhotoPaths[0]}");
                        await GenerateFilterPreviews(photoCaptureService.CapturedPhotoPaths[0]);
                        UpdateStatusText("Select a filter to preview");
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
                        UpdateStatusText("Error loading filters - proceeding without filters");
                        postSessionFilterOverlay.Visibility = Visibility.Collapsed;
                        ProcessTemplateWithPhotosInternal();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"ShowPostSessionFilterOverlay: Fatal error: {ex.Message}", ex);
                    UpdateStatusText("Error - proceeding without filters");
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
                        var filterTask = photoProcessingOperations.ApplyFilterToPhoto(samplePhotoPath, filterType, true);
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
                            
                            var filterTask = photoProcessingOperations.ApplyFilterToPhoto(samplePhotoPath, filterType, true);
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
        
        public ShareResult GetCurrentShareResult()
        {
            return currentShareResult;
        }
        
        public string GetCurrentSessionGuid()
        {
            try
            {
                // Get the current session ID from DatabaseOperations
                if (databaseOperations?.CurrentSessionId != null)
                {
                    string connectionString = "Data Source=templates.db;Version=3;";
                    using (var conn = new SQLiteConnection(connectionString))
                    {
                        conn.Open();
                        string query = "SELECT SessionGuid FROM PhotoSessions WHERE Id = @id";
                        using (var cmd = new SQLiteCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", databaseOperations.CurrentSessionId.Value);
                            var result = cmd.ExecuteScalar();
                            if (result != null)
                            {
                                return result.ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GetCurrentSessionGuid: Error getting session GUID: {ex.Message}");
            }
            return null;
        }
        
        private async Task AutoUploadVideoSession(string videoPath, string videoType)
        {
            try
            {
                // Generate a session ID for this video
                string sessionId = $"{videoType}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 8)}";
                
                Log.Debug($"AutoUploadVideoSession: Starting auto-upload for {videoType} session {sessionId}");
                
                // Create list with just the video file
                List<string> videosToUpload = new List<string> { videoPath };
                
                // Use offline queue service to upload
                var queueService = sharingOperations.GetOrCreateOfflineQueueService();
                string eventName = currentEvent?.Name;
                var uploadResult = await queueService.QueuePhotosForUpload(sessionId, videosToUpload, eventName);
                
                if (uploadResult.Success)
                {
                    Log.Debug($"AutoUploadVideoSession: {videoType} upload queued successfully. Immediate: {uploadResult.Immediate}");
                    
                    if (uploadResult.Immediate && !string.IsNullOrEmpty(uploadResult.GalleryUrl))
                    {
                        // Update current share result for immediate QR code access
                        currentShareResult = new ShareResult
                        {
                            Success = true,
                            SessionId = sessionId,
                            GalleryUrl = uploadResult.GalleryUrl,
                            ShortUrl = uploadResult.ShortUrl,
                            QRCodeImage = uploadResult.QRCodeImage
                        };
                        
                        // Update sharing buttons visibility on UI thread
                        Dispatcher.Invoke(() => UpdateSharingButtonsVisibility());
                        
                        Log.Debug($"AutoUploadVideoSession: {videoType} gallery URL: {uploadResult.GalleryUrl}");
                    }
                }
                else
                {
                    Log.Error($"AutoUploadVideoSession: Failed to queue {videoType} upload: {uploadResult.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"AutoUploadVideoSession: Error during {videoType} auto-upload: {ex.Message}");
                // Don't show error to user - auto-upload is a background operation
            }
        }
        
        private async Task AutoUploadSessionPhotos(string processedImagePath)
        {
            try
            {
                // Get the session GUID from the database
                string sessionId = GetCurrentSessionGuid();
                
                Log.Debug($"AutoUploadSessionPhotos: Starting auto-upload for session {sessionId}");
                
                if (string.IsNullOrEmpty(sessionId))
                {
                    // Generate a new session ID if needed
                    sessionId = Guid.NewGuid().ToString();
                    Log.Debug($"AutoUploadSessionPhotos: Generated new session ID: {sessionId}");
                }
                
                // Prepare photos for upload (include processed template)
                List<string> photosToUpload = new List<string>();
                
                // Add the processed template image
                if (!string.IsNullOrEmpty(processedImagePath) && File.Exists(processedImagePath))
                {
                    photosToUpload.Add(processedImagePath);
                }
                
                // Add individual captured photos
                foreach (var photoPath in capturedPhotoPaths)
                {
                    if (File.Exists(photoPath))
                    {
                        photosToUpload.Add(photoPath);
                    }
                }
                
                Log.Debug($"AutoUploadSessionPhotos: Uploading {photosToUpload.Count} photos");
                
                // Use offline queue service to upload (handles both online and offline scenarios)
                var queueService = sharingOperations.GetOrCreateOfflineQueueService();
                string eventName = currentEvent?.Name;
                var uploadResult = await queueService.QueuePhotosForUpload(sessionId, photosToUpload, eventName);
                
                if (uploadResult.Success)
                {
                    Log.Debug($"AutoUploadSessionPhotos: Upload queued successfully. Immediate: {uploadResult.Immediate}");
                    
                    // Automatically create/update the event gallery page
                    if (currentEvent != null && uploadResult.Immediate)
                    {
                        _ = Task.Run(async () => await UpdateEventGalleryAsync(eventName, currentEvent.Id));
                    }
                    
                    // Store the gallery URL if we got one
                    if (!string.IsNullOrEmpty(uploadResult.GalleryUrl))
                    {
                        // Save to database for later retrieval
                        var db = new TemplateDatabase();
                        db.UpdatePhotoSessionGalleryUrl(sessionId, uploadResult.GalleryUrl);
                        Log.Debug($"AutoUploadSessionPhotos: Saved gallery URL to database: {uploadResult.GalleryUrl}");
                        
                        // Update current share result for immediate QR code access
                        currentShareResult = new ShareResult
                        {
                            Success = true,
                            GalleryUrl = uploadResult.GalleryUrl,
                            ShortUrl = uploadResult.ShortUrl,
                            QRCodeImage = uploadResult.QRCodeImage
                        };
                    }
                }
                else
                {
                    Log.Error($"AutoUploadSessionPhotos: Failed to queue upload: {uploadResult.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"AutoUploadSessionPhotos: Error during auto-upload: {ex.Message}");
                // Don't show error to user - auto-upload is a background operation
            }
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
                        await Dispatcher.InvokeAsync(() => UpdateStatusText("Applying filters..."));
                        
                        // Apply filter to each photo
                        List<string> filteredPaths = new List<string>();
                        for (int i = 0; i < capturedPhotoPaths.Count; i++)
                        {
                            string filteredPath = await photoProcessingOperations.ApplyFilterToPhoto(photoCaptureService.CapturedPhotoPaths[i], selectedFilter);
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
                        
                        // Auto-upload the processed image and photos to cloud
                        await AutoUploadSessionPhotos(processedImagePath);
                        
                        Dispatcher.Invoke(() =>
                        {
                            // Show the processed image (always show the original, not the 4x6 duplicate)
                            liveViewImage.Source = new BitmapImage(new Uri(processedImagePath));
                            UpdateStatusText("Photos processed successfully!");
                            
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
                                    UpdateStatusText("Session complete - Click photos to view or touch PRINT to print");
                                    
                                    // Hide the start button when session completes
                                    if (startButtonOverlay != null)
                                    {
                                        startButtonOverlay.Visibility = Visibility.Collapsed;
                                        Log.Debug("Session complete (ProcessTemplateWithPhotosInternal): Hiding start button");
                                    }
                                    
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
                                            if (currentEvent != null && eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 1)
                                            {
                                                // Show template selection for same event
                                                statusText.Text = $"Event: {currentEvent.Name} - Touch START to select another template";
                                            }
                                            else if (currentEvent != null && eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count == 1)
                                            {
                                                // Single template - ready to start again
                                                currentTemplate = eventTemplateService.AvailableTemplates[0];
                                                totalPhotosNeeded = photoboothService.GetTemplatePhotoCount(currentTemplate);
                                                currentPhotoIndex = 0;
                                                UpdatePhotoStripPlaceholders(true); // Preserve existing photos
                                                statusText.Text = $"Event: {currentEvent.Name} - Touch START for another session";
                                            }
                                            else
                                            {
                                                // No event or templates - show event selection
                                                UpdateStatusText("Touch Event Settings to select an event");
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
                            UpdateStatusText("Failed to process template");
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
                            await filterSelectionControl.SetSourceImage(photoCaptureService.CapturedPhotoPaths[0]);
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
                
                // Update media remaining if available (for DNP printers)
                if (e.MediaRemaining > 0)
                {
                    mediaRemainingPanel.Visibility = Visibility.Visible;
                    mediaRemainingText.Text = e.MediaRemaining.ToString();
                    
                    // Update the suffix text based on media type
                    if (!string.IsNullOrEmpty(e.MediaType))
                    {
                        mediaTypeText.Text = $" {e.MediaType} left";
                    }
                    else
                    {
                        mediaTypeText.Text = " prints left";
                    }
                    
                    // Color code based on remaining amount
                    var color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green
                    if (e.MediaRemaining < 50)
                    {
                        color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)); // Orange
                    }
                    if (e.MediaRemaining < 20)
                    {
                        color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)); // Red
                    }
                    mediaRemainingText.Foreground = color;
                }
                else
                {
                    mediaRemainingPanel.Visibility = Visibility.Collapsed;
                }
                
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
                eventTemplateService.LoadAvailableTemplates(eventData.Id);
                // Templates loaded into service
                
                if (eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count == 1)
                {
                    // Auto-select single template
                    SetTemplate(eventTemplateService.AvailableTemplates[0]);
                }
                else if (eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 0)
                {
                    // Multiple templates - will be selected when START is pressed
                    statusText.Text = $"Event: {eventData.Name} - Touch START to select template";
                }
                else
                {
                    statusText.Text = $"Event: {eventData.Name} - No templates available";
                }
                
                Log.Debug($"SetEvent: Event '{eventData.Name}' loaded with {eventTemplateService.AvailableTemplates?.Count ?? 0} templates");
                
                // Force the start button to be visible after setting the event
                // Always show the start button when an event is set, even if templates aren't loaded yet
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (startButtonOverlay != null)
                    {
                        // Show start button if we have an event, regardless of template loading status
                        // Templates will be selected when the user clicks START
                        bool shouldShowStartButton = currentEvent != null;
                        
                        startButtonOverlay.Visibility = shouldShowStartButton ? Visibility.Visible : Visibility.Collapsed;
                        Log.Debug($"SetEvent: Start button visibility set to {startButtonOverlay.Visibility}, event={currentEvent?.Name}, templates={eventTemplateService.AvailableTemplates?.Count ?? 0}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
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
                photoCaptureService.ResetSession();
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
        // PIN entry now managed by PinLockService
        private Action _pendingActionAfterUnlock = null;
        
        // PIN pad modes
        private enum PinPadMode
        {
            Unlock,
            PhoneNumber
        }
        // PIN mode now managed by PinLockService
        
        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Update to new PinLockService - pinLockService.ToggleLock();
        }
        
        // PIN/Lock functionality moved to PinLockService
        
        private void PinPadButton_Click(object sender, RoutedEventArgs e)
        {
            // Extract digit from button
            string digit = "";
            if (sender is Button button)
            {
                if (button.Content is StackPanel stackPanel && stackPanel.Children.Count > 0)
                {
                    if (stackPanel.Children[0] is TextBlock textBlock)
                        digit = textBlock.Text;
                }
                else if (button.Content != null)
                    digit = button.Content.ToString();
            }
            
            if (!string.IsNullOrEmpty(digit))
            {
                // TODO: Update to new PinLockService - pinLockService.HandlePinPadButton(digit);
            }
        }

        private string FormatPhoneNumber(string phoneNumber)
        {
            // Basic phone number formatting
            if (phoneNumber.Length >= 10)
            {
                return $"({phoneNumber.Substring(0, 3)}) {phoneNumber.Substring(3, 3)}-{phoneNumber.Substring(6)}";
            }
            else if (phoneNumber.Length >= 6)
            {
                return $"({phoneNumber.Substring(0, 3)}) {phoneNumber.Substring(3)}-";
            }
            else if (phoneNumber.Length >= 3)
            {
                return $"({phoneNumber.Substring(0, 3)}) {phoneNumber.Substring(3)}";
            }
            else
            {
                return phoneNumber;
            }
        }

        private async Task HandlePhoneNumberComplete()
        {
            try
            {
                // Hide PIN pad
                pinEntryOverlay.Visibility = Visibility.Collapsed;
                // PIN mode reset handled by PinLockService
                
                // Send SMS with photos
                // TODO: Update to new PinLockService - await SendSMSWithPhotos(pinLockService.EnteredPin);
            }
            catch (Exception ex)
            {
                Log.Error($"HandlePhoneNumberComplete: Error sending SMS: {ex.Message}");
                ShowSimpleMessage($"Failed to send SMS: {ex.Message}");
            }
        }

        private async Task SendSMSWithPhotos(string phoneNumber)
        {
            string sessionGuid = databaseOperations.CurrentSessionGuid;
            try
            {
                var cloudService = CloudShareProvider.GetShareService();
                
                // Use current share result (should already be uploaded at this point)
                if (currentShareResult != null && !string.IsNullOrEmpty(currentShareResult.GalleryUrl))
                {
                    // Send SMS
                    bool smsSuccess = await cloudService.SendSMSAsync(phoneNumber, currentShareResult.GalleryUrl);
                    
                    // Log SMS send result in database
                    try
                    {
                        var db = new Database.TemplateDatabase();
                        db.LogSMSSend(sessionGuid, phoneNumber, currentShareResult.GalleryUrl, smsSuccess, 
                                     smsSuccess ? null : "SMS sending failed");
                        Log.Debug($"Logged SMS send result to database for session {sessionGuid}");
                    }
                    catch (Exception dbEx)
                    {
                        Log.Error($"Failed to log SMS send to database: {dbEx.Message}");
                    }
                    
                    if (smsSuccess)
                    {
                        ShowSimpleMessage("SMS sent successfully!");
                    }
                    else
                    {
                        ShowSimpleMessage("Failed to send SMS. Please check phone number and try again.");
                    }
                }
                else
                {
                    ShowSimpleMessage("No uploaded photos available for SMS sharing.");
                    
                    // Log failed attempt
                    try
                    {
                        var db = new Database.TemplateDatabase();
                        db.LogSMSSend(sessionGuid, phoneNumber, "", false, "No uploaded photos available");
                    }
                    catch (Exception dbEx)
                    {
                        Log.Error($"Failed to log SMS failure to database: {dbEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SendSMSWithPhotos: Error: {ex.Message}");
                ShowSimpleMessage($"SMS sending failed: {ex.Message}");
                
                // Log exception
                try
                {
                    var db = new Database.TemplateDatabase();
                    db.LogSMSSend(sessionGuid, phoneNumber, currentShareResult?.GalleryUrl ?? "", false, ex.Message);
                }
                catch (Exception dbEx)
                {
                    Log.Error($"Failed to log SMS exception to database: {dbEx.Message}");
                }
            }
        }
        
        private void PinClearButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Update to new PinLockService - pinLockService.ClearPin();
        }
        
        private void PinSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Update to new PinLockService - pinLockService.SubmitPin();
        }
        
        private void PinCancelButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Update to new PinLockService - pinLockService.CancelPinEntry();
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
        
        private void UpdateCloudSyncStatus()
        {
            try
            {
                // Check if cloud sync is configured - read from User environment variables
                string bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME", EnvironmentVariableTarget.User);
                string accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
                string secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);
                
                if (!string.IsNullOrEmpty(bucketName) && !string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
                {
                    // Cloud sync is configured
                    cloudSyncStatusText.Text = "Connected";
                    cloudSyncStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
                    
                    // Truncate bucket name if too long for display
                    string displayBucketName = bucketName;
                    if (displayBucketName.Length > 15)
                    {
                        displayBucketName = displayBucketName.Substring(0, 12) + "...";
                    }
                    cloudSyncBucketText.Text = displayBucketName;
                    cloudSyncBucketText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136));
                    Log.Debug($"UpdateCloudSyncStatus: Cloud sync configured with bucket {bucketName}");
                }
                else
                {
                    // Cloud sync not configured
                    cloudSyncStatusText.Text = "Not Configured";
                    cloudSyncStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)); // Orange
                    cloudSyncBucketText.Text = "Setup in settings";
                    cloudSyncBucketText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136));
                    Log.Debug("UpdateCloudSyncStatus: Cloud sync not configured");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"UpdateCloudSyncStatus: Error checking cloud sync status: {ex.Message}");
                cloudSyncStatusText.Text = "Error";
                cloudSyncStatusText.Foreground = new SolidColorBrush(Colors.Red);
                cloudSyncBucketText.Text = "Check settings";
                cloudSyncBucketText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136));
            }
        }
        
        private void ShowUploadProgress(bool show, string statusMessage = null, double progress = 0)
        {
            try
            {
                if (show)
                {
                    cloudUploadStatusPanel.Visibility = Visibility.Visible;
                    cloudUploadProgress.Value = progress;
                    if (!string.IsNullOrEmpty(statusMessage))
                    {
                        cloudUploadStatusText.Text = statusMessage;
                    }
                }
                else
                {
                    cloudUploadStatusPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ShowUploadProgress: Error updating upload progress: {ex.Message}");
            }
        }
        
        private async void TriggerCloudSharing()
        {
            try
            {
                Log.Debug("TriggerCloudSharing: Starting cloud sharing process");
                
                // Check if cloud sharing is enabled
                string cloudEnabled = Environment.GetEnvironmentVariable("CLOUD_SHARING_ENABLED", EnvironmentVariableTarget.User);
                Log.Debug($"TriggerCloudSharing: CLOUD_SHARING_ENABLED = '{cloudEnabled}'");
                
                if (cloudEnabled != "True")
                {
                    Log.Debug("TriggerCloudSharing: Cloud sharing is not enabled, skipping");
                    return;
                }
                
                // Check if we have photos to share
                if (capturedPhotoPaths == null || capturedPhotoPaths.Count == 0)
                {
                    Log.Debug("TriggerCloudSharing: No photos to share");
                    return;
                }
                
                Log.Debug($"TriggerCloudSharing: Have {capturedPhotoPaths.Count} captured photos to upload");
                
                // Also include the composed image if available
                var photosToUpload = new List<string>(capturedPhotoPaths);
                if (!string.IsNullOrEmpty(lastProcessedImagePath) && File.Exists(lastProcessedImagePath))
                {
                    photosToUpload.Add(lastProcessedImagePath);
                    Log.Debug($"TriggerCloudSharing: Added composed image, total photos: {photosToUpload.Count}");
                }
                
                // Generate session ID
                string sessionId = databaseOperations.CurrentSessionGuid ?? Guid.NewGuid().ToString();
                Log.Debug($"TriggerCloudSharing: Using session ID: {sessionId}");
                
                // Show upload progress
                ShowUploadProgress(true, "Uploading photos...", 0);
                
                // Get the share service
                var cloudShareService = Services.CloudShareProvider.GetShareService();
                Log.Debug($"TriggerCloudSharing: Got share service: {cloudShareService?.GetType().Name}");
                
                // Check if we're using stub service
                if (cloudShareService is StubShareService)
                {
                    Log.Debug("WARNING: TriggerCloudSharing: Using StubShareService - cloud features not available!");
                }
                
                // Upload photos and create gallery
                Log.Debug("TriggerCloudSharing: Calling CreateShareableGalleryAsync...");
                string eventName = currentEvent?.Name;
                var shareResult = await cloudShareService.CreateShareableGalleryAsync(sessionId, photosToUpload, eventName);
                
                Log.Debug($"TriggerCloudSharing: Upload result - Success: {shareResult.Success}, " +
                         $"GalleryUrl: {shareResult.GalleryUrl}, " +
                         $"UploadedPhotos: {shareResult.UploadedPhotos?.Count ?? 0}, " +
                         $"Error: {shareResult.ErrorMessage}");
                
                if (shareResult.Success)
                {
                    ShowUploadProgress(true, "Upload complete!", 100);
                    Log.Debug($"TriggerCloudSharing: Successfully uploaded {shareResult.UploadedPhotos.Count} photos");
                    
                    // Wait a moment then hide progress
                    await Task.Delay(2000);
                    ShowUploadProgress(false);
                    
                    // Check if this is a local URL (stub service result)
                    if (shareResult.GalleryUrl?.StartsWith("file:///") == true)
                    {
                        Log.Debug("WARNING: TriggerCloudSharing: Got local file URL - stub service is being used");
                        // Still show the overlay but indicate it's local
                    }
                    
                    // Show sharing overlay with QR code
                    ShowSharingOverlay(shareResult);
                }
                else
                {
                    ShowUploadProgress(true, "Upload failed", 0);
                    Log.Error($"TriggerCloudSharing: Upload failed - {shareResult.ErrorMessage}");
                    
                    // Hide progress after showing error
                    await Task.Delay(2000);
                    ShowUploadProgress(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TriggerCloudSharing: Exception during cloud sharing: {ex}", ex);
                ShowUploadProgress(false);
            }
        }
        
        private ShareResult currentShareResult;
        
        private void ShowSharingOverlay(ShareResult shareResult)
        {
            try
            {
                Log.Debug($"ShowSharingOverlay: Starting - GalleryUrl: {shareResult?.GalleryUrl}");
                currentShareResult = shareResult;
                
                // Show the QR code if available
                if (shareResult.QRCodeImage != null)
                {
                    Log.Debug("ShowSharingOverlay: QR code image available");
                    if (qrCodeImage != null)
                    {
                        qrCodeImage.Source = shareResult.QRCodeImage;
                        Log.Debug("ShowSharingOverlay: QR code set to image control");
                    }
                    else
                    {
                        Log.Debug("WARNING: ShowSharingOverlay: qrCodeImage control is null!");
                    }
                }
                else
                {
                    Log.Debug("WARNING: ShowSharingOverlay: No QR code image in share result");
                }
                
                // Show the gallery URL
                if (galleryUrlText != null && !string.IsNullOrEmpty(shareResult.GalleryUrl))
                {
                    var urlToDisplay = shareResult.ShortUrl ?? shareResult.GalleryUrl;
                    Log.Debug($"ShowSharingOverlay: URL to display - ShortUrl: '{shareResult.ShortUrl}', GalleryUrl: '{shareResult.GalleryUrl}'");
                    Log.Debug($"ShowSharingOverlay: Setting text to: '{urlToDisplay}'");
                    Log.Debug($"ShowSharingOverlay: URL char by char: {string.Join(" ", urlToDisplay.Select(c => $"{c}({(int)c})"))}");
                    galleryUrlText.Text = urlToDisplay;
                    Log.Debug($"ShowSharingOverlay: Gallery URL text set to: {galleryUrlText.Text}");
                }
                else
                {
                    Log.Debug($"WARNING: ShowSharingOverlay: galleryUrlText is {(galleryUrlText == null ? "null" : "not null")}, GalleryUrl: {shareResult?.GalleryUrl}");
                }
                
                // Show the sharing overlay
                if (sharingOverlay != null)
                {
                    Log.Debug("ShowSharingOverlay: Making overlay visible");
                    sharingOverlay.Visibility = Visibility.Visible;
                    
                    // Make sure it's on top
                    Panel.SetZIndex(sharingOverlay, 999);
                    
                    // Animate overlay appearance
                    var fadeIn = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(300)
                    };
                    sharingOverlay.BeginAnimation(OpacityProperty, fadeIn);
                    Log.Debug("ShowSharingOverlay: Animation started");
                }
                else
                {
                    Log.Error("ShowSharingOverlay: sharingOverlay is null!");
                }
                
                Log.Debug("ShowSharingOverlay: Sharing overlay displayed successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"ShowSharingOverlay: Exception occurred: {ex.Message}", ex);
            }
        }
        
        private async void SendSMS_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // SMS now works with or without uploaded photos (using offline queue)
                
                // Get phone number from input
                if (phoneNumberTextBox == null || string.IsNullOrEmpty(phoneNumberTextBox.Text))
                {
                    Log.Debug("SendSMS_Click: No phone number entered");
                    ShowSimpleMessage("Please enter a phone number");
                    return;
                }
                
                string phoneNumber = phoneNumberTextBox.Text.Trim();
                
                // Validate phone number format (basic validation)
                if (!phoneNumber.StartsWith("+"))
                {
                    // Assume US number if no country code
                    phoneNumber = "+1" + phoneNumber.Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "");
                }
                
                // Show queueing status
                if (sendSmsButton != null)
                {
                    sendSmsButton.IsEnabled = false;
                    sendSmsButton.Content = "Queueing...";
                }
                
                // Get gallery URL from current share result or use pending URL
                string galleryUrl = currentShareResult?.GalleryUrl;
                string sessionId = databaseOperations.CurrentSessionGuid ?? Guid.NewGuid().ToString();
                
                // If no gallery URL, this means photos are pending upload
                if (string.IsNullOrEmpty(galleryUrl))
                {
                    galleryUrl = $"https://photos.app/pending/{sessionId}";
                    Log.Debug($"SendSMS_Click: Using pending URL: {galleryUrl}");
                }
                
                // Use cached offline queue service for SMS (works offline)
                var queueService = sharingOperations.GetOrCreateOfflineQueueService();
                var queueResult = await queueService.QueueSMS(phoneNumber, galleryUrl, sessionId);
                
                if (queueResult.Success)
                {
                    Log.Debug($"SendSMS_Click: SMS queued successfully for {phoneNumber}");
                    
                    // Log SMS in the database as well
                    try
                    {
                        var db = new Database.TemplateDatabase();
                        db.LogSMSSend(sessionId, phoneNumber, galleryUrl, queueResult.Immediate, 
                                     queueResult.Immediate ? null : "Queued for sending when online");
                        Log.Debug($"Logged SMS queue result to database for session {sessionId}");
                    }
                    catch (Exception dbEx)
                    {
                        Log.Error($"Failed to log SMS to database: {dbEx.Message}");
                    }
                    
                    // Show success status on button
                    if (sendSmsButton != null)
                    {
                        if (queueResult.Immediate)
                        {
                            sendSmsButton.Content = "Sent ✓";
                            ShowSimpleMessage("SMS sent successfully!");
                        }
                        else
                        {
                            sendSmsButton.Content = "Queued ✓";
                            ShowSimpleMessage("SMS queued for sending when online");
                        }
                    }
                    
                    // Clear phone number after successful queue
                    await Task.Delay(2000);
                    if (phoneNumberTextBox != null)
                    {
                        phoneNumberTextBox.Text = "";
                    }
                    if (sendSmsButton != null)
                    {
                        sendSmsButton.Content = "Send";
                        sendSmsButton.IsEnabled = true;
                    }
                }
                else
                {
                    Log.Error($"SendSMS_Click: Failed to queue SMS: {queueResult.Message}");
                    
                    if (sendSmsButton != null)
                    {
                        sendSmsButton.Content = "Failed";
                    }
                    ShowSimpleMessage($"Failed to queue SMS: {queueResult.Message}");
                    
                    // Reset button after showing error
                    await Task.Delay(2000);
                    if (sendSmsButton != null)
                    {
                        sendSmsButton.Content = "Send";
                        sendSmsButton.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SendSMS_Click: Error queueing SMS: {ex.Message}");
                ShowSimpleMessage($"SMS queueing failed: {ex.Message}");
                
                // Reset button
                if (sendSmsButton != null)
                {
                    sendSmsButton.Content = "Send";
                    sendSmsButton.IsEnabled = true;
                }
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
            
            // Note: Cloud sharing is now handled by separate sharing buttons
            // No automatic popup - users control sharing via QR code and SMS buttons
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
            // Debug logging to track which path is being used
            Log.Debug($"PrintButton_Click: lastProcessedImagePath = {lastProcessedImagePath}");
            Log.Debug($"PrintButton_Click: lastProcessedImagePathForPrinting = {lastProcessedImagePathForPrinting}");
            
            // Use the print version if available, otherwise use the display version
            string imageToPrint = !string.IsNullOrEmpty(lastProcessedImagePathForPrinting) ? 
                lastProcessedImagePathForPrinting : lastProcessedImagePath;
            
            // Use the display version for the dialog preview
            string imageToPreview = lastProcessedImagePath;
            
            Log.Debug($"PrintButton_Click: Selected imageToPrint = {imageToPrint}");
            
            // Check the actual image dimensions
            if (File.Exists(imageToPrint))
            {
                using (var img = System.Drawing.Image.FromFile(imageToPrint))
                {
                    Log.Debug($"PrintButton_Click: Image dimensions = {img.Width}x{img.Height}");
                }
            }
                
            if (string.IsNullOrEmpty(imageToPrint) || !File.Exists(imageToPrint))
            {
                UpdateStatusText("No image available to print");
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
                    
                    UpdateStatusText("Sending to printer...");
                    
                    // Disable print button to prevent multiple clicks
                    printButton.IsEnabled = false;
                    
                    // Use the PrintService to handle printing
                    var printService = PrintService.Instance;
                    
                    // Prepare photos for printing (use the print version)
                    var photoPaths = new List<string> { imageToPrint };
                    
                    // Print using the service - pass the original format information for proper routing
                    Log.Debug($"PrintButton_Click: About to call PrintService.PrintPhotos with lastProcessedWas2x6Template={lastProcessedWas2x6Template}");
                    var result = printService.PrintPhotos(photoPaths, sessionId, copies, lastProcessedWas2x6Template);
                    Log.Debug($"PrintButton_Click: PrintService.PrintPhotos returned - Success={result.Success}, Message='{result.Message}'");
                    
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
            photoCaptureService.ResetSession();
            photoStripItems.Clear();
            retakePhotos.Clear();
            
            // Reset counters
            currentPhotoIndex = 0;
            photoCount = 0;
            photoCaptureService.ResetSession();
            
            // Clear processed image paths
            lastProcessedImagePath = null;
            lastProcessedImagePathForPrinting = null;
            lastProcessedWas2x6Template = false;
            
            // Reset database session tracking
            databaseOperations.EndSession();
            // Photo IDs cleared by DatabaseOperations
            
            // Clear UI
            liveViewImage.Source = null;
            UpdateStatusText("Touch START to begin");
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
            
            // Show the start button again
            if (startButtonOverlay != null)
            {
                startButtonOverlay.Visibility = Visibility.Visible;
            }
            
            // Check if this is a multi-template event - if so, return to template selection
            if (currentEvent != null && eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 1)
            {
                Log.Debug("ClearSession: Multi-template event detected - returning to template selection");
                // Reset template selection but keep the event
                currentTemplate = null;
                ShowTemplateSelectionForSession();
            }
            else
            {
                // Show start button for single template or no event selected
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
            }
            
            // Hide any overlays
            if (retakeReviewOverlay != null)
            {
                retakeReviewOverlay.Visibility = Visibility.Collapsed;
            }
            if (countdownOverlay != null)
            {
                CloseOverlay(countdownOverlay, "countdown");
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
                // Restore session to database operations
                // Note: Need to add method to restore session in DatabaseOperations
                
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
                    
                    Log.Debug($"★★★ LATEST COMPOSED DEBUG: OutputFormat='{latestComposed.OutputFormat}', lastProcessedWas2x6Template={lastProcessedWas2x6Template} ★★★");
                    
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
                        CloseOverlay(startButtonOverlay, "start button");
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
                        CloseOverlay(startButtonOverlay, "start button");
                    }
                    
                    // Start auto-clear timer if enabled
                    StartAutoClearTimer();
                }
                
                // Check for existing gallery URL and update sharing buttons accordingly
                CheckAndLoadGalleryUrl(sessionData.SessionGuid);
                
                Log.Debug($"LoadSessionData: Session '{sessionData.SessionName}' loaded successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"LoadSessionData: Error loading session data: {ex.Message}");
                statusText.Text = $"Error loading session: {ex.Message}";
                throw;
            }
        }
        
        private void CheckAndLoadGalleryUrl(string sessionGuid)
        {
            try
            {
                Log.Debug($"CheckAndLoadGalleryUrl: Checking for existing gallery URL for session {sessionGuid}");
                
                // Check database for existing gallery URL
                var galleryUrl = database.GetPhotoSessionGalleryUrl(sessionGuid);
                
                if (!string.IsNullOrEmpty(galleryUrl))
                {
                    Log.Debug($"CheckAndLoadGalleryUrl: Found existing gallery URL: {galleryUrl}");
                    
                    // Create a ShareResult object from the existing data
                    var shareService = Services.CloudShareProvider.GetShareService();
                    var qrCodeImage = shareService?.GenerateQRCode(galleryUrl);
                    
                    currentShareResult = new ShareResult
                    {
                        Success = true,
                        GalleryUrl = galleryUrl,
                        QRCodeImage = qrCodeImage,
                        UploadedPhotos = new List<UploadedPhoto>() // We don't need the photo paths for existing shares
                    };
                    
                    // Update sharing button visibility - QR should be visible since it's already uploaded
                    UpdateSharingButtonsVisibility();
                    
                    Log.Debug("CheckAndLoadGalleryUrl: Gallery URL loaded successfully, sharing buttons updated");
                }
                else
                {
                    Log.Debug("CheckAndLoadGalleryUrl: No existing gallery URL found");
                    
                    // No existing URL, but we have photos, so show SMS button only
                    UpdateSharingButtonsVisibility();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CheckAndLoadGalleryUrl: Error checking gallery URL: {ex.Message}");
                
                // On error, assume not uploaded and show SMS only
                UpdateSharingButtonsVisibility();
            }
        }
        
        private void LoadPhotoStripFromSession(List<PhotoData> sessionPhotos, List<ComposedImageData> composedImages)
        {
            try
            {
                // Clear current photo strip
                photoStripImages.Clear();
                photoStripItems.Clear();
                photoCaptureService.ResetSession();
                
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
                            photoCaptureService.CapturedPhotoPaths.Add(photo.FilePath);
                            
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
                                        bitmap.UriSource = new Uri(photoCaptureService.CapturedPhotoPaths[0]);
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
                        CloseOverlay(countdownOverlay, "countdown");
                    
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
                    
                    Log.Debug($"★★★ DISPLAY COMPOSED DEBUG: OutputFormat='{composedImage.OutputFormat}', lastProcessedWas2x6Template={lastProcessedWas2x6Template} ★★★");
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
                        if (itemToSelect.PhotoNumber > 0 && itemToSelect.PhotoNumber <= photoCaptureService.CapturedPhotoPaths.Count)
                        {
                            string photoPath = photoCaptureService.CapturedPhotoPaths[itemToSelect.PhotoNumber - 1];
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
        
        public void AddComposedImageToPhotoStrip(string composedImagePath)
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
                            bitmap.UriSource = new Uri(photoCaptureService.CapturedPhotoPaths[0]); // Use first photo as thumbnail
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
                            bitmap.UriSource = new Uri(photoCaptureService.CapturedPhotoPaths[0]);
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
                    
                    // Display MP4 animations in live view (same as video recordings)
                    if (itemType == "VIDEO" && animationPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Debug($"AddGifToPhotoStrip: Displaying MP4 animation in live view: {animationPath}");
                        DisplayVideoInLiveView(animationPath);
                    }
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
                
                if (databaseOperations.CurrentSessionId.HasValue)
                {
                    Log.Debug("CreateDatabaseSession: Database session already exists");
                    return;
                }
                
                string sessionName = $"{currentEvent.Name}_{currentTemplate.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
                databaseOperations.CreateSession(currentEvent?.Id, currentTemplate?.Id);
                // Session GUID managed by DatabaseOperations
                // Photo IDs cleared by DatabaseOperations
                
                Log.Debug($"CreateDatabaseSession: Created session ID {databaseOperations.CurrentSessionId} with GUID {databaseOperations.CurrentSessionGuid}");
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
                if (!databaseOperations.CurrentSessionId.HasValue)
                {
                    Log.Debug("SavePhotoToDatabase: No active session, creating one");
                    databaseOperations.CreateSession(currentEvent?.Id, currentTemplate?.Id);
                }
                
                if (!databaseOperations.CurrentSessionId.HasValue)
                {
                    Log.Error("SavePhotoToDatabase: Failed to create session");
                    return;
                }
                
                var photoData = new PhotoData
                {
                    SessionId = databaseOperations.CurrentSessionId.Value,
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
                // Photo ID tracked by DatabaseOperations
                
                Log.Debug($"SavePhotoToDatabase: Saved photo {photoId} - {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Log.Error($"SavePhotoToDatabase: Failed to save photo: {ex.Message}");
            }
        }
        
        public void UpdateProcessedImagePaths(string displayPath, string printPath)
        {
            Log.Debug($"UpdateProcessedImagePaths called:");
            Log.Debug($"  - displayPath: {displayPath}");
            Log.Debug($"  - printPath: {printPath}");
            
            lastProcessedImagePath = displayPath;
            lastProcessedImagePathForPrinting = printPath;
            
            // Verify the paths are different for 2x6
            if (displayPath != printPath)
            {
                Log.Debug($"  - Paths are different - using 4x6 duplicate for printing");
                if (File.Exists(printPath))
                {
                    using (var img = System.Drawing.Image.FromFile(printPath))
                    {
                        Log.Debug($"  - Print image dimensions: {img.Width}x{img.Height}");
                    }
                }
            }
            
            UpdateSharingButtonsVisibility();
        }
        
        public void SaveComposedImageToDatabase(string filePath, string outputFormat = "4x6")
        {
            try
            {
                Log.Debug($"★★★ SAVE TO DB DEBUG: filePath='{filePath}', outputFormat='{outputFormat}' ★★★");
                Log.Debug($"★★★ SESSION DEBUG: CurrentSessionId={databaseOperations.CurrentSessionId}, HasValue={databaseOperations.CurrentSessionId.HasValue} ★★★");
                
                if (!databaseOperations.CurrentSessionId.HasValue)
                {
                    Log.Error("SaveComposedImageToDatabase: No active session - creating emergency session");
                    
                    // Create an emergency session to save the composed image
                    if (currentEvent != null)
                    {
                        databaseOperations.CreateSession(currentEvent.Id, currentTemplate?.Id);
                        Log.Debug($"★★★ EMERGENCY SESSION: Created session {databaseOperations.CurrentSessionId} ★★★");
                    }
                    else
                    {
                        Log.Error("SaveComposedImageToDatabase: No currentEvent available for emergency session");
                        return;
                    }
                }
                
                var composedImageData = new ComposedImageData
                {
                    SessionId = databaseOperations.CurrentSessionId.Value,
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
                if (databaseOperations.CurrentPhotoIds.Count > 0)
                {
                    database.LinkPhotosToComposedImage(composedImageId, databaseOperations.CurrentPhotoIds);
                    Log.Debug($"SaveComposedImageToDatabase: Linked composed image {composedImageId} to {databaseOperations.CurrentPhotoIds.Count} photos");
                }
                
                Log.Debug($"SaveComposedImageToDatabase: Saved composed image {composedImageId} - {Path.GetFileName(filePath)}");
                Log.Debug($"★★★ SAVED TO DB: composedImageId={composedImageId}, outputFormat='{outputFormat}' ★★★");
                
                // CRITICAL: Update lastProcessedWas2x6Template immediately after database save
                lastProcessedImagePath = filePath;
                lastProcessedWas2x6Template = outputFormat == "2x6";
                Log.Debug($"★★★ IMMEDIATE UPDATE: lastProcessedWas2x6Template={lastProcessedWas2x6Template} based on outputFormat='{outputFormat}' ★★★");
                
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
                        thumbnailPath = GenerateThumbnailPath(photoCaptureService.CapturedPhotoPaths[0]);
                        if (!File.Exists(thumbnailPath))
                        {
                            // Create thumbnail from first photo
                            using (var image = System.Drawing.Image.FromFile(photoCaptureService.CapturedPhotoPaths[0]))
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
                if (!databaseOperations.CurrentSessionId.HasValue)
                {
                    Log.Debug("SaveAnimationToDatabase: No active session");
                    return;
                }
                
                var animationData = new ComposedImageData
                {
                    SessionId = databaseOperations.CurrentSessionId.Value,
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
                if (animationId > 0 && databaseOperations.CurrentPhotoIds.Count > 0)
                {
                    database.LinkPhotosToComposedImage(animationId, databaseOperations.CurrentPhotoIds);
                    Log.Debug($"SaveAnimationToDatabase: Saved {animationType} animation ID {animationId} linked to {databaseOperations.CurrentPhotoIds.Count} photos");
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
                if (databaseOperations.CurrentSessionId.HasValue)
                {
                    // Only end the session if it has photos
                    if (databaseOperations.CurrentPhotoIds.Count > 0)
                    {
                        database.EndPhotoSession(databaseOperations.CurrentSessionId.Value);
                        Log.Debug($"EndDatabaseSession: Ended session {databaseOperations.CurrentSessionId} with {databaseOperations.CurrentPhotoIds.Count} photos");
                    }
                    else
                    {
                        // Delete empty session from database
                        Log.Debug($"EndDatabaseSession: Deleting empty session {databaseOperations.CurrentSessionId}");
                        database.DeletePhotoSession(databaseOperations.CurrentSessionId.Value);
                    }
                    
                    databaseOperations.EndSession();
                    // Session and photo IDs cleared by DatabaseOperations
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
                var originalImage = new BitmapImage(new Uri(photoCaptureService.CapturedPhotoPaths[0]));
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

        #region Cloud Sharing Event Handlers (Stubs)

        private void QRShareButton_Click(object sender, RoutedEventArgs e)
        {
            // Stub for QR share button
            ShowSimpleMessage("QR code sharing not available in this build");
        }

        private void SMSShareButton_Click(object sender, RoutedEventArgs e)
        {
            // Stub for SMS share button
            ShowSimpleMessage("SMS sharing not available in this build");
        }

        private void CloudSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Stub for cloud settings button
            ShowSimpleMessage("Cloud settings not available in this build");
        }

        private void PhoneTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Stub for phone textbox focus
        }

        private void PhoneTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Stub for phone textbox lost focus
        }

        private void CloseShareOverlay_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("CloseShareOverlay_Click: Closing share overlay");
            
            try
            {
                // Close share overlay
                if (sharingOverlay != null)
                {
                    // Stop any animations first
                    sharingOverlay.BeginAnimation(OpacityProperty, null);
                    sharingOverlay.Opacity = 1;
                    
                    // Hide the overlay
                    sharingOverlay.Visibility = Visibility.Collapsed;
                    Log.Debug("CloseShareOverlay_Click: Overlay hidden");
                }
                else
                {
                    Log.Debug("WARNING: CloseShareOverlay_Click: sharingOverlay is null");
                }
                
                // Don't clear the share result - keep it cached for subsequent QR code displays
                // currentShareResult = null;  // Commented out to preserve gallery URL
                
                // Clear the QR code image
                if (qrCodeImage != null)
                {
                    qrCodeImage.Source = null;
                }
                
                // Clear the phone number if entered
                if (phoneNumberTextBox != null)
                {
                    phoneNumberTextBox.Text = "";
                }
                
                Log.Debug("CloseShareOverlay_Click: Cleanup complete");
            }
            catch (Exception ex)
            {
                Log.Error($"CloseShareOverlay_Click: Error closing overlay: {ex.Message}", ex);
            }
        }

        public void ShowSimpleMessage(string message)
        {
            MessageBox.Show(message, "Feature Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion
        
        #region Public Methods for Custom UI Integration
        
        /// <summary>
        /// Public method for custom UI buttons to start photo session
        /// </summary>
        public void StartPhotoSession()
        {
            // Call the existing start button logic
            StartButton_Click(null, null);
        }
        
        /// <summary>
        /// Public method for custom UI to open settings
        /// </summary>
        public void OpenSettings()
        {
            // Call the existing settings button logic
            ModernSettingsButton_Click(null, null);
        }
        
        /// <summary>
        /// Public method for custom UI to open gallery
        /// </summary>
        public void OpenGallery()
        {
            // Navigate to gallery or show gallery overlay
            ShowSimpleMessage("Gallery feature coming soon");
        }
        
        /// <summary>
        /// Public method for custom UI to return home
        /// </summary>
        public void ReturnHome()
        {
            // Call the existing home button logic
            HomeButton_Click(null, null);
        }
        
        /// <summary>
        /// Public method for custom UI to stop capture
        /// </summary>
        public void StopCapture()
        {
            // Call the existing stop button logic
            StopButton_Click(null, null);
        }
        
        /// <summary>
        /// Stops live view and cleans up resources - called during shutdown
        /// </summary>
        public void StopLiveView()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("PhotoboothTouchModern.StopLiveView called");
                
                // Stop the live view timer
                if (liveViewTimer != null)
                {
                    liveViewTimer.Stop();
                    liveViewTimer = null;
                }
                
                // Stop any running countdown
                if (countdownTimer != null)
                {
                    countdownTimer.Stop();
                    countdownTimer = null;
                }
                
                // Stop camera live view
                if (DeviceManager != null && DeviceManager.SelectedCameraDevice != null)
                {
                    try
                    {
                        DeviceManager.SelectedCameraDevice.StopLiveView();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error stopping camera live view: {ex.Message}");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("PhotoboothTouchModern.StopLiveView completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PhotoboothTouchModern.StopLiveView: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Public method for custom UI to open camera settings
        /// </summary>
        public void OpenCameraSettings()
        {
            // Call the existing camera settings button logic
            CameraSettingsButton_Click(null, null);
        }
        
        /// <summary>
        /// Public method for custom UI to open event selection
        /// </summary>
        public void OpenEventSelection()
        {
            // Call the existing event settings button logic
            EventSettingsButton_Click(null, null);
        }
        
        /// <summary>
        /// Refresh the UI layout (e.g., after changing orientation)
        /// </summary>
        public void RefreshCustomLayout()
        {
            // Method kept for compatibility but no longer uses custom layouts
        }
        
        /// <summary>
        /// Check if custom layout is active
        /// </summary>
        public bool IsCustomLayoutActive
        {
            get { return false; } // Custom layouts removed - always return false
        }
        
        /// <summary>
        /// Update share result (called from SharingOperations)
        /// </summary>
        public void UpdateShareResult(ShareResult result)
        {
            currentShareResult = result;
        }
        
        
        
        #endregion

        #region Template Preview Generation

        private void TemplatePreviewCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Canvas canvas && canvas.Tag is TemplateData templateData)
                {
                    GenerateTemplatePreview(canvas, templateData);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplatePreviewCanvas_Loaded: Error generating preview: {ex.Message}");
            }
        }

        private void GenerateTemplatePreview(Canvas canvas, TemplateData templateData)
        {
            try
            {
                // Clear existing content
                canvas.Children.Clear();
                
                double canvasWidth = canvas.Width;
                double canvasHeight = canvas.Height;
                
                // Calculate scale to fit template in preview
                double templateWidth = templateData.CanvasWidth;
                double templateHeight = templateData.CanvasHeight;
                double scale = Math.Min(canvasWidth / templateWidth, canvasHeight / templateHeight) * 0.85;
                
                // Center the template preview
                double offsetX = (canvasWidth - templateWidth * scale) / 2;
                double offsetY = (canvasHeight - templateHeight * scale) / 2;
                
                // Draw template background
                var templateBorder = new Border
                {
                    Width = templateWidth * scale,
                    Height = templateHeight * scale,
                    Background = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                    BorderThickness = new Thickness(1)
                };
                Canvas.SetLeft(templateBorder, offsetX);
                Canvas.SetTop(templateBorder, offsetY);
                canvas.Children.Add(templateBorder);
                
                // Get template items from database
                var items = database.GetCanvasItems(templateData.Id);
                Log.Debug($"GenerateTemplatePreview: Template '{templateData.Name}' (ID: {templateData.Id}) has {items.Count} canvas items");
                
                // Filter for photo placeholders and images
                var photoItems = items.Where(i => i.ItemType == "Placeholder" || i.ItemType == "Image").ToList();
                Log.Debug($"GenerateTemplatePreview: Found {photoItems.Count} photo placeholders/images");
                
                // If no items found, create default placeholders based on template type
                if (photoItems.Count == 0)
                {
                    Log.Debug($"GenerateTemplatePreview: No items found, generating default layout");
                    photoItems = GenerateDefaultPlaceholders(templateData);
                }
                
                // Draw placeholder boxes for photo positions
                foreach (var item in photoItems)
                {
                    double x = offsetX + item.X * scale;
                    double y = offsetY + item.Y * scale;
                    double width = item.Width * scale;
                    double height = item.Height * scale;
                    
                    // Create placeholder rectangle
                    var placeholder = new Border
                    {
                        Width = width,
                        Height = height,
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)),
                        BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180)),
                        BorderThickness = new Thickness(1)
                    };
                    
                    // Add camera icon
                    var icon = new TextBlock
                    {
                        Text = "📷",
                        FontSize = Math.Min(width, height) * 0.4,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    
                    placeholder.Child = icon;
                    
                    Canvas.SetLeft(placeholder, x);
                    Canvas.SetTop(placeholder, y);
                    canvas.Children.Add(placeholder);
                }
                
                // Add template type label (optional)
                string templateType = GetTemplateTypeLabel(templateData);
                if (!string.IsNullOrEmpty(templateType))
                {
                    var typeLabel = new Border
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 2, 5, 2)
                    };
                    
                    var labelText = new TextBlock
                    {
                        Text = templateType,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontWeight = FontWeights.Bold
                    };
                    
                    typeLabel.Child = labelText;
                    Canvas.SetRight(typeLabel, 5);
                    Canvas.SetBottom(typeLabel, 5);
                    canvas.Children.Add(typeLabel);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GenerateTemplatePreview: Error: {ex.Message}");
                
                // Fallback to simple icon
                canvas.Children.Clear();
                var fallbackIcon = new TextBlock
                {
                    Text = "📷",
                    FontSize = 60,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Canvas.SetLeft(fallbackIcon, canvas.Width / 2 - 30);
                Canvas.SetTop(fallbackIcon, canvas.Height / 2 - 30);
                canvas.Children.Add(fallbackIcon);
            }
        }
        
        private List<CanvasItemData> GenerateDefaultPlaceholders(TemplateData templateData)
        {
            var placeholders = new List<CanvasItemData>();
            double ratio = templateData.CanvasWidth / templateData.CanvasHeight;
            
            // Generate default layouts based on template dimensions
            if (Math.Abs(ratio - 1.5) < 0.1) // 4x6 landscape
            {
                // 2x2 grid for landscape
                double itemWidth = templateData.CanvasWidth * 0.4;
                double itemHeight = templateData.CanvasHeight * 0.4;
                double spacing = templateData.CanvasWidth * 0.05;
                
                placeholders.Add(CreatePlaceholder(spacing, spacing, itemWidth, itemHeight, 1));
                placeholders.Add(CreatePlaceholder(templateData.CanvasWidth - spacing - itemWidth, spacing, itemWidth, itemHeight, 2));
                placeholders.Add(CreatePlaceholder(spacing, templateData.CanvasHeight - spacing - itemHeight, itemWidth, itemHeight, 3));
                placeholders.Add(CreatePlaceholder(templateData.CanvasWidth - spacing - itemWidth, templateData.CanvasHeight - spacing - itemHeight, itemWidth, itemHeight, 4));
            }
            else if (Math.Abs(ratio - 0.667) < 0.1) // 4x6 portrait
            {
                // 2x3 grid for portrait
                double itemWidth = templateData.CanvasWidth * 0.4;
                double itemHeight = templateData.CanvasHeight * 0.27;
                double spacingX = templateData.CanvasWidth * 0.067;
                double spacingY = templateData.CanvasHeight * 0.04;
                
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        double x = spacingX + col * (itemWidth + spacingX);
                        double y = spacingY + row * (itemHeight + spacingY);
                        placeholders.Add(CreatePlaceholder(x, y, itemWidth, itemHeight, row * 2 + col + 1));
                    }
                }
            }
            else if (Math.Abs(ratio - 0.333) < 0.1) // 2x6 strip
            {
                // 3 vertical photos for strip
                double itemWidth = templateData.CanvasWidth * 0.8;
                double itemHeight = templateData.CanvasHeight * 0.28;
                double spacingX = templateData.CanvasWidth * 0.1;
                double spacingY = templateData.CanvasHeight * 0.04;
                
                for (int i = 0; i < 3; i++)
                {
                    double y = spacingY + i * (itemHeight + spacingY);
                    placeholders.Add(CreatePlaceholder(spacingX, y, itemWidth, itemHeight, i + 1));
                }
            }
            else
            {
                // Default single centered placeholder
                double itemWidth = templateData.CanvasWidth * 0.8;
                double itemHeight = templateData.CanvasHeight * 0.8;
                double x = (templateData.CanvasWidth - itemWidth) / 2;
                double y = (templateData.CanvasHeight - itemHeight) / 2;
                placeholders.Add(CreatePlaceholder(x, y, itemWidth, itemHeight, 1));
            }
            
            return placeholders;
        }
        
        private CanvasItemData CreatePlaceholder(double x, double y, double width, double height, int number)
        {
            return new CanvasItemData
            {
                ItemType = "Placeholder",
                X = x,
                Y = y,
                Width = width,
                Height = height,
                PlaceholderNumber = number,
                Name = $"Photo {number}"
            };
        }
        
        private string GetTemplateTypeLabel(TemplateData templateData)
        {
            double ratio = templateData.CanvasWidth / templateData.CanvasHeight;
            
            // Detect template type based on dimensions
            if (Math.Abs(ratio - 1.5) < 0.1) // 4x6 landscape (6/4 = 1.5)
                return "4x6 Landscape";
            else if (Math.Abs(ratio - 0.667) < 0.1) // 4x6 portrait (4/6 = 0.667)
                return "4x6 Portrait";
            else if (Math.Abs(ratio - 0.333) < 0.1) // 2x6 strip (2/6 = 0.333)
                return "2x6 Strip";
            else if (Math.Abs(ratio - 1.25) < 0.1) // 5x7 landscape
                return "5x7 Landscape";
            else if (Math.Abs(ratio - 0.8) < 0.1) // 5x7 portrait
                return "5x7 Portrait";
            else if (Math.Abs(ratio - 1.25) < 0.1) // 8x10 landscape
                return "8x10 Landscape";
            else if (Math.Abs(ratio - 0.8) < 0.1) // 8x10 portrait
                return "8x10 Portrait";
            else
                return "";
        }

        #endregion

        #region Sharing Button Event Handlers

        private void QrCodeSharingButton_Click(object sender, RoutedEventArgs e)
        {
            // Delegate to SharingOperations service
            // Get the session GUID properly
            string sessionGuid = GetCurrentSessionGuid();
            sharingOperations.HandleQrCodeSharingClick(currentShareResult, sessionGuid);
        }

        private void SmsSharingButton_Click(object sender, RoutedEventArgs e)
        {
            // Delegate to SharingOperations service
            // Get the session GUID properly
            string sessionGuid = GetCurrentSessionGuid();
            sharingOperations.HandleSmsSharingClick(lastProcessedImagePath, capturedPhotoPaths, 
                currentShareResult, sessionGuid);
        }

        

        private void ShowSharingOverlayForSMS()
        {
            // Use the main ShowSharingOverlay method
            ShowSharingOverlay(currentShareResult);
            
            // Then focus on phone number input for SMS
            if (phoneNumberTextBox != null)
            {
                phoneNumberTextBox.Text = "";
                phoneNumberTextBox.Focus();
                Log.Debug("ShowSharingOverlayForSMS: Phone number textbox focused");
            }
        }
        
        private async Task UpdateEventGalleryAsync(string eventName, int eventId)
        {
            try
            {
                Log.Debug($"UpdateEventGalleryAsync: Updating gallery for event '{eventName}'");
                
                // Get the cloud share service
                var shareService = Services.CloudShareProvider.GetShareService();
                
                if (shareService is Services.CloudShareServiceRuntime runtimeService)
                {
                    // Create/update the event gallery
                    var (galleryUrl, password) = await runtimeService.CreateEventGalleryAsync(eventName, eventId);
                    
                    if (!string.IsNullOrEmpty(galleryUrl))
                    {
                        Log.Debug($"UpdateEventGalleryAsync: Event gallery updated successfully");
                        Log.Debug($"Gallery URL: {galleryUrl}");
                        Log.Debug($"Gallery Password: {password}");
                        
                        // Store the event gallery URL for later use
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // You could store this in a property or database for display later
                            currentEventGalleryUrl = galleryUrl;
                            currentEventGalleryPassword = password;
                        });
                    }
                    else
                    {
                        Log.Debug("UpdateEventGalleryAsync: Failed to create event gallery");
                    }
                }
                else
                {
                    Log.Debug("UpdateEventGalleryAsync: CloudShareServiceRuntime not available");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"UpdateEventGalleryAsync: Error updating event gallery: {ex.Message}");
            }
        }
        
        private string currentEventGalleryUrl;
        private string currentEventGalleryPassword;
        
        private void ShowSMSInputDialog()
        {
            try
            {
                // Set PIN pad to phone number mode
                // TODO: Update to new PinLockService - pinLockService.SetMode(PinLockService.PinPadMode.PhoneNumber);
                
                // Show PIN pad overlay for phone number input
                pinEntryOverlay.Visibility = Visibility.Visible;
                pinDisplayBox.Text = "Enter Phone Number (+1234567890)";
                
                Log.Debug("ShowSMSInputDialog: SMS input dialog shown");
            }
            catch (Exception ex)
            {
                Log.Error($"ShowSMSInputDialog: Error showing SMS input: {ex.Message}");
                ShowSimpleMessage("Failed to show SMS input");
            }
        }

        /// <summary>
        /// Show or hide sharing buttons based on conditions
        /// </summary>
        public void UpdateSharingButtonsVisibility()
        {
            try
            {
                // Basic conditions for showing sharing buttons
                bool hasPhotos = !isCapturing && 
                               (lastProcessedImagePath != null || 
                                (capturedPhotoPaths != null && capturedPhotoPaths.Count > 0));

                // Check if photos are already uploaded (from currentShareResult or database)
                bool photosUploaded = false;
                
                // First check currentShareResult
                if (currentShareResult != null && !string.IsNullOrEmpty(currentShareResult.GalleryUrl))
                {
                    photosUploaded = true;
                }
                // If not in currentShareResult, check database for existing gallery URL
                else if (!string.IsNullOrEmpty(databaseOperations.CurrentSessionGuid))
                {
                    try
                    {
                        var db = new Database.TemplateDatabase();
                        string galleryUrl = db.GetPhotoSessionGalleryUrl(databaseOperations.CurrentSessionGuid);
                        photosUploaded = !string.IsNullOrEmpty(galleryUrl);
                        
                        // If we found a URL in database but currentShareResult is null, create it
                        if (photosUploaded && currentShareResult == null && !string.IsNullOrEmpty(galleryUrl))
                        {
                            var cloudService = CloudShareProvider.GetShareService();
                            var qrCodeImage = cloudService?.GenerateQRCode(galleryUrl);
                            currentShareResult = new ShareResult
                            {
                                Success = true,
                                GalleryUrl = galleryUrl,
                                QRCodeImage = qrCodeImage,
                                UploadedPhotos = new List<UploadedPhoto>()
                            };
                        }
                    }
                    catch (Exception dbEx)
                    {
                        Log.Error($"UpdateSharingButtonsVisibility: Error checking database for gallery URL: {dbEx.Message}");
                    }
                }

                // QR Code button: Only show if photos are already uploaded
                var qrVisibility = hasPhotos && photosUploaded ? Visibility.Visible : Visibility.Collapsed;
                
                // SMS button: Always show if we have photos (works offline with queue)
                var smsVisibility = hasPhotos ? Visibility.Visible : Visibility.Collapsed;
                
                if (qrCodeSharingButton != null)
                {
                    qrCodeSharingButton.Visibility = qrVisibility;
                }
                
                if (smsSharingButton != null)
                {
                    smsSharingButton.Visibility = smsVisibility;
                }
                
                Log.Debug($"UpdateSharingButtonsVisibility: QR={qrVisibility}, SMS={smsVisibility}, Photos uploaded={photosUploaded}");
            }
            catch (Exception ex)
            {
                Log.Error($"UpdateSharingButtonsVisibility: Error updating visibility: {ex.Message}");
            }
        }
        
        #region Sync Status Methods
        
        private void UpdateSyncStatus()
        {
            try
            {
                // Get queue status from cached offline queue service
                var queueService = sharingOperations.GetOrCreateOfflineQueueService();
                var status = queueService.GetQueueStatus();
                
                Dispatcher.Invoke(() =>
                {
                    if (status.IsUploading)
                    {
                        // Currently uploading
                        syncStatusIcon.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // Blue
                        syncStatusText.Text = "Uploading...";
                        syncStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                        
                        // Show upload name
                        syncPendingCount.Text = status.CurrentUploadName;
                        syncPendingCount.Visibility = Visibility.Visible;
                        
                        // Show and update progress bar
                        syncUploadProgress.Visibility = Visibility.Visible;
                        syncUploadProgress.Value = status.UploadProgress * 100; // Convert to percentage
                    }
                    else if (status.PendingUploads > 0 || status.PendingSMS > 0)
                    {
                        // Items pending but not currently uploading
                        syncStatusIcon.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 167, 38)); // Orange
                        syncStatusText.Text = "Uploads Pending";
                        syncStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 167, 38));
                        
                        // Show pending count
                        int totalPending = status.PendingUploads + status.PendingSMS;
                        syncPendingCount.Text = $"{totalPending} item{(totalPending != 1 ? "s" : "")} pending";
                        syncPendingCount.Visibility = Visibility.Visible;
                        
                        // Hide progress bar when not uploading
                        syncUploadProgress.Visibility = Visibility.Collapsed;
                    }
                    else if (!status.IsOnline)
                    {
                        // Offline mode
                        syncStatusIcon.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158)); // Gray
                        syncStatusText.Text = "Offline Mode";
                        syncStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158));
                        syncPendingCount.Visibility = Visibility.Collapsed;
                        syncUploadProgress.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // All uploads current
                        syncStatusIcon.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green
                        syncStatusText.Text = "Uploads Current";
                        syncStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                        syncPendingCount.Visibility = Visibility.Collapsed;
                        syncUploadProgress.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"UpdateSyncStatus: Error updating sync status: {ex.Message}");
            }
        }
        
        private void OnUploadProgressChanged(double progress, string uploadName)
        {
            // Update UI immediately when upload progress changes
            Dispatcher.Invoke(() =>
            {
                if (progress > 0 || !string.IsNullOrEmpty(uploadName))
                {
                    // Show uploading status
                    syncStatusIcon.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // Blue
                    syncStatusText.Text = "Uploading...";
                    syncStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                    
                    // Show upload name
                    syncPendingCount.Text = uploadName;
                    syncPendingCount.Visibility = Visibility.Visible;
                    
                    // Show and update progress bar
                    syncUploadProgress.Visibility = Visibility.Visible;
                    syncUploadProgress.Value = progress * 100; // Convert to percentage
                }
                else
                {
                    // Upload complete, trigger full status update
                    UpdateSyncStatus();
                }
            });
        }
        
        private void StartSyncStatusMonitoring()
        {
            try
            {
                // Get the offline queue service and subscribe to upload progress
                var queueService = sharingOperations.GetOrCreateOfflineQueueService();
                queueService.UploadProgressChanged += OnUploadProgressChanged;
                
                // Create a timer to update sync status every 5 seconds
                var syncStatusTimer = new System.Windows.Threading.DispatcherTimer();
                syncStatusTimer.Interval = TimeSpan.FromSeconds(5);
                syncStatusTimer.Tick += (s, e) => UpdateSyncStatus();
                syncStatusTimer.Start();
                
                // Initial update
                UpdateSyncStatus();
                
                Log.Debug("StartSyncStatusMonitoring: Sync status monitoring started");
            }
            catch (Exception ex)
            {
                Log.Error($"StartSyncStatusMonitoring: Error starting sync status monitoring: {ex.Message}");
            }
        }
        
        #endregion
        
        #region SMS Phone Pad Event Handlers
        
        private void SmsPhonePadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button?.Content is StackPanel stackPanel)
                {
                    var textBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null)
                    {
                        string digit = textBlock.Text;
                        
                        // Add digit to phone number
                        if (_smsPhoneNumber.Length < 20) // Max phone number length
                        {
                            _smsPhoneNumber += digit;
                            UpdateSmsPhoneDisplay();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SmsPhonePadButton_Click: Error adding digit: {ex.Message}");
            }
        }
        
        private void SmsPhoneBackspace_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Remove last digit, but keep "+1" as minimum
                if (_smsPhoneNumber.Length > 2)
                {
                    _smsPhoneNumber = _smsPhoneNumber.Substring(0, _smsPhoneNumber.Length - 1);
                    UpdateSmsPhoneDisplay();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SmsPhoneBackspace_Click: Error removing digit: {ex.Message}");
            }
        }
        
        private async void SmsSendButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate phone number
                if (_smsPhoneNumber.Length < 5) // Minimum phone number length
                {
                    ShowSimpleMessage("Please enter a valid phone number");
                    return;
                }
                
                // Use the phone number from our dedicated phone pad
                string phoneNumber = _smsPhoneNumber;
                
                // Get gallery URL from current share result or use pending URL
                string galleryUrl = currentShareResult?.GalleryUrl;
                string sessionId = databaseOperations.CurrentSessionGuid ?? Guid.NewGuid().ToString();
                
                // If no gallery URL, this means photos are pending upload
                if (string.IsNullOrEmpty(galleryUrl))
                {
                    galleryUrl = $"https://photos.app/pending/{sessionId}";
                    Log.Debug($"SmsSendButton_Click: Using pending URL: {galleryUrl}");
                }
                
                // Use cached offline queue service for SMS (works offline)
                var queueService = sharingOperations.GetOrCreateOfflineQueueService();
                var queueResult = await queueService.QueueSMS(phoneNumber, galleryUrl, sessionId);
                
                if (queueResult.Success)
                {
                    Log.Debug($"SmsSendButton_Click: SMS queued successfully for {phoneNumber}");
                    
                    // Log SMS in the database as well
                    try
                    {
                        var db = new Database.TemplateDatabase();
                        db.LogSMSSend(sessionId, phoneNumber, galleryUrl, queueResult.Immediate, 
                                     queueResult.Immediate ? null : "Queued for sending when online");
                        Log.Debug($"Logged SMS queue result to database for session {sessionId}");
                    }
                    catch (Exception dbEx)
                    {
                        Log.Error($"Failed to log SMS to database: {dbEx.Message}");
                    }
                    
                    // Show success message and close overlay
                    if (queueResult.Immediate)
                    {
                        ShowSimpleMessage("SMS sent successfully!");
                    }
                    else
                    {
                        ShowSimpleMessage("SMS queued for sending when online");
                    }
                    
                    // Close SMS phone pad overlay
                    sharingOperations.HideSmsPhonePadOverlay();
                }
                else
                {
                    Log.Error($"SmsSendButton_Click: Failed to queue SMS: {queueResult.Message}");
                    ShowSimpleMessage($"Failed to queue SMS: {queueResult.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SmsSendButton_Click: Error queueing SMS: {ex.Message}");
                ShowSimpleMessage($"SMS queueing failed: {ex.Message}");
            }
        }
        
        private void SmsPhonePadCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                sharingOperations.HideSmsPhonePadOverlay();
            }
            catch (Exception ex)
            {
                Log.Error($"SmsPhonePadCancel_Click: Error closing SMS phone pad: {ex.Message}");
            }
        }
        
        private void UpdateSmsPhoneDisplay()
        {
            try
            {
                if (smsPhoneNumberDisplay != null)
                {
                    smsPhoneNumberDisplay.Text = _smsPhoneNumber;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"UpdateSmsPhoneDisplay: Error updating display: {ex.Message}");
            }
        }
        
        
        #endregion
        
        #region Video and Boomerang Module Methods
        
        private void UpdateModuleButtonVisibility()
        {
            try
            {
                // Update video button visibility
                if (videoRecordButton != null)
                {
                    videoRecordButton.Visibility = modulesConfig.ShowVideoButton ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Update boomerang button visibility
                if (boomerangButton != null)
                {
                    boomerangButton.Visibility = modulesConfig.ShowBoomerangButton ? Visibility.Visible : Visibility.Collapsed;
                }
                
                // Show/hide flipbook button
                if (flipbookButton != null)
                {
                    flipbookButton.Visibility = modulesConfig.ShowFlipbookButton ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"UpdateModuleButtonVisibility: Error: {ex.Message}");
            }
        }
        
        private async void VideoRecordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug($"[VIDEO UI] VideoRecordButton clicked. isRecording: {isRecording}");
                
                if (!isRecording)
                {
                    // Start recording
                    UpdateStatusText("Starting video recording...");
                    Log.Debug($"[VIDEO UI] Preparing to start recording. FolderForPhotos: {FolderForPhotos}");
                    
                    // Note: We're keeping live view running during recording
                    // The Canon camera will handle live view internally
                    Log.Debug($"[VIDEO UI] Live view timer status: {liveViewTimer?.IsEnabled ?? false}");
                    
                    string videoPath = Path.Combine(FolderForPhotos, $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
                    Log.Debug($"[VIDEO UI] Calling StartRecordingAsync with path: {videoPath}");
                    
                    bool started = await videoService.StartRecordingAsync(videoPath);
                    
                    if (started)
                    {
                        isRecording = true;
                        UpdateStatusText($"Recording... (max {modulesConfig.VideoDuration}s)");
                        Log.Debug($"[VIDEO UI] Recording started successfully");
                        
                        // Subscribe to recording events
                        videoService.RecordingProgress += OnVideoRecordingProgress;
                        videoService.RecordingStopped += OnVideoRecordingStopped;
                        
                        // Update UI binding for button state
                        OnPropertyChanged(nameof(IsRecording));
                    }
                    else
                    {
                        UpdateStatusText("Failed to start recording");
                        Log.Error("[VIDEO UI] Failed to start video recording");
                    }
                }
                else
                {
                    // Stop recording
                    UpdateStatusText("Stopping video...");
                    Log.Debug($"[VIDEO UI] Stopping recording...");
                    
                    string videoPath = await videoService.StopRecordingAsync();
                    Log.Debug($"[VIDEO UI] StopRecordingAsync returned: {videoPath ?? "NULL"}");
                    
                    if (!string.IsNullOrEmpty(videoPath))
                    {
                        isRecording = false;
                        UpdateStatusText("Video saved!");
                        
                        Log.Debug($"[VIDEO UI] Processing completed video: {videoPath}");
                        
                        // Critical: Clear camera busy state after video recording
                        if (DeviceManager.SelectedCameraDevice != null)
                        {
                            DeviceManager.SelectedCameraDevice.IsBusy = false;
                            Log.Debug("[VIDEO UI] Cleared camera IsBusy flag after video recording");
                        }
                        
                        // Add to captured files list
                        capturedPhotoPaths.Add(videoPath);
                        Log.Debug($"[VIDEO UI] Added video to captured files list: {capturedPhotoPaths.Count} total files");
                        
                        // Update UI binding
                        OnPropertyChanged(nameof(IsRecording));
                        
                        // Display the video in the live view screen
                        Log.Debug($"[VIDEO UI] About to call DisplayVideoInLiveView with: {videoPath}");
                        DisplayVideoInLiveView(videoPath);
                        
                        // Auto-upload video to cloud
                        _ = Task.Run(async () => await AutoUploadVideoSession(videoPath, "video"));
                        
                        Log.Debug($"[VIDEO UI] Video saved successfully, displaying in live view: {videoPath}");
                    }
                    else
                    {
                        Log.Debug("[VIDEO UI] VideoPath is null or empty, not displaying video");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"VideoRecordButton_Click: Error: {ex.Message}");
                UpdateStatusText("Video recording error");
                isRecording = false;
                
                // Ensure camera state is cleared even on error
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    DeviceManager.SelectedCameraDevice.IsBusy = false;
                    Log.Debug("[VIDEO UI] Cleared camera IsBusy flag after video recording error");
                }
                
                OnPropertyChanged(nameof(IsRecording));
            }
        }
        
        private void OnVideoRecordingProgress(object sender, TimeSpan elapsed)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int remaining = modulesConfig.VideoDuration - (int)elapsed.TotalSeconds;
                if (remaining >= 0)
                {
                    UpdateStatusText($"Recording... {remaining}s remaining");
                }
            }));
        }
        
        private void OnVideoRecordingStopped(object sender, VideoRecordingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                isRecording = false;
                OnPropertyChanged(nameof(IsRecording));
                
                // Unsubscribe from events
                videoService.RecordingProgress -= OnVideoRecordingProgress;
                videoService.RecordingStopped -= OnVideoRecordingStopped;
            }));
        }
        
        private void ShowVideoPreview(string videoPath)
        {
            try
            {
                // Add to photo strip as a special item
                var videoItem = new PhotoStripItem
                {
                    PhotoNumber = photoStripItems.Count + 1,
                    IsPlaceholder = false,
                    ItemType = "Video",
                    FilePath = videoPath
                };
                
                photoStripItems.Add(videoItem);
                
                UpdateStatusText("Video captured!");
                
                // Show the video in the playback overlay
                PlayVideoInOverlay(videoPath);
            }
            catch (Exception ex)
            {
                Log.Error($"ShowVideoPreview: Error: {ex.Message}");
            }
        }
        
        private async void BoomerangButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!isCapturingBoomerang)
                {
                    // Start boomerang capture using camera video recording (same as flipbook)
                    UpdateStatusText("Starting boomerang capture...");
                    Log.Debug("BoomerangButton_Click: Starting boomerang video recording");
                    
                    if (DeviceManager.SelectedCameraDevice != null)
                    {
                        isCapturingBoomerang = true;
                        OnPropertyChanged(nameof(IsCapturingBoomerang));
                        
                        // Apply video settings before starting recording
                        ApplyVideoSettingsForRecording();
                        
                        // Use the same video recording method as video module
                        DeviceManager.SelectedCameraDevice.StartRecordMovie();
                        Log.Debug("BoomerangButton_Click: Camera video recording started");
                        Log.Debug($"BoomerangButton_Click: isCapturingBoomerang flag set to true");
                        
                        // Auto-stop after 3 seconds for boomerang (good duration for boomerang effect)
                        int boomerangDuration = 3000; // 3 seconds in milliseconds
                        Log.Debug($"BoomerangButton_Click: Recording for 3 seconds");
                        await Task.Delay(boomerangDuration);
                        
                        // Auto-stop if still capturing
                        if (isCapturingBoomerang)
                        {
                            BoomerangButton_Click(null, null); // Trigger stop
                        }
                    }
                    else
                    {
                        UpdateStatusText("No camera connected for boomerang");
                        Log.Error("BoomerangButton_Click: No camera device available");
                    }
                }
                else
                {
                    // Stop boomerang capture using camera video recording
                    UpdateStatusText("Finishing boomerang...");
                    Log.Debug("BoomerangButton_Click: Stopping boomerang video recording");
                    
                    if (DeviceManager.SelectedCameraDevice != null)
                    {
                        DeviceManager.SelectedCameraDevice.StopRecordMovie();
                        Log.Debug("BoomerangButton_Click: Camera video recording stopped");
                        
                        // Restore photo settings after recording
                        RestorePhotoSettingsAfterRecording();
                    }
                    
                    // DON'T clear the flag here - let ProcessBoomerangVideo clear it
                    // This ensures the video is recognized as a boomerang in PhotoCaptured
                    Log.Debug($"BoomerangButton_Click: Keeping isCapturingBoomerang=true until video is processed");
                    // isCapturingBoomerang = false;  // Don't clear this here - let ProcessBoomerangVideo clear it
                    OnPropertyChanged(nameof(IsCapturingBoomerang));
                    
                    UpdateStatusText("Boomerang capture completed - processing...");
                    // Note: The actual boomerang file will be handled by PhotoCaptured event
                }
            }
            catch (Exception ex)
            {
                Log.Error($"BoomerangButton_Click: Error: {ex.Message}");
                UpdateStatusText("Boomerang error");
                isCapturingBoomerang = false;
                OnPropertyChanged(nameof(IsCapturingBoomerang));
            }
        }
        
        // NOTE: StartBoomerangFrameCapture method removed - now using camera video recording directly
        // instead of frame-by-frame capture from live view
        
        private async void FlipbookButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!isRecordingFlipbook)
                {
                    // Start flipbook session with countdown
                    Log.Debug("FlipbookButton_Click: Starting flipbook session with countdown");
                    
                    if (DeviceManager.SelectedCameraDevice != null)
                    {
                        // Hide the photo booth start button during flipbook session
                        if (startButtonOverlay != null)
                        {
                            startButtonOverlay.Visibility = Visibility.Collapsed;
                            Log.Debug("FlipbookButton_Click: Hidden start photobooth button");
                        }
                        
                        // FIRST: Start live view BEFORE applying settings or countdown
                        try
                        {
                            DeviceManager.SelectedCameraDevice.StartLiveView();
                            Log.Debug("FlipbookButton_Click: Started live view");
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"FlipbookButton_Click: Live view may already be running: {ex.Message}");
                        }
                        
                        // Start live view timer immediately
                        if (liveViewTimer != null && !liveViewTimer.IsEnabled)
                        {
                            liveViewTimer.Start();
                            Log.Debug("FlipbookButton_Click: Started live view timer");
                        }
                        
                        // Wait a moment for live view to stabilize
                        await Task.Delay(200);
                        
                        // THEN: Apply video settings while live view is running
                        ApplyVideoSettingsForRecording();
                        Log.Debug("FlipbookButton_Click: Applied video settings for live view");
                        
                        // Get countdown settings, but always use at least 3 seconds for flipbook
                        int countdownDuration = Properties.Settings.Default.CountdownSeconds;
                        if (countdownDuration <= 0)
                        {
                            countdownDuration = 3; // Default to 3 seconds for flipbook
                        }
                        
                        // Always show countdown for flipbook (ignore ShowCountdown setting)
                        UpdateStatusText($"Flipbook starting in {countdownDuration}...");
                        
                        // Start countdown overlay
                        countdownOverlay.Visibility = Visibility.Visible;
                        int remainingSeconds = countdownDuration;
                        
                        // Countdown with live view still running
                        while (remainingSeconds > 0)
                        {
                            countdownText.Text = remainingSeconds.ToString();
                            UpdateStatusText($"Get ready for flipbook! {remainingSeconds}");
                            await Task.Delay(1000);
                            remainingSeconds--;
                        }
                        
                        // Show "ACTION!" and prepare for recording
                        countdownText.Text = "ACTION!";
                        UpdateStatusText("Starting recording...");
                        await Task.Delay(500); // Brief pause for "ACTION!"
                        countdownOverlay.Visibility = Visibility.Collapsed;
                        
                        // Stop live view timer JUST before starting recording
                        if (liveViewTimer != null && liveViewTimer.IsEnabled)
                        {
                            liveViewTimer.Stop();
                            Log.Debug("FlipbookButton_Click: Stopped live view timer for recording");
                        }
                        
                        // Small delay for camera to prepare for recording mode
                        await Task.Delay(300);
                        
                        // Now start actual recording
                        isRecordingFlipbook = true;
                        Log.Debug($"FlipbookButton_Click: Set isRecordingFlipbook=true");
                        
                        // Start camera video recording
                        DeviceManager.SelectedCameraDevice.StartRecordMovie();
                        Log.Debug("FlipbookButton_Click: Camera video recording started");
                        
                        // Show recording progress indicator
                        ShowFlipbookRecordingIndicator(true);
                        UpdateStatusText("Recording flipbook...");
                        
                        // Start timer to track 4 seconds with progress
                        flipbookElapsedSeconds = 0;
                        flipbookTimer = new DispatcherTimer();
                        flipbookTimer.Interval = TimeSpan.FromMilliseconds(100); // Update every 100ms for smooth progress
                        flipbookTimer.Tick += FlipbookTimer_Tick;
                        flipbookTimer.Start();
                        
                        // Auto-stop after 4 seconds
                        await Task.Delay(4000);
                        
                        // Auto-stop if still recording
                        if (isRecordingFlipbook)
                        {
                            // Stop recording directly instead of recursive call
                            UpdateStatusText("Stopping flipbook recording...");
                            Log.Debug("FlipbookButton_Click: Auto-stopping flipbook video recording");
                            
                            // Stop timer
                            if (flipbookTimer != null)
                            {
                                flipbookTimer.Stop();
                                flipbookTimer = null;
                            }
                            
                            if (DeviceManager.SelectedCameraDevice != null)
                            {
                                DeviceManager.SelectedCameraDevice.StopRecordMovie();
                                Log.Debug("FlipbookButton_Click: Camera video recording stopped");
                                
                                // Restore photo settings after recording
                                RestorePhotoSettingsAfterRecording();
                            }
                            
                            // Hide countdown overlay after recording
                            countdownOverlay.Visibility = Visibility.Collapsed;
                            
                            // Restore the photo booth start button
                            if (startButtonOverlay != null)
                            {
                                startButtonOverlay.Visibility = Visibility.Visible;
                                Log.Debug("FlipbookButton_Click: Restored start photobooth button");
                            }
                            
                            // Don't restart live view here - DisplayVideoInLiveView will handle it through ReturnToLiveView
                            // Just ensure live view is ready for when the video display ends
                            Log.Debug("FlipbookButton_Click: Live view will be handled by video display completion");
                            
                            // Keep the flag true until the video is received
                            Log.Debug($"FlipbookButton_Click: Keeping isRecordingFlipbook=true until video is processed");
                            ShowFlipbookRecordingIndicator(false);
                            
                            UpdateStatusText("Flipbook recording completed - processing...");
                        }
                    }
                    else
                    {
                        UpdateStatusText("No camera connected for flipbook");
                        Log.Error("FlipbookButton_Click: No camera device available");
                    }
                }
                else
                {
                    // Stop flipbook recording
                    UpdateStatusText("Stopping flipbook recording...");
                    Log.Debug("FlipbookButton_Click: Stopping flipbook video recording");
                    
                    // Stop timer
                    if (flipbookTimer != null)
                    {
                        flipbookTimer.Stop();
                        flipbookTimer = null;
                    }
                    
                    if (DeviceManager.SelectedCameraDevice != null)
                    {
                        DeviceManager.SelectedCameraDevice.StopRecordMovie();
                        Log.Debug("FlipbookButton_Click: Camera video recording stopped");
                        
                        // Restore photo settings after recording
                        RestorePhotoSettingsAfterRecording();
                    }
                    
                    // Hide countdown overlay after recording
                    countdownOverlay.Visibility = Visibility.Collapsed;
                    
                    // Restore the photo booth start button
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Visible;
                        Log.Debug("FlipbookButton_Click: Restored start photobooth button");
                    }
                    
                    // Don't restart live view here - DisplayVideoInLiveView will handle it through ReturnToLiveView
                    Log.Debug("FlipbookButton_Click: Live view will be handled by video display completion");
                    
                    // Keep the flag true until the video is received
                    Log.Debug($"FlipbookButton_Click: Keeping isRecordingFlipbook=true until video is processed");
                    // isRecordingFlipbook = false;  // Don't clear this here - let ProcessFlipbookVideo clear it
                    ShowFlipbookRecordingIndicator(false);
                    
                    UpdateStatusText("Flipbook recording completed - processing...");
                    // Note: The actual video file will be handled by PhotoCaptured event
                }
            }
            catch (Exception ex)
            {
                Log.Error($"FlipbookButton_Click: Error: {ex.Message}");
                UpdateStatusText("Flipbook error");
                isRecordingFlipbook = false;
                ShowFlipbookRecordingIndicator(false);
                
                // Hide countdown overlay on error
                countdownOverlay.Visibility = Visibility.Collapsed;
                
                // Restore the photo booth start button on error
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
                
                // On error, we should restart live view since video won't be displayed
                if (DeviceManager.SelectedCameraDevice != null)
                {
                    try
                    {
                        DeviceManager.SelectedCameraDevice.StartLiveView();
                        if (liveViewTimer != null && !liveViewTimer.IsEnabled)
                        {
                            liveViewTimer.Start();
                        }
                        Log.Debug("FlipbookButton_Click: Restarted live view after error");
                    }
                    catch
                    {
                        // Ignore errors when trying to restart live view
                    }
                }
                
                if (flipbookTimer != null)
                {
                    flipbookTimer.Stop();
                    flipbookTimer = null;
                }
            }
        }
        
        private void FlipbookTimer_Tick(object sender, EventArgs e)
        {
            // Increment elapsed time (now in 100ms increments)
            flipbookElapsedSeconds++;
            
            // Calculate actual elapsed seconds and progress
            double elapsedSeconds = flipbookElapsedSeconds / 10.0; // Convert from 100ms units to seconds
            double progress = elapsedSeconds / 4.0; // Progress from 0 to 1 over 4 seconds
            
            // Update display with progress
            UpdateFlipbookProgressDisplay(elapsedSeconds, progress);
            
            // Stop after 4 seconds (40 ticks of 100ms)
            if (flipbookElapsedSeconds >= 40)
            {
                flipbookTimer.Stop();
            }
        }
        
        private void ShowFlipbookRecordingIndicator(bool show)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var button = flipbookButton;
                if (button != null)
                {
                    var content = FindVisualChild<StackPanel>(button, "flipbookButtonContent");
                    var indicator = FindVisualChild<StackPanel>(button, "flipbookRecordingIndicator");
                    
                    if (content != null && indicator != null)
                    {
                        content.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
                        indicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }));
        }
        
        private void UpdateFlipbookTimeDisplay(int seconds)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var button = flipbookButton;
                if (button != null)
                {
                    var textBlock = FindVisualChild<TextBlock>(button, "flipbookTimeText");
                    if (textBlock != null)
                    {
                        textBlock.Text = $"{seconds}s";
                    }
                }
            }));
        }
        
        private void UpdateFlipbookProgressDisplay(double elapsedSeconds, double progress)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Update status text with remaining time
                int remainingSeconds = (int)(4 - elapsedSeconds);
                UpdateStatusText($"Recording flipbook... {remainingSeconds}s");
                
                // DON'T show countdown overlay during recording - it was already used for countdown
                // Just update the flipbook button display
                
                // Update flipbook button time display
                var button = flipbookButton;
                if (button != null)
                {
                    var textBlock = FindVisualChild<TextBlock>(button, "flipbookTimeText");
                    if (textBlock != null)
                    {
                        textBlock.Text = $"{elapsedSeconds:F1}s / 4s";
                    }
                }
            }));
        }
        
        private async void ProcessBoomerangVideo(string videoPath)
        {
            try
            {
                Log.Debug($"ProcessBoomerangVideo: Starting to process video: {videoPath}");
                UpdateStatusText("Creating boomerang effect...");
                
                // Create boomerang using FFmpeg (forward + reverse)
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    Log.Error($"ProcessBoomerangVideo: ffmpeg.exe not found at {ffmpegPath}");
                    UpdateStatusText("Boomerang error: FFmpeg not found");
                    return;
                }
                
                // Generate output path for boomerang
                string outputDir = Path.GetDirectoryName(videoPath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string boomerangPath = Path.Combine(outputDir, $"boomerang_{timestamp}.mp4");
                
                Log.Debug($"ProcessBoomerangVideo: Creating boomerang at: {boomerangPath}");
                
                // FFmpeg command to create boomerang effect (forward + reverse)
                // 1. Create reverse video
                // 2. Concatenate original + reverse
                string tempReversePath = Path.Combine(Path.GetTempPath(), $"reverse_{timestamp}.mp4");
                string tempListPath = Path.Combine(Path.GetTempPath(), $"concat_{timestamp}.txt");
                
                await Task.Run(() =>
                {
                    try
                    {
                        // Step 1: Create reversed video
                        var reverseArgs = $"-i \"{videoPath}\" -vf reverse -c:v libx264 -preset fast -crf 23 -y \"{tempReversePath}\"";
                        
                        var reverseProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = reverseArgs,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };
                        
                        Log.Debug($"ProcessBoomerangVideo: Creating reverse video with FFmpeg: {reverseArgs}");
                        reverseProcess.Start();
                        string reverseError = reverseProcess.StandardError.ReadToEnd();
                        reverseProcess.WaitForExit(10000); // 10 second timeout
                        
                        if (reverseProcess.ExitCode != 0 || !File.Exists(tempReversePath))
                        {
                            Log.Error($"ProcessBoomerangVideo: Failed to create reverse video. Exit code: {reverseProcess.ExitCode}");
                            Log.Error($"ProcessBoomerangVideo: FFmpeg error: {reverseError}");
                            UpdateStatusText("Failed to create boomerang");
                            return;
                        }
                        
                        // Step 2: Create concat file
                        File.WriteAllText(tempListPath, 
                            $"file '{videoPath.Replace('\\', '/')}'\r\n" +
                            $"file '{tempReversePath.Replace('\\', '/')}'");
                        
                        // Step 3: Concatenate forward and reverse videos
                        var concatArgs = $"-f concat -safe 0 -i \"{tempListPath}\" -c:v libx264 -preset fast -crf 23 -movflags +faststart -y \"{boomerangPath}\"";
                        
                        var concatProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = concatArgs,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };
                        
                        Log.Debug($"ProcessBoomerangVideo: Concatenating videos with FFmpeg: {concatArgs}");
                        concatProcess.Start();
                        string concatError = concatProcess.StandardError.ReadToEnd();
                        concatProcess.WaitForExit(10000); // 10 second timeout
                        
                        if (concatProcess.ExitCode != 0 || !File.Exists(boomerangPath))
                        {
                            Log.Error($"ProcessBoomerangVideo: Failed to create boomerang. Exit code: {concatProcess.ExitCode}");
                            Log.Error($"ProcessBoomerangVideo: FFmpeg error: {concatError}");
                            UpdateStatusText("Failed to create boomerang");
                            return;
                        }
                        
                        // Clean up temp files
                        try 
                        { 
                            File.Delete(tempReversePath); 
                            File.Delete(tempListPath);
                        } 
                        catch { }
                        
                        Log.Debug($"ProcessBoomerangVideo: Boomerang created successfully at: {boomerangPath}");
                        
                        // Display the boomerang in live view
                        Dispatcher.Invoke(() =>
                        {
                            UpdateStatusText("Boomerang created!");
                            DisplayVideoInLiveView(boomerangPath);
                            
                            // Auto-upload boomerang to cloud
                            _ = Task.Run(async () => await AutoUploadVideoSession(boomerangPath, "boomerang"));
                            
                            // Add to photo strip
                            var boomerangItem = new PhotoStripItem
                            {
                                PhotoNumber = photoStripItems.Count + 1,
                                IsPlaceholder = false,
                                ItemType = "Boomerang",
                                FilePath = boomerangPath
                            };
                            photoStripItems.Add(boomerangItem);
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"ProcessBoomerangVideo: Error creating boomerang: {ex.Message}");
                        Log.Error($"ProcessBoomerangVideo: Full exception: {ex}");
                        UpdateStatusText($"Boomerang error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                UpdateStatusText($"Boomerang error: {ex.Message}");
                Log.Error($"ProcessBoomerangVideo: Error: {ex.Message}");
                Log.Error($"ProcessBoomerangVideo: Full exception: {ex}");
            }
            finally
            {
                // Clear the boomerang capturing flag
                isCapturingBoomerang = false;
                Log.Debug($"ProcessBoomerangVideo: Cleared isCapturingBoomerang flag");
            }
        }
        
        private async void ProcessFlipbookVideo(string videoPath)
        {
            try
            {
                Log.Debug($"ProcessFlipbookVideo: Starting to process video: {videoPath}");
                UpdateStatusText("Creating flipbook pages...");
                
                // Initialize flipbook service if needed
                if (flipbookService == null)
                {
                    flipbookService = new FlipbookService();
                    Log.Debug("ProcessFlipbookVideo: Initialized FlipbookService");
                }
                
                Log.Debug($"ProcessFlipbookVideo: Calling CreateFlipbookFromVideo with path: {videoPath}");
                // Process the video into flipbook strips
                var result = await flipbookService.CreateFlipbookFromVideo(videoPath);
                
                if (result != null && result.FlipbookStrips != null && result.FlipbookStrips.Count > 0)
                {
                    UpdateStatusText($"Flipbook created! {result.FlipbookStrips.Count} strips ready for printing");
                    Log.Debug($"ProcessFlipbookVideo: Created {result.FlipbookStrips.Count} flipbook strips");
                    
                    // Display the MP4 animation in live view
                    if (!string.IsNullOrEmpty(result.Mp4Path) && File.Exists(result.Mp4Path))
                    {
                        Log.Debug($"ProcessFlipbookVideo: Displaying flipbook MP4: {result.Mp4Path}");
                        DisplayVideoInLiveView(result.Mp4Path);
                        
                        // Auto-upload flipbook MP4 to cloud
                        _ = Task.Run(async () => await AutoUploadVideoSession(result.Mp4Path, "flipbook"));
                        
                        // Don't call ShowBoomerangPreview - it internally calls DisplayVideoInLiveView again
                        // Just add to photo strip directly
                        var flipbookItem = new PhotoStripItem
                        {
                            PhotoNumber = photoStripItems.Count + 1,
                            IsPlaceholder = false,
                            ItemType = "Flipbook",
                            FilePath = result.Mp4Path
                        };
                        photoStripItems.Add(flipbookItem);
                    }
                    
                    // Auto-printing disabled per user request
                    // User can manually print strips if needed
                    // if (Properties.Settings.Default.EnablePrinting && result.FlipbookStrips.Count > 0)
                    // {
                    //     // Print the first strip as a sample
                    //     PrintFlipbookStrip(result.FlipbookStrips[0]);
                    // }
                }
                else
                {
                    UpdateStatusText("Failed to create flipbook");
                    Log.Error("ProcessFlipbookVideo: Failed to create flipbook strips");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText($"Flipbook error: {ex.Message}");
                Log.Error($"ProcessFlipbookVideo: Error: {ex.Message}");
                Log.Error($"ProcessFlipbookVideo: Full exception: {ex}");
            }
            finally
            {
                // Clear the flipbook recording flag
                isRecordingFlipbook = false;
                Log.Debug($"ProcessFlipbookVideo: Cleared isRecordingFlipbook flag");
            }
        }
        
        private void PrintFlipbookStrip(string stripPath)
        {
            try
            {
                // Use the 2x6 printer for flipbook strips
                if (printingService == null)
                {
                    printingService = new PrintingService();
                }
                
                // Queue for 2x6 printing
                _ = printingService.PrintImageAsync(stripPath, null, true); // true = is2x6
                UpdateStatusText("Printing flipbook strip...");
                Log.Debug($"PrintFlipbookStrip: Queued flipbook strip for printing: {stripPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"PrintFlipbookStrip: Error: {ex.Message}");
            }
        }
        
        private void OnBoomerangFrameCaptured(object sender, int frameCount)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Update the frame counter in the button
                var button = boomerangButton;
                if (button != null)
                {
                    var textBlock = FindVisualChild<TextBlock>(button, "boomerangFrameCountText");
                    if (textBlock != null)
                    {
                        textBlock.Text = $"{frameCount}/{modulesConfig.BoomerangFrames}";
                    }
                }
                
                UpdateStatusText($"Capturing frame {frameCount}/{modulesConfig.BoomerangFrames}");
            }));
        }
        
        private void OnBoomerangCreated(object sender, string filePath)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Unsubscribe from events
                boomerangService.FrameCaptured -= OnBoomerangFrameCaptured;
                boomerangService.BoomerangCreated -= OnBoomerangCreated;
                
                UpdateStatusText("Boomerang saved!");
            }));
        }
        
        private void ShowBoomerangPreview(string boomerangPath)
        {
            try
            {
                // Add to photo strip as a special item
                var boomerangItem = new PhotoStripItem
                {
                    PhotoNumber = photoStripItems.Count + 1,
                    IsPlaceholder = false,
                    ItemType = "Boomerang",
                    FilePath = boomerangPath
                };
                
                photoStripItems.Add(boomerangItem);
                
                // Use the same display method as video recordings (live view display)
                if (boomerangPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || 
                    boomerangPath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug($"ShowBoomerangPreview: Displaying boomerang in live view: {boomerangPath}");
                    DisplayVideoInLiveView(boomerangPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ShowBoomerangPreview: Error: {ex.Message}");
            }
        }
        
        private string currentVideoPath;
        private DispatcherTimer videoDisplayTimer;
        
        private void DisplayVideoInLiveView(string videoPath)
        {
            try
            {
                if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
                {
                    Log.Error($"DisplayVideoInLiveView: Video file not found: {videoPath}");
                    return;
                }

                Log.Debug($"DisplayVideoInLiveView: Displaying video in live view: {videoPath}");
                
                // Stop live view timer to prevent camera feed from overriding video
                if (liveViewTimer != null)
                {
                    liveViewTimer.Stop();
                }

                // Stop any existing video display timer
                if (videoDisplayTimer != null)
                {
                    videoDisplayTimer.Stop();
                    videoDisplayTimer = null;
                }

                // Use the main live view image control to display the video
                // We'll create a MediaElement to play the video in place of the camera feed
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Remove any existing video elements first
                        CleanupVideoElements();
                        
                        // Create a MediaElement for video playback in the live view area
                        var videoElement = new MediaElement
                        {
                            Source = new Uri(videoPath),
                            LoadedBehavior = MediaState.Manual,
                            UnloadedBehavior = MediaState.Close,
                            Stretch = Stretch.Uniform,
                            Name = "liveViewVideoPlayer"
                        };

                        // Position it over the live view image
                        if (liveViewImage != null)
                        {
                            var parent = liveViewImage.Parent as Panel;
                            if (parent != null)
                            {
                                // Add video element to same container as live view image
                                parent.Children.Add(videoElement);
                                
                                // Hide the camera live view
                                liveViewImage.Visibility = Visibility.Hidden;
                                
                                // Start playing the video
                                videoElement.Play();
                                
                                // Store reference for cleanup
                                currentVideoPath = videoPath;
                                
                                Log.Debug("DisplayVideoInLiveView: Video element added and playing");
                                
                                // Set up timeout to return to live view after 10 seconds (for flipbook videos)
                                bool isFlipbook = videoPath.IndexOf("flipbook", StringComparison.OrdinalIgnoreCase) >= 0;
                                if (isFlipbook)
                                {
                                    videoDisplayTimer = new DispatcherTimer();
                                    videoDisplayTimer.Interval = TimeSpan.FromSeconds(10);
                                    videoDisplayTimer.Tick += (s, e) =>
                                    {
                                        videoDisplayTimer.Stop();
                                        Log.Debug("DisplayVideoInLiveView: Video display timeout - returning to live view");
                                        ReturnToLiveView(isFlipbook);
                                    };
                                    videoDisplayTimer.Start();
                                }
                                
                                // Set up event to return to live view when video ends
                                videoElement.MediaEnded += (s, e) =>
                                {
                                    if (videoDisplayTimer != null)
                                    {
                                        videoDisplayTimer.Stop();
                                    }
                                    ReturnToLiveView(isFlipbook);
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"DisplayVideoInLiveView: Error creating video element: {ex.Message}");
                        ReturnToLiveView(false); // Fallback to live view
                    }
                }));
            }
            catch (Exception ex)
            {
                Log.Error($"DisplayVideoInLiveView: Error: {ex.Message}");
                ReturnToLiveView(false); // Fallback to live view
            }
        }
        
        private void CleanupVideoElements()
        {
            if (liveViewImage != null)
            {
                var parent = liveViewImage.Parent as Panel;
                if (parent != null)
                {
                    // Find and remove all video elements
                    var mediaElements = parent.Children.OfType<MediaElement>().ToList();
                    foreach (var element in mediaElements)
                    {
                        element.Stop();
                        element.Close();
                        parent.Children.Remove(element);
                    }
                }
            }
        }

        private void DisplayGifInLiveView(string gifPath)
        {
            try
            {
                if (string.IsNullOrEmpty(gifPath) || !File.Exists(gifPath))
                {
                    Log.Error($"DisplayGifInLiveView: GIF file not found: {gifPath}");
                    return;
                }

                Log.Debug($"DisplayGifInLiveView: Displaying GIF in live view: {gifPath}");
                
                // Stop live view timer to prevent camera feed from overriding GIF
                if (liveViewTimer != null)
                {
                    liveViewTimer.Stop();
                }

                // Use the main live view image control to display the GIF
                // We'll create a MediaElement to play the GIF in place of the camera feed
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Create a MediaElement for GIF playback in the live view area
                        var gifElement = new MediaElement
                        {
                            Source = new Uri(gifPath),
                            LoadedBehavior = MediaState.Manual,
                            UnloadedBehavior = MediaState.Close,
                            Stretch = Stretch.Uniform,
                            Name = "liveViewGifPlayer"
                        };

                        // Position it over the live view image
                        if (liveViewImage != null)
                        {
                            var parent = liveViewImage.Parent as Panel;
                            if (parent != null)
                            {
                                // Add GIF element to same container as live view image
                                parent.Children.Add(gifElement);
                                
                                // Hide the camera live view
                                liveViewImage.Visibility = Visibility.Hidden;
                                
                                // Start playing the GIF
                                gifElement.Play();
                                
                                Log.Debug("DisplayGifInLiveView: GIF element added and playing");
                                
                                // Set up event to return to live view when GIF ends
                                // For GIFs, we'll add a timer since they loop indefinitely
                                var gifTimer = new DispatcherTimer
                                {
                                    Interval = TimeSpan.FromSeconds(5) // Show GIF for 5 seconds
                                };
                                
                                gifTimer.Tick += (s, e) =>
                                {
                                    gifTimer.Stop();
                                    ReturnToLiveView(false);
                                };
                                
                                gifTimer.Start();
                                
                                // Also handle MediaEnded in case GIF stops
                                gifElement.MediaEnded += (s, e) =>
                                {
                                    gifTimer.Stop();
                                    ReturnToLiveView(false);
                                };
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"DisplayGifInLiveView: Error creating GIF element: {ex.Message}");
                        ReturnToLiveView(false); // Fallback to live view
                    }
                }));
            }
            catch (Exception ex)
            {
                Log.Error($"DisplayGifInLiveView: Error: {ex.Message}");
                ReturnToLiveView(false); // Fallback to live view
            }
        }

        private void ReturnToLiveView(bool isFromFlipbook = false)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Stop any video display timer
                    if (videoDisplayTimer != null)
                    {
                        videoDisplayTimer.Stop();
                        videoDisplayTimer = null;
                    }
                    
                    // Remove any video and GIF elements
                    if (liveViewImage != null)
                    {
                        var parent = liveViewImage.Parent as Panel;
                        if (parent != null)
                        {
                            // Find and remove video and GIF elements
                            var mediaElements = parent.Children.OfType<MediaElement>().ToList();
                            foreach (var element in mediaElements)
                            {
                                element.Stop();
                                element.Close();
                                parent.Children.Remove(element);
                            }
                        }
                        
                        // Show the camera live view again
                        liveViewImage.Visibility = Visibility.Visible;
                    }
                    
                    // For flipbook, live view should already be running from when recording ended
                    // Don't restart it again to avoid conflicts
                    if (!isFromFlipbook)
                    {
                        // Handle live view based on idle setting (same as app initialization)
                        if (DeviceManager?.SelectedCameraDevice != null)
                        {
                            if (Properties.Settings.Default.EnableIdleLiveView)
                            {
                                // Start live view if idle live view is enabled
                                try
                                {
                                    if (liveViewTimer != null)
                                    {
                                        liveViewTimer.Start();
                                    }
                                    DeviceManager.SelectedCameraDevice.StartLiveView();
                                    Log.Debug("ReturnToLiveView: Started idle live view");
                                }
                                catch (Exception ex)
                                {
                                    Log.Debug($"ReturnToLiveView: Failed to start idle live view: {ex.Message}");
                                }
                            }
                            else
                            {
                                // Stop live view if idle live view is disabled
                                try
                                {
                                    if (liveViewTimer != null)
                                    {
                                        liveViewTimer.Stop();
                                    }
                                    DeviceManager.SelectedCameraDevice.StopLiveView();
                                    Log.Debug("ReturnToLiveView: Stopped live view (idle live view disabled)");
                                }
                                catch (Exception ex)
                                {
                                    Log.Debug($"ReturnToLiveView: Failed to stop live view: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Log.Debug("ReturnToLiveView: Skipping live view restart for flipbook (already running)");
                        // Just ensure the timer is running if it should be
                        if (liveViewTimer != null && !liveViewTimer.IsEnabled && Properties.Settings.Default.EnableIdleLiveView)
                        {
                            liveViewTimer.Start();
                        }
                    }
                    
                    // Ensure camera is ready for photo mode after video display
                    if (DeviceManager.SelectedCameraDevice != null)
                    {
                        DeviceManager.SelectedCameraDevice.IsBusy = false;
                        Log.Debug("ReturnToLiveView: Cleared camera IsBusy flag");
                    }
                    
                    // Don't clear template for flipbook - we're still in the same session
                    if (!isFromFlipbook)
                    {
                        // Return to main idle state (same as app startup)
                        // Clear current template to reset workflow for next session
                        currentTemplate = null;
                        Log.Debug("ReturnToLiveView: Cleared currentTemplate to reset workflow");
                    }
                    
                    // Ensure templates are loaded if we have an event (same as initialization logic)
                    if (currentEvent != null)
                    {
                        DebugService.LogDebug($"ReturnToLiveView: Loading templates for event: {currentEvent.Name}");
                        eventTemplateService.LoadAvailableTemplates(currentEvent.Id);
                        Log.Debug($"ReturnToLiveView: Loaded {eventTemplateService.AvailableTemplates?.Count ?? 0} templates for event {currentEvent.Name}");
                    }
                    
                    // Show start button based on same logic as initialization
                    if (startButtonOverlay != null)
                    {
                        bool shouldShowStartButton = (currentTemplate != null) || 
                            (currentEvent != null && eventTemplateService.AvailableTemplates != null && eventTemplateService.AvailableTemplates.Count > 0);
                        
                        startButtonOverlay.Visibility = shouldShowStartButton ? Visibility.Visible : Visibility.Collapsed;
                        Log.Debug($"ReturnToLiveView: Start button visibility = {startButtonOverlay.Visibility}, shouldShow={shouldShowStartButton}");
                    }
                    
                    // Ensure template selection overlay is hidden (return to main idle state)
                    if (eventSelectionOverlay != null)
                    {
                        eventSelectionOverlay.Visibility = Visibility.Collapsed;
                        Log.Debug("ReturnToLiveView: Hidden template selection overlay");
                    }
                    
                    // Update status display (same as initialization)
                    RefreshDisplay();
                    
                    Log.Debug("ReturnToLiveView: Returned to camera live view with proper UI state");
                }));
            }
            catch (Exception ex)
            {
                Log.Error($"ReturnToLiveView: Error: {ex.Message}");
            }
        }
        
        // Method already exists above, removing duplicate
        
        private void VideoPlaybackOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            // Don't close if clicking on controls
            if (e.OriginalSource == videoPlayerOverlay)
            {
                // Could optionally close the overlay when clicking outside
                // CloseVideoOverlay();
            }
        }
        
        private void PlayPauseButton_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (videoPlayer.Position >= videoPlayer.NaturalDuration.TimeSpan)
                {
                    // If at end, restart
                    videoPlayer.Position = TimeSpan.Zero;
                }
                
                videoPlayer.Play();
                playPauseButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error($"PlayPauseButton_Click: Error: {ex.Message}");
            }
        }
        
        private void PlayAgainButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                videoPlayer.Position = TimeSpan.Zero;
                videoPlayer.Play();
                playPauseButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error($"PlayAgainButton_Click: Error: {ex.Message}");
            }
        }
        
        private void SaveVideoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(currentVideoPath))
                {
                    // Video is already saved in the session folder
                    UpdateStatusText("Video saved to session!");
                    
                    // Optionally copy to a special location or process further
                    // You could also trigger upload to cloud here
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SaveVideoButton_Click: Error: {ex.Message}");
            }
        }
        
        private void ShareVideoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(currentVideoPath))
                {
                    // Share the video - could upload to cloud and get shareable link
                    // For now, just show message
                    UpdateStatusText("Preparing video for sharing...");
                    
                    // TODO: Implement actual sharing functionality
                    // Could upload to S3 and generate a shareable link
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ShareVideoButton_Click: Error: {ex.Message}");
            }
        }
        
        private void CloseVideoButton_Click(object sender, RoutedEventArgs e)
        {
            CloseVideoOverlay();
        }
        
        private void CloseVideoOverlay()
        {
            try
            {
                videoPlayer.Stop();
                videoPlayer.Source = null;
                videoPlayerOverlay.Visibility = Visibility.Collapsed;
                currentVideoPath = null;
            }
            catch (Exception ex)
            {
                Log.Error($"CloseVideoOverlay: Error: {ex.Message}");
            }
        }
        
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild && typedChild.Name == name)
                    return typedChild;
                
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }
        
        // Video/Photo settings management methods
        private void ApplyVideoSettingsForRecording()
        {
            if (DeviceManager.SelectedCameraDevice == null) return;
            
            try
            {
                // Save current photo settings first
                savedPhotoSettings = new SavedPhotoSettings
                {
                    ISO = DeviceManager.SelectedCameraDevice.IsoNumber?.Value ?? "Auto",
                    Aperture = DeviceManager.SelectedCameraDevice.FNumber?.Value ?? "Auto",
                    ShutterSpeed = DeviceManager.SelectedCameraDevice.ShutterSpeed?.Value ?? "Auto",
                    WhiteBalance = DeviceManager.SelectedCameraDevice.WhiteBalance?.Value ?? "Auto",
                    FocusMode = DeviceManager.SelectedCameraDevice.FocusMode?.Value ?? "Auto",
                    ExposureCompensation = DeviceManager.SelectedCameraDevice.ExposureCompensation?.Value ?? "0"
                };
                
                Log.Debug($"Saved photo settings - ISO: {savedPhotoSettings.ISO}, Aperture: {savedPhotoSettings.Aperture}");
                
                // Apply video settings from Properties.Settings
                var settings = Properties.Settings.Default;
                
                if (!string.IsNullOrEmpty(settings.VideoISO) && settings.VideoISO != "Auto")
                {
                    SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.IsoNumber, settings.VideoISO);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoAperture) && settings.VideoAperture != "Auto")
                {
                    SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.FNumber, settings.VideoAperture);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoShutterSpeed) && settings.VideoShutterSpeed != "Auto")
                {
                    SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.ShutterSpeed, settings.VideoShutterSpeed);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoWhiteBalance) && settings.VideoWhiteBalance != "Auto")
                {
                    SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.WhiteBalance, settings.VideoWhiteBalance);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoFocusMode))
                {
                    SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.FocusMode, settings.VideoFocusMode);
                }
                
                if (!string.IsNullOrEmpty(settings.VideoExposureCompensation))
                {
                    SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.ExposureCompensation, settings.VideoExposureCompensation);
                }
                
                Log.Debug($"Applied video settings - ISO: {settings.VideoISO}, Aperture: {settings.VideoAperture}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error applying video settings: {ex.Message}");
            }
        }
        
        private void RestorePhotoSettingsAfterRecording()
        {
            if (DeviceManager.SelectedCameraDevice == null || savedPhotoSettings == null) return;
            
            try
            {
                SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.IsoNumber, savedPhotoSettings.ISO);
                SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.FNumber, savedPhotoSettings.Aperture);
                SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.ShutterSpeed, savedPhotoSettings.ShutterSpeed);
                SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.WhiteBalance, savedPhotoSettings.WhiteBalance);
                SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.FocusMode, savedPhotoSettings.FocusMode);
                SetCameraPropertyToValue(DeviceManager.SelectedCameraDevice.ExposureCompensation, savedPhotoSettings.ExposureCompensation);
                
                Log.Debug($"Restored photo settings - ISO: {savedPhotoSettings.ISO}, Aperture: {savedPhotoSettings.Aperture}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error restoring photo settings: {ex.Message}");
            }
        }
        
        private void SetCameraPropertyToValue(PropertyValue<long> property, string targetValue)
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
                Log.Debug($"Error setting property value: {ex.Message}");
            }
        }
        
        // INotifyPropertyChanged implementation for data binding
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        #endregion

        #endregion
    }
}