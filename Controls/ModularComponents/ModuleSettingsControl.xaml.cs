using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Photobooth.Controls.ModularComponents
{
    public partial class ModuleSettingsControl : UserControl
    {
        private ObservableCollection<ModuleSettingsViewModel> _moduleSettings;
        
        public event EventHandler SettingsSaved;
        public event EventHandler SettingsCancelled;
        
        public ModuleSettingsControl()
        {
            InitializeComponent();
            LoadModuleSettings();
        }
        
        private void LoadModuleSettings()
        {
            _moduleSettings = new ObservableCollection<ModuleSettingsViewModel>();
            var moduleManager = ModuleManager.Instance;
            
            foreach (var module in moduleManager.Modules.Values)
            {
                var settingsVm = new ModuleSettingsViewModel
                {
                    Module = module,
                    ModuleName = module.ModuleName,
                    IconPath = module.IconPath,
                    IsEnabled = module.IsEnabled
                };
                
                // Set module-specific properties
                switch (module)
                {
                    case PhotoCaptureModule photoModule:
                        settingsVm.IsPhotoModule = true;
                        settingsVm.CountdownDuration = photoModule.CountdownDuration;
                        settingsVm.Description = "Capture single photos with countdown timer";
                        settingsVm.HasSettings = true;
                        break;
                        
                    case GifModule gifModule:
                        settingsVm.IsGifModule = true;
                        settingsVm.FrameCount = gifModule.FrameCount;
                        settingsVm.FrameDelay = gifModule.FrameDelayMs;
                        settingsVm.Description = "Create animated GIFs from multiple frames";
                        settingsVm.HasSettings = true;
                        break;
                        
                    case BoomerangModule boomerangModule:
                        settingsVm.IsBoomerangModule = true;
                        settingsVm.FrameCount = boomerangModule.FrameCount;
                        settingsVm.PlaybackDelay = boomerangModule.PlaybackDelayMs;
                        settingsVm.Description = "Create forward-backward looping videos";
                        settingsVm.HasSettings = true;
                        break;
                        
                    case VideoModule videoModule:
                        settingsVm.IsVideoModule = true;
                        settingsVm.MaxRecordingDuration = videoModule.MaxRecordingDuration;
                        settingsVm.Description = "Record video clips with audio";
                        settingsVm.HasSettings = true;
                        break;
                        
                    case PhotoPrintModule:
                        settingsVm.Description = "Print photos directly from the booth";
                        settingsVm.HasSettings = false;
                        break;
                        
                    default:
                        settingsVm.Description = "Custom module";
                        settingsVm.HasSettings = false;
                        break;
                }
                
                _moduleSettings.Add(settingsVm);
            }
            
            ModulesList.ItemsSource = _moduleSettings;
        }
        
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Apply settings to modules
            foreach (var settingsVm in _moduleSettings)
            {
                settingsVm.Module.IsEnabled = settingsVm.IsEnabled;
                
                switch (settingsVm.Module)
                {
                    case PhotoCaptureModule photoModule:
                        photoModule.CountdownDuration = settingsVm.CountdownDuration;
                        break;
                        
                    case GifModule gifModule:
                        gifModule.FrameCount = settingsVm.FrameCount;
                        gifModule.FrameDelayMs = settingsVm.FrameDelay;
                        break;
                        
                    case BoomerangModule boomerangModule:
                        boomerangModule.FrameCount = settingsVm.FrameCount;
                        boomerangModule.PlaybackDelayMs = settingsVm.PlaybackDelay;
                        break;
                        
                    case VideoModule videoModule:
                        videoModule.MaxRecordingDuration = settingsVm.MaxRecordingDuration;
                        break;
                }
            }
            
            // Save to persistent storage
            ModuleManager.Instance.SaveModuleSettings();
            
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsCancelled?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public class ModuleSettingsViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private int _countdownDuration = 3;
        private int _frameCount = 4;
        private int _frameDelay = 500;
        private int _playbackDelay = 50;
        private int _maxRecordingDuration = 15;
        
        public IPhotoboothModule Module { get; set; }
        public string ModuleName { get; set; }
        public string IconPath { get; set; }
        public string Description { get; set; }
        public bool HasSettings { get; set; }
        
        // Module type flags
        public bool IsPhotoModule { get; set; }
        public bool IsGifModule { get; set; }
        public bool IsBoomerangModule { get; set; }
        public bool IsVideoModule { get; set; }
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
        
        public int CountdownDuration
        {
            get => _countdownDuration;
            set
            {
                _countdownDuration = value;
                OnPropertyChanged();
            }
        }
        
        public int FrameCount
        {
            get => _frameCount;
            set
            {
                _frameCount = value;
                OnPropertyChanged();
            }
        }
        
        public int FrameDelay
        {
            get => _frameDelay;
            set
            {
                _frameDelay = value;
                OnPropertyChanged();
            }
        }
        
        public int PlaybackDelay
        {
            get => _playbackDelay;
            set
            {
                _playbackDelay = value;
                OnPropertyChanged();
            }
        }
        
        public int MaxRecordingDuration
        {
            get => _maxRecordingDuration;
            set
            {
                _maxRecordingDuration = value;
                OnPropertyChanged();
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}