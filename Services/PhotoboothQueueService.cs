using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Clean Architecture Queue Service for managing SMS and QR code availability
    /// Stores SMS requests until valid gallery URLs are available
    /// Manages QR code visibility based on external URL validity
    /// Follows business logic separation principles
    /// </summary>
    public class PhotoboothQueueService
    {
        #region Dependencies and Configuration
        private readonly string _dbPath;
        private readonly IShareService _shareService;
        private readonly Database.TemplateDatabase _templateDatabase;
        private readonly Timer _processingTimer;
        private readonly object _lockObject = new object();
        
        // Cache to reduce database calls for sessions without URLs
        private readonly Dictionary<string, CachedUrlResult> _urlCache = new Dictionary<string, CachedUrlResult>();
        private readonly object _cacheLock = new object();
        
        // Singleton pattern for global queue management
        private static PhotoboothQueueService _instance;
        private static readonly object _singletonLock = new object();
        
        public static PhotoboothQueueService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_singletonLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PhotoboothQueueService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Events
        public event Action<string, string> OnValidUrlAvailable;
        public event Action<string, bool> OnQRCodeVisibilityChanged;
        public event Action<string> OnSMSProcessed;
        public event Action<PhotoboothQueueStatus> OnQueueStatusChanged;
        #endregion

        #region Constructor and Initialization
        private PhotoboothQueueService()
        {
            _shareService = CloudShareProvider.GetShareService();
            _templateDatabase = new Database.TemplateDatabase();
            
            // Create queue database path
            _dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth",
                "photobooth_queue.db");
            
            InitializeDatabase();
            
            // Start background processor (check every 5 minutes to reduce database load)
            _processingTimer = new Timer(ProcessQueueCallback, null, 
                TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
            
            Log.Debug("PhotoboothQueueService: Initialized with clean architecture pattern");
        }

        private void InitializeDatabase()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dbPath));
                
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // SMS queue table - stores SMS requests until valid URLs available
                    var smsCmd = new SQLiteCommand(@"
                        CREATE TABLE IF NOT EXISTS sms_queue (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            session_id TEXT NOT NULL,
                            phone_number TEXT NOT NULL,
                            message_template TEXT,
                            requested_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            processed_at DATETIME,
                            gallery_url TEXT,
                            status TEXT DEFAULT 'pending',
                            retry_count INTEGER DEFAULT 0,
                            is_gallery_session BOOLEAN DEFAULT 0
                        )", conn);
                    smsCmd.ExecuteNonQuery();
                    
                    // QR code visibility tracking
                    var qrCmd = new SQLiteCommand(@"
                        CREATE TABLE IF NOT EXISTS qr_visibility (
                            session_id TEXT PRIMARY KEY,
                            has_valid_url BOOLEAN DEFAULT 0,
                            gallery_url TEXT,
                            last_checked DATETIME DEFAULT CURRENT_TIMESTAMP,
                            is_gallery_session BOOLEAN DEFAULT 0
                        )", conn);
                    qrCmd.ExecuteNonQuery();
                }
                
                Log.Debug("PhotoboothQueueService: Database initialized successfully");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Database initialization failed: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region SMS Queue Management
        /// <summary>
        /// Queue SMS for sending when valid gallery URL becomes available
        /// </summary>
        public async Task<QueueSmsResult> QueueSmsAsync(string sessionId, string phoneNumber, bool isGallerySession = false, string customMessage = null)
        {
            try
            {
                Log.Debug($"PhotoboothQueueService: Queueing SMS for session {sessionId}, phone {phoneNumber}, isGallery: {isGallerySession}");
                
                if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(phoneNumber))
                {
                    return new QueueSmsResult
                    {
                        Success = false,
                        Message = "Session ID and phone number are required"
                    };
                }

                // Check if we already have a valid URL
                var existingUrl = GetValidGalleryUrl(sessionId, isGallerySession);
                if (!string.IsNullOrEmpty(existingUrl))
                {
                    // Send immediately
                    var sent = await _shareService.SendSMSAsync(phoneNumber, existingUrl);
                    if (sent)
                    {
                        Log.Debug($"PhotoboothQueueService: SMS sent immediately to {phoneNumber} with URL: {existingUrl}");
                        OnSMSProcessed?.Invoke(phoneNumber);
                        
                        return new QueueSmsResult
                        {
                            Success = true,
                            SentImmediately = true,
                            Message = "SMS sent successfully",
                            GalleryUrl = existingUrl
                        };
                    }
                }

                // Queue for later processing
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    var cmd = new SQLiteCommand(@"
                        INSERT INTO sms_queue (session_id, phone_number, message_template, is_gallery_session) 
                        VALUES (@session, @phone, @message, @isGallery)", conn);
                    
                    cmd.Parameters.AddWithValue("@session", sessionId);
                    cmd.Parameters.AddWithValue("@phone", phoneNumber);
                    cmd.Parameters.AddWithValue("@message", customMessage ?? "Your photos are ready! 📸 View and download: {url}");
                    cmd.Parameters.AddWithValue("@isGallery", isGallerySession);
                    cmd.ExecuteNonQuery();
                }

                Log.Debug($"PhotoboothQueueService: SMS queued for session {sessionId}");
                NotifyQueueStatusChanged();
                
                return new QueueSmsResult
                {
                    Success = true,
                    SentImmediately = false,
                    Message = "SMS queued - will send when photos are uploaded"
                };
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error queueing SMS: {ex.Message}");
                return new QueueSmsResult
                {
                    Success = false,
                    Message = $"Failed to queue SMS: {ex.Message}"
                };
            }
        }
        #endregion

        #region QR Code Visibility Management
        /// <summary>
        /// Check if QR code should be visible for given session
        /// </summary>
        public async Task<QRVisibilityResult> CheckQRVisibilityAsync(string sessionId, bool isGallerySession = false)
        {
            try
            {
                Log.Debug($"PhotoboothQueueService: Checking QR visibility for session {sessionId}, isGallery: {isGallerySession}");
                
                if (string.IsNullOrEmpty(sessionId))
                {
                    return new QRVisibilityResult
                    {
                        IsVisible = false,
                        Message = "No session ID provided",
                        EnableSMS = false,  // No SMS without session
                        SMSMessage = "No session available"
                    };
                }

                // Check for valid gallery URL
                var galleryUrl = GetValidGalleryUrl(sessionId, isGallerySession);
                bool hasValidUrl = !string.IsNullOrEmpty(galleryUrl);
                
                // Log URL validation for debugging
                if (!hasValidUrl)
                {
                    Log.Debug($"PhotoboothQueueService: No gallery URL available yet for session {sessionId}");
                }
                else
                {
                    Log.Debug($"PhotoboothQueueService: Gallery URL found for session {sessionId}: {galleryUrl}");
                }
                
                // Update visibility tracking
                await UpdateQRVisibilityTrackingAsync(sessionId, hasValidUrl, galleryUrl, isGallerySession);
                
                // Generate QR code if we have valid URL
                System.Windows.Media.Imaging.BitmapImage qrImage = null;
                if (hasValidUrl)
                {
                    qrImage = _shareService.GenerateQRCode(galleryUrl);
                }

                var result = new QRVisibilityResult
                {
                    IsVisible = hasValidUrl,
                    GalleryUrl = galleryUrl,
                    QRCodeImage = qrImage,
                    Message = hasValidUrl ? "QR code ready" : "Waiting for photos to upload...",
                    EnableSMS = true,  // SMS always enabled - will queue if offline
                    SMSMessage = hasValidUrl ? "SMS ready to send" : "SMS will be queued for sending"
                };

                Log.Debug($"PhotoboothQueueService: QR visibility result - Visible: {hasValidUrl}, URL: {galleryUrl ?? "none"}");
                OnQRCodeVisibilityChanged?.Invoke(sessionId, hasValidUrl);
                
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error checking QR visibility: {ex.Message}");
                return new QRVisibilityResult
                {
                    IsVisible = false,
                    Message = $"Error checking QR visibility: {ex.Message}",
                    EnableSMS = true,  // Still allow SMS queuing on error
                    SMSMessage = "SMS will be queued for sending"
                };
            }
        }

        private async Task UpdateQRVisibilityTrackingAsync(string sessionId, bool hasValidUrl, string galleryUrl, bool isGallerySession)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    var cmd = new SQLiteCommand(@"
                        INSERT OR REPLACE INTO qr_visibility 
                        (session_id, has_valid_url, gallery_url, last_checked, is_gallery_session) 
                        VALUES (@session, @hasUrl, @url, CURRENT_TIMESTAMP, @isGallery)", conn);
                    
                    cmd.Parameters.AddWithValue("@session", sessionId);
                    cmd.Parameters.AddWithValue("@hasUrl", hasValidUrl);
                    cmd.Parameters.AddWithValue("@url", galleryUrl ?? "");
                    cmd.Parameters.AddWithValue("@isGallery", isGallerySession);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error updating QR visibility tracking: {ex.Message}");
            }
        }
        #endregion

        #region URL Validation and Retrieval
        /// <summary>
        /// Get valid gallery URL for session from database with caching to reduce database load
        /// </summary>
        private string GetValidGalleryUrl(string sessionId, bool isGallerySession)
        {
            try
            {
                // Create cache key
                string cacheKey = $"{sessionId}_{isGallerySession}";
                
                lock (_cacheLock)
                {
                    // Check cache first
                    if (_urlCache.ContainsKey(cacheKey))
                    {
                        var cached = _urlCache[cacheKey];
                        
                        // If we have a valid URL, return it
                        if (!string.IsNullOrEmpty(cached.Url))
                        {
                            return cached.Url;
                        }
                        
                        // If it's been less than 30 seconds since we checked for null, return cached null
                        // But allow more frequent checks so URLs appear faster after upload
                        if ((DateTime.Now - cached.LastChecked).TotalSeconds < 30)
                        {
                            return cached.Url;
                        }
                    }
                }
                
                Log.Debug($"PhotoboothQueueService.GetValidGalleryUrl: Cache miss - querying database for sessionId='{sessionId}', isGallerySession={isGallerySession}");
                
                string galleryUrl = null;
                if (isGallerySession)
                {
                    galleryUrl = _templateDatabase.GetPhotoSessionGalleryUrl(sessionId);
                }
                else
                {
                    galleryUrl = _templateDatabase.GetPhotoSessionGalleryUrl(sessionId);
                }
                
                // Update cache
                lock (_cacheLock)
                {
                    _urlCache[cacheKey] = new CachedUrlResult
                    {
                        Url = galleryUrl,
                        LastChecked = DateTime.Now
                    };
                    
                    // Clean old cache entries (older than 10 minutes)
                    var cutoff = DateTime.Now.AddMinutes(-10);
                    var keysToRemove = _urlCache.Where(kvp => kvp.Value.LastChecked < cutoff).Select(kvp => kvp.Key).ToList();
                    foreach (var key in keysToRemove)
                    {
                        _urlCache.Remove(key);
                    }
                }
                
                Log.Debug($"PhotoboothQueueService.GetValidGalleryUrl: Retrieved and cached URL: {galleryUrl ?? "NULL"}");
                return galleryUrl;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error getting gallery URL for session {sessionId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validate if URL is external and accessible - STRICT validation to prevent local URLs
        /// </summary>
        private bool IsValidExternalUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // CRITICAL: Block all local file URLs
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return false;

            // Block localhost, local IPs, and development URLs
            if (url.Contains("localhost") || url.Contains("127.0.0.1") || url.Contains("192.168.") || 
                url.Contains("10.0.") || url.Contains("172.16.") || url.Contains("pending/"))
                return false;

            // Must be valid HTTP/HTTPS URL
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result) || 
                (result.Scheme != Uri.UriSchemeHttp && result.Scheme != Uri.UriSchemeHttps))
                return false;

            // Additional validation: Must have proper domain (not IP-based for local networks)
            if (result.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                result.Host.StartsWith("192.168.") || result.Host.StartsWith("10.0.") || 
                result.Host.StartsWith("172.16.") || result.Host.Equals("127.0.0.1"))
                return false;

            // Must be internet-accessible domain
            return true;
        }
        
        /// <summary>
        /// Invalidate cache for a specific session when URL is updated
        /// </summary>
        public void InvalidateUrlCache(string sessionId, bool isGallerySession = false)
        {
            try
            {
                string cacheKey = $"{sessionId}_{isGallerySession}";
                lock (_cacheLock)
                {
                    if (_urlCache.ContainsKey(cacheKey))
                    {
                        _urlCache.Remove(cacheKey);
                        Log.Debug($"PhotoboothQueueService: Invalidated URL cache for session {sessionId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error invalidating cache for session {sessionId}: {ex.Message}");
            }
        }
        #endregion

        #region Background Processing
        private async void ProcessQueueCallback(object state)
        {
            await ProcessPendingQueues();
        }

        /// <summary>
        /// Process pending SMS queue and update QR visibility
        /// </summary>
        public async Task ProcessPendingQueues()
        {
            if (!Monitor.TryEnter(_lockObject, TimeSpan.FromSeconds(1)))
            {
                return; // Skip if already processing
            }

            try
            {
                await ProcessPendingSMS();
                await UpdateQRVisibilities();
                NotifyQueueStatusChanged();
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error processing queues: {ex.Message}");
            }
            finally
            {
                Monitor.Exit(_lockObject);
            }
        }

        private async Task ProcessPendingSMS()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // Get pending SMS with retry limit
                    var cmd = new SQLiteCommand(@"
                        SELECT id, session_id, phone_number, message_template, is_gallery_session, retry_count
                        FROM sms_queue 
                        WHERE status = 'pending' AND retry_count < 3
                        ORDER BY requested_at", conn);
                    
                    var pendingSms = new List<dynamic>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            pendingSms.Add(new
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("id")),
                                SessionId = reader.GetString(reader.GetOrdinal("session_id")),
                                PhoneNumber = reader.GetString(reader.GetOrdinal("phone_number")),
                                MessageTemplate = reader.GetString(reader.GetOrdinal("message_template")),
                                IsGallerySession = reader.GetBoolean(reader.GetOrdinal("is_gallery_session")),
                                RetryCount = reader.GetInt32(reader.GetOrdinal("retry_count"))
                            });
                        }
                    }

                    // Process each pending SMS
                    foreach (var sms in pendingSms)
                    {
                        var galleryUrl = GetValidGalleryUrl(sms.SessionId, sms.IsGallerySession);
                        
                        if (!string.IsNullOrEmpty(galleryUrl) && IsValidExternalUrl(galleryUrl))
                        {
                            try
                            {
                                // Send SMS
                                var sent = await _shareService.SendSMSAsync(sms.PhoneNumber, galleryUrl);
                                
                                if (sent)
                                {
                                    // Mark as processed
                                    var updateCmd = new SQLiteCommand(@"
                                        UPDATE sms_queue 
                                        SET status = 'sent', processed_at = CURRENT_TIMESTAMP, gallery_url = @url
                                        WHERE id = @id", conn);
                                    
                                    updateCmd.Parameters.AddWithValue("@url", galleryUrl);
                                    updateCmd.Parameters.AddWithValue("@id", sms.Id);
                                    updateCmd.ExecuteNonQuery();
                                    
                                    Log.Debug($"PhotoboothQueueService: SMS sent to {sms.PhoneNumber} for session {sms.SessionId}");
                                    OnSMSProcessed?.Invoke(sms.PhoneNumber);
                                    OnValidUrlAvailable?.Invoke(sms.SessionId, galleryUrl);
                                }
                                else
                                {
                                    // Increment retry count
                                    IncrementRetryCount("sms_queue", sms.Id, conn);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"PhotoboothQueueService: Error sending SMS to {sms.PhoneNumber}: {ex.Message}");
                                IncrementRetryCount("sms_queue", sms.Id, conn);
                            }
                        }
                        else
                        {
                            // Still no valid URL, increment retry but don't fail yet
                            IncrementRetryCount("sms_queue", sms.Id, conn);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error processing pending SMS: {ex.Message}");
            }
        }

        private async Task UpdateQRVisibilities()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // Get sessions to check - check more frequently for sessions without URLs
                    var cmd = new SQLiteCommand(@"
                        SELECT session_id, is_gallery_session, has_valid_url
                        FROM qr_visibility 
                        WHERE has_valid_url = 0 OR last_checked < datetime('now', '-30 seconds')", conn);
                    
                    var sessionsToCheck = new List<dynamic>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            sessionsToCheck.Add(new
                            {
                                SessionId = reader.GetString(reader.GetOrdinal("session_id")),
                                IsGallerySession = reader.GetBoolean(reader.GetOrdinal("is_gallery_session")),
                                HadValidUrl = reader.GetBoolean(reader.GetOrdinal("has_valid_url"))
                            });
                        }
                    }

                    // Update visibility for each session
                    foreach (var session in sessionsToCheck)
                    {
                        var galleryUrl = GetValidGalleryUrl(session.SessionId, session.IsGallerySession);
                        bool hasValidUrl = !string.IsNullOrEmpty(galleryUrl) && IsValidExternalUrl(galleryUrl);
                        
                        if (hasValidUrl != session.HadValidUrl)
                        {
                            // Status changed, update and notify
                            await UpdateQRVisibilityTrackingAsync(session.SessionId, hasValidUrl, galleryUrl, session.IsGallerySession);
                            OnQRCodeVisibilityChanged?.Invoke(session.SessionId, hasValidUrl);
                            
                            if (hasValidUrl)
                            {
                                OnValidUrlAvailable?.Invoke(session.SessionId, galleryUrl);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error updating QR visibilities: {ex.Message}");
            }
        }

        private void IncrementRetryCount(string table, int id, SQLiteConnection conn)
        {
            var cmd = new SQLiteCommand($"UPDATE {table} SET retry_count = retry_count + 1 WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        #endregion

        #region Queue Status and Management
        /// <summary>
        /// Get current queue status
        /// </summary>
        public PhotoboothQueueStatus GetQueueStatus()
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    var status = new PhotoboothQueueStatus();
                    
                    // Count pending SMS
                    var smsCmd = new SQLiteCommand("SELECT COUNT(*) FROM sms_queue WHERE status = 'pending'", conn);
                    status.PendingSMSCount = Convert.ToInt32(smsCmd.ExecuteScalar());
                    
                    // Count sessions waiting for URLs
                    var qrCmd = new SQLiteCommand("SELECT COUNT(*) FROM qr_visibility WHERE has_valid_url = 0", conn);
                    status.SessionsWaitingForUrls = Convert.ToInt32(qrCmd.ExecuteScalar());
                    
                    // Get recent activity
                    var recentCmd = new SQLiteCommand(@"
                        SELECT COUNT(*) FROM sms_queue 
                        WHERE processed_at > datetime('now', '-1 hour')", conn);
                    status.RecentSMSSent = Convert.ToInt32(recentCmd.ExecuteScalar());
                    
                    return status;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error getting queue status: {ex.Message}");
                return new PhotoboothQueueStatus();
            }
        }

        /// <summary>
        /// Manual cleanup - only removes entries that have been verified as successfully processed
        /// Should only be called after confirming all uploads are complete and URLs are valid
        /// </summary>
        public async Task<CleanupResult> ManualCleanupProcessedEntries()
        {
            var result = new CleanupResult();
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // Only clean SMS that were successfully sent (not failed or pending)
                    var smsCmd = new SQLiteCommand(@"
                        SELECT COUNT(*) FROM sms_queue 
                        WHERE status = 'sent' AND processed_at IS NOT NULL", conn);
                    result.SMSEntriesEligibleForCleanup = Convert.ToInt32(smsCmd.ExecuteScalar());
                    
                    // Only clean QR tracking for sessions that have confirmed valid URLs
                    var qrCmd = new SQLiteCommand(@"
                        SELECT COUNT(*) FROM qr_visibility 
                        WHERE has_valid_url = 1 AND gallery_url IS NOT NULL AND gallery_url != ''", conn);
                    result.QREntriesEligibleForCleanup = Convert.ToInt32(qrCmd.ExecuteScalar());
                    
                    Log.Debug($"PhotoboothQueueService: Manual cleanup scan - {result.SMSEntriesEligibleForCleanup} SMS, {result.QREntriesEligibleForCleanup} QR entries eligible");
                }
                
                result.Success = true;
                result.Message = $"Found {result.SMSEntriesEligibleForCleanup} SMS and {result.QREntriesEligibleForCleanup} QR entries ready for cleanup";
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error scanning for cleanup: {ex.Message}");
                result.Success = false;
                result.Message = $"Cleanup scan failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Execute manual cleanup after confirmation - permanently removes verified processed entries
        /// WARNING: This permanently deletes data. Only call after verifying uploads are complete.
        /// </summary>
        public async Task<CleanupResult> ExecuteManualCleanup(bool confirmCleanup = false)
        {
            var result = new CleanupResult();
            
            if (!confirmCleanup)
            {
                result.Success = false;
                result.Message = "Cleanup not executed - confirmation required. Set confirmCleanup=true to proceed.";
                return result;
            }

            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // Delete only successfully sent SMS entries
                    var smsDeleteCmd = new SQLiteCommand(@"
                        DELETE FROM sms_queue 
                        WHERE status = 'sent' AND processed_at IS NOT NULL", conn);
                    result.SMSEntriesDeleted = smsDeleteCmd.ExecuteNonQuery();
                    
                    // Delete only confirmed valid QR tracking entries
                    var qrDeleteCmd = new SQLiteCommand(@"
                        DELETE FROM qr_visibility 
                        WHERE has_valid_url = 1 AND gallery_url IS NOT NULL AND gallery_url != ''", conn);
                    result.QREntriesDeleted = qrDeleteCmd.ExecuteNonQuery();
                    
                    Log.Debug($"PhotoboothQueueService: Manual cleanup executed - deleted {result.SMSEntriesDeleted} SMS, {result.QREntriesDeleted} QR entries");
                }
                
                result.Success = true;
                result.Message = $"Cleanup completed - deleted {result.SMSEntriesDeleted} SMS and {result.QREntriesDeleted} QR entries";
                NotifyQueueStatusChanged();
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error executing cleanup: {ex.Message}");
                result.Success = false;
                result.Message = $"Cleanup failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Get detailed cleanup status for administrative review
        /// </summary>
        public async Task<CleanupStatusReport> GetCleanupStatusReport()
        {
            var report = new CleanupStatusReport();
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    
                    // SMS statistics
                    var smsStatsCmd = new SQLiteCommand(@"
                        SELECT 
                            COUNT(CASE WHEN status = 'pending' THEN 1 END) as pending,
                            COUNT(CASE WHEN status = 'sent' THEN 1 END) as sent,
                            COUNT(CASE WHEN retry_count >= 3 THEN 1 END) as failed
                        FROM sms_queue", conn);
                    
                    using (var reader = smsStatsCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int pendingOrdinal = reader.GetOrdinal("pending");
                            int sentOrdinal = reader.GetOrdinal("sent");
                            int failedOrdinal = reader.GetOrdinal("failed");
                            
                            report.PendingSMSCount = reader.IsDBNull(pendingOrdinal) ? 0 : reader.GetInt32(pendingOrdinal);
                            report.SentSMSCount = reader.IsDBNull(sentOrdinal) ? 0 : reader.GetInt32(sentOrdinal);
                            report.FailedSMSCount = reader.IsDBNull(failedOrdinal) ? 0 : reader.GetInt32(failedOrdinal);
                        }
                    }
                    
                    // QR statistics
                    var qrStatsCmd = new SQLiteCommand(@"
                        SELECT 
                            COUNT(CASE WHEN has_valid_url = 0 THEN 1 END) as waiting,
                            COUNT(CASE WHEN has_valid_url = 1 THEN 1 END) as ready
                        FROM qr_visibility", conn);
                    
                    using (var reader = qrStatsCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int waitingOrdinal = reader.GetOrdinal("waiting");
                            int readyOrdinal = reader.GetOrdinal("ready");
                            
                            report.QRWaitingCount = reader.IsDBNull(waitingOrdinal) ? 0 : reader.GetInt32(waitingOrdinal);
                            report.QRReadyCount = reader.IsDBNull(readyOrdinal) ? 0 : reader.GetInt32(readyOrdinal);
                        }
                    }
                    
                    // Get oldest entries
                    var oldestCmd = new SQLiteCommand(@"
                        SELECT MIN(requested_at) as oldest_sms FROM sms_queue
                        UNION ALL
                        SELECT MIN(last_checked) as oldest_qr FROM qr_visibility", conn);
                    
                    using (var reader = oldestCmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int oldestSmsOrdinal = reader.GetOrdinal("oldest_sms");
                            report.OldestSMSEntry = reader.IsDBNull(oldestSmsOrdinal) ? null : reader.GetString(oldestSmsOrdinal);
                        }
                        if (reader.Read())
                        {
                            int oldestQrOrdinal = reader.GetOrdinal("oldest_qr");
                            report.OldestQREntry = reader.IsDBNull(oldestQrOrdinal) ? null : reader.GetString(oldestQrOrdinal);
                        }
                    }
                }
                
                report.Success = true;
                return report;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoboothQueueService: Error generating cleanup report: {ex.Message}");
                report.Success = false;
                report.ErrorMessage = ex.Message;
                return report;
            }
        }

        private void NotifyQueueStatusChanged()
        {
            var status = GetQueueStatus();
            OnQueueStatusChanged?.Invoke(status);
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            _processingTimer?.Dispose();
        }
        #endregion
    }

    #region Result Classes
    public class QueueSmsResult
    {
        public bool Success { get; set; }
        public bool SentImmediately { get; set; }
        public string Message { get; set; }
        public string GalleryUrl { get; set; }
    }

    public class QRVisibilityResult
    {
        public bool IsVisible { get; set; }
        public string GalleryUrl { get; set; }
        public System.Windows.Media.Imaging.BitmapImage QRCodeImage { get; set; }
        public string Message { get; set; }
        
        // SMS can be enabled even offline since it will be queued
        public bool EnableSMS { get; set; } = true;  // Default to enabled for offline queuing
        public string SMSMessage { get; set; }
    }

    public class PhotoboothQueueStatus
    {
        public int PendingSMSCount { get; set; }
        public int SessionsWaitingForUrls { get; set; }
        public int RecentSMSSent { get; set; }
    }

    public class CleanupResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int SMSEntriesEligibleForCleanup { get; set; }
        public int QREntriesEligibleForCleanup { get; set; }
        public int SMSEntriesDeleted { get; set; }
        public int QREntriesDeleted { get; set; }
    }

    public class CleanupStatusReport
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int PendingSMSCount { get; set; }
        public int SentSMSCount { get; set; }
        public int FailedSMSCount { get; set; }
        public int QRWaitingCount { get; set; }
        public int QRReadyCount { get; set; }
        public string OldestSMSEntry { get; set; }
        public string OldestQREntry { get; set; }
    }

    public class CachedUrlResult
    {
        public string Url { get; set; }
        public DateTime LastChecked { get; set; }
    }
    #endregion
}