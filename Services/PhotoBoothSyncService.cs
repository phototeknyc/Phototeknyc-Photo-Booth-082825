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
        private CloudShareServiceRuntime _cloudService; // Removed readonly to allow reinitialization
        #endregion

        #region Properties
        private SyncConfiguration _config;
        private SyncManifest _localManifest;
        private SyncManifest _remoteManifest;
        private bool _isSyncing;
        private DateTime _lastSyncTime;
        private string _boothId;
        private System.Timers.Timer _syncTimer;

        public bool IsSyncing => _isSyncing;
        public DateTime LastSyncTime => _lastSyncTime;
        public string BoothId => _boothId;
        public SyncConfiguration Config => _config;

        /// <summary>
        /// Get sync status information
        /// </summary>
        public SyncStatus GetSyncStatus()
        {
            return new SyncStatus
            {
                IsEnabled = _config.IsEnabled,
                IsAutoSyncEnabled = _config.AutoSync,
                IsSyncing = _isSyncing,
                LastSyncTime = _lastSyncTime,
                NextSyncTime = GetNextSyncTime(),
                SyncIntervalMinutes = _config.SyncInterval,
                BoothId = _boothId
            };
        }

        /// <summary>
        /// Calculate next sync time
        /// </summary>
        private DateTime? GetNextSyncTime()
        {
            if (!_config.IsEnabled || !_config.AutoSync || _config.SyncInterval <= 0)
                return null;

            if (_lastSyncTime == DateTime.MinValue)
                return DateTime.Now;

            return _lastSyncTime.AddMinutes(_config.SyncInterval);
        }

        /// <summary>
        /// Get the cloud service instance for testing
        /// </summary>
        public CloudShareServiceRuntime GetCloudService()
        {
            return _cloudService;
        }

        /// <summary>
        /// Reinitialize the cloud service with updated credentials
        /// </summary>
        public void ReinitializeCloudService()
        {
            try
            {
                Debug.WriteLine("PhotoBoothSyncService: Reinitializing cloud service with updated credentials");

                // Set environment variables from current settings
                SetEnvironmentVariablesFromSettings();

                // Create new cloud service instance with updated credentials
                _cloudService = new CloudShareServiceRuntime();

                Debug.WriteLine("PhotoBoothSyncService: Cloud service reinitialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error reinitializing cloud service: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the remote manifest for testing/display
        /// </summary>
        public async Task<SyncManifest> GetRemoteManifestAsync()
        {
            try
            {
                return await DownloadManifestAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error getting remote manifest: {ex.Message}");
                return null;
            }
        }

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
        public event EventHandler<TemplateUpdateEventArgs> TemplateUpdating;
        public event EventHandler<SettingsUpdateEventArgs> SettingsUpdating;
        public event EventHandler<EventUpdateEventArgs> EventUpdating;
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

            // Initialize automatic sync timer
            InitializeAutoSyncTimer();

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
                // Set both in process environment (for immediate use) and user environment (for persistence)
                if (!string.IsNullOrEmpty(settings.S3AccessKey))
                {
                    Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", settings.S3AccessKey, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", settings.S3AccessKey, EnvironmentVariableTarget.User);
                }

                if (!string.IsNullOrEmpty(settings.S3SecretKey))
                {
                    Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", settings.S3SecretKey, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", settings.S3SecretKey, EnvironmentVariableTarget.User);
                }

                if (!string.IsNullOrEmpty(settings.S3BucketName))
                {
                    Environment.SetEnvironmentVariable("S3_BUCKET_NAME", settings.S3BucketName, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("S3_BUCKET_NAME", settings.S3BucketName, EnvironmentVariableTarget.User);
                }

                if (!string.IsNullOrEmpty(settings.S3Region))
                {
                    Environment.SetEnvironmentVariable("S3_REGION", settings.S3Region, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("S3_REGION", settings.S3Region, EnvironmentVariableTarget.User);
                }

                // Also set the gallery base URL if available
                if (!string.IsNullOrEmpty(settings.GalleryBaseUrl))
                {
                    Environment.SetEnvironmentVariable("GALLERY_BASE_URL", settings.GalleryBaseUrl, EnvironmentVariableTarget.Process);
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

        /// <summary>
        /// Initialize automatic sync timer
        /// </summary>
        private void InitializeAutoSyncTimer()
        {
            try
            {
                if (_syncTimer != null)
                {
                    _syncTimer.Stop();
                    _syncTimer.Dispose();
                }

                // Create timer but don't start it yet
                _syncTimer = new System.Timers.Timer();
                _syncTimer.Elapsed += async (sender, e) => await OnAutoSyncTimerElapsed();

                // Configure and start timer if auto-sync is enabled
                UpdateAutoSyncTimer();

                Debug.WriteLine($"PhotoBoothSyncService: Auto-sync timer initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error initializing auto-sync timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Update auto-sync timer settings
        /// </summary>
        public void UpdateAutoSyncTimer()
        {
            try
            {
                if (_syncTimer == null) return;

                _syncTimer.Stop();

                if (_config.IsEnabled && _config.AutoSync && _config.SyncInterval > 0)
                {
                    // Convert minutes to milliseconds
                    double intervalMs = _config.SyncInterval * 60 * 1000;

                    // Minimum interval of 1 minute
                    if (intervalMs < 60000) intervalMs = 60000;

                    _syncTimer.Interval = intervalMs;
                    _syncTimer.Start();

                    Debug.WriteLine($"PhotoBoothSyncService: Auto-sync timer started with interval of {_config.SyncInterval} minutes");
                }
                else
                {
                    Debug.WriteLine("PhotoBoothSyncService: Auto-sync timer stopped (disabled or interval is 0)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error updating auto-sync timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle auto-sync timer elapsed
        /// </summary>
        private async Task OnAutoSyncTimerElapsed()
        {
            try
            {
                if (_isSyncing)
                {
                    Debug.WriteLine("PhotoBoothSyncService: Auto-sync skipped - sync already in progress");
                    return;
                }

                Debug.WriteLine("PhotoBoothSyncService: Auto-sync timer elapsed, starting sync...");

                // Perform sync
                var result = await SyncAsync();

                if (result.Success)
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Auto-sync completed successfully");
                }
                else
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Auto-sync failed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error during auto-sync: {ex.Message}");
            }
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

            // Scan and add local templates to manifest
            ScanAndAddLocalTemplates();
        }

        private void ScanAndAddLocalTemplates()
        {
            try
            {
                string templatesDir = GetTemplatesDirectory();
                if (!Directory.Exists(templatesDir))
                {
                    Directory.CreateDirectory(templatesDir);
                }

                // Export templates from database to files for syncing
                ExportTemplatesFromDatabase(templatesDir);

                // Get all template files (assuming .xaml or .pbtpl extensions)
                var templateFiles = Directory.GetFiles(templatesDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".pbtpl", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var templateFile in templateFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(templateFile);
                        var fileInfo = new FileInfo(templateFile);
                        var hash = CalculateFileHash(templateFile);

                        // Check if this template already exists in manifest
                        var existingItem = _localManifest.Items.FirstOrDefault(i =>
                            i.Type == SyncItemType.Template && i.FileName == fileName);

                        if (existingItem != null)
                        {
                            // Update existing item if hash changed
                            if (existingItem.Hash != hash)
                            {
                                existingItem.Hash = hash;
                                existingItem.LastModified = fileInfo.LastWriteTime;
                                Debug.WriteLine($"PhotoBoothSyncService: Updated template in manifest: {fileName}");
                            }
                        }
                        else
                        {
                            // Add new template to manifest
                            var syncItem = new SyncItem
                            {
                                Id = Guid.NewGuid().ToString(),
                                Type = SyncItemType.Template,
                                FileName = fileName,
                                CloudPath = $"{SYNC_ROOT}{TEMPLATES_PATH}{fileName}",
                                Hash = hash,
                                LastModified = fileInfo.LastWriteTime,
                                Metadata = new Dictionary<string, object>
                                {
                                    ["BoothId"] = _boothId,
                                    ["CreatedAt"] = DateTime.Now
                                }
                            };

                            _localManifest.Items.Add(syncItem);
                            Debug.WriteLine($"PhotoBoothSyncService: Added new template to manifest: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Error processing template {templateFile}: {ex.Message}");
                    }
                }

                // Remove templates from manifest that no longer exist locally
                var templateItems = _localManifest.Items.Where(i => i.Type == SyncItemType.Template).ToList();
                foreach (var item in templateItems)
                {
                    var localPath = Path.Combine(templatesDir, item.FileName);
                    if (!File.Exists(localPath))
                    {
                        _localManifest.Items.Remove(item);
                        Debug.WriteLine($"PhotoBoothSyncService: Removed missing template from manifest: {item.FileName}");
                    }
                }

                Debug.WriteLine($"PhotoBoothSyncService: Scanned {templateFiles.Count} templates, manifest now has {_localManifest.Items.Count(i => i.Type == SyncItemType.Template)} template items");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error scanning local templates: {ex.Message}");
            }
        }

        private void ExportTemplatesFromDatabase(string templatesDir)
        {
            try
            {
                var templateDb = new Database.TemplateDatabase();
                var templates = templateDb.GetAllTemplates();

                Debug.WriteLine($"PhotoBoothSyncService: Found {templates.Count} templates in database");

                // Clean up orphaned template JSON files that no longer exist in database
                var existingTemplateFiles = Directory.GetFiles(templatesDir, "template_*.json");
                var validTemplateIds = new HashSet<int>(templates.Select(t => t.Id));

                foreach (var file in existingTemplateFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.StartsWith("template_") && fileName.EndsWith(".json"))
                    {
                        var idStr = fileName.Substring(9, fileName.Length - 14); // Extract ID from template_ID.json
                        if (int.TryParse(idStr, out int templateId))
                        {
                            if (!validTemplateIds.Contains(templateId))
                            {
                                // This template JSON file is orphaned - delete it
                                File.Delete(file);
                                Debug.WriteLine($"PhotoBoothSyncService: Deleted orphaned template file: {fileName}");
                            }
                        }
                    }
                }

                foreach (var template in templates)
                {
                    try
                    {
                        // Don't upload assets here - just export JSON with local paths
                        // Assets will be uploaded during the actual sync process

                        // Create a JSON representation of the template
                        var templateExport = new
                        {
                            template.Id,
                            template.Name,
                            template.Description,
                            template.CanvasWidth,
                            template.CanvasHeight,
                            template.BackgroundColor,
                            template.BackgroundImagePath,
                            template.ThumbnailImagePath,
                            template.CreatedDate,
                            template.ModifiedDate,
                            template.IsActive,
                            Items = GetTemplateItems(templateDb, template.Id)
                        };

                        string fileName = $"template_{template.Id}.json";
                        string filePath = Path.Combine(templatesDir, fileName);

                        string json = JsonConvert.SerializeObject(templateExport, Formatting.Indented);
                        File.WriteAllText(filePath, json);

                        Debug.WriteLine($"PhotoBoothSyncService: Exported template {template.Name} to {fileName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Error exporting template {template.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error exporting templates from database: {ex.Message}");
            }
        }

        private async Task<string> UploadTemplateAssetAsync(int templateId, string localPath, string assetType)
        {
            try
            {
                if (!File.Exists(localPath))
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Asset file not found: {localPath}");
                    return null;
                }

                // Generate S3 path for the asset
                string fileExtension = Path.GetExtension(localPath);
                string s3FileName = $"template_{templateId}_{assetType}{fileExtension}";
                string s3Path = $"{SYNC_ROOT}{TEMPLATES_PATH}assets/{s3FileName}";

                // Upload the file to S3
                bool uploaded = await _cloudService.UploadFileAsync(s3Path, localPath);

                if (uploaded)
                {
                    // Return the S3 URL
                    string bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME") ?? "phototeknyc";
                    string s3Url = $"https://{bucketName}.s3.amazonaws.com/{s3Path}";
                    Debug.WriteLine($"PhotoBoothSyncService: Uploaded asset {assetType} for template {templateId} to {s3Url}");
                    return s3Url;
                }
                else
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Failed to upload asset {assetType} for template {templateId}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error uploading template asset: {ex.Message}");
                return null;
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

                // Step 0: Rescan local templates to ensure manifest is up to date
                UpdateProgress("Scanning local templates", 5);
                ScanAndAddLocalTemplates();

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

                    // Download remote templates
                    foreach (var template in syncPlan.TemplatesToSync)
                    {
                        await DownloadTemplateAsync(template);
                    }

                    // Upload local templates that need syncing
                    var templatesToUpload = syncPlan.ItemsToUpload.Where(i => i.Type == SyncItemType.Template).ToList();
                    foreach (var template in templatesToUpload)
                    {
                        await UploadTemplateAsync(template);
                    }

                    result.TemplatesSynced = syncPlan.TemplatesToSync.Count + templatesToUpload.Count;
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
                bool uploaded = await _cloudService.UploadFileAsync(cloudPath, templatePath);
                
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

        /// <summary>
        /// Enable or disable auto-sync
        /// </summary>
        public void SetAutoSyncEnabled(bool enabled)
        {
            _config.AutoSync = enabled;
            Properties.Settings.Default.AutoSyncOnStartup = enabled;
            Properties.Settings.Default.Save();

            UpdateAutoSyncTimer();

            Debug.WriteLine($"PhotoBoothSyncService: Auto-sync {(enabled ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Set sync interval in minutes
        /// </summary>
        public void SetSyncInterval(int minutes)
        {
            if (minutes < 1) minutes = 1; // Minimum 1 minute

            _config.SyncInterval = minutes;
            Properties.Settings.Default.SyncIntervalMinutes = minutes;
            Properties.Settings.Default.Save();

            UpdateAutoSyncTimer();

            Debug.WriteLine($"PhotoBoothSyncService: Sync interval set to {minutes} minutes");
        }

        /// <summary>
        /// Update configuration from settings
        /// </summary>
        public void RefreshConfiguration()
        {
            InitializeConfiguration();
            UpdateAutoSyncTimer();

            Debug.WriteLine("PhotoBoothSyncService: Configuration refreshed from settings");
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
                // For templates, compare by FileName instead of Id since IDs are auto-generated locally
                var localItem = remoteItem.Type == SyncItemType.Template
                    ? _localManifest.Items.FirstOrDefault(i => i.Type == SyncItemType.Template && i.FileName == remoteItem.FileName)
                    : _localManifest.Items.FirstOrDefault(i => i.Id == remoteItem.Id);

                if (localItem == null)
                {
                    // New item - download it
                    plan.ItemsToDownload.Add(remoteItem);
                    Debug.WriteLine($"PhotoBoothSyncService: Will download {remoteItem.Type} - {remoteItem.FileName}");
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
                // For templates, compare by FileName instead of Id
                bool existsInRemote = localItem.Type == SyncItemType.Template
                    ? _remoteManifest.Items.Any(i => i.Type == SyncItemType.Template && i.FileName == localItem.FileName)
                    : _remoteManifest.Items.Any(i => i.Id == localItem.Id);

                if (!existsInRemote)
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
                        // Fixed parameter order: cloudPath first, then localPath
                        await _cloudService.UploadFileAsync(item.CloudPath, localPath);
                        Debug.WriteLine($"PhotoBoothSyncService: Uploaded {item.FileName} to {item.CloudPath}");
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
                await _cloudService.UploadFileAsync(cloudPath, localPath);
                
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
                    // Upload the template JSON file
                    var cloudPath = $"{SYNC_ROOT}{TEMPLATES_PATH}{template.FileName}";
                    await _cloudService.UploadFileAsync(cloudPath, localPath);
                    Debug.WriteLine($"PhotoBoothSyncService: Uploaded template {template.FileName}");

                    // Now upload the template's assets (background, thumbnail, item images)
                    await UploadTemplateAssetsAsync(localPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error uploading template: {ex.Message}");
            }
        }

        private async Task UploadTemplateAssetsAsync(string templateJsonPath)
        {
            try
            {
                // Read the template JSON to get asset paths
                string json = File.ReadAllText(templateJsonPath);
                dynamic templateData = JsonConvert.DeserializeObject(json);

                // Extract template ID from filename (template_X.json)
                string fileName = Path.GetFileNameWithoutExtension(templateJsonPath);
                int templateId = int.Parse(fileName.Replace("template_", ""));

                // Upload background image if it exists
                string backgroundPath = (string)templateData.BackgroundImagePath;
                if (!string.IsNullOrEmpty(backgroundPath) && File.Exists(backgroundPath))
                {
                    string bgUrl = await UploadTemplateAssetAsync(templateId, backgroundPath, "background");
                    if (!string.IsNullOrEmpty(bgUrl))
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Uploaded background for template {templateId}");
                    }
                }

                // Upload thumbnail image if it exists
                string thumbnailPath = (string)templateData.ThumbnailImagePath;
                if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    string thumbUrl = await UploadTemplateAssetAsync(templateId, thumbnailPath, "thumbnail");
                    if (!string.IsNullOrEmpty(thumbUrl))
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Uploaded thumbnail for template {templateId}");
                    }
                }

                // Upload item images if they exist
                if (templateData.Items != null)
                {
                    foreach (var item in templateData.Items)
                    {
                        string itemImagePath = (string)item.ImagePath;
                        if (!string.IsNullOrEmpty(itemImagePath) && File.Exists(itemImagePath))
                        {
                            int itemId = (int)item.Id;
                            string itemUrl = await UploadTemplateAssetAsync(templateId, itemImagePath, $"item_{itemId}");
                            if (!string.IsNullOrEmpty(itemUrl))
                            {
                                Debug.WriteLine($"PhotoBoothSyncService: Uploaded image for item {itemId} in template {templateId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error uploading template assets: {ex.Message}");
            }
        }
        
        private async Task DownloadTemplateAsync(SyncItem template)
        {
            try
            {
                var cloudPath = $"{SYNC_ROOT}{TEMPLATES_PATH}{template.FileName}";
                var localPath = Path.Combine(GetTemplatesDirectory(), template.FileName);

                // Download the template file from S3
                bool downloaded = await _cloudService.DownloadFileAsync(cloudPath, localPath);

                if (downloaded && File.Exists(localPath))
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Downloaded template {template.FileName}");

                    // Import the template into the database
                    ImportTemplateToDatabase(localPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error downloading template: {ex.Message}");
            }
        }

        private List<object> GetTemplateItems(Database.TemplateDatabase templateDb, int templateId)
        {
            try
            {
                // Get canvas items from database
                var items = templateDb.GetCanvasItems(templateId);
                var exportItems = new List<object>();

                foreach (var item in items)
                {
                    // Don't upload images here - just export with local paths
                    // Images will be uploaded during the actual sync process

                    // Create export object
                    var exportItem = new
                    {
                        item.Id,
                        item.TemplateId,
                        item.ItemType,
                        item.Name,
                        item.X,
                        item.Y,
                        item.Width,
                        item.Height,
                        item.Rotation,
                        item.ZIndex,
                        item.LockedPosition,
                        item.LockedSize,
                        item.LockedAspectRatio,
                        item.IsVisible,
                        item.IsLocked,
                        item.ImagePath,
                        item.ImageHash,
                        item.Text,
                        item.FontFamily,
                        item.FontSize,
                        item.FontWeight,
                        item.FontStyle,
                        item.TextColor,
                        item.TextAlignment,
                        item.IsBold,
                        item.IsItalic,
                        item.IsUnderlined,
                        item.HasShadow,
                        item.ShadowOffsetX,
                        item.ShadowOffsetY,
                        item.ShadowBlurRadius,
                        item.ShadowColor,
                        item.HasOutline,
                        item.OutlineThickness,
                        item.OutlineColor,
                        item.PlaceholderNumber,
                        item.PlaceholderColor,
                        item.ShapeType,
                        item.FillColor,
                        item.StrokeColor,
                        item.StrokeThickness,
                        item.HasNoFill,
                        item.HasNoStroke
                    };

                    exportItems.Add(exportItem);
                }

                return exportItems;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error getting template items: {ex.Message}");
                return new List<object>();
            }
        }

        private void SaveTemplateItemToDatabase(Database.TemplateDatabase templateDb, int templateId, dynamic item)
        {
            try
            {
                // Download item image if it's an S3 URL
                string localImagePath = null;
                if (item.ImagePath != null)
                {
                    string imageUrl = (string)item.ImagePath;
                    if (!string.IsNullOrEmpty(imageUrl) && imageUrl.Contains(".s3.amazonaws.com"))
                    {
                        localImagePath = DownloadItemAsset(imageUrl, templateId, (int)item.Id);
                    }
                    else
                    {
                        localImagePath = imageUrl;
                    }
                }

                // Create CanvasItemData object
                var canvasItem = new Database.CanvasItemData
                {
                    TemplateId = templateId,
                    ItemType = item.ItemType,
                    Name = item.Name,
                    X = item.X,
                    Y = item.Y,
                    Width = item.Width,
                    Height = item.Height,
                    Rotation = item.Rotation,
                    ZIndex = item.ZIndex,
                    LockedPosition = item.LockedPosition ?? false,
                    LockedSize = item.LockedSize ?? false,
                    LockedAspectRatio = item.LockedAspectRatio ?? false,
                    IsVisible = item.IsVisible ?? true,
                    IsLocked = item.IsLocked ?? false,
                    ImagePath = localImagePath ?? (string)item.OriginalImagePath,
                    ImageHash = item.ImageHash,
                    Text = item.Text,
                    FontFamily = item.FontFamily,
                    FontSize = item.FontSize ?? 0,
                    FontWeight = item.FontWeight,
                    FontStyle = item.FontStyle,
                    TextColor = item.TextColor,
                    TextAlignment = item.TextAlignment,
                    IsBold = item.IsBold ?? false,
                    IsItalic = item.IsItalic ?? false,
                    IsUnderlined = item.IsUnderlined ?? false,
                    HasShadow = item.HasShadow ?? false,
                    ShadowOffsetX = item.ShadowOffsetX ?? 0,
                    ShadowOffsetY = item.ShadowOffsetY ?? 0,
                    ShadowBlurRadius = item.ShadowBlurRadius ?? 0,
                    ShadowColor = item.ShadowColor,
                    HasOutline = item.HasOutline ?? false,
                    OutlineThickness = item.OutlineThickness ?? 0,
                    OutlineColor = item.OutlineColor,
                    PlaceholderNumber = item.PlaceholderNumber ?? 0,
                    PlaceholderColor = item.PlaceholderColor,
                    ShapeType = item.ShapeType,
                    FillColor = item.FillColor,
                    StrokeColor = item.StrokeColor,
                    StrokeThickness = item.StrokeThickness ?? 0,
                    HasNoFill = item.HasNoFill ?? false,
                    HasNoStroke = item.HasNoStroke ?? false
                };

                // Save to database
                templateDb.SaveCanvasItem(canvasItem);
                Debug.WriteLine($"PhotoBoothSyncService: Saved template item {canvasItem.Name} for template {templateId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error saving template item: {ex.Message}");
            }
        }

        private string DownloadItemAsset(string assetUrl, int templateId, int itemId)
        {
            try
            {
                // Extract the S3 path from the URL
                Uri uri = new Uri(assetUrl);
                string s3Path = uri.AbsolutePath.TrimStart('/');

                // Create local path for the asset
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string assetsDir = Path.Combine(appData, "PhotoBooth", "Templates", "Assets", $"Template_{templateId}");

                if (!Directory.Exists(assetsDir))
                {
                    Directory.CreateDirectory(assetsDir);
                }

                string fileExtension = Path.GetExtension(assetUrl);
                string localFileName = $"item_{itemId}{fileExtension}";
                string localPath = Path.Combine(assetsDir, localFileName);

                // Download the file from S3
                bool downloaded = _cloudService.DownloadFileAsync(s3Path, localPath).Result;

                if (downloaded && File.Exists(localPath))
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Downloaded item asset for item {itemId} to {localPath}");
                    return localPath;
                }
                else
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Failed to download item asset for item {itemId}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error downloading item asset: {ex.Message}");
                return null;
            }
        }

        private void ImportTemplateToDatabase(string templateFilePath)
        {
            try
            {
                // Read the JSON file
                string json = File.ReadAllText(templateFilePath);
                dynamic templateData = JsonConvert.DeserializeObject(json);

                var templateDb = new Database.TemplateDatabase();

                // Check if template already exists
                int templateId = (int)templateData.Id;
                var existingTemplate = templateDb.GetTemplate(templateId);

                // Download image assets if they are S3 URLs
                string localBackgroundPath = DownloadTemplateAsset(templateData, "BackgroundImagePath", templateId, "background");
                string localThumbnailPath = DownloadTemplateAsset(templateData, "ThumbnailImagePath", templateId, "thumbnail");

                if (existingTemplate != null)
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Template {templateId} already exists, updating...");

                    // Notify that template is being updated
                    TemplateUpdating?.Invoke(this, new TemplateUpdateEventArgs
                    {
                        TemplateId = templateId,
                        TemplateName = existingTemplate.Name,
                        UpdateType = TemplateUpdateType.Modified,
                        Message = $"Updating template '{existingTemplate.Name}' from cloud sync..."
                    });

                    // Update existing template
                    existingTemplate.Name = templateData.Name;
                    existingTemplate.Description = templateData.Description;
                    existingTemplate.CanvasWidth = templateData.CanvasWidth;
                    existingTemplate.CanvasHeight = templateData.CanvasHeight;
                    existingTemplate.BackgroundColor = templateData.BackgroundColor;
                    existingTemplate.BackgroundImagePath = localBackgroundPath ?? (string)templateData.OriginalBackgroundPath ?? existingTemplate.BackgroundImagePath;
                    existingTemplate.ThumbnailImagePath = localThumbnailPath ?? (string)templateData.OriginalThumbnailPath ?? existingTemplate.ThumbnailImagePath;
                    existingTemplate.IsActive = templateData.IsActive;
                    existingTemplate.ModifiedDate = DateTime.Now;

                    templateDb.UpdateTemplate(existingTemplate.Id, existingTemplate);

                    // Clear and re-import canvas items for the template
                    templateDb.DeleteCanvasItems(templateId);
                    Debug.WriteLine($"PhotoBoothSyncService: Cleared existing canvas items for template {templateId} during update");

                    // Import template items if they exist
                    if (templateData.Items != null)
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Importing {templateData.Items.Count} items for updated template {templateId}");
                        foreach (var item in templateData.Items)
                        {
                            SaveTemplateItemToDatabase(templateDb, templateId, item);
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Importing new template {templateId}");

                    // Create new template
                    var newTemplate = new Database.TemplateData
                    {
                        Id = templateId,
                        Name = templateData.Name,
                        Description = templateData.Description,
                        CanvasWidth = templateData.CanvasWidth,
                        CanvasHeight = templateData.CanvasHeight,
                        BackgroundColor = templateData.BackgroundColor,
                        BackgroundImagePath = localBackgroundPath ?? (string)templateData.OriginalBackgroundPath,
                        ThumbnailImagePath = localThumbnailPath ?? (string)templateData.OriginalThumbnailPath,
                        CreatedDate = templateData.CreatedDate ?? DateTime.Now,
                        ModifiedDate = DateTime.Now,
                        IsActive = templateData.IsActive ?? true
                    };

                    // Save the template with its original ID to maintain event associations
                    templateDb.SaveTemplateWithId(newTemplate);

                    // Clear canvas items AFTER saving the template (so the template exists for the foreign key)
                    // This prevents duplicates when re-importing
                    templateDb.DeleteCanvasItems(templateId);
                    Debug.WriteLine($"PhotoBoothSyncService: Cleared canvas items for template {templateId} before import");

                    // Import template items if they exist
                    if (templateData.Items != null)
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: Importing {templateData.Items.Count} items for template {templateId}");
                        foreach (var item in templateData.Items)
                        {
                            // Template items are saved separately via the database's item methods
                            SaveTemplateItemToDatabase(templateDb, templateId, item);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"PhotoBoothSyncService: No items found in template data for template {templateId}");
                    }
                }

                Debug.WriteLine($"PhotoBoothSyncService: Successfully imported template from {templateFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error importing template to database: {ex.Message}");
            }
        }

        private string DownloadTemplateAsset(dynamic templateData, string propertyName, int templateId, string assetType)
        {
            try
            {
                string assetUrl = templateData[propertyName];

                if (string.IsNullOrEmpty(assetUrl))
                {
                    return null;
                }

                // Check if it's an S3 URL
                if (!assetUrl.Contains(".s3.amazonaws.com"))
                {
                    // It's a local path, not an S3 URL
                    return assetUrl;
                }

                // Extract the S3 path from the URL
                Uri uri = new Uri(assetUrl);
                string s3Path = uri.AbsolutePath.TrimStart('/');

                // Create local path for the asset
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string assetsDir = Path.Combine(appData, "PhotoBooth", "Templates", "Assets");

                if (!Directory.Exists(assetsDir))
                {
                    Directory.CreateDirectory(assetsDir);
                }

                string fileExtension = Path.GetExtension(assetUrl);
                string localFileName = $"template_{templateId}_{assetType}{fileExtension}";
                string localPath = Path.Combine(assetsDir, localFileName);

                // Download the file from S3
                bool downloaded = _cloudService.DownloadFileAsync(s3Path, localPath).Result;

                if (downloaded && File.Exists(localPath))
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Downloaded asset {assetType} for template {templateId} to {localPath}");
                    return localPath;
                }
                else
                {
                    Debug.WriteLine($"PhotoBoothSyncService: Failed to download asset {assetType} for template {templateId}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error downloading template asset: {ex.Message}");
                return null;
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
            try
            {
                Debug.WriteLine("PhotoBoothSyncService: Starting settings sync");

                // Export all current settings to a JSON file
                var settingsJson = await _settingsSync.ExportSettingsAsync();
                if (!string.IsNullOrEmpty(settingsJson))
                {
                    // Save settings JSON locally
                    string settingsDir = GetSettingsDirectory();
                    if (!Directory.Exists(settingsDir))
                    {
                        Directory.CreateDirectory(settingsDir);
                    }

                    string localSettingsPath = Path.Combine(settingsDir, "settings.json");
                    File.WriteAllText(localSettingsPath, settingsJson);

                    // Upload settings to S3
                    string s3SettingsPath = $"{SYNC_ROOT}settings/settings.json";
                    bool uploaded = await _cloudService.UploadFileAsync(s3SettingsPath, localSettingsPath);

                    if (uploaded)
                    {
                        Debug.WriteLine("PhotoBoothSyncService: Settings uploaded successfully");

                        // Add to manifest
                        var settingsItem = new SyncItem
                        {
                            Id = "settings_main",
                            Type = SyncItemType.Setting,
                            FileName = "settings.json",
                            CloudPath = s3SettingsPath,
                            Hash = CalculateFileHash(localSettingsPath),
                            LastModified = DateTime.Now,
                            Metadata = new Dictionary<string, object>
                            {
                                ["BoothId"] = _boothId,
                                ["DeviceId"] = Properties.Settings.Default.DeviceId
                            }
                        };

                        // Update or add in local manifest
                        var existingSettings = _localManifest.Items.FirstOrDefault(i => i.Id == "settings_main");
                        if (existingSettings != null)
                        {
                            _localManifest.Items.Remove(existingSettings);
                        }
                        _localManifest.Items.Add(settingsItem);
                    }
                }

                // Download remote settings if they exist and are newer
                if (_remoteManifest != null)
                {
                    var remoteSettings = _remoteManifest.Items.FirstOrDefault(i => i.Id == "settings_main");
                    var localSettings = _localManifest.Items.FirstOrDefault(i => i.Id == "settings_main");

                    if (remoteSettings != null && (localSettings == null || remoteSettings.LastModified > localSettings.LastModified))
                    {
                        // Download remote settings
                        string localSettingsPath = Path.Combine(GetSettingsDirectory(), "settings_remote.json");
                        bool downloaded = await _cloudService.DownloadFileAsync(remoteSettings.CloudPath, localSettingsPath);

                        if (downloaded && File.Exists(localSettingsPath))
                        {
                            // Notify that settings are being updated
                            SettingsUpdating?.Invoke(this, new SettingsUpdateEventArgs
                            {
                                Message = "Updating application settings from cloud sync...",
                                UpdateType = SettingsUpdateType.Imported
                            });

                            // Import the settings
                            string remoteSettingsJson = File.ReadAllText(localSettingsPath);
                            bool imported = await _settingsSync.ImportSettingsAsync(remoteSettingsJson);

                            if (imported)
                            {
                                Debug.WriteLine("PhotoBoothSyncService: Remote settings imported successfully");

                                // Update local manifest with remote settings info
                                if (localSettings != null)
                                {
                                    _localManifest.Items.Remove(localSettings);
                                }
                                _localManifest.Items.Add(remoteSettings);
                            }
                        }
                    }
                }

                Debug.WriteLine("PhotoBoothSyncService: Settings sync completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PhotoBoothSyncService: Error syncing settings: {ex.Message}");
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
                            // Notify that new event is being added
                            EventUpdating?.Invoke(this, new EventUpdateEventArgs
                            {
                                EventName = remoteConfig.EventName,
                                UpdateType = EventUpdateType.Added,
                                Message = $"Adding new event '{remoteConfig.EventName}' from cloud sync..."
                            });

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
                                // Notify that event is being updated
                                EventUpdating?.Invoke(this, new EventUpdateEventArgs
                                {
                                    EventId = existingEvent.Id,
                                    EventName = remoteConfig.EventName,
                                    UpdateType = EventUpdateType.Modified,
                                    Message = $"Updating event '{remoteConfig.EventName}' from cloud sync..."
                                });

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
                        TemplateFile = $"template_{template.Id}.json", // Use consistent file naming
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
                        var templateService = new TemplateService();
                        var templates = templateService.GetAllTemplates();

                        // First try to match by ID (extracted from TemplateFile)
                        Database.TemplateData template = null;
                        if (!string.IsNullOrEmpty(templateConfig.TemplateFile) &&
                            templateConfig.TemplateFile.StartsWith("template_"))
                        {
                            // Extract ID from filename like "template_5.json"
                            var idStr = templateConfig.TemplateFile.Replace("template_", "").Replace(".json", "");
                            if (int.TryParse(idStr, out int templateId))
                            {
                                template = templates.FirstOrDefault(t => t.Id == templateId);
                            }
                        }

                        // If not found by ID, try to match by name
                        if (template == null)
                        {
                            template = templates.FirstOrDefault(t =>
                                t.Name.Equals(templateConfig.TemplateName, StringComparison.OrdinalIgnoreCase));
                        }

                        if (template != null)
                        {
                            eventService.AssignTemplateToEvent(eventId, template.Id, templateConfig.IsDefault);
                            Debug.WriteLine($"PhotoBoothSyncService: Attached template '{template.Name}' (ID: {template.Id}) to event");
                        }
                        else
                        {
                            Debug.WriteLine($"PhotoBoothSyncService: Could not find template '{templateConfig.TemplateName}' for event");
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

                        // First try to match by ID (extracted from TemplateFile)
                        Database.TemplateData template = null;
                        if (!string.IsNullOrEmpty(templateConfig.TemplateFile) &&
                            templateConfig.TemplateFile.StartsWith("template_"))
                        {
                            // Extract ID from filename like "template_5.json"
                            var idStr = templateConfig.TemplateFile.Replace("template_", "").Replace(".json", "");
                            if (int.TryParse(idStr, out int templateId))
                            {
                                template = templates.FirstOrDefault(t => t.Id == templateId);
                            }
                        }

                        // If not found by ID, try to match by name
                        if (template == null)
                        {
                            template = templates.FirstOrDefault(t =>
                                t.Name.Equals(templateConfig.TemplateName, StringComparison.OrdinalIgnoreCase));
                        }

                        if (template != null)
                        {
                            eventService.AssignTemplateToEvent(eventId, template.Id, templateConfig.IsDefault);
                            Debug.WriteLine($"PhotoBoothSyncService: Updated event template '{template.Name}' (ID: {template.Id})");
                        }
                        else
                        {
                            Debug.WriteLine($"PhotoBoothSyncService: Could not find template '{templateConfig.TemplateName}' for event update");
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

    public class SyncStatus
    {
        public bool IsEnabled { get; set; }
        public bool IsAutoSyncEnabled { get; set; }
        public bool IsSyncing { get; set; }
        public DateTime LastSyncTime { get; set; }
        public DateTime? NextSyncTime { get; set; }
        public int SyncIntervalMinutes { get; set; }
        public string BoothId { get; set; }
    }

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

    public class TemplateUpdateEventArgs : EventArgs
    {
        public int TemplateId { get; set; }
        public string TemplateName { get; set; }
        public TemplateUpdateType UpdateType { get; set; }
        public string Message { get; set; }
    }

    public enum TemplateUpdateType
    {
        Added,
        Modified,
        Deleted
    }

    public class SettingsUpdateEventArgs : EventArgs
    {
        public string Message { get; set; }
        public SettingsUpdateType UpdateType { get; set; }
    }

    public enum SettingsUpdateType
    {
        Imported,
        Exported,
        Modified
    }

    public class EventUpdateEventArgs : EventArgs
    {
        public int EventId { get; set; }
        public string EventName { get; set; }
        public EventUpdateType UpdateType { get; set; }
        public string Message { get; set; }
    }

    public enum EventUpdateType
    {
        Added,
        Modified,
        Deleted
    }

    #endregion
}