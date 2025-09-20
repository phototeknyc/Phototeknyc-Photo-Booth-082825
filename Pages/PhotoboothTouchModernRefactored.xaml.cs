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
using Photobooth.Windows;
using static Photobooth.Services.PhotoboothSessionService;
using static Photobooth.Services.PhotoboothWorkflowService;
using static Photobooth.Services.PhotoboothUIService;
using static Photobooth.Services.PhotoCompositionService;
using static Photobooth.Services.GallerySessionService;

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
        private Services.FilterSelectionService _filterSelectionService;
        private Services.RetakeSelectionService _retakeSelectionService;
        private Services.InfoPanelService _infoPanelService;
        private bool _isProcessingRetakes = false;
        
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
        private Services.InfoPanelUIService _infoPanelUIService;
        
        // Database connection
        private DatabaseOperations _database;
        #endregion

        #region Minimal State Management (Keep UI-only state here)
        private DispatcherTimer _liveViewTimer;
        private DispatcherTimer _countdownTimer;
        private DispatcherTimer _recordingDurationTimer;
        private DateTime _recordingStartTime;
        private bool _isDisplayingCapturedPhoto = false; // Flag to prevent live view from overwriting captured photo
        private bool _isCapturing = false; // Flag to track if we're actively capturing a photo
        private bool _isDisplayingSessionResult = false; // Flag to prevent live view from overwriting session result
        private bool _isRetaking = false; // Flag to track if we're doing a retake
        private int _currentRetakeIndex = -1; // Index of photo being retaken
        private int _recordingFrameCounter = 0; // Counter for debug logging during recording
        private DateTime _lastFpsUpdate = DateTime.Now;
        private int _fpsFrameCount = 0;
        private double _currentFps = 0;
        
        private void SetDisplayingSessionResult(bool value)
        {
            if (_isDisplayingSessionResult != value)
            {
                _isDisplayingSessionResult = value;
                Log.Debug($"★★★ FLAG CHANGED: _isDisplayingSessionResult = {value}");

                // Re-apply camera rotation when going back to live view
                if (!value)
                {
                    ApplyCameraRotation();
                }
            }
        }
        private ShareResult _currentShareResult;
        
        // Event/Template state for UI display only
        private EventData _currentEvent;
        private TemplateData _currentTemplate;

        // Template overlay for live view (enabled by default)
        private bool _showTemplateOverlay = true;
        private int _currentPhotoIndex = 0; // Track which photo we're taking
        private Services.TemplateOverlayService _templateOverlayService;
        
        // Current module
        private IPhotoboothModule _activeModule;
        private ModuleManager _moduleManager;
        private UILayoutService _uiLayoutService;
        
        // Gallery navigation state
        private List<Services.SessionGalleryData> _gallerySessions;
        private int _currentGallerySessionIndex;
        private Services.SessionGalleryData _currentGallerySession;
        private bool _isInGalleryMode;

        // Track if session is being cleared (to prevent QR code auto-show)
        private bool _isSessionBeingCleared = false;
        private CancellationTokenSource _autoUploadCancellationTokenSource;
        
        // Gallery auto-clear timer
        private DispatcherTimer _galleryTimer;
        private int _galleryTimerElapsed;
        
        // Camera reconnect timer
        private DispatcherTimer _cameraReconnectTimer;
        private bool _isReconnecting;
        
        // Pending print modal parameters
        private string _pendingPrintPath;
        private string _pendingPrintSessionId;
        private bool _pendingPrintIs2x6Template;
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
            
            // Add keyboard protection
            this.PreviewKeyDown += Page_PreviewKeyDown;
        }

        #region Initialization
        private void InitializeServices()
        {
            Log.Debug("Initializing clean services architecture...");
            
            // Core camera system
            _cameraManager = CameraSessionManager.Instance;
            
            // Use a single DatabaseOperations instance to avoid multiple database connections
            _databaseOps = new DatabaseOperations();
            _database = _databaseOps; // Use same instance
            
            // Initialize CLEAN SERVICES that do all the work, sharing the DatabaseOperations instance
            _sessionService = new Services.PhotoboothSessionService(_databaseOps);
            _workflowService = new Services.PhotoboothWorkflowService(_cameraManager, _sessionService);
            _uiService = new Services.PhotoboothUIService();
            _compositionService = new Services.PhotoCompositionService();
            _galleryService = new Services.EventGalleryService();
            _galleryBrowserService = new Services.GalleryBrowserService();
            _infoPanelService = new Services.InfoPanelService(_cameraManager);
            _infoPanelUIService = Services.InfoPanelUIService.Instance;
            
            // Connect InfoPanelUIService to InfoPanelService
            _infoPanelUIService.SetInfoPanelService(_infoPanelService);
            
            // Initialize existing services from Services folder
            _eventTemplateService = new EventTemplateService();
            _printingService = new PrintingService();
            _templateOverlayService = new Services.TemplateOverlayService();
            _shareService = CloudShareProvider.GetShareService();
            _sessionManager = new SessionManager();
            
            // Initialize camera settings service (lazy loading - only when overlay shown)
            var cameraSettingsService = CameraSettingsService.Instance;
            cameraSettingsService.RegisterOverlayVisibilityCallback(visible =>
            {
                Dispatcher.Invoke(() =>
                {
                    CameraSettingsOverlayControl.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                });
            });
            
            // Initialize PIN lock service
            var pinLockService = PinLockService.Instance;
            pinLockService.SetPinEntryOverlay(PinEntryOverlayControl);
            pinLockService.InterfaceLockStateChanged += OnInterfaceLockStateChanged;
            
            // Initialize sharing UI service for modal display management first
            // Pass the MainGrid instead of the page so UI elements can be added properly
            // Also pass the session service for timer management
            _sharingUIService = new Services.SharingUIService(MainGrid, _sessionService);
            
            // Wire sharing UI service events
            _sharingUIService.QrCodeOverlayClosed += OnQrCodeOverlayClosed;
            _sharingUIService.SmsOverlayClosed += OnSmsOverlayClosed;
            _sharingUIService.SendSmsRequested += OnSendSmsRequested;
            _sharingUIService.ShowSmsFromQrRequested += OnShowSmsFromQrRequested;
            _sharingUIService.UpdateSmsButtonState += OnUpdateSmsButtonState;
            _sharingUIService.UpdateQrButtonState += OnUpdateQrButtonState;
            
            // Wire event selection overlay events
            if (EventSelectionOverlayControl != null)
            {
                EventSelectionOverlayControl.EventSelected += OnEventSelectionOverlayEventSelected;
                EventSelectionOverlayControl.SelectionCancelled += OnEventSelectionOverlayCancelled;
            }

            // Subscribe to event expiration from EventSelectionService
            EventSelectionService.Instance.EventExpired += OnEventExpired;
            
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
            
            // Initialize PhotoCaptureService with shared DatabaseOperations instance
            _photoCaptureService = new PhotoCaptureService(_databaseOps); // Share the instance
            // Share the PhotoCaptureService instance with SessionService to maintain retake state
            _sessionService.SetPhotoCaptureService(_photoCaptureService);
            _photoboothService = new PhotoboothService();
            // SharingOperations requires parent page - remove it, use share service instead
            _offlineQueueService = OfflineQueueService.Instance;
            _uiLayoutService = new UILayoutService();
            _templateSelectionService = new Services.TemplateSelectionService();
            _templateSelectionUIService = new Services.TemplateSelectionUIService();
            _filterSelectionService = new Services.FilterSelectionService();
            _retakeSelectionService = new Services.RetakeSelectionService();
            
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
            // Configure live view FPS from settings (default: 30 FPS for smooth live view)
            // Common options: 60 FPS = 16ms, 30 FPS = 33ms, 20 FPS = 50ms, 15 FPS = 66ms, 10 FPS = 100ms
            int liveViewFps = Properties.Settings.Default.LiveViewFrameRate;
            
            // Clamp FPS to reasonable range (5-60 FPS)
            liveViewFps = Math.Max(5, Math.Min(60, liveViewFps));
            
            int intervalMs = 1000 / liveViewFps;
            
            _liveViewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs)
            };
            _liveViewTimer.Tick += LiveViewTimer_Tick;
            
            Log.Debug($"Live view timer configured for {liveViewFps} FPS ({intervalMs}ms interval) from settings");
            
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
            
            // Initialize gallery timer
            _galleryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _galleryTimer.Tick += GalleryTimer_Tick;
            
            // Initialize camera reconnect timer
            _cameraReconnectTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3) // Try to reconnect every 3 seconds
            };
            _cameraReconnectTimer.Tick += CameraReconnectTimer_Tick;
        }
        
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            // Handled by workflow service now
        }
        
        private void CameraReconnectTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                Log.Debug("CameraReconnectTimer_Tick: Attempting to reconnect camera...");

                // Check if camera is now connected
                if (DeviceManager?.SelectedCameraDevice != null && DeviceManager.SelectedCameraDevice.IsConnected)
                {
                    // Camera reconnected successfully
                    _cameraReconnectTimer.Stop();
                    _isReconnecting = false;

                    UpdateCameraStatus($"Camera reconnected: {DeviceManager.SelectedCameraDevice.DeviceName}");
                    Log.Debug($"Camera successfully reconnected: {DeviceManager.SelectedCameraDevice.DeviceName}");

                    // Only restart live view if we have an active session that is NOT complete
                    // and we're not currently displaying session results or in gallery mode
                    if (_sessionService?.IsSessionActive == true &&
                        !_isDisplayingSessionResult &&
                        !_isInGalleryMode)
                    {
                        Log.Debug("Restarting live view - session is active and not displaying results");
                        _liveViewTimer.Start();
                    }
                    else
                    {
                        Log.Debug($"NOT restarting live view - Session active: {_sessionService?.IsSessionActive}, " +
                                $"Displaying result: {_isDisplayingSessionResult}, Gallery mode: {_isInGalleryMode}");
                    }
                }
                else
                {
                    // Try to force reconnect using CameraSessionManager
                    Log.Debug("Camera still disconnected, attempting force reconnect...");
                    UpdateCameraStatus("Reconnecting camera...");

                    // Use the singleton's ForceReconnect method
                    CameraSessionManager.Instance.ForceReconnect();

                    // Check again after reconnect attempt
                    if (DeviceManager?.SelectedCameraDevice != null && DeviceManager.SelectedCameraDevice.IsConnected)
                    {
                        _cameraReconnectTimer.Stop();
                        _isReconnecting = false;

                        UpdateCameraStatus($"Camera reconnected: {DeviceManager.SelectedCameraDevice.DeviceName}");
                        Log.Debug($"Camera successfully reconnected after force reconnect: {DeviceManager.SelectedCameraDevice.DeviceName}");

                        // Only restart live view if we have an active session that is NOT complete
                        // and we're not currently displaying session results or in gallery mode
                        if (_sessionService?.IsSessionActive == true &&
                            !_isDisplayingSessionResult &&
                            !_isInGalleryMode)
                        {
                            Log.Debug("Restarting live view after force reconnect - session is active and not displaying results");
                            _liveViewTimer.Start();
                        }
                        else
                        {
                            Log.Debug($"NOT restarting live view after force reconnect - Session active: {_sessionService?.IsSessionActive}, " +
                                    $"Displaying result: {_isDisplayingSessionResult}, Gallery mode: {_isInGalleryMode}");
                        }
                    }
                    else
                    {
                        Log.Debug("Camera reconnect attempt failed, will retry in 3 seconds...");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in CameraReconnectTimer_Tick: {ex.Message}");
                UpdateCameraStatus("Camera reconnection failed - retrying...");
            }
        }
        
        private void GalleryTimer_Tick(object sender, EventArgs e)
        {
            _galleryTimerElapsed++;
            
            // Use the same timeout setting as session timeout
            int timeoutSeconds = Properties.Settings.Default.AutoClearTimeout;
            
            if (_galleryTimerElapsed >= timeoutSeconds)
            {
                Log.Debug($"Gallery auto-clearing after {timeoutSeconds} seconds");
                StopGalleryTimer();
                
                // Exit gallery mode
                ExitGalleryMode();
            }
        }
        
        private void StartGalleryTimer()
        {
            if (Properties.Settings.Default.AutoClearSession)
            {
                Log.Debug($"Starting gallery timer for {Properties.Settings.Default.AutoClearTimeout} seconds");
                _galleryTimerElapsed = 0;
                _galleryTimer?.Start();
            }
        }
        
        private void StopGalleryTimer()
        {
            if (_galleryTimer != null && _galleryTimer.IsEnabled)
            {
                Log.Debug("Stopping gallery timer");
                _galleryTimer.Stop();
                _galleryTimerElapsed = 0;
            }
        }
        
        private void ExitGalleryMode()
        {
            Dispatcher.Invoke(() =>
            {
                // Stop gallery timer
                StopGalleryTimer();
                
                // Clear gallery state
                _isInGalleryMode = false;
                _currentGallerySession = null;
                _gallerySessions = null;
                _currentGallerySessionIndex = 0;
                
                // Hide action buttons
                if (actionButtonsPanel != null)
                    actionButtonsPanel.Visibility = Visibility.Collapsed;
                
                // Clear photos container
                ClearPhotosContainer();
                
                // Show start button overlay
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                }
                
                // Keep gallery browser visible and refresh it
                LoadGalleryPreview();
                
                _uiService?.UpdateStatus("Gallery session closed");
                
                Log.Debug("Exited gallery mode - gallery browser remains visible");
            });
        }

        private void InitializeModules()
        {
            // Use singleton instance
            _moduleManager = ModuleManager.Instance;
            
            // Initialize modules when camera is ready
            var outputFolder = FileValidationService.Instance.GetDefaultPhotoOutputFolder("Photobooth");
            
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
            
            // Initialize lock button state
            var pinLockService = PinLockService.Instance;
            UpdateLockButtonAppearance(pinLockService.IsInterfaceLocked);
            UpdateSettingsAccessibility(!pinLockService.IsInterfaceLocked);
            
            // Add window protection against minimize when locked
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                // Remove system buttons if not already done
                parentWindow.WindowStyle = WindowStyle.None;
                parentWindow.ResizeMode = ResizeMode.NoResize;
                
                // Handle window events
                parentWindow.StateChanged += ParentWindow_StateChanged;
                parentWindow.Deactivated += ParentWindow_Deactivated;
                parentWindow.Closing += ParentWindow_Closing;
                parentWindow.PreviewKeyDown += ParentWindow_PreviewKeyDown;
            }
            
            // Initialize camera
            await InitializeCamera();
            
            // Initialize modules with camera
            if (DeviceManager?.SelectedCameraDevice != null)
            {
                var outputFolder = FileValidationService.Instance.GetDefaultPhotoOutputFolder("Photobooth");
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

            // Initialize template overlay (enabled by default)
            if (_showTemplateOverlay)
            {
                if (templateOverlayContainer != null)
                {
                    templateOverlayContainer.Visibility = Visibility.Visible;
                    Log.Debug($"Template overlay container visibility set to Visible");
                }
                else
                {
                    Log.Debug("Template overlay container is null - cannot show overlay");
                }

                // Log current template state
                Log.Debug($"Current template: {_currentTemplate?.Name ?? "null"}");
            }
            else
            {
                Log.Debug("Template overlay is disabled");
            }

            // Apply camera rotation setting
            ApplyCameraRotation();

            // Subscribe to settings changes
            var settingsService = Services.SettingsManagementService.Instance;
            if (settingsService != null)
            {
                settingsService.SettingChanged += OnSettingChanged;
            }

            // Load gallery preview
            LoadGalleryPreview();
        }

        private void OnSettingChanged(object sender, Services.SettingChangedEventArgs e)
        {
            // Check if camera rotation changed
            if (e.SettingName == "CameraRotation")
            {
                Dispatcher.Invoke(() => ApplyCameraRotation());
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from settings changes
            var settingsService = Services.SettingsManagementService.Instance;
            if (settingsService != null)
            {
                settingsService.SettingChanged -= OnSettingChanged;
            }

            // Remove window event handlers
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.StateChanged -= ParentWindow_StateChanged;
                parentWindow.Deactivated -= ParentWindow_Deactivated;
                parentWindow.Closing -= ParentWindow_Closing;
                parentWindow.PreviewKeyDown -= ParentWindow_PreviewKeyDown;
            }

            // Unsubscribe from event expiration
            EventSelectionService.Instance.EventExpired -= OnEventExpired;
            
            Cleanup();
        }

        public void Cleanup()
        {
            try
            {
                Log.Debug("PhotoboothTouchModernRefactored cleanup starting...");
                
                // Stop all timers first
                _liveViewTimer?.Stop();
                _countdownTimer?.Stop();
                _cameraReconnectTimer?.Stop();
                _isReconnecting = false;
                
                // Clean up services
                if (_sessionService != null)
                {
                    try
                    {
                        _sessionService.ClearSession();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Error clearing session: {ex.Message}");
                    }
                }
                
                if (_workflowService != null)
                {
                    try
                    {
                        _workflowService.StopWorkflow();
                        _workflowService.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Error stopping workflow: {ex.Message}");
                    }
                }
                
                if (_uiService != null)
                {
                    try
                    {
                        _uiService.HideAllControls();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Error hiding controls: {ex.Message}");
                    }
                }
                
                // Stop live view - but don't try to access camera device here
                // Let App.xaml.cs handle camera cleanup in OnExit
                
                try
                {
                    RemoveEventHandlers();
                }
                catch (Exception ex)
                {
                    Log.Debug($"Error removing event handlers: {ex.Message}");
                }
                
                try
                {
                    _activeModule?.Cleanup();
                }
                catch (Exception ex)
                {
                    Log.Debug($"Error cleaning up module: {ex.Message}");
                }
                
                try
                {
                    _viewModel?.Cleanup();
                }
                catch (Exception ex)
                {
                    Log.Debug($"Error cleaning up viewmodel: {ex.Message}");
                }
                
                Log.Debug("PhotoboothTouchModernRefactored cleanup completed");
            }
            catch (Exception ex)
            {
                Log.Error($"Cleanup error: {ex.Message}");
                Log.Debug($"Overall cleanup error: {ex.Message}");
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
                // Set the shared browser service instance so it uses the correct event filtering
                GalleryBrowserModal.SetBrowserService(_galleryBrowserService);
            }
            
            // Subscribe to camera events like original PhotoboothTouchModern
            if (DeviceManager != null)
            {
                DeviceManager.CameraSelected += DeviceManager_CameraSelected;
                DeviceManager.CameraConnected += DeviceManager_CameraConnected; 
                // Don't subscribe to PhotoCaptured - workflow service handles this
                DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;
                Log.Debug("SetupEventHandlers: Subscribed to camera events (PhotoCaptured handled by workflow)");
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
            
            if (_filterSelectionService != null)
            {
                _filterSelectionService.FilterSelected += OnServiceFilterSelected;
                _filterSelectionService.FilterSelectionCancelled += OnServiceFilterSelectionCancelled;
                _filterSelectionService.ShowFilterSelectionRequested += OnServiceShowFilterSelectionRequested;
                _filterSelectionService.HideFilterSelectionRequested += OnServiceHideFilterSelectionRequested;
            }
            
            if (_retakeSelectionService != null)
            {
                _retakeSelectionService.RetakeSelected += OnServiceRetakeSelected;
                _retakeSelectionService.RetakeRequested += OnServiceRetakeRequested;
                _retakeSelectionService.ShowRetakeSelectionRequested += OnServiceShowRetakeSelectionRequested;
                _retakeSelectionService.HideRetakeSelectionRequested += OnServiceHideRetakeSelectionRequested;
                _retakeSelectionService.RetakeTimerTick += OnServiceRetakeTimerTick;
                _retakeSelectionService.RetakePhotoRequired += OnServiceRetakePhotoRequired;
                _retakeSelectionService.RetakeProcessCompleted += OnServiceRetakeProcessCompleted;
            }
            
            if (_infoPanelService != null)
            {
                _infoPanelService.PrinterStatusUpdated += OnInfoPanelPrinterStatusUpdated;
                _infoPanelService.CloudSyncStatusUpdated += OnInfoPanelCloudSyncStatusUpdated;
                _infoPanelService.CameraStatusUpdated += OnInfoPanelCameraStatusUpdated;
                _infoPanelService.PhotoCountUpdated += OnInfoPanelPhotoCountUpdated;
                _infoPanelService.SyncProgressUpdated += OnInfoPanelSyncProgressUpdated;
            }
            
            // Subscribe to InfoPanelUIService events for clean UI updates
            if (_infoPanelUIService != null)
            {
                _infoPanelUIService.CloudSyncUIUpdateRequested += OnCloudSyncUIUpdate;
                _infoPanelUIService.CloudShareUIUpdateRequested += OnCloudShareUIUpdate;
                _infoPanelUIService.PrinterStatusUIUpdateRequested += OnPrinterStatusUIUpdate;
                _infoPanelUIService.CameraStatusUIUpdateRequested += OnCameraStatusUIUpdate;
            }
            
            // Subscribe to queue service events for status indicators
            var queueService = PhotoboothQueueService.Instance;
            if (queueService != null)
            {
                queueService.OnQueueStatusChanged += OnQueueStatusChanged;
                queueService.OnQRCodeVisibilityChanged += OnQRCodeVisibilityChanged;
                queueService.OnSMSProcessed += OnSMSProcessed;
                Log.Debug("Subscribed to queue service events for status indicators");
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
                // PhotoCaptured not subscribed
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
            
            if (_infoPanelService != null)
            {
                _infoPanelService.PrinterStatusUpdated -= OnInfoPanelPrinterStatusUpdated;
                _infoPanelService.CloudSyncStatusUpdated -= OnInfoPanelCloudSyncStatusUpdated;
                _infoPanelService.CameraStatusUpdated -= OnInfoPanelCameraStatusUpdated;
                _infoPanelService.PhotoCountUpdated -= OnInfoPanelPhotoCountUpdated;
                _infoPanelService.SyncProgressUpdated -= OnInfoPanelSyncProgressUpdated;
                _infoPanelService.Dispose();
            }
            
            // Unsubscribe from InfoPanelUIService events
            if (_infoPanelUIService != null)
            {
                _infoPanelUIService.CloudSyncUIUpdateRequested -= OnCloudSyncUIUpdate;
                _infoPanelUIService.CloudShareUIUpdateRequested -= OnCloudShareUIUpdate;
                _infoPanelUIService.PrinterStatusUIUpdateRequested -= OnPrinterStatusUIUpdate;
                _infoPanelUIService.CameraStatusUIUpdateRequested -= OnCameraStatusUIUpdate;
                _infoPanelUIService.Dispose();
            }
            
            // Unsubscribe from queue service events
            var queueService = PhotoboothQueueService.Instance;
            if (queueService != null)
            {
                queueService.OnQueueStatusChanged -= OnQueueStatusChanged;
                queueService.OnQRCodeVisibilityChanged -= OnQRCodeVisibilityChanged;
                queueService.OnSMSProcessed -= OnSMSProcessed;
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
                    // Use GallerySessionService to prepare session
                    var loadResult = GallerySessionService.Instance.PrepareSessionForLoading(e.Session, e.Photos);
                    if (loadResult == null) return;
                    
                    // Store current gallery session
                    _currentGallerySession = e.Session;
                    
                    // Clear photos container
                    ClearPhotosContainer();
                    
                    // Process each photo action from service
                    string lastComposedPath = null;
                    foreach (var action in loadResult.PhotoActions)
                    {
                        ProcessPhotoLoadAction(action);
                        
                        // Track composed image for auto-display
                        if (action.Action == "AddComposed")
                        {
                            lastComposedPath = action.FilePath;
                        }
                    }
                    
                    // Auto-display composed image in live view if available
                    if (!string.IsNullOrEmpty(lastComposedPath))
                    {
                        _uiService.DisplayImage(lastComposedPath);
                        Log.Debug($"Auto-displaying composed image: {lastComposedPath}");
                    }
                    
                    // Update UI with status
                    _uiService.UpdateStatus(loadResult.StatusMessage);
                    
                    // Show gallery navigation
                    ShowGalleryNavigationButtons(_currentGallerySessionIndex, _gallerySessions?.Count ?? 1);
                    
                    Log.Debug($"Session loaded: {loadResult.PhotosLoaded} photos displayed, {loadResult.PhotosSkipped} skipped");
                }
                catch (Exception ex)
                {
                    Log.Error($"Error loading session: {ex.Message}");
                }
            });
        }
        
        private void ClearPhotosContainer()
        {
            if (photosContainer != null)
            {
                int beforeClear = photosContainer.Children.Count;
                photosContainer.Children.Clear();
                Log.Debug($"Cleared {beforeClear} items from photos container");
            }
        }
        
        private void ProcessPhotoLoadAction(PhotoLoadAction action)
        {
            Log.Debug($"ProcessPhotoLoadAction: Action={action.Action}, FilePath={action.FilePath}, PhotoType={action.PhotoType}");
            
            switch (action.Action)
            {
                case "AddGif":
                    Log.Debug($"  Adding GIF thumbnail for: {action.FilePath}");
                    AddGifThumbnailForGallery(action.FilePath, action.ThumbnailPath);
                    break;
                case "AddComposed":
                    Log.Debug($"  Adding composed thumbnail for: {action.FilePath}");
                    AddComposedThumbnail(action.FilePath);
                    
                    // Also display the composed image in live view like we do for regular sessions
                    if (FileValidationService.Instance.ValidateFilePath(action.FilePath))
                    {
                        Log.Debug($"  Displaying composed image in live view: {action.FilePath}");
                        _uiService.DisplayImage(action.FilePath);
                    }
                    break;
                case "AddPhoto":
                    Log.Debug($"  Adding original photo thumbnail for: {action.FilePath}");
                    AddPhotoThumbnail(action.FilePath);
                    break;
                default:
                    Log.Debug($"  Unknown action: {action.Action}");
                    break;
            }
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
                    
                    // Start gallery auto-clear timer
                    StartGalleryTimer();
                    
                    // Trigger queue service to update button status indicators for gallery mode
                    // The status will be updated via QueueStatusUpdated event when buttons are clicked
                    // For now, ensure the indicators reflect the current connection state
                    var queueService = PhotoboothQueueService.Instance;
                    if (queueService != null && _currentGallerySession != null)
                    {
                        // Check QR visibility for current gallery session which will trigger status updates
                        _ = Task.Run(async () => 
                        {
                            // Use SessionFolder (GUID) for database lookup, not SessionName (timestamp)
                            var sessionId = _currentGallerySession.SessionFolder;
                            var result = await queueService.CheckQRVisibilityAsync(sessionId, true);
                            Log.Debug($"Gallery mode QR check triggered for session {sessionId} ({_currentGallerySession.SessionName}) - visibility: {result.IsVisible}, message: {result.Message}");
                            
                            // Update button enabled states on UI thread
                            Dispatcher.Invoke(() =>
                            {
                                UpdateQRStatusIndicator(result.IsVisible, result.IsVisible ? "QR code ready" : "Processing QR code...");
                                // Also update SMS status - SMS is always enabled for gallery mode as it will queue if needed
                                UpdateSMSStatusIndicator(true, "SMS ready");
                            });
                        });
                    }
                    
                    // Also log individual button visibility and set them based on settings
                    if (printButton != null)
                    {
                        bool showPrintButton = Properties.Settings.Default.ShowPrintButton && Properties.Settings.Default.EnablePrinting;
                        printButton.Visibility = showPrintButton ? Visibility.Visible : Visibility.Collapsed;
                        Log.Debug($"  - Print button visibility: {printButton.Visibility} (ShowPrintButton: {Properties.Settings.Default.ShowPrintButton}, EnablePrinting: {Properties.Settings.Default.EnablePrinting})");
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
                // Stop reconnect timer if it's running
                if (_isReconnecting && _cameraReconnectTimer != null)
                {
                    _cameraReconnectTimer.Stop();
                    _isReconnecting = false;
                    Log.Debug("Stopped camera reconnect timer - camera is now connected");
                }
                
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
                UpdateCameraStatus("Camera disconnected - Attempting to reconnect...");
                _liveViewTimer.Stop();
                liveViewImage.Source = null;
                
                // Start auto-reconnect timer
                if (!_isReconnecting && _cameraReconnectTimer != null)
                {
                    _isReconnecting = true;
                    _cameraReconnectTimer.Start();
                    Log.Debug("Started camera reconnect timer");
                }
            }));
        }

        // REMOVED: DeviceManager_PhotoCaptured - Workflow service handles all photo captures
        // In photographer mode, when trigger is pressed, the workflow service's 
        // PhotoCaptured handler processes it directly

        #endregion

        #region Service Event Handlers - Clean Architecture
        private void OnServiceSessionStarted(object sender, Services.SessionStartedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Service session started: {e.SessionId}");
                _uiService.UpdateStatus($"Session started for {e.Event?.Name}");
                _uiService.ShowSessionControls();

                // Load template for overlay if enabled and we have a template from session service
                if (_showTemplateOverlay && _sessionService?.CurrentTemplate != null)
                {
                    LoadTemplateForOverlay(_sessionService.CurrentTemplate);
                }

                // Hide the start button when session starts
                _uiService.HideStartButton();
                Log.Debug("Start button hidden - session in progress");
                
                // Hide gallery preview during session
                UpdateGalleryPreviewVisibility(true);
                
                // Show cancel session button
                if (cancelSessionButton != null)
                {
                    cancelSessionButton.Visibility = Visibility.Visible;
                    Log.Debug("Cancel session button shown");
                }
                
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
                
                // Don't update UI or continue if we're processing retakes
                if (_isProcessingRetakes)
                {
                    Log.Debug("Skipping normal flow - processing retakes");
                    return;
                }
                
                // Update UI with thumbnail and counter
                UpdatePhotoUI(e.PhotoPath, e.PhotoIndex, e.TotalPhotos);
                
                if (!e.IsComplete)
                {
                    // Continue capturing photos
                    await HandleNextPhotoCapture(e.PhotoIndex, e.TotalPhotos);
                }
                else
                {
                    // All photos captured - complete session
                    await HandleSessionCompletion();
                }
            });
        }
        
        private void UpdatePhotoUI(string photoPath, int photoIndex, int totalPhotos)
        {
            _uiService.AddPhotoThumbnail(photoPath, photoIndex);
            _uiService.UpdatePhotoCounter(photoIndex, totalPhotos);
        }
        
        private async Task HandleNextPhotoCapture(int photoIndex, int totalPhotos)
        {
            _uiService.UpdateStatus($"Photo {photoIndex} captured! Get ready for photo {photoIndex + 1}...");
            
            if (Properties.Settings.Default.PhotographerMode)
            {
                // Photographer mode - just update status
                Log.Debug($"Photographer mode - waiting for manual trigger for photo {photoIndex + 1}");
                _uiService.UpdateStatus($"Ready for photo {photoIndex + 1} - Press camera trigger when ready");
                
                // The workflow is already active and waiting for the next trigger
                // Don't need to do anything else
            }
            else
            {
                // Auto mode - delay then capture
                int delaySeconds = Properties.Settings.Default.DelayBetweenPhotos;
                Log.Debug($"Auto mode - waiting {delaySeconds} seconds before next photo");
                await Task.Delay(delaySeconds * 1000);
                
                Log.Debug($"Starting capture workflow for photo {photoIndex + 1} of {totalPhotos}");
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            }
        }
        
        private async Task HandleSessionCompletion()
        {
            Log.Debug("All photos captured, processing session");
            _uiService.UpdateStatus("Processing your photos...");
            
            // First, check if retake is enabled
            if (Properties.Settings.Default.EnableRetake)
            {
                Log.Debug("Showing retake selection UI");
                // Initialize retake selection with captured photos
                var capturedPhotos = _sessionService.CapturedPhotoPaths;
                _retakeSelectionService.InitializeRetakeSelection(capturedPhotos);
                _retakeSelectionService.RequestRetakeSelection();
                // The flow will continue in OnServiceRetakeSelected after retake is complete
                return;
            }
            
            // If no retake, proceed to filters
            await CheckAndApplyFilters();
        }
        
        private async Task CheckAndApplyFilters()
        {
            Log.Debug("===== CheckAndApplyFilters STARTING =====");
            Log.Debug($"  EnableFilters: {Properties.Settings.Default.EnableFilters}");
            Log.Debug($"  Session photos count: {_sessionService.CapturedPhotoPaths?.Count ?? 0}");
            
            try
            {
                // Check if filters are enabled
                if (Properties.Settings.Default.EnableFilters)
                {
                    Log.Debug("Filters are enabled, checking for auto-apply filter");
                    
                    // Check for auto-apply filter
                    var autoFilter = _filterSelectionService.GetAutoApplyFilter();
                    Log.Debug($"Auto filter result: {autoFilter}");
                    
                    if (autoFilter.HasValue)
                    {
                        // Auto-apply filter without showing UI
                        Log.Debug($"Auto-applying filter: {autoFilter.Value}");
                        _sessionService.SetSelectedFilter(autoFilter.Value);
                        
                        // Apply filter to photos
                        Log.Debug("Applying filter to photos...");
                        await _sessionService.ApplyFilterToPhotosAsync();
                        Log.Debug("Filter applied successfully");
                        
                        // Proceed with composition
                        Log.Debug("Proceeding to composition after filter");
                        await ProceedWithComposition();
                    }
                    else if (_filterSelectionService.ShouldShowFilterSelection())
                    {
                        // Show filter selection UI only if allowed and should show
                        Log.Debug("Showing filter selection UI");
                        _filterSelectionService.RequestFilterSelection();
                        // The filter will be applied in the OnServiceFilterSelected event handler
                        Log.Debug("Waiting for filter selection...");
                        // IMPORTANT: Don't proceed here - wait for OnServiceFilterSelected
                    }
                    else
                    {
                        // No auto-filter and no selection needed - proceed directly
                        Log.Debug("No filter to apply, proceeding to composition directly");
                        await ProceedWithComposition();
                        Log.Debug("ProceedWithComposition completed (no filter path)");
                    }
            }
            else
            {
                // No filters - proceed directly to composition
                Log.Debug("Filters disabled, proceeding directly to composition");
                await ProceedWithComposition();
                Log.Debug("ProceedWithComposition completed (filters disabled path)");
            }
            
            Log.Debug("===== CheckAndApplyFilters COMPLETED =====");
        }
        catch (Exception ex)
        {
            Log.Error($"CheckAndApplyFilters ERROR: {ex.Message}");
            Log.Error($"Stack trace: {ex.StackTrace}");
            // Try to continue anyway
            Log.Error("Attempting to proceed despite error...");
            await ProceedWithComposition();
        }
        }
        
        private async Task ProceedWithComposition()
        {
            Log.Debug("ProceedWithComposition: Starting composition process");
            
            // Check if we need to trigger animation generation
            // This might be needed if retakes happened before all photos were captured
            bool animationStarted = _sessionService.IsAnimationGenerationStarted;
            Log.Debug($"ProceedWithComposition: Animation generation already started: {animationStarted}");
            
            if (!animationStarted && _sessionService.CurrentPhotoIndex >= _sessionService.TotalPhotosRequired)
            {
                Log.Debug("ProceedWithComposition: Triggering animation generation (not yet started)");
                await _sessionService.TriggerBackgroundProcessingAsync();
            }
            else
            {
                Log.Debug("ProceedWithComposition: Skipping animation trigger (already started or not enough photos)");
            }
            
            // Compose template if available
            await ComposeSessionTemplate();
            
            // Complete session (triggers auto-upload and finalization)
            Log.Debug("ProceedWithComposition: Completing session to trigger finalization");
            var completed = await _sessionService.CompleteSessionAsync();
            
            if (completed)
            {
                Log.Debug("ProceedWithComposition: Session completed successfully");
            }
            else
            {
                Log.Error("ProceedWithComposition: Session completion failed!");
            }
        }
        
        private async Task ComposeSessionTemplate()
        {
            Log.Debug($"ComposeSessionTemplate: Starting - Template={_currentTemplate?.Name}, PhotoCount={_sessionService.CapturedPhotoPaths?.Count}");
            
            if (_currentTemplate == null || _sessionService.CapturedPhotoPaths?.Count == 0)
            {
                Log.Debug("ComposeSessionTemplate: No template or photos available for composition");
                return;
            }
            
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
                string displayPath = _compositionService.LastDisplayPath ?? composedPath;
                string printPath = _compositionService.LastPrintPath ?? composedPath;
                _sessionService.SetComposedImagePaths(displayPath, printPath);
            }
        }
        
        private void OnInterfaceLockStateChanged(object sender, bool isLocked)
        {
            Dispatcher.Invoke(() =>
            {
                // Update UI based on lock state
                if (isLocked)
                {
                    // Disable controls when locked
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Collapsed;
                    if (bottomControlBar != null)
                        bottomControlBar.Visibility = Visibility.Collapsed;
                    // Show lock indicator
                    _uiService.UpdateStatus("Interface Locked 🔒");
                }
                else
                {
                    // Enable controls when unlocked
                    if (startButtonOverlay != null)
                        startButtonOverlay.Visibility = Visibility.Visible;
                    if (bottomControlBar != null)
                        bottomControlBar.Visibility = Visibility.Visible;
                    _uiService.UpdateStatus("Ready");
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

                    // Clear template overlay when displaying session results
                    if (templateOverlayCanvas != null)
                    {
                        templateOverlayCanvas.Children.Clear();
                        Log.Debug("Cleared template overlay for session results display");
                    }

                    // Display composed image in live view if available
                    if (FileValidationService.Instance.ValidateFilePath(e.CompletedSession.ComposedImagePath))
                    {
                        Log.Debug($"★★★ COMPOSED IMAGE PATH EXISTS: {e.CompletedSession.ComposedImagePath}");
                        Log.Debug($"★★★ Calling _uiService.DisplayImage to show composed image");
                        _uiService.DisplayImage(e.CompletedSession.ComposedImagePath);
                        
                        // Add composed image thumbnail to strip
                        _uiService.AddPhotoThumbnail(e.CompletedSession.ComposedImagePath, -1);
                    }
                    else
                    {
                        bool pathValid = FileValidationService.Instance.ValidateFilePathWithLogging(e.CompletedSession.ComposedImagePath, "SessionCompleted");
                        Log.Error($"★★★ NO COMPOSED IMAGE! Path: {e.CompletedSession.ComposedImagePath}, Valid: {pathValid}");
                    }
                    
                    // MP4/GIF will be added as thumbnail via OnServiceAnimationReady when ready
                    Log.Debug("Session completed - composed image displayed, waiting for animation");
                    
                    // Show completion UI
                    _uiService.UpdateStatus("Session complete!");
                    _uiService.ShowCompletionControls();
                    
                    // ENSURE we're not in retake mode anymore
                    _isProcessingRetakes = false;
                    
                    // Show unified action buttons panel for current session (not gallery mode)
                    _isInGalleryMode = false;
                    if (actionButtonsPanel != null) 
                    {
                        actionButtonsPanel.Visibility = Visibility.Visible;
                        Log.Debug("Showing action buttons panel after session completion");
                    }
                    else
                    {
                        Log.Error("actionButtonsPanel is NULL - cannot show action buttons!");
                    }
                    
                    // Check QR and SMS status for current session to update indicators
                    if (_sessionService?.CurrentSessionId != null)
                    {
                        var queueService = PhotoboothQueueService.Instance;
                        if (queueService != null)
                        {
                            // Check QR code status and queue status which will trigger status update via events
                            _ = Task.Run(async () =>
                            {
                                // Try multiple times to get the URL as upload may take time
                                for (int i = 0; i < 5; i++)
                                {
                                    await Task.Delay(2000); // Wait 2 seconds between checks
                                    
                                    // Clear cache before checking to ensure we get fresh data
                                    queueService.InvalidateUrlCache(_sessionService.CurrentSessionId, false);
                                    
                                    var qrResult = await queueService.CheckQRVisibilityAsync(_sessionService.CurrentSessionId, false);
                                    Log.Debug($"Post-session QR check attempt {i+1}: visibility={qrResult.IsVisible}, message={qrResult.Message}");
                                    
                                    // Trigger QR visibility event
                                    Dispatcher.Invoke(() =>
                                    {
                                        OnQRCodeVisibilityChanged(_sessionService.CurrentSessionId, qrResult.IsVisible);
                                    });
                                    
                                    // If URL is available, stop checking
                                    if (qrResult.IsVisible)
                                    {
                                        Log.Debug("QR code is ready, stopping checks");
                                        break;
                                    }
                                }
                                
                                // Get queue status to update SMS indicator
                                var queueStatus = queueService.GetQueueStatus();
                                Dispatcher.Invoke(() =>
                                {
                                    OnQueueStatusChanged(queueStatus);
                                });
                                
                                Log.Debug("Post-session status checks completed");
                            });
                        }
                    }
                    
                    // Gallery button removed - sessions can be accessed through main gallery
                    
                    // Start auto-clear timer through service if enabled
                    if (Properties.Settings.Default.AutoClearSession)
                    {
                        _sessionService.StartAutoClearTimer();
                    }
                    
                    // Show gallery preview after session completes so users can browse
                    UpdateGalleryPreviewVisibility(false);
                    Log.Debug("Showing gallery preview after session completion");
                    
                    // Hide cancel session button - session is complete
                    if (cancelSessionButton != null)
                    {
                        cancelSessionButton.Visibility = Visibility.Collapsed;
                        Log.Debug("Cancel session button hidden - session complete");
                    }
                    
                    // Handle auto-upload - always enabled
                    // Cancel any existing upload task
                    _autoUploadCancellationTokenSource?.Cancel();
                    _autoUploadCancellationTokenSource = new CancellationTokenSource();
                    var cancellationToken = _autoUploadCancellationTokenSource.Token;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Longer delay for video sessions to ensure file is fully downloaded and released
                            int delay = !string.IsNullOrEmpty(e.CompletedSession.VideoPath) ? 5000 : 1000;
                            await Task.Delay(delay, cancellationToken); // Wait for video to be fully available

                            // Check if cancelled before proceeding
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                await AutoUploadSessionPhotos(e.CompletedSession);
                            }
                            else
                            {
                                Log.Debug("Auto-upload cancelled - session was cleared");
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            Log.Debug("Auto-upload task was cancelled");
                        }
                    }, cancellationToken);
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
                Log.Debug($"PhotoCaptureService retake state: IsRetaking={_photoCaptureService.IsRetakingPhoto}, Index={_photoCaptureService.PhotoIndexToRetake}");
                
                // Check if this is a retake capture
                if (_photoCaptureService.IsRetakingPhoto && _photoCaptureService.PhotoIndexToRetake >= 0)
                {
                    Log.Debug($"This is a retake capture for photo index {_photoCaptureService.PhotoIndexToRetake}");
                    OnRetakePhotoCaptured(e.PhotoPath);
                }
                else
                {
                    _uiService.UpdateStatus("Processing photo...");
                }
            });
        }
        private void OnServicePhotoDisplayRequested(object sender, Services.PhotoDisplayEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (FileValidationService.Instance.ValidateFilePath(e.PhotoPath))
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

                        // Clear template overlay when displaying captured photo
                        if (templateOverlayCanvas != null)
                        {
                            templateOverlayCanvas.Children.Clear();
                            Log.Debug("Cleared template overlay for photo display");
                        }

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
                        bool pathValid = FileValidationService.Instance.ValidateFilePathWithLogging(e.PhotoPath, "CaptureCompleted");
                        Log.Error($"Cannot display photo - Path: {e.PhotoPath}, Valid: {pathValid}");
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
                    if (!FileValidationService.Instance.ValidateFilePath(e.ImagePath))
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
                        // Reset rotation when displaying composed template or session result
                        // The composed template should not be rotated, only the live camera feed
                        if (_isDisplayingSessionResult || e.ImagePath.Contains("composed") || e.ImagePath.Contains("template"))
                        {
                            if (liveViewRotateTransform != null)
                            {
                                liveViewRotateTransform.Angle = 0;
                                Log.Debug("Reset rotation for composed template display");
                            }
                        }

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
                    if (!FileValidationService.Instance.ValidateFilePath(e.GifPath))
                    {
                        Log.Error($"Invalid file path for display: {e.GifPath}");
                        return;
                    }

                    string fileExtension = FileValidationService.Instance.GetFileExtension(e.GifPath);
                    bool isMP4 = fileExtension == "mp4";
                    
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
                if (!FileValidationService.Instance.ValidateFilePath(videoPath))
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
                if (FileValidationService.Instance.ValidateFilePath(imagePath))
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
                if (!FileValidationService.Instance.ValidateFilePath(gifPath))
                {
                    Log.Error($"DisplayGifInLiveView: Invalid path or file not found: {gifPath}");
                    return;
                }
                
                bool isMP4 = FileValidationService.Instance.IsVideoFile(gifPath);
                
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
                if (!FileValidationService.Instance.ValidateFilePath(composedPath))
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
                
                if (!FileValidationService.Instance.ValidateFilePath(gifPath))
                {
                    Log.Error($"AddGifThumbnailForGallery: Invalid path or file not found: {gifPath}");
                    return;
                }
                
                // Determine if it's a GIF or MP4
                bool isMP4 = FileValidationService.Instance.IsVideoFile(gifPath);
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
                
                if (isMP4 && FileValidationService.Instance.ValidateFilePath(thumbnailPhotoPath))
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
                
                if (!FileValidationService.Instance.ValidateFilePath(gifPath))
                {
                    Log.Error($"AddGifThumbnail: Invalid path or file not found: {gifPath}");
                    return;
                }
                
                // Determine if it's a GIF or MP4
                bool isMP4 = FileValidationService.Instance.IsVideoFile(gifPath);
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
                        if (FileValidationService.Instance.ValidateFilePath(firstPhotoPath))
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

                // Cancel any pending auto-upload
                _autoUploadCancellationTokenSource?.Cancel();
                _autoUploadCancellationTokenSource = null;

                // Clean up any video elements first (MP4 playback)
                CleanupVideoElements();
                Log.Debug("Session cleared: Cleaned up video elements and cancelled uploads");

                // Clear display flags
                SetDisplayingSessionResult(false);
                _isDisplayingCapturedPhoto = false;
                Log.Debug("Display flags cleared - live view can resume");

                // Reset the session cleared flag for next session
                _isSessionBeingCleared = false;
                
                // Check if we need to resume video mode after session
                var videoModeService = Services.VideoModeLiveViewService.Instance;
                if (videoModeService.IsEnabled && !videoModeService.IsVideoModeActive)
                {
                    Log.Debug("Session cleared - resuming video mode that was active before photo session");
                    _ = Task.Run(async () => 
                    {
                        await Task.Delay(500); // Brief delay to let UI settle
                        await videoModeService.ResumeVideoModeAfterCapture();
                    });
                }
                
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
                // Home and gallery buttons removed from sharing overlay
                
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
                        // Only try to stop live view if camera is connected
                        if (DeviceManager.SelectedCameraDevice?.IsConnected == true)
                        {
                            try
                            {
                                DeviceManager.SelectedCameraDevice.StopLiveView();
                            }
                            catch (Exception ex)
                            {
                                Log.Debug($"Failed to stop live view after session clear: {ex.Message}");
                            }
                        }
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
                
                // Hide cancel button if visible
                if (cancelSessionButton != null)
                {
                    cancelSessionButton.Visibility = Visibility.Collapsed;
                }
                
                Log.Debug("Session fully cleared - ready for new session");
            });
        }
        
        /// <summary>
        /// Hide all overlays to reset UI
        /// </summary>
        private void HideAllOverlays()
        {
            try
            {
                // Hide selection overlays
                if (EventSelectionOverlayControl != null)
                    EventSelectionOverlayControl.Visibility = Visibility.Collapsed;
                if (templateSelectionOverlay != null)
                    templateSelectionOverlay.Visibility = Visibility.Collapsed;
                if (templateSelectionOverlayNew != null)
                    templateSelectionOverlayNew.Visibility = Visibility.Collapsed;
                if (filterSelectionOverlay != null)
                    filterSelectionOverlay.Visibility = Visibility.Collapsed;
                if (retakeSelectionOverlay != null)
                    retakeSelectionOverlay.Visibility = Visibility.Collapsed;

                // Hide control overlays
                if (CameraSettingsOverlayControl != null)
                    CameraSettingsOverlayControl.Visibility = Visibility.Collapsed;
                if (TimerSettingsOverlayControl != null)
                    TimerSettingsOverlayControl.Visibility = Visibility.Collapsed;
                if (PrintSettingsOverlayControl != null)
                    PrintSettingsOverlayControl.Visibility = Visibility.Collapsed;
                if (SettingsOverlayControl != null)
                    SettingsOverlayControl.Visibility = Visibility.Collapsed;
                if (SharingStatusOverlayControl != null)
                    SharingStatusOverlayControl.Visibility = Visibility.Collapsed;
                if (TemplateDesignerOverlayControl != null)
                    TemplateDesignerOverlayControl.Visibility = Visibility.Collapsed;
                if (PinEntryOverlayControl != null)
                    PinEntryOverlayControl.Visibility = Visibility.Collapsed;
                if (galleryOverlay != null)
                    galleryOverlay.Visibility = Visibility.Collapsed;
                if (CaptureModesOverlay != null)
                    CaptureModesOverlay.Visibility = Visibility.Collapsed;

                // Hide countdown overlay
                if (countdownOverlay != null)
                    countdownOverlay.Visibility = Visibility.Collapsed;

                // Hide template overlay
                if (templateOverlayContainer != null)
                    templateOverlayContainer.Visibility = Visibility.Collapsed;

                Log.Debug("All overlays hidden");
            }
            catch (Exception ex)
            {
                Log.Error($"Error hiding overlays: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle auto-clear timer expired event from service
        /// </summary>
        private void OnServiceAutoClearTimerExpired(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug("Auto-clear timer expired - FORCE clearing session and resetting UI");

                // Cancel any pending auto-upload
                _autoUploadCancellationTokenSource?.Cancel();
                _autoUploadCancellationTokenSource = null;
                Log.Debug("Cancelled auto-upload task due to timeout");

                // Set flag to prevent QR code from showing
                _isSessionBeingCleared = true;

                // Force clear all display states immediately
                SetDisplayingSessionResult(false);
                _isDisplayingCapturedPhoto = false;
                _isProcessingRetakes = false;
                _isRetaking = false;

                // Exit gallery mode if we're in it
                if (_isInGalleryMode)
                {
                    _isInGalleryMode = false;
                    _currentGallerySession = null;
                    _currentGallerySessionIndex = 0;
                    _gallerySessions = null;
                    Log.Debug("Force exited gallery mode due to auto-clear");
                }

                // Stop all timers
                _galleryTimer?.Stop();
                _countdownTimer?.Stop();

                // Hide ALL overlays and panels
                if (actionButtonsPanel != null)
                {
                    actionButtonsPanel.Visibility = Visibility.Collapsed;
                }

                // Note: galleryActionPanel and photosPanel don't exist as separate controls
                // The photos are in photosContainer which gets cleared below

                if (cancelSessionButton != null)
                {
                    cancelSessionButton.Visibility = Visibility.Collapsed;
                }

                // Clear the live view image completely
                if (liveViewImage != null)
                {
                    liveViewImage.Source = null;
                    Log.Debug("Cleared live view image");
                }

                // Clean up any video elements (MP4 playback)
                CleanupVideoElements();
                Log.Debug("Cleaned up video elements");

                // Make sure live view image is visible again
                if (liveViewImage != null)
                {
                    liveViewImage.Visibility = Visibility.Visible;
                }

                // Clear photo strip
                if (photosContainer != null)
                {
                    photosContainer.Children.Clear();
                    Log.Debug("Cleared photo strip");
                }

                // Hide any visible overlays
                HideAllOverlays();

                // Force clear the session immediately
                try
                {
                    Log.Debug("Force calling ClearSession due to timeout");
                    _sessionService?.ClearSession();
                }
                catch (Exception ex)
                {
                    Log.Error($"Error force clearing session: {ex.Message}");
                }

                // Show the start button
                if (startButtonOverlay != null)
                {
                    startButtonOverlay.Visibility = Visibility.Visible;
                    Log.Debug("Showed start button");
                }

                // Resume live view
                if (_liveViewTimer != null && !_liveViewTimer.IsEnabled)
                {
                    Log.Debug("Resuming live view after force clear");
                    _liveViewTimer.Start();
                }

                // Reset UI to initial state
                _uiService?.UpdateStatus("Touch to start");

                Log.Debug("Force session clear completed - UI reset to initial state");
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
                    if (FileValidationService.Instance.ValidateFilePath(e.AnimationPath))
                    {
                        string fileType = FileValidationService.Instance.IsVideoFile(e.AnimationPath) ? "MP4" : "GIF";
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

                // Load template for overlay if enabled
                if (_showTemplateOverlay && _currentTemplate != null)
                {
                    LoadTemplateForOverlay(_currentTemplate);
                }
                
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
        
        #region Filter Selection Event Handlers
        /// <summary>
        /// Handle filter selected event - apply filter and proceed with composition
        /// </summary>
        private async void OnServiceFilterSelected(object sender, Services.FilterSelectedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Log.Debug($"Filter selected: {e.SelectedFilter}");
                
                // Apply the selected filter to photos
                if (e.SelectedFilter != FilterType.None)
                {
                    _sessionService.SetSelectedFilter(e.SelectedFilter);
                    await _sessionService.ApplyFilterToPhotosAsync();
                }
                
                // Proceed with composition after filter is applied
                await ProceedWithComposition();
            });
        }
        
        /// <summary>
        /// Handle filter selection cancelled - proceed without filter
        /// </summary>
        private async void OnServiceFilterSelectionCancelled(object sender, EventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Log.Debug("Filter selection cancelled - proceeding without filter");
                await ProceedWithComposition();
            });
        }
        
        /// <summary>
        /// Show filter selection overlay
        /// </summary>
        private async void OnServiceShowFilterSelectionRequested(object sender, EventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                // Populate filter options
                var filters = _filterSelectionService.GetEnabledFilters();
                filterItemsControl.ItemsSource = filters;
                
                // Show the overlay
                filterSelectionOverlay.Visibility = Visibility.Visible;
                
                // Generate preview thumbnails using the last captured photo
                if (_sessionService?.CapturedPhotoPaths?.Count > 0)
                {
                    string lastPhoto = _sessionService.CapturedPhotoPaths.Last();
                    await _filterSelectionService.GenerateFilterPreviewsAsync(lastPhoto);
                }
            });
        }
        
        /// <summary>
        /// Hide filter selection overlay
        /// </summary>
        private void OnServiceHideFilterSelectionRequested(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                filterSelectionOverlay.Visibility = Visibility.Collapsed;
            });
        }
        
        /// <summary>
        /// Handle filter button click - UI event routing only
        /// </summary>
        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is FilterType filter)
            {
                _filterSelectionService.SelectFilter(filter);
            }
        }
        
        /// <summary>
        /// Handle close filter selection button - UI event routing only
        /// </summary>
        private void CloseFilterSelection_Click(object sender, RoutedEventArgs e)
        {
            _filterSelectionService.CancelFilterSelection();
        }
        
        /// <summary>
        /// Handle apply no filter button - UI event routing only
        /// </summary>
        private void ApplyNoFilter_Click(object sender, RoutedEventArgs e)
        {
            _filterSelectionService.ApplyNoFilter();
        }
        #endregion
        
        #region Retake Event Handlers
        /// <summary>
        /// Handle retake selected event - photos have been retaken or skipped
        /// </summary>
        private async void OnServiceRetakeSelected(object sender, Services.RetakeSelectedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (e.RetakeCompleted)
                {
                    Log.Debug($"OnServiceRetakeSelected: Retake process completed with {e.PhotoPaths?.Count ?? 0} photos");
                    
                    // Ensure retake flag is cleared
                    _isProcessingRetakes = false;
                    
                    // Reset any lingering retake state now that retakes are complete
                    if (_photoCaptureService.IsRetakingPhoto)
                    {
                        _photoCaptureService.ResetRetakeState();
                        Log.Debug("OnServiceRetakeSelected: Reset lingering retake state after completion");
                    }
                    
                    // Update the session with the new photo paths if any were retaken
                    // The service already updated the paths internally
                    
                    // IMPORTANT: The session needs to know we're ready for completion
                    // Since we bypassed the normal photo capture flow, we need to ensure
                    // the session state is correct
                    Log.Debug($"Session state: CurrentPhotoIndex={_sessionService.CurrentPhotoIndex}, TotalRequired={_sessionService.TotalPhotosRequired}");
                    
                    // Now proceed to filters and composition
                    Log.Debug("Proceeding to filter check after retakes");
                    await CheckAndApplyFilters();
                }
            });
        }
        
        /// <summary>
        /// Handle retake requested event - specific photos need to be retaken
        /// </summary>
        private async void OnServiceRetakeRequested(object sender, Services.RetakeRequestedEventArgs e)
        {
            // This event is now deprecated - the service handles the workflow internally
            Log.Debug($"RetakeRequested event (deprecated) - photos: {string.Join(", ", e.PhotoIndices)}");
        }
        
        /// <summary>
        /// Handle when a single retake photo is required
        /// </summary>
        private async void OnServiceRetakePhotoRequired(object sender, Services.RetakePhotoEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                Log.Debug($"OnServiceRetakePhotoRequired: Retaking photo {e.PhotoNumber} (index {e.PhotoIndex})");
                
                // Set flag to indicate we're processing retakes
                _isProcessingRetakes = true;
                _currentRetakeIndex = e.PhotoIndex; // Store the current retake index as fallback
                
                // Update UI status
                _uiService.UpdateStatus($"Retaking photo {e.PhotoNumber}...");
                
                // Set up photo capture service for retake
                _photoCaptureService.StartRetake(e.PhotoIndex);
                Log.Debug($"PhotoCaptureService configured for retake: IsRetaking={_photoCaptureService.IsRetakingPhoto}, Index={_photoCaptureService.PhotoIndexToRetake}");
                Log.Debug($"Stored fallback retake index: {_currentRetakeIndex}");
                
                // Start the workflow with countdown
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            });
        }
        
        /// <summary>
        /// Handle when all retakes are completed
        /// </summary>
        private void OnServiceRetakeProcessCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug("OnServiceRetakeProcessCompleted: All retakes completed, clearing retake flags");
                _isProcessingRetakes = false;
                _currentRetakeIndex = -1;
                // The service will fire RetakeSelected event next to continue the workflow
            });
        }
        
        /// <summary>
        /// Handle when a retake photo has been captured
        /// </summary>
        private void OnRetakePhotoCaptured(string photoPath)
        {
            int photoIndex = -1;
            
            // Try to get index from PhotoCaptureService
            if (_photoCaptureService.IsRetakingPhoto && _photoCaptureService.PhotoIndexToRetake >= 0)
            {
                photoIndex = _photoCaptureService.PhotoIndexToRetake;
                Log.Debug($"OnRetakePhotoCaptured: Using PhotoCaptureService index: {photoIndex}");
            }
            // Fallback to stored index if we're processing retakes
            else if (_isProcessingRetakes && _currentRetakeIndex >= 0)
            {
                photoIndex = _currentRetakeIndex;
                Log.Debug($"OnRetakePhotoCaptured: WARNING - PhotoCaptureService lost retake state!");
                Log.Debug($"OnRetakePhotoCaptured: Using fallback retake index: {photoIndex}");
            }
            
            if (photoIndex >= 0)
            {
                Log.Debug($"OnRetakePhotoCaptured: Processing retake for photo {photoIndex + 1}, path: {photoPath}");
                
                // NOTE: We do NOT call ReplacePhotoAtIndex here anymore!
                // The SessionService.ProcessCapturedPhotoAsync will handle the replacement automatically
                // since it now checks if PhotoCaptureService.IsRetakingPhoto is true
                
                // Clear our tracking
                _currentRetakeIndex = -1;
                
                // Notify retake service that capture is complete 
                // This will trigger ProcessNextRetake() internally for the next retake in queue
                // Note: We pass the raw photo path here, the RetakeSelectionService will get the processed path later
                _retakeSelectionService.OnRetakePhotoCaptured(photoIndex, photoPath);
                
                // DON'T reset the retake state here - SessionService needs to use it when ProcessCapturedPhotoAsync is called
                // SessionService will reset it after processing the retake
            }
            else
            {
                Log.Error($"OnRetakePhotoCaptured: No valid photo index for retake!");
                Log.Error($"  - PhotoCaptureService.IsRetaking: {_photoCaptureService.IsRetakingPhoto}");
                Log.Error($"  - PhotoCaptureService.Index: {_photoCaptureService.PhotoIndexToRetake}");
                Log.Error($"  - _isProcessingRetakes: {_isProcessingRetakes}");
                Log.Error($"  - _currentRetakeIndex: {_currentRetakeIndex}");
            }
        }
        
        /// <summary>
        /// Show retake selection overlay
        /// </summary>
        private void OnServiceShowRetakeSelectionRequested(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Bind the retake photos to the UI
                retakePhotoGrid.ItemsSource = _retakeSelectionService.RetakePhotos;
                
                // Update instruction text based on settings
                if (Properties.Settings.Default.AllowMultipleRetakes)
                {
                    retakeInstructionText.Text = "Click photos to select them, then click 'Retake Selected' (multiple selection allowed)";
                }
                else
                {
                    retakeInstructionText.Text = "Click a photo to select it, then click 'Retake Selected' (single selection only)";
                }
                
                // Initially hide the retake button until photos are selected
                if (retakeSelectedButton != null)
                {
                    retakeSelectedButton.Visibility = Visibility.Collapsed;
                }
                
                // Show the overlay
                retakeSelectionOverlay.Visibility = Visibility.Visible;
            });
        }
        
        /// <summary>
        /// Hide retake selection overlay
        /// </summary>
        private void OnServiceHideRetakeSelectionRequested(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                retakeSelectionOverlay.Visibility = Visibility.Collapsed;
            });
        }
        
        /// <summary>
        /// Update retake timer display
        /// </summary>
        private void OnServiceRetakeTimerTick(object sender, int timeRemaining)
        {
            Dispatcher.Invoke(() =>
            {
                retakeTimerText.Text = timeRemaining.ToString();
            });
        }
        
        /// <summary>
        /// Handle photo click in retake selection
        /// </summary>
        private void RetakePhoto_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            Log.Debug($"RetakePhoto_Click: Border = {border}, Tag = {border?.Tag}");
            
            if (border?.Tag is int photoIndex)
            {
                Log.Debug($"RetakePhoto_Click: Toggling retake for photo {photoIndex + 1}");
                
                // Toggle the retake selection for this photo
                _retakeSelectionService.TogglePhotoRetake(photoIndex);
                
                // Log the current state
                var photo = _retakeSelectionService.RetakePhotos.FirstOrDefault(p => p.PhotoIndex == photoIndex);
                if (photo != null)
                {
                    Log.Debug($"RetakePhoto_Click: Photo {photoIndex + 1} MarkedForRetake = {photo.MarkedForRetake}");
                }
                
                // Check if any photos are selected and update button visibility
                var anySelected = _retakeSelectionService.RetakePhotos.Any(p => p.MarkedForRetake);
                if (retakeSelectedButton != null)
                {
                    retakeSelectedButton.Visibility = anySelected ? Visibility.Visible : Visibility.Collapsed;
                    
                    // Update button text to show count
                    var selectedCount = _retakeSelectionService.RetakePhotos.Count(p => p.MarkedForRetake);
                    retakeSelectedButton.Content = selectedCount > 1 
                        ? $"Retake {selectedCount} Photos" 
                        : "Retake Selected Photo";
                }
                
                // Force UI refresh if needed
                retakePhotoGrid.Items.Refresh();
            }
            else
            {
                Log.Error($"RetakePhoto_Click: Invalid tag or border - Tag type = {border?.Tag?.GetType()}");
            }
        }
        
        /// <summary>
        /// Handle retake selected button click
        /// </summary>
        private void RetakeSelected_Click(object sender, RoutedEventArgs e)
        {
            _retakeSelectionService.ProcessRetakes();
        }
        
        /// <summary>
        /// Handle skip retake button click
        /// </summary>
        private void SkipRetake_Click(object sender, RoutedEventArgs e)
        {
            _retakeSelectionService.SkipRetake();
        }
        
        /// <summary>
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
                        
                        // Start live view if idle live view is enabled OR if video recording is supported
                        var modulesConfig = PhotoboothModulesConfig.Instance;
                        bool shouldStartLiveView = Properties.Settings.Default.EnableIdleLiveView || 
                                                 modulesConfig.VideoEnabled;
                        
                        if (shouldStartLiveView)
                        {
                            device.StartLiveView();
                            _liveViewTimer.Start();
                            Log.Debug($"InitializeCamera: Started live view (IdleLiveView: {Properties.Settings.Default.EnableIdleLiveView}, VideoModule: {modulesConfig.VideoEnabled})");
                        }
                        else
                        {
                            Log.Debug("InitializeCamera: Live view disabled - both idle and video disabled");
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
                        
                        // Start live view if idle live view is enabled OR if video recording is supported
                        var modulesConfig = PhotoboothModulesConfig.Instance;
                        bool shouldStartLiveView = Properties.Settings.Default.EnableIdleLiveView || 
                                                 modulesConfig.VideoEnabled;
                        
                        if (shouldStartLiveView)
                        {
                            device.StartLiveView();
                            _liveViewTimer.Start();
                            Log.Debug($"InitializeCamera: Started live view after auto-connect (IdleLiveView: {Properties.Settings.Default.EnableIdleLiveView}, VideoModule: {modulesConfig.VideoEnabled})");
                        }
                        else
                        {
                            Log.Debug("InitializeCamera: Live view disabled after auto-connect - both idle and video disabled");
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
                // Update FPS counter
                _fpsFrameCount++;
                var now = DateTime.Now;
                var elapsed = (now - _lastFpsUpdate).TotalSeconds;
                if (elapsed >= 1.0) // Update FPS every second
                {
                    _currentFps = _fpsFrameCount / elapsed;
                    _fpsFrameCount = 0;
                    _lastFpsUpdate = now;
                    
                    // Log actual FPS periodically
                    if (_recordingFrameCounter % 300 == 0) // Every 10 seconds at 30fps
                    {
                        Log.Debug($"Live view actual FPS: {_currentFps:F1}");
                    }
                }
                
                // Check if video recording is active FIRST - always allow live view during recording
                var videoCoordinator = Services.VideoRecordingCoordinatorService.Instance;
                bool isVideoRecording = videoCoordinator.IsRecording;
                
                // Don't update live view if we're displaying a captured photo or session is complete
                // BUT always allow live view during video recording
                if (!isVideoRecording && (_isDisplayingCapturedPhoto || _isDisplayingSessionResult))
                {
                    return;
                }
                
                var device = DeviceManager?.SelectedCameraDevice;
                if (device?.IsConnected == true)
                {
                    // Track frames during recording for logging purposes
                    if (isVideoRecording)
                    {
                        _recordingFrameCounter++;
                        
                        if (_recordingFrameCounter % 180 == 0) // Log every ~6 seconds at 30fps
                        {
                            Log.Debug($"[RECORDING] LiveViewTimer_Tick: Processing live view frame during recording (FPS: {_currentFps:F1})");
                        }
                    }
                    else
                    {
                        _recordingFrameCounter = 0;
                    }
                    
                    LiveViewData liveViewData = null;
                    try
                    {
                        liveViewData = device.GetLiveViewImage();
                    }
                    catch (Exception ex)
                    {
                        if (isVideoRecording && _recordingFrameCounter % 60 == 0)
                        {
                            Log.Debug($"[RECORDING] GetLiveViewImage exception: {ex.Message}");
                        }
                    }
                    
                    // Minimal logging during recording to reduce overhead
                    if (isVideoRecording && _recordingFrameCounter % 100 == 0) // Log every ~10 seconds
                    {
                        if (liveViewData?.ImageData != null && liveViewData.ImageData.Length > 0)
                        {
                            Log.Debug($"[RECORDING] Live view active: {liveViewData.ImageData.Length} bytes");
                        }
                    }
                    
                    if (liveViewData?.ImageData != null && liveViewData.ImageData.Length > 0)
                    {
                        DisplayLiveView(liveViewData.ImageData);
                        if (isVideoRecording && _recordingFrameCounter % 100 == 0)
                        {
                            Log.Debug("[RECORDING] DisplayLiveView called successfully");
                        }
                    }
                    else if (isVideoRecording && _recordingFrameCounter % 60 == 0)
                    {
                        Log.Debug("[RECORDING] No live view data to display during video recording");
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
            try
            {
                // Check if we're recording for debug purposes
                var videoCoordinator = Services.VideoRecordingCoordinatorService.Instance;
                bool isVideoRecording = videoCoordinator.IsRecording;
                
                using (var ms = new MemoryStream(imageData))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    if (isVideoRecording && _recordingFrameCounter % 100 == 0)
                    {
                        Log.Debug($"[RECORDING] DisplayLiveView: Bitmap {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                    }
                    
                    if (liveViewImage != null)
                    {
                        liveViewImage.Source = bitmap;

                        // Update template overlay if needed (only during live view, not when displaying captured photos)
                        if (_showTemplateOverlay && !_isDisplayingCapturedPhoto && !_isDisplayingSessionResult)
                        {
                            UpdateTemplateOverlay(bitmap.PixelWidth, bitmap.PixelHeight);
                        }
                        else if (_isDisplayingCapturedPhoto || _isDisplayingSessionResult)
                        {
                            // Hide overlay when showing captured photos or session results
                            if (templateOverlayCanvas != null)
                            {
                                templateOverlayCanvas.Children.Clear();
                            }
                        }

                        if (isVideoRecording && _recordingFrameCounter % 60 == 0)
                        {
                            Log.Debug($"[RECORDING] DisplayLiveView: Set liveViewImage.Source to new bitmap - UI should be showing live camera feed");

                            // Force UI update
                            liveViewImage.InvalidateVisual();
                            Log.Debug($"[RECORDING] DisplayLiveView: Called InvalidateVisual() to force UI update");
                        }
                    }
                    else if (isVideoRecording)
                    {
                        Log.Debug($"[RECORDING] ERROR: liveViewImage is NULL - cannot display!");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DisplayLiveView error: {ex.Message}");
            }
        }

        #region Camera Rotation

        /// <summary>
        /// Apply camera rotation to live view
        /// </summary>
        private void ApplyCameraRotation()
        {
            try
            {
                if (liveViewRotateTransform == null || liveViewImage == null)
                    return;

                var rotation = Properties.Settings.Default.CameraRotation;
                liveViewRotateTransform.Angle = rotation;

                // When rotated 90 or 270 degrees, we need to adjust the image scaling
                // to ensure it fits within the container bounds
                if (rotation == 90 || rotation == 270)
                {
                    // For 90/270 degree rotations, the aspect ratio is effectively swapped
                    // We may need to adjust the Stretch property or scale
                    // Keep Uniform stretch to maintain aspect ratio and fit within bounds
                    liveViewImage.Stretch = Stretch.Uniform;
                }
                else
                {
                    // For 0/180 degree rotations, normal uniform stretch
                    liveViewImage.Stretch = Stretch.Uniform;
                }

                Log.Debug($"Applied camera rotation: {rotation}°, Stretch: {liveViewImage.Stretch}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to apply camera rotation: {ex.Message}");
            }
        }

        #endregion

        #region Template Overlay Methods

        /// <summary>
        /// Toggles the template overlay on/off for live view
        /// </summary>
        public void ToggleTemplateOverlay()
        {
            _showTemplateOverlay = !_showTemplateOverlay;

            if (templateOverlayContainer != null)
            {
                templateOverlayContainer.Visibility = _showTemplateOverlay ? Visibility.Visible : Visibility.Collapsed;

                if (_showTemplateOverlay)
                {
                    // Get current template from session service
                    var sessionTemplate = _sessionService?.CurrentTemplate;
                    if (sessionTemplate != null)
                    {
                        LoadTemplateForOverlay(sessionTemplate);
                    }
                    else if (_currentTemplate != null)
                    {
                        LoadTemplateForOverlay(_currentTemplate);
                    }
                }
                else
                {
                    // Clear overlay when hiding
                    ClearTemplateOverlay();
                }
            }
        }

        /// <summary>
        /// Loads a template for overlay display from TemplateData
        /// </summary>
        private void LoadTemplateForOverlay(TemplateData templateData)
        {
            if (_templateOverlayService == null)
            {
                Log.Debug("Template overlay service not initialized");
                return;
            }

            var loaded = _templateOverlayService.LoadTemplateForOverlay(templateData);
            if (loaded)
            {
                Log.Debug($"Template overlay loaded successfully for: {templateData?.Name}");
            }
            else
            {
                Log.Debug($"Failed to load template overlay for: {templateData?.Name}");
            }
        }


        /// <summary>
        /// Updates the template overlay based on live view dimensions
        /// </summary>
        private void UpdateTemplateOverlay(int liveViewWidth, int liveViewHeight)
        {
            if (templateOverlayCanvas == null)
            {
                Log.Debug("UpdateTemplateOverlay: templateOverlayCanvas is null");
                return;
            }

            // Always use the service-based approach
            if (_currentTemplate != null)
            {
                UpdateTemplateOverlayFromTemplateData(liveViewWidth, liveViewHeight);
            }
            else
            {
                Log.Debug("UpdateTemplateOverlay: No current template to display");
            }
        }

        /// <summary>
        /// Clears the template overlay
        /// </summary>
        private void ClearTemplateOverlay()
        {
            if (templateOverlayCanvas != null)
            {
                templateOverlayCanvas.Children.Clear();
            }

            _templateOverlayService?.Clear();
        }

        /// <summary>
        /// Updates the template overlay from database TemplateData (when no XML exists)
        /// </summary>
        private void UpdateTemplateOverlayFromTemplateData(int liveViewWidth, int liveViewHeight)
        {
            if (templateOverlayCanvas == null || _currentTemplate == null || _templateOverlayService == null)
                return;

            try
            {
                // Clear existing overlays
                templateOverlayCanvas.Children.Clear();

                // Get the actual display size of the live view image
                var displayWidth = liveViewImage.ActualWidth;
                var displayHeight = liveViewImage.ActualHeight;

                if (displayWidth <= 0 || displayHeight <= 0)
                    return;

                // Get current photo index
                var currentPhotoCount = _sessionService?.CapturedPhotoPaths?.Count ?? 0;
                _currentPhotoIndex = currentPhotoCount; // Next photo to be taken

                // Get placeholder data from service
                var placeholderData = _templateOverlayService.GetPlaceholderForPhoto(_currentPhotoIndex, _currentTemplate);

                if (placeholderData != null)
                {
                    // Set canvas size to match display
                    templateOverlayCanvas.Width = displayWidth;
                    templateOverlayCanvas.Height = displayHeight;

                    // Calculate the aspect ratio of the placeholder
                    var placeholderAspect = placeholderData.Width / placeholderData.Height;

                    double overlayWidth, overlayHeight;
                    double overlayX, overlayY;

                    // Always scale to fit the height
                    overlayHeight = displayHeight * 0.95; // Use 95% of display height
                    overlayWidth = overlayHeight * placeholderAspect;

                    // Center horizontally
                    overlayX = (displayWidth - overlayWidth) / 2;
                    overlayY = (displayHeight - overlayHeight) / 2;

                    Log.Debug($"Placeholder overlay: Display({displayWidth:F0}x{displayHeight:F0}) Placeholder({placeholderData.Width:F0}x{placeholderData.Height:F0}) Overlay({overlayWidth:F0}x{overlayHeight:F0}) at ({overlayX:F0},{overlayY:F0})");

                    // Create a darkened area outside the placeholder
                    // First, add a semi-transparent black rectangle covering the entire canvas
                    var darkenRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = displayWidth,
                        Height = displayHeight,
                        Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)) // 80% black overlay
                    };
                    Canvas.SetLeft(darkenRect, 0);
                    Canvas.SetTop(darkenRect, 0);
                    templateOverlayCanvas.Children.Add(darkenRect);

                    // Now create a clear "hole" for the placeholder area using a Path with CombinedGeometry
                    var outerRect = new RectangleGeometry(new Rect(0, 0, displayWidth, displayHeight));
                    var innerRect = new RectangleGeometry(new Rect(overlayX, overlayY, overlayWidth, overlayHeight));

                    var combinedGeometry = new CombinedGeometry(
                        GeometryCombineMode.Exclude,
                        outerRect,
                        innerRect);

                    var path = new System.Windows.Shapes.Path
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0)), // 80% black
                        Data = combinedGeometry
                    };

                    // Clear the canvas first
                    templateOverlayCanvas.Children.Clear();
                    templateOverlayCanvas.Children.Add(path);

                    // Add white border around the placeholder area
                    var placeholderBorder = new System.Windows.Shapes.Rectangle
                    {
                        Width = overlayWidth,
                        Height = overlayHeight,
                        Fill = Brushes.Transparent, // No fill, just border
                        Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), // White border
                        StrokeThickness = 2
                    };

                    Canvas.SetLeft(placeholderBorder, overlayX);
                    Canvas.SetTop(placeholderBorder, overlayY);
                    templateOverlayCanvas.Children.Add(placeholderBorder);

                    // Add a subtle label at the center of the placeholder
                    var label = new TextBlock
                    {
                        Text = $"Photo {placeholderData.PhotoNumber}",
                        Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                        FontSize = 14,
                        FontWeight = FontWeights.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    // Center the label in the placeholder
                    var labelBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 3, 6, 3),
                        Child = label
                    };

                    // Measure the label to center it properly
                    labelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var labelWidth = labelBorder.DesiredSize.Width;
                    var labelHeight = labelBorder.DesiredSize.Height;

                    Canvas.SetLeft(labelBorder, overlayX + (overlayWidth - labelWidth) / 2);
                    Canvas.SetTop(labelBorder, overlayY + (overlayHeight - labelHeight) / 2);
                    templateOverlayCanvas.Children.Add(labelBorder);
                }
                else
                {
                    Log.Debug($"No placeholder data for photo index {_currentPhotoIndex}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating template overlay from TemplateData: {ex.Message}");
            }
        }

        #endregion

        private string FindLatestCapturedPhoto()
        {
            try
            {
                // Use service to scan for recent images
                var recentFiles = FileValidationService.Instance.ScanForRecentImages(
                    FileValidationService.Instance.GetStandardPhotoFolders()
                );
                
                // Get the most recent file from last 2 minutes
                var filteredFiles = FileValidationService.Instance.FilterFilesByCreationTime(recentFiles, 2);
                var latestFile = filteredFiles.FirstOrDefault();
                    
                if (!string.IsNullOrEmpty(latestFile))
                {
                    Log.Debug($"Found latest captured photo: {latestFile}");
                    return latestFile;
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

            // First check if there's a saved event that hasn't expired
            EventSelectionService.Instance.CheckAndRestoreSavedEvent();

            // If a saved event was restored, use it
            if (EventSelectionService.Instance.SelectedEvent != null)
            {
                _currentEvent = EventSelectionService.Instance.SelectedEvent;
                PhotoboothService.CurrentEvent = _currentEvent;
                Log.Debug($"LoadInitialEventTemplate: Restored saved event '{_currentEvent.Name}' from settings");
            }
            else
            {
                // Load from PhotoboothService static properties
                _currentEvent = PhotoboothService.CurrentEvent;
            }

            _currentTemplate = PhotoboothService.CurrentTemplate;

            Log.Debug($"LoadInitialEventTemplate: CurrentEvent = {_currentEvent?.Name ?? "null"}");
            Log.Debug($"LoadInitialEventTemplate: CurrentTemplate = {_currentTemplate?.Name ?? "null"}");

            if (_currentEvent != null)
            {
                _eventTemplateService.SelectEvent(_currentEvent);
                statusText.Text = $"Event: {_currentEvent.Name}";
                
                // Save the event ID to settings for use by other services
                Properties.Settings.Default.SelectedEventId = _currentEvent.Id;
                Properties.Settings.Default.Save();
                Log.Debug($"LoadInitialEventTemplate: Saved SelectedEventId {_currentEvent.Id} to settings");
                
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

                    // Load template for overlay if enabled
                    if (_showTemplateOverlay && _currentTemplate != null)
                    {
                        LoadTemplateForOverlay(_currentTemplate);
                    }

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

                // Load template for overlay if enabled
                if (_showTemplateOverlay && _currentTemplate != null)
                {
                    LoadTemplateForOverlay(_currentTemplate);
                }

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
            Log.Debug("ShowEventSelectionOverlay: Using new EventSelectionOverlay control");
            
            // Show the modern event selection overlay
            if (EventSelectionOverlayControl != null)
            {
                EventSelectionOverlayControl.ShowOverlay();
            }
            else
            {
                Log.Error("ShowEventSelectionOverlay: EventSelectionOverlayControl is null!");
                statusText.Text = "No events available - Please configure events first";
                
                // Still show start button for basic photo capture
                startButtonOverlay.Visibility = Visibility.Visible;
            }
        }

        #region Bottom Control Bar
        private void ToggleBottomBar_Click(object sender, RoutedEventArgs e)
        {
            // Check if settings are locked
            var pinLockService = PinLockService.Instance;
            if (pinLockService.IsInterfaceLocked)
            {
                // Request PIN to access settings
                pinLockService.RequestPinForUnlock((success) =>
                {
                    if (success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Unlock and show the settings bar
                            pinLockService.IsInterfaceLocked = false;
                            UpdateLockButtonAppearance(false);
                            UpdateSettingsAccessibility(true);
                            bottomControlBar.Visibility = Visibility.Visible;
                            bottomBarToggleChevron.Text = "⚙"; // Gear icon
                            Log.Debug("Settings unlocked and bottom bar shown");
                        });
                    }
                });
                return;
            }
            
            if (bottomControlBar.Visibility == Visibility.Collapsed)
            {
                // Show bottom bar
                bottomControlBar.Visibility = Visibility.Visible;
                bottomBarToggleChevron.Text = "⚙"; // Gear icon
            }
            else
            {
                // Hide bottom bar  
                bottomControlBar.Visibility = Visibility.Collapsed;
                bottomBarToggleChevron.Text = "⚙"; // Gear icon
            }
        }

        private void BottomBarToggle_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Mouse enter handled by XAML animations
            Log.Debug("Mouse entered settings toggle");
        }

        private void BottomBarToggle_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Mouse leave handled by XAML animations
            Log.Debug("Mouse left settings toggle");
        }

        private void BottomBarToggle_TouchEnter(object sender, System.Windows.Input.TouchEventArgs e)
        {
            // Trigger same animation as mouse enter
            if (bottomBarToggle != null)
            {
                bottomBarToggle.Opacity = 1.0;
                if (toggleScale != null)
                {
                    toggleScale.ScaleX = 1.0;
                    toggleScale.ScaleY = 1.0;
                }
            }
            Log.Debug("Touch entered settings toggle");
        }

        private void BottomBarToggle_TouchLeave(object sender, System.Windows.Input.TouchEventArgs e)
        {
            // Trigger same animation as mouse leave
            if (bottomBarToggle != null)
            {
                bottomBarToggle.Opacity = 0.3;
                if (toggleScale != null)
                {
                    toggleScale.ScaleX = 0.8;
                    toggleScale.ScaleY = 0.8;
                }
            }
            Log.Debug("Touch left settings toggle");
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
            Log.Debug("CameraSettingsButton_Click: Opening camera settings overlay");
            
            // Show the camera settings overlay using the service (lazy loaded)
            var cameraSettingsService = CameraSettingsService.Instance;
            cameraSettingsService.ShowOverlay();
        }

        private void TimerSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("TimerSettingsButton_Click: Opening timer settings overlay");
            
            // Show the timer settings overlay
            if (TimerSettingsOverlayControl != null)
            {
                TimerSettingsOverlayControl.ShowOverlay();
            }
            else
            {
                Log.Error("TimerSettingsButton_Click: TimerSettingsOverlayControl is null");
            }
        }

        private void EventButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("EventButton_Click: Opening event selection overlay");
            
            // Show the event selection overlay
            if (EventSelectionOverlayControl != null)
            {
                EventSelectionOverlayControl.ShowOverlay();
            }
            else
            {
                Log.Error("EventButton_Click: EventSelectionOverlayControl is null");
            }
        }
        
        private void PrintSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("PrintSettingsButton_Click: Opening print settings overlay");
            
            // Show the print settings overlay
            if (PrintSettingsOverlayControl != null)
            {
                PrintSettingsOverlayControl.ShowOverlay();
            }
            else
            {
                Log.Error("PrintSettingsButton_Click: PrintSettingsOverlayControl is null");
            }
        }
        #endregion

        #region Event/Template Selection Handlers
        private void OnEventSelectionOverlayEventSelected(object sender, EventData selectedEvent)
        {
            if (selectedEvent != null)
            {
                Log.Debug($"OnEventSelectionOverlayEventSelected: Selected event '{selectedEvent.Name}' (ID: {selectedEvent.Id})");
                
                _currentEvent = selectedEvent;
                _eventTemplateService.SelectEvent(_currentEvent);

                // Update EventSelectionService with the selected event and start timer
                // This will also trigger the 5-hour expiration timer
                EventSelectionService.Instance.SelectedEvent = selectedEvent;
                EventSelectionService.Instance.StartTimerForCurrentEvent();

                // Save the selected event ID and timestamp to settings for use by other services
                Properties.Settings.Default.SelectedEventId = selectedEvent.Id;
                Properties.Settings.Default.EventSelectionTime = DateTime.Now;
                Properties.Settings.Default.Save();
                Log.Debug($"OnEventSelectionOverlayEventSelected: Saved SelectedEventId {selectedEvent.Id} and timestamp to settings");
                
                // Initialize template selection with the selected event
                Log.Debug($"OnEventSelectionOverlayEventSelected: Initializing template selection for event ID {selectedEvent.Id}");
                _templateSelectionService?.InitializeForEvent(selectedEvent);
            }
        }
        
        private void OnEventSelectionOverlayCancelled(object sender, EventArgs e)
        {
            Log.Debug("OnEventSelectionOverlayCancelled: Event selection cancelled");

            // Show start button for basic photo capture if no event selected
            if (_currentEvent == null)
            {
                statusText.Text = "Touch START to begin";
                startButtonOverlay.Visibility = Visibility.Visible;
            }
        }

        private void OnEventExpired(object sender, EventArgs e)
        {
            Log.Debug("OnEventExpired: Event has expired after 5 hours, returning to event selection");

            // Clear current event and template
            _currentEvent = null;
            _currentTemplate = null;

            // Clear settings
            Properties.Settings.Default.SelectedEventId = 0;
            Properties.Settings.Default.Save();

            // Clear template service state
            _eventTemplateService?.ClearSelections();
            _templateSelectionService?.ClearSelection();

            // Update UI on the main thread
            Dispatcher.Invoke(() =>
            {
                // Hide any active overlays first
                if (templateSelectionOverlay?.Visibility == Visibility.Visible)
                {
                    templateSelectionOverlay.Visibility = Visibility.Collapsed;
                }
                if (templateSelectionOverlayNew?.Visibility == Visibility.Visible)
                {
                    templateSelectionOverlayNew.Visibility = Visibility.Collapsed;
                }

                // Show event expired message briefly
                statusText.Text = "Event session expired. Please select an event to continue.";

                // Show event selection overlay after a short delay
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    ShowEventSelectionOverlay();
                };
                timer.Start();
            });
        }

        // Legacy handler - keeping for old inline overlay (to be removed)
        private void EventItem_Click(object sender, RoutedEventArgs e)
        {
            // This handler is for the old inline overlay - no longer used
            Log.Debug("EventItem_Click: Legacy handler called - should not happen with new overlay");
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

        // Legacy handler - no longer needed with new overlay
        private void CloseEventSelection_Click(object sender, RoutedEventArgs e)
        {
            // This handler is for the old inline overlay - no longer used
            Log.Debug("CloseEventSelection_Click: Legacy handler called - should not happen with new overlay");
        }

        private void BackToEventSelection_Click(object sender, RoutedEventArgs e)
        {
            templateSelectionOverlay.Visibility = Visibility.Collapsed;
            // Use the new EventSelectionOverlay control
            if (EventSelectionOverlayControl != null)
            {
                EventSelectionOverlayControl.ShowOverlay();
            }
        }
        
        #region Template Designer Overlay Methods
        
        /// <summary>
        /// Show the template designer overlay
        /// </summary>
        public void ShowTemplateDesignerOverlay(string templatePath = null)
        {
            if (TemplateDesignerOverlayControl != null)
            {
                Log.Debug($"Showing template designer overlay {(string.IsNullOrEmpty(templatePath) ? "for new template" : $"for: {templatePath}")}");
                TemplateDesignerOverlayControl.ShowOverlay(templatePath);
            }
        }
        
        /// <summary>
        /// Template Designer button click handler
        /// </summary>
        private void SharingStatusButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Sharing Status button clicked");
            
            // Hide the bottom control bar
            if (bottomControlBar != null)
            {
                bottomControlBar.Visibility = Visibility.Collapsed;
                bottomBarToggleChevron.Text = "⚙"; // Reset to gear icon
            }
            
            // Show the sharing status overlay
            if (SharingStatusOverlayControl != null)
            {
                SharingStatusOverlayControl.Show();
            }
        }
        
        private void TemplateDesignerButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Template Designer button clicked");
            
            // Hide the bottom control bar
            if (bottomControlBar != null)
            {
                bottomControlBar.Visibility = Visibility.Collapsed;
                bottomBarToggleChevron.Text = "⚙"; // Reset to gear icon
            }
            
            // Check if we have a current template to edit
            TemplateData currentTemplateData = _eventTemplateService?.CurrentTemplate;
            if (currentTemplateData == null)
            {
                currentTemplateData = _currentTemplate;
            }
            
            // For now, always open a new template designer
            // In the future, we could show a menu to choose between:
            // - Create New Template
            // - Edit Current Template
            // - Browse Templates
            ShowTemplateDesignerOverlay(null);
            
            // Optional: Show a message about the current template
            if (currentTemplateData != null)
            {
                Log.Debug($"Current template available for editing: {currentTemplateData.Name}");
            }
        }
        
        /// <summary>
        /// Example: Open template designer for editing current template
        /// Can be called from a button click or menu item
        /// </summary>
        public void EditCurrentTemplate()
        {
            // Get the current template if one is selected
            TemplateData currentTemplateData = _eventTemplateService?.SelectedTemplateForOverlay;
            
            if (currentTemplateData == null)
            {
                // Try to get from current template
                currentTemplateData = _currentTemplate;
            }
            
            // For database templates, we need to load them differently
            // For now, just pass null to create a new template
            // TODO: Implement loading from database if needed
            string templatePath = null;
            
            // Show the template designer overlay
            ShowTemplateDesignerOverlay(templatePath);
        }
        
        /// <summary>
        /// Example: Create a new template
        /// Can be called from a button click or menu item
        /// </summary>
        public void CreateNewTemplate()
        {
            // Show the template designer overlay for a new template
            ShowTemplateDesignerOverlay(null);
        }
        
        #endregion

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
            Log.Debug("StartButton_Click: Button clicked");
            StartPhotoSession();
        }

        private async void StartPhotoSession()
        {
            try
            {
                Log.Debug("=== STARTING PHOTO SESSION USING CLEAN SERVICES ===");
                
                // Check if capture modes are enabled
                var captureModesService = CaptureModesService.Instance;
                Log.Debug($"CaptureModesService - Enabled: {captureModesService.IsEnabled}, HasMultipleModes: {captureModesService.HasMultipleModes}, EnabledModes Count: {captureModesService.EnabledModes.Count}");
                
                if (captureModesService.IsEnabled && captureModesService.HasMultipleModes)
                {
                    // Show capture modes overlay for user to select
                    ShowCaptureModesOverlay();
                    return;
                }
                
                // Ensure template and event are loaded
                if (!await EnsureTemplateAndEvent())
                    return;
                
                // Start session using clean service
                bool sessionStarted = await _sessionService.StartSessionAsync(
                    _currentEvent, 
                    _currentTemplate, 
                    GetTotalPhotosNeeded());
                
                if (sessionStarted)
                {
                    // Start the workflow for both modes
                    // In photographer mode, it will wait for trigger instead of countdown
                    await _workflowService.StartPhotoCaptureWorkflowAsync();
                    
                    if (Properties.Settings.Default.PhotographerMode)
                    {
                        // Stop live view to release camera for trigger
                        try
                        {
                            Log.Debug("Stopping live view to release camera trigger");
                            DeviceManager.SelectedCameraDevice?.StopLiveView();
                            _liveViewTimer.Stop();
                            
                            // Reset IsBusy flag to allow trigger
                            if (DeviceManager.SelectedCameraDevice != null)
                            {
                                DeviceManager.SelectedCameraDevice.IsBusy = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug($"Error stopping live view for photographer mode: {ex.Message}");
                        }
                    }
                }
                else
                {
                    HandleSessionStartFailure("Failed to start session");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StartPhotoSession error: {ex.Message}");
                HandleSessionStartFailure("Session start failed - Please try again");
            }
        }
        
        private async Task<bool> EnsureTemplateAndEvent()
        {
            // Load template if missing
            if (_currentTemplate == null)
            {
                _currentTemplate = PhotoboothService.CurrentTemplate;
                
                if (_currentTemplate == null && _currentEvent != null)
                {
                    Log.Debug($"Initializing template selection for event: {_currentEvent.Name}");
                    _templateSelectionService.InitializeForEvent(_currentEvent);
                    return false; // Template selection will handle the rest
                }
                else if (_currentTemplate != null)
                {
                    _eventTemplateService.SelectTemplate(_currentTemplate);
                    Log.Debug($"Reloaded template: {_currentTemplate.Name}");
                }
            }
            
            // Load event if missing
            if (_currentEvent == null)
            {
                _currentEvent = PhotoboothService.CurrentEvent;
                if (_currentEvent != null)
                {
                    _eventTemplateService.SelectEvent(_currentEvent);
                    Log.Debug($"Reloaded event: {_currentEvent.Name}");
                }
            }
            
            return _currentTemplate != null;
        }
        
        private void HandleSessionStartFailure(string message)
        {
            Log.Error(message);
            _uiService.UpdateStatus(message);
            startButtonOverlay.Visibility = Visibility.Visible;
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
                
                // Build list of ALL files to upload (including composed, MP4/GIF, and video)
                var allFiles = new List<string>();
                
                // Add original photos
                if (completedSession.PhotoPaths != null && completedSession.PhotoPaths.Count > 0)
                {
                    allFiles.AddRange(completedSession.PhotoPaths);
                    Log.Debug($"AutoUploadSessionPhotos: Added {completedSession.PhotoPaths.Count} original photos");
                }
                
                // Add composed image if it exists
                if (FileValidationService.Instance.ValidateFilePath(completedSession.ComposedImagePath))
                {
                    allFiles.Add(completedSession.ComposedImagePath);
                    Log.Debug($"AutoUploadSessionPhotos: Added composed image: {Path.GetFileName(completedSession.ComposedImagePath)}");
                }
                
                // Add GIF/MP4 if it exists
                if (FileValidationService.Instance.ValidateFilePath(completedSession.GifPath))
                {
                    allFiles.Add(completedSession.GifPath);
                    Log.Debug($"AutoUploadSessionPhotos: Added animation: {Path.GetFileName(completedSession.GifPath)}");
                }
                
                // Add video recording if it exists (for video sessions)
                if (FileValidationService.Instance.ValidateFilePath(completedSession.VideoPath))
                {
                    allFiles.Add(completedSession.VideoPath);
                    Log.Debug($"AutoUploadSessionPhotos: Added video recording: {Path.GetFileName(completedSession.VideoPath)}");
                }
                
                Log.Debug($"AutoUploadSessionPhotos: Uploading {allFiles.Count} total files");
                System.Diagnostics.Debug.WriteLine($"AutoUploadSessionPhotos: Uploading {allFiles.Count} files for session {completedSession.SessionId}");
                
                // Only proceed if we have files to upload
                if (allFiles.Count == 0)
                {
                    Log.Debug("AutoUploadSessionPhotos: No files to upload, skipping cloud upload");
                    _uiService.UpdateStatus("Session complete");
                    return;
                }
                
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
                    
                    // Note: Video cloud URL will be saved separately when available
                    // The compressed video URL is embedded in the gallery HTML
                    
                    Log.Debug($"Gallery URL saved to database for session {completedSession.SessionId}: {uploadResult.GalleryUrl}");
                    _uiService.UpdateStatus("Upload complete!");
                    
                    // Process any pending SMS for this session now that we have a valid URL
                    try
                    {
                        // Update OfflineQueueService SMS entries with the real URL
                        var offlineQueue = OfflineQueueService.Instance;
                        offlineQueue.UpdateSMSQueueUrls(completedSession.SessionId, uploadResult.GalleryUrl);
                        
                        // Also process PhotoboothQueueService
                        var queueService = PhotoboothQueueService.Instance;
                        await queueService.ProcessPendingQueues();
                        
                        Log.Debug($"Triggered SMS queue processing after upload for session {completedSession.SessionId}");
                    }
                    catch (Exception queueEx)
                    {
                        Log.Error($"Error processing SMS queue after upload: {queueEx.Message}");
                    }
                    
                    // Automatically show QR code (we'll only get here if not cancelled)
                    if (_sharingUIService != null && uploadResult.QRCodeImage != null)
                    {
                        Log.Debug($"Auto-displaying QR code after successful upload for session {completedSession.SessionId}");

                        // Use Dispatcher to ensure UI operations happen on the main thread
                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                _sharingUIService.ShowQrCodeOverlay(uploadResult.GalleryUrl, uploadResult.QRCodeImage);
                            }
                            catch (Exception uiEx)
                            {
                                Log.Error($"Error displaying QR code: {uiEx.Message}");
                            }
                        });
                    }
                    else
                    {
                        Log.Debug($"QR auto-display skipped - SharingUIService: {_sharingUIService != null}, QRCodeImage: {uploadResult.QRCodeImage != null}");
                    }
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
                // Cancel any pending auto-upload
                _autoUploadCancellationTokenSource?.Cancel();
                _autoUploadCancellationTokenSource = null;
                Log.Debug("Done button: Cancelled auto-upload task");

                // Set flag to prevent QR code from showing
                _isSessionBeingCleared = true;

                // Clear display flags when done
                SetDisplayingSessionResult(false);
                _isDisplayingCapturedPhoto = false;

                // Clean up any video elements (MP4 playback)
                CleanupVideoElements();
                Log.Debug("Done button: Cleaned up video elements");

                // Make sure live view image is visible again
                if (liveViewImage != null)
                {
                    liveViewImage.Visibility = Visibility.Visible;
                    liveViewImage.Source = null; // Clear any stuck image
                }

                if (_isInGalleryMode)
                {
                    // Done with gallery session - use ExitGalleryMode to keep gallery browser visible
                    Log.Debug("Done with gallery session - exiting gallery mode");
                    
                    // Use ExitGalleryMode which keeps the gallery browser visible
                    ExitGalleryMode();
                    
                    // Clear the session service
                    _sessionService?.ClearSession();
                    
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
                Log.Debug("Gallery button clicked - checking event selection and lock state");
                
                // Check if an event is selected
                if (_currentEvent == null)
                {
                    Log.Debug("No event selected - gallery cannot be shown");
                    _uiService?.UpdateStatus("Please select an event first");
                    return;
                }
                
                // Check if interface is locked
                var pinLockService = PinLockService.Instance;
                if (pinLockService.IsInterfaceLocked)
                {
                    // Request PIN to access gallery
                    pinLockService.RequestPinForUnlock((success) =>
                    {
                        if (success)
                        {
                            Dispatcher.Invoke(async () =>
                            {
                                // Unlock and show gallery
                                pinLockService.IsInterfaceLocked = false;
                                UpdateLockButtonAppearance(false);
                                UpdateSettingsAccessibility(true);
                                
                                // Check again if event is selected after PIN unlock
                                if (_currentEvent == null)
                                {
                                    Log.Debug("No event selected after PIN unlock - gallery cannot be shown");
                                    _uiService?.UpdateStatus("Please select an event first");
                                    return;
                                }
                                
                                // Hide session completion UI
                                if (doneButton != null)
                                    doneButton.Visibility = Visibility.Collapsed;
                                
                                // Stop live view for gallery mode
                                _liveViewTimer?.Stop();
                                
                                // Show gallery using service, filtered by current event
                                await _galleryService?.ShowGalleryAsync(_currentEvent.Id);
                                
                                Log.Debug("Gallery unlocked and shown");
                            });
                        }
                    });
                    return;
                }
                
                // Hide session completion UI
                if (doneButton != null)
                    doneButton.Visibility = Visibility.Collapsed;
                
                // Stop live view for gallery mode
                _liveViewTimer?.Stop();
                
                // Show gallery using service, filtered by current event (we know it's not null from check above)
                await _galleryService?.ShowGalleryAsync(_currentEvent.Id);
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
            
            // Use service to analyze print paths
            var pathAnalysis = FileValidationService.Instance.ComparePrintPaths(outputPath, printPath);
            
            if (pathAnalysis.PathsDiffer)
            {
                Log.Debug($"  - PATHS DIFFER: 2x6 duplicated to 4x6");
                if (pathAnalysis.PrintImageInfo != null)
                {
                    Log.Debug($"  - Print file dimensions: {pathAnalysis.PrintImageInfo.Width}x{pathAnalysis.PrintImageInfo.Height}");
                    Log.Debug($"  - Is this 4x6 duplicate? {pathAnalysis.PrintImageInfo.Is4x6Duplicate}");
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
                PrintRequestResult result;
                
                if (_isInGalleryMode && _currentGallerySession != null)
                {
                    // Route to printing service for gallery print processing
                    result = await _printingService.ProcessGalleryPrintRequestAsync(_currentGallerySession);
                }
                else if (_sessionService.IsSessionActive)
                {
                    // Route to printing service for session print processing
                    result = await _printingService.ProcessSessionPrintRequestAsync(_sessionService);
                }
                else
                {
                    result = new PrintRequestResult { Success = false, Message = "No active session or gallery" };
                }

                // Handle the result from printing service
                if (result.Success && result.ShowModal)
                {
                    // Show appropriate modal based on context
                    if (_isInGalleryMode)
                    {
                        ShowPrintCopiesModalForGalleryPrint(result.ImagePath, result.SessionId, result.Is2x6Template);
                    }
                    else
                    {
                        ShowPrintCopiesModalForMainPrint(result.ImagePath, result.SessionId, result.Is2x6Template);
                    }
                }
                
                // Update UI with result message
                _uiService.UpdateStatus(result.Message);
            }
            catch (Exception ex)
            {
                Log.Error($"Print error: {ex.Message}");
                _uiService.UpdateStatus("Print failed");
            }
        }

        private void ShowPrintCopiesModalForMainPrint(string imagePath, string sessionId, bool is2x6Template)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 ShowPrintCopiesModalForMainPrint CALLED");
                System.Diagnostics.Debug.WriteLine($"🔍 Image path: {imagePath}");
                System.Diagnostics.Debug.WriteLine($"🔍 Session ID: {sessionId}");
                System.Diagnostics.Debug.WriteLine($"🔍 Is 2x6 Template: {is2x6Template}");
                
                // Stop auto-clear timer when showing print modal
                _sessionService?.StopAutoClearTimer();
                Log.Debug("Stopped auto-clear timer for print modal");

                // Store print parameters for use when user selects copies
                _pendingPrintPath = imagePath;
                _pendingPrintSessionId = sessionId;
                _pendingPrintIs2x6Template = is2x6Template;

                // Subscribe to modal events
                printCopiesModal.CopiesSelected += OnMainPrintCopiesSelected;
                printCopiesModal.SelectionCancelled += OnMainPrintSelectionCancelled;

                System.Diagnostics.Debug.WriteLine($"🔍 Modal visibility before Show(): {printCopiesModal.Visibility}");
                printCopiesModal.Show();
                System.Diagnostics.Debug.WriteLine($"🔍 Modal visibility after Show(): {printCopiesModal.Visibility}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error showing print copies modal: {ex.Message}");
                _uiService?.UpdateStatus("Failed to show print options");
            }
        }

        private async void OnMainPrintCopiesSelected(object sender, int copies)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"OnMainPrintCopiesSelected: User selected {copies} copies");
                
                // Resume auto-clear timer after print modal closed
                if (_sessionService?.IsSessionActive == true)
                {
                    _sessionService.StartAutoClearTimer();
                    Log.Debug("Resumed auto-clear timer after print modal closed");
                }
                
                // Unsubscribe from events
                printCopiesModal.CopiesSelected -= OnMainPrintCopiesSelected;
                printCopiesModal.SelectionCancelled -= OnMainPrintSelectionCancelled;

                if (!string.IsNullOrEmpty(_pendingPrintPath))
                {
                    // Print the specified number of copies
                    for (int i = 0; i < copies; i++)
                    {
                        bool printSuccess = await _printingService.PrintImageAsync(
                            _pendingPrintPath,
                            _pendingPrintSessionId,
                            _pendingPrintIs2x6Template
                        );

                        if (!printSuccess)
                        {
                            Log.Error($"Failed to print copy {i + 1} of {copies}");
                            break;
                        }
                    }

                    _uiService?.UpdateStatus($"{copies} copies sent to printer!");
                    System.Diagnostics.Debug.WriteLine($"Print completed: {copies} copies of {_pendingPrintPath}");
                }

                // Clear pending print parameters
                _pendingPrintPath = null;
                _pendingPrintSessionId = null;
                _pendingPrintIs2x6Template = false;
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling print copies selection: {ex.Message}");
                _uiService?.UpdateStatus("Print failed");
            }
        }

        private void OnMainPrintSelectionCancelled(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OnMainPrintSelectionCancelled: User cancelled print");
                
                // Resume auto-clear timer after print modal closed
                if (_sessionService?.IsSessionActive == true)
                {
                    _sessionService.StartAutoClearTimer();
                    Log.Debug("Resumed auto-clear timer after print modal cancelled");
                }
                
                // Unsubscribe from events
                printCopiesModal.CopiesSelected -= OnMainPrintCopiesSelected;
                printCopiesModal.SelectionCancelled -= OnMainPrintSelectionCancelled;

                // Clear pending print parameters
                _pendingPrintPath = null;
                _pendingPrintSessionId = null;
                _pendingPrintIs2x6Template = false;

                _uiService?.UpdateStatus("Print cancelled");
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling print selection cancellation: {ex.Message}");
            }
        }

        private void ShowPrintCopiesModalForGalleryPrint(string imagePath, string sessionId, bool is2x6Template)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 ShowPrintCopiesModalForGalleryPrint CALLED");
                System.Diagnostics.Debug.WriteLine($"🔍 Image path: {imagePath}");
                System.Diagnostics.Debug.WriteLine($"🔍 Session ID: {sessionId}");
                System.Diagnostics.Debug.WriteLine($"🔍 Is 2x6 Template: {is2x6Template}");
                
                // Stop gallery timer when showing print modal
                StopGalleryTimer();
                Log.Debug("Stopped gallery timer for print modal");

                // Store print parameters for use when user selects copies
                _pendingPrintPath = imagePath;
                _pendingPrintSessionId = sessionId;
                _pendingPrintIs2x6Template = is2x6Template;

                // Subscribe to modal events (reuse the same event handlers)
                printCopiesModal.CopiesSelected += OnGalleryPrintCopiesSelected;
                printCopiesModal.SelectionCancelled += OnGalleryPrintSelectionCancelled;

                System.Diagnostics.Debug.WriteLine($"🔍 Gallery Modal visibility before Show(): {printCopiesModal.Visibility}");
                printCopiesModal.Show();
                System.Diagnostics.Debug.WriteLine($"🔍 Gallery Modal visibility after Show(): {printCopiesModal.Visibility}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error showing gallery print copies modal: {ex.Message}");
                _uiService?.UpdateStatus("Failed to show print options");
            }
        }

        private async void OnGalleryPrintCopiesSelected(object sender, int copies)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"OnGalleryPrintCopiesSelected: User selected {copies} copies");
                
                // Resume gallery timer after print modal closed
                if (_isInGalleryMode)
                {
                    StartGalleryTimer();
                    Log.Debug("Resumed gallery timer after print modal closed");
                }
                
                // Unsubscribe from events
                printCopiesModal.CopiesSelected -= OnGalleryPrintCopiesSelected;
                printCopiesModal.SelectionCancelled -= OnGalleryPrintSelectionCancelled;

                if (!string.IsNullOrEmpty(_pendingPrintPath))
                {
                    // Print the specified number of copies
                    for (int i = 0; i < copies; i++)
                    {
                        bool printSuccess = await _printingService.PrintImageAsync(
                            _pendingPrintPath,
                            _pendingPrintSessionId,
                            _pendingPrintIs2x6Template
                        );

                        if (!printSuccess)
                        {
                            Log.Error($"Failed to print gallery copy {i + 1} of {copies}");
                            break;
                        }
                    }

                    _uiService?.UpdateStatus($"{copies} copies sent to printer!");
                    System.Diagnostics.Debug.WriteLine($"Gallery Print completed: {copies} copies of {_pendingPrintPath}");
                }

                // Clear pending print parameters
                _pendingPrintPath = null;
                _pendingPrintSessionId = null;
                _pendingPrintIs2x6Template = false;
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling gallery print copies selection: {ex.Message}");
                _uiService?.UpdateStatus("Print failed");
            }
        }

        private void OnGalleryPrintSelectionCancelled(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OnGalleryPrintSelectionCancelled: User cancelled gallery print");
                
                // Resume gallery timer after print modal cancelled
                if (_isInGalleryMode)
                {
                    StartGalleryTimer();
                    Log.Debug("Resumed gallery timer after print modal cancelled");
                }
                
                // Unsubscribe from events
                printCopiesModal.CopiesSelected -= OnGalleryPrintCopiesSelected;
                printCopiesModal.SelectionCancelled -= OnGalleryPrintSelectionCancelled;

                // Clear pending print parameters
                _pendingPrintPath = null;
                _pendingPrintSessionId = null;
                _pendingPrintIs2x6Template = false;

                _uiService?.UpdateStatus("Print cancelled");
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling gallery print selection cancellation: {ex.Message}");
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
                
            // Use InfoPanelService for camera status updates (clean architecture)
            _infoPanelService?.UpdateCameraStatus(status);
                
            // Update sync status based on camera connection
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
                
                // First check if the file exists
                if (!File.Exists(imagePath))
                {
                    Log.Error($"AddPhotoThumbnail: File does not exist: {imagePath}");
                    return;
                }
                
                if (photosContainer == null)
                {
                    Log.Error("AddPhotoThumbnail: photosContainer is null!");
                    return;
                }
                
                Log.Debug($"AddPhotoThumbnail: File exists, creating thumbnail. Container has {photosContainer.Children.Count} children before adding");
                
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
                
                Log.Debug($"★★★ AddPhotoThumbnail: Successfully added ORIGINAL photo thumbnail. Container now has {photosContainer.Children.Count} children");
                
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
                if (FileValidationService.Instance.ValidatePhotoFile(photo))
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
        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if we're recording video
            var videoCoordinator = Services.VideoRecordingCoordinatorService.Instance;
            if (videoCoordinator.IsRecording)
            {
                await StopVideoRecording();
            }
            else
            {
                CancelCurrentPhoto();
            }
        }
        
        private void CancelSessionButton_Click(object sender, RoutedEventArgs e)
        {
            CancelEntireSession();
        }
        
        /// <summary>
        /// Cancel the entire session and return to ready state
        /// </summary>
        private async void CancelEntireSession()
        {
            try
            {
                Log.Debug("=== CANCELING ENTIRE SESSION ===");

                // Cancel any pending auto-upload
                _autoUploadCancellationTokenSource?.Cancel();
                _autoUploadCancellationTokenSource = null;
                Log.Debug("Cancel session: Cancelled auto-upload task");

                // Set flag to prevent QR code from showing
                _isSessionBeingCleared = true;

                // Stop any active captures
                _workflowService?.CancelCurrentPhotoCapture();
                
                // Clear the session
                _sessionService?.ClearSession();
                
                // Hide cancel button
                if (cancelSessionButton != null)
                    cancelSessionButton.Visibility = Visibility.Collapsed;
                
                // Show start button
                _uiService?.ShowStartButton();
                
                // Update status
                _uiService?.UpdateStatus("Session cancelled");
                
                // Show gallery preview again
                UpdateGalleryPreviewVisibility(false);
                
                Log.Debug("Session cancelled - ready for new session");
            }
            catch (Exception ex)
            {
                Log.Error($"Error canceling session: {ex.Message}");
            }
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
                Log.Debug("BackButton_Click: Starting navigation back to Surface Launcher");

                // Get the current parent window
                var parentWindow = Window.GetWindow(this);

                // Check if we're in SurfacePhotoBoothWindow (original navigation)
                if (parentWindow is SurfacePhotoBoothWindow surfaceWindow)
                {
                    Log.Debug("BackButton_Click: In SurfacePhotoBoothWindow, calling NavigateBack");
                    surfaceWindow.NavigateBack();
                }
                // If we're in ModernPhotoboothWindow or any other window, open SurfacePhotoBoothWindow
                else if (parentWindow is ModernPhotoboothWindow || parentWindow != null)
                {
                    Log.Debug($"BackButton_Click: In {parentWindow?.GetType().Name}, opening SurfacePhotoBoothWindow");

                    // Create and show the Surface window
                    var surfacePhotoBoothWindow = new SurfacePhotoBoothWindow();

                    // Set window state to match current window
                    if (parentWindow.WindowState == WindowState.Maximized)
                    {
                        surfacePhotoBoothWindow.WindowState = WindowState.Maximized;
                    }
                    else
                    {
                        surfacePhotoBoothWindow.Width = parentWindow.Width;
                        surfacePhotoBoothWindow.Height = parentWindow.Height;
                        surfacePhotoBoothWindow.Left = parentWindow.Left;
                        surfacePhotoBoothWindow.Top = parentWindow.Top;
                    }

                    surfacePhotoBoothWindow.Show();

                    // Make it the main window if needed
                    if (Application.Current.MainWindow == parentWindow)
                    {
                        Application.Current.MainWindow = surfacePhotoBoothWindow;
                    }

                    // Close the modern window after showing Surface window
                    parentWindow.Close();

                    Log.Debug("BackButton_Click: Successfully opened SurfacePhotoBoothWindow and closed previous window");
                }
                else if (NavigationService != null && NavigationService.CanGoBack)
                {
                    Log.Debug("BackButton_Click: Using NavigationService.GoBack");
                    NavigationService.GoBack();
                }
                else
                {
                    // No navigation available - try opening Surface window anyway
                    Log.Debug("BackButton_Click: No parent window found, attempting to open SurfacePhotoBoothWindow directly");

                    var surfacePhotoBoothWindow = new SurfacePhotoBoothWindow();
                    surfacePhotoBoothWindow.WindowState = WindowState.Maximized;
                    surfacePhotoBoothWindow.Show();

                    // If there's a current main window, close it
                    if (Application.Current?.MainWindow != null && Application.Current.MainWindow != surfacePhotoBoothWindow)
                    {
                        Application.Current.MainWindow.Close();
                    }

                    Application.Current.MainWindow = surfacePhotoBoothWindow;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"BackButton navigation error: {ex.Message}");
                MessageBox.Show($"Error navigating back: {ex.Message}", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);

                // Last resort - try to open Surface window
                try
                {
                    var surfacePhotoBoothWindow = new SurfacePhotoBoothWindow();
                    surfacePhotoBoothWindow.Show();

                    var parentWindow = Window.GetWindow(this);
                    parentWindow?.Close();
                }
                catch (Exception fallbackEx)
                {
                    Log.Error($"Fallback navigation also failed: {fallbackEx.Message}");
                }
            }
        }
        
        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Toggle template overlay with G key (Ctrl+G for Guide/Grid)
            if (e.Key == Key.G && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ToggleTemplateOverlay();
                e.Handled = true;
                Log.Debug($"Template overlay toggled: {_showTemplateOverlay}");
                return;
            }

            // Check if interface is locked
            bool isLocked = PinLockService.Instance.IsInterfaceLocked;

            // Block system keys when locked
            if (isLocked)
            {
                // Block Alt+F4
                if (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    e.Handled = true;
                    Log.Debug("Blocked Alt+F4 - interface is locked");
                    return;
                }

                // Block Windows key combinations
                if (e.Key == Key.LWin || e.Key == Key.RWin)
                {
                    e.Handled = true;
                    Log.Debug("Blocked Windows key - interface is locked");
                    return;
                }

                // Block Alt+Tab
                if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    e.Handled = true;
                    Log.Debug("Blocked Alt+Tab - interface is locked");
                    return;
                }
                
                // Block Escape
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Log.Debug("Blocked Escape - interface is locked");
                    return;
                }
                
                // Block Ctrl+Esc (Start Menu)
                if (e.Key == Key.Escape && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    e.Handled = true;
                    Log.Debug("Blocked Ctrl+Esc - interface is locked");
                    return;
                }
                
                // Block Ctrl+Alt+Del (handled at system level but try anyway)
                if ((e.Key == Key.Delete || e.Key == Key.System) && 
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                    (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    e.Handled = true;
                    Log.Debug("Blocked Ctrl+Alt+Del attempt - interface is locked");
                    return;
                }
                
                // Block Ctrl+Shift+Esc (Task Manager)
                if (e.Key == Key.Escape && 
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                    (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    e.Handled = true;
                    Log.Debug("Blocked Ctrl+Shift+Esc (Task Manager) - interface is locked");
                    return;
                }
            }
        }
        
        private void ParentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cancel close when locked
            if (PinLockService.Instance.IsInterfaceLocked)
            {
                e.Cancel = true;
                Log.Debug("Blocked window close - interface is locked");
                
                // Request PIN to close
                PinLockService.Instance.RequestPinForUnlock((success) =>
                {
                    if (success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Unlock and close
                            PinLockService.Instance.IsInterfaceLocked = false;
                            UpdateLockButtonAppearance(false);
                            UpdateSettingsAccessibility(true);
                            
                            var window = Window.GetWindow(this);
                            window?.Close();
                        });
                    }
                });
            }
        }
        
        private void ParentWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Additional keyboard protection at window level
            if (PinLockService.Instance.IsInterfaceLocked)
            {
                // Block Alt+F4
                if (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    e.Handled = true;
                    Log.Debug("Window blocked Alt+F4 - interface is locked");
                    return;
                }
                
                // Block Alt+Space (system menu)
                if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    e.Handled = true;
                    Log.Debug("Window blocked Alt+Space - interface is locked");
                    return;
                }
            }
        }
        
        private void ParentWindow_StateChanged(object sender, EventArgs e)
        {
            var window = sender as Window;
            if (window != null && PinLockService.Instance.IsInterfaceLocked)
            {
                // Prevent ANY state change when locked
                if (window.WindowState != WindowState.Maximized)
                {
                    // Force back to maximized
                    window.WindowState = WindowState.Maximized;
                    window.Activate();
                    window.Focus();
                    
                    // Make sure it stays on top temporarily
                    window.Topmost = true;
                    
                    // Reset topmost after a brief moment
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(100);
                    timer.Tick += (s, args) =>
                    {
                        window.Topmost = false;
                        timer.Stop();
                    };
                    timer.Start();
                    
                    Log.Debug($"Blocked window state change to {window.WindowState} - interface is locked");
                }
            }
        }
        
        private void ParentWindow_Deactivated(object sender, EventArgs e)
        {
            var window = sender as Window;
            if (window != null && PinLockService.Instance.IsInterfaceLocked)
            {
                // Force window to stay active
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    window.Activate();
                    window.Focus();
                    
                    // Ensure it's maximized
                    if (window.WindowState != WindowState.Maximized)
                    {
                        window.WindowState = WindowState.Maximized;
                    }
                    
                    Log.Debug("Forced window reactivation - interface is locked");
                }), System.Windows.Threading.DispatcherPriority.Send);
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
            Log.Debug("SettingsButton_Click: Opening settings overlay");
            
            // Check if settings are locked
            var pinLockService = PinLockService.Instance;
            if (pinLockService.IsInterfaceLocked)
            {
                // Request PIN to access settings
                pinLockService.RequestPinForUnlock((success) =>
                {
                    if (success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Unlock and show settings
                            pinLockService.IsInterfaceLocked = false;
                            UpdateLockButtonAppearance(false);
                            UpdateSettingsAccessibility(true);
                            
                            if (SettingsOverlayControl != null)
                            {
                                SettingsOverlayControl.ShowOverlay(true); // Bypass PIN since we just authenticated
                            }
                            Log.Debug("Settings unlocked and overlay shown");
                        });
                    }
                });
                return;
            }
            
            // Show the comprehensive settings overlay
            if (SettingsOverlayControl != null)
            {
                // Bypass PIN since lock is not active
                SettingsOverlayControl.ShowOverlay(true);
            }
            else
            {
                Log.Error("SettingsButton_Click: SettingsOverlayControl is null");
            }
        }
        
        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("LockButton_Click: Toggling settings lock");
            
            var pinLockService = PinLockService.Instance;
            
            // If currently locked, request PIN to unlock
            if (pinLockService.IsInterfaceLocked)
            {
                pinLockService.RequestPinForUnlock((success) =>
                {
                    if (success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Unlock the interface
                            pinLockService.IsInterfaceLocked = false;
                            UpdateLockButtonAppearance(false);
                            UpdateSettingsAccessibility(true);
                            Log.Debug("Settings unlocked successfully");
                        });
                    }
                });
            }
            else
            {
                // Lock the interface immediately
                pinLockService.IsInterfaceLocked = true;
                UpdateLockButtonAppearance(true);
                UpdateSettingsAccessibility(false);
                Log.Debug("Settings locked");
            }
        }
        
        private void UpdateLockButtonAppearance(bool isLocked)
        {
            if (lockButton != null)
            {
                var template = lockButton.Template;
                if (template != null)
                {
                    var border = template.FindName("border", lockButton) as Border;
                    var icon = template.FindName("lockIcon", lockButton) as TextBlock;
                    
                    if (isLocked)
                    {
                        lockButton.Background = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x44, 0x44)); // Semi-transparent red
                        lockButton.ToolTip = "Settings Locked - Click to Unlock";
                        if (icon != null) icon.Text = "🔒";
                    }
                    else
                    {
                        lockButton.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); // Semi-transparent white
                        lockButton.ToolTip = "Lock Settings";
                        if (icon != null) icon.Text = "🔓";
                    }
                }
            }
        }
        
        private void UpdateSettingsAccessibility(bool accessible)
        {
            // Hide or disable the bottom control bar when locked
            if (bottomControlBar != null)
            {
                bottomControlBar.IsEnabled = accessible;
                if (!accessible && bottomControlBar.Visibility == Visibility.Visible)
                {
                    // Collapse the settings bar if it's open and we're locking
                    bottomControlBar.Visibility = Visibility.Collapsed;
                    bottomBarToggleChevron.Text = "⚙";
                }
            }
            
            // Also disable the settings button if visible
            if (settingsButton != null)
            {
                settingsButton.IsEnabled = accessible;
            }
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
                var validPhotos = FileValidationService.Instance.GetValidPhotos(_currentGallerySession.Photos);
                var photoPaths = validPhotos.Select(p => p.FilePath).ToList();
                    
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
                var printPhoto = FileValidationService.Instance.FindFirstValidPhotoByTypes(
                    _currentGallerySession.Photos, 
                    "4x6_print", 
                    "COMP"
                );
                    
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
                    var origPhotos = FileValidationService.Instance.GetValidPhotosByType(_currentGallerySession.Photos, "ORIG");
                    var photoPaths = origPhotos.Select(p => p.FilePath).ToList();
                    
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
                var validPhotos = FileValidationService.Instance.GetValidPhotos(_currentGallerySession.Photos);
                var photoPaths = validPhotos.Select(p => p.FilePath).ToList();
                    
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

            // Clean up any video elements (MP4 playback)
            CleanupVideoElements();
            Log.Debug("Gallery done: Cleaned up video elements");

            // Make sure live view image is visible and clear
            if (liveViewImage != null)
            {
                liveViewImage.Visibility = Visibility.Visible;
                liveViewImage.Source = null;
            }

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
            // Use the centralized ExitGalleryMode method which keeps gallery browser visible
            ExitGalleryMode();
            
            // Resume live view if enabled
            if (DeviceManager?.SelectedCameraDevice != null && Properties.Settings.Default.EnableIdleLiveView)
            {
                DeviceManager.SelectedCameraDevice.StartLiveView();
                _liveViewTimer.Start();
            }
            
            // Show start button
            _uiService.ShowStartButton();
            _uiService.UpdateStatus("Ready to start");
            
            Log.Debug("Exited gallery mode via exit button");
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
                
                // Force status check after SMS action to update indicator
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    // SMS status will be updated via the QueueStatusUpdated event
                    // triggered by the SMS sending process
                    Log.Debug("SMS action completed, status will update via queue events");
                });
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
                
                // Force status check after QR action to update indicator
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    var queueService = PhotoboothQueueService.Instance;
                    if (queueService != null)
                    {
                        var sessionId = _isInGalleryMode ? _currentGallerySession?.SessionFolder : _sessionService?.CurrentSessionId;
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            var qrResult = await queueService.CheckQRVisibilityAsync(sessionId, _isInGalleryMode);
                            Log.Debug($"QR status check after button click: visibility={qrResult.IsVisible}, message={qrResult.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"QR Code error: {ex.Message}");
                _uiService.UpdateStatus("QR Code failed");
            }
        }
        
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if interface is locked
            var pinLockService = PinLockService.Instance;
            if (pinLockService.IsInterfaceLocked)
            {
                // Request PIN to exit
                pinLockService.RequestPinForUnlock((success) =>
                {
                    if (success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Unlock and proceed with exit
                            pinLockService.IsInterfaceLocked = false;
                            UpdateLockButtonAppearance(false);
                            UpdateSettingsAccessibility(true);
                            
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
                        });
                    }
                });
                return;
            }
            
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
                    
                    // Update UI elements for both original and overlay gallery preview
                    if (galleryPreviewImage != null)
                        galleryPreviewImage.Source = bitmap;
                    if (galleryPreviewImageOverlay != null)
                        galleryPreviewImageOverlay.Source = bitmap;
                    
                    var sessionText = $"{previewData.SessionCount} session{(previewData.SessionCount != 1 ? "s" : "")}";
                    if (galleryPreviewInfo != null)
                        galleryPreviewInfo.Text = sessionText;
                    if (galleryPreviewInfoOverlay != null)
                        galleryPreviewInfoOverlay.Text = sessionText;
                    
                    // Original gallery box is hidden, only show overlay
                    if (galleryPreviewBox != null)
                        galleryPreviewBox.Visibility = Visibility.Collapsed;
                    if (galleryPreviewBoxOverlay != null)
                        galleryPreviewBoxOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    // No gallery content, hide both previews
                    if (galleryPreviewBox != null)
                        galleryPreviewBox.Visibility = Visibility.Collapsed;
                    if (galleryPreviewBoxOverlay != null)
                        galleryPreviewBoxOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading gallery preview: {ex.Message}");
                // Hide on error
                if (galleryPreviewBox != null)
                    galleryPreviewBox.Visibility = Visibility.Collapsed;
                if (galleryPreviewBoxOverlay != null)
                    galleryPreviewBoxOverlay.Visibility = Visibility.Collapsed;
            }
        }
        
        private void UpdateGalleryPreviewVisibility(bool inSession)
        {
            // Update visibility for overlay gallery preview
            if (galleryPreviewBoxOverlay != null)
            {
                // Hide during active session or gallery mode
                if (inSession || _isInGalleryMode)
                {
                    galleryPreviewBoxOverlay.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Show if we have gallery content
                    LoadGalleryPreview();
                }
            }
            
            // Original gallery box always hidden since we use overlay
            if (galleryPreviewBox != null)
            {
                galleryPreviewBox.Visibility = Visibility.Collapsed;
            }
        }
        
        private async void GalleryPreviewBox_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Log.Debug("Gallery preview clicked - checking event selection and lock state");
                
                // Check if an event is selected
                if (_currentEvent == null)
                {
                    Log.Debug("No event selected - gallery browser cannot be shown");
                    _uiService?.UpdateStatus("Please select an event first");
                    return;
                }
                
                // Check if interface is locked
                var pinLockService = PinLockService.Instance;
                if (pinLockService.IsInterfaceLocked)
                {
                    // Request PIN to access gallery
                    pinLockService.RequestPinForUnlock((success) =>
                    {
                        if (success)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                // Unlock and show gallery browser
                                pinLockService.IsInterfaceLocked = false;
                                UpdateLockButtonAppearance(false);
                                UpdateSettingsAccessibility(true);
                                
                                // Set the current event ID for filtering
                                _galleryBrowserService?.SetCurrentEventId(_currentEvent?.Id);
                                
                                // Show the gallery browser modal
                                if (GalleryBrowserModal != null)
                                {
                                    GalleryBrowserModal.ShowModal();
                                }
                                
                                Log.Debug("Gallery browser unlocked and shown");
                            });
                        }
                    });
                    return;
                }
                
                // Set the current event ID for filtering
                _galleryBrowserService?.SetCurrentEventId(_currentEvent?.Id);
                
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
            // Handle hover for both old and overlay gallery preview
            if (galleryPreviewHover != null)
                galleryPreviewHover.Visibility = Visibility.Visible;
            if (galleryPreviewHoverOverlay != null)
                galleryPreviewHoverOverlay.Visibility = Visibility.Visible;
        }
        
        private void GalleryPreviewBox_MouseLeave(object sender, MouseEventArgs e)
        {
            // Handle hover for both old and overlay gallery preview
            if (galleryPreviewHover != null)
                galleryPreviewHover.Visibility = Visibility.Collapsed;
            if (galleryPreviewHoverOverlay != null)
                galleryPreviewHoverOverlay.Visibility = Visibility.Collapsed;
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
        
        private void OnUpdateQrButtonState(bool hasQr, string message)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Don't override button states in gallery mode - gallery mode manages its own button states
                    if (_isInGalleryMode && _currentGallerySession != null)
                    {
                        Log.Debug($"Ignoring QR button state update in gallery mode: HasQR={hasQr}, Message={message}");
                        return;
                    }
                    
                    if (qrButton != null)
                    {
                        qrButton.IsEnabled = hasQr;
                        qrButton.ToolTip = message;
                        qrButton.Opacity = hasQr ? 1.0 : 0.6;
                        
                        // Update visual indicator based on QR availability
                        if (qrStatusIndicator != null && qrStatusIcon != null)
                        {
                            if (hasQr && (message.ToLowerInvariant().Contains("ready") || message.ToLowerInvariant().Contains("available") || message.ToLowerInvariant().Contains("view qr")))
                            {
                                // QR ready - green checkmark
                                qrStatusIndicator.Visibility = System.Windows.Visibility.Visible;
                                qrStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green
                                qrStatusIcon.Data = System.Windows.Media.Geometry.Parse("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z"); // Checkmark
                            }
                            else if (message.ToLowerInvariant().Contains("waiting") || message.ToLowerInvariant().Contains("upload") || message.ToLowerInvariant().Contains("processing"))
                            {
                                // Waiting for upload - orange cloud upload
                                qrStatusIndicator.Visibility = System.Windows.Visibility.Visible;
                                qrStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)); // Orange
                                qrStatusIcon.Data = System.Windows.Media.Geometry.Parse("M14,13V17H10V13H7L12,8L17,13M19.35,10.03C18.67,6.59 15.64,4 12,4C9.11,4 6.6,5.64 5.35,8.03C2.34,8.36 0,10.9 0,14A6,6 0 0,0 6,20H19A5,5 0 0,0 24,15C24,12.36 21.95,10.22 19.35,10.03Z"); // Cloud upload
                            }
                            else if (!hasQr)
                            {
                                // No QR available - hide or show offline indicator
                                if (message.ToLowerInvariant().Contains("offline") || message.ToLowerInvariant().Contains("no connection"))
                                {
                                    qrStatusIndicator.Visibility = System.Windows.Visibility.Visible;
                                    qrStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158)); // Gray
                                    qrStatusIcon.Data = System.Windows.Media.Geometry.Parse("M3.27,4.27L4.27,3.27L21,20L20,21L17.73,18.73C17.19,18.9 16.63,19 16.05,19A5,5 0 0,1 11.05,14C11.05,13.42 11.15,12.86 11.32,12.32L8.73,9.73C7.19,10.43 6.05,11.91 6.05,13.63A3.37,3.37 0 0,0 9.42,17H14.73L10.73,13H10.42A2.62,2.62 0 0,1 7.8,10.38C7.8,9.91 7.95,9.46 8.21,9.1L3.27,4.27M16.05,6A7,7 0 0,1 23.05,13A5,5 0 0,1 18.05,18L16.64,16.59C17.5,16.21 18.11,15.4 18.11,14.44A2.56,2.56 0 0,0 15.55,11.88H14.26L14.05,10.86A5,5 0 0,0 9.23,7.03L7.81,5.61C9.46,4.59 11.44,4 13.55,4C15.9,4 18.06,4.88 19.65,6.35C21.27,7.84 22.17,9.85 22.17,12C22.17,14.12 21.31,16.08 19.85,17.58L18.43,16.16C19.45,15.15 20.05,13.78 20.05,12.31A5.31,5.31 0 0,0 14.74,7A5.33,5.33 0 0,0 10.65,10.1L9.23,8.68C10.64,7.23 12.62,6.31 14.74,6.31C15.78,6.31 16.78,6.57 17.66,7.03L16.05,6Z"); // WiFi off
                                }
                                else
                                {
                                    qrStatusIndicator.Visibility = System.Windows.Visibility.Collapsed;
                                }
                            }
                            else
                            {
                                // Hide indicator if no specific state
                                qrStatusIndicator.Visibility = System.Windows.Visibility.Collapsed;
                            }
                        }
                        
                        Log.Debug($"QR button state updated: HasQR={hasQr}, Message={message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating QR button state: {ex.Message}");
            }
        }
        
        private void OnUpdateSmsButtonState(bool enabled, string message)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Don't override button states in gallery mode - gallery mode manages its own button states
                    if (_isInGalleryMode && _currentGallerySession != null)
                    {
                        Log.Debug($"Ignoring SMS button state update in gallery mode: Enabled={enabled}, Message={message}");
                        return;
                    }
                    
                    if (smsButton != null)
                    {
                        smsButton.IsEnabled = enabled;
                        smsButton.ToolTip = message;
                        smsButton.Opacity = enabled ? 1.0 : 0.6;
                        
                        // Update visual indicator based on message content
                        if (smsStatusIndicator != null && smsStatusIcon != null)
                        {
                            if (message.ToLowerInvariant().Contains("ready") || message.ToLowerInvariant().Contains("available") || (enabled && message.ToLowerInvariant().Contains("sms")))
                            {
                                // Online and ready - green checkmark
                                smsStatusIndicator.Visibility = System.Windows.Visibility.Visible;
                                smsStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green
                                smsStatusIcon.Data = System.Windows.Media.Geometry.Parse("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z"); // Checkmark
                            }
                            else if (message.ToLowerInvariant().Contains("queued") || message.ToLowerInvariant().Contains("queue") || message.ToLowerInvariant().Contains("offline"))
                            {
                                // Will be queued - orange clock
                                smsStatusIndicator.Visibility = System.Windows.Visibility.Visible;
                                smsStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)); // Orange
                                smsStatusIcon.Data = System.Windows.Media.Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M16.2,16.2L11,13V7H12.5V12.2L17,14.9L16.2,16.2Z"); // Clock
                            }
                            else if (!enabled)
                            {
                                // Disabled - red X
                                smsStatusIndicator.Visibility = System.Windows.Visibility.Visible;
                                smsStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)); // Red
                                smsStatusIcon.Data = System.Windows.Media.Geometry.Parse("M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"); // X
                            }
                            else
                            {
                                // Hide indicator if no specific state
                                smsStatusIndicator.Visibility = System.Windows.Visibility.Collapsed;
                            }
                        }
                        
                        Log.Debug($"SMS button state updated: Enabled={enabled}, Message={message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating SMS button state: {ex.Message}");
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
        
        #region Info Panel Event Handlers - Clean Architecture
        
        /// <summary>
        /// Handle printer status updates from InfoPanelService (UI updates only)
        /// </summary>
        private void OnInfoPanelPrinterStatusUpdated(object sender, PrinterStatusUpdatedEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var status = e.Status;
                    
                    // Update printer name
                    printerNameText.Text = status.Name;
                    
                    // Update connection status
                    printerConnectionText.Text = status.ConnectionText;
                    printerConnectionText.Foreground = new SolidColorBrush(status.ConnectionColor);
                    
                    // Update status
                    printerStatusText.Text = status.StatusText;
                    printerStatusIndicator.Background = new SolidColorBrush(status.StatusColor);
                    printerStatusText.Foreground = new SolidColorBrush(status.StatusColor);
                    
                    // Update queue count
                    printerQueueText.Text = status.QueueCount.ToString();
                    
                    // Update media remaining
                    if (status.ShowMediaRemaining)
                    {
                        mediaRemainingPanel.Visibility = Visibility.Visible;
                        mediaRemainingText.Text = status.MediaRemaining.ToString();
                        mediaTypeText.Text = status.MediaTypeText;
                        
                        // Color based on remaining count
                        var mediaColor = status.MediaRemaining > 100 ? Colors.LimeGreen 
                                       : status.MediaRemaining > 50 ? Colors.Orange 
                                       : Colors.Red;
                        mediaRemainingText.Foreground = new SolidColorBrush(mediaColor);
                    }
                    else
                    {
                        mediaRemainingPanel.Visibility = Visibility.Collapsed;
                    }
                    
                    // Update error display
                    if (status.HasError && !string.IsNullOrEmpty(status.ErrorMessage))
                    {
                        printerErrorText.Text = status.ErrorMessage;
                        printerErrorText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        printerErrorText.Visibility = Visibility.Collapsed;
                    }
                    
                    Log.Debug($"UI updated with printer status: {status.Name} - {status.StatusText}");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating printer status UI: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle cloud sync status updates from InfoPanelService (UI updates only)
        /// </summary>
        private void OnInfoPanelCloudSyncStatusUpdated(object sender, CloudSyncStatusUpdatedEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var status = e.Status;
                    
                    // Update status text and color
                    cloudSyncStatusText.Text = status.StatusText;
                    cloudSyncStatusText.Foreground = new SolidColorBrush(status.StatusColor);
                    
                    // Update bucket text and color
                    cloudSyncBucketText.Text = status.BucketText;
                    cloudSyncBucketText.Foreground = new SolidColorBrush(status.BucketColor);
                    
                    // Update icon color
                    cloudSyncIcon.Foreground = new SolidColorBrush(status.IconColor);
                    
                    // Update upload progress visibility
                    cloudUploadStatusPanel.Visibility = status.ShowUploadProgress ? Visibility.Visible : Visibility.Collapsed;
                    
                    Log.Debug($"UI updated with cloud sync status: {status.StatusText}");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating cloud sync status UI: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle camera status updates from InfoPanelService (UI updates only)
        /// </summary>
        private void OnInfoPanelCameraStatusUpdated(object sender, CameraStatusUpdatedEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    cameraStatusText.Text = e.Status;
                    
                    // Color based on connection status
                    var color = e.IsConnected ? Colors.LimeGreen : Colors.Orange;
                    cameraStatusText.Foreground = new SolidColorBrush(color);
                    
                    Log.Debug($"UI updated with camera status: {e.Status}");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating camera status UI: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle photo count updates from InfoPanelService (UI updates only)
        /// </summary>
        private void OnInfoPanelPhotoCountUpdated(object sender, PhotoCountUpdatedEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    photoCountText.Text = e.DisplayText;
                    photoCountText.Visibility = e.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                    
                    Log.Debug($"UI updated with photo count: {e.PhotoCount}, visible: {e.IsVisible}");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating photo count UI: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle sync progress updates from InfoPanelService (UI updates only)
        /// </summary>
        private void OnInfoPanelSyncProgressUpdated(object sender, SyncProgressUpdatedEventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Update pending count
                    syncPendingCount.Text = e.PendingText;
                    syncPendingCount.Visibility = e.ShowPendingCount ? Visibility.Visible : Visibility.Collapsed;
                    
                    // Update upload progress
                    if (e.ShowUploadProgress)
                    {
                        syncUploadProgress.Value = e.Progress;
                        syncUploadProgress.Visibility = Visibility.Visible;
                        cloudUploadStatusPanel.Visibility = Visibility.Visible;
                        cloudUploadStatusText.Text = e.UploadStatusText;
                    }
                    else
                    {
                        syncUploadProgress.Visibility = Visibility.Collapsed;
                        cloudUploadStatusPanel.Visibility = Visibility.Collapsed;
                    }
                    
                    Log.Debug($"UI updated with sync progress: {e.PendingCount} pending, {e.Progress}% progress");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating sync progress UI: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Queue Service Event Handlers
        
        private void OnQueueStatusChanged(PhotoboothQueueStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"Queue status changed - Pending SMS: {status.PendingSMSCount}, Waiting for URLs: {status.SessionsWaitingForUrls}");
                // Only update SMS indicator based on queue status
                // QR indicator should only be updated by OnQRCodeVisibilityChanged for the specific session
                bool isOffline = !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                string smsMessage = status.PendingSMSCount > 0 ? 
                    (isOffline ? "SMS queued (offline)" : "SMS queued") : 
                    "SMS ready";
                UpdateSMSStatusIndicator(status.PendingSMSCount == 0, smsMessage);
                
                // Don't update QR indicator here as it's session-specific
                // The queue having items doesn't mean THIS session's QR isn't ready
            });
        }
        
        private void OnQRCodeVisibilityChanged(string sessionId, bool isVisible)
        {
            Dispatcher.Invoke(() =>
            {
                // Only update indicator if this is for the current session
                var currentSessionId = _isInGalleryMode ? _currentGallerySession?.SessionName : _sessionService?.CurrentSessionId;
                
                if (sessionId == currentSessionId)
                {
                    Log.Debug($"QR visibility changed for current session {sessionId}: {isVisible}");
                    UpdateQRStatusIndicator(isVisible, isVisible ? "QR code ready" : "Processing QR code...");
                }
                else
                {
                    Log.Debug($"QR visibility changed for different session {sessionId} (current: {currentSessionId}): {isVisible} - ignoring");
                }
            });
        }
        
        private void OnSMSProcessed(string sessionId)
        {
            Dispatcher.Invoke(() =>
            {
                Log.Debug($"SMS processed for session {sessionId}");
                UpdateSMSStatusIndicator(true, "SMS ready");
            });
        }
        
        private void UpdateQRStatusIndicator(bool isReady, string message)
        {
            if (qrStatusIndicator != null && qrStatusIcon != null)
            {
                if (isReady)
                {
                    // QR ready - green checkmark
                    qrStatusIndicator.Visibility = Visibility.Visible;
                    qrStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    qrStatusIcon.Data = Geometry.Parse("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z"); // Checkmark
                }
                else if (message.ToLowerInvariant().Contains("processing") || message.ToLowerInvariant().Contains("uploading"))
                {
                    // Processing - orange cloud upload
                    qrStatusIndicator.Visibility = Visibility.Visible;
                    qrStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    qrStatusIcon.Data = Geometry.Parse("M14,13V17H10V13H7L12,8L17,13M19.35,10.03C18.67,6.59 15.64,4 12,4C9.11,4 6.6,5.64 5.35,8.03C2.34,8.36 0,10.9 0,14A6,6 0 0,0 6,20H19A5,5 0 0,0 24,15C24,12.36 21.95,10.22 19.35,10.03Z"); // Cloud upload
                }
                else
                {
                    // Not ready - hide
                    qrStatusIndicator.Visibility = Visibility.Collapsed;
                }
                Log.Debug($"QR indicator updated - Ready: {isReady}, Message: {message}");
            }
            
            // Update button enabled state based on QR readiness
            if (qrButton != null)
            {
                qrButton.IsEnabled = isReady;
                Log.Debug($"QR button enabled state updated: {isReady}");
            }
        }
        
        private void UpdateSMSStatusIndicator(bool isReady, string message)
        {
            if (smsStatusIndicator != null && smsStatusIcon != null)
            {
                if (isReady)
                {
                    // SMS ready - green checkmark
                    smsStatusIndicator.Visibility = Visibility.Visible;
                    smsStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    smsStatusIcon.Data = Geometry.Parse("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z"); // Checkmark
                }
                else if (message.ToLowerInvariant().Contains("queued") || message.ToLowerInvariant().Contains("offline"))
                {
                    // Queued - orange clock
                    smsStatusIndicator.Visibility = Visibility.Visible;
                    smsStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    smsStatusIcon.Data = Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M16.2,16.2L11,13V7H12.5V12.2L17,14.9L16.2,16.2Z"); // Clock
                }
                else
                {
                    // Not ready - hide
                    smsStatusIndicator.Visibility = Visibility.Collapsed;
                }
                Log.Debug($"SMS indicator updated - Ready: {isReady}, Message: {message}");
            }
            
            // Update SMS button enabled state - SMS is always enabled for queuing
            if (smsButton != null)
            {
                smsButton.IsEnabled = true; // SMS always enabled to allow queuing
                Log.Debug("SMS button enabled state updated: always true (allows queuing)");
            }
        }
        
        #endregion
        
        #endregion
        
        #region Capture Modes Integration
        
        private void ShowCaptureModesOverlay()
        {
            Log.Debug($"ShowCaptureModesOverlay: CaptureModesOverlay is {(CaptureModesOverlay != null ? "not null" : "null")}");
            
            if (CaptureModesOverlay == null)
            {
                Log.Error("ShowCaptureModesOverlay: CaptureModesOverlay is null!");
                return;
            }
            
            // Subscribe to overlay events
            CaptureModesOverlay.ModeSelected -= OnCaptureModeSelected;
            CaptureModesOverlay.ModeSelected += OnCaptureModeSelected;
            
            // Show the overlay
            Log.Debug("ShowCaptureModesOverlay: Calling Show() method");
            CaptureModesOverlay.Show();
            Log.Debug("ShowCaptureModesOverlay: Show() method called");
        }
        
        private async void OnCaptureModeSelected(object sender, Photobooth.Services.CaptureMode mode)
        {
            Log.Debug($"Capture mode selected: {mode}");
            
            // Ensure template and event are loaded
            if (!await EnsureTemplateAndEvent())
                return;
            
            // Handle different capture modes
            switch (mode)
            {
                case Photobooth.Services.CaptureMode.Photo:
                    await StartStandardPhotoSession();
                    break;
                    
                case Photobooth.Services.CaptureMode.Video:
                    await StartVideoSession();
                    break;
                    
                case Photobooth.Services.CaptureMode.Gif:
                    await StartGifSession();
                    break;
                    
                case Photobooth.Services.CaptureMode.Boomerang:
                    await StartBoomerangSession();
                    break;
                    
                case Photobooth.Services.CaptureMode.GreenScreen:
                    await StartGreenScreenSession();
                    break;
                    
                case Photobooth.Services.CaptureMode.AI:
                    await StartAISession();
                    break;
                    
                case Photobooth.Services.CaptureMode.Flipbook:
                    await StartFlipbookSession();
                    break;
            }
        }
        
        private async Task StartStandardPhotoSession()
        {
            // Standard photo capture - same as before
            bool sessionStarted = await _sessionService.StartSessionAsync(
                _currentEvent, 
                _currentTemplate, 
                GetTotalPhotosNeeded());
            
            if (sessionStarted)
            {
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            }
        }
        
        private async Task StartVideoSession()
        {
            var videoCoordinator = Services.VideoRecordingCoordinatorService.Instance;
            
            try
            {
                Log.Debug($"Starting video recording session - Current Event: {_currentEvent?.Name ?? "NULL"}, Event ID: {_currentEvent?.Id ?? 0}");
                
                // Subscribe to video recording completion event
                videoCoordinator.RecordingStopped += OnVideoRecordingStopped;
                
                // Start a session for tracking
                bool sessionStarted = await _sessionService.StartSessionAsync(
                    _currentEvent, 
                    _currentTemplate, 
                    0); // No photos for video
                
                if (!sessionStarted)
                {
                    Log.Error("Failed to start video session");
                    _uiService.UpdateStatus("Failed to start video session");
                    return;
                }
                
                // Hide the capture overlay
                if (CaptureModesOverlay != null && CaptureModesOverlay.Visibility == Visibility.Visible)
                {
                    CaptureModesOverlay.Hide();
                }
                
                // Clear display flags to ensure live view can be shown during recording
                _isDisplayingCapturedPhoto = false;
                SetDisplayingSessionResult(false);
                Log.Debug("Cleared display flags for video recording - live view will be shown");
                
                // Ensure live view timer is running BEFORE starting video recording
                Log.Debug($"Live view timer status before video: IsEnabled={_liveViewTimer?.IsEnabled}");
                if (_liveViewTimer != null && !_liveViewTimer.IsEnabled)
                {
                    _liveViewTimer.Start();
                    Log.Debug("Started live view timer BEFORE video recording");
                }
                
                // Ensure camera live view is active
                if (DeviceManager?.SelectedCameraDevice != null)
                {
                    var device = DeviceManager.SelectedCameraDevice;
                    // Just start live view - it's safe to call even if already running
                    device.StartLiveView();
                    Log.Debug("Ensured camera live view is active for video recording");
                }
                
                // Update UI to show video recording state
                _uiService.ShowVideoRecordingControls();
                _uiService.UpdateStatus("Starting video recording...");
                
                // Start video recording with live view
                bool recordingStarted = false;
                string videoStartError = null;
                
                try
                {
                    // Get the session info from the database operations
                    var dbOps = _sessionService?.GetDatabaseOperations();
                    int sessionId = dbOps?.CurrentSessionId ?? 0;
                    string sessionGuid = dbOps?.CurrentSessionGuid ?? _sessionService?.CurrentSessionId;
                    
                    Log.Debug($"Calling StartVideoSessionAsync with event: {_currentEvent?.Name ?? "NULL"} (ID: {_currentEvent?.Id ?? 0}), sessionId: {sessionId}, sessionGuid: {sessionGuid}");
                    recordingStarted = await videoCoordinator.StartVideoSessionAsync(null, _currentEvent, sessionId, sessionGuid);
                }
                catch (Exception videoEx)
                {
                    Log.Debug($"StartVideoSessionAsync threw exception: {videoEx.Message}");
                    // Check if this is a non-critical Canon property exception
                    if (videoEx.Message.Contains("Device Busy") && videoEx.Message.Contains("property"))
                    {
                        Log.Debug("Non-critical Canon property exception - checking if recording actually started");
                        // Give it a moment and check if recording is actually working
                        await Task.Delay(1000);
                        recordingStarted = videoCoordinator.IsRecording;
                        Log.Debug($"After property exception, IsRecording = {recordingStarted}");
                    }
                    else
                    {
                        videoStartError = videoEx.Message;
                    }
                }
                
                if (recordingStarted)
                {
                    Log.Debug("Video recording started successfully - live view should be visible");
                    _uiService.UpdateStatus("Recording video...");
                    
                    // Show stop button for video recording
                    if (stopSessionButton != null)
                    {
                        stopSessionButton.Visibility = Visibility.Visible;
                    }
                    
                    // Show recording indicator
                    if (VideoRecordingGrid != null)
                    {
                        VideoRecordingGrid.Visibility = Visibility.Visible;
                    }
                    
                    // Start recording duration timer
                    StartRecordingDurationTimer();
                    
                    // Log live view timer status during recording
                    Log.Debug($"Live view timer status during recording: IsEnabled={_liveViewTimer?.IsEnabled}");
                }
                else
                {
                    var errorToShow = videoStartError ?? "Failed to start video recording";
                    Log.Error($"Failed to start video recording: {errorToShow}");
                    
                    // Check if it's a Canon T6 mode dial issue
                    var errorMessage = "Failed to start video recording";
                    if (DeviceManager?.SelectedCameraDevice?.DeviceName?.Contains("T6") == true ||
                        DeviceManager?.SelectedCameraDevice?.DeviceName?.Contains("1300D") == true)
                    {
                        errorMessage = "Please set camera dial to MOVIE mode (video icon)";
                        
                        // Show a message box for Canon T6 users
                        await Dispatcher.InvokeAsync(() =>
                        {
                            System.Windows.MessageBox.Show(
                                "Canon T6 Video Recording:\n\n" +
                                "Please set your camera's mode dial to MOVIE MODE (video camera icon).\n\n" +
                                "The Canon T6 requires the physical dial to be in Movie mode for video recording.\n\n" +
                                "After changing the dial, try recording again.",
                                "Camera Setup Required",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information);
                        });
                    }
                    
                    _uiService.UpdateStatus(errorMessage);
                    
                    // End the session since recording failed
                    await _sessionService.CompleteSessionAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StartVideoSession error: {ex.Message}");
                
                // Check if this is a Canon property exception that might not be critical
                if (ex.Message.Contains("Device Busy") && ex.Message.Contains("property"))
                {
                    Log.Debug("Canon property exception in outer catch - checking if recording actually started");
                    // Give it a moment and check if recording is working despite the exception
                    await Task.Delay(1000);
                    if (videoCoordinator.IsRecording)
                    {
                        Log.Debug("Recording is actually working despite property exception");
                        _uiService.UpdateStatus("Recording video...");
                        return; // Don't show error, recording is working
                    }
                }
                
                _uiService.UpdateStatus("Video recording error");
            }
        }
        
        private void PlayVideoInLiveView(string videoPath)
        {
            try
            {
                if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
                {
                    Log.Error($"Cannot play video - file not found: {videoPath}");
                    return;
                }
                
                // Create a MediaElement to play the video
                var mediaElement = new MediaElement
                {
                    Source = new Uri(videoPath, UriKind.Absolute),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Close, // Close instead of Stop to release file
                    Stretch = Stretch.Uniform,
                    Volume = 0.5, // Set default volume
                    // Use lower buffering to reduce file lock time
                    ScrubbingEnabled = false
                };
                
                // Handle when video finishes
                mediaElement.MediaEnded += (s, args) =>
                {
                    Log.Debug("Video playback ended - looping");
                    mediaElement.Position = TimeSpan.Zero;
                    mediaElement.Play();
                };
                
                // Handle media opened to start playback
                mediaElement.MediaOpened += (s, args) =>
                {
                    Log.Debug($"Video loaded: Duration = {mediaElement.NaturalDuration}");
                    mediaElement.Play();
                };
                
                // Handle media failed
                mediaElement.MediaFailed += (s, args) =>
                {
                    Log.Error($"Failed to play video: {args.ErrorException?.Message}");
                };
                
                // Replace the live view image with the media element
                if (liveViewImage != null)
                {
                    // Get the parent container
                    var parent = liveViewImage.Parent as Panel;
                    if (parent != null)
                    {
                        // Store the current image visibility
                        liveViewImage.Visibility = Visibility.Collapsed;
                        
                        // Add the media element at the same position
                        parent.Children.Add(mediaElement);
                        
                        // Store reference for cleanup
                        _currentVideoPlayer = mediaElement;
                        
                        Log.Debug($"Video player added to UI for: {videoPath}");
                        
                        // Show video controls
                        if (VideoPlaybackControls != null)
                        {
                            VideoPlaybackControls.Visibility = Visibility.Visible;
                            UpdatePlayPauseButton(true); // Video is playing
                        }
                        
                        // Start playback
                        mediaElement.Play();
                        _isVideoPlaying = true;
                    }
                    else
                    {
                        Log.Error("Could not find parent panel for live view image");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PlayVideoInLiveView error: {ex.Message}");
            }
        }
        
        private MediaElement _currentVideoPlayer = null;
        private bool _isVideoPlaying = true; // Track play/pause state
        
        private void StopVideoPlayback()
        {
            try
            {
                if (_currentVideoPlayer != null)
                {
                    _currentVideoPlayer.Stop();
                    _currentVideoPlayer.Close();
                    
                    // Remove from UI
                    var parent = _currentVideoPlayer.Parent as Panel;
                    if (parent != null)
                    {
                        parent.Children.Remove(_currentVideoPlayer);
                    }
                    
                    _currentVideoPlayer = null;
                    
                    // Restore live view image visibility
                    if (liveViewImage != null)
                    {
                        liveViewImage.Visibility = Visibility.Visible;
                    }
                    
                    // Hide video controls
                    if (VideoPlaybackControls != null)
                    {
                        VideoPlaybackControls.Visibility = Visibility.Collapsed;
                    }
                    
                    _isVideoPlaying = false;
                    Log.Debug("Video playback stopped and player removed");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StopVideoPlayback error: {ex.Message}");
            }
        }
        
        private async void OnVideoRecordingStopped(object sender, VideoRecordingEventArgs e)
        {
            try
            {
                Log.Debug($"OnVideoRecordingStopped: Video recording completed - Duration: {e.Duration}");
                
                // Get the video path
                var videoCoordinator = Services.VideoRecordingCoordinatorService.Instance;
                string videoPath = videoCoordinator.CurrentVideoPath;
                
                if (string.IsNullOrEmpty(videoPath) && !string.IsNullOrEmpty(e.FilePath))
                {
                    videoPath = e.FilePath;
                }
                
                Log.Debug($"OnVideoRecordingStopped: Video file path: {videoPath}");
                
                // Update UI on main thread
                await Dispatcher.InvokeAsync(async () =>
                {
                    _uiService.UpdateStatus("Video recording completed");
                    
                    // Hide stop button
                    if (stopSessionButton != null)
                    {
                        stopSessionButton.Visibility = Visibility.Collapsed;
                    }
                    
                    // Hide recording indicator
                    if (VideoRecordingGrid != null)
                    {
                        VideoRecordingGrid.Visibility = Visibility.Collapsed;
                    }
                    
                    // Stop the recording duration timer
                    StopRecordingDurationTimer();
                    
                    // Stop live view timer since we'll show video
                    if (_liveViewTimer != null && _liveViewTimer.IsEnabled)
                    {
                        _liveViewTimer.Stop();
                        Log.Debug("Stopped live view timer to show video playback");
                    }
                    
                    // Generate video thumbnail and save to database
                    if (!string.IsNullOrEmpty(videoPath))
                    {
                        // Generate thumbnail using FFmpeg
                        try
                        {
                            var compressionService = Services.VideoCompressionService.Instance;
                            if (compressionService.IsFFmpegAvailable())
                            {
                                string thumbnailPath = await compressionService.GenerateThumbnailAsync(videoPath);
                                if (!string.IsNullOrEmpty(thumbnailPath))
                                {
                                    Log.Debug($"Generated video thumbnail: {thumbnailPath}");
                                }
                                
                                // Get video file info
                                if (File.Exists(videoPath))
                                {
                                    var fileInfo = new FileInfo(videoPath);
                                    long fileSize = fileInfo.Length;
                                    int durationSeconds = (int)e.Duration.TotalSeconds;
                                    
                                    // Save video session data to database
                                    _database.SaveVideoSessionData(videoPath, thumbnailPath, fileSize, durationSeconds);
                                    Log.Debug($"Saved video session data to database - Size: {fileSize}, Duration: {durationSeconds}s");
                                }
                            }
                        }
                        catch (Exception thumbEx)
                        {
                            Log.Error($"Error generating thumbnail or saving video data: {thumbEx.Message}");
                        }
                    }
                    
                    // Wait for video file to be fully downloaded
                    if (!string.IsNullOrEmpty(videoPath))
                    {
                        // Wait for file to be fully downloaded (not just exist)
                        bool fileReady = false;
                        await Task.Run(async () =>
                        {
                            int attempts = 0;
                            long lastSize = 0;
                            int stableCount = 0;
                            
                            while (attempts < 60) // Wait up to 60 seconds
                            {
                                if (System.IO.File.Exists(videoPath))
                                {
                                    try
                                    {
                                        var fileInfo = new System.IO.FileInfo(videoPath);
                                        long currentSize = fileInfo.Length;
                                        
                                        // Check if file size is stable (not growing)
                                        if (currentSize > 0 && currentSize == lastSize)
                                        {
                                            stableCount++;
                                            if (stableCount >= 3) // File size stable for 3 seconds
                                            {
                                                Log.Debug($"Video file fully downloaded: {videoPath} (Size: {currentSize / 1024.0 / 1024.0:F2} MB)");
                                                fileReady = true;
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            stableCount = 0;
                                            Log.Debug($"Video download in progress: {currentSize / 1024.0 / 1024.0:F2} MB");
                                        }
                                        
                                        lastSize = currentSize;
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Debug($"Error checking file size: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    Log.Debug($"Waiting for video file to be created: {videoPath}");
                                }
                                
                                await Task.Delay(1000);
                                attempts++;
                            }
                            
                            if (attempts >= 60)
                            {
                                Log.Error($"Timeout waiting for video download to complete: {videoPath}");
                            }
                        });
                        
                        if (fileReady && System.IO.File.Exists(videoPath))
                        {
                            Log.Debug($"Playing video in live view area: {videoPath}");
                            PlayVideoInLiveView(videoPath);
                        }
                        else
                        {
                            Log.Error($"Video file not ready or not found: {videoPath}");
                        }
                    }
                    
                    // Unsubscribe from event to prevent memory leaks
                    videoCoordinator.RecordingStopped -= OnVideoRecordingStopped;
                    
                    // Complete the session with video path
                    Log.Debug("OnVideoRecordingStopped: Completing video session");
                    await _sessionService.CompleteSessionAsync(videoPath);
                    
                    // Show completion options
                    _uiService.ShowCompletionControls();
                    _uiService.UpdateStatus("Session completed - Video ready");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"OnVideoRecordingStopped error: {ex.Message}");
            }
        }
        
        #region Video Playback Control Handlers
        
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentVideoPlayer != null)
                {
                    if (_isVideoPlaying)
                    {
                        _currentVideoPlayer.Pause();
                        _isVideoPlaying = false;
                        UpdatePlayPauseButton(false);
                        Log.Debug("Video paused");
                    }
                    else
                    {
                        _currentVideoPlayer.Play();
                        _isVideoPlaying = true;
                        UpdatePlayPauseButton(true);
                        Log.Debug("Video resumed");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PlayPauseButton_Click error: {ex.Message}");
            }
        }
        
        private void VideoStopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopVideoPlayback();
                
                // Optionally restart live view
                if (_liveViewTimer != null && !_liveViewTimer.IsEnabled)
                {
                    _liveViewTimer.Start();
                    Log.Debug("Restarted live view after stopping video");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"VideoStopButton_Click error: {ex.Message}");
            }
        }
        
        private void VideoReplayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentVideoPlayer != null)
                {
                    _currentVideoPlayer.Position = TimeSpan.Zero;
                    _currentVideoPlayer.Play();
                    _isVideoPlaying = true;
                    UpdatePlayPauseButton(true);
                    Log.Debug("Video restarted from beginning");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"VideoReplayButton_Click error: {ex.Message}");
            }
        }
        
        private void UpdatePlayPauseButton(bool isPlaying)
        {
            try
            {
                // Find the TextBlock inside the PlayPauseButton template
                if (PlayPauseButton != null)
                {
                    var template = PlayPauseButton.Template;
                    if (template != null)
                    {
                        var border = PlayPauseButton.Template.FindName("PlayPauseBorder", PlayPauseButton) as Border;
                        if (border != null)
                        {
                            var textBlock = border.Child as TextBlock;
                            if (textBlock != null)
                            {
                                textBlock.Text = isPlaying ? "⏸" : "▶";
                                PlayPauseButton.ToolTip = isPlaying ? "Pause" : "Play";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"UpdatePlayPauseButton error: {ex.Message}");
            }
        }
        
        #endregion
        
        private async Task StartGifSession()
        {
            // Start GIF capture session (typically 4 photos)
            bool sessionStarted = await _sessionService.StartSessionAsync(
                _currentEvent, 
                _currentTemplate, 
                4); // 4 photos for GIF
            
            if (sessionStarted)
            {
                // TODO: Track GIF session mode in PhotoboothSessionService
                // _sessionService.IsGifSession = true;
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            }
        }
        
        private async Task StartBoomerangSession()
        {
            // Start Boomerang session (rapid burst)
            bool sessionStarted = await _sessionService.StartSessionAsync(
                _currentEvent, 
                _currentTemplate, 
                10); // 10 photos for boomerang
            
            if (sessionStarted)
            {
                // Set rapid capture mode
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            }
        }
        
        private async Task StopVideoRecording()
        {
            try
            {
                Log.Debug("Stopping video recording");
                
                var videoCoordinator = Services.VideoRecordingCoordinatorService.Instance;
                
                // Get the current video path from the coordinator's recording service
                string videoPath = videoCoordinator.CurrentVideoPath;
                Log.Debug($"Current video path before stopping: {videoPath ?? "null"}");
                
                // Stop the video session
                bool stopped = await videoCoordinator.StopVideoSessionAsync();
                
                if (stopped)
                {
                    Log.Debug("Video recording stopped successfully");
                    _uiService.UpdateStatus("Video recording completed");
                    
                    // Hide stop button
                    if (stopSessionButton != null)
                    {
                        stopSessionButton.Visibility = Visibility.Collapsed;
                    }
                    
                    // Hide recording indicator
                    if (VideoRecordingGrid != null)
                    {
                        VideoRecordingGrid.Visibility = Visibility.Collapsed;
                    }
                    
                    // Stop recording duration timer
                    StopRecordingDurationTimer();
                    
                    // Display the video in the live view window
                    if (!string.IsNullOrEmpty(videoPath) && System.IO.File.Exists(videoPath))
                    {
                        Log.Debug($"Displaying recorded video: {videoPath}");
                        DisplayVideoInLiveView(videoPath);
                    }
                    else
                    {
                        Log.Error($"Video file not found after recording: {videoPath}");
                    }
                    
                    // End the session
                    await _sessionService.CompleteSessionAsync();
                    
                    // Show completion options
                    _uiService.ShowCompletionControls();
                }
                else
                {
                    Log.Error("Failed to stop video recording properly");
                    _uiService.UpdateStatus("Error stopping video");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StopVideoRecording error: {ex.Message}");
                _uiService.UpdateStatus("Error stopping video");
            }
        }
        
        private void StartRecordingDurationTimer()
        {
            _recordingStartTime = DateTime.Now;
            
            if (_recordingDurationTimer == null)
            {
                _recordingDurationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _recordingDurationTimer.Tick += RecordingDurationTimer_Tick;
            }
            
            _recordingDurationTimer.Start();
            Log.Debug("Recording duration timer started");
        }
        
        private void StopRecordingDurationTimer()
        {
            _recordingDurationTimer?.Stop();
            
            if (RecordingDurationText != null)
            {
                RecordingDurationText.Text = "00:00";
            }
            
            Log.Debug("Recording duration timer stopped");
        }
        
        private async void RecordingDurationTimer_Tick(object sender, EventArgs e)
        {
            var duration = DateTime.Now - _recordingStartTime;
            
            if (RecordingDurationText != null)
            {
                RecordingDurationText.Text = $"{duration:mm\\:ss}";
            }
            
            // Check if recording coordinator is still recording to sync UI
            var coordinator = Services.VideoRecordingCoordinatorService.Instance;
            if (!coordinator.IsRecording)
            {
                Log.Debug("UI timer detected recording stopped - stopping UI timer");
                StopRecordingDurationTimer();
                // The video recording should have already stopped, just update UI
                await StopVideoRecording();
            }
        }
        
        private async void StopVideoButton_Click(object sender, RoutedEventArgs e)
        {
            await StopVideoRecording();
        }
        
        private async Task StartGreenScreenSession()
        {
            // Start green screen session
            bool sessionStarted = await _sessionService.StartSessionAsync(
                _currentEvent, 
                _currentTemplate, 
                1); // Single photo with green screen
            
            if (sessionStarted)
            {
                // Enable green screen processing
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            }
        }
        
        private async Task StartAISession()
        {
            // Start AI-enhanced photo session
            bool sessionStarted = await _sessionService.StartSessionAsync(
                _currentEvent, 
                _currentTemplate, 
                1); // Single photo with AI enhancement
            
            if (sessionStarted)
            {
                // Enable AI processing
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            }
        }
        
        private async Task StartFlipbookSession()
        {
            // Start flipbook session (many rapid photos)
            bool sessionStarted = await _sessionService.StartSessionAsync(
                _currentEvent, 
                _currentTemplate, 
                8); // 8 photos for flipbook
            
            if (sessionStarted)
            {
                // Set rapid capture for flipbook
                await _workflowService.StartPhotoCaptureWorkflowAsync();
            }
        }
        
        #endregion
        
        #region Info Panel UI Updates (Clean Architecture - UI Only)
        
        /// <summary>
        /// Handle cloud sync UI updates from service (UI updates only)
        /// </summary>
        private void OnCloudSyncUIUpdate(object sender, Services.CloudSyncUIUpdateEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var state = e.State;
                
                // Update UI elements with provided state
                cloudRealSyncStatusText.Text = state.StatusText;
                cloudRealSyncStatusText.Foreground = new SolidColorBrush(state.StatusColor);
                cloudRealSyncIcon.Foreground = new SolidColorBrush(state.IconColor);
                cloudRealSyncInfoText.Text = state.InfoText;
                cloudRealSyncInfoText.Foreground = new SolidColorBrush(state.InfoColor);
                
                // Update progress panel
                cloudRealSyncProgressPanel.Visibility = state.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
                if (state.ShowProgress)
                {
                    cloudRealSyncProgress.IsIndeterminate = state.IsProgressIndeterminate;
                    if (!state.IsProgressIndeterminate)
                    {
                        cloudRealSyncProgress.Value = state.ProgressValue;
                    }
                    cloudRealSyncProgressText.Text = state.ProgressText;
                }
            });
        }
        
        /// <summary>
        /// Handle cloud share UI updates from service (UI updates only)
        /// </summary>
        private void OnCloudShareUIUpdate(object sender, Services.CloudShareUIUpdateEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var state = e.State;
                
                // Update cloud share UI elements
                cloudSyncStatusText.Text = state.StatusText;
                cloudSyncStatusText.Foreground = new SolidColorBrush(state.StatusColor);
                cloudSyncIcon.Foreground = new SolidColorBrush(state.IconColor);
                cloudSyncBucketText.Text = state.BucketText;
                cloudSyncBucketText.Foreground = new SolidColorBrush(state.BucketColor);
                
                // Update upload progress
                cloudUploadStatusPanel.Visibility = state.ShowUploadProgress ? Visibility.Visible : Visibility.Collapsed;
                if (state.ShowUploadProgress)
                {
                    cloudUploadProgress.Value = state.UploadProgress;
                    cloudUploadStatusText.Text = state.UploadText;
                }
            });
        }
        
        /// <summary>
        /// Handle printer status UI updates from service (UI updates only)
        /// </summary>
        private void OnPrinterStatusUIUpdate(object sender, Services.PrinterStatusUIUpdateEventArgs e)
        {
            // Forward to existing printer status handler
            OnInfoPanelPrinterStatusUpdated(sender, new Services.PrinterStatusUpdatedEventArgs { Status = e.Status });
        }
        
        /// <summary>
        /// Handle camera status UI updates from service (UI updates only)
        /// </summary>
        private void OnCameraStatusUIUpdate(object sender, Services.CameraStatusUIUpdateEventArgs e)
        {
            // Forward to existing camera status handler
            OnInfoPanelCameraStatusUpdated(sender, new Services.CameraStatusUpdatedEventArgs 
            { 
                Status = e.Status?.CameraName ?? "No Camera",
                IsConnected = e.Status?.IsConnected ?? false
            });
        }
        
        /// <summary>
        /// Touch Template Designer button click handler
        /// </summary>
        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null)
                {
                    if (parentWindow.WindowState == WindowState.Maximized && parentWindow.WindowStyle == WindowStyle.None)
                    {
                        // Exit fullscreen
                        parentWindow.WindowState = WindowState.Normal;
                        parentWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                        
                        // Update button text
                        if (FullscreenText != null) FullscreenText.Text = "Fullscreen";
                        if (FullscreenIcon != null) FullscreenIcon.Text = "⛶";
                        
                        Log.Debug("Exited fullscreen mode");
                    }
                    else
                    {
                        // Enter fullscreen
                        parentWindow.WindowStyle = WindowStyle.None;
                        parentWindow.WindowState = WindowState.Maximized;
                        
                        // Update button text
                        if (FullscreenText != null) FullscreenText.Text = "Exit Fullscreen";
                        if (FullscreenIcon != null) FullscreenIcon.Text = "⛶";
                        
                        Log.Debug("Entered fullscreen mode");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"FullscreenButton_Click error: {ex.Message}");
            }
        }
        
        private void TouchTemplateDesignerButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Touch Template Designer button clicked");
            
            // Hide the bottom control bar
            if (bottomControlBar != null)
            {
                bottomControlBar.Visibility = Visibility.Collapsed;
                bottomBarToggleChevron.Text = "⚙"; // Reset to gear icon
            }
            
            // Create and show the touch template designer overlay
            var touchDesignerOverlay = new Photobooth.Controls.TouchTemplateDesignerOverlay();
            
            // Add directly to the main grid of this page (not the parent)
            // Find the main grid by looking for the first Grid child of this UserControl
            Grid mainPageGrid = null;
            
            // The page's content should be a Grid
            if (this.Content is Grid grid)
            {
                mainPageGrid = grid;
            }
            
            if (mainPageGrid != null)
            {
                // Add to the same grid that contains other overlays
                mainPageGrid.Children.Add(touchDesignerOverlay);
                
                // Set grid row and column spans to cover entire area
                Grid.SetRow(touchDesignerOverlay, 0);
                Grid.SetRowSpan(touchDesignerOverlay, 10);
                Grid.SetColumnSpan(touchDesignerOverlay, 10);
                
                // Set high z-index to appear above everything except PIN overlay
                Panel.SetZIndex(touchDesignerOverlay, 3000); // Higher than TemplateDesignerOverlay (2000)
                
                // Make it visible
                touchDesignerOverlay.Visibility = Visibility.Visible;
                
                // Handle close event to remove from grid
                touchDesignerOverlay.CloseRequested += (s, args) =>
                {
                    mainPageGrid.Children.Remove(touchDesignerOverlay);
                };
            }
            else
            {
                // Fallback: Create a new window for the designer if we can't find the grid
                var designerWindow = new Window
                {
                    Title = "Touch Template Designer",
                    WindowState = WindowState.Maximized,
                    WindowStyle = WindowStyle.None,
                    Content = touchDesignerOverlay
                };
                designerWindow.Show();
            }
        }
        
        #endregion
    }
}