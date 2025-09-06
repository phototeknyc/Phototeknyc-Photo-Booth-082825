using System;
using System.Drawing.Printing;
using System.Management;
using System.Windows;
using System.Windows.Media;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Info Panel Service - Clean Architecture
    /// Handles all business logic for status information display
    /// Fires events for UI updates, keeping page as thin routing layer
    /// </summary>
    public class InfoPanelService
    {
        #region Events for UI Updates
        public event EventHandler<PrinterStatusUpdatedEventArgs> PrinterStatusUpdated;
        public event EventHandler<CloudSyncStatusUpdatedEventArgs> CloudSyncStatusUpdated;
        public event EventHandler<CameraStatusUpdatedEventArgs> CameraStatusUpdated;
        public event EventHandler<PhotoCountUpdatedEventArgs> PhotoCountUpdated;
        public event EventHandler<SyncProgressUpdatedEventArgs> SyncProgressUpdated;
        #endregion

        #region Private Fields
        private System.Threading.Timer _statusUpdateTimer;
        private bool _isDisposed = false;
        private CameraSessionManager _cameraManager;
        private DateTime _lastPrinterCheck = DateTime.MinValue;
        private bool _lastPrinterStatus = false;
        private readonly TimeSpan _printerCheckCooldown = TimeSpan.FromSeconds(3);
        #endregion

        #region Constructor and Initialization
        public InfoPanelService(CameraSessionManager cameraManager = null)
        {
            _cameraManager = cameraManager;
            Log.Debug("InfoPanelService initialized with camera manager");
            InitializeStatusUpdates();
        }

        private void InitializeStatusUpdates()
        {
            try
            {
                // Start periodic status updates (every 15 seconds for better responsiveness)
                _statusUpdateTimer = new System.Threading.Timer(
                    UpdateAllStatuses, 
                    null, 
                    TimeSpan.FromSeconds(5), // Start after 5 seconds 
                    TimeSpan.FromSeconds(15) // Update every 15 seconds
                );
                
                Log.Debug("Status update timer initialized - updates every 15 seconds");
            }
            catch (Exception ex)
            {
                Log.Error($"Error initializing status updates: {ex.Message}");
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Force refresh of all status information
        /// </summary>
        public void RefreshAllStatuses()
        {
            try
            {
                Log.Debug("Manual refresh of all statuses requested");
                UpdateAllStatuses(null);
            }
            catch (Exception ex)
            {
                Log.Error($"Error refreshing all statuses: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Force refresh of printer status only
        /// </summary>
        public void RefreshPrinterStatus()
        {
            try
            {
                Log.Debug("Manual refresh of printer status requested");
                UpdatePrinterStatusInternal();
            }
            catch (Exception ex)
            {
                Log.Error($"Error refreshing printer status: {ex.Message}");
            }
        }

        /// <summary>
        /// Update camera status display
        /// </summary>
        public void UpdateCameraStatus(string status)
        {
            try
            {
                CameraStatusUpdated?.Invoke(this, new CameraStatusUpdatedEventArgs
                {
                    Status = status,
                    IsConnected = !string.IsNullOrEmpty(status) && !status.Contains("Disconnected")
                });
                
                Log.Debug($"Camera status updated: {status}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating camera status: {ex.Message}");
            }
        }

        /// <summary>
        /// Update photo count display
        /// </summary>
        public void UpdatePhotoCount(int count, bool isVisible = true)
        {
            try
            {
                PhotoCountUpdated?.Invoke(this, new PhotoCountUpdatedEventArgs
                {
                    PhotoCount = count,
                    IsVisible = isVisible,
                    DisplayText = count > 0 ? $"{count} photo{(count != 1 ? "s" : "")} captured" : ""
                });
                
                Log.Debug($"Photo count updated: {count}, visible: {isVisible}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating photo count: {ex.Message}");
            }
        }

        /// <summary>
        /// Update sync progress display
        /// </summary>
        public void UpdateSyncProgress(int pendingCount, bool isUploading = false, int progress = 0)
        {
            try
            {
                SyncProgressUpdated?.Invoke(this, new SyncProgressUpdatedEventArgs
                {
                    PendingCount = pendingCount,
                    IsUploading = isUploading,
                    Progress = progress,
                    PendingText = pendingCount > 0 ? $"{pendingCount} item{(pendingCount != 1 ? "s" : "")} pending" : "",
                    UploadStatusText = isUploading ? "Uploading..." : "",
                    ShowPendingCount = pendingCount > 0,
                    ShowUploadProgress = isUploading
                });
                
                Log.Debug($"Sync progress updated: {pendingCount} pending, uploading: {isUploading}, progress: {progress}%");
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating sync progress: {ex.Message}");
            }
        }
        #endregion

        #region Private Status Update Methods
        private void UpdateAllStatuses(object state)
        {
            try
            {
                if (_isDisposed) return;

                Log.Debug("Updating all status information");
                UpdatePrinterStatusInternal();
                UpdateCloudSyncStatusInternal();
                UpdateCameraStatusInternal();
            }
            catch (Exception ex)
            {
                Log.Error($"Error in periodic status update: {ex.Message}");
            }
        }

        private void UpdatePrinterStatusInternal()
        {
            try
            {
                var printerName = Properties.Settings.Default.PrinterName;
                var status = new PrinterStatusData();

                if (!string.IsNullOrEmpty(printerName))
                {
                    // Get printer information
                    status.Name = printerName.Length > 20 ? printerName.Substring(0, 17) + "..." : printerName;
                    
                    // Check if printer is actually online
                    bool isOnline = CheckPrinterOnline(printerName);
                    
                    if (isOnline)
                    {
                        status.ConnectionText = " (USB)";
                        status.ConnectionColor = Colors.LightGreen;
                        status.StatusText = "Ready";
                        status.StatusColor = Color.FromRgb(76, 175, 80); // Green
                        status.IsOnline = true;
                        status.HasError = false;
                    }
                    else
                    {
                        status.ConnectionText = " (Offline)";
                        status.ConnectionColor = Color.FromRgb(255, 152, 0); // Orange
                        status.StatusText = "Offline";
                        status.StatusColor = Color.FromRgb(255, 152, 0); // Orange
                        status.IsOnline = false;
                        status.HasError = false;
                    }
                    
                    status.QueueCount = 0; // Would come from actual print service
                    // Media info would come from printer service if available
                    status.MediaRemaining = 0;
                    status.ShowMediaRemaining = false;
                    
                    Log.Debug($"Printer status updated: {status.Name} - {(isOnline ? "Online" : "Offline")}");
                }
                else
                {
                    status.Name = "No Printer";
                    status.ConnectionText = " (Offline)";
                    status.ConnectionColor = Color.FromRgb(255, 152, 0); // Orange
                    status.StatusText = "Not Available";
                    status.StatusColor = Color.FromRgb(255, 82, 82); // Red
                    status.QueueCount = 0;
                    status.IsOnline = false;
                    status.HasError = false;
                    status.ShowMediaRemaining = false;
                    
                    Log.Debug("Printer status updated: No printer configured");
                }

                // Fire event for UI update
                PrinterStatusUpdated?.Invoke(this, new PrinterStatusUpdatedEventArgs { Status = status });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating printer status: {ex.Message}");
                
                // Fire error status
                var errorStatus = new PrinterStatusData
                {
                    Name = "Error",
                    StatusText = "Error",
                    StatusColor = Colors.Red,
                    HasError = true
                };
                PrinterStatusUpdated?.Invoke(this, new PrinterStatusUpdatedEventArgs { Status = errorStatus });
            }
        }

        private void UpdateCloudSyncStatusInternal()
        {
            try
            {
                // Check cloud sync configuration from environment variables (like original)
                var bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME", EnvironmentVariableTarget.User);
                var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
                var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);
                
                var status = new CloudSyncStatusData();

                if (!string.IsNullOrEmpty(bucketName) && !string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
                {
                    // Cloud sync is configured
                    status.StatusText = "Connected";
                    status.StatusColor = Colors.LimeGreen;
                    status.IconColor = Colors.LimeGreen;
                    
                    // Truncate bucket name if too long for display
                    status.BucketText = bucketName.Length > 15 ? bucketName.Substring(0, 12) + "..." : bucketName;
                    status.BucketColor = Color.FromRgb(136, 136, 136);
                    status.IsConfigured = true;
                    
                    Log.Debug($"Cloud sync status updated: Connected to {bucketName}");
                }
                else
                {
                    // Cloud sync not configured
                    status.StatusText = "Not Configured";
                    status.StatusColor = Color.FromRgb(255, 152, 0); // Orange
                    status.BucketText = "Setup in settings";
                    status.BucketColor = Color.FromRgb(136, 136, 136);
                    status.IconColor = Color.FromRgb(136, 136, 136);
                    status.IsConfigured = false;
                    
                    Log.Debug("Cloud sync status updated: Not configured");
                }

                // Hide upload progress by default
                status.ShowUploadProgress = false;

                // Fire event for UI update
                CloudSyncStatusUpdated?.Invoke(this, new CloudSyncStatusUpdatedEventArgs { Status = status });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating cloud sync status: {ex.Message}");
                
                // Fire error status
                var errorStatus = new CloudSyncStatusData
                {
                    StatusText = "Error",
                    StatusColor = Colors.Red,
                    BucketText = "Check settings",
                    BucketColor = Color.FromRgb(136, 136, 136),
                    IconColor = Colors.Red,
                    HasError = true
                };
                CloudSyncStatusUpdated?.Invoke(this, new CloudSyncStatusUpdatedEventArgs { Status = errorStatus });
            }
        }

        private void UpdateCameraStatusInternal()
        {
            try
            {
                string cameraStatus = "No Camera";
                bool isConnected = false;

                if (_cameraManager?.DeviceManager?.SelectedCameraDevice != null)
                {
                    var camera = _cameraManager.DeviceManager.SelectedCameraDevice;
                    var deviceName = camera.DeviceName;
                    
                    // Truncate long camera names for display
                    if (!string.IsNullOrEmpty(deviceName))
                    {
                        if (deviceName.Length > 25)
                        {
                            deviceName = deviceName.Substring(0, 22) + "...";
                        }
                        
                        cameraStatus = deviceName;
                        isConnected = camera.IsConnected;
                        
                        // Add connection status info
                        if (isConnected)
                        {
                            cameraStatus += " (Connected)";
                            if (camera.IsBusy)
                            {
                                cameraStatus += " - Busy";
                            }
                        }
                        else
                        {
                            cameraStatus += " (Disconnected)";
                        }
                        
                        Log.Debug($"Camera status updated: {deviceName}, Connected: {isConnected}, Busy: {camera.IsBusy}");
                    }
                    else
                    {
                        cameraStatus = "Unknown Camera";
                        Log.Debug("Camera detected but no device name available");
                    }
                }
                else
                {
                    Log.Debug("No camera device detected");
                }

                // Fire event for UI update
                CameraStatusUpdated?.Invoke(this, new CameraStatusUpdatedEventArgs
                {
                    Status = cameraStatus,
                    IsConnected = isConnected
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Error updating camera status: {ex.Message}");
                
                // Fire error status
                CameraStatusUpdated?.Invoke(this, new CameraStatusUpdatedEventArgs
                {
                    Status = "Camera Error",
                    IsConnected = false
                });
            }
        }
        #endregion

        #region Printer Status Checking
        private bool CheckPrinterOnline(string printerName)
        {
            try
            {
                if (string.IsNullOrEmpty(printerName))
                    return false;
                
                // Use cached result if checked recently
                if (DateTime.Now - _lastPrinterCheck < _printerCheckCooldown)
                {
                    return _lastPrinterStatus;
                }
                
                // First check if printer is valid
                var settings = new PrinterSettings();
                settings.PrinterName = printerName;
                
                if (!settings.IsValid)
                {
                    _lastPrinterCheck = DateTime.Now;
                    _lastPrinterStatus = false;
                    return false;
                }
                
                bool isOnline = false;
                
                // Check printer status using WMI with timeout
                try
                {
                    // Use more specific WMI query for better performance
                    string query = $"SELECT WorkOffline, PrinterStatus FROM Win32_Printer WHERE Name = '{printerName.Replace("\\", "\\\\")}'";
                    using (var searcher = new ManagementObjectSearcher(query))
                    {
                        searcher.Options.Timeout = TimeSpan.FromSeconds(2); // Add timeout
                        
                        foreach (ManagementObject printer in searcher.Get())
                        {
                            // Check PrinterStatus: 3 = Idle/Ready, 4 = Printing
                            int printerStatus = Convert.ToInt32(printer["PrinterStatus"] ?? 0);
                            bool isOffline = Convert.ToBoolean(printer["WorkOffline"] ?? false);
                            
                            // Printer is online if it's not offline and status is idle (3) or printing (4) or unknown (0)
                            isOnline = !isOffline && (printerStatus == 3 || printerStatus == 4 || printerStatus == 0);
                            break; // We found the printer, no need to continue
                        }
                    }
                }
                catch (Exception wmiEx)
                {
                    Log.Debug($"WMI check failed, falling back to IsValid: {wmiEx.Message}");
                    // If WMI fails, just check if printer is valid
                    isOnline = settings.IsValid;
                }
                
                // Cache the result
                _lastPrinterCheck = DateTime.Now;
                _lastPrinterStatus = isOnline;
                
                return isOnline;
            }
            catch (Exception ex)
            {
                Log.Error($"Error checking printer online status: {ex.Message}");
                _lastPrinterCheck = DateTime.Now;
                _lastPrinterStatus = false;
                return false;
            }
        }
        #endregion

        #region Cleanup
        public void Dispose()
        {
            try
            {
                _isDisposed = true;
                _statusUpdateTimer?.Dispose();
                _statusUpdateTimer = null;
                
                Log.Debug("InfoPanelService disposed");
            }
            catch (Exception ex)
            {
                Log.Error($"Error disposing InfoPanelService: {ex.Message}");
            }
        }
        #endregion
    }

    #region Event Args and Data Classes
    public class PrinterStatusUpdatedEventArgs : EventArgs
    {
        public PrinterStatusData Status { get; set; }
    }

    public class CloudSyncStatusUpdatedEventArgs : EventArgs
    {
        public CloudSyncStatusData Status { get; set; }
    }

    public class CameraStatusUpdatedEventArgs : EventArgs
    {
        public string Status { get; set; }
        public bool IsConnected { get; set; }
    }

    public class PhotoCountUpdatedEventArgs : EventArgs
    {
        public int PhotoCount { get; set; }
        public bool IsVisible { get; set; }
        public string DisplayText { get; set; }
    }

    public class SyncProgressUpdatedEventArgs : EventArgs
    {
        public int PendingCount { get; set; }
        public bool IsUploading { get; set; }
        public int Progress { get; set; }
        public string PendingText { get; set; }
        public string UploadStatusText { get; set; }
        public bool ShowPendingCount { get; set; }
        public bool ShowUploadProgress { get; set; }
    }

    public class PrinterStatusData
    {
        public string Name { get; set; } = "No Printer";
        public string ConnectionText { get; set; } = " (Offline)";
        public Color ConnectionColor { get; set; } = Color.FromRgb(255, 152, 0);
        public string StatusText { get; set; } = "Not Available";
        public Color StatusColor { get; set; } = Color.FromRgb(255, 82, 82);
        public int QueueCount { get; set; } = 0;
        public bool IsOnline { get; set; } = false;
        public bool HasError { get; set; } = false;
        public string ErrorMessage { get; set; } = "";
        public int MediaRemaining { get; set; } = 0;
        public string MediaTypeText { get; set; } = " prints left";
        public bool ShowMediaRemaining { get; set; } = false;
    }

    public class CloudSyncStatusData
    {
        public string StatusText { get; set; } = "Not Configured";
        public Color StatusColor { get; set; } = Color.FromRgb(255, 152, 0);
        public string BucketText { get; set; } = "Setup in settings";
        public Color BucketColor { get; set; } = Color.FromRgb(136, 136, 136);
        public Color IconColor { get; set; } = Color.FromRgb(136, 136, 136);
        public bool IsConfigured { get; set; } = false;
        public bool ShowUploadProgress { get; set; } = false;
        public bool HasError { get; set; } = false;
    }
    
    public class CameraStatusData
    {
        public string CameraName { get; set; } = "No Camera";
        public string StatusText { get; set; } = "Not Connected";
        public Color StatusColor { get; set; } = Color.FromRgb(255, 152, 0);
        public bool IsConnected { get; set; } = false;
        public bool IsReady { get; set; } = false;
        public string ErrorMessage { get; set; }
    }
    
    public class PhotoCountData
    {
        public int SessionPhotos { get; set; } = 0;
        public int TotalPhotos { get; set; } = 0;
        public int RemainingPhotos { get; set; } = 0;
    }
    
    public class SyncProgressData
    {
        public bool IsActive { get; set; } = false;
        public double ProgressPercentage { get; set; } = 0;
        public string StatusMessage { get; set; } = "";
        public string CurrentItem { get; set; } = "";
    }
    #endregion
}