using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Configuration;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles synchronization of settings across multiple photo booths
    /// </summary>
    public class SettingsSyncService
    {
        private static SettingsSyncService _instance;
        public static SettingsSyncService Instance => _instance ?? (_instance = new SettingsSyncService());
        
        private readonly string _settingsDirectory;
        private readonly string _manifestPath;
        private Dictionary<string, SettingManifestEntry> _localManifest;
        
        public event EventHandler<SettingsSyncProgressEventArgs> SyncProgress;
        public event EventHandler<SettingsSyncCompletedEventArgs> SyncCompleted;
        
        private SettingsSyncService()
        {
            _settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhotoBooth", "Settings"
            );
            
            _manifestPath = Path.Combine(_settingsDirectory, "settings_manifest.json");
            
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
            }
            
            LoadManifest();
        }
        
        /// <summary>
        /// Setting manifest entry
        /// </summary>
        public class SettingManifestEntry
        {
            public string SettingKey { get; set; }
            public string Category { get; set; }
            public object Value { get; set; }
            public string ValueType { get; set; }
            public string Hash { get; set; }
            public DateTime LastModified { get; set; }
            public bool IsSyncEnabled { get; set; }
            public int Priority { get; set; }
        }
        
        /// <summary>
        /// Settings categories for organization
        /// </summary>
        public enum SettingCategory
        {
            General,
            Camera,
            Printing,
            Sharing,
            Gallery,
            Templates,
            Effects,
            Advanced,
            CloudSync,
            UI,
            Events
        }
        
        /// <summary>
        /// Get all syncable settings
        /// </summary>
        public async Task<List<SettingManifestEntry>> GetLocalSettingsAsync()
        {
            var settings = new List<SettingManifestEntry>();
            
            try
            {
                await Task.Run(() =>
                {
                    // Get all application settings
                    var appSettings = Properties.Settings.Default;
                    
                    // Define which settings should be synced
                    var syncableSettings = GetSyncableSettingsList();
                    
                    foreach (var settingDef in syncableSettings)
                    {
                        try
                        {
                            var value = appSettings[settingDef.Key];
                            var entry = CreateSettingEntry(settingDef.Key, value, settingDef.Category, settingDef.Priority);
                            if (entry != null)
                            {
                                settings.Add(entry);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"SettingsSyncService: Skipping setting {settingDef.Key}: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error getting local settings: {ex.Message}");
            }
            
            return settings;
        }
        
        /// <summary>
        /// Define which settings should be synchronized
        /// </summary>
        private List<(string Key, SettingCategory Category, int Priority)> GetSyncableSettingsList()
        {
            return new List<(string, SettingCategory, int)>
            {
                // Camera Settings
                ("DefaultCameraMode", SettingCategory.Camera, 1),
                ("LiveViewFrameRate", SettingCategory.Camera, 2),
                ("CameraConnectionTimeout", SettingCategory.Camera, 3),
                ("EnableLiveView", SettingCategory.Camera, 4),
                ("AutoReconnectCamera", SettingCategory.Camera, 5),
                
                // Printing Settings
                ("DefaultPrinterName", SettingCategory.Printing, 1),
                ("PrintCopies", SettingCategory.Printing, 2),
                ("PrinterDPI", SettingCategory.Printing, 3),
                ("EnableAutoPrint", SettingCategory.Printing, 4),
                ("PrintDelay", SettingCategory.Printing, 5),
                
                // Sharing Settings
                ("EnableCloudUpload", SettingCategory.Sharing, 1),
                ("CloudUploadUrl", SettingCategory.Sharing, 2),
                ("EnableQRCode", SettingCategory.Sharing, 3),
                ("EnableSMS", SettingCategory.Sharing, 4),
                ("EnableEmail", SettingCategory.Sharing, 5),
                ("GalleryBaseUrl", SettingCategory.Sharing, 6),
                
                // Gallery Settings
                ("GalleryColumns", SettingCategory.Gallery, 1),
                ("GalleryRows", SettingCategory.Gallery, 2),
                ("GalleryAutoRefresh", SettingCategory.Gallery, 3),
                ("GalleryRefreshInterval", SettingCategory.Gallery, 4),
                
                // Template Settings
                ("DefaultTemplateId", SettingCategory.Templates, 1),
                ("TemplateAutoSelect", SettingCategory.Templates, 2),
                ("EnableTemplatePreview", SettingCategory.Templates, 3),
                
                // Effects Settings
                ("EnableFilters", SettingCategory.Effects, 1),
                ("DefaultFilter", SettingCategory.Effects, 2),
                ("EnableGreenScreen", SettingCategory.Effects, 3),
                ("ChromaKeyColor", SettingCategory.Effects, 4),
                ("ChromaKeyTolerance", SettingCategory.Effects, 5),
                
                // UI Settings
                ("UILanguage", SettingCategory.UI, 1),
                ("UITheme", SettingCategory.UI, 2),
                ("ShowCountdown", SettingCategory.UI, 3),
                ("CountdownDuration", SettingCategory.UI, 4),
                ("EnableTouchMode", SettingCategory.UI, 5),
                ("IdleTimeout", SettingCategory.UI, 6),
                
                // Event Settings
                ("RequireEventSelection", SettingCategory.Events, 1),
                ("DefaultEventId", SettingCategory.Events, 2),
                ("EventAutoCreate", SettingCategory.Events, 3),
                
                // Video Settings
                ("EnableVideoMode", SettingCategory.Camera, 10),
                ("VideoRecordingDuration", SettingCategory.Camera, 11),
                ("EnableVideoCompression", SettingCategory.Camera, 12),
                ("VideoCompressionQuality", SettingCategory.Camera, 13),
                ("VideoUploadResolution", SettingCategory.Camera, 14),
                
                // Advanced Settings
                ("DebugMode", SettingCategory.Advanced, 1),
                ("LogLevel", SettingCategory.Advanced, 2),
                ("DatabaseCleanupDays", SettingCategory.Advanced, 3),
                ("CacheDirectory", SettingCategory.Advanced, 4),
                ("MaxCacheSize", SettingCategory.Advanced, 5)
            };
        }
        
        /// <summary>
        /// Create setting entry for manifest
        /// </summary>
        private SettingManifestEntry CreateSettingEntry(string key, object value, SettingCategory category, int priority)
        {
            try
            {
                // Skip null values
                if (value == null)
                    return null;
                
                // Create hash of the value
                var valueString = JsonConvert.SerializeObject(value);
                var hash = CalculateHash(valueString);
                
                return new SettingManifestEntry
                {
                    SettingKey = key,
                    Category = category.ToString(),
                    Value = value,
                    ValueType = value.GetType().Name,
                    Hash = hash,
                    LastModified = DateTime.Now,
                    IsSyncEnabled = true,
                    Priority = priority
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error creating setting entry for {key}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Calculate hash of setting value
        /// </summary>
        private string CalculateHash(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        
        /// <summary>
        /// Compare local and remote settings
        /// </summary>
        public SyncDifferences CompareSettings(List<SettingManifestEntry> localSettings, List<SettingManifestEntry> remoteSettings)
        {
            var differences = new SyncDifferences();
            
            // Build lookup dictionaries
            var localDict = localSettings.ToDictionary(s => s.SettingKey);
            var remoteDict = remoteSettings.ToDictionary(s => s.SettingKey);
            
            // Find settings to upload (local newer or remote missing)
            foreach (var local in localSettings)
            {
                if (!local.IsSyncEnabled)
                    continue;
                
                if (!remoteDict.ContainsKey(local.SettingKey))
                {
                    differences.ToUpload.Add(local);
                }
                else if (remoteDict[local.SettingKey].Hash != local.Hash)
                {
                    var remote = remoteDict[local.SettingKey];
                    
                    // Check which is newer
                    if (local.LastModified > remote.LastModified)
                    {
                        differences.ToUpload.Add(local);
                    }
                    else
                    {
                        differences.ToDownload.Add(remote);
                    }
                    
                    // Record conflict
                    differences.Conflicts.Add(new SyncConflict
                    {
                        ItemId = local.SettingKey,
                        LocalVersion = local,
                        RemoteVersion = remote
                    });
                }
            }
            
            // Find settings to download (remote only)
            foreach (var remote in remoteSettings)
            {
                if (!localDict.ContainsKey(remote.SettingKey))
                {
                    differences.ToDownload.Add(remote);
                }
            }
            
            return differences;
        }
        
        /// <summary>
        /// Apply remote setting to local configuration
        /// </summary>
        public async Task<bool> ApplyRemoteSettingAsync(SettingManifestEntry remoteSetting)
        {
            try
            {
                return await Task.Run(() =>
                {
                    var appSettings = Properties.Settings.Default;
                    
                    // Convert value to appropriate type
                    var targetType = appSettings.Properties[remoteSetting.SettingKey]?.PropertyType;
                    if (targetType == null)
                    {
                        Debug.WriteLine($"SettingsSyncService: Setting {remoteSetting.SettingKey} not found");
                        return false;
                    }
                    
                    object convertedValue = ConvertSettingValue(remoteSetting.Value, targetType);
                    
                    // Apply the setting
                    appSettings[remoteSetting.SettingKey] = convertedValue;
                    appSettings.Save();
                    
                    // Update local manifest
                    _localManifest[remoteSetting.SettingKey] = remoteSetting;
                    SaveManifest();
                    
                    OnSyncProgress(new SettingsSyncProgressEventArgs
                    {
                        Message = $"Applied setting: {remoteSetting.SettingKey}",
                        Progress = 100,
                        CurrentItem = remoteSetting.SettingKey
                    });
                    
                    return true;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error applying remote setting {remoteSetting.SettingKey}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Convert setting value to appropriate type
        /// </summary>
        private object ConvertSettingValue(object value, Type targetType)
        {
            try
            {
                // Handle JSON deserialized values
                if (value is Newtonsoft.Json.Linq.JValue jValue)
                {
                    value = jValue.Value;
                }
                
                // Handle nullable types
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    if (value == null)
                        return null;
                    
                    targetType = Nullable.GetUnderlyingType(targetType);
                }
                
                // Convert to target type
                if (targetType.IsEnum)
                {
                    return Enum.Parse(targetType, value.ToString());
                }
                
                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error converting value for type {targetType}: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Export current settings for sync
        /// </summary>
        public async Task<string> ExportSettingsAsync()
        {
            try
            {
                var settings = await GetLocalSettingsAsync();
                var export = new SettingsExport
                {
                    ExportDate = DateTime.Now,
                    Version = "1.0",
                    DeviceId = GetDeviceId(),
                    Settings = settings
                };
                
                return JsonConvert.SerializeObject(export, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error exporting settings: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Import settings from sync
        /// </summary>
        public async Task<bool> ImportSettingsAsync(string settingsJson)
        {
            try
            {
                var export = JsonConvert.DeserializeObject<SettingsExport>(settingsJson);
                if (export == null || export.Settings == null)
                    return false;
                
                foreach (var setting in export.Settings)
                {
                    await ApplyRemoteSettingAsync(setting);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error importing settings: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get unique device identifier
        /// </summary>
        private string GetDeviceId()
        {
            try
            {
                // Try to get from settings first
                var deviceId = Properties.Settings.Default.DeviceId;
                if (string.IsNullOrEmpty(deviceId))
                {
                    // Generate new device ID
                    deviceId = Guid.NewGuid().ToString();
                    Properties.Settings.Default.DeviceId = deviceId;
                    Properties.Settings.Default.Save();
                }
                return deviceId;
            }
            catch
            {
                return Environment.MachineName;
            }
        }
        
        /// <summary>
        /// Load manifest from disk
        /// </summary>
        private void LoadManifest()
        {
            try
            {
                if (File.Exists(_manifestPath))
                {
                    var json = File.ReadAllText(_manifestPath);
                    _localManifest = JsonConvert.DeserializeObject<Dictionary<string, SettingManifestEntry>>(json) 
                        ?? new Dictionary<string, SettingManifestEntry>();
                }
                else
                {
                    _localManifest = new Dictionary<string, SettingManifestEntry>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error loading manifest: {ex.Message}");
                _localManifest = new Dictionary<string, SettingManifestEntry>();
            }
        }
        
        /// <summary>
        /// Save manifest to disk
        /// </summary>
        private void SaveManifest()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_localManifest, Formatting.Indented);
                File.WriteAllText(_manifestPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsSyncService: Error saving manifest: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Settings export container
        /// </summary>
        public class SettingsExport
        {
            public DateTime ExportDate { get; set; }
            public string Version { get; set; }
            public string DeviceId { get; set; }
            public List<SettingManifestEntry> Settings { get; set; }
        }
        
        /// <summary>
        /// Sync differences container
        /// </summary>
        public class SyncDifferences
        {
            public List<SettingManifestEntry> ToUpload { get; set; } = new List<SettingManifestEntry>();
            public List<SettingManifestEntry> ToDownload { get; set; } = new List<SettingManifestEntry>();
            public List<SyncConflict> Conflicts { get; set; } = new List<SyncConflict>();
        }
        
        /// <summary>
        /// Sync conflict information
        /// </summary>
        public class SyncConflict
        {
            public string ItemId { get; set; }
            public object LocalVersion { get; set; }
            public object RemoteVersion { get; set; }
        }
        
        protected virtual void OnSyncProgress(SettingsSyncProgressEventArgs e)
        {
            SyncProgress?.Invoke(this, e);
        }
        
        protected virtual void OnSyncCompleted(SettingsSyncCompletedEventArgs e)
        {
            SyncCompleted?.Invoke(this, e);
        }
    }
    
    /// <summary>
    /// Settings sync progress event arguments
    /// </summary>
    public class SettingsSyncProgressEventArgs : EventArgs
    {
        public string Message { get; set; }
        public int Progress { get; set; }
        public string CurrentItem { get; set; }
    }
    
    /// <summary>
    /// Settings sync completed event arguments
    /// </summary>
    public class SettingsSyncCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ItemsSynced { get; set; }
        public List<string> Errors { get; set; }
    }
}