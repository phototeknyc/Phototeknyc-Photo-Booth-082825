using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Controls;
using Photobooth.Controls.ModularComponents;
using Photobooth.Database;
using Photobooth.Models;
using Photobooth.Services;
using Photobooth.ViewModels;
using Photobooth.Properties;
using static Photobooth.Services.PhotoboothSessionService;
using static Photobooth.Services.PhotoboothWorkflowService;
using static Photobooth.Services.PhotoboothUIService;
using static Photobooth.Services.PhotoCompositionService;

namespace Photobooth.Pages
{
    /// <summary>
    /// Clean refactored PhotoboothTouchModern page using existing services architecture
    /// 
    /// ═══ CLEAN ARCHITECTURE PATTERN - FOLLOW THIS TO AVOID BLOAT ═══
    /// 
    /// This page follows a CLEAN SERVICE-ORIENTED ARCHITECTURE:
    /// 
    /// 🎯 PURPOSE: This page should ONLY handle:
    ///     - UI event routing (button clicks, etc.)
    ///     - Service coordination
    ///     - UI updates based on service events
    /// 
    /// ❌ NEVER ADD TO THIS PAGE:
    ///     - Business logic (photo processing, session management, etc.)
    ///     - File operations (copying, saving, etc.)
    ///     - Database operations (beyond simple service calls)
    ///     - Complex image processing
    ///     - Manual camera operations
    ///     - Direct UI manipulations (except simple property updates)
    /// 
    /// ✅ INSTEAD USE THESE SERVICES:
    ///     - PhotoboothSessionService: Complete session workflow management
    ///     - PhotoboothWorkflowService: Camera operations, countdown, capture orchestration
    ///     - PhotoboothUIService: All UI updates and notifications
    ///     - PhotoCaptureService: Photo capture and file management
    ///     - ImageProcessingService: Image manipulations
    ///     - PrintingService: Printing operations
    ///     - SharingOperations: Cloud sharing and QR codes (if available)
    /// 
    /// 🏗️ ARCHITECTURE FLOW:
    ///     1. User interacts with UI
    ///     2. Page routes to appropriate service method
    ///     3. Service handles business logic and fires events
    ///     4. Page responds to service events with UI updates
    ///     5. Keep page methods under 10 lines when possible
    /// 
    /// 📝 ADDING NEW FEATURES:
    ///     1. Create new service if functionality doesn't exist
    ///     2. Add event handlers in page to respond to service events
    ///     3. Keep UI updates in PhotoboothUIService when possible
    ///     4. Never add complex logic directly to this page
    /// 
    /// 🔧 REFACTORING RULE:
    ///     If a method in this page is longer than 15 lines or does complex operations,
    ///     it should be moved to a service. This keeps the page maintainable.
    /// 
    /// ═══════════════════════════════════════════════════════════════
    /// </summary>
    public partial class PhotoboothTouchModernRefactored : Page
    {
        #region Services - Clean Architecture (Follow the rules above!)
        // Core services - these do all the heavy lifting
        private Services.PhotoboothSessionService _sessionService;
        private Services.PhotoboothWorkflowService _workflowService; 
        private Services.PhotoboothUIService _uiService;
        private Services.PhotoCompositionService _compositionService;
        private Services.EventGalleryService _galleryService;
        private Services.GalleryBrowserService _galleryBrowserService;
        private Services.GalleryActionService _galleryActionService;
        private Services.SharingUIService _sharingUIService;
        
        // Supporting services - using existing services from Services folder
        private EventTemplateService _eventTemplateService;
        private Services.TemplateSelectionService _templateSelectionService;
        private Services.TemplateSelectionUIService _templateSelectionUIService;
        private PrintingService _printingService;
        private IShareService _shareService;
        private PhotoCaptureService _photoCaptureService;
        private DatabaseOperations _databaseOps;
        private PhotoboothService _photoboothService;
        private OfflineQueueService _offlineQueueService;
        private CameraSessionManager _cameraManager;
        private PhotoboothTouchModernViewModel _viewModel;
        private SessionManager _sessionManager;
        
        // Database connection
        private DatabaseOperations _database;
        #endregion

        #region Minimal State Management (Keep UI-only state here)
        private DispatcherTimer _liveViewTimer;
        private DispatcherTimer _countdownTimer;
        private bool _isDisplayingCapturedPhoto = false; // Flag to prevent live view from overwriting captured photo
        private bool _isCapturing = false; // Flag to track if we're actively capturing a photo
        private bool _isDisplayingSessionResult = false; // Flag to prevent live view from overwriting session result
        
        private void SetDisplayingSessionResult(bool value)
        {
            if (_isDisplayingSessionResult != value)
            {
                _isDisplayingSessionResult = value;
                Log.Debug($"★★★ FLAG CHANGED: _isDisplayingSessionResult = {value}");
            }
        }
        private ShareResult _currentShareResult;
        
        // Event/Template state for UI display only
        private EventData _currentEvent;
        private TemplateData _currentTemplate;
        
        // Current module
        private IPhotoboothModule _activeModule;
        private ModuleManager _moduleManager;
        private UILayoutService _uiLayoutService;
        
        // Gallery navigation state
        private List<Services.SessionGalleryData> _gallerySessions;
        private int _currentGallerySessionIndex;
        private Services.SessionGalleryData _currentGallerySession;
        private bool _isInGalleryMode;
        #endregion

        #region Properties
        // Use singleton camera manager to maintain session across screens like original
        public CameraDeviceManager DeviceManager => _cameraManager?.DeviceManager;
        #endregion

        public PhotoboothTouchModernRefactored()
        {
            InitializeComponent();
            InitializeServices();
            InitializeTimers();
            InitializeModules();
            
            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        #region Initialization
        private void InitializeServices()
        {
            Log.Debug("Initializing clean services architecture...");
            
            // Core camera system
            _cameraManager = CameraSessionManager.Instance;
            
            // Initialize CLEAN SERVICES that do all the work
            _sessionService = new Services.PhotoboothSessionService();
            _workflowService = new Services.PhotoboothWorkflowService(_cameraManager, _sessionService);
            _uiService = new Services.PhotoboothUIService();
            _compositionService = new Services.PhotoCompositionService();
            _galleryService = new Services.EventGalleryService();
            _galleryBrowserService = new Services.GalleryBrowserService();
            
            // Initialize existing services from Services folder
            _eventTemplateService = new EventTemplateService();
            _printingService = new PrintingService();
            _shareService = CloudShareProvider.GetShareService();
            _sessionManager = new SessionManager();
            
            // Initialize sharing UI service for modal display management first
            // Pass the MainGrid instead of the page so UI elements can be added properly
            _sharingUIService = new Services.SharingUIService(MainGrid);
            
            // Wire sharing UI service events
            _sharingUIService.QrCodeOverlayClosed += OnQrCodeOverlayClosed;
            _sharingUIService.SmsOverlayClosed += OnSmsOverlayClosed;
            _sharingUIService.SendSmsRequested += OnSendSmsRequested;
            _sharingUIService.ShowSmsFromQrRequested += OnShowSmsFromQrRequested;
            
            // Initialize unified action service (requires other services including SharingUIService)
            _galleryActionService = new Services.GalleryActionService(
                _sessionManager,
                _printingService,
                _sessionService,
                _galleryService,
                _uiService,
                _shareService,
                _sharingUIService
            );
            // Use a single DatabaseOperations instance to avoid multiple database connections
            _databaseOps = new DatabaseOperations();
            _database = _databaseOps; // Use same instance
            _photoCaptureService = new PhotoCaptureService(_databaseOps); // Share the instance
            _photoboothService = new PhotoboothService();
            // SharingOperations requires parent page - remove it, use share service instead
            _offlineQueueService = OfflineQueueService.Instance;
            _uiLayoutService = new UILayoutService();
            _templateSelectionService = new Services.TemplateSelectionService();
            _templateSelectionUIService = new Services.TemplateSelectionUIService();
            
            // View model will be created in Page_Loaded to avoid stack overflow
            // _viewModel = new PhotoboothTouchModernViewModel();
            // _viewModel.DeviceManager = _cameraManager.DeviceManager;
            // DataContext = _viewModel;
            
            // Wire up service events
            SetupServiceEventHandlers();
            
            Log.Debug("Clean services architecture initialized successfully");
        }

        private void InitializeTimers()
        {
            _liveViewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _liveViewTimer.Tick += LiveViewTimer_Tick;
            
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
        }
        
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            // Handled by workflow service now
        }

        private void InitializeModules()
        {
            // Use singleton instance
            _moduleManager = ModuleManager.Instance;
            
            // Initialize modules when camera is ready
            var outputFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth"
            );
            
            // Initialize will be called later when camera is ready
            if (DeviceManager?.SelectedCameraDevice != null)
            {
                _moduleManager.Initialize(DeviceManager.SelectedCameraDevice, outputFolder);
            }
            
            // Set default module to Photo
            _activeModule = _moduleManager.GetModule("Photo");
        }
        #endregion

        #region Page Lifecycle
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Create ViewModel after services are initialized
            if (_viewModel == null)
            {
                _viewModel = new PhotoboothTouchModernViewModel();
                _viewModel.DeviceManager = _cameraManager.DeviceManager;
                DataContext = _viewModel;
            }
            
            // Initialize camera
            await InitializeCamera();
            
            // Initialize modules with camera
            if (DeviceManager?.SelectedCameraDevice != null)
            {
                var outputFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth"
                );
                _moduleManager.Initialize(DeviceManager.SelectedCameraDevice, outputFolder);
            }
            
            // Apply UI layout if enabled (disabled temporarily)
            // ApplyUILayout();
            
            // Setup event handlers
            SetupEventHandlers();
            
            // Load event/template from static properties
            LoadInitialEventTemplate();
            
            // Update UI
            UpdateUI();
            
            // Show the START button
            _uiService?.ShowStartButton();
            
            
            // Load gallery preview
            LoadGalleryPreview();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            Cleanup();
        }

        public void Cleanup()
        {
            try
            {
                _liveViewTimer?.Stop();
                _countdownTimer?.Stop();
                
                // Stop live view through device
                DeviceManager?.SelectedCameraDevice?.StopLiveView();
                
                RemoveEventHandlers();
                
                _activeModule?.Cleanup();
                _viewModel?.Cleanup();
            }
            catch (Exception ex)
            {
                Log.Error($"Cleanup error: {ex.Message}");
            }
        }

        private void SetupEventHandlers()
        {
            SessionSelectionModal.SessionSelected += OnSessionSelected;
            SessionSelectionModal.ModalClosed += OnModalClosed;
            
            // Gallery browser modal events
            if (GalleryBrowserModal != null)
            {
                GalleryBrowserModal.SessionSelected += OnGallerySessionSelected;
                GalleryBrowserModal.ModalClosed += OnGalleryModalClosed;
            }
            
            // Subscribe to camera events like original PhotoboothTouchModern
            if (DeviceManager != null)
            {
                DeviceManager.CameraSelected += DeviceManager_CameraSelected;
                DeviceManager.CameraConnected += DeviceManager_CameraConnected; 
                // REMOVED: DeviceManager.PhotoCaptured - Let workflow service handle this
                DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;
                Log.Debug("SetupEventHandlers: Subscribed to camera events (except PhotoCaptured - handled by workflow)");
            }
        }
        
        private void SetupServiceEventHandlers()
        {
            // Wire up service events for clean architecture
            if (_sessionService != null)
            {
                _sessionService.SessionStarted += OnServiceSessionStarted;
                _sessionService.PhotoProcessed += OnServicePhotoProcessed;
                _sessionService.SessionCompleted += OnServiceSessionCompleted;
                Log.Debug($"PhotoboothTouchModernRefactored: Subscribed to SessionCompleted event");
                _sessionService.SessionError += OnServiceSessionError;
                _sessionService.SessionCleared += OnServiceSessionCleared;
                _sessionService.AutoClearTimerExpired += OnServiceAutoClearTimerExpired;
                _sessionService.AnimationReady += OnServiceAnimationReady;
            }
            
            if (_workflowService != null)
            {
                _workflowService.CountdownStarted += OnServiceCountdownStarted;
                _workflowService.CountdownTick += OnServiceCountdownTick;
                _workflowService.CountdownCompleted += OnServiceCountdownCompleted;
                _workflowService.CaptureStarted += OnServiceCaptureStarted;
                _workflowService.CaptureCompleted += OnServiceCaptureCompleted;
                _workflowService.PhotoDisplayRequested += OnServicePhotoDisplayRequested;
                _workflowService.PhotoDisplayCompleted += OnServicePhotoDisplayCompleted;
                _workflowService.WorkflowError += OnServiceWorkflowError;
                _workflowService.StatusChanged += OnServiceStatusChanged;
            }
            
            if (_uiService != null)
            {
                _uiService.UIUpdateRequested += OnServiceUIUpdateRequested;
                _uiService.ThumbnailRequested += OnServiceThumbnailRequested;
                _uiService.GalleryThumbnailRequested += OnServiceGalleryThumbnailRequested;
                _uiService.StatusUpdateRequested += OnServiceStatusUpdateRequested;
                _uiService.ImageDisplayRequested += OnServiceImageDisplayRequested;
                _uiService.GifDisplayRequested += OnServiceGifDisplayRequested;
            }
            
            if (_compositionService != null)
            {
                _compositionService.CompositionStarted += OnServiceCompositionStarted;
                _compositionService.CompositionCompleted += OnServiceCompositionCompleted;
                _compositionService.CompositionError += OnServiceCompositionError;
            }
            
            if (_galleryService != null)
            {
                _galleryService.GalleryLoaded += OnServiceGalleryLoaded;
                _galleryService.GalleryError += OnServiceGalleryError;
                _galleryService.SessionSaved += OnServiceSessionSaved;
                _galleryService.GalleryDisplayRequested += OnServiceGalleryDisplayRequested;
                _galleryService.SessionLoadRequested += OnServiceSessionLoadRequested;
            }
            
            if (_templateSelectionService != null)
            {
                _templateSelectionService.TemplateSelectionRequested += OnTemplateSelectionRequested;
                _templateSelectionService.TemplateSelected += OnTemplateSelected;
                _templateSelectionService.TemplateSelectionCancelled += OnTemplateSelectionCancelled;
            }
            
            if (_templateSelectionUIService != null)
            {
                _templateSelectionUIService.TemplateCardClicked += OnTemplateCardClicked;
                _templateSelectionUIService.ShowOverlayRequested += OnShowOverlayRequested;
                _templateSelectionUIService.HideOverlayRequested += OnHideOverlayRequested;
            }
            
            Log.Debug("Service event handlers wired up");
        }

        private void ApplyUILayout()
        {
            try
            {
                Log.Debug("ApplyUILayout: Applying UI layout customizations");
                
                // Apply layout if UILayoutService is available
                if (_uiLayoutService != null)
                {
                    _uiLayoutService.ApplyLayoutToPage(this, MainGrid);
                    Log.Debug("ApplyUILayout: UI layout applied successfully");
                }
                else
                {
                    Log.Debug("ApplyUILayout: UILayoutService not available, using default layout");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ApplyUILayout error: {ex.Message}");
            }
        }

        private void RemoveEventHandlers()
        {
            SessionSelectionModal.SessionSelected -= OnSessionSelected;
            SessionSelectionModal.ModalClosed -= OnModalClosed;
            
            // Unsubscribe from camera events to prevent duplicate handlers
            if (DeviceManager != null)
            {
                DeviceManager.CameraSelected -= DeviceManager_CameraSelected;
                DeviceManager.CameraConnected -= DeviceManager_CameraConnected;
                // REMOVED: DeviceManager.PhotoCaptured - Not subscribed anymore
                DeviceManager.CameraDisconnected -= DeviceManager_CameraDisconnected;
                Log.Debug("RemoveEventHandlers: Unsubscribed from camera events");
            }
            
            // Unsubscribe from service events
            RemoveServiceEventHandlers();
        }
        
        private void RemoveServiceEventHandlers()
        {
            if (_sessionService != null)
            {
                _sessionService.SessionStarted -= OnServiceSessionStarted;
                _sessionService.PhotoProcessed -= OnServicePhotoProcessed;
                _sessionService.SessionCompleted -= OnServiceSessionCompleted;
                _sessionService.SessionError -= OnServiceSessionError;
                _sessionService.SessionCleared -= OnServiceSessionCleared;
                _sessionService.AutoClearTimerExpired -= OnServiceAutoClearTimerExpired;
                _sessionService.AnimationReady -= OnServiceAnimationReady;
            }
            
            if (_workflowService != null)
            {
                _workflowService.CountdownStarted -= OnServiceCountdownStarted;
                _workflowService.CountdownTick -= OnServiceCountdownTick;
                _workflowService.CountdownCompleted -= OnServiceCountdownCompleted;
                _workflowService.CaptureStarted -= OnServiceCaptureStarted;
                _workflowService.CaptureCompleted -= OnServiceCaptureCompleted;
                _workflowService.PhotoDisplayRequested -= OnServicePhotoDisplayRequested;
                _workflowService.PhotoDisplayCompleted -= OnServicePhotoDisplayCompleted;
                _workflowService.WorkflowError -= OnServiceWorkflowError;
                _workflowService.StatusChanged -= OnServiceStatusChanged;
            }
            
            if (_uiService != null)
            {
                _uiService.UIUpdateRequested -= OnServiceUIUpdateRequested;
                _uiService.ThumbnailRequested -= OnServiceThumbnailRequested;
                _uiService.GalleryThumbnailRequested -= OnServiceGalleryThumbnailRequested;
                _uiService.StatusUpdateRequested -= OnServiceStatusUpdateRequested;
                _uiService.ImageDisplayRequested -= OnServiceImageDisplayRequested;
                _uiService.GifDisplayRequested -= OnServiceGifDisplayRequested;
            }
            
            if (_compositionService != null)
            {
                _compositionService.CompositionStarted -= OnServiceCompositionStarted;
                _compositionService.CompositionCompleted -= OnServiceCompositionCompleted;
                _compositionService.CompositionError -= OnServiceCompositionError;
            }
            
            if (_galleryService != null)
            {
                _galleryService.GalleryLoaded -= OnServiceGalleryLoaded;
                _galleryService.GalleryError -= OnServiceGalleryError;
                _galleryService.SessionSaved -= OnServiceSessionSaved;
                _galleryService.GalleryDisplayRequested -= OnServiceGalleryDisplayRequested;
                _galleryService.SessionLoadRequested -= OnServiceSessionLoadRequested;
            }
        }
        
        /// <summary>
        /// Handle gallery service events
        /// </summary>
        private void OnServiceGalleryLoaded(object sender, Services.GalleryLoadedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Gallery loaded: {e.TotalSessions} sessions, {e.TotalPhotos} photos");
                
                // Store gallery sessions for navigation
                if (e.GalleryData?.Sessions != null && e.GalleryData.Sessions.Any())
                {
                    _gallerySessions = e.GalleryData.Sessions;
                    _currentGallerySessionIndex = 0;
                    _isInGalleryMode = true;
                    
                    // Hide the Touch to Start button when gallery is loaded
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Collapsed;
                    }
                    
                    _uiService.UpdateStatus($"Gallery loaded: {e.TotalPhotos} photos in {e.TotalSessions} sessions");
                }
            });
        }
        
        private void OnServiceGalleryError(object sender, Services.GalleryErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Error($"Gallery error in {e.Operation}: {e.Error.Message}");
                _uiService.UpdateStatus($"Gallery error: {e.Operation}");
            });
        }
        
        private void OnServiceSessionSaved(object sender, Services.SessionSavedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Session {e.SessionId} saved to gallery: {e.PhotoCount} photos");
                _uiService.UpdateStatus($"Session saved to gallery: {e.PhotoCount} photos");
            });
        }
        
        private void OnServiceGalleryDisplayRequested(object sender, Services.GalleryRequestEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Gallery now loads into main view, not overlay
                if (e.Action == "Show")
                {
                    Log.Debug("Gallery loading into main view");
                    _uiService.UpdateStatus("Loading gallery sessions...");
                }
                else if (e.Action == "Hide")
                {
                    Log.Debug("Gallery mode exited");
                    _uiService.UpdateStatus("Gallery closed");
                }
            });
        }
        
        private void OnServiceSessionLoadRequested(object sender, Services.SessionLoadRequestEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (e.Session == null || e.Photos == null)
                    {
                        Log.Debug("Invalid session data for loading");
                        return;
                    }
                    
                    // Store current gallery session
                    _currentGallerySession = e.Session;
                    
                    // Clear current photos in the strip
                    if (photosContainer != null)
                    {
                        int beforeClear = photosContainer.Children.Count;
                        photosContainer.Children.Clear();
                        Log.Debug($"OnServiceSessionLoadRequested: Cleared {beforeClear} existing items from photosContainer");
                    }
                    else
                    {
                        Log.Error("OnServiceSessionLoadRequested: photosContainer is null!");
                    }
                    
                    // Load session photos using UI service (business logic)
                    Log.Debug($"OnServiceSessionLoadRequested: Loading {e.Photos.Count} photos into photo strip using UI service:");
                    
                    foreach (var photo in e.Photos)
                    {
                        Log.Debug($"  Photo: {photo.FileName} (Type: {photo.PhotoType}) - Path: {photo.FilePath}");
                        
                        if (!File.Exists(photo.FilePath))
                        {
                            Log.Debug($"  File not found: {photo.FilePath}, skipping");
                            continue;
                        }
                        
                        // Skip displaying 4x6_print thumbnails in gallery (but keep them in session for printing)
                        if (photo.PhotoType == "4x6_print")
                        {
                            Log.Debug($"  Skipping 4x6_print thumbnail display (keeping for print): {photo.FilePath}");
                            continue;
                        }
                        
                        // For gallery loading, call local methods directly to ensure click handlers are attached
                        switch (photo.PhotoType)
                        {
                            case "GIF":
                            case "MP4":
                                // For gallery sessions, we need to pass original photos for MP4 thumbnail
                                // Get the first original photo for MP4 thumbnail
                                string firstPhotoPath = null;
                                if (photo.PhotoType == "MP4")
                                {
                                    var firstOrigPhoto = e.Photos.FirstOrDefault(p => (p.PhotoType == "ORIG" || p.PhotoType == "Original") && File.Exists(p.FilePath));
                                    firstPhotoPath = firstOrigPhoto?.FilePath;
                                    Log.Debug($"  Using first original photo for MP4 thumbnail: {firstPhotoPath}");
                                }
                                
                                // Call method with gallery context
                                AddGifThumbnailForGallery(photo.FilePath, firstPhotoPath);
                                Log.Debug($"  Added {photo.PhotoType} thumbnail with click handler: {photo.FilePath}");
                                break;
                            case "COMP":
                            case "2x6":  // Handle 2x6 composed images
                                // Call local method directly to ensure proper display
                                AddComposedThumbnail(photo.FilePath);
                                Log.Debug($"  Added composed thumbnail: {photo.FilePath}");
                                break;
                            case "ORIG":
                            case "Original":
                            default:
                                // Call local method for original photos
                                AddPhotoThumbnail(photo.FilePath);
                                Log.Debug($"  Added photo thumbnail: {photo.FilePath}");
                                break;
                        }
                    }
                    
                    // Check how many are actually in the container
                    int containerCount = photosContainer?.Children.Count ?? 0;
                    Log.Debug($"Photos in container after loading: {containerCount} (expected: {e.Photos.Count})");
                    
                    // Update status with session info
                    _uiService.UpdateStatus($"Viewing: {e.Session.SessionName} ({e.Photos.Count} photos)");
                    
                    // Show gallery navigation buttons with proper index
                    var sessionIndex = _currentGallerySessionIndex;
                    var totalSessions = _gallerySessions?.Count ?? 1;
                    ShowGalleryNavigationButtons(sessionIndex, totalSessions);
                    
                    Log.Debug($"Loaded session {e.Session.SessionName} with {e.Photos.Count} photos");
                }
                catch (Exception ex)
                {
                    Log.Error($"Error loading session into view: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Extract photo index from filename for proper thumbnail ordering
        /// </summary>
        private int ExtractPhotoIndexFromFilename(string filename)
        {
            try
            {
                // Try to extract number from filenames like "photo_01.jpg", "photo_02.jpg", etc.
                var match = System.Text.RegularExpressions.Regex.Match(filename, @"(\d+)");
                if (match.Success)
                {
                    return int.Parse(match.Groups[1].Value);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Failed to extract photo index from filename {filename}: {ex.Message}");
            }
            
            // Default to 1 if extraction fails
            return 1;
        }
        
        
        private void ShowGalleryNavigationButtons(int currentIndex, int totalSessions)
        {
            // Ensure we're on the UI thread
            Dispatcher.Invoke(() =>
            {
                // Show unified action buttons panel for gallery mode
                if (actionButtonsPanel != null)
                {
                    actionButtonsPanel.Visibility = Visibility.Visible;
                    _isInGalleryMode = true;
                    Log.Debug($"★★★ ShowGalleryNavigationButtons: Showing action buttons panel for gallery session (Visibility: {actionButtonsPanel.Visibility})");
                    
                    // Also log individual button visibility and set them to Visible
                    if (printButton != null)
                    {
                        printButton.Visibility = Visibility.Visible;
                        Log.Debug($"  - Print button visibility: {printButton.Visibility}");
                    }
                    if (shareButton != null)
                    {
                        shareButton.Visibility = Visibility.Visible;
                        Log.Debug($"  - Share button visibility: {shareButton.Visibility}");
                    }
                    if (emailButton != null)
                    {
                        emailButton.Visibility = Visibility.Visible;
                        Log.Debug($"  - Email button visibility: {emailButton.Visibility}");
                    }
                }
                else
                {
                    Log.Error("★★★ actionButtonsPanel is null - cannot show gallery navigation buttons");
                }
                
                // Hide the Touch to Start button when in gallery mode
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Collapsed;
                    Log.Debug("Hiding Touch to Start button for gallery mode");
                }
                
                // Hide other UI elements that might interfere
                // TODO: Implement HideActionButtons if needed
                
                Log.Debug($"Gallery action buttons shown for session {currentIndex + 1} of {totalSessions}");
            });
        }
        #endregion

        #region Camera Event Handlers
        private void DeviceManager_CameraSelected(ICameraDevice oldcameraDevice, ICameraDevice newcameraDevice)
        {
            Log.Debug($"DeviceManager_CameraSelected: {oldcameraDevice?.DeviceName} -> {newcameraDevice?.DeviceName}");
            // Update UI on main thread
            Dispatcher.BeginInvoke(new Action(() => 
            {
                UpdateCameraStatus(newcameraDevice?.IsConnected == true ? 
                    $"Connected: {newcameraDevice.DeviceName}" : "Camera selected");
            }));
        }

        private void DeviceManager_CameraConnected(ICameraDevice cameraDevice)
        {
            Log.Debug($"DeviceManager_CameraConnected: {cameraDevice?.DeviceName}");
            // Update UI on main thread
            Dispatcher.BeginInvoke(new Action(() => 
            {
                UpdateCameraStatus($"Connected: {cameraDevice.DeviceName}");
                // Start live view if idle live view is enabled and not already started
                if (!_liveViewTimer.IsEnabled && Properties.Settings.Default.EnableIdleLiveView)
                {
                    cameraDevice?.StartLiveView();
                    _liveViewTimer.Start();
                    Log.Debug("DeviceManager_CameraConnected: Started idle live view");
                }
                else if (!Properties.Settings.Default.EnableIdleLiveView)
                {
                    Log.Debug("DeviceManager_CameraConnected: Idle live view disabled");
                }
            }));
        }

        private void DeviceManager_CameraDisconnected(ICameraDevice cameraDevice)
        {
            Log.Debug($"DeviceManager_CameraDisconnected: {cameraDevice?.DeviceName}");
            // Update UI on main thread
            Dispatcher.BeginInvoke(new Action(() => 
            {
                UpdateCameraStatus("Camera disconnected");
                _liveViewTimer.Stop();
                liveViewImage.Source = null;
            }));
        }

        // REMOVED: DeviceManager_PhotoCaptured - This was causing duplicate processing
        // The workflow service now handles all photo capture events directly

        #endregion

        #region Service Event Handlers - Clean Architecture
        private void OnServiceSessionStarted(object sender, Services.SessionStartedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Service session started: {e.SessionId}");
                _uiService.UpdateStatus($"Session started for {e.Event?.Name}");
                _uiService.ShowSessionControls();
                
                // Hide the start button when session starts
                _uiService.HideStartButton();
                Log.Debug("Start button hidden - session in progress");
                
                // Hide gallery preview during session
                UpdateGalleryPreviewVisibility(true);
                
                // Don't show stop button here - it will be shown when countdown starts
                // This prevents the button from appearing before the countdown
                
                // Ensure live view is running for the session (needed for capture)
                if (DeviceManager?.SelectedCameraDevice != null && !_liveViewTimer.IsEnabled)
                {
                    DeviceManager.SelectedCameraDevice.StartLiveView();
                    _liveViewTimer.Start();
                    Log.Debug("Live view started for photo session");
                }
            });
        }
        
        private async void OnServicePhotoProcessed(object sender, Services.PhotoProcessedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Log.Debug($"Service photo processed: {e.PhotoIndex} of {e.TotalPhotos}");
                
                // Add thumbnail using UI service
                _uiService.AddPhotoThumbnail(e.PhotoPath, e.PhotoIndex);
                
                // Update photo counter
                _uiService.UpdatePhotoCounter(e.PhotoIndex, e.TotalPhotos);
                
                if (!e.IsComplete)
                {
                    _uiService.UpdateStatus($"Photo {e.PhotoIndex} captured! Get ready for photo {e.PhotoIndex + 1}...");
                    
                    // Check if photographer mode is enabled
                    bool photographerMode = Properties.Settings.Default.PhotographerMode;
                    
                    if (photographerMode)
                    {
                        // In photographer mode, wait for manual trigger
                        Log.Debug($"Photographer mode enabled - waiting for manual trigger for photo {e.PhotoIndex + 1}");
                        _uiService.UpdateStatus($"Ready for photo {e.PhotoIndex + 1} - Press camera trigger when ready");
                        
                        // Start the workflow in photographer mode (it will wait for manual trigger)
                        await _workflowService.StartPhotoCaptureWorkflowAsync();
                    }
                    else
                    {
                        // Use configurable delay between photos
                        int delaySeconds = Properties.Settings.Default.DelayBetweenPhotos;
                        Log.Debug($"Waiting {delaySeconds} seconds before next photo");
                        
                        await Task.Delay(delaySeconds * 1000);
                        
                        // Start the next photo capture workflow
                        Log.Debug($"Starting capture workflow for photo {e.PhotoIndex + 1} of {e.TotalPhotos}");
                        await _workflowService.StartPhotoCaptureWorkflowAsync();
                    }
                }
                else
                {
                    // All photos captured - compose template and complete session
                    Log.Debug("All photos captured, composing template before completing session");
                    _uiService.UpdateStatus("Processing your photos...");
                    
                    // Animation generation will happen in background via ProcessSessionPhotos()
                    // and UI will be updated via OnServiceAnimationReady event
                    Log.Debug("Animation generation started in background - UI will update when ready");
                    
                    // Compose template if available
                    if (_currentTemplate != null && _sessionService.CapturedPhotoPaths?.Count > 0)
                    {
                        _uiService.UpdateStatus("Composing final image...");
                        var completedData = new CompletedSessionData
                        {
                            SessionId = _sessionService.CurrentSessionId,
                            Event = _sessionService.CurrentEvent,
                            Template = _sessionService.CurrentTemplate,
                            PhotoPaths = _sessionService.CapturedPhotoPaths
                        };
                        
                        string composedPath = await _compositionService.ComposeTemplateAsync(completedData);
                        if (!string.IsNullOrEmpty(composedPath))
                        {
                            // Get both paths from the composition service
                            string displayPath = _compositionService.LastDisplayPath ?? composedPath;
                            string printPath = _compositionService.LastPrintPath ?? composedPath;
                            
                            
                            // Set the composed image paths in the session
                            _sessionService.SetComposedImagePaths(displayPath, printPath);
                        }
                    }
                    
                    // Complete the session to trigger MP4 generation and auto-upload
                    Log.Debug("Completing session to trigger MP4 generation and auto-upload");
                    await _sessionService.CompleteSessionAsync();
                }
            });
        }
        
        private async void OnServiceSessionCompleted(object sender, Services.SessionCompletedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Log.Debug($"★★★ OnServiceSessionCompleted CALLED - CompletedSession is {(e.CompletedSession != null ? "NOT NULL" : "NULL")}");
                Log.Debug("Service session completed");
                if (e.CompletedSession != null)
                {
                    // STOP live view timer to prevent it from overwriting the composed image
                    _liveViewTimer?.Stop();
                    SetDisplayingSessionResult(true); // Set flag to prevent live view from restarting
                    Log.Debug("Stopped live view timer - session complete, set _isDisplayingSessionResult = true");

                    // Display composed image in live view if available
                    if (!string.IsNullOrEmpty(e.CompletedSession.ComposedImagePath) && File.Exists(e.CompletedSession.ComposedImagePath))
                    {
                        Log.Debug($"★★★ COMPOSED IMAGE PATH EXISTS: {e.CompletedSession.ComposedImagePath}");
                        Log.Debug($"★★★ Calling _uiService.DisplayImage to show composed image");
                        _uiService.DisplayImage(e.CompletedSession.ComposedImagePath);
                        
                        // Add composed image thumbnail to strip
                        _uiService.AddPhotoThumbnail(e.CompletedSession.ComposedImagePath, -1);
                    }
                    else
                    {
                        Log.Error($"★★★ NO COMPOSED IMAGE! Path: {e.CompletedSession.ComposedImagePath}, Exists: {(!string.IsNullOrEmpty(e.CompletedSession.ComposedImagePath) ? File.Exists(e.CompletedSession.ComposedImagePath).ToString() : "empty")}");
                    }
                    
                    // MP4/GIF will be added as thumbnail via OnServiceAnimationReady when ready
                    Log.Debug("Session completed - composed image displayed, waiting for animation");
                    
                    // Show completion UI
                    _uiService.UpdateStatus("Session complete!");
                    _uiService.ShowCompletionControls();
                    
                    // Show unified action buttons panel for current session (not gallery mode)
                    _isInGalleryMode = false;
                    if (actionButtonsPanel != null) 
                    {
                        actionButtonsPanel.Visibility = Visibility.Visible;
                        Log.Debug("Showing action buttons panel after session completion");
                    }
                    
                    // Show Gallery button to view saved sessions
                    if (galleryButton != null) galleryButton.Visibility = Visibility.Visible;
                    
                    // Start auto-clear timer through service if enabled
                    if (Properties.Settings.Default.AutoClearSession)
                    {
                        _sessionService.StartAutoClearTimer();
                    }
                    
                    // Handle auto-upload - always enabled
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000); // Brief delay before upload
                        await AutoUploadSessionPhotos(e.CompletedSession);
                    });
                }
            });
        }
        private void OnServiceSessionError(object sender, Services.SessionErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Error($"Service session error: {e.Error.Message}");
                _uiService.UpdateStatus($"Session error: {e.Operation}");
                _uiService.ShowStartButton();
            });
        }
        
        private void OnServiceCountdownStarted(object sender, Services.CountdownEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Service countdown started: {e.CountdownValue}");
                _uiService.ShowCountdown(e.CountdownValue, e.TotalSeconds);
                
                // Show stop button when countdown starts
                if (stopSessionButton != null)
                {
                    stopSessionButton.Visibility = Visibility.Visible;
                    Log.Debug("Stop button shown - countdown in progress");
                }
            });
        }
        
        private void OnServiceCountdownTick(object sender, Services.CountdownEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _uiService.ShowCountdown(e.CountdownValue, e.TotalSeconds);
            });
        }
        
        private void OnServiceCountdownCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug("Service countdown completed");
                _uiService.HideCountdown();
                
                // Hide stop button when countdown completes (capture will start)
                if (stopSessionButton != null)
                {
                    stopSessionButton.Visibility = Visibility.Collapsed;
                    Log.Debug("Stop button hidden - countdown completed");
                }
            });
        }
        
        private void OnServiceCaptureStarted(object sender, Services.CaptureEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Service capture started: Photo {e.PhotoIndex} of {e.TotalPhotos}");
                
                // Stop live view timer during capture (camera will stop live view)
                _liveViewTimer?.Stop();
                Log.Debug("Live view timer stopped for capture");
                
                _uiService.UpdateStatus("Capturing...");
            });
        }
        
        private void OnServiceCaptureCompleted(object sender, Services.CaptureEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Service capture completed: {e.PhotoPath}");
                _uiService.UpdateStatus("Processing photo...");
            });
        }
        private void OnServicePhotoDisplayRequested(object sender, Services.PhotoDisplayEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(e.PhotoPath) && System.IO.File.Exists(e.PhotoPath))
                    {
                        Log.Debug($"=== PHOTO DISPLAY REQUESTED ===");
                        Log.Debug($"Photo path: {e.PhotoPath}");
                        Log.Debug($"Display duration: {e.DisplayDuration} seconds");
                        Log.Debug($"Live view timer status: {(_liveViewTimer?.IsEnabled == true ? "RUNNING" : "STOPPED")}");
                        
                        // Prevent individual photo display when session results are being shown
                        if (_isDisplayingSessionResult)
                        {
                            Log.Debug("★★★ Prevented individual photo display during session results");
                            return;
                        }
                        
                        // Set flag to prevent live view from overwriting the photo
                        _isDisplayingCapturedPhoto = true;
                        
                        // Ensure live view timer is stopped
                        if (_liveViewTimer?.IsEnabled == true)
                        {
                            _liveViewTimer.Stop();
                            Log.Debug("Stopped live view timer for photo display");
                        }
                        
                        // Display the captured photo
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(e.PhotoPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
                        liveViewImage.Source = bitmap;
                        Log.Debug($"Photo displayed successfully, should remain visible for {e.DisplayDuration} seconds");
                        Log.Debug("Set _isDisplayingCapturedPhoto flag to true");
                    }
                    else
                    {
                        Log.Error($"Cannot display photo - Path: {e.PhotoPath}, Exists: {System.IO.File.Exists(e.PhotoPath)}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to display captured photo: {ex.Message}");
                }
            });
        }
        private void OnServicePhotoDisplayCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug("=== PHOTO DISPLAY COMPLETED ===");
                
                // Clear the flag to allow live view to update again
                _isDisplayingCapturedPhoto = false;
                Log.Debug("Cleared _isDisplayingCapturedPhoto flag");
                
                // Only restart live view if we're NOT displaying session results
                if (!_isDisplayingSessionResult)
                {
                    Log.Debug($"Starting live view timer to resume camera feed");
                    // Resume live view timer - it will update the image on next tick
                    _liveViewTimer?.Start();
                    Log.Debug("Live view timer restarted - camera feed will replace photo on next tick");
                }
                else
                {
                    Log.Debug("★★★ NOT restarting live view - session results are being displayed");
                }
                
                // The workflow service has already restarted camera live view
                // Our timer will pick it up and display it (only if not showing session results)
            });
        }
        
        private void OnServiceWorkflowError(object sender, Services.WorkflowErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Error($"Service workflow error: {e.Error.Message}");
                _uiService.UpdateStatus($"Workflow error: {e.Operation}");
                _uiService.ShowStartButton();
            });
        }
        
        private void OnServiceStatusChanged(object sender, Services.StatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _uiService.UpdateStatus(e.Status);
            });
        }
        
        private void OnServiceUIUpdateRequested(object sender, Services.UIUpdateEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Handle generic UI update requests from service
                try
                {
                    var element = FindName(e.ElementName) as FrameworkElement;
                    if (element != null)
                    {
                        if (e.Property == "Visibility" && e.Value is Visibility visibility)
                        {
                            element.Visibility = visibility;
                        }
                        else if (e.Property == "Text" && element is TextBlock textBlock)
                        {
                            textBlock.Text = e.Value?.ToString();
                        }
                        // Add more property handlers as needed
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"UI update error: {ex.Message}");
                }
            });
        }
        
        private void OnServiceThumbnailRequested(object sender, Services.ThumbnailEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"★★★ OnServiceThumbnailRequested: Type={e.ThumbnailType}, Path={e.ImagePath ?? e.PhotoPath}");
                
                // Handle different types of thumbnails based on ThumbnailType
                if (e.ThumbnailType == "GIF")
                {
                    Log.Debug($"★★★ Adding GIF/MP4 thumbnail via event: {e.ImagePath}");
                    AddGifThumbnail(e.ImagePath);
                }
                else if (e.ThumbnailType == "COMPOSED")
                {
                    Log.Debug($"★★★ Adding COMPOSED thumbnail via event: {e.ImagePath}");
                    AddComposedThumbnail(e.ImagePath);
                }
                else
                {
                    // Regular photo thumbnail
                    Log.Debug($"★★★ Adding regular photo thumbnail via event: {e.PhotoPath ?? e.ImagePath}");
                    AddPhotoThumbnail(e.PhotoPath ?? e.ImagePath);
                }
            });
        }
        
        private void OnServiceGalleryThumbnailRequested(object sender, Services.ThumbnailEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"OnServiceGalleryThumbnailRequested: Type={e.ThumbnailType}, Path={e.PhotoPath ?? e.ImagePath}");
                
                // Handle gallery thumbnails - call page methods directly to avoid event loops
                if (e.ThumbnailType == "GIF")
                {
                    Log.Debug("OnServiceGalleryThumbnailRequested: Adding GIF thumbnail");
                    AddGifThumbnail(e.ImagePath);
                }
                else if (e.ThumbnailType == "COMPOSED")
                {
                    Log.Debug("OnServiceGalleryThumbnailRequested: Adding COMPOSED thumbnail");
                    AddComposedThumbnail(e.ImagePath);
                }
                else
                {
                    // Regular photo thumbnail
                    var path = e.PhotoPath ?? e.ImagePath;
                    Log.Debug($"OnServiceGalleryThumbnailRequested: Adding regular photo thumbnail: {path}");
                    AddPhotoThumbnail(path);
                }
                
                // Log current container state
                int count = photosContainer?.Children.Count ?? 0;
                Log.Debug($"OnServiceGalleryThumbnailRequested: After adding, photosContainer has {count} children");
            });
        }
        
        private void OnServiceStatusUpdateRequested(object sender, Services.StatusEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatusText(e.Status);
            });
        }
        
        /// <summary>
        /// Handle composition service events
        /// </summary>
        private void OnServiceCompositionStarted(object sender, CompositionStartedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Composition started for session {e.SessionId}");
                _uiService.UpdateStatus($"Composing {e.TemplateName}...");
            });
        }
        
        private void OnServiceCompositionCompleted(object sender, CompositionCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Composition completed: {e.ComposedImagePath}");
                // Don't call SetComposedImagePath here as it would overwrite the print path
                // The paths are already set by UpdateProcessedImagePaths which has both display and print paths
                // Just display the image
                _uiService.DisplayImage(e.ComposedImagePath);
            });
        }
        
        private void OnServiceCompositionError(object sender, CompositionErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Error($"Composition error for session {e.SessionId}: {e.Error.Message}");
                _uiService.UpdateStatus("Template composition failed");
            });
        }
        
        /// <summary>
        /// Handle UI service display requests
        /// </summary>
        private void OnServiceImageDisplayRequested(object sender, ImageDisplayEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(e.ImagePath) || !File.Exists(e.ImagePath))
                    {
                        Log.Error($"Invalid image path for display: {e.ImagePath}");
                        return;
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(e.ImagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    if (liveViewImage != null)
                    {
                        liveViewImage.Source = bitmap;
                        Log.Debug($"★★★ Image SET in live view: {e.ImagePath}");
                        Log.Debug($"★★★ liveViewImage.Source is now: {liveViewImage.Source?.ToString() ?? "null"}");
                        Log.Debug($"★★★ _isDisplayingSessionResult flag: {_isDisplayingSessionResult}");
                    }
                    else
                    {
                        Log.Error($"★★★ liveViewImage is NULL! Cannot display: {e.ImagePath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to display image: {ex.Message}");
                }
            });
        }
        
        private void OnServiceGifDisplayRequested(object sender, GifDisplayEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(e.GifPath) || !File.Exists(e.GifPath))
                    {
                        Log.Error($"Invalid file path for display: {e.GifPath}");
                        return;
                    }

                    string fileExtension = Path.GetExtension(e.GifPath).ToLower();
                    bool isMP4 = fileExtension == ".mp4";
                    
                    Log.Debug($"Displaying {(isMP4 ? "MP4" : "GIF")} in live view: {e.GifPath}");
                    
                    // Stop live view timer to prevent camera feed from overriding display
                    _liveViewTimer?.Stop();
                    
                    if (isMP4)
                    {
                        // For MP4, we need to use a MediaElement
                        DisplayVideoInLiveView(e.GifPath);
                    }
                    else
                    {
                        // For GIF, use BitmapImage
                        var gifImage = new BitmapImage();
                        gifImage.BeginInit();
                        gifImage.UriSource = new Uri(e.GifPath, UriKind.Absolute);
                        gifImage.CacheOption = BitmapCacheOption.OnLoad;
                        gifImage.EndInit();
                        
                        if (liveViewImage != null)
                        {
                            liveViewImage.Source = gifImage;
                            Log.Debug("GIF displayed in live view - WPF will animate it automatically");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to display animation: {ex.Message}");
                }
            });
        }
        
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
                
                // Remove any existing video elements first
                CleanupVideoElements();
                
                // Create a MediaElement for video playback
                var videoElement = new System.Windows.Controls.MediaElement
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
                        
                        // Set up event handlers for looping
                        videoElement.MediaEnded += (s, args) =>
                        {
                            videoElement.Position = TimeSpan.Zero;
                            videoElement.Play();
                        };
                        
                        // Start playing the video
                        videoElement.Play();
                        
                        Log.Debug("MP4 video element created and playing");
                        
                        // Set up a timer to return to live view after a duration
                        var videoDisplayTimer = new System.Windows.Threading.DispatcherTimer();
                        videoDisplayTimer.Interval = TimeSpan.FromSeconds(10); // Display for 10 seconds
                        videoDisplayTimer.Tick += (s, args) =>
                        {
                            videoDisplayTimer.Stop();
                            CleanupVideoElements();
                            liveViewImage.Visibility = Visibility.Visible;
                            _liveViewTimer?.Start();
                            Log.Debug("Returned to live view after video display");
                        };
                        videoDisplayTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DisplayVideoInLiveView: Error: {ex.Message}");
            }
        }
        
        private void CleanupVideoElements()
        {
            try
            {
                if (liveViewImage != null)
                {
                    var parent = liveViewImage.Parent as Panel;
                    if (parent != null)
                    {
                        // Remove any MediaElement children
                        var videoElements = parent.Children.OfType<System.Windows.Controls.MediaElement>().ToList();
                        foreach (var element in videoElements)
                        {
                            element.Stop();
                            element.Close();
                            parent.Children.Remove(element);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CleanupVideoElements: Error: {ex.Message}");
            }
        }
        #endregion
        
        #region Display Methods - Moved to Services
        
        // Display methods have been moved to PhotoboothUIService for clean architecture
        // The page now responds to service events instead of containing display logic
        
        // Helper method for displaying images from gallery
        private void DisplayImage(string imagePath, bool isManualClick = false)
        {
            try
            {
                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    // Only prevent automatic cycling, allow manual thumbnail clicks
                    if (_isDisplayingSessionResult && !isManualClick)
                    {
                        Log.Debug($"★★★ Prevented individual photo display during session result: {imagePath}");
                        return;
                    }
                    
                    // Hide MediaElement if it exists and show Image control
                    HideMediaElement();
                    
                    _uiService.DisplayImage(imagePath);
                    Log.Debug($"Displaying image: {imagePath}{(isManualClick ? " (manual click)" : "")}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error displaying image: {ex.Message}");
            }
        }
        
        private void HideMediaElement()
        {
            try
            {
                // Show the Image control
                if (liveViewImage != null)
                {
                    liveViewImage.Visibility = Visibility.Visible;
                }
                
                // Hide and stop MediaElement if it exists
                var parent = liveViewImage?.Parent as Grid;
                if (parent != null)
                {
                    foreach (var child in parent.Children)
                    {
                        if (child is MediaElement mediaElement)
                        {
                            mediaElement.Stop();
                            mediaElement.Visibility = Visibility.Collapsed;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error hiding MediaElement: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Display the animated GIF or MP4 in the live view area
        /// </summary>
        private void DisplayGifInLiveView(string gifPath)
        {
            try
            {
                if (string.IsNullOrEmpty(gifPath) || !File.Exists(gifPath))
                {
                    Log.Error($"DisplayGifInLiveView: Invalid path or file not found: {gifPath}");
                    return;
                }
                
                string fileExtension = Path.GetExtension(gifPath).ToLower();
                bool isMP4 = fileExtension == ".mp4";
                
                Log.Debug($"Displaying {(isMP4 ? "MP4" : "GIF")} in live view: {gifPath}");
                
                // Stop live view timer to prevent camera feed from overriding display
                _liveViewTimer?.Stop();
                
                if (isMP4)
                {
                    // For MP4 files, use MediaElement to play the video
                    Log.Debug($"MP4 display requested - playing video: {gifPath}");
                    
                    // Hide the Image control and show MediaElement instead
                    if (liveViewImage != null)
                    {
                        liveViewImage.Visibility = Visibility.Collapsed;
                    }
                    
                    // Check if we already have a MediaElement, if not create one
                    var parent = liveViewImage?.Parent as Grid;
                    if (parent != null)
                    {
                        // Look for existing MediaElement
                        MediaElement mediaElement = null;
                        foreach (var child in parent.Children)
                        {
                            if (child is MediaElement me)
                            {
                                mediaElement = me;
                                break;
                            }
                        }
                        
                        // Create MediaElement if it doesn't exist
                        if (mediaElement == null)
                        {
                            mediaElement = new MediaElement
                            {
                                Name = "videoPlayer",
                                LoadedBehavior = MediaState.Manual,
                                UnloadedBehavior = MediaState.Stop,
                                Stretch = Stretch.Uniform,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center
                                // Note: AreTransportControlsEnabled is not available in WPF, only in UWP
                                // We would need to create custom controls or use a third-party library
                            };
                            
                            // Configure transport controls (available in WPF 4.5+)
                            mediaElement.Volume = 0.5; // Set default volume to 50%
                            mediaElement.IsMuted = false;
                            
                            // Add it to the same parent as liveViewImage
                            parent.Children.Add(mediaElement);
                            
                            // Handle when video ends - loop it
                            mediaElement.MediaEnded += (s, e) =>
                            {
                                mediaElement.Position = TimeSpan.Zero;
                                mediaElement.Play();
                            };
                            
                            // Add error handling
                            mediaElement.MediaFailed += (s, e) =>
                            {
                                Log.Error($"Failed to play MP4: {e.ErrorException?.Message}");
                                // Fall back to showing composed image
                                HideMediaElement();
                                if (_sessionService?.ComposedImagePath != null)
                                {
                                    DisplayImage(_sessionService.ComposedImagePath);
                                }
                            };
                        }
                        
                        // Set the video source and play
                        mediaElement.Visibility = Visibility.Visible;
                        mediaElement.Source = new Uri(gifPath, UriKind.Absolute);
                        mediaElement.Play();
                        
                        Log.Debug($"MP4 video playing in MediaElement");
                    }
                    else
                    {
                        Log.Error("Cannot find parent container for MediaElement");
                    }
                    
                    // Show a message that MP4 is ready
                    _uiService.UpdateStatus("MP4 animation ready - Click Print or Share to use");
                }
                else
                {
                    // For animated GIFs, we can use a BitmapImage with WPF's built-in GIF support
                    // WPF automatically animates GIFs when displayed in an Image control
                    var gifImage = new BitmapImage();
                    gifImage.BeginInit();
                    gifImage.UriSource = new Uri(gifPath, UriKind.Absolute);
                    gifImage.CacheOption = BitmapCacheOption.OnLoad;
                    gifImage.EndInit();
                    
                    // Display in the live view image control
                    if (liveViewImage != null)
                    {
                        liveViewImage.Source = gifImage;
                        Log.Debug("GIF displayed in live view - WPF will animate it automatically");
                    }
                }
                
                // Note: WPF's Image control automatically plays animated GIFs
                // No additional code needed for animation
            }
            catch (Exception ex)
            {
                Log.Error($"DisplayGifInLiveView failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Add a composed image thumbnail to the photo strip with label
        /// </summary>
        private void AddComposedThumbnail(string composedPath)
        {
            try
            {
                if (string.IsNullOrEmpty(composedPath) || !File.Exists(composedPath))
                {
                    Log.Error($"AddComposedThumbnail: Invalid path or file not found: {composedPath}");
                    return;
                }
                
                // Create a thumbnail with COMPOSED label
                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(5)
                };
                
                // Create thumbnail image
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(composedPath);
                bitmap.DecodePixelWidth = 150;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                var image = new Image
                {
                    Source = bitmap,
                    Width = 120,
                    Height = 80,
                    Stretch = Stretch.UniformToFill
                };
                
                // Add COMPOSED label
                var label = new TextBlock
                {
                    Text = "FINAL",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Blue),
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                
                stackPanel.Children.Add(image);
                stackPanel.Children.Add(label);
                
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.Blue),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    Child = stackPanel,
                    Margin = new Thickness(2),
                    Cursor = Cursors.Hand
                };
                
                // Add click handler to display composed image
                border.MouseLeftButtonUp += (s, e) =>
                {
                    DisplayImage(composedPath, isManualClick: true);
                };
                
                // Add to photo strip
                photosContainer.Children.Add(border);
                
                Log.Debug($"Added composed image thumbnail to photo strip: {composedPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error adding composed thumbnail: {ex.Message}");
            }
        }
        
        private void AddGifThumbnailForGallery(string gifPath, string thumbnailPhotoPath = null)
        {
            try
            {
                Log.Debug($"★★★ AddGifThumbnailForGallery called with path: {gifPath}, thumbnail: {thumbnailPhotoPath}");
                
                if (string.IsNullOrEmpty(gifPath) || !File.Exists(gifPath))
                {
                    Log.Error($"AddGifThumbnailForGallery: Invalid path or file not found: {gifPath}");
                    return;
                }
                
                // Determine if it's a GIF or MP4
                string fileExtension = Path.GetExtension(gifPath).ToLower();
                bool isMP4 = fileExtension == ".mp4";
                string labelText = isMP4 ? "MP4" : "GIF";
                
                Log.Debug($"★★★ Adding {labelText} thumbnail to photo strip (gallery context)");
                
                // Create a thumbnail with appropriate label
                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(5)
                };
                
                // Create thumbnail image 
                BitmapImage bitmap = null;
                
                if (isMP4 && !string.IsNullOrEmpty(thumbnailPhotoPath) && File.Exists(thumbnailPhotoPath))
                {
                    // For MP4, use provided thumbnail photo
                    Log.Debug($"★★★ Using provided photo as MP4 thumbnail: {thumbnailPhotoPath}");
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(thumbnailPhotoPath);
                    bitmap.DecodePixelWidth = 150;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                }
                else if (!isMP4)
                {
                    // For GIF, show the GIF directly
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(gifPath);
                    bitmap.DecodePixelWidth = 150;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                }
                else
                {
                    // Fallback for MP4 without thumbnail - don't add thumbnail
                    Log.Debug($"★★★ No thumbnail available for MP4, skipping thumbnail creation");
                    return;
                }
                
                var image = new Image
                {
                    Source = bitmap,
                    Width = 120,
                    Height = 80,
                    Stretch = Stretch.UniformToFill
                };
                
                // Add appropriate label
                var label = new TextBlock
                {
                    Text = labelText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Green),
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                
                stackPanel.Children.Add(image);
                stackPanel.Children.Add(label);
                
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.Green),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(2),
                    Child = stackPanel,
                    Cursor = Cursors.Hand
                };
                
                // Add click handler to display GIF/MP4 when clicked
                border.MouseLeftButtonUp += (s, e) =>
                {
                    DisplayGifInLiveView(gifPath);
                    Log.Debug($"★★★ MP4/GIF clicked from gallery - playing: {gifPath}");
                };
                
                // Add to photo container
                photosContainer.Children.Add(border);
                
                Log.Debug($"★★★ {labelText} thumbnail successfully added to photo strip (gallery): {gifPath}");
                Log.Debug($"★★★ Photo container now has {photosContainer.Children.Count} children");
            }
            catch (Exception ex)
            {
                Log.Error($"★★★ AddGifThumbnailForGallery error: {ex.Message}");
                Log.Error($"★★★ Stack trace: {ex.StackTrace}");
            }
        }
        
        private void AddGifThumbnail(string gifPath)
        {
            try
            {
                Log.Debug($"★★★ AddGifThumbnail called with path: {gifPath}");
                
                if (string.IsNullOrEmpty(gifPath) || !File.Exists(gifPath))
                {
                    Log.Error($"AddGifThumbnail: Invalid path or file not found: {gifPath}");
                    return;
                }
                
                // Determine if it's a GIF or MP4
                string fileExtension = Path.GetExtension(gifPath).ToLower();
                bool isMP4 = fileExtension == ".mp4";
                string labelText = isMP4 ? "MP4" : "GIF";
                
                Log.Debug($"★★★ Adding {labelText} thumbnail to photo strip");
                
                // Create a thumbnail with appropriate label
                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(5)
                };
                
                // Create thumbnail image 
                BitmapImage bitmap = null;
                
                if (isMP4)
                {
                    // For MP4, use the first photo from the session as thumbnail
                    Log.Debug($"★★★ MP4 detected, looking for first photo to use as thumbnail");
                    if (_sessionService?.CapturedPhotoPaths?.Count > 0)
                    {
                        string firstPhotoPath = _sessionService.CapturedPhotoPaths[0];
                        Log.Debug($"★★★ Using first photo as MP4 thumbnail: {firstPhotoPath}");
                        if (File.Exists(firstPhotoPath))
                        {
                            bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(firstPhotoPath);
                            bitmap.DecodePixelWidth = 150;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                        }
                        else
                        {
                            Log.Error($"★★★ First photo not found: {firstPhotoPath}");
                        }
                    }
                    else
                    {
                        Log.Error($"★★★ No captured photos available for MP4 thumbnail");
                    }
                }
                else
                {
                    // For GIF, show the GIF directly
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(gifPath);
                    bitmap.DecodePixelWidth = 150;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                }
                
                // Only create UI elements if we have a bitmap
                if (bitmap == null)
                {
                    Log.Error($"★★★ Failed to create thumbnail for {labelText}");
                    return;
                }
                
                var image = new Image
                {
                    Source = bitmap,
                    Width = 120,
                    Height = 80,
                    Stretch = Stretch.UniformToFill
                };
                
                // Add appropriate label
                var label = new TextBlock
                {
                    Text = labelText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Green),
                    FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                
                stackPanel.Children.Add(image);
                stackPanel.Children.Add(label);
                
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.Green),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(2),
                    Child = stackPanel,
                    Cursor = Cursors.Hand
                };
                
                // Add click handler to display GIF when clicked (MP4s are always clickable)
                border.MouseLeftButtonUp += (s, e) =>
                {
                    // MP4s should always be playable regardless of session result display state
                    DisplayGifInLiveView(gifPath);
                    Log.Debug($"★★★ MP4/GIF clicked - playing: {gifPath}");
                };
                
                // Add to photo container
                photosContainer.Children.Add(border);
                
                Log.Debug($"★★★ {labelText} thumbnail successfully added to photo strip: {gifPath}");
                Log.Debug($"★★★ Photo container now has {photosContainer.Children.Count} children");
            }
            catch (Exception ex)
            {
                Log.Error($"★★★ AddGifThumbnail error: {ex.Message}");
                Log.Error($"★★★ Stack trace: {ex.StackTrace}");
            }
        }
        
        #endregion
        
        #region Session Cleared Events - Routing Only
        
        /// <summary>
        /// Handle session cleared event from service
        /// </summary>
        private void OnServiceSessionCleared(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug("=== SESSION CLEARED - RESETTING FOR NEW SESSION ===");
                
                // Clear display flags
                SetDisplayingSessionResult(false);
                _isDisplayingCapturedPhoto = false;
                Log.Debug("Display flags cleared - live view can resume");
                
                // Stop timers
                _countdownTimer?.Stop();
                
                // Clear the photo strip
                if (photosContainer != null)
                {
                    photosContainer.Children.Clear();
                    Log.Debug("Photo strip cleared");
                }
                
                // Clear the composed/GIF image from display
                if (liveViewImage != null)
                {
                    liveViewImage.Source = null;
                }
                
                // Check if we should return to template selection (multi-template events)
                if (_currentEvent != null && _templateSelectionService?.HasMultipleTemplates == true)
                {
                    Log.Debug($"Event '{_currentEvent.Name}' has multiple templates - returning to template selection");
                    
                    // Clear current template selection for multi-template events
                    _currentTemplate = null;
                    PhotoboothService.CurrentTemplate = null;
                    
                    // Re-initialize template selection for the event
                    _templateSelectionService.InitializeForEvent(_currentEvent);
                }
                else
                {
                    // Single template or no event - keep the template for next session
                    Log.Debug($"Keeping template for next session: {_currentTemplate?.Name ?? "None"}");
                    
                    // Show start button for single template scenario
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Visible;
                }
                
                // Hide unified action buttons panel and other completion controls
                if (actionButtonsPanel != null) 
                {
                    actionButtonsPanel.Visibility = Visibility.Collapsed;
                    Log.Debug("Action buttons panel hidden after session clear");
                }
                if (homeButton != null) homeButton.Visibility = Visibility.Collapsed;
                if (galleryButton != null) galleryButton.Visibility = Visibility.Collapsed;
                
                // Reset gallery mode when session is cleared
                _isInGalleryMode = false;
                _currentGallerySession = null;
                
                // Hide stop button when session is cleared
                if (stopSessionButton != null) stopSessionButton.Visibility = Visibility.Collapsed;
                
                // Restart live view based on idle setting (but not if displaying session results)
                if (DeviceManager?.SelectedCameraDevice != null)
                {
                    if (Properties.Settings.Default.EnableIdleLiveView && !_isDisplayingSessionResult)
                    {
                        DeviceManager.SelectedCameraDevice.StartLiveView();
                        _liveViewTimer?.Start();
                        Log.Debug("Live view restarted after session clear (idle live view enabled)");
                    }
                    else if (_isDisplayingSessionResult)
                    {
                        Log.Debug("★★★ NOT restarting live view - session result is being displayed");
                    }
                    else
                    {
                        DeviceManager.SelectedCameraDevice.StopLiveView();
                        _liveViewTimer?.Stop();
                        Log.Debug("Live view stopped after session clear (idle live view disabled)");
                    }
                }
                
                // Reset UI to initial state (shows start button)
                _uiService?.ResetToInitialState();
                _uiService?.UpdateStatus("Ready to start");
                
                // Show the start button overlay
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                    Log.Debug("Start button shown - ready for new session");
                }
                
                // Show gallery preview again after session ends
                UpdateGalleryPreviewVisibility(false);
                
                Log.Debug("Session fully cleared - ready for new session");
            });
        }
        
        /// <summary>
        /// Handle auto-clear timer expired event from service
        /// </summary>
        private void OnServiceAutoClearTimerExpired(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug("Auto-clear timer expired - session will be cleared");
                // Service will automatically clear the session after this event
            });
        }
        
        /// <summary>
        /// Handle animation ready event from service (when MP4/GIF generation completes in background)
        /// </summary>
        private void OnServiceAnimationReady(object sender, Services.AnimationReadyEventArgs e)
        {
            Log.Debug($"★★★ OnServiceAnimationReady: Animation ready at {e.AnimationPath}");
            
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(e.AnimationPath) && File.Exists(e.AnimationPath))
                    {
                        string fileType = Path.GetExtension(e.AnimationPath).ToLower() == ".mp4" ? "MP4" : "GIF";
                        Log.Debug($"★★★ {fileType} ready, adding to UI: {e.AnimationPath}");
                        
                        // Add MP4/GIF to thumbnail strip ONLY (composed image stays in live view)
                        Log.Debug($"★★★ Adding {fileType} thumbnail to strip (not displaying in live view)...");
                        _uiService.AddGifThumbnail(e.AnimationPath);
                        Log.Debug($"★★★ Successfully called AddGifThumbnail for {fileType} at: {e.AnimationPath}");
                        
                        // DO NOT display MP4 in live view - composed image should remain visible
                        // The composed image is already displayed in OnServiceSessionCompleted
                        
                        // Update status to show it's ready
                        _uiService.UpdateStatus($"{fileType} ready!");
                    }
                    else
                    {
                        Log.Error($"★★★ Animation file not found: {e.AnimationPath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"★★★ Error handling animation ready: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Complete session - routes to service
        /// </summary>
        public void CompleteSession()
        {
            Log.Debug("Routing manual session completion to service");
            
            // Stop auto-clear timer through service
            _sessionService?.StopAutoClearTimer();
            
            // Clear the session through service
            _sessionService?.ClearSession();
        }
        
        /// <summary>
        /// Handle template selection requested event - THIN
        /// </summary>
        private void OnTemplateSelectionRequested(object sender, Services.TemplateSelectionRequestedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Initialize UI service controls if not done
                if (_templateSelectionUIService != null)
                {
                    _templateSelectionUIService.InitializeControls(
                        templateSelectionOverlayNew,
                        templatesGrid,
                        templateSelectionTitle,
                        templateSelectionSubtitle
                    );
                    
                    // Let UI service handle everything
                    _templateSelectionUIService.ShowTemplateSelection(e.Event, e.Templates);
                }
                
                // Hide start button
                if (startButtonOverlay != null)
                    startButtonOverlay.Visibility = Visibility.Collapsed;
            });
        }
        
        /// <summary>
        /// Handle template selected event - THIN
        /// </summary>
        private void OnTemplateSelected(object sender, Services.TemplateSelectedEventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                // Update current selections
                _currentTemplate = e.Template;
                _currentEvent = e.Event;
                PhotoboothService.CurrentTemplate = _currentTemplate;
                if (_currentEvent != null)
                    PhotoboothService.CurrentEvent = _currentEvent;
                
                // UI updates through service
                _templateSelectionUIService?.HideTemplateSelection();
                
                // Update status to show what's selected
                if (_currentEvent != null && _currentTemplate != null)
                {
                    _uiService?.UpdateStatus($"{_currentEvent.Name} - {_currentTemplate.Name}");
                }
                else if (_currentTemplate != null)
                {
                    _uiService?.UpdateStatus($"Template: {_currentTemplate.Name}");
                }
                
                // Check if this is a multi-template event selection
                if (_templateSelectionService?.HasMultipleTemplates == true)
                {
                    // Multi-template event: Start session immediately after selection
                    Log.Debug($"Multi-template event: Starting session immediately for '{_currentTemplate?.Name}'");
                    await Task.Delay(500); // Brief delay for UI transition
                    StartPhotoSession();
                }
                else
                {
                    // Single template or no event context: Show start button for user interaction
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Visible;
                        Log.Debug($"Single template selected: '{_currentTemplate?.Name}'. Showing start button for user interaction.");
                    }
                }
            });
        }
        
        /// <summary>
        /// Handle template selection cancelled event - THIN
        /// </summary>
        private void OnTemplateSelectionCancelled(object sender, Services.TemplateSelectionCancelledEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // UI updates through service
                _templateSelectionUIService?.HideTemplateSelection();
                
                // Show start button
                if (startButtonOverlay != null)
                    startButtonOverlay.Visibility = Visibility.Visible;
                
                _uiService?.UpdateStatus(e.Reason);
            });
        }
        
        /// <summary>
        /// Handle template card clicked from UI service - THIN
        /// </summary>
        private void OnTemplateCardClicked(object sender, Services.TemplateCardClickedEventArgs e)
        {
            // Route to business logic service
            _templateSelectionService?.SelectTemplate(e.Template);
        }
        
        /// <summary>
        /// Handle show overlay requested - THIN
        /// </summary>
        private void OnShowOverlayRequested(object sender, Services.TemplateUIEventArgs e)
        {
            // Any additional UI logic if needed
            Log.Debug("Template selection overlay shown");
        }
        
        /// <summary>
        /// Handle hide overlay requested - THIN
        /// </summary>
        private void OnHideOverlayRequested(object sender, Services.TemplateUIEventArgs e)
        {
            // Any additional UI logic if needed
            Log.Debug("Template selection overlay hidden");
        }
        
        #endregion
        
        #region Camera Management
        private async Task InitializeCamera()
        {
            try
            {
                var device = DeviceManager?.SelectedCameraDevice;
                if (device != null)
                {
                    if (!device.IsConnected)
                    {
                        UpdateCameraStatus("Connecting...");
                        // Device is already managed by DeviceManager
                        await Task.Delay(100);
                    }
                    
                    if (device.IsConnected)
                    {
                        UpdateCameraStatus($"Connected: {device.DeviceName}");
                        
                        // Only start live view if idle live view is enabled
                        if (Properties.Settings.Default.EnableIdleLiveView)
                        {
                            device.StartLiveView();
                            _liveViewTimer.Start();
                            Log.Debug("InitializeCamera: Started idle live view");
                        }
                        else
                        {
                            Log.Debug("InitializeCamera: Idle live view disabled, not starting");
                        }
                    }
                }
                else
                {
                    UpdateCameraStatus("No camera");
                    // Try to connect to first available
                    await Task.Run(() => DeviceManager?.ConnectToCamera());
                    device = DeviceManager?.SelectedCameraDevice;
                    if (device?.IsConnected == true)
                    {
                        UpdateCameraStatus($"Connected: {device.DeviceName}");
                        
                        // Only start live view if idle live view is enabled
                        if (Properties.Settings.Default.EnableIdleLiveView)
                        {
                            device.StartLiveView();
                            _liveViewTimer.Start();
                            Log.Debug("InitializeCamera: Started idle live view after auto-connect");
                        }
                        else
                        {
                            Log.Debug("InitializeCamera: Idle live view disabled after auto-connect");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateCameraStatus("Connection failed");
                Log.Error($"Camera init error: {ex.Message}");
            }
        }

        private void LiveViewTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Don't update live view if we're displaying a captured photo or session is complete
                if (_isDisplayingCapturedPhoto || _isDisplayingSessionResult)
                {
                    return;
                }
                
                var device = DeviceManager?.SelectedCameraDevice;
                if (device?.IsConnected == true)
                {
                    var liveViewData = device.GetLiveViewImage();
                    if (liveViewData?.ImageData != null)
                    {
                        DisplayLiveView(liveViewData.ImageData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Live view error: {ex.Message}");
            }
        }

        private void DisplayLiveView(byte[] imageData)
        {
            using (var ms = new MemoryStream(imageData))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                
                liveViewImage.Source = bitmap;
            }
        }

        private string FindLatestCapturedPhoto()
        {
            try
            {
                // Check common camera folders for the latest photo
                var possibleFolders = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth"),
                    @"C:\Users\Public\Pictures",
                    Path.GetTempPath()
                };

                foreach (var folder in possibleFolders)
                {
                    if (!Directory.Exists(folder)) continue;

                    var recentFiles = Directory.GetFiles(folder, "*.jpg")
                        .Concat(Directory.GetFiles(folder, "*.jpeg"))
                        .Concat(Directory.GetFiles(folder, "*.png"))
                        .Where(f => File.GetCreationTime(f) > DateTime.Now.AddMinutes(-2))
                        .OrderByDescending(f => File.GetCreationTime(f))
                        .Take(1);

                    var latestFile = recentFiles.FirstOrDefault();
                    if (!string.IsNullOrEmpty(latestFile))
                    {
                        Log.Debug($"Found latest captured photo: {latestFile}");
                        return latestFile;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error finding latest captured photo: {ex.Message}");
            }

            return null;
        }
        #endregion

        #region Event/Template Management
        private void LoadInitialEventTemplate()
        {
            Log.Debug("LoadInitialEventTemplate: Starting event/template loading");
            
            // Load from PhotoboothService static properties
            _currentEvent = PhotoboothService.CurrentEvent;
            _currentTemplate = PhotoboothService.CurrentTemplate;
            
            Log.Debug($"LoadInitialEventTemplate: CurrentEvent = {_currentEvent?.Name ?? "null"}");
            Log.Debug($"LoadInitialEventTemplate: CurrentTemplate = {_currentTemplate?.Name ?? "null"}");
            
            if (_currentEvent != null)
            {
                _eventTemplateService.SelectEvent(_currentEvent);
                statusText.Text = $"Event: {_currentEvent.Name}";
                
                // If we have an event but no template, initialize template selection
                if (_currentTemplate == null)
                {
                    Log.Debug($"LoadInitialEventTemplate: Have event but no template - initializing template selection");
                    _templateSelectionService.InitializeForEvent(_currentEvent);
                    // The service will handle showing template selection or auto-selecting single template
                    return;
                }
                else
                {
                    // We have both event and template
                    _eventTemplateService.SelectTemplate(_currentTemplate);
                    Log.Debug($"LoadInitialEventTemplate: Template selected: {_currentTemplate.Name}");
                    // Don't show start button here - let the template selection logic decide
                    // Single template events will auto-select and show the button
                    // Multi-template events will show template selection instead
                }
            }
            else if (_currentTemplate != null)
            {
                // Have template but no event - just use the template
                _eventTemplateService.SelectTemplate(_currentTemplate);
                Log.Debug($"LoadInitialEventTemplate: Template selected (no event): {_currentTemplate.Name}");
                startButtonOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                // No event or template - show event selection
                Log.Debug("LoadInitialEventTemplate: No current event or template, showing event selection");
                ShowEventSelectionOverlay();
            }
        }

        private void ShowEventSelectionOverlay()
        {
            Log.Debug("ShowEventSelectionOverlay: Loading available events");
            
            // Load events
            _eventTemplateService.LoadAvailableEvents();
            
            Log.Debug($"ShowEventSelectionOverlay: Found {_eventTemplateService.AvailableEvents?.Count ?? 0} events");
            
            // Show event selection UI instead of auto-selecting
            if (_eventTemplateService.AvailableEvents?.Any() == true)
            {
                // Bind events to UI
                eventsList.ItemsSource = _eventTemplateService.AvailableEvents;
                eventSelectionOverlay.Visibility = Visibility.Visible;
                
                Log.Debug("ShowEventSelectionOverlay: Showing event selection UI");
            }
            else
            {
                Log.Error("ShowEventSelectionOverlay: No events available!");
                statusText.Text = "No events available - Please configure events first";
                
                // Still show start button for basic photo capture
                startButtonOverlay.Visibility = Visibility.Visible;
                Log.Debug("ShowEventSelectionOverlay: Showing start button anyway for basic photo capture");
            }
        }

        #region Bottom Control Bar
        private void ToggleBottomBar_Click(object sender, RoutedEventArgs e)
        {
            if (bottomControlBar.Visibility == Visibility.Collapsed)
            {
                // Show bottom bar
                bottomControlBar.Visibility = Visibility.Visible;
                bottomBarToggleChevron.Text = "⌄"; // Down chevron
            }
            else
            {
                // Hide bottom bar  
                bottomControlBar.Visibility = Visibility.Collapsed;
                bottomBarToggleChevron.Text = "⌃"; // Up chevron
            }
        }

        private void SelectEventButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear current event and template when switching events via settings
            Log.Debug("SelectEventButton_Click: Clearing current event/template for event selection");
            _currentEvent = null;
            _currentTemplate = null;
            PhotoboothService.CurrentEvent = null;
            PhotoboothService.CurrentTemplate = null;
            
            // Hide any template selection UI that might be showing
            _templateSelectionUIService?.HideTemplateSelection();
            
            // Hide start button while selecting new event
            if (startButtonOverlay != null)
                startButtonOverlay.Visibility = Visibility.Collapsed;
            
            // Clear any active session
            if (_sessionService?.IsSessionActive == true)
            {
                _sessionService.ClearSession();
            }
            
            // Show event selection overlay
            ShowEventSelectionOverlay();
        }

        private void CameraSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Show camera settings overlay
            Log.Debug("CameraSettingsButton_Click: Not implemented yet");
        }

        private void TimerSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Show timer settings
            Log.Debug("TimerSettingsButton_Click: Not implemented yet");
        }

        private void PrintSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Show print settings
            Log.Debug("PrintSettingsButton_Click: Not implemented yet");
        }
        #endregion

        #region Event/Template Selection Handlers
        private void EventItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is EventData selectedEvent)
            {
                Log.Debug($"EventItem_Click: Selected event '{selectedEvent.Name}' (ID: {selectedEvent.Id})");
                
                _currentEvent = selectedEvent;
                _eventTemplateService.SelectEvent(_currentEvent);
                
                // Hide event selection
                eventSelectionOverlay.Visibility = Visibility.Collapsed;
                
                // Initialize template selection with the selected event
                Log.Debug($"EventItem_Click: Initializing template selection for event ID {selectedEvent.Id}");
                _templateSelectionService?.InitializeForEvent(selectedEvent);
            }
        }

        private void TemplateItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is TemplateData selectedTemplate)
            {
                Log.Debug($"TemplateItem_Click: Selected template '{selectedTemplate.Name}'");
                
                _currentTemplate = selectedTemplate;
                _eventTemplateService.SelectTemplate(_currentTemplate);
                
                Log.Debug($"TemplateItem_Click: Template selected: {_currentTemplate.Name}");
                
                // Hide template selection, show start button
                templateSelectionOverlay.Visibility = Visibility.Collapsed;
                statusText.Text = $"Event: {_currentEvent.Name} | Template: {_currentTemplate.Name}";
                startButtonOverlay.Visibility = Visibility.Visible;
                
                Log.Debug("TemplateItem_Click: Start button should now be visible");
            }
        }

        private void CloseEventSelection_Click(object sender, RoutedEventArgs e)
        {
            eventSelectionOverlay.Visibility = Visibility.Collapsed;
            
            // Show start button anyway for basic photo capture
            statusText.Text = "Touch START to begin";
            startButtonOverlay.Visibility = Visibility.Visible;
        }

        private void BackToEventSelection_Click(object sender, RoutedEventArgs e)
        {
            templateSelectionOverlay.Visibility = Visibility.Collapsed;
            eventSelectionOverlay.Visibility = Visibility.Visible;
        }

        private void ShowTemplateSelectionOverlay()
        {
            if (_currentEvent == null) return;
            
            Log.Debug($"ShowTemplateSelectionOverlay: Loading templates for event '{_currentEvent.Name}'");
            
            // Load templates for the selected event
            _eventTemplateService.LoadAvailableTemplates(_currentEvent.Id);
            
            Log.Debug($"ShowTemplateSelectionOverlay: Found {_eventTemplateService.AvailableTemplates?.Count ?? 0} templates");
            
            if (_eventTemplateService.AvailableTemplates?.Any() == true)
            {
                // Bind templates to UI
                templatesList.ItemsSource = _eventTemplateService.AvailableTemplates;
                templateSelectionEventName.Text = $"Select Template for {_currentEvent.Name}";
                templateSelectionEventDescription.Text = _currentEvent.Description;
                templateSelectionOverlay.Visibility = Visibility.Visible;
                
                Log.Debug("ShowTemplateSelectionOverlay: Showing template selection UI");
            }
            else
            {
                Log.Error($"ShowTemplateSelectionOverlay: No templates available for event '{_currentEvent.Name}'");
                
                // Go back to event selection
                BackToEventSelection_Click(null, null);
            }
        }
        #endregion

        private void OnSessionSelected(PhotoSessionData session)
        {
            if (session != null)
            {
                LoadExistingSession(session);
            }
        }

        private void OnModalClosed()
        {
            UpdateUI();
        }
        #endregion

        #region Photo Session Management - Clean Architecture
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartPhotoSession();
        }

        private async void StartPhotoSession()
        {
            try
            {
                Log.Debug("=== STARTING PHOTO SESSION USING CLEAN SERVICES ===");
                
                // Check if we need to select a template first
                if (_currentTemplate == null)
                {
                    Log.Debug("No template selected, checking for templates");
                    
                    // Try to reload from static properties
                    _currentTemplate = PhotoboothService.CurrentTemplate;
                    
                    if (_currentTemplate == null && _currentEvent != null)
                    {
                        // Initialize template selection for current event
                        Log.Debug($"Initializing template selection for event: {_currentEvent.Name}");
                        _templateSelectionService.InitializeForEvent(_currentEvent);
                        return; // Template selection will handle the rest
                    }
                    else if (_currentTemplate != null)
                    {
                        _eventTemplateService.SelectTemplate(_currentTemplate);
                        Log.Debug($"Reloaded template: {_currentTemplate.Name}");
                    }
                }
                
                // Also check event
                if (_currentEvent == null)
                {
                    Log.Debug("Event was null, reloading from static properties");
                    _currentEvent = PhotoboothService.CurrentEvent;
                    if (_currentEvent != null)
                    {
                        _eventTemplateService.SelectEvent(_currentEvent);
                        Log.Debug($"Reloaded event: {_currentEvent.Name}");
                    }
                }
                
                // Start session using clean service (this will trigger events that hide the button)
                bool sessionStarted = await _sessionService.StartSessionAsync(_currentEvent, _currentTemplate, GetTotalPhotosNeeded());
                
                if (sessionStarted)
                {
                    // Start photo capture workflow using clean service
                    await _workflowService.StartPhotoCaptureWorkflowAsync();
                }
                else
                {
                    Log.Error("Failed to start session");
                    _uiService.UpdateStatus("Failed to start session");
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StartPhotoSession error: {ex.Message}");
                _uiService.UpdateStatus("Session start failed - Please try again");
                startButtonOverlay.Visibility = Visibility.Visible;
            }
        }
        
        private int GetTotalPhotosNeeded()
        {
            return _currentTemplate != null ? 
                new PhotoboothService().GetTemplatePhotoCount(_currentTemplate) : 1;
        }











        private void StopPhotoSession()
        {
            // Use session service to cancel session cleanly
            _sessionService?.CancelSession();
            
            // Reset UI using UI service
            _uiService?.ResetToInitialState();
            
            Log.Debug("Photo session stopped using clean services");
        }
        #endregion

        #region Sharing & Upload - Clean Architecture
        private async Task AutoUploadSessionPhotos(Services.CompletedSessionData completedSession)
        {
            try
            {
                Log.Debug($"AutoUploadSessionPhotos: Starting auto-upload for session GUID: {completedSession.SessionId}");
                System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Session GUID = {completedSession.SessionId}");
                _uiService.UpdateStatus("Uploading...");
                
                // Build list of ALL files to upload (including composed and MP4/GIF)
                var allFiles = new List<string>();
                
                // Add original photos
                if (completedSession.PhotoPaths != null)
                {
                    allFiles.AddRange(completedSession.PhotoPaths);
                    Log.Debug($"AutoUploadSessionPhotos: Added {completedSession.PhotoPaths.Count} original photos");
                }
                
                // Add composed image if it exists
                if (!string.IsNullOrEmpty(completedSession.ComposedImagePath) && File.Exists(completedSession.ComposedImagePath))
                {
                    allFiles.Add(completedSession.ComposedImagePath);
                    Log.Debug($"AutoUploadSessionPhotos: Added composed image: {Path.GetFileName(completedSession.ComposedImagePath)}");
                }
                
                // Add GIF/MP4 if it exists
                if (!string.IsNullOrEmpty(completedSession.GifPath) && File.Exists(completedSession.GifPath))
                {
                    allFiles.Add(completedSession.GifPath);
                    Log.Debug($"AutoUploadSessionPhotos: Added animation: {Path.GetFileName(completedSession.GifPath)}");
                }
                
                Log.Debug($"AutoUploadSessionPhotos: Uploading {allFiles.Count} total files");
                System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Uploading {allFiles.Count} files for session {completedSession.SessionId}");
                
                // Use existing share service for upload with ALL files
                var uploadResult = await _shareService.CreateShareableGalleryAsync(
                    completedSession.SessionId,
                    allFiles,  // Use all files, not just PhotoPaths
                    completedSession.Event?.Name ?? "Photobooth Session"
                );
                
                if (uploadResult.Success && !string.IsNullOrEmpty(uploadResult.GalleryUrl))
                {
                    _currentShareResult = uploadResult;
                    
                    // IMPORTANT: Save the gallery URL to database so it can be retrieved later
                    System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Saving gallery URL to database");
                    System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Session GUID = {completedSession.SessionId}");
                    System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Gallery URL = {uploadResult.GalleryUrl}");
                    
                    var database = new Database.TemplateDatabase();
                    database.UpdatePhotoSessionGalleryUrl(completedSession.SessionId, uploadResult.GalleryUrl);
                    
                    Log.Debug($"Gallery URL saved to database for session {completedSession.SessionId}: {uploadResult.GalleryUrl}");
                    _uiService.UpdateStatus("Upload complete!");
                }
                else
                {
                    Log.Error($"Upload failed or no gallery URL generated for session {completedSession.SessionId}");
                    System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Upload failed - Success={uploadResult?.Success}, URL={uploadResult?.GalleryUrl ?? "NULL"}");
                    _uiService.UpdateStatus("Upload failed");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Auto-upload error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Exception - {ex.Message}");
                _uiService.UpdateStatus("Upload failed");
            }
        }

        private void ShowQRCode()
        {
            if (_currentShareResult?.QRCodeImage != null)
            {
                liveViewImage.Source = _currentShareResult.QRCodeImage;
            }
        }

        private async void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use GalleryActionService for unified sharing
                bool success = await _galleryActionService.ShareSessionAsync(_isInGalleryMode, _currentGallerySession);
                
                if (success && !_isInGalleryMode)
                {
                    // Show QR code for current session sharing
                    ShowQRCode();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Share error: {ex.Message}");
                _uiService.UpdateStatus("Share failed");
            }
        }
        
        
        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear display flags when done
                SetDisplayingSessionResult(false);
                _isDisplayingCapturedPhoto = false;
                
                if (_isInGalleryMode)
                {
                    // Done with gallery session - use existing gallery logic
                    Log.Debug("Done with gallery session - clearing and returning to start");
                    
                    // Clear current session and prepare for new one
                    _sessionService?.ClearSession();
                    _isInGalleryMode = false;
                    _currentGallerySession = null;
                    
                    // Hide action buttons panel
                    if (actionButtonsPanel != null)
                        actionButtonsPanel.Visibility = Visibility.Collapsed;
                    
                    // Clear photo strip
                    if (photosContainer != null)
                        photosContainer.Children.Clear();
                    
                    // Start live view if camera is connected
                    if (DeviceManager.SelectedCameraDevice != null)
                    {
                        try
                        {
                            DeviceManager.SelectedCameraDevice.StartLiveView();
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to start live view: {ex.Message}");
                        }
                    }
                    
                    // Show the Touch to Start button again when exiting gallery mode
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Visible;
                        Log.Debug("Showing Touch to Start button after exiting gallery mode");
                    }
                    
                    _uiService.UpdateStatus("Ready for new session");
                }
                else
                {
                    // Done with current session
                    Log.Debug("Done button clicked - clearing session and returning to start");
                    
                    // Clear the current session to return to start state
                    _sessionService?.ClearSession();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in DoneButton_Click: {ex.Message}");
            }
        }
        
        private async void GalleryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug("Gallery button clicked - showing event gallery");
                
                // Hide session completion UI
                if (doneButton != null)
                    doneButton.Visibility = Visibility.Collapsed;
                
                // Stop live view for gallery mode
                _liveViewTimer?.Stop();
                
                // Show gallery using service
                await _galleryService?.ShowGalleryAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"Error in GalleryButton_Click: {ex.Message}");
            }
        }
        #endregion

        #region Legacy Template Composition Support
        // These methods are kept for compatibility with PhotoProcessingOperations
        // but the business logic has been moved to PhotoCompositionService
        public void UpdateProcessedImagePaths(string outputPath, string printPath)
        {
            Log.Debug($"★★★ UpdateProcessedImagePaths CALLED ★★★");
            Log.Debug($"  - outputPath (display): {outputPath}");
            Log.Debug($"  - printPath (for printing): {printPath}");
            
            // Check if paths are different (indicating 2x6 -> 4x6 duplication)
            if (outputPath != printPath)
            {
                Log.Debug($"  - PATHS DIFFER: 2x6 duplicated to 4x6");
                
                // Verify the print file exists and check dimensions
                if (System.IO.File.Exists(printPath))
                {
                    using (var img = System.Drawing.Image.FromFile(printPath))
                    {
                        Log.Debug($"  - Print file dimensions: {img.Width}x{img.Height}");
                        Log.Debug($"  - Is this 4x6 duplicate? {img.Width == 1200 && img.Height == 1800}");
                    }
                }
                else
                {
                    Log.Error($"  - ERROR: Print path does not exist!");
                }
            }
            else
            {
                Log.Debug($"  - PATHS SAME: No duplication");
            }
            
            // Update session service with both paths
            if (_sessionService != null)
            {
                _sessionService.SetComposedImagePaths(outputPath, printPath);
                Log.Debug($"  - Session service updated with paths");
            }
            else
            {
                Log.Error($"  - ERROR: Session service is null!");
            }
        }
        
        public void SaveComposedImageToDatabase(string outputPath, string outputFormat)
        {
            Log.Debug($"Legacy SaveComposedImageToDatabase: path={outputPath}, format={outputFormat}");
        }
        
        public void AddComposedImageToPhotoStrip(string outputPath)
        {
            _uiService.AddPhotoThumbnail(outputPath, -1);
        }
        #endregion

        #region Printing
        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInGalleryMode && _currentGallerySession != null)
                {
                    // Gallery mode - print the composed image from the gallery session
                    // First look for the 4x6_print version (duplicated 2x6), then fall back to regular composed
                    var printPhoto = _currentGallerySession.Photos
                        ?.FirstOrDefault(p => p.PhotoType == "4x6_print" && File.Exists(p.FilePath));
                    
                    // If no 4x6_print version, look for regular composed image
                    if (printPhoto == null)
                    {
                        printPhoto = _currentGallerySession.Photos
                            ?.FirstOrDefault(p => p.PhotoType == "COMP" && File.Exists(p.FilePath));
                    }
                    
                    if (printPhoto != null && _printingService != null)
                    {
                        // Check if this is a 2x6 template with improved detection
                        string fileName = printPhoto.FileName?.ToLower() ?? "";
                        string filePath = printPhoto.FilePath?.ToLower() ?? "";
                        bool is2x6Template = printPhoto.PhotoType == "4x6_print" || // 4x6_print means it's a duplicated 2x6
                                           fileName.Contains("2x6") || fileName.Contains("2_6") || fileName.Contains("2-6") ||
                                           filePath.Contains("2x6") || filePath.Contains("2_6") || filePath.Contains("2-6") ||
                                           fileName.Contains("4x6_print"); // Also check for 4x6_print in filename
                        
                        Log.Debug($"★★★ UNIFIED PRINT (Gallery): Print photo: {printPhoto.FileName}");
                        Log.Debug($"★★★ UNIFIED PRINT (Gallery): PhotoType: {printPhoto.PhotoType}");
                        Log.Debug($"★★★ UNIFIED PRINT (Gallery): Is 2x6 template: {is2x6Template}");
                        
                        bool success = await _printingService.PrintImageAsync(
                            printPhoto.FilePath,
                            _currentGallerySession.SessionFolder,
                            is2x6Template // This will trigger proper printer routing
                        );
                        
                        if (success)
                        {
                            _uiService.UpdateStatus("Photos sent to printer!");
                        }
                        else
                        {
                            _uiService.UpdateStatus("Print failed");
                        }
                    }
                    else
                    {
                        Log.Debug($"★★★ No print photo found in gallery session");
                        if (_currentGallerySession.Photos != null)
                        {
                            Log.Debug($"★★★ Available photo types: {string.Join(", ", _currentGallerySession.Photos.Select(p => p.PhotoType))}");
                        }
                        _uiService.UpdateStatus("No composed image to print");
                    }
                }
                else if (_sessionService.IsSessionActive)
                {
                    // Active session mode - use the print-specific path if available
                    Log.Debug($"★★★ PRINT PATH SELECTION ★★★");
                    Log.Debug($"  - ComposedImagePath (display): {_sessionService.ComposedImagePath}");
                    Log.Debug($"  - ComposedImagePrintPath (print): {_sessionService.ComposedImagePrintPath}");
                    Log.Debug($"  - IsCurrentTemplate2x6: {_sessionService.IsCurrentTemplate2x6}");
                    
                    string imageToPrint = !string.IsNullOrEmpty(_sessionService.ComposedImagePrintPath) ? 
                        _sessionService.ComposedImagePrintPath : _sessionService.ComposedImagePath;
                    
                    Log.Debug($"  - SELECTED imageToPrint: {imageToPrint}");
                    
                    if (!string.IsNullOrEmpty(imageToPrint) && File.Exists(imageToPrint))
                    {
                        // Verify dimensions of selected image
                        using (var img = System.Drawing.Image.FromFile(imageToPrint))
                        {
                            Log.Debug($"  - Image dimensions: {img.Width}x{img.Height}");
                            bool is4x6Duplicate = img.Width == 1200 && img.Height == 1800;
                            Log.Debug($"  - Is 4x6 duplicate: {is4x6Duplicate}");
                        }
                        
                        if (imageToPrint != _sessionService.ComposedImagePath)
                        {
                            Log.Debug($"  - ✅ Using 4x6 duplicate for printing (different from display)");
                        }
                        else
                        {
                            Log.Debug($"  - ⚠️ Using same image for display and print");
                        }
                        
                        bool printSuccess = await _printingService.PrintImageAsync(
                            imageToPrint,
                            _sessionService.CurrentSessionId,
                            _sessionService.IsCurrentTemplate2x6 // Use proper 2x6 flag from session service
                        );
                        
                        if (printSuccess)
                        {
                            _uiService.UpdateStatus("Photo sent to printer!");
                        }
                        else
                        {
                            _uiService.UpdateStatus("Print failed");
                        }
                    }
                    else
                    {
                        _uiService.UpdateStatus("No photo to print");
                    }
                }
                else
                {
                    _uiService.UpdateStatus("No active session or gallery");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Print error: {ex.Message}");
                _uiService.UpdateStatus("Print failed");
            }
        }
        #endregion

        #region UI Management
        // ApplyUILayout method moved earlier in the file to avoid duplication

        private void UpdateUI()
        {
            UpdateCameraStatus(DeviceManager?.SelectedCameraDevice?.IsConnected == true ? 
                "Connected" : "Disconnected");
        }

        private void UpdateCameraStatus(string status)
        {
            // Use UI service for camera status updates
            _uiService?.UpdateCameraStatus(status.Contains("Connected"), 
                status.Replace("Connected: ", ""));
                
            // Legacy UI updates for elements not yet in service
            cameraStatusText.Text = status;
            syncStatusText.Text = status.Contains("Connected") ? "Ready" : "Offline";
            syncStatusIcon.Background = new SolidColorBrush(
                status.Contains("Connected") ? Colors.Green : Colors.Red);
        }

        private void UpdateStatusText(string text)
        {
            // Direct update to UI element
            // Don't call _uiService.UpdateStatus here as this method is called FROM the UI service event
            statusText.Text = text;
        }

        private void AddPhotoThumbnail(string imagePath)
        {
            try
            {
                Log.Debug($"AddPhotoThumbnail: Adding thumbnail for {imagePath}");
                
                if (photosContainer == null)
                {
                    Log.Error("AddPhotoThumbnail: photosContainer is null!");
                    return;
                }
                
                // Simple UI-only thumbnail creation (business logic moved to services)
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.DecodePixelWidth = 150;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                var image = new Image
                {
                    Source = bitmap,
                    Width = 120,
                    Height = 90,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(5),
                    Cursor = Cursors.Hand
                };
                
                image.MouseLeftButtonDown += (s, e) => DisplayImage(imagePath, isManualClick: true);
                
                var border = new Border
                {
                    Child = image,
                    BorderBrush = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5)
                };
                
                photosContainer.Children.Add(border);
                
                Log.Debug($"AddPhotoThumbnail: Successfully added thumbnail. Container now has {photosContainer.Children.Count} children");
                
                // Check visibility
                var visibility = photosContainer.Visibility;
                var parent = photosContainer.Parent as ScrollViewer;
                var parentVisibility = parent?.Visibility ?? Visibility.Collapsed;
                Log.Debug($"AddPhotoThumbnail: photosContainer visibility={visibility}, parent visibility={parentVisibility}");
            }
            catch (Exception ex)
            {
                Log.Error($"AddPhotoThumbnail error: {ex.Message}");
            }
        }


        private void ResetForNewSession()
        {
            // Use UI service to reset to initial state (clean architecture)
            _uiService?.ResetToInitialState();
            _uiService?.ClearPhotoThumbnails();
            
            // Legacy UI updates for elements not yet in service
            photosContainer.Children.Clear();
            
            // Restart live view (but not if displaying session results)
            if (!_isDisplayingSessionResult)
            {
                DeviceManager?.SelectedCameraDevice?.StartLiveView();
                _liveViewTimer.Start();
                Log.Debug("Live view restarted in ResetForNewSession");
            }
            else
            {
                Log.Debug("★★★ NOT restarting live view in ResetForNewSession - session result displayed");
            }
        }

        private void LoadExistingSession(PhotoSessionData session)
        {
            photosContainer.Children.Clear();
            // DatabaseOperations doesn't have these methods - would need refactoring
            // For now, just clear the display
            // TODO: Implement session loading through proper service methods
            /*
            var photos = _database.GetSessionPhotos(session.Id);
            foreach (var photo in photos)
            {
                if (File.Exists(photo.FilePath))
                {
                    AddPhotoThumbnail(photo.FilePath);
                }
            }
            
            var composedImages = _database.GetSessionComposedImages(session.Id);
            */
            
            // Placeholder implementation - would need proper service methods
            // to load session data
        }
        #endregion

        #region Button Handlers
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            CancelCurrentPhoto();
        }
        
        /// <summary>
        /// Cancel the current photo capture and reset for retake (not entire session)
        /// </summary>
        private async void CancelCurrentPhoto()
        {
            try
            {
                Log.Debug("=== CANCELING CURRENT PHOTO FOR RETAKE ===");
                
                // Cancel current photo capture workflow through service
                _workflowService?.CancelCurrentPhotoCapture();
                
                // Hide countdown overlay
                if (countdownOverlay != null)
                    countdownOverlay.Visibility = Visibility.Collapsed;
                
                // Update UI through service
                _uiService?.UpdateStatus("Photo canceled - Restarting countdown...");
                
                // Clear the displayed photo to show live view
                _isDisplayingCapturedPhoto = false;
                
                // Wait a moment for the cancel to complete
                await Task.Delay(500);
                
                // Restart the capture workflow for retake
                if (_sessionService?.IsSessionActive == true && _workflowService != null)
                {
                    Log.Debug("Restarting capture workflow after cancel");
                    bool started = await _workflowService.StartPhotoCaptureWorkflowAsync();
                    if (started)
                    {
                        Log.Debug("Capture workflow restarted successfully for retake");
                    }
                    else
                    {
                        Log.Error("Failed to restart capture workflow for retake");
                        _uiService?.UpdateStatus("Ready for retake - press capture button");
                    }
                }
                
                Log.Debug("Current photo canceled - countdown restarted for retake");
            }
            catch (Exception ex)
            {
                Log.Error($"Error canceling current photo: {ex.Message}");
            }
        }

        private void DecreaseCountdown_Click(object sender, RoutedEventArgs e)
        {
            if (Properties.Settings.Default.CountdownSeconds > 1)
            {
                Properties.Settings.Default.CountdownSeconds--;
                countdownSecondsDisplay.Text = $"{Properties.Settings.Default.CountdownSeconds}s";
            }
        }

        private void IncreaseCountdown_Click(object sender, RoutedEventArgs e)
        {
            if (Properties.Settings.Default.CountdownSeconds < 10)
            {
                Properties.Settings.Default.CountdownSeconds++;
                countdownSecondsDisplay.Text = $"{Properties.Settings.Default.CountdownSeconds}s";
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Navigate back to Surface home like original PhotoboothTouchModern
                var parentWindow = Window.GetWindow(this);
                if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
                {
                    surfaceWindow.NavigateBack();
                }
                else if (NavigationService != null && NavigationService.CanGoBack)
                {
                    NavigationService.GoBack();
                }
                else
                {
                    // Last resort - close the window
                    parentWindow?.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"BackButton navigation error: {ex.Message}");
                // Fallback - just close the window
                try
                {
                    var parentWindow = Window.GetWindow(this);
                    parentWindow?.Close();
                }
                catch (Exception closeEx)
                {
                    Log.Error($"Window close fallback failed: {closeEx.Message}");
                }
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Complete and clear the session
                CompleteSession();
                
                // Navigate back to Surface home like original PhotoboothTouchModern  
                var parentWindow = Window.GetWindow(this);
                if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
                {
                    surfaceWindow.NavigateBack();
                }
                else
                {
                    // Fallback to navigation service
                    NavigationService?.Navigate(new Uri("/Pages/MainPage.xaml", UriKind.Relative));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"HomeButton navigation error: {ex.Message}");
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.OpenSettingsCommand?.Execute(null);
        }
        
        // Gallery Action Button Handlers
        private async void GalleryShareButton_Click(object sender, RoutedEventArgs e)
        {
            // Share current gallery session
            if (_currentGallerySession != null && _currentGallerySession.Photos != null)
            {
                Log.Debug("Gallery share button clicked");
                _uiService.UpdateStatus("Sharing session...");
                
                // Use share service to share photos
                var photoPaths = _currentGallerySession.Photos
                    .Where(p => File.Exists(p.FilePath))
                    .Select(p => p.FilePath)
                    .ToList();
                    
                if (photoPaths.Any() && _shareService != null)
                {
                    try
                    {
                        // Use CreateShareableGalleryAsync from IShareService
                        var shareResult = await _shareService.CreateShareableGalleryAsync(
                            _currentGallerySession.SessionFolder, // sessionId (GUID)
                            photoPaths,
                            _currentGallerySession.SessionName // eventName
                        );
                        
                        if (shareResult != null && shareResult.Success)
                        {
                            _uiService.UpdateStatus($"Session shared! URL: {shareResult.GalleryUrl}");
                            
                            // Show QR code if available
                            if (shareResult.QRCodeImage != null)
                            {
                                // TODO: Display QR code in modal
                                Log.Debug($"Gallery URL: {shareResult.GalleryUrl}");
                            }
                        }
                        else
                        {
                            _uiService.UpdateStatus("Failed to share session");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to share gallery session: {ex.Message}");
                        _uiService.UpdateStatus("Failed to share session");
                    }
                }
            }
        }
        
        private async void GalleryPrintButton_Click(object sender, RoutedEventArgs e)
        {
            // Print current gallery session photos
            if (_currentGallerySession != null && _currentGallerySession.Photos != null)
            {
                Log.Debug("Gallery print button clicked");
                _uiService.UpdateStatus("Printing photos...");
                
                // First look for the 4x6_print version (duplicated 2x6), then fall back to regular composed
                var printPhoto = _currentGallerySession.Photos
                    .FirstOrDefault(p => p.PhotoType == "4x6_print" && File.Exists(p.FilePath));
                
                // If no 4x6_print version, look for regular composed image
                if (printPhoto == null)
                {
                    printPhoto = _currentGallerySession.Photos
                        .FirstOrDefault(p => p.PhotoType == "COMP" && File.Exists(p.FilePath));
                }
                    
                if (printPhoto != null && _printingService != null)
                {
                    try
                    {
                        // Use PrintImageAsync from PrintingService with proper 2x6 routing
                        // Check if this is a 2x6 template by looking at the file path, photo type, or template name
                        // Also check for "2_6", "2-6", "2X6" variations
                        string fileName = printPhoto.FileName?.ToLower() ?? "";
                        string filePath = printPhoto.FilePath?.ToLower() ?? "";
                        bool is2x6Template = printPhoto.PhotoType == "4x6_print" || // 4x6_print means it's a duplicated 2x6
                                           fileName.Contains("2x6") || fileName.Contains("2_6") || fileName.Contains("2-6") ||
                                           filePath.Contains("2x6") || filePath.Contains("2_6") || filePath.Contains("2-6") ||
                                           fileName.Contains("4x6_print"); // Also check for 4x6_print in filename
                        
                        Log.Debug($"★★★ GALLERY PRINT: Print photo: {printPhoto.FileName}");
                        Log.Debug($"★★★ GALLERY PRINT: PhotoType: {printPhoto.PhotoType}");
                        Log.Debug($"★★★ GALLERY PRINT: Is 2x6 template: {is2x6Template}");
                        
                        bool printSuccess = await _printingService.PrintImageAsync(
                            printPhoto.FilePath,
                            _currentGallerySession.SessionFolder, // sessionId
                            is2x6Template
                        );
                        
                        if (printSuccess)
                        {
                            _uiService.UpdateStatus("Photos sent to printer!");
                        }
                        else
                        {
                            _uiService.UpdateStatus("Failed to print photos");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to print gallery session: {ex.Message}");
                        _uiService.UpdateStatus("Failed to print photos");
                    }
                }
                else
                {
                    // Print individual photos if no composed image
                    var photoPaths = _currentGallerySession.Photos
                        .Where(p => p.PhotoType == "ORIG" && File.Exists(p.FilePath))
                        .Select(p => p.FilePath)
                        .ToList();
                    
                    if (photoPaths.Any() && _printingService != null)
                    {
                        try
                        {
                            bool anyPrinted = false;
                            foreach (var path in photoPaths)
                            {
                                // For individual photos, assume 4x6 unless filename indicates 2x6
                                bool is2x6Template = Path.GetFileName(path)?.Contains("2x6") == true;
                                
                                bool success = await _printingService.PrintImageAsync(
                                    path,
                                    _currentGallerySession.SessionFolder,
                                    is2x6Template
                                );
                                if (success) anyPrinted = true;
                            }
                            
                            if (anyPrinted)
                            {
                                _uiService.UpdateStatus("Photos sent to printer!");
                            }
                            else
                            {
                                _uiService.UpdateStatus("Failed to print photos");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to print photos: {ex.Message}");
                            _uiService.UpdateStatus("Failed to print photos");
                        }
                    }
                }
            }
        }
        
        private void GalleryEmailButton_Click(object sender, RoutedEventArgs e)
        {
            // Email current gallery session
            if (_currentGallerySession != null && _currentGallerySession.Photos != null)
            {
                Log.Debug("Gallery email button clicked");
                _uiService.UpdateStatus("Preparing email...");
                
                // Show email modal using UI service
                var photoPaths = _currentGallerySession.Photos
                    .Where(p => File.Exists(p.FilePath))
                    .Select(p => p.FilePath)
                    .ToList();
                    
                if (photoPaths.Any())
                {
                    // TODO: Implement email modal or use sharing service email functionality
                    _uiService.UpdateStatus("Email feature coming soon");
                }
            }
        }
        
        private void GallerySmsButton_Click(object sender, RoutedEventArgs e)
        {
            // Send SMS for current gallery session
            if (_currentGallerySession != null)
            {
                Log.Debug("Gallery SMS button clicked");
                _uiService.UpdateStatus("Preparing SMS...");
                
                // TODO: Show SMS modal or use sharing service SMS functionality
                // For now, use the session folder (GUID) to look up gallery URL from database
                _uiService.UpdateStatus("SMS feature coming soon");
            }
        }
        
        private async void GalleryQrButton_Click(object sender, RoutedEventArgs e)
        {
            // Show QR code for current gallery session
            if (_currentGallerySession != null && _currentGallerySession.Photos != null)
            {
                Log.Debug($"Gallery QR button clicked - SessionFolder (GUID): {_currentGallerySession.SessionFolder}");
                
                // Use the unified GalleryActionService to handle QR code generation
                // This will retrieve the already-uploaded URL from the database
                if (_galleryActionService != null)
                {
                    try
                    {
                        // Generate QR code for gallery session
                        // Pass true for isGalleryMode and the current gallery session
                        bool success = await _galleryActionService.GenerateQRCodeAsync(true, _currentGallerySession);
                        
                        if (!success)
                        {
                            Log.Debug("Gallery QR code generation returned false");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to generate gallery QR code: {ex.Message}");
                        _uiService.UpdateStatus("Failed to generate QR code");
                    }
                }
                else
                {
                    Log.Error("GalleryActionService is not initialized");
                    _uiService.UpdateStatus("QR service not available");
                }
            }
            else
            {
                Log.Debug("No gallery session available for QR code");
                _uiService.UpdateStatus("No gallery session selected");
            }
        }
        
        private void GalleryDoneButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear current session and prepare for new one
            Log.Debug("Gallery done button clicked - clearing session for new capture");
            
            // Clear session and return to start
            _sessionService?.ClearSession();
            _isInGalleryMode = false;
            _currentGallerySession = null;
            
            // Hide unified action buttons panel
            if (actionButtonsPanel != null)
                actionButtonsPanel.Visibility = Visibility.Collapsed;
            
            // Show start action button if available
            // TODO: Implement ShowStartActionButton method or show the actual start button
            
            // Clear photo strip
            if (photosContainer != null)
                photosContainer.Children.Clear();
            
            // Resume live view
            if (DeviceManager?.SelectedCameraDevice != null)
            {
                DeviceManager.SelectedCameraDevice.StartLiveView();
            }
            
            _uiService.UpdateStatus("Ready for new session");
        }
        
        private void ExitGalleryButton_Click(object sender, RoutedEventArgs e)
        {
            // Exit gallery mode
            _isInGalleryMode = false;
            _currentGallerySession = null;
            _gallerySessions = null;
            _currentGallerySessionIndex = 0;
            
            // Hide unified action buttons panel
            if (actionButtonsPanel != null)
                actionButtonsPanel.Visibility = Visibility.Collapsed;
            
            // Clear the photo strip
            if (photosContainer != null)
                photosContainer.Children.Clear();
            
            // Resume live view
            if (DeviceManager?.SelectedCameraDevice != null && Properties.Settings.Default.EnableIdleLiveView)
            {
                DeviceManager.SelectedCameraDevice.StartLiveView();
                _liveViewTimer.Start();
            }
            
            // Show start button
            _uiService.ShowStartButton();
            _uiService.UpdateStatus("Ready to start");
            
            // Show gallery preview again after exiting gallery
            UpdateGalleryPreviewVisibility(false);
            
            Log.Debug("Exited gallery mode");
        }

        #region Unified Action Button Handlers
        
        private async void EmailButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use GalleryActionService for unified emailing
                await _galleryActionService.EmailSessionAsync(_isInGalleryMode, _currentGallerySession);
            }
            catch (Exception ex)
            {
                Log.Error($"Email error: {ex.Message}");
                _uiService.UpdateStatus("Email failed");
            }
        }
        
        private async void SmsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug("SmsButton_Click: Button clicked, using GalleryActionService");
                
                // Use GalleryActionService for unified SMS
                await _galleryActionService.SmsSessionAsync(_isInGalleryMode, _currentGallerySession);
            }
            catch (Exception ex)
            {
                Log.Error($"SMS error: {ex.Message}");
                _uiService.UpdateStatus("SMS failed");
            }
        }
        
        private async void QrButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Debug("QrButton_Click: Button clicked, using GalleryActionService");
                
                // Use GalleryActionService for unified QR code generation
                await _galleryActionService.GenerateQRCodeAsync(_isInGalleryMode, _currentGallerySession);
            }
            catch (Exception ex)
            {
                Log.Error($"QR Code error: {ex.Message}");
                _uiService.UpdateStatus("QR Code failed");
            }
        }
        
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isInGalleryMode)
            {
                // Exit gallery mode
                ExitGalleryMode();
            }
            else
            {
                // Exit current session (same as Done for now)
                DoneButton_Click(sender, e);
            }
        }
        
        private void ExitGalleryMode()
        {
            // Exit gallery mode
            _isInGalleryMode = false;
            _currentGallerySession = null;
            _gallerySessions = null;
            _currentGallerySessionIndex = 0;
            
            // Hide action buttons panel
            if (actionButtonsPanel != null)
                actionButtonsPanel.Visibility = Visibility.Collapsed;
            
            // Clear the photo strip
            if (photosContainer != null)
                photosContainer.Children.Clear();
            
            // Show the Touch to Start button again when exiting gallery mode
            if (startButtonOverlay != null)
            {
                startButtonOverlay.Visibility = Visibility.Visible;
                Log.Debug("Showing Touch to Start button after exiting gallery mode via Exit button");
            }
            
            // Start live view if camera is connected
            if (DeviceManager.SelectedCameraDevice != null)
            {
                try
                {
                    DeviceManager.SelectedCameraDevice.StartLiveView();
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to start live view: {ex.Message}");
                }
            }
            
            _uiService.UpdateStatus("Ready for new session");
        }
        
        #endregion
        
        // Gallery Preview Methods - CLEAN ARCHITECTURE
        // Uses GalleryBrowserService for all business logic
        private async void LoadGalleryPreview()
        {
            try
            {
                // Use service to get preview data (no business logic here!)
                var previewData = await _galleryBrowserService?.GetPreviewDataAsync();
                
                if (previewData != null && previewData.HasContent)
                {
                    // Load preview image (UI only)
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(previewData.PreviewImagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 200; // Optimize for thumbnail size
                    bitmap.EndInit();
                    
                    // Update UI elements
                    if (galleryPreviewImage != null)
                        galleryPreviewImage.Source = bitmap;
                    
                    if (galleryPreviewInfo != null)
                        galleryPreviewInfo.Text = $"{previewData.SessionCount} session{(previewData.SessionCount != 1 ? "s" : "")}";
                    
                    if (galleryPreviewBox != null)
                        galleryPreviewBox.Visibility = Visibility.Visible;
                }
                else
                {
                    // No gallery content, hide preview
                    if (galleryPreviewBox != null)
                        galleryPreviewBox.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading gallery preview: {ex.Message}");
                if (galleryPreviewBox != null)
                    galleryPreviewBox.Visibility = Visibility.Collapsed;
            }
        }
        
        private void UpdateGalleryPreviewVisibility(bool inSession)
        {
            if (galleryPreviewBox != null)
            {
                // Hide during active session or gallery mode
                if (inSession || _isInGalleryMode)
                {
                    galleryPreviewBox.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Show if we have gallery content
                    LoadGalleryPreview();
                }
            }
        }
        
        private async void GalleryPreviewBox_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Log.Debug("Gallery preview clicked - opening gallery browser modal");
                
                // Show the gallery browser modal (UI routing only!)
                if (GalleryBrowserModal != null)
                {
                    GalleryBrowserModal.ShowModal();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error opening gallery browser: {ex.Message}");
            }
        }
        
        private void GalleryPreviewBox_MouseEnter(object sender, MouseEventArgs e)
        {
            if (galleryPreviewHover != null)
                galleryPreviewHover.Visibility = Visibility.Visible;
        }
        
        private void GalleryPreviewBox_MouseLeave(object sender, MouseEventArgs e)
        {
            if (galleryPreviewHover != null)
                galleryPreviewHover.Visibility = Visibility.Collapsed;
        }
        #endregion

        #region Modal Handlers
        private void SessionSelectionModal_Loaded(object sender, RoutedEventArgs e)
        {
            // Handle modal loading if needed
        }
        
        private async void OnGallerySessionSelected(object sender, Photobooth.Controls.SessionSelectedEventArgs e)
        {
            try
            {
                if (e.SelectedSession != null)
                {
                    Log.Debug($"Gallery session selected: {e.SelectedSession.SessionName}");
                    
                    // Debug: Check how many photos are in the selected session
                    int photoCount = e.SelectedSession?.Photos?.Count ?? 0;
                    Log.Debug($"OnGallerySessionSelected: SelectedSession has {photoCount} photos");
                    if (e.SelectedSession?.Photos != null)
                    {
                        foreach (var photo in e.SelectedSession.Photos)
                        {
                            Log.Debug($"  - Photo: {photo.FileName} (Type: {photo.PhotoType}, Path: {photo.FilePath})");
                        }
                    }
                    
                    // Hide the gallery preview box since we're entering gallery mode
                    if (galleryPreviewBox != null)
                        galleryPreviewBox.Visibility = Visibility.Collapsed;
                    
                    // Stop live view
                    _liveViewTimer?.Stop();
                    
                    // Convert GallerySessionInfo to format expected by EventGalleryService
                    System.Diagnostics.Debug.WriteLine($"OnGallerySessionSelected: SessionFolder from e.SelectedSession = {e.SelectedSession.SessionFolder}");
                    System.Diagnostics.Debug.WriteLine($"OnGallerySessionSelected: SessionName = {e.SelectedSession.SessionName}");
                    
                    var sessionData = new Services.SessionGalleryData
                    {
                        SessionName = e.SelectedSession.SessionName,
                        SessionTime = e.SelectedSession.SessionTimeDisplay,
                        PhotoCount = e.SelectedSession.PhotoCount,
                        SessionFolder = e.SelectedSession.SessionFolder,
                        Photos = e.SelectedSession.Photos.Select(p => new Services.PhotoGalleryData
                        {
                            FilePath = p.FilePath,
                            FileName = p.FileName,
                            FileSize = p.FileSizeDisplay,
                            ThumbnailPath = p.FilePath,
                            PhotoType = p.PhotoType
                        }).ToList()
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"OnGallerySessionSelected: Created SessionGalleryData with SessionFolder = {sessionData.SessionFolder}");
                    
                    // Load the selected session into the view
                    await _galleryService?.LoadSessionIntoViewAsync(sessionData);
                    
                    // Mark that we're in gallery mode
                    _isInGalleryMode = true;
                    
                    // Hide the Touch to Start button when loading gallery session
                    if (startButtonOverlay != null)
                    {
                        startButtonOverlay.Visibility = Visibility.Collapsed;
                    }
                    
                    // Explicitly show the action buttons panel for gallery mode
                    // Add a small delay to ensure the session is fully loaded
                    await Task.Delay(100);
                    
                    if (actionButtonsPanel != null)
                    {
                        actionButtonsPanel.Visibility = Visibility.Visible;
                        Log.Debug($"★★★ Showing action buttons panel after loading gallery session (Visibility: {actionButtonsPanel.Visibility})");
                        
                        // Force UI update
                        actionButtonsPanel.UpdateLayout();
                        
                        // Log button states
                        if (printButton != null)
                            Log.Debug($"  - Print button exists and visibility: {printButton.Visibility}");
                        else
                            Log.Error("  - Print button is null!");
                    }
                    else
                    {
                        Log.Error("★★★ actionButtonsPanel is null when trying to show gallery buttons!");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading selected gallery session: {ex.Message}");
            }
        }
        
        private void OnGalleryModalClosed(object sender, EventArgs e)
        {
            Log.Debug("Gallery browser modal closed");
            
            // If no session was selected, restore normal view
            if (!_isInGalleryMode)
            {
                UpdateGalleryPreviewVisibility(false);
            }
        }
        
        #region Sharing UI Service Event Handlers
        
        private void OnQrCodeOverlayClosed()
        {
            Log.Debug("QR code overlay closed via SharingUIService");
        }
        
        private void OnSmsOverlayClosed()
        {
            Log.Debug("SMS overlay closed via SharingUIService");
        }
        
        private async void OnSendSmsRequested(string phoneNumber)
        {
            try
            {
                Log.Debug($"SMS send requested via SharingUIService for: {phoneNumber}, isGallery: {_isInGalleryMode}");
                
                // Use GalleryActionService to send SMS with session context
                var success = await _galleryActionService.SendSMSAsync(phoneNumber, _isInGalleryMode, _currentGallerySession);
                
                if (success)
                {
                    _sharingUIService.HideSmsPhonePadOverlay();
                    _uiService?.UpdateStatus("SMS processed successfully!");
                }
                else
                {
                    _uiService?.UpdateStatus("Failed to process SMS. Please try again.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending SMS via SharingUIService: {ex.Message}");
                _uiService?.UpdateStatus("SMS sending failed");
            }
        }
        
        private void OnShowSmsFromQrRequested()
        {
            try
            {
                Log.Debug("SMS from QR requested via SharingUIService");
                _sharingUIService.ShowSmsPhonePadOverlay();
            }
            catch (Exception ex)
            {
                Log.Error($"Error showing SMS from QR: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Template Selection UI Handlers - THIN
        private void TemplateCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Route to UI service
            var border = sender as Border;
            _templateSelectionUIService?.HandleTemplateCardClick(border?.DataContext);
        }
        
        private void CloseTemplateSelection_Click(object sender, RoutedEventArgs e)
        {
            // Route to business service
            _templateSelectionService?.CancelSelection();
        }
        #endregion
        
        #endregion
    }
}