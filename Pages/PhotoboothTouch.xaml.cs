using CameraControl.Devices;
using CameraControl.Devices.Classes;
using System;
using System.Collections.Generic;
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
    // Photo strip item for displaying both captured photos and placeholders
    public class PhotoStripItem : System.ComponentModel.INotifyPropertyChanged
    {
        private BitmapImage _image;
        private bool _isPlaceholder = true;
        private int _photoNumber;
        private bool _isSelected = false;
        private string _itemType = "Photo"; // "Photo", "Composed", "GIF"
        private string _filePath;
        
        public BitmapImage Image 
        { 
            get => _image; 
            set 
            { 
                _image = value; 
                OnPropertyChanged("Image");
            } 
        }
        
        public bool IsPlaceholder 
        { 
            get => _isPlaceholder; 
            set 
            { 
                _isPlaceholder = value; 
                OnPropertyChanged("IsPlaceholder");
            } 
        }
        
        public int PhotoNumber 
        { 
            get => _photoNumber; 
            set 
            { 
                _photoNumber = value; 
                OnPropertyChanged("PhotoNumber");
            } 
        }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged("IsSelected");
            }
        }
        
        public string ItemType
        {
            get => _itemType;
            set
            {
                _itemType = value;
                OnPropertyChanged("ItemType");
            }
        }
        
        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged("FilePath");
            }
        }
        
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
    
    // Retake photo item for review grid
    public class RetakePhotoItem : INotifyPropertyChanged
    {
        private bool _markedForRetake;
        
        public BitmapImage Image { get; set; }
        public string Label { get; set; }
        public int PhotoIndex { get; set; }
        public string FilePath { get; set; }
        
        public bool MarkedForRetake
        {
            get => _markedForRetake;
            set
            {
                _markedForRetake = value;
                OnPropertyChanged(nameof(MarkedForRetake));
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public partial class PhotoboothTouch : Page
    {
        private System.Threading.CancellationTokenSource currentCaptureToken;
        // Use singleton camera manager to maintain session across screens
        public CameraDeviceManager DeviceManager => CameraSessionManager.Instance.DeviceManager;
        public string FolderForPhotos { get; set; }
        
        private DispatcherTimer liveViewTimer;
        private DispatcherTimer countdownTimer;
        private int countdownSeconds = 5;
        private int currentCountdown = 5;
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
        
        // Filter service
        private PhotoFilterService filterService;
        
        // Printer monitoring
        private PrinterMonitorService printerMonitor;

        public PhotoboothTouch()
        {
            InitializeComponent();
            
            // Use singleton camera manager - don't create new instance
            // Event subscriptions moved to Loaded event to prevent duplicates

            // Initialize services
            photoboothService = new PhotoboothService();
            eventService = new EventService();
            database = new TemplateDatabase();
            filterService = new PhotoFilterService();
            
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

            // Initialize UI
            countdownSecondsText.Text = countdownSeconds.ToString();
            photoCountText.Text = photoCount.ToString();
            
            // Bind photo strip to collection
            photoStripControl.ItemsSource = photoStripItems;
            
            // Bind retake grid to collection
            retakePhotoGrid.ItemsSource = retakePhotos;
            
            Loaded += PhotoboothTouch_Loaded;
            Unloaded += PhotoboothTouch_Unloaded;
        }

        private void PhotoboothTouch_Unloaded(object sender, RoutedEventArgs e)
        {
            Log.Debug("PhotoboothTouch_Unloaded: Page is being unloaded, cleaning up camera resources");
            
            try
            {
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
        }

        private void PhotoboothTouch_Loaded(object sender, RoutedEventArgs e)
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
            startButton.IsEnabled = true;
            stopButton.IsEnabled = false;
            countdownOverlay.Visibility = Visibility.Collapsed;
            
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
            
            // Use exact same synchronous approach as working Camera.xaml.cs
            try
            {
                // Wait a moment to ensure previous page has released camera
                System.Threading.Thread.Sleep(500);
                
                DeviceManager.ConnectToCamera();
                RefreshDisplay();
                
                // Ensure camera is not in live view mode when page loads
                if (DeviceManager?.SelectedCameraDevice != null)
                {
                    try
                    {
                        // Try to stop live view if it might be running
                        DeviceManager.SelectedCameraDevice.StopLiveView();
                        Log.Debug("PhotoboothTouch_Loaded: Stopped live view that was already running");
                    }
                    catch 
                    { 
                        // Expected if live view wasn't running
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
            if (DeviceManager.SelectedCameraDevice == null)
            {
                statusText.Text = "No camera connected";
                return;
            }

            if (isCapturing)
                return;

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
            StopPhotoSequence();
            
            if (currentEvent != null && currentTemplate != null)
            {
                statusText.Text = $"Event: {currentEvent.Name} - Touch START to continue";
            }
            else
            {
                statusText.Text = "Touch START to begin";
            }
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
                
                // Add placeholder boxes for all photos needed
                UpdatePhotoStripPlaceholders();
                
                Log.Debug($"StartPhotoSequence: Cleared capturedPhotoPaths list and photo strip (starting new sequence)");
            }
            
            try
            {
                isCapturing = true;
                startButton.IsEnabled = false;
                stopButton.IsEnabled = true;
                
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

        private void UpdatePhotoStripPlaceholders()
        {
            photoStripItems.Clear();
            
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
            
            startButton.IsEnabled = true;
            stopButton.IsEnabled = false;
            
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
                    
                    // More photos needed - stop current sequence and prepare for next
                    statusText.Text = $"Photo {currentPhotoIndex} saved! Photo {currentPhotoIndex + 1} of {totalPhotosNeeded} starting soon...";
                    
                    // Stop current photo sequence to reset camera state
                    Log.Debug("HandlePhotoSequenceProgress: Calling StopPhotoSequence()");
                    StopPhotoSequence();
                    
                    // Preview delay before next photo
                    Log.Debug("HandlePhotoSequenceProgress: Starting 4-second delay before next photo");
                    Task.Delay(4000).ContinueWith(_ =>
                    {
                        Log.Debug("HandlePhotoSequenceProgress: 4-second delay complete, checking camera state");
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
                                    startButton.IsEnabled = true;
                                }
                            }
                            else
                            {
                                Log.Debug("HandlePhotoSequenceProgress: No camera - showing manual prompt");
                                // No camera - show manual prompt
                                statusText.Text = $"Camera not connected - Touch START for photo {currentPhotoIndex + 1} of {totalPhotosNeeded}";
                                startButton.IsEnabled = true;
                            }
                        });
                    });
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
                countdownSecondsText.Text = countdownSeconds.ToString();
            }
        }

        private void DecreaseCountdown_Click(object sender, RoutedEventArgs e)
        {
            if (countdownSeconds > 1)
            {
                countdownSeconds--;
                countdownSecondsText.Text = countdownSeconds.ToString();
            }
        }

        // Event Handlers
        private void DeviceManager_CameraConnected(ICameraDevice cameraDevice)
        {
            Dispatcher.Invoke(() =>
            {
                cameraStatusText.Text = $"Connected: {cameraDevice.DeviceName}";
                
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
            
            // Check if FileName is null (common with Sony SDK)
            // Only generate filename if it's truly null/empty - don't modify Canon filenames
            if (string.IsNullOrEmpty(eventArgs.FileName))
            {
                Log.Debug("PhotoCaptured: FileName is null or empty, generating default filename");
                // Generate a default filename for cameras that don't provide one (like Sony)
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                eventArgs.FileName = $"IMG_{timestamp}.jpg";
            }
            
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
                
                // Save the final composed image
                string outputDir = Path.Combine(FolderForPhotos, "Composed");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string outputPath = Path.Combine(outputDir, $"{currentEvent.Name}_{timestamp}.jpg");
                
                // Save as high-quality JPEG
                var jpegEncoder = ImageCodecInfo.GetImageEncoders()
                    .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    Encoder.Quality, 95L);
                
                finalBitmap.Save(outputPath, jpegEncoder, encoderParams);
                finalBitmap.Dispose();
                
                Log.Debug($"ComposeTemplateWithPhotos: Saved composed image to {outputPath}");
                return outputPath;
            }
            catch (Exception ex)
            {
                Log.Error("ComposeTemplateWithPhotos: Failed to compose template", ex);
                return null;
            }
        }
        
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("ExitButton_Click: User clicked exit, performing cleanup");
            
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
            
            Log.Debug("ExitButton_Click: Closing window");
            
            // Close this window
            Window.GetWindow(this)?.Close();
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
            // Clear current event to allow selecting a new one
            currentEvent = null;
            currentTemplate = null;
            currentPhotoIndex = 0;
            totalPhotosNeeded = 0;
            capturedPhotoPaths.Clear();
            
            ShowEventSelectionOverlay();
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
        
        private void GalleryButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("GalleryButton_Click: Opening gallery overlay");
            
            try
            {
                // Show the gallery overlay
                galleryOverlay.Show();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to open gallery overlay", ex);
                MessageBox.Show($"Failed to open gallery: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CameraSettingsButton_Click(object sender, RoutedEventArgs e)
        {
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
                if (Properties.Settings.Default.EnableFilters && Properties.Settings.Default.AllowFilterChange)
                {
                    // Show filter-only review (simplified version of retake review)
                    ShowFilterOnlyReview();
                    return;
                }
                else if (Properties.Settings.Default.EnableFilters)
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
            
            // Initialize filter controls based on settings
            if (Properties.Settings.Default.EnableFilters)
            {
                // Show filter section
                filterSectionContainer.Visibility = Visibility.Visible;
                
                // Show filter UI only if filters are enabled in settings
                if (Properties.Settings.Default.AllowFilterChange)
                {
                    // Show filter selection if users are allowed to change filters
                    enableFiltersCheckBox.Visibility = Visibility.Visible;
                    enableFiltersCheckBox.IsChecked = true;
                    filterSelectionControl.Visibility = Visibility.Visible;
                    
                    // Use the first photo as the preview source
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
                else
                {
                    // Filters enabled but users can't change them - hide UI but apply default filter
                    enableFiltersCheckBox.Visibility = Visibility.Collapsed;
                    filterSelectionControl.Visibility = Visibility.Collapsed;
                    
                    // Set default filter if specified
                    if (Properties.Settings.Default.DefaultFilter > 0)
                    {
                        FilterType defaultFilter = (FilterType)Properties.Settings.Default.DefaultFilter;
                        filterSelectionControl.SetSelectedFilter(defaultFilter);
                    }
                }
            }
            else
            {
                // Filters completely disabled - hide entire filter section
                filterSectionContainer.Visibility = Visibility.Collapsed;
                enableFiltersCheckBox.Visibility = Visibility.Collapsed;
                filterSelectionControl.Visibility = Visibility.Collapsed;
                
                // Ensure no filter is selected
                filterSelectionControl.SetSelectedFilter(FilterType.None);
            }
            
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
                startButton.IsEnabled = false;
                stopButton.IsEnabled = true;
                
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
            
            Task.Run(async () =>
            {
                try
                {
                    Log.Debug($"ProcessTemplateWithPhotos: Processing {capturedPhotoPaths.Count} photos into template");
                    
                    // Apply filters only if enabled in settings
                    if (Properties.Settings.Default.EnableFilters)
                    {
                        FilterType selectedFilter = FilterType.None;
                        
                        // Try to get the selected filter from the control if available
                        if (filterSelectionControl != null)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                selectedFilter = filterSelectionControl.GetSelectedFilter();
                            });
                        }
                        
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
                                string filteredPath = await ApplyFilterToPhoto(capturedPhotoPaths[i], selectedFilter);
                                filteredPaths.Add(filteredPath);
                            }
                            
                            // Update paths to use filtered versions
                            capturedPhotoPaths = filteredPaths;
                        }
                    }
                    else
                    {
                        Log.Debug("Filters are disabled - skipping filter application");
                    }
                    
                    // Process the template with the captured photos
                    string processedImagePath = await ComposeTemplateWithPhotos();
                    
                    if (!string.IsNullOrEmpty(processedImagePath) && File.Exists(processedImagePath))
                    {
                        Log.Debug($"ProcessTemplateWithPhotos: Template processed successfully: {processedImagePath}");
                        
                        Dispatcher.Invoke(() =>
                        {
                            // Show the processed image
                            liveViewImage.Source = new BitmapImage(new Uri(processedImagePath));
                            statusText.Text = "Photos processed successfully!";
                            
                            // Optional: Auto-stop after processing (with delay)
                            Task.Delay(3000).ContinueWith(_ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    StopPhotoSequence();
                                    statusText.Text = "Session complete - Touch START for new session";
                                    
                                    // Automatically stop after processing template
                                    Task.Delay(500).ContinueWith(__ =>
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            // Reset for next session
                                            currentPhotoIndex = 0;
                                            capturedPhotoPaths.Clear();
                                            photoStripImages.Clear();
                                            photoStripItems.Clear();
                                            
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
                                                UpdatePhotoStripPlaceholders();
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
        
        private async Task<string> ApplyFilterToPhoto(string inputPath, FilterType filterType)
        {
            try
            {
                if (filterType == FilterType.None)
                    return inputPath;
                    
                string outputPath = Path.Combine(
                    Path.GetDirectoryName(inputPath),
                    $"{Path.GetFileNameWithoutExtension(inputPath)}_filtered{Path.GetExtension(inputPath)}"
                );
                
                float intensity = Properties.Settings.Default.FilterIntensity / 100f;
                string filteredPath = filterService.ApplyFilterToFile(inputPath, outputPath, filterType, intensity);
                
                return filteredPath;
            }
            catch (Exception ex)
            {
                Log.Error($"ApplyFilterToPhoto: Failed to apply filter to {inputPath}", ex);
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
    }
}