using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Threading;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to manage info panel UI updates for status displays
    /// Provides clean separation between business logic and UI updates
    /// </summary>
    public class InfoPanelUIService
    {
        #region Singleton
        private static InfoPanelUIService _instance;
        public static InfoPanelUIService Instance => _instance ?? (_instance = new InfoPanelUIService());
        #endregion

        #region Events
        // UI update events - pages subscribe to these for actual UI updates
        public event EventHandler<CloudSyncUIUpdateEventArgs> CloudSyncUIUpdateRequested;
        public event EventHandler<CloudShareUIUpdateEventArgs> CloudShareUIUpdateRequested;
        public event EventHandler<PrinterStatusUIUpdateEventArgs> PrinterStatusUIUpdateRequested;
        public event EventHandler<CameraStatusUIUpdateEventArgs> CameraStatusUIUpdateRequested;
        #endregion

        #region Private Fields
        private PhotoBoothSyncService _syncService;
        private InfoPanelService _infoPanelService;
        private DispatcherTimer _statusRefreshTimer;
        private CloudSyncUIState _currentSyncState;
        private CloudShareUIState _currentShareState;
        #endregion

        #region Constructor
        private InfoPanelUIService()
        {
            Initialize();
        }
        #endregion

        #region Initialization
        private void Initialize()
        {
            // Get service instances
            _syncService = PhotoBoothSyncService.Instance;
            // InfoPanelService will be set via SetInfoPanelService method
            
            // Initialize states
            _currentSyncState = new CloudSyncUIState();
            _currentShareState = new CloudShareUIState();
            
            // Subscribe to sync service events
            if (_syncService != null)
            {
                _syncService.SyncStarted += OnSyncStarted;
                _syncService.SyncCompleted += OnSyncCompleted;
                _syncService.SyncProgress += OnSyncProgress;
                _syncService.SyncError += OnSyncError;
            }
            
            // Info panel service events will be subscribed via SetInfoPanelService method
            
            // Initialize status refresh timer
            _statusRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _statusRefreshTimer.Tick += OnStatusRefreshTimerTick;
            _statusRefreshTimer.Start();
            
            // Initial status update
            UpdateAllStatuses();
        }
        #endregion

        #region Public Methods
        public void SetInfoPanelService(InfoPanelService infoPanelService)
        {
            // Unsubscribe from old service if exists
            if (_infoPanelService != null)
            {
                _infoPanelService.CloudSyncStatusUpdated -= OnCloudShareStatusUpdated;
                _infoPanelService.PrinterStatusUpdated -= OnPrinterStatusUpdated;
                _infoPanelService.CameraStatusUpdated -= OnCameraStatusUpdated;
            }
            
            // Set new service
            _infoPanelService = infoPanelService;
            
            // Subscribe to new service events
            if (_infoPanelService != null)
            {
                _infoPanelService.CloudSyncStatusUpdated += OnCloudShareStatusUpdated;
                _infoPanelService.PrinterStatusUpdated += OnPrinterStatusUpdated;
                _infoPanelService.CameraStatusUpdated += OnCameraStatusUpdated;
            }
            
            // Initial status update
            UpdateAllStatuses();
        }
        
        public void UpdateAllStatuses()
        {
            UpdateCloudSyncStatus();
            UpdateCloudShareStatus();
        }
        
        public void Dispose()
        {
            // Unsubscribe from events
            if (_syncService != null)
            {
                _syncService.SyncStarted -= OnSyncStarted;
                _syncService.SyncCompleted -= OnSyncCompleted;
                _syncService.SyncProgress -= OnSyncProgress;
                _syncService.SyncError -= OnSyncError;
            }
            
            if (_infoPanelService != null)
            {
                _infoPanelService.CloudSyncStatusUpdated -= OnCloudShareStatusUpdated;
                _infoPanelService.PrinterStatusUpdated -= OnPrinterStatusUpdated;
                _infoPanelService.CameraStatusUpdated -= OnCameraStatusUpdated;
                _infoPanelService.Dispose();
            }
            
            _statusRefreshTimer?.Stop();
        }
        #endregion

        #region Cloud Sync Status Management
        private void UpdateCloudSyncStatus()
        {
            var state = new CloudSyncUIState();
            
            // Check if cloud sync is enabled
            bool syncEnabled = Properties.Settings.Default.EnableCloudSync;
            bool syncTemplates = Properties.Settings.Default.SyncTemplates;
            bool syncSettings = Properties.Settings.Default.SyncSettings;
            bool syncEvents = Properties.Settings.Default.SyncEvents;
            
            if (syncEnabled)
            {
                // Build sync items list
                var syncItems = new List<string>();
                if (syncTemplates) syncItems.Add("Templates");
                if (syncSettings) syncItems.Add("Settings");
                if (syncEvents) syncItems.Add("Events");
                
                state.StatusText = "Sync Enabled";
                state.StatusColor = Colors.LimeGreen;
                state.IconColor = Colors.LimeGreen;
                
                if (syncItems.Count > 0)
                {
                    state.InfoText = string.Join(", ", syncItems);
                    state.InfoColor = Color.FromRgb(136, 136, 136);
                }
                else
                {
                    state.InfoText = "Nothing selected";
                    state.InfoColor = Colors.Orange;
                }
                
                // Check last sync time
                var lastSyncTime = Properties.Settings.Default.LastSyncTime;
                if (!string.IsNullOrEmpty(lastSyncTime) && DateTime.TryParse(lastSyncTime, out DateTime lastSync))
                {
                    var timeSinceSync = DateTime.Now - lastSync;
                    if (timeSinceSync.TotalMinutes < 5)
                    {
                        state.StatusText = "Synced";
                        state.InfoText += $" ({(int)timeSinceSync.TotalMinutes}m ago)";
                    }
                    else if (timeSinceSync.TotalHours < 1)
                    {
                        state.StatusText = "Sync Ready";
                        state.InfoText += $" ({(int)timeSinceSync.TotalMinutes}m ago)";
                    }
                    else
                    {
                        state.StatusText = "Sync Pending";
                        state.InfoText += $" ({(int)timeSinceSync.TotalHours}h ago)";
                        state.StatusColor = Colors.Orange;
                        state.IconColor = Colors.Orange;
                    }
                }
            }
            else
            {
                state.StatusText = "Sync Disabled";
                state.StatusColor = Color.FromRgb(136, 136, 136);
                state.IconColor = Color.FromRgb(136, 136, 136);
                state.InfoText = "Enable in settings";
                state.InfoColor = Color.FromRgb(136, 136, 136);
            }
            
            _currentSyncState = state;
            CloudSyncUIUpdateRequested?.Invoke(this, new CloudSyncUIUpdateEventArgs { State = state });
        }
        
        private void OnSyncStarted(object sender, SyncEventArgs e)
        {
            var state = new CloudSyncUIState
            {
                StatusText = "Syncing...",
                StatusColor = Colors.DeepSkyBlue,
                IconColor = Colors.DeepSkyBlue,
                InfoText = "Starting sync...",
                InfoColor = Color.FromRgb(136, 136, 136),
                ShowProgress = true,
                IsProgressIndeterminate = true,
                ProgressText = "Starting sync..."
            };
            
            _currentSyncState = state;
            CloudSyncUIUpdateRequested?.Invoke(this, new CloudSyncUIUpdateEventArgs { State = state });
        }
        
        private void OnSyncCompleted(object sender, SyncEventArgs e)
        {
            var state = new CloudSyncUIState();
            
            bool success = e.Result?.Success ?? false;
            if (success)
            {
                state.StatusText = "Synced";
                state.StatusColor = Colors.LimeGreen;
                state.IconColor = Colors.LimeGreen;
                state.InfoText = "Sync complete";
                state.InfoColor = Color.FromRgb(136, 136, 136);
                state.ProgressText = "Sync complete";
                
                // Update last sync time
                Properties.Settings.Default.LastSyncTime = DateTime.Now.ToString();
                Properties.Settings.Default.Save();
            }
            else
            {
                state.StatusText = "Sync Failed";
                state.StatusColor = Colors.OrangeRed;
                state.IconColor = Colors.OrangeRed;
                state.InfoText = e.Result?.Message ?? "Sync failed";
                state.InfoColor = Colors.OrangeRed;
                state.ProgressText = "Sync failed";
            }
            
            state.ShowProgress = true;
            _currentSyncState = state;
            CloudSyncUIUpdateRequested?.Invoke(this, new CloudSyncUIUpdateEventArgs { State = state });
            
            // Hide progress after delay
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                UpdateCloudSyncStatus();
            };
            timer.Start();
        }
        
        private void OnSyncProgress(object sender, SyncProgressEventArgs e)
        {
            var state = _currentSyncState ?? new CloudSyncUIState();
            state.ShowProgress = true;
            state.IsProgressIndeterminate = false;
            state.ProgressValue = e.ProgressPercentage;
            state.ProgressText = e.Message;
            
            if (!string.IsNullOrEmpty(e.CurrentItem))
            {
                state.InfoText = $"Syncing: {e.CurrentItem}";
                state.InfoColor = Color.FromRgb(136, 136, 136);
            }
            
            _currentSyncState = state;
            CloudSyncUIUpdateRequested?.Invoke(this, new CloudSyncUIUpdateEventArgs { State = state });
        }
        
        private void OnSyncError(object sender, SyncErrorEventArgs e)
        {
            var state = new CloudSyncUIState
            {
                StatusText = "Sync Error",
                StatusColor = Colors.Red,
                IconColor = Colors.Red,
                InfoText = e.Message ?? e.Error?.Message ?? "Unknown error",
                InfoColor = Colors.Red,
                ShowProgress = false
            };
            
            _currentSyncState = state;
            CloudSyncUIUpdateRequested?.Invoke(this, new CloudSyncUIUpdateEventArgs { State = state });
        }
        #endregion

        #region Cloud Share Status Management
        private void UpdateCloudShareStatus()
        {
            var state = new CloudShareUIState();
            
            // Check S3 configuration
            var bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME", EnvironmentVariableTarget.User);
            var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
            var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);
            
            if (!string.IsNullOrEmpty(bucketName) && !string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
            {
                state.StatusText = "Connected";
                state.StatusColor = Colors.LimeGreen;
                state.IconColor = Colors.LimeGreen;
                state.BucketText = bucketName.Length > 15 ? bucketName.Substring(0, 12) + "..." : bucketName;
                state.BucketColor = Color.FromRgb(136, 136, 136);
                state.IsConfigured = true;
            }
            else
            {
                state.StatusText = "Not Configured";
                state.StatusColor = Colors.Orange;
                state.IconColor = Color.FromRgb(136, 136, 136);
                state.BucketText = "Setup in settings";
                state.BucketColor = Color.FromRgb(136, 136, 136);
                state.IsConfigured = false;
            }
            
            _currentShareState = state;
            CloudShareUIUpdateRequested?.Invoke(this, new CloudShareUIUpdateEventArgs { State = state });
        }
        
        private void OnCloudShareStatusUpdated(object sender, CloudSyncStatusUpdatedEventArgs e)
        {
            // Convert from InfoPanelService status to our share status
            var state = new CloudShareUIState
            {
                StatusText = e.Status.StatusText,
                StatusColor = e.Status.StatusColor,
                IconColor = e.Status.IconColor,
                BucketText = e.Status.BucketText,
                BucketColor = e.Status.BucketColor,
                IsConfigured = e.Status.IsConfigured
            };
            
            _currentShareState = state;
            CloudShareUIUpdateRequested?.Invoke(this, new CloudShareUIUpdateEventArgs { State = state });
        }
        #endregion

        #region Other Status Updates
        private void OnPrinterStatusUpdated(object sender, PrinterStatusUpdatedEventArgs e)
        {
            // Forward printer status updates
            PrinterStatusUIUpdateRequested?.Invoke(this, new PrinterStatusUIUpdateEventArgs { Status = e.Status });
        }
        
        private void OnCameraStatusUpdated(object sender, CameraStatusUpdatedEventArgs e)
        {
            // Convert string status to CameraStatusData
            var statusData = new CameraStatusData
            {
                CameraName = e.Status ?? "No Camera",
                StatusText = e.IsConnected ? "Connected" : "Not Connected",
                StatusColor = e.IsConnected ? Colors.LimeGreen : Color.FromRgb(255, 152, 0),
                IsConnected = e.IsConnected,
                IsReady = e.IsConnected
            };
            
            // Forward camera status updates
            CameraStatusUIUpdateRequested?.Invoke(this, new CameraStatusUIUpdateEventArgs { Status = statusData });
        }
        
        private void OnStatusRefreshTimerTick(object sender, EventArgs e)
        {
            // Periodic refresh of statuses
            UpdateCloudSyncStatus();
        }
        #endregion
    }

    #region Event Args Classes
    public class CloudSyncUIUpdateEventArgs : EventArgs
    {
        public CloudSyncUIState State { get; set; }
    }
    
    public class CloudShareUIUpdateEventArgs : EventArgs
    {
        public CloudShareUIState State { get; set; }
    }
    
    public class PrinterStatusUIUpdateEventArgs : EventArgs
    {
        public PrinterStatusData Status { get; set; }
    }
    
    public class CameraStatusUIUpdateEventArgs : EventArgs
    {
        public CameraStatusData Status { get; set; }
    }
    #endregion

    #region UI State Classes
    public class CloudSyncUIState
    {
        public string StatusText { get; set; } = "Sync Disabled";
        public Color StatusColor { get; set; } = Color.FromRgb(136, 136, 136);
        public Color IconColor { get; set; } = Color.FromRgb(136, 136, 136);
        public string InfoText { get; set; } = "Enable in settings";
        public Color InfoColor { get; set; } = Color.FromRgb(136, 136, 136);
        public bool ShowProgress { get; set; }
        public bool IsProgressIndeterminate { get; set; }
        public double ProgressValue { get; set; }
        public string ProgressText { get; set; }
    }
    
    public class CloudShareUIState
    {
        public string StatusText { get; set; } = "Not Configured";
        public Color StatusColor { get; set; } = Colors.Orange;
        public Color IconColor { get; set; } = Color.FromRgb(136, 136, 136);
        public string BucketText { get; set; } = "Setup in settings";
        public Color BucketColor { get; set; } = Color.FromRgb(136, 136, 136);
        public bool IsConfigured { get; set; }
        public bool ShowUploadProgress { get; set; }
        public double UploadProgress { get; set; }
        public string UploadText { get; set; }
    }
    #endregion
}