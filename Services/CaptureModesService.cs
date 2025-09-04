using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CameraControl.Devices.Classes;

namespace Photobooth.Services
{
    // CaptureMode enum is defined in PhotoboothModulesConfig.cs
    
    public class CaptureModeInfo
    {
        public CaptureMode Mode { get; set; }
        public string Name { get; set; }
        public string IconPath { get; set; }
        public string Description { get; set; }
        public bool IsEnabled { get; set; }
        public int PhotoCount { get; set; } // Number of photos for this mode (e.g., 4 for GIF)
        public double CaptureInterval { get; set; } // Seconds between captures for burst modes
        public bool RequiresVideo { get; set; } // True for video, boomerang modes
    }

    public class CaptureModesService : INotifyPropertyChanged
    {
        private static CaptureModesService _instance;
        public static CaptureModesService Instance => _instance ?? (_instance = new CaptureModesService());

        private CaptureMode _currentMode = CaptureMode.Photo;
        private Dictionary<CaptureMode, CaptureModeInfo> _modeConfigurations;
        private bool _isEnabled;

        public event EventHandler<CaptureMode> CaptureModeChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public CaptureModesService()
        {
            InitializeModeConfigurations();
            LoadSettings();
        }

        #region Properties

        public CaptureMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode != value)
                {
                    var oldMode = _currentMode;
                    _currentMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentModeInfo));
                    CaptureModeChanged?.Invoke(this, value);
                    Log.Debug($"CaptureModesService: Mode changed from {oldMode} to {value}");
                }
            }
        }

        public CaptureModeInfo CurrentModeInfo => _modeConfigurations[_currentMode];

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public List<CaptureModeInfo> EnabledModes
        {
            get
            {
                return _modeConfigurations.Values
                    .Where(m => m.IsEnabled)
                    .OrderBy(m => GetModeOrder(m.Mode))
                    .ToList();
            }
        }

        public bool HasMultipleModes => EnabledModes.Count > 1;

        #endregion

        #region Initialization

        private void InitializeModeConfigurations()
        {
            _modeConfigurations = new Dictionary<CaptureMode, CaptureModeInfo>
            {
                [CaptureMode.Photo] = new CaptureModeInfo
                {
                    Mode = CaptureMode.Photo,
                    Name = "Photo",
                    IconPath = "/Resources/Icons/camera.png",
                    Description = "Take a single photo",
                    PhotoCount = 1,
                    RequiresVideo = false
                },
                [CaptureMode.Video] = new CaptureModeInfo
                {
                    Mode = CaptureMode.Video,
                    Name = "Video",
                    IconPath = "/Resources/Icons/video.png",
                    Description = "Record a video clip",
                    PhotoCount = 0,
                    RequiresVideo = true
                },
                [CaptureMode.Boomerang] = new CaptureModeInfo
                {
                    Mode = CaptureMode.Boomerang,
                    Name = "Boomerang",
                    IconPath = "/Resources/Icons/boomerang.png",
                    Description = "Create a looping video",
                    PhotoCount = 10,
                    CaptureInterval = 0.2,
                    RequiresVideo = false
                },
                [CaptureMode.Gif] = new CaptureModeInfo
                {
                    Mode = CaptureMode.Gif,
                    Name = "GIF",
                    IconPath = "/Resources/Icons/gif.png",
                    Description = "Create an animated GIF",
                    PhotoCount = 4,
                    CaptureInterval = 0.5,
                    RequiresVideo = false
                },
                [CaptureMode.GreenScreen] = new CaptureModeInfo
                {
                    Mode = CaptureMode.GreenScreen,
                    Name = "Green Screen",
                    IconPath = "/Resources/Icons/greenscreen.png",
                    Description = "Photo with virtual background",
                    PhotoCount = 1,
                    RequiresVideo = false
                },
                [CaptureMode.AI] = new CaptureModeInfo
                {
                    Mode = CaptureMode.AI,
                    Name = "AI Photo",
                    IconPath = "/Resources/Icons/ai.png",
                    Description = "AI-enhanced photo",
                    PhotoCount = 1,
                    RequiresVideo = false
                },
                [CaptureMode.Flipbook] = new CaptureModeInfo
                {
                    Mode = CaptureMode.Flipbook,
                    Name = "Flipbook",
                    IconPath = "/Resources/Icons/flipbook.png",
                    Description = "Create a printable flipbook",
                    PhotoCount = 8,
                    CaptureInterval = 0.3,
                    RequiresVideo = false
                }
            };
        }

        private int GetModeOrder(CaptureMode mode)
        {
            switch (mode)
            {
                case CaptureMode.Photo: return 0;
                case CaptureMode.Video: return 1;
                case CaptureMode.Boomerang: return 2;
                case CaptureMode.Gif: return 3;
                case CaptureMode.GreenScreen: return 4;
                case CaptureMode.AI: return 5;
                case CaptureMode.Flipbook: return 6;
                default: return 99;
            }
        }

        #endregion

        #region Settings Management

        private void LoadSettings()
        {
            var settings = Properties.Settings.Default;
            
            _isEnabled = settings.CaptureModesEnabled;
            
            // Load enabled state for each mode
            _modeConfigurations[CaptureMode.Photo].IsEnabled = settings.CaptureModePhoto;
            _modeConfigurations[CaptureMode.Video].IsEnabled = settings.CaptureModeVideo;
            _modeConfigurations[CaptureMode.Boomerang].IsEnabled = settings.CaptureModeBoomerang;
            _modeConfigurations[CaptureMode.Gif].IsEnabled = settings.CaptureModeGif;
            _modeConfigurations[CaptureMode.GreenScreen].IsEnabled = settings.CaptureModeGreenScreen;
            _modeConfigurations[CaptureMode.AI].IsEnabled = settings.CaptureModeAI;
            _modeConfigurations[CaptureMode.Flipbook].IsEnabled = settings.CaptureModeFlipbook;

            // Load default mode
            if (Enum.TryParse<CaptureMode>(settings.DefaultCaptureMode, out var defaultMode))
            {
                _currentMode = defaultMode;
            }
            
            Log.Debug($"CaptureModesService: Loaded settings - Enabled: {_isEnabled}, Default Mode: {_currentMode}");
        }

        public void SaveSettings()
        {
            var settings = Properties.Settings.Default;
            
            settings.CaptureModesEnabled = _isEnabled;
            
            // Save enabled state for each mode
            settings.CaptureModePhoto = _modeConfigurations[CaptureMode.Photo].IsEnabled;
            settings.CaptureModeVideo = _modeConfigurations[CaptureMode.Video].IsEnabled;
            settings.CaptureModeBoomerang = _modeConfigurations[CaptureMode.Boomerang].IsEnabled;
            settings.CaptureModeGif = _modeConfigurations[CaptureMode.Gif].IsEnabled;
            settings.CaptureModeGreenScreen = _modeConfigurations[CaptureMode.GreenScreen].IsEnabled;
            settings.CaptureModeAI = _modeConfigurations[CaptureMode.AI].IsEnabled;
            settings.CaptureModeFlipbook = _modeConfigurations[CaptureMode.Flipbook].IsEnabled;
            
            settings.DefaultCaptureMode = _currentMode.ToString();
            
            settings.Save();
            
            Log.Debug("CaptureModesService: Settings saved");
        }

        #endregion

        #region Mode Management

        public void SetModeEnabled(CaptureMode mode, bool enabled)
        {
            if (_modeConfigurations.ContainsKey(mode))
            {
                _modeConfigurations[mode].IsEnabled = enabled;
                OnPropertyChanged(nameof(EnabledModes));
                OnPropertyChanged(nameof(HasMultipleModes));
                SaveSettings();
                
                // If disabling current mode, switch to first available mode
                if (!enabled && _currentMode == mode)
                {
                    var firstEnabled = EnabledModes.FirstOrDefault();
                    if (firstEnabled != null)
                    {
                        CurrentMode = firstEnabled.Mode;
                    }
                }
            }
        }

        public bool IsModeEnabled(CaptureMode mode)
        {
            return _modeConfigurations.ContainsKey(mode) && _modeConfigurations[mode].IsEnabled;
        }

        public async Task<bool> StartCaptureSession(CaptureMode mode)
        {
            if (!IsModeEnabled(mode))
            {
                Log.Warning($"CaptureModesService: Attempted to start disabled mode {mode}");
                return false;
            }

            CurrentMode = mode;
            var modeInfo = _modeConfigurations[mode];
            
            Log.Debug($"CaptureModesService: Starting {mode} session - Photos: {modeInfo.PhotoCount}, Video: {modeInfo.RequiresVideo}");
            
            // Mode-specific initialization
            switch (mode)
            {
                case CaptureMode.Video:
                    return await StartVideoCapture();
                    
                case CaptureMode.Boomerang:
                    return await StartBoomerangCapture();
                    
                case CaptureMode.Gif:
                    return await StartGifCapture();
                    
                case CaptureMode.GreenScreen:
                    return await StartGreenScreenCapture();
                    
                case CaptureMode.AI:
                    return await StartAICapture();
                    
                case CaptureMode.Flipbook:
                    return await StartFlipbookCapture();
                    
                case CaptureMode.Photo:
                default:
                    return await StartPhotoCapture();
            }
        }

        #endregion

        #region Capture Mode Implementations

        private async Task<bool> StartPhotoCapture()
        {
            // Standard photo capture - handled by existing PhotoboothWorkflowService
            return true;
        }

        private async Task<bool> StartVideoCapture()
        {
            // Video recording - integrate with VideoRecordingService
            var videoService = VideoRecordingService.Instance;
            if (videoService != null)
            {
                await videoService.StartRecording();
                return true;
            }
            return false;
        }

        private async Task<bool> StartBoomerangCapture()
        {
            // Boomerang - rapid burst of photos that loop
            // Will capture 10 photos quickly and create a looping video
            return true;
        }

        private async Task<bool> StartGifCapture()
        {
            // GIF creation - typically 4 photos with delay
            // Uses existing GIF generation service
            return true;
        }

        private async Task<bool> StartGreenScreenCapture()
        {
            // Green screen - single photo with background removal
            // Requires green screen processing service
            return true;
        }

        private async Task<bool> StartAICapture()
        {
            // AI photo - single photo with AI enhancement
            // Requires AI processing service
            return true;
        }

        private async Task<bool> StartFlipbookCapture()
        {
            // Flipbook - 8+ rapid photos for printed flipbook
            return true;
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}