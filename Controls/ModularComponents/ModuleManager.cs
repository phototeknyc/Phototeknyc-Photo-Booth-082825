using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CameraControl.Devices;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Photobooth.Controls.ModularComponents
{
    public class ModuleManager : INotifyPropertyChanged
    {
        private static ModuleManager _instance;
        private Dictionary<string, IPhotoboothModule> _modules;
        private IPhotoboothModule _activeModule;
        private ICameraDevice _camera;
        private string _outputFolder;
        private bool _isProcessing;
        
        public static ModuleManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ModuleManager();
                }
                return _instance;
            }
        }
        
        public IReadOnlyDictionary<string, IPhotoboothModule> Modules => _modules;
        
        public IPhotoboothModule ActiveModule
        {
            get => _activeModule;
            private set
            {
                if (_activeModule != value)
                {
                    _activeModule = value;
                    OnPropertyChanged();
                    ActiveModuleChanged?.Invoke(this, _activeModule);
                }
            }
        }
        
        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (_isProcessing != value)
                {
                    _isProcessing = value;
                    OnPropertyChanged();
                }
            }
        }
        
        // Alias for ActiveModule for ViewModel compatibility
        public IPhotoboothModule CurrentModule => ActiveModule;
        
        // Status message for current processing state
        public string ProcessingStatus => IsProcessing ? $"Processing with {ActiveModule?.ModuleName}..." : "Ready";
        
        public event EventHandler<IPhotoboothModule> ActiveModuleChanged;
        public event EventHandler<ModuleCaptureEventArgs> AnyCaptureCompleted;
        public event EventHandler<ModuleStatusEventArgs> AnyStatusChanged;
        public event PropertyChangedEventHandler PropertyChanged;
        
        private ModuleManager()
        {
            _modules = new Dictionary<string, IPhotoboothModule>();
            InitializeDefaultModules();
        }
        
        private void InitializeDefaultModules()
        {
            // Register default modules
            RegisterModule(new PhotoCaptureModule());
            // RegisterModule(new PhotoPrintModule()); // Temporarily commented out
            RegisterModule(new GifModule());
            RegisterModule(new BoomerangModule());
            RegisterModule(new VideoModule());
        }
        
        public void Initialize(ICameraDevice camera, string outputFolder)
        {
            _camera = camera;
            _outputFolder = outputFolder;
            
            foreach (var module in _modules.Values)
            {
                module.Initialize(camera, outputFolder);
            }
        }
        
        public void RegisterModule(IPhotoboothModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            
            if (_modules.ContainsKey(module.ModuleName))
            {
                UnregisterModule(module.ModuleName);
            }
            
            _modules[module.ModuleName] = module;
            
            // Subscribe to module events
            module.CaptureCompleted += Module_CaptureCompleted;
            module.StatusChanged += Module_StatusChanged;
            
            // Initialize if camera is already set
            if (_camera != null && _outputFolder != null)
            {
                module.Initialize(_camera, _outputFolder);
            }
        }
        
        public void UnregisterModule(string moduleName)
        {
            if (_modules.TryGetValue(moduleName, out var module))
            {
                if (_activeModule == module)
                {
                    _ = StopActiveModule();
                }
                
                module.CaptureCompleted -= Module_CaptureCompleted;
                module.StatusChanged -= Module_StatusChanged;
                module.Cleanup();
                
                _modules.Remove(moduleName);
            }
        }
        
        public IPhotoboothModule GetModule(string moduleName)
        {
            return _modules.TryGetValue(moduleName, out var module) ? module : null;
        }
        
        public T GetModule<T>() where T : class, IPhotoboothModule
        {
            return _modules.Values.OfType<T>().FirstOrDefault();
        }
        
        public async Task<bool> StartModule(string moduleName)
        {
            if (IsProcessing) return false;
            
            if (!_modules.TryGetValue(moduleName, out var module))
            {
                return false;
            }
            
            if (!module.IsEnabled)
            {
                return false;
            }
            
            // Stop any active module first
            if (_activeModule != null && _activeModule.IsActive)
            {
                await StopActiveModule();
            }
            
            IsProcessing = true;
            ActiveModule = module;
            
            try
            {
                await module.StartCapture();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start module {moduleName}: {ex.Message}");
                IsProcessing = false;
                ActiveModule = null;
                return false;
            }
        }
        
        public async Task<bool> StopActiveModule()
        {
            if (_activeModule == null) return false;
            
            try
            {
                await _activeModule.StopCapture();
                ActiveModule = null;
                IsProcessing = false;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to stop module: {ex.Message}");
                IsProcessing = false;
                return false;
            }
        }
        
        public void UpdateCamera(ICameraDevice camera)
        {
            _camera = camera;
            
            foreach (var module in _modules.Values)
            {
                module.Initialize(camera, _outputFolder);
            }
        }
        
        public void UpdateOutputFolder(string outputFolder)
        {
            _outputFolder = outputFolder;
            
            foreach (var module in _modules.Values)
            {
                module.Initialize(_camera, outputFolder);
            }
        }
        
        public List<IPhotoboothModule> GetEnabledModules()
        {
            return _modules.Values.Where(m => m.IsEnabled).ToList();
        }
        
        public void LoadModuleSettings()
        {
            // Load module enabled states from settings
            foreach (var module in _modules.Values)
            {
                try
                {
                    string settingKey = $"Module_{module.ModuleName}_Enabled";
                    
                    // Check if the setting exists before accessing it
                    var settingsProperties = Properties.Settings.Default.Properties;
                    if (settingsProperties[settingKey] != null)
                    {
                        module.IsEnabled = Properties.Settings.Default[settingKey] as bool? ?? true;
                    }
                    else
                    {
                        // Setting doesn't exist, default to enabled
                        module.IsEnabled = true;
                    }
                }
                catch (Exception)
                {
                    // If any error occurs, default to enabled
                    module.IsEnabled = true;
                }
            }
        }
        
        public void SaveModuleSettings()
        {
            // Save module enabled states to settings
            foreach (var module in _modules.Values)
            {
                try
                {
                    string settingKey = $"Module_{module.ModuleName}_Enabled";
                    
                    // Check if the setting exists before trying to save it
                    var settingsProperties = Properties.Settings.Default.Properties;
                    if (settingsProperties[settingKey] != null)
                    {
                        Properties.Settings.Default[settingKey] = module.IsEnabled;
                    }
                    // If setting doesn't exist, we can't save it without defining it first
                    // This is acceptable since LoadModuleSettings will default to enabled
                }
                catch (Exception)
                {
                    // Silently ignore save errors for undefined settings
                }
            }
            
            try
            {
                Properties.Settings.Default.Save();
            }
            catch (Exception)
            {
                // Silently ignore save errors
            }
        }
        
        private void Module_CaptureCompleted(object sender, ModuleCaptureEventArgs e)
        {
            IsProcessing = false;
            AnyCaptureCompleted?.Invoke(sender, e);
        }
        
        private void Module_StatusChanged(object sender, ModuleStatusEventArgs e)
        {
            AnyStatusChanged?.Invoke(sender, e);
        }
        
        public void Cleanup()
        {
            foreach (var module in _modules.Values)
            {
                module.CaptureCompleted -= Module_CaptureCompleted;
                module.StatusChanged -= Module_StatusChanged;
                module.Cleanup();
            }
            
            _modules.Clear();
            _activeModule = null;
            _camera = null;
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class ModuleSettings
    {
        public Dictionary<string, bool> ModuleEnabledStates { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, object> ModuleConfigurations { get; set; } = new Dictionary<string, object>();
        
        public bool IsModuleEnabled(string moduleName)
        {
            return ModuleEnabledStates.TryGetValue(moduleName, out bool enabled) ? enabled : true;
        }
        
        public void SetModuleEnabled(string moduleName, bool enabled)
        {
            ModuleEnabledStates[moduleName] = enabled;
        }
        
        public T GetModuleConfig<T>(string moduleName, string key, T defaultValue = default)
        {
            string configKey = $"{moduleName}_{key}";
            if (ModuleConfigurations.TryGetValue(configKey, out object value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }
        
        public void SetModuleConfig(string moduleName, string key, object value)
        {
            string configKey = $"{moduleName}_{key}";
            ModuleConfigurations[configKey] = value;
        }
    }
}