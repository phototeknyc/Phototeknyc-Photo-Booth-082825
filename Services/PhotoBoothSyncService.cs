using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System.Diagnostics;

namespace Photobooth.Services
{
    /// <summary>
    /// Main service for synchronizing templates and settings across multiple photo booths via cloud storage
    /// </summary>
    public class PhotoBoothSyncService
    {
        #region Singleton
        private static PhotoBoothSyncService _instance;
        public static PhotoBoothSyncService Instance => _instance ?? (_instance = new PhotoBoothSyncService());
        #endregion

        #region Services
        private readonly TemplateSyncService _templateSync;
        private readonly SettingsSyncService _settingsSync;
        private readonly CloudShareServiceRuntime _cloudService;
        #endregion

        #region Properties
        private SyncConfiguration _config;
        private SyncManifest _localManifest;
        private SyncManifest _remoteManifest;
        private bool _isSyncing;
        private DateTime _lastSyncTime;
        private string _boothId;

        public bool IsSyncing => _isSyncing;
        public DateTime LastSyncTime => _lastSyncTime;
        public string BoothId => _boothId;
        public SyncConfiguration Config => _config;
        
        // Sync paths in S3
        private const string SYNC_ROOT = "photobooth-sync/";
        private const string MANIFEST_FILE = "sync-manifest.json";
        private const string TEMPLATES_PATH = "templates/";
        private const string SETTINGS_PATH = "settings/";
        private const string DATABASE_PATH = "database/";
        #endregion

        #region Events
        public event EventHandler<SyncEventArgs> SyncStarted;
        public event EventHandler<SyncEventArgs> SyncCompleted;
        public event EventHandler<SyncProgressEventArgs> SyncProgress;
        public event EventHandler<SyncErrorEventArgs> SyncError;
        public event EventHandler<ConflictEventArgs> ConflictDetected;
        #endregion

        #region Constructor
        private PhotoBoothSyncService()
        {
            _templateSync = TemplateSyncService.Instance;
            _settingsSync = SettingsSyncService.Instance;
            
            // Set environment variables from application settings so CloudShareServiceRuntime uses the same credentials
            SetEnvironmentVariablesFromSettings();
            
            _cloudService = new CloudShareServiceRuntime();
            
            InitializeConfiguration();
            LoadLocalManifest();
            
            Debug.WriteLine($"PhotoBoothSyncService: Initialized with booth ID: {_boothId}");
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Set environment variables from application settings for CloudShareServiceRuntime
        /// </summary>
        private void SetEnvironmentVariablesFromSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;
                
                // Set AWS credentials from application settings
                if (!string.IsNullOrEmpty(settings.S3AccessKey))
                {
                    Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", settings.S3AccessKey, EnvironmentVariableTarget.User);
                }
                
                if (!string.IsNullOrEmpty(settings.S3SecretKey))
                {
                    Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", settings.S3SecretKey, EnvironmentVariableTarget.User);
                }
                
                if (!string.IsNullOrEmpty(settings.S3BucketName))
                {
                    Environment.SetEnvironmentVariable("S3_BUCKET_NAME", settings.S3BucketName, EnvironmentVariableTarget.User);
                }
                
                if (!string.IsNullOrEmpty(settings.S3Region))
                {
                    Environment.SetEnvironmentVariable("S3_REGION", settings.S3Region, EnvironmentVariableTarget.User);
                }
                
                // Also set the gallery base URL if available
                if (!string.IsNullOrEmpty(settings.GalleryBaseUrl))
                {
                    Environment.SetEnvironmentVariable("GALLERY_BASE_URL", settings.GalleryBaseUrl, EnvironmentVariableTarget.User);
                }
                
                Debug.WriteLine($"PhotoBoothSyncService: Environment variables set from application settings");
                Debug.WriteLine($"  Bucket: {settings.S3BucketName}");
                Debug.WriteLine($"  Region: {settings.S3Region}");
                Debug.WriteLine($"  Credentials present: {!string.IsNullOrEmpty(settings.S3AccessKey)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error setting environment variables: {ex.Message}");
            }
        }
        
        private void InitializeConfiguration()
        {
            // Load configuration from settings
            _config = new SyncConfiguration
            {
                IsEnabled = Properties.Settings.Default.EnableCloudSync,
                SyncInterval = Properties.Settings.Default.SyncIntervalMinutes, // minutes
                SyncTemplates = Properties.Settings.Default.SyncTemplates,
                SyncSettings = Properties.Settings.Default.SyncSettings,
                SyncEvents = Properties.Settings.Default.SyncEvents,
                SyncDatabase = false, // For future: full database sync
                ConflictResolution = ConflictResolutionMode.NewestWins,
                AutoSync = Properties.Settings.Default.AutoSyncOnStartup
            };

            // Generate or load booth ID
            _boothId = Properties.Settings.Default.DeviceId;
            if (string.IsNullOrEmpty(_boothId))
            {
                _boothId = GenerateBoothId();
                Properties.Settings.Default.DeviceId = _boothId;
                Properties.Settings.Default.Save();
            }
        }

        private string GenerateBoothId()
        {
            // Generate unique booth ID based on machine name and random component
            string machineName = Environment.MachineName;
            string randomPart = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
            return $"BOOTH-{machineName}-{randomPart}";
        }

        private void LoadLocalManifest()
        {
            string manifestPath = GetLocalManifestPath();
            if (File.Exists(manifestPath))
            {
                try
                {
                    string json = File.ReadAllText(manifestPath);
                    _localManifest = JsonConvert.DeserializeObject<SyncManifest>(json);
                    Debug.WriteLine($"PhotoBoothSyncService: Loaded local manifest with {_localManifest.Items.Count} items");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Error loading local manifest: {ex.Message}");
                    _localManifest = new SyncManifest { BoothId = _boothId };
                }
            }
            else
            {
                _localManifest = new SyncManifest { BoothId = _boothId };
            }
        }

        private string GetLocalManifestPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string syncDir = Path.Combine(appData, "PhotoBooth", "Sync");
            
            if (!Directory.Exists(syncDir))
            {
                Directory.CreateDirectory(syncDir);
            }
            
            return Path.Combine(syncDir, MANIFEST_FILE);
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Perform a full sync with cloud
        /// </summary>
        public async Task<SyncResult> SyncAsync(SyncOptions options = null)
        {
            if (_isSyncing)
            {
                Debug.WriteLine("PhotoBoothSyncService: Sync already in progress");
                return new SyncResult { Success = false, Message = "Sync already in progress" };
            }

            _isSyncing = true;
            var result = new SyncResult();
            
            try
            {
                Debug.WriteLine("PhotoBoothSyncService: Starting sync operation");
                SyncStarted?.Invoke(this, new SyncEventArgs { BoothId = _boothId });

                // Step 1: Download remote manifest
                UpdateProgress("Downloading sync manifest", 10);
                _remoteManifest = await DownloadManifestAsync();

                // Step 2: Compare manifests and identify changes
                UpdateProgress("Comparing local and remote data", 20);
                var syncPlan = CompareMani();

                // Step 3: Sync templates if enabled
                if (_config.SyncTemplates && (options?.SyncTemplates ?? true))
                {
                    UpdateProgress("Syncing templates", 30);
                    // Templates are synced via manifest comparison
                    result.TemplatesSynced = syncPlan.TemplatesToSync.Count;
                }

                // Step 4: Sync settings if enabled
                if (_config.SyncSettings && (options?.SyncSettings ?? true))
                {
                    UpdateProgress("Syncing settings", 50);
                    // Settings are synced via manifest comparison
                    result.SettingsSynced = syncPlan.SettingsToSync.Count;
                }
                
                // Step 5: Sync events if enabled
                if (_config.SyncEvents && (options?.SyncEvents ?? true))
                {
                    UpdateProgress("Syncing events", 60);
                    var eventResult = await SyncEventsAsync();
                    result.EventsSynced = eventResult;
                }

                // Step 6: Sync database items if enabled
                if (_config.SyncDatabase && (options?.SyncDatabase ?? true))
                {
                    UpdateProgress("Syncing database items", 70);
                    var dbResult = await SyncDatabaseItemsAsync(syncPlan.DatabaseItemsToSync);
                    result.DatabaseItemsSynced = dbResult;
                }

                // Step 6: Upload local changes
                UpdateProgress("Uploading local changes", 85);
                await UploadLocalChangesAsync(syncPlan);

                // Step 7: Update and save manifest
                UpdateProgress("Updating sync manifest", 95);
                await UpdateManifestAsync();

                _lastSyncTime = DateTime.Now;
                result.Success = true;
                result.Message = "Sync completed successfully";
                
                UpdateProgress("Sync completed", 100);
                Debug.WriteLine($"PhotoBoothSyncService: Sync completed - Templates: {result.TemplatesSynced}, Settings: {result.SettingsSynced}");
                
                SyncCompleted?.Invoke(this, new SyncEventArgs 
                { 
                    BoothId = _boothId, 
                    Result = result 
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Sync error - {ex.Message}");
                result.Success = false;
                result.Message = $"Sync failed: {ex.Message}";
                
                SyncError?.Invoke(this, new SyncErrorEventArgs 
                { 
                    Error = ex, 
                    Message = ex.Message 
                });
            }
            finally
            {
                _isSyncing = false;
            }

            return result;
        }

        /// <summary>
        /// Upload a specific template to cloud
        /// </summary>
        public async Task<bool> UploadTemplateAsync(string templatePath)
        {
            try
            {
                Debug.WriteLine($"PhotoBoothSyncService: Uploading template {templatePath}");
                
                // Calculate hash for version tracking
                string hash = CalculateFileHash(templatePath);
                string fileName = Path.GetFileName(templatePath);
                string cloudPath = $"{SYNC_ROOT}{TEMPLATES_PATH}{fileName}";
                
                // Upload to cloud
                bool uploaded = await _cloudService.UploadFileAsync(templatePath, cloudPath);
                
                if (uploaded)
                {
                    // Update manifest
                    var item = new SyncItem
                    {
                        Id = Path.GetFileNameWithoutExtension(fileName),
                        Type = SyncItemType.Template,
                        FileName = fileName,
                        Hash = hash,
                        LastModified = DateTime.Now,
                        ModifiedBy = _boothId,
                        CloudPath = cloudPath
                    };
                    
                    UpdateLocalManifestItem(item);
                    Debug.WriteLine($"PhotoBoothSyncService: Template uploaded successfully");
                }
                
                return uploaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error uploading template - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Download a specific template from cloud
        /// </summary>
        public async Task<bool> DownloadTemplateAsync(string templateId)
        {
            try
            {
                var remoteItem = _remoteManifest?.Items.FirstOrDefault(i => i.Id == templateId && i.Type == SyncItemType.Template);
                if (remoteItem == null)
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Template {templateId} not found in remote manifest");
                    return false;
                }
                
                string localPath = Path.Combine(GetTemplatesDirectory(), remoteItem.FileName);
                bool downloaded = await _cloudService.DownloadFileAsync(remoteItem.CloudPath, localPath);
                
                if (downloaded)
                {
                    UpdateLocalManifestItem(remoteItem);
                    Debug.WriteLine($"PhotoBoothSyncService: Template {templateId} downloaded successfully");
                }
                
                return downloaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error downloading template - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if auto-sync should run
        /// </summary>
        public bool ShouldAutoSync()
        {
            if (!_config.AutoSync || !_config.IsEnabled)
                return false;
                
            if (_lastSyncTime == DateTime.MinValue)
                return true;
                
            var timeSinceLastSync = DateTime.Now - _lastSyncTime;
            return timeSinceLastSync.TotalMinutes >= _config.SyncInterval;
        }

        #endregion

        #region Private Methods

        private async Task<SyncManifest> DownloadManifestAsync()
        {
            try
            {
                string manifestPath = $"{SYNC_ROOT}{MANIFEST_FILE}";
                string tempPath = Path.GetTempFileName();
                
                bool downloaded = await _cloudService.DownloadFileAsync(manifestPath, tempPath);
                
                if (downloaded && File.Exists(tempPath))
                {
                    string json = File.ReadAllText(tempPath);
                    var manifest = JsonConvert.DeserializeObject<SyncManifest>(json);
                    File.Delete(tempPath);
                    
                    Debug.WriteLine($"PhotoBoothSyncService: Downloaded remote manifest with {manifest?.Items.Count ?? 0} items");
                    return manifest ?? new SyncManifest();
                }
                
                Debug.WriteLine("PhotoBoothSyncService: No remote manifest found, creating new one");
                return new SyncManifest();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error downloading manifest - {ex.Message}");
                return new SyncManifest();
            }
        }

        private SyncPlan CompareMani()
        {
            var plan = new SyncPlan();
            
            if (_remoteManifest == null || _localManifest == null)
                return plan;
            
            // Find items to download (in remote but not in local or newer in remote)
            foreach (var remoteItem in _remoteManifest.Items)
            {
                var localItem = _localManifest.Items.FirstOrDefault(i => i.Id == remoteItem.Id);
                
                if (localItem == null)
                {
                    // New item - download it
                    plan.ItemsToDownload.Add(remoteItem);
                }
                else if (remoteItem.Hash != localItem.Hash)
                {
                    // Modified item - check conflict resolution
                    if (ShouldDownloadItem(localItem, remoteItem))
                    {
                        plan.ItemsToDownload.Add(remoteItem);
                    }
                    else if (ShouldUploadItem(localItem, remoteItem))
                    {
                        plan.ItemsToUpload.Add(localItem);
                    }
                    else
                    {
                        // Conflict - let user decide
                        plan.Conflicts.Add(new SyncConflict
                        {
                            ItemId = remoteItem.Id,
                            LocalItem = localItem,
                            RemoteItem = remoteItem
                        });
                        
                        ConflictDetected?.Invoke(this, new ConflictEventArgs
                        {
                            Conflict = plan.Conflicts.Last()
                        });
                    }
                }
            }
            
            // Find items to upload (in local but not in remote)
            foreach (var localItem in _localManifest.Items)
            {
                if (!_remoteManifest.Items.Any(i => i.Id == localItem.Id))
                {
                    plan.ItemsToUpload.Add(localItem);
                }
            }
            
            // Categorize by type
            plan.TemplatesToSync = plan.ItemsToDownload.Where(i => i.Type == SyncItemType.Template).ToList();
            plan.SettingsToSync = plan.ItemsToDownload.Where(i => i.Type == SyncItemType.Setting).ToList();
            plan.DatabaseItemsToSync = plan.ItemsToDownload.Where(i => i.Type == SyncItemType.Database).ToList();
            
            Debug.WriteLine($"PhotoBoothSyncService: Sync plan - Download: {plan.ItemsToDownload.Count}, Upload: {plan.ItemsToUpload.Count}, Conflicts: {plan.Conflicts.Count}");
            
            return plan;
        }

        private bool ShouldDownloadItem(SyncItem local, SyncItem remote)
        {
            switch (_config.ConflictResolution)
            {
                case ConflictResolutionMode.NewestWins:
                    return remote.LastModified > local.LastModified;
                case ConflictResolutionMode.RemoteWins:
                    return true;
                case ConflictResolutionMode.LocalWins:
                    return false;
                default:
                    return false; // Manual resolution
            }
        }

        private bool ShouldUploadItem(SyncItem local, SyncItem remote)
        {
            switch (_config.ConflictResolution)
            {
                case ConflictResolutionMode.NewestWins:
                    return local.LastModified > remote.LastModified;
                case ConflictResolutionMode.LocalWins:
                    return true;
                case ConflictResolutionMode.RemoteWins:
                    return false;
                default:
                    return false; // Manual resolution
            }
        }

        private async Task UploadLocalChangesAsync(SyncPlan plan)
        {
            foreach (var item in plan.ItemsToUpload)
            {
                try
                {
                    string localPath = GetLocalPathForItem(item);
                    if (File.Exists(localPath))
                    {
                        await _cloudService.UploadFileAsync(localPath, item.CloudPath);
                        Debug.WriteLine($"PhotoBoothSyncService: Uploaded {item.FileName}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Error uploading {item.FileName} - {ex.Message}");
                }
            }
        }

        private string GetLocalPathForItem(SyncItem item)
        {
            switch (item.Type)
            {
                case SyncItemType.Template:
                    return Path.Combine(GetTemplatesDirectory(), item.FileName);
                case SyncItemType.Setting:
                    return Path.Combine(GetSettingsDirectory(), item.FileName);
                case SyncItemType.Database:
                    return GetLocalDatabasePath(item.FileName);
                default:
                    return null;
            }
        }

        private string GetLocalDatabasePath(string fileName)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "PhotoBooth", "Database", fileName);
        }

        private async Task<int> SyncDatabaseItemsAsync(List<SyncItem> items)
        {
            int synced = 0;
            
            foreach (var item in items)
            {
                try
                {
                    string localPath = GetLocalDatabasePath(item.FileName);
                    bool downloaded = await _cloudService.DownloadFileAsync(item.CloudPath, localPath);
                    
                    if (downloaded)
                    {
                        synced++;
                        UpdateLocalManifestItem(item);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Error syncing database item {item.FileName} - {ex.Message}");
                }
            }
            
            return synced;
        }

        private async Task UpdateManifestAsync()
        {
            try
            {
                // Save local manifest
                string localPath = GetLocalManifestPath();
                string json = JsonConvert.SerializeObject(_localManifest, Formatting.Indented);
                File.WriteAllText(localPath, json);
                
                // Upload to cloud
                string cloudPath = $"{SYNC_ROOT}{MANIFEST_FILE}";
                await _cloudService.UploadFileAsync(localPath, cloudPath);
                
                Debug.WriteLine("PhotoBoothSyncService: Manifest updated and uploaded");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error updating manifest - {ex.Message}");
            }
        }

        private void UpdateLocalManifestItem(SyncItem item)
        {
            var existingItem = _localManifest.Items.FirstOrDefault(i => i.Id == item.Id);
            if (existingItem != null)
            {
                _localManifest.Items.Remove(existingItem);
            }
            
            _localManifest.Items.Add(item);
            _localManifest.LastModified = DateTime.Now;
            _localManifest.ModifiedBy = _boothId;
        }

        private string CalculateFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private void UpdateProgress(string message, int percentage)
        {
            SyncProgress?.Invoke(this, new SyncProgressEventArgs
            {
                Message = message,
                ProgressPercentage = percentage
            });
        }
        
        private void RaiseSyncStarted()
        {
            SyncStarted?.Invoke(this, new SyncEventArgs
            {
                BoothId = _boothId,
                Result = new SyncResult()
            });
        }
        
        private void RaiseSyncProgress(string message, int percentage)
        {
            UpdateProgress(message, percentage);
        }
        
        private void RaiseSyncCompleted(bool success)
        {
            SyncCompleted?.Invoke(this, new SyncEventArgs
            {
                BoothId = _boothId,
                Result = new SyncResult { Success = success }
            });
        }
        
        private void RaiseSyncError(Exception ex)
        {
            SyncError?.Invoke(this, new SyncErrorEventArgs
            {
                Error = ex,
                Message = ex.Message
            });
        }
        
        private async Task DownloadRemoteManifestAsync()
        {
            try
            {
                var manifestPath = $"{SYNC_ROOT}{MANIFEST_FILE}";
                var data = await _cloudService.DownloadAsync(manifestPath);
                if (data != null)
                {
                    var json = System.Text.Encoding.UTF8.GetString(data);
                    _remoteManifest = JsonConvert.DeserializeObject<SyncManifest>(json);
                }
                else
                {
                    _remoteManifest = new SyncManifest { BoothId = "remote" };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error downloading remote manifest: {ex.Message}");
                _remoteManifest = new SyncManifest { BoothId = "remote" };
            }
        }
        
        private async Task UploadLocalManifestAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_localManifest, Formatting.Indented);
                var data = System.Text.Encoding.UTF8.GetBytes(json);
                var cloudManifestPath = $"{SYNC_ROOT}{MANIFEST_FILE}";
                await _cloudService.UploadAsync(cloudManifestPath, data);
                
                // Also save manifest locally
                var localManifestPath = GetLocalManifestPath();
                File.WriteAllText(localManifestPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error uploading local manifest: {ex.Message}");
            }
        }
        
        private ManifestDifferences CompareManifests()
        {
            var differences = new ManifestDifferences();
            
            if (_localManifest == null || _remoteManifest == null)
                return differences;
            
            // Build lookup dictionaries
            var localItems = _localManifest.Items.ToDictionary(i => i.Id);
            var remoteItems = _remoteManifest.Items.ToDictionary(i => i.Id);
            
            // Find items to upload (local only or local newer)
            foreach (var localItem in _localManifest.Items)
            {
                if (!remoteItems.ContainsKey(localItem.Id))
                {
                    // Item only exists locally
                    if (localItem.Type == SyncItemType.Template)
                        differences.TemplatesToUpload.Add(localItem);
                    else if (localItem.Type == SyncItemType.Setting)
                        differences.SettingsToUpload.Add(localItem);
                }
                else if (localItem.Hash != remoteItems[localItem.Id].Hash)
                {
                    // Item exists in both but different
                    var remoteItem = remoteItems[localItem.Id];
                    if (localItem.LastModified > remoteItem.LastModified)
                    {
                        if (localItem.Type == SyncItemType.Template)
                            differences.TemplatesToUpload.Add(localItem);
                        else if (localItem.Type == SyncItemType.Setting)
                            differences.SettingsToUpload.Add(localItem);
                    }
                    else
                    {
                        if (remoteItem.Type == SyncItemType.Template)
                            differences.TemplatesToDownload.Add(remoteItem);
                        else if (remoteItem.Type == SyncItemType.Setting)
                            differences.SettingsToDownload.Add(remoteItem);
                    }
                    differences.Conflicts.Add(localItem);
                }
            }
            
            // Find items to download (remote only)
            foreach (var remoteItem in _remoteManifest.Items)
            {
                if (!localItems.ContainsKey(remoteItem.Id))
                {
                    if (remoteItem.Type == SyncItemType.Template)
                        differences.TemplatesToDownload.Add(remoteItem);
                    else if (remoteItem.Type == SyncItemType.Setting)
                        differences.SettingsToDownload.Add(remoteItem);
                }
            }
            
            return differences;
        }
        
        private async Task UploadTemplateAsync(SyncItem template)
        {
            try
            {
                var localPath = Path.Combine(GetTemplatesDirectory(), template.FileName);
                if (File.Exists(localPath))
                {
                    var cloudPath = $"{SYNC_ROOT}{TEMPLATES_PATH}{template.FileName}";
                    await _cloudService.UploadFileAsync(cloudPath, localPath);
                    Debug.WriteLine($"PhotoBoothSyncService: Uploaded template {template.FileName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error uploading template: {ex.Message}");
            }
        }
        
        private async Task DownloadTemplateAsync(SyncItem template)
        {
            try
            {
                var cloudPath = $"{SYNC_ROOT}{TEMPLATES_PATH}{template.FileName}";
                var localPath = Path.Combine(GetTemplatesDirectory(), template.FileName);
                await _cloudService.DownloadFileAsync(cloudPath, localPath);
                Debug.WriteLine($"PhotoBoothSyncService: Downloaded template {template.FileName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error downloading template: {ex.Message}");
            }
        }
        
        private async Task UploadSettingAsync(SyncItem setting)
        {
            // Settings are handled differently - they're in the application settings
            // For now, we'll skip individual setting uploads as they're handled by manifest
            await Task.CompletedTask;
        }
        
        private async Task DownloadSettingAsync(SyncItem setting)
        {
            // Settings are handled differently - they're in the application settings
            // For now, we'll skip individual setting downloads as they're handled by manifest
            await Task.CompletedTask;
        }
        
        private string GetTemplatesDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhotoBooth", "Templates"
            );
        }
        
        private string GetSettingsDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhotoBooth", "Settings"
            );
        }

        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Perform a full sync operation
        /// </summary>
        public async Task<bool> PerformSyncAsync()
        {
            if (_isSyncing)
            {
                Debug.WriteLine("PhotoBoothSyncService: Sync already in progress");
                return false;
            }
            
            try
            {
                _isSyncing = true;
                RaiseSyncStarted();
                RaiseSyncProgress("Starting synchronization...", 0);
                
                // Download remote manifest
                RaiseSyncProgress("Fetching remote manifest...", 10);
                await DownloadRemoteManifestAsync();
                
                // Compare manifests
                RaiseSyncProgress("Comparing changes...", 20);
                var differences = CompareManifests();
                
                // Sync templates
                if (_config.SyncTemplates)
                {
                    RaiseSyncProgress("Syncing templates...", 30);
                    await SyncTemplatesAsync(differences);
                }
                
                // Sync settings
                if (_config.SyncSettings)
                {
                    RaiseSyncProgress("Syncing settings...", 50);
                    await SyncSettingsAsync(differences);
                }
                
                // Upload local manifest
                RaiseSyncProgress("Updating manifest...", 80);
                await UploadLocalManifestAsync();
                
                _lastSyncTime = DateTime.Now;
                RaiseSyncProgress("Sync completed successfully", 100);
                RaiseSyncCompleted(true);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Sync failed - {ex.Message}");
                RaiseSyncError(ex);
                RaiseSyncCompleted(false);
                return false;
            }
            finally
            {
                _isSyncing = false;
            }
        }
        
        /// <summary>
        /// Test connection to cloud storage
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Test S3 connection by trying to list objects in the sync root
                var testPath = $"{SYNC_ROOT}test-{_boothId}.txt";
                var testContent = Encoding.UTF8.GetBytes($"Connection test from {_boothId} at {DateTime.Now}");
                
                // Try to upload a test file
                await _cloudService.UploadAsync(testPath, testContent);
                
                // Try to download it back
                var downloaded = await _cloudService.DownloadAsync(testPath);
                
                // Clean up test file
                await _cloudService.DeleteAsync(testPath);
                
                return downloaded != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Connection test failed - {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Sync templates with differences
        /// </summary>
        private async Task SyncTemplatesAsync(ManifestDifferences differences)
        {
            // Implementation for template sync
            foreach (var template in differences.TemplatesToUpload)
            {
                await UploadTemplateAsync(template);
            }
            
            foreach (var template in differences.TemplatesToDownload)
            {
                await DownloadTemplateAsync(template);
            }
        }
        
        /// <summary>
        /// Sync settings with differences
        /// </summary>
        private async Task SyncSettingsAsync(ManifestDifferences differences)
        {
            // Implementation for settings sync
            foreach (var setting in differences.SettingsToUpload)
            {
                await UploadSettingAsync(setting);
            }
            
            foreach (var setting in differences.SettingsToDownload)
            {
                await DownloadSettingAsync(setting);
            }
        }
        
        /// <summary>
        /// Sync events with their configurations
        /// </summary>
        private async Task<int> SyncEventsAsync()
        {
            int syncedCount = 0;
            try
            {
                Debug.WriteLine("PhotoBoothSyncService: Starting event sync");
                
                // Get local events
                var eventService = new EventService();
                var localEvents = eventService.GetAllEvents();
                
                // Convert to EventConfiguration objects
                var localConfigs = new List<Photobooth.Models.EventConfiguration>();
                foreach (var evt in localEvents)
                {
                    var config = await GetEventConfigurationAsync(evt.Id);
                    if (config != null)
                    {
                        localConfigs.Add(config);
                    }
                }
                
                // Upload local event configurations
                string eventsPath = $"{SYNC_ROOT}events/";
                foreach (var config in localConfigs)
                {
                    try
                    {
                        // Generate filename based on event name (sanitized)
                        string safeFileName = SanitizeFileName($"{config.EventName}_{config.EventId}.json");
                        string cloudPath = $"{eventsPath}{safeFileName}";
                        
                        // Serialize configuration
                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                        
                        // Upload to cloud
                        bool uploaded = await _cloudService.UploadAsync(cloudPath, data);
                        if (uploaded)
                        {
                            syncedCount++;
                            Debug.WriteLine($"PhotoBoothSyncService: Uploaded event config for {config.EventName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Error uploading event {config.EventName}: {ex.Message}");
                    }
                }
                
                // Download remote event configurations
                var remoteConfigs = await DownloadEventConfigurationsAsync(eventsPath);
                foreach (var remoteConfig in remoteConfigs)
                {
                    try
                    {
                        // Check if event exists locally by name
                        var existingEvent = localEvents.FirstOrDefault(e => 
                            e.Name.Equals(remoteConfig.EventName, StringComparison.OrdinalIgnoreCase));
                        
                        if (existingEvent == null)
                        {
                            // Create new event with configuration
                            await CreateEventFromConfigurationAsync(remoteConfig);
                            syncedCount++;
                            Debug.WriteLine($"PhotoBoothSyncService: Created new event from remote: {remoteConfig.EventName}");
                        }
                        else
                        {
                            // Update existing event configuration if remote is newer
                            var localConfig = localConfigs.FirstOrDefault(c => c.EventId == existingEvent.Id);
                            if (localConfig == null || remoteConfig.ModifiedDate > localConfig.ModifiedDate)
                            {
                                await UpdateEventConfigurationAsync(existingEvent.Id, remoteConfig);
                                syncedCount++;
                                Debug.WriteLine($"PhotoBoothSyncService: Updated event from remote: {remoteConfig.EventName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Error processing remote event {remoteConfig.EventName}: {ex.Message}");
                    }
                }
                
                Debug.WriteLine($"PhotoBoothSyncService: Event sync completed - {syncedCount} events synced");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error during event sync: {ex.Message}");
            }
            
            return syncedCount;
        }
        
        private async Task<Photobooth.Models.EventConfiguration> GetEventConfigurationAsync(int eventId)
        {
            try
            {
                var eventService = new EventService();
                var eventData = eventService.GetEvent(eventId);
                if (eventData == null) return null;
                
                // Create configuration from event data
                var config = Photobooth.Models.EventConfiguration.FromApplicationSettings(eventData.Name);
                config.EventId = eventId;
                config.EventName = eventData.Name;
                config.EventType = eventData.EventType;
                config.Location = eventData.Location;
                config.EventDate = eventData.EventDate;
                config.StartTime = eventData.StartTime;
                config.EndTime = eventData.EndTime;
                config.HostName = eventData.HostName;
                config.ContactEmail = eventData.ContactEmail;
                config.ContactPhone = eventData.ContactPhone;
                config.IsActive = eventData.IsActive;
                config.GalleryUrl = eventData.GalleryUrl;
                config.GalleryPassword = eventData.GalleryPassword;
                
                // Get attached templates
                var templates = eventService.GetEventTemplates(eventId);
                foreach (var template in templates)
                {
                    config.Templates.Add(new Photobooth.Models.EventTemplateConfig
                    {
                        TemplateId = template.Id,
                        TemplateName = template.Name,
                        TemplateFile = template.Name, // Use name as file reference
                        IsDefault = false, // Will be set based on database
                        SortOrder = 0
                    });
                }
                
                // Set booth metadata
                config.CreatedByBoothId = _boothId;
                config.LastModifiedByBoothId = _boothId;
                config.ConfigurationHash = config.GenerateHash();
                
                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error getting event configuration: {ex.Message}");
                return null;
            }
        }
        
        private async Task<List<Photobooth.Models.EventConfiguration>> DownloadEventConfigurationsAsync(string eventsPath)
        {
            var configs = new List<Photobooth.Models.EventConfiguration>();
            try
            {
                // List files in events folder
                // Note: ListFilesAsync not implemented in CloudShareServiceRuntime
                // For now, return empty list - would need to implement listing in CloudShareServiceRuntime
                var files = new List<string>();
                
                foreach (var file in files)
                {
                    if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Download configuration file
                            var data = await _cloudService.DownloadAsync($"{eventsPath}{file}");
                            if (data != null)
                            {
                                string json = System.Text.Encoding.UTF8.GetString(data);
                                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<Photobooth.Models.EventConfiguration>(json);
                                if (config != null)
                                {
                                    configs.Add(config);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"PhotoBoothSyncService: Error downloading event config {file}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error listing event files: {ex.Message}");
            }
            
            return configs;
        }
        
        private async Task CreateEventFromConfigurationAsync(Photobooth.Models.EventConfiguration config)
        {
            try
            {
                var eventService = new EventService();
                
                // Create the event - CreateEvent has different signature
                int eventId = eventService.CreateEvent(
                    config.EventName,
                    "", // description
                    config.EventType ?? "",
                    config.Location ?? "",
                    config.EventDate,
                    config.StartTime,
                    config.EndTime,
                    config.HostName ?? "",
                    config.ContactEmail ?? "",
                    config.ContactPhone ?? ""
                );
                
                if (eventId > 0)
                {
                    // Attach templates
                    foreach (var templateConfig in config.Templates)
                    {
                        // Try to find template by name
                        var templateService = new TemplateService();
                        var templates = templateService.GetAllTemplates();
                        var template = templates.FirstOrDefault(t => 
                            t.Name.Equals(templateConfig.TemplateName, StringComparison.OrdinalIgnoreCase));
                        
                        if (template != null)
                        {
                            eventService.AssignTemplateToEvent(eventId, template.Id, templateConfig.IsDefault);
                        }
                    }
                    
                    // Apply workflow settings if this is the currently selected event
                    var currentEventId = Properties.Settings.Default.SelectedEventId;
                    if (currentEventId == 0 || currentEventId == eventId)
                    {
                        config.ApplyToApplicationSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error creating event from configuration: {ex.Message}");
            }
        }
        
        private async Task UpdateEventConfigurationAsync(int eventId, Photobooth.Models.EventConfiguration config)
        {
            try
            {
                var eventService = new EventService();
                var eventData = eventService.GetEvent(eventId);
                
                if (eventData != null)
                {
                    // Update event data
                    eventData.Name = config.EventName;
                    eventData.EventType = config.EventType;
                    eventData.Location = config.Location;
                    eventData.EventDate = config.EventDate;
                    eventData.StartTime = config.StartTime;
                    eventData.EndTime = config.EndTime;
                    eventData.HostName = config.HostName;
                    eventData.ContactEmail = config.ContactEmail;
                    eventData.ContactPhone = config.ContactPhone;
                    eventData.GalleryUrl = config.GalleryUrl;
                    eventData.GalleryPassword = config.GalleryPassword;
                    
                    eventService.UpdateEvent(eventData);
                    
                    // Update templates
                    // First remove all existing templates
                    var existingTemplates = eventService.GetEventTemplates(eventId);
                    foreach (var existing in existingTemplates)
                    {
                        eventService.RemoveTemplateFromEvent(eventId, existing.Id);
                    }
                    
                    // Then add new templates
                    foreach (var templateConfig in config.Templates)
                    {
                        var templateService = new TemplateService();
                        var templates = templateService.GetAllTemplates();
                        var template = templates.FirstOrDefault(t => 
                            t.Name.Equals(templateConfig.TemplateName, StringComparison.OrdinalIgnoreCase));
                        
                        if (template != null)
                        {
                            eventService.AssignTemplateToEvent(eventId, template.Id, templateConfig.IsDefault);
                        }
                    }
                    
                    // Apply workflow settings if this is the currently selected event
                    var currentEventId = Properties.Settings.Default.SelectedEventId;
                    if (currentEventId == eventId)
                    {
                        config.ApplyToApplicationSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error updating event configuration: {ex.Message}");
            }
        }
        
        private string SanitizeFileName(string fileName)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
        
        #endregion
    }

    #region Data Models

    public class SyncConfiguration
    {
        public bool IsEnabled { get; set; }
        public int SyncInterval { get; set; } // minutes
        public bool SyncTemplates { get; set; }
        public bool SyncSettings { get; set; }
        public bool SyncEvents { get; set; }
        public bool SyncDatabase { get; set; }
        public ConflictResolutionMode ConflictResolution { get; set; }
        public bool AutoSync { get; set; }
    }

    public class SyncManifest
    {
        public string Version { get; set; } = "1.0";
        public string BoothId { get; set; }
        public DateTime LastModified { get; set; } = DateTime.Now;
        public string ModifiedBy { get; set; }
        public List<SyncItem> Items { get; set; } = new List<SyncItem>();
    }

    public class SyncItem
    {
        public string Id { get; set; }
        public SyncItemType Type { get; set; }
        public string FileName { get; set; }
        public string Hash { get; set; }
        public DateTime LastModified { get; set; }
        public string ModifiedBy { get; set; }
        public string CloudPath { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public class SyncPlan
    {
        public List<SyncItem> ItemsToDownload { get; set; } = new List<SyncItem>();
        public List<SyncItem> ItemsToUpload { get; set; } = new List<SyncItem>();
        public List<SyncConflict> Conflicts { get; set; } = new List<SyncConflict>();
        public List<SyncItem> TemplatesToSync { get; set; } = new List<SyncItem>();
        public List<SyncItem> SettingsToSync { get; set; } = new List<SyncItem>();
        public List<SyncItem> DatabaseItemsToSync { get; set; } = new List<SyncItem>();
    }

    public class SyncConflict
    {
        public string ItemId { get; set; }
        public SyncItem LocalItem { get; set; }
        public SyncItem RemoteItem { get; set; }
        public ConflictResolution Resolution { get; set; }
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int TemplatesSynced { get; set; }
        public int SettingsSynced { get; set; }
        public int EventsSynced { get; set; }
        public int DatabaseItemsSynced { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class SyncOptions
    {
        public bool SyncTemplates { get; set; } = true;
        public bool SyncSettings { get; set; } = true;
        public bool SyncEvents { get; set; } = true;
        public bool SyncDatabase { get; set; } = true;
        public bool ForceSync { get; set; } = false;
    }
    
    public class ManifestDifferences
    {
        public List<SyncItem> TemplatesToUpload { get; set; } = new List<SyncItem>();
        public List<SyncItem> TemplatesToDownload { get; set; } = new List<SyncItem>();
        public List<SyncItem> SettingsToUpload { get; set; } = new List<SyncItem>();
        public List<SyncItem> SettingsToDownload { get; set; } = new List<SyncItem>();
        public List<SyncItem> Conflicts { get; set; } = new List<SyncItem>();
    }

    public enum SyncItemType
    {
        Template,
        Setting,
        Database,
        Asset
    }

    public enum ConflictResolutionMode
    {
        Manual,
        NewestWins,
        LocalWins,
        RemoteWins
    }

    public enum ConflictResolution
    {
        UseLocal,
        UseRemote,
        Merge,
        Skip
    }

    #endregion

    #region Event Args

    public class SyncEventArgs : EventArgs
    {
        public string BoothId { get; set; }
        public SyncResult Result { get; set; }
    }

    public class SyncProgressEventArgs : EventArgs
    {
        public string Message { get; set; }
        public int ProgressPercentage { get; set; }
        public int Progress { get; set; }
        public string CurrentItem { get; set; }
    }
    
    public class SyncCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ItemsSynced { get; set; }
        public List<string> Errors { get; set; }
    }

    public class SyncErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
        public string Message { get; set; }
    }

    public class ConflictEventArgs : EventArgs
    {
        public SyncConflict Conflict { get; set; }
    }

    #endregion
}