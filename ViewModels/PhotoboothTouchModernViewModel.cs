using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Controls.ModularComponents;
using Photobooth.Services;
using Photobooth.Models;
using Photobooth.Database;
using System.Windows.Threading;

namespace Photobooth.ViewModels
{
    public class PhotoboothTouchModernViewModel : INotifyPropertyChanged
    {
        private ModuleManager _moduleManager;
        private CameraDeviceManager _deviceManager;
        private ICameraDevice _selectedCamera;
        private BitmapImage _liveViewImage;
        private BitmapImage _lastCapturedImage;
        private bool _isLiveViewActive;
        private string _statusMessage;
        private int _countdownValue;
        private bool _isCountingDown;
        private string _outputFolder;
        private DispatcherTimer _liveViewTimer;
        
        // Event/Template properties
        private EventData _currentEvent;
        private TemplateData _currentTemplate;
        private EventTemplateService _eventTemplateService;
        
        // Collections
        private ObservableCollection<ModuleButtonViewModel> _moduleButtons;
        private ObservableCollection<BitmapImage> _capturedImages;
        private ObservableCollection<ICameraDevice> _availableCameras;
        
        public ModuleManager ModuleManager => _moduleManager;
        
        public CameraDeviceManager DeviceManager
        {
            get => _deviceManager;
            set
            {
                _deviceManager = value;
                OnPropertyChanged();
                RefreshCameras();
            }
        }
        
        public ICameraDevice SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                if (_selectedCamera != value)
                {
                    _selectedCamera = value;
                    OnPropertyChanged();
                    OnCameraChanged();
                }
            }
        }
        
        public BitmapImage LiveViewImage
        {
            get => _liveViewImage;
            set
            {
                _liveViewImage = value;
                OnPropertyChanged();
            }
        }
        
        public BitmapImage LastCapturedImage
        {
            get => _lastCapturedImage;
            set
            {
                _lastCapturedImage = value;
                OnPropertyChanged();
            }
        }
        
        public bool IsLiveViewActive
        {
            get => _isLiveViewActive;
            set
            {
                _isLiveViewActive = value;
                OnPropertyChanged();
            }
        }
        
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
        
        public int CountdownValue
        {
            get => _countdownValue;
            set
            {
                _countdownValue = value;
                OnPropertyChanged();
            }
        }
        
        public bool IsCountingDown
        {
            get => _isCountingDown;
            set
            {
                _isCountingDown = value;
                OnPropertyChanged();
            }
        }
        
        public EventData CurrentEvent
        {
            get => _currentEvent;
            set
            {
                _currentEvent = value;
                OnPropertyChanged();
            }
        }
        
        public TemplateData CurrentTemplate
        {
            get => _currentTemplate;
            set
            {
                _currentTemplate = value;
                OnPropertyChanged();
            }
        }
        
        public ObservableCollection<ModuleButtonViewModel> ModuleButtons
        {
            get => _moduleButtons;
            set
            {
                _moduleButtons = value;
                OnPropertyChanged();
            }
        }
        
        public ObservableCollection<BitmapImage> CapturedImages
        {
            get => _capturedImages;
            set
            {
                _capturedImages = value;
                OnPropertyChanged();
            }
        }
        
        public ObservableCollection<ICameraDevice> AvailableCameras
        {
            get => _availableCameras;
            set
            {
                _availableCameras = value;
                OnPropertyChanged();
            }
        }
        
        // Additional Properties for UI Binding
        public string LiveViewButtonText => IsLiveViewActive ? "Stop Live View" : "Start Live View";
        public string CurrentEventName => CurrentEvent?.Name ?? "No Event Selected";
        public string CurrentTemplateName => CurrentTemplate?.Name ?? "No Template Selected";
        public string CapturedCountText => $"{CapturedImages?.Count ?? 0} photos captured";
        public bool ShowLastCapture => LastCapturedImage != null;
        public bool IsProcessing => _moduleManager?.IsProcessing ?? false;
        public string CurrentModuleName => _moduleManager?.CurrentModule?.ModuleName ?? "";
        public string ProcessingStatus => _moduleManager?.ProcessingStatus ?? "";

        // Commands
        public ICommand StartModuleCommand { get; private set; }
        public ICommand StopCaptureCommand { get; private set; }
        public ICommand ToggleLiveViewCommand { get; private set; }
        public ICommand RefreshCamerasCommand { get; private set; }
        public ICommand ClearSessionCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        
        public PhotoboothTouchModernViewModel()
        {
            InitializeViewModel();
            InitializeCommands();
            InitializeModules();
            InitializeLiveView();
        }
        
        private void InitializeViewModel()
        {
            _moduleManager = ModuleManager.Instance;
            _deviceManager = CameraSessionManager.Instance.DeviceManager;
            _eventTemplateService = new EventTemplateService();
            
            _moduleButtons = new ObservableCollection<ModuleButtonViewModel>();
            _capturedImages = new ObservableCollection<BitmapImage>();
            _availableCameras = new ObservableCollection<ICameraDevice>();
            
            _outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");
            if (!Directory.Exists(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
            }
            
            StatusMessage = "Initializing cameras...";
            
            // Subscribe to device manager events for photographer mode
            if (_deviceManager != null && Properties.Settings.Default.PhotographerMode)
            {
                Log.Debug($"PhotoboothTouchModernViewModel: Enabling photographer mode - subscribing to PhotoCaptured event");
                _deviceManager.PhotoCaptured += OnPhotographerModeCapture;
            }
            else
            {
                Log.Debug($"PhotoboothTouchModernViewModel: Photographer mode disabled or DeviceManager null. Mode={Properties.Settings.Default.PhotographerMode}, DeviceManager={_deviceManager != null}");
            }
            
            // Initialize cameras like the original PhotoboothTouchModern does
            _ = InitializeCamerasAsync();
        }
        
        private void InitializeCommands()
        {
            StartModuleCommand = new RelayCommand<string>(async (moduleName) => await StartModule(moduleName));
            StopCaptureCommand = new RelayCommand(async () => await StopCurrentCapture());
            ToggleLiveViewCommand = new RelayCommand(() => ToggleLiveView());
            RefreshCamerasCommand = new RelayCommand(() => RefreshCameras());
            ClearSessionCommand = new RelayCommand(() => ClearSession());
            OpenSettingsCommand = new RelayCommand(() => OpenSettings());
        }
        
        private void InitializeModules()
        {
            _moduleManager.LoadModuleSettings();
            _moduleManager.Initialize(_selectedCamera, _outputFolder);
            
            // Subscribe to module events
            _moduleManager.AnyCaptureCompleted += OnModuleCaptureCompleted;
            _moduleManager.AnyStatusChanged += OnModuleStatusChanged;
            
            // Create module buttons for enabled modules
            UpdateModuleButtons();
        }
        
        private void CreatePlaceholderModules()
        {
            var modules = new[]
            {
                new { Name = "Photo", Icon = "📷" },
                new { Name = "Video", Icon = "🎥" },
                new { Name = "GIF", Icon = "🎞️" },
                new { Name = "Boomerang", Icon = "🔄" }
            };
            
            foreach (var module in modules)
            {
                var buttonVm = new ModuleButtonViewModel
                {
                    ModuleName = module.Name,
                    IconText = module.Icon,
                    IsEnabled = true,
                    Command = StartModuleCommand,
                    CommandParameter = module.Name
                };
                
                ModuleButtons.Add(buttonVm);
            }
        }
        
        private void InitializeLiveView()
        {
            _liveViewTimer = new DispatcherTimer();
            _liveViewTimer.Interval = TimeSpan.FromMilliseconds(100);
            _liveViewTimer.Tick += LiveViewTimer_Tick;
        }
        
        private void UpdateModuleButtons()
        {
            ModuleButtons.Clear();
            
            foreach (var module in _moduleManager.GetEnabledModules())
            {
                var buttonVm = new ModuleButtonViewModel
                {
                    ModuleName = module.ModuleName,
                    IconPath = module.IconPath,
                    IconText = GetModuleIcon(module.ModuleName),
                    IsEnabled = module.IsEnabled,
                    Command = StartModuleCommand,
                    CommandParameter = module.ModuleName
                };
                
                ModuleButtons.Add(buttonVm);
            }
        }

        private string GetModuleIcon(string moduleName)
        {
            if (string.IsNullOrEmpty(moduleName))
                return "📸";

            switch (moduleName.ToLower())
            {
                case "photo":
                case "photocapture":
                    return "📷";
                case "video":
                    return "🎥";
                case "gif":
                    return "🎞️";
                case "boomerang":
                    return "🔄";
                case "print":
                    return "🖨️";
                default:
                    return "📸";
            }
        }
        
        private async Task StartModule(string moduleName)
        {
            if (_moduleManager.IsProcessing)
            {
                StatusMessage = "Already processing...";
                return;
            }
            
            var success = await _moduleManager.StartModule(moduleName);
            if (!success)
            {
                StatusMessage = $"Failed to start {moduleName}";
            }
        }
        
        private async Task StopCurrentCapture()
        {
            await _moduleManager.StopActiveModule();
            StatusMessage = "Capture stopped";
        }
        
        private void ToggleLiveView()
        {
            if (IsLiveViewActive)
            {
                StopLiveView();
            }
            else
            {
                StartLiveView();
            }
        }
        
        private void StartLiveView()
        {
            if (_selectedCamera == null || !_selectedCamera.IsConnected)
            {
                StatusMessage = "No camera connected";
                return;
            }
            
            try
            {
                _selectedCamera.StartLiveView();
                _liveViewTimer.Start();
                IsLiveViewActive = true;
                StatusMessage = "Live view started";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Live view error: {ex.Message}";
            }
        }
        
        private void StopLiveView()
        {
            try
            {
                _liveViewTimer.Stop();
                _selectedCamera?.StopLiveView();
                IsLiveViewActive = false;
                LiveViewImage = null;
                StatusMessage = "Live view stopped";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error stopping live view: {ex.Message}";
            }
        }
        
        private async void LiveViewTimer_Tick(object sender, EventArgs e)
        {
            if (_selectedCamera == null || !IsLiveViewActive) return;
            
            try
            {
                var liveViewData = await Task.Run(() => _selectedCamera.GetLiveViewImage());
                if (liveViewData != null && liveViewData.ImageData != null)
                {
                    using (var stream = new MemoryStream(liveViewData.ImageData))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
                        LiveViewImage = bitmap;
                    }
                }
            }
            catch
            {
                // Silently ignore live view errors to avoid spam
            }
        }
        
        private async Task InitializeCamerasAsync()
        {
            try
            {
                StatusMessage = "Connecting to cameras...";
                
                // Connect to cameras like the original PhotoboothTouchModern does
                if (_deviceManager != null)
                {
                    await Task.Run(() => _deviceManager.ConnectToCamera());
                    
                    // Wait a moment for cameras to be detected
                    await Task.Delay(1000);
                    
                    // Refresh the camera list on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        RefreshCameras();
                        
                        if (AvailableCameras.Count > 0)
                        {
                            StatusMessage = $"Connected to {AvailableCameras.Count} camera(s)";
                        }
                        else
                        {
                            StatusMessage = "No cameras detected";
                        }
                    });
                }
                else
                {
                    StatusMessage = "Camera manager not available";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Camera initialization error: {ex.Message}";
            }
        }
        
        private void RefreshCameras()
        {
            AvailableCameras.Clear();
            
            if (_deviceManager != null)
            {
                foreach (var camera in _deviceManager.ConnectedDevices)
                {
                    AvailableCameras.Add(camera);
                }
                
                if (AvailableCameras.Count > 0 && _selectedCamera == null)
                {
                    SelectedCamera = AvailableCameras.First();
                }
            }
        }
        
        private void OnCameraChanged()
        {
            StopLiveView();
            _moduleManager.UpdateCamera(_selectedCamera);
            
            // Set up photographer mode for the new camera
            SetupPhotographerMode();
            
            if (_selectedCamera != null && _selectedCamera.IsConnected)
            {
                StartLiveView();
            }
        }
        
        private void SetupPhotographerMode()
        {
            if (_selectedCamera == null) return;
            
            // Always unsubscribe first to avoid duplicate subscriptions
            _selectedCamera.PhotoCaptured -= OnPhotographerModeCapture;
            
            if (Properties.Settings.Default.PhotographerMode)
            {
                Log.Debug($"PhotoboothTouchModernViewModel: Setting up photographer mode for {_selectedCamera.DeviceName}");
                
                try
                {
                    // Configure camera for photographer mode
                    // For Canon cameras, set SaveTo to Host or Both
                    var cameraType = _selectedCamera.GetType();
                    if (cameraType.Name.Contains("Canon"))
                    {
                        ConfigureCanonForPhotographerMode();
                    }
                    
                    // Ensure CaptureInSdRam is false for all cameras
                    _selectedCamera.CaptureInSdRam = false;
                    
                    // Subscribe directly to camera's PhotoCaptured event
                    _selectedCamera.PhotoCaptured += OnPhotographerModeCapture;
                    Log.Debug("PhotoboothTouchModernViewModel: Subscribed to camera PhotoCaptured event for photographer mode");
                    
                    StatusMessage = "Photographer mode enabled - camera button will capture photos";
                }
                catch (Exception ex)
                {
                    Log.Error($"PhotoboothTouchModernViewModel: Failed to setup photographer mode: {ex.Message}", ex);
                    StatusMessage = $"Photographer mode setup failed: {ex.Message}";
                }
            }
        }
        
        private void ConfigureCanonForPhotographerMode()
        {
            try
            {
                var cameraType = _selectedCamera.GetType();
                
                // Try to call SavePicturesToHost method if it exists
                var savePicsMethod = cameraType.GetMethod("SavePicturesToHost");
                if (savePicsMethod != null)
                {
                    savePicsMethod.Invoke(_selectedCamera, new object[] { true });
                    Log.Debug("PhotoboothTouchModernViewModel: Called SavePicturesToHost(true) on Canon camera");
                    return;
                }
                
                // Try to set SaveTo property
                var saveToProperty = cameraType.GetProperty("SaveTo");
                if (saveToProperty != null)
                {
                    try
                    {
                        // Try Both (3) first - saves to both camera and host
                        var saveToValue = saveToProperty.GetValue(_selectedCamera);
                        Log.Debug($"PhotoboothTouchModernViewModel: Current Canon SaveTo value: {saveToValue}");
                        
                        // Set to Both (3)
                        saveToProperty.SetValue(_selectedCamera, 3);
                        Log.Debug("PhotoboothTouchModernViewModel: Set Canon SaveTo to Both (3)");
                    }
                    catch (Exception ex)
                    {
                        // Try Host (2) if Both fails
                        try
                        {
                            saveToProperty.SetValue(_selectedCamera, 2);
                            Log.Debug("PhotoboothTouchModernViewModel: Set Canon SaveTo to Host (2)");
                        }
                        catch
                        {
                            Log.Debug($"PhotoboothTouchModernViewModel: Could not set SaveTo: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Log.Debug("PhotoboothTouchModernViewModel: Canon camera does not have SaveTo property");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothTouchModernViewModel: Error configuring Canon for photographer mode: {ex.Message}", ex);
            }
        }
        
        private void OnModuleCaptureCompleted(object sender, ModuleCaptureEventArgs e)
        {
            if (e.Success && !string.IsNullOrEmpty(e.OutputPath) && File.Exists(e.OutputPath))
            {
                // Load and display the captured image
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(e.OutputPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                
                LastCapturedImage = bitmap;
                CapturedImages.Add(bitmap);
                
                StatusMessage = $"{e.ModuleName} capture completed";
            }
            else
            {
                StatusMessage = $"{e.ModuleName} capture failed: {e.ErrorMessage}";
            }
        }
        
        private void OnModuleStatusChanged(object sender, ModuleStatusEventArgs e)
        {
            StatusMessage = e.Message ?? e.Status;
            
            // Handle countdown updates
            if (e.Status == "Countdown" && int.TryParse(e.Message?.Split(' ').LastOrDefault(), out int countdown))
            {
                CountdownValue = countdown;
                IsCountingDown = countdown > 0;
            }
            else
            {
                IsCountingDown = false;
            }
        }
        
        private void ClearSession()
        {
            CapturedImages.Clear();
            LastCapturedImage = null;
            StatusMessage = "Session cleared";
        }
        
        private void OpenSettings()
        {
            // This would open a settings window/page
            StatusMessage = "Settings feature coming soon...";
        }
        
        private async void OnPhotographerModeCapture(object sender, PhotoCapturedEventArgs e)
        {
            // Handle photo captured by camera's physical button in photographer mode
            if (!Properties.Settings.Default.PhotographerMode) return;
            
            // Run the entire handler on a background thread to avoid blocking the camera
            await Task.Run(async () =>
            {
                try
                {
                    // Update status on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = "Photo captured via camera button - processing...";
                    });
                    
                    // Generate output path
                    string fileName = $"Photographer_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                    string fullPath = Path.Combine(_outputFolder, fileName);
                    
                    // Transfer the photo (on background thread)
                    if (e.Handle != null && e.CameraDevice != null)
                    {
                        e.CameraDevice.TransferFile(e.Handle, fullPath);
                        
                        // Release camera resources
                        try
                        {
                            e.CameraDevice.ReleaseResurce(e.Handle);
                        }
                        catch { }
                        
                        // Add to captured images - must create BitmapImage on UI thread
                        if (File.Exists(fullPath))
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.UriSource = new Uri(fullPath);
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.EndInit();
                                    bitmap.Freeze(); // Make it thread-safe
                                    
                                    CapturedImages.Add(bitmap);
                                    LastCapturedImage = bitmap;
                                    StatusMessage = $"Photo saved: {fileName}";
                                }
                                catch (Exception uiEx)
                                {
                                    Log.Error($"PhotoboothTouchModernViewModel: UI update error: {uiEx.Message}", uiEx);
                                    StatusMessage = $"Photo saved but UI update failed: {fileName}";
                                }
                            });
                        }
                        else
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusMessage = $"Photo transfer failed - file not found";
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = $"Failed to process photographer mode capture: {ex.Message}";
                    });
                    Log.Error($"PhotoboothTouchModernViewModel: Photographer mode error: {ex.Message}", ex);
                }
            });
        }
        
        public void Cleanup()
        {
            // Unsubscribe from photographer mode events
            if (_deviceManager != null)
            {
                _deviceManager.PhotoCaptured -= OnPhotographerModeCapture;
            }
            
            // Unsubscribe from camera events
            if (_selectedCamera != null)
            {
                _selectedCamera.PhotoCaptured -= OnPhotographerModeCapture;
            }
            
            StopLiveView();
            _liveViewTimer?.Stop();
            _moduleManager.Cleanup();
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    

    public class ModuleButtonViewModel : INotifyPropertyChanged
    {
        private bool _isActive;
        
        public string ModuleName { get; set; }
        public string IconPath { get; set; }
        public string IconText { get; set; }
        public bool IsEnabled { get; set; }
        public ICommand Command { get; set; }
        public object CommandParameter { get; set; }
        
        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;
        
        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        
        public event EventHandler CanExecuteChanged
        {
            add { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }
        
        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
    }
    
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;
        
        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        
        public event EventHandler CanExecuteChanged
        {
            add { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }
        
        public bool CanExecute(object parameter) => _canExecute?.Invoke((T)parameter) ?? true;
        public void Execute(object parameter) => _execute((T)parameter);
    }

}