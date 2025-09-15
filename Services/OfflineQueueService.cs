using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Photobooth.Services
{
    /// <summary>
    /// Manages offline queue for SMS and uploads
    /// </summary>
    public class OfflineQueueService : IDisposable
    {
        private readonly string _dbPath;
        private readonly IShareService _shareService;
        private bool _isOnline = true;
        private System.Threading.Timer _processTimer;
        
        // Upload progress tracking
        private bool _isUploading = false;
        private double _uploadProgress = 0.0;
        private string _currentUploadName = "";
        
        // Track processing to prevent duplicates and implement backoff
        private readonly HashSet<string> _currentlyProcessing = new HashSet<string>();
        private readonly Dictionary<string, DateTime> _lastFailedAttempt = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, int> _failureCount = new Dictionary<string, int>();
        
        // Event for upload progress updates
        public event Action<double, string> UploadProgressChanged;
        
        // Public properties for status monitoring
        public bool IsUploading => _isUploading;
        public double UploadProgress => _uploadProgress;
        public string CurrentUploadName => _currentUploadName;

        // Singleton pattern to avoid multiple instances
        private static OfflineQueueService _instance;
        private static readonly object _lock = new object();
        
        public static OfflineQueueService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new OfflineQueueService();
                        }
                    }
                }
                return _instance;
            }
        }

        private OfflineQueueService()
        {
            _shareService = CloudShareProvider.GetShareService();
            System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Initialized with share service: {_shareService?.GetType().Name}");
            
            // Create queue database
            _dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth",
                "offline_queue.db");
            
            InitializeDatabase();
            
            // Start background processor (check every 30 seconds)
            // Initial delay of 5 seconds to let app initialize, then every 30 seconds
            _processTimer = new System.Threading.Timer(
                ProcessQueue, 
                null, 
                TimeSpan.FromSeconds(5), 
                TimeSpan.FromSeconds(30));
            
            // Check online status
            CheckOnlineStatus();
            System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Online status after check: {_isOnline}");
            
            // Process any pending queues immediately on startup
            Task.Run(async () => 
            {
                await Task.Delay(3000); // Small delay to let services initialize
                System.Diagnostics.Debug.WriteLine("OfflineQueueService: Processing pending queues on startup");
                ProcessQueue(null); // Call the existing ProcessQueue method
            });
        }

        private void InitializeDatabase()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));
            
            using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();
                
                // SMS queue table
                var cmd = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS sms_queue (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        phone_number TEXT NOT NULL,
                        message TEXT NOT NULL,
                        gallery_url TEXT NOT NULL,
                        session_id TEXT,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        retry_count INTEGER DEFAULT 0,
                        sent_at DATETIME,
                        status TEXT DEFAULT 'pending'
                    )", conn);
                cmd.ExecuteNonQuery();
                
                // Add session_id column if it doesn't exist (for migration)
                try
                {
                    var alterCmd = new SQLiteCommand(@"
                        ALTER TABLE sms_queue ADD COLUMN session_id TEXT", conn);
                    alterCmd.ExecuteNonQuery();
                }
                catch
                {
                    // Column already exists, ignore
                }
                
                // Upload queue table
                var cmd2 = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS upload_queue (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id TEXT NOT NULL,
                        photo_paths TEXT NOT NULL,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        retry_count INTEGER DEFAULT 0,
                        event_name TEXT,
                        uploaded_at DATETIME,
                        status TEXT DEFAULT 'pending',
                        gallery_url TEXT
                    )", conn);
                cmd2.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Queue SMS for sending (works offline)
        /// </summary>
        public async Task<QueueResult> QueueSMS(string phoneNumber, string galleryUrl, string sessionId)
        {
            try
            {
                // Only send immediately if we have a valid external URL and are online
                bool hasValidUrl = !string.IsNullOrEmpty(galleryUrl) && 
                                  !galleryUrl.Contains("/pending/") && 
                                  (galleryUrl.StartsWith("http://") || galleryUrl.StartsWith("https://"));
                
                if (_isOnline && hasValidUrl)
                {
                    var message = $"Your photos are ready! 📸\nView and download: {galleryUrl}\nLink expires in 7 days.";
                    var sent = await _shareService.SendSMSAsync(phoneNumber, galleryUrl);
                    if (sent)
                    {
                        return new QueueResult 
                        { 
                            Success = true, 
                            Immediate = true,
                            Message = "SMS sent successfully" 
                        };
                    }
                }
                
                // Queue for later - will send when valid URL is available
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand(@"
                        INSERT INTO sms_queue (phone_number, message, gallery_url, session_id) 
                        VALUES (@phone, @msg, @url, @session)", conn);
                    
                    cmd.Parameters.AddWithValue("@phone", phoneNumber);
                    cmd.Parameters.AddWithValue("@msg", "Your photos are ready! 📸\nView and download: {url}\nLink expires in 7 days.");
                    cmd.Parameters.AddWithValue("@url", galleryUrl ?? "");
                    cmd.Parameters.AddWithValue("@session", sessionId ?? "");
                    cmd.ExecuteNonQuery();
                }
                
                return new QueueResult 
                { 
                    Success = true, 
                    Immediate = false,
                    Message = hasValidUrl ? "SMS queued for sending when online" : "SMS queued - will send when photos are uploaded" 
                };
            }
            catch (Exception ex)
            {
                return new QueueResult 
                { 
                    Success = false, 
                    Message = $"Failed to queue SMS: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// Queue photos for upload (works offline)
        /// </summary>
        public async Task<UploadQueueResult> QueuePhotosForUpload(string sessionId, List<string> photoPaths, string eventName = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"OfflineQueueService.QueuePhotosForUpload: Starting for session {sessionId}, {photoPaths?.Count ?? 0} photos, online={_isOnline}");
                
                // Check if this session is already queued or being processed
                if (_currentlyProcessing.Contains(sessionId))
                {
                    System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Session {sessionId} is already being processed, skipping");
                    return new UploadQueueResult
                    {
                        Success = true,
                        Immediate = false,
                        Message = "Already processing this session"
                    };
                }
                
                // Check if already in queue
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    var checkCmd = new SQLiteCommand(
                        "SELECT COUNT(*) FROM upload_queue WHERE session_id = @session AND status = 'pending'", conn);
                    checkCmd.Parameters.AddWithValue("@session", sessionId);
                    var count = Convert.ToInt32(checkCmd.ExecuteScalar());
                    
                    if (count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Session {sessionId} already in queue, skipping");
                        return new UploadQueueResult
                        {
                            Success = true,
                            Immediate = false,
                            Message = "Already queued for upload"
                        };
                    }
                }
                
                // Check if we should wait due to recent failure
                if (_lastFailedAttempt.ContainsKey(sessionId))
                {
                    var timeSinceLastFail = DateTime.Now - _lastFailedAttempt[sessionId];
                    var failCount = _failureCount.ContainsKey(sessionId) ? _failureCount[sessionId] : 0;
                    var backoffSeconds = Math.Min(30 * Math.Pow(2, failCount), 600);
                    
                    if (timeSinceLastFail.TotalSeconds < backoffSeconds)
                    {
                        System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Session {sessionId} in backoff period, queuing instead");
                        // Queue it instead of trying immediately
                        _isOnline = false; // Force queuing
                    }
                }
                
                // Try immediate upload if online and not in backoff
                if (_isOnline)
                {
                    System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Online - attempting immediate upload");
                    
                    // Mark as processing
                    _currentlyProcessing.Add(sessionId);
                    
                    try
                    {
                        // Set upload status and notify UI
                        _isUploading = true;
                        _currentUploadName = $"Session {sessionId}";
                        _uploadProgress = 0.0;
                        UploadProgressChanged?.Invoke(_uploadProgress, _currentUploadName);
                        
                        // Start a task to simulate progress
                        var progressTask = Task.Run(async () =>
                        {
                            for (int i = 1; i <= 9; i++)
                            {
                                await Task.Delay(200); // Simulate progress every 200ms
                                if (_isUploading)
                                {
                                    _uploadProgress = i * 0.1; // Progress from 10% to 90%
                                    UploadProgressChanged?.Invoke(_uploadProgress, _currentUploadName);
                                }
                            }
                        });
                        
                        System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Calling CreateShareableGalleryAsync on {_shareService?.GetType().Name}");
                        var shareResult = await _shareService.CreateShareableGalleryAsync(sessionId, photoPaths, eventName);
                        
                        // Cancel progress simulation and set to complete
                        _uploadProgress = 1.0;
                        UploadProgressChanged?.Invoke(_uploadProgress, _currentUploadName);
                        
                        // Reset upload status
                        _isUploading = false;
                        _uploadProgress = 0.0;
                        _currentUploadName = "";
                        UploadProgressChanged?.Invoke(0.0, "");
                        
                        if (shareResult.Success)
                        {
                            System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Upload successful! Gallery URL: {shareResult.GalleryUrl}");
                            
                            // Clear failure tracking on success
                            _lastFailedAttempt.Remove(sessionId);
                            _failureCount.Remove(sessionId);
                            
                            return new UploadQueueResult
                            {
                                Success = true,
                                Immediate = true,
                                GalleryUrl = shareResult.GalleryUrl,
                                ShortUrl = shareResult.ShortUrl,
                                QRCodeImage = shareResult.QRCodeImage
                            };
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Upload failed - {shareResult.ErrorMessage}");
                            
                            // Track failure for backoff
                            _lastFailedAttempt[sessionId] = DateTime.Now;
                            _failureCount[sessionId] = (_failureCount.ContainsKey(sessionId) ? _failureCount[sessionId] : 0) + 1;
                            
                            // Mark as offline to prevent further immediate attempts
                            _isOnline = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Exception during upload: {ex.Message}");
                        
                        // Track failure for backoff
                        _lastFailedAttempt[sessionId] = DateTime.Now;
                        _failureCount[sessionId] = (_failureCount.ContainsKey(sessionId) ? _failureCount[sessionId] : 0) + 1;
                        
                        // Mark as offline
                        _isOnline = false;
                    }
                    finally
                    {
                        // Always remove from processing set
                        _currentlyProcessing.Remove(sessionId);
                    }
                }
                
                // Queue for later if offline
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand(@"
                        INSERT INTO upload_queue (session_id, photo_paths, event_name) 
                        VALUES (@session, @paths, @event)", conn);
                    
                    cmd.Parameters.AddWithValue("@session", sessionId);
                    cmd.Parameters.AddWithValue("@paths", JsonConvert.SerializeObject(photoPaths));
                    cmd.Parameters.AddWithValue("@event", eventName ?? "general");
                    cmd.ExecuteNonQuery();
                }
                
                // Generate local QR code pointing to pending page
                var pendingUrl = $"https://photos.app/pending/{sessionId}";
                var qrCode = _shareService.GenerateQRCode(pendingUrl);
                
                // Trigger immediate processing if we're online
                if (_isOnline)
                {
                    Task.Run(() => ProcessQueue(null));
                }
                
                return new UploadQueueResult
                {
                    Success = true,
                    Immediate = false,
                    IsPending = true,
                    GalleryUrl = pendingUrl,
                    QRCodeImage = qrCode,
                    Message = "Photos will upload when online"
                };
            }
            catch (Exception ex)
            {
                return new UploadQueueResult
                {
                    Success = false,
                    Message = $"Failed to queue upload: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Process queued items when online
        /// </summary>
        public async void ProcessQueue(object state)
        {
            if (!_isOnline)
            {
                CheckOnlineStatus();
                if (!_isOnline) return;
            }
            
            await ProcessUploadQueue();
            await ProcessSMSQueue();
        }

        /// <summary>
        /// Process pending uploads
        /// </summary>
        private async Task ProcessUploadQueue()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // Get all pending uploads - no retry limit, keep trying until successful
                    var cmd = new SQLiteCommand(
                        "SELECT * FROM upload_queue WHERE status = 'pending'", conn);
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        var uploads = new List<dynamic>();
                        while (reader.Read())
                        {
                            uploads.Add(new
                            {
                                Id = reader.GetInt32(0),
                                SessionId = reader.GetString(1),
                                PhotoPaths = JsonConvert.DeserializeObject<List<string>>(reader.GetString(2)),
                                EventName = reader.IsDBNull(6) ? null : reader.GetString(6)
                            });
                        }
                        
                        // Process each upload
                        int currentIndex = 0;
                        foreach (var upload in uploads)
                        {
                            try
                            {
                                // Check if already processing this session
                                if (_currentlyProcessing.Contains(upload.SessionId))
                                {
                                    System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Skipping {upload.SessionId} - already processing");
                                    continue;
                                }
                                
                                // Check backoff period after failures
                                if (_lastFailedAttempt.ContainsKey(upload.SessionId))
                                {
                                    var timeSinceLastFail = DateTime.Now - _lastFailedAttempt[upload.SessionId];
                                    var failCount = _failureCount.ContainsKey(upload.SessionId) ? _failureCount[upload.SessionId] : 0;
                                    
                                    // Exponential backoff: 30s, 1m, 2m, 4m, 8m, then cap at 10m
                                    var backoffSeconds = Math.Min(30 * Math.Pow(2, failCount), 600);
                                    
                                    if (timeSinceLastFail.TotalSeconds < backoffSeconds)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Skipping {upload.SessionId} - backoff for {backoffSeconds - timeSinceLastFail.TotalSeconds:F0}s more");
                                        continue;
                                    }
                                }
                                
                                // Mark as processing
                                _currentlyProcessing.Add(upload.SessionId);
                                
                                // Update progress tracking
                                _isUploading = true;
                                _currentUploadName = $"Session {upload.SessionId}";
                                _uploadProgress = (double)currentIndex / uploads.Count;
                                
                                // Notify UI of progress
                                UploadProgressChanged?.Invoke(_uploadProgress, _currentUploadName);
                                
                                var result = await _shareService.CreateShareableGalleryAsync(
                                    upload.SessionId, 
                                    upload.PhotoPaths,
                                    upload.EventName);
                                
                                currentIndex++;
                                _uploadProgress = (double)currentIndex / uploads.Count;
                                UploadProgressChanged?.Invoke(_uploadProgress, _currentUploadName);
                                
                                if (result.Success)
                                {
                                    // Mark as uploaded
                                    var updateCmd = new SQLiteCommand(@"
                                        UPDATE upload_queue 
                                        SET status = 'completed', 
                                            uploaded_at = CURRENT_TIMESTAMP,
                                            gallery_url = @url
                                        WHERE id = @id", conn);
                                    
                                    updateCmd.Parameters.AddWithValue("@url", result.GalleryUrl);
                                    updateCmd.Parameters.AddWithValue("@id", upload.Id);
                                    updateCmd.ExecuteNonQuery();
                                    
                                    // Clear failure tracking
                                    _lastFailedAttempt.Remove(upload.SessionId);
                                    _failureCount.Remove(upload.SessionId);
                                    
                                    // Update SMS queue with real URL
                                    UpdateSMSQueueUrls(upload.SessionId, result.GalleryUrl);
                                    
                                    // Notify UI that upload completed
                                    OnUploadCompleted?.Invoke(upload.SessionId, result.GalleryUrl);
                                }
                                else
                                {
                                    // Track failure for backoff
                                    _lastFailedAttempt[upload.SessionId] = DateTime.Now;
                                    _failureCount[upload.SessionId] = (_failureCount.ContainsKey(upload.SessionId) ? _failureCount[upload.SessionId] : 0) + 1;
                                    
                                    // Increment retry count
                                    IncrementRetryCount("upload_queue", upload.Id, conn);
                                    
                                    // Mark as offline if we get connection errors
                                    _isOnline = false;
                                    System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Upload failed for {upload.SessionId}, marking as offline");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Exception processing {upload.SessionId}: {ex.Message}");
                                
                                // Track failure for backoff
                                _lastFailedAttempt[upload.SessionId] = DateTime.Now;
                                _failureCount[upload.SessionId] = (_failureCount.ContainsKey(upload.SessionId) ? _failureCount[upload.SessionId] : 0) + 1;
                                
                                IncrementRetryCount("upload_queue", upload.Id, conn);
                                _isOnline = false;
                            }
                            finally
                            {
                                // Remove from processing set
                                _currentlyProcessing.Remove(upload.SessionId);
                            }
                        }
                        
                        // Reset upload status when done
                        _isUploading = false;
                        _uploadProgress = 0.0;
                        _currentUploadName = "";
                        UploadProgressChanged?.Invoke(0.0, "");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing upload queue: {ex.Message}");
            }
            finally
            {
                // Ensure upload status is reset on error
                _isUploading = false;
                _uploadProgress = 0.0;
                _currentUploadName = "";
            }
        }

        /// <summary>
        /// Process pending SMS messages
        /// </summary>
        private async Task ProcessSMSQueue()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // Get all pending SMS - no retry limit, keep trying until successful
                    var cmd = new SQLiteCommand(
                        "SELECT * FROM sms_queue WHERE status = 'pending'", conn);
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        var messages = new List<dynamic>();
                        while (reader.Read())
                        {
                            messages.Add(new
                            {
                                Id = reader.GetInt32(0),
                                PhoneNumber = reader.GetString(1),
                                GalleryUrl = reader.GetString(3)
                            });
                        }
                        
                        // Send each SMS - but only if we have a valid URL
                        foreach (var sms in messages)
                        {
                            try
                            {
                                // Check if we have a valid URL (not a pending/fake URL)
                                bool hasValidUrl = !string.IsNullOrEmpty(sms.GalleryUrl) && 
                                                  !sms.GalleryUrl.Contains("/pending/") && 
                                                  (sms.GalleryUrl.StartsWith("http://") || sms.GalleryUrl.StartsWith("https://"));
                                
                                if (!hasValidUrl)
                                {
                                    // Skip this SMS - URL not ready yet
                                    System.Diagnostics.Debug.WriteLine($"Skipping SMS for {sms.PhoneNumber} - URL not ready: {sms.GalleryUrl}");
                                    continue;
                                }
                                
                                var sent = await _shareService.SendSMSAsync(
                                    sms.PhoneNumber, 
                                    sms.GalleryUrl);
                                
                                if (sent)
                                {
                                    // Mark as sent
                                    var updateCmd = new SQLiteCommand(@"
                                        UPDATE sms_queue 
                                        SET status = 'sent', sent_at = CURRENT_TIMESTAMP 
                                        WHERE id = @id", conn);
                                    
                                    updateCmd.Parameters.AddWithValue("@id", sms.Id);
                                    updateCmd.ExecuteNonQuery();
                                    
                                    // Notify UI
                                    OnSMSSent?.Invoke(sms.PhoneNumber);
                                }
                                else
                                {
                                    IncrementRetryCount("sms_queue", sms.Id, conn);
                                }
                            }
                            catch
                            {
                                IncrementRetryCount("sms_queue", sms.Id, conn);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing SMS queue: {ex.Message}");
            }
        }

        /// <summary>
        /// Update SMS queue with real gallery URL when available
        /// </summary>
        public void UpdateSMSQueueUrls(string sessionId, string galleryUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(galleryUrl))
                    return;
                
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand(@"
                        UPDATE sms_queue 
                        SET gallery_url = @url 
                        WHERE session_id = @session AND status = 'pending'", conn);
                    
                    cmd.Parameters.AddWithValue("@url", galleryUrl);
                    cmd.Parameters.AddWithValue("@session", sessionId);
                    var updated = cmd.ExecuteNonQuery();
                    
                    if (updated > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Updated {updated} SMS entries with gallery URL for session {sessionId}");
                        
                        // Trigger immediate processing since we now have a valid URL
                        Task.Run(async () => await ProcessSMSQueue());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating SMS queue URLs: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if we're online
        /// </summary>
        private void CheckOnlineStatus()
        {
            bool wasOffline = !_isOnline;
            
            try
            {
                // Use .NET's network availability check
                _isOnline = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                
                if (_isOnline != !wasOffline)
                {
                    System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Network status changed - Online: {_isOnline}");
                    
                    // If we just came online, clear failure tracking to retry immediately
                    if (_isOnline && wasOffline)
                    {
                        _lastFailedAttempt.Clear();
                        _failureCount.Clear();
                        System.Diagnostics.Debug.WriteLine("OfflineQueueService: Came online - clearing backoff timers");
                    }
                }
            }
            catch (Exception ex)
            {
                // Still set online to true - let AWS SDK handle connectivity
                _isOnline = true;
                System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Exception in online check (ignoring): {ex.Message}");
            }
            
            OnOnlineStatusChanged?.Invoke(_isOnline);
            
            // If we just came back online, trigger immediate processing
            if (wasOffline && _isOnline)
            {
                Task.Run(() => ProcessQueue(null));
            }
        }

        private void IncrementRetryCount(string table, int id, SQLiteConnection conn)
        {
            var cmd = new SQLiteCommand(
                $"UPDATE {table} SET retry_count = retry_count + 1 WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Get queue status
        /// </summary>
        public QueueStatus GetQueueStatus()
        {
            using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();
                
                var status = new QueueStatus 
                { 
                    IsOnline = _isOnline,
                    IsUploading = _isUploading,
                    UploadProgress = _uploadProgress,
                    CurrentUploadName = _currentUploadName
                };
                
                // Count pending uploads
                var cmd = new SQLiteCommand(
                    "SELECT COUNT(*) FROM upload_queue WHERE status = 'pending'", conn);
                status.PendingUploads = Convert.ToInt32(cmd.ExecuteScalar());
                
                // Count pending SMS
                cmd = new SQLiteCommand(
                    "SELECT COUNT(*) FROM sms_queue WHERE status = 'pending'", conn);
                status.PendingSMS = Convert.ToInt32(cmd.ExecuteScalar());
                
                return status;
            }
        }

        // Events
        public event Action<string, string> OnUploadCompleted;
        public event Action<string> OnSMSSent;
        public event Action<bool> OnOnlineStatusChanged;
        
        // IDisposable implementation
        public void Dispose()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OfflineQueueService: Disposing...");
                _processTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                _processTimer?.Dispose();
                _processTimer = null;
                System.Diagnostics.Debug.WriteLine("OfflineQueueService: Disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OfflineQueueService: Error during dispose: {ex.Message}");
            }
        }
    }

    public class QueueResult
    {
        public bool Success { get; set; }
        public bool Immediate { get; set; }
        public string Message { get; set; }
    }

    public class UploadQueueResult : QueueResult
    {
        public string GalleryUrl { get; set; }
        public string ShortUrl { get; set; }
        public System.Windows.Media.Imaging.BitmapImage QRCodeImage { get; set; }
        public bool IsPending { get; set; }
    }

    public class QueueStatus
    {
        public bool IsOnline { get; set; }
        public int PendingUploads { get; set; }
        public int PendingSMS { get; set; }
        public bool IsUploading { get; set; }
        public double UploadProgress { get; set; } // 0.0 to 1.0
        public string CurrentUploadName { get; set; }
    }
}