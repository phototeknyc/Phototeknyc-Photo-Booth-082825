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
    public class OfflineQueueService
    {
        private readonly string _dbPath;
        private readonly IShareService _shareService;
        private bool _isOnline = true;
        private System.Threading.Timer _processTimer;
        
        // Upload progress tracking
        private bool _isUploading = false;
        private double _uploadProgress = 0.0;
        private string _currentUploadName = "";
        
        // Event for upload progress updates
        public event Action<double, string> UploadProgressChanged;

        public OfflineQueueService()
        {
            _shareService = CloudShareProvider.GetShareService();
            
            // Create queue database
            _dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth",
                "offline_queue.db");
            
            InitializeDatabase();
            
            // Start background processor (check every 30 seconds)
            _processTimer = new System.Threading.Timer(
                ProcessQueue, 
                null, 
                TimeSpan.FromSeconds(30), 
                TimeSpan.FromSeconds(30));
            
            // Check online status
            CheckOnlineStatus();
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
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        retry_count INTEGER DEFAULT 0,
                        sent_at DATETIME,
                        status TEXT DEFAULT 'pending'
                    )", conn);
                cmd.ExecuteNonQuery();
                
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
                var message = $"Your photos are ready! 📸\nView and download: {galleryUrl}\nLink expires in 7 days.";
                
                // Try to send immediately if online
                if (_isOnline)
                {
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
                
                // Queue for later if offline or failed
                using (var conn = new SQLiteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand(@"
                        INSERT INTO sms_queue (phone_number, message, gallery_url) 
                        VALUES (@phone, @msg, @url)", conn);
                    
                    cmd.Parameters.AddWithValue("@phone", phoneNumber);
                    cmd.Parameters.AddWithValue("@msg", message);
                    cmd.Parameters.AddWithValue("@url", galleryUrl);
                    cmd.ExecuteNonQuery();
                }
                
                return new QueueResult 
                { 
                    Success = true, 
                    Immediate = false,
                    Message = "SMS queued for sending when online" 
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
                // Try immediate upload if online
                if (_isOnline)
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
                        return new UploadQueueResult
                        {
                            Success = true,
                            Immediate = true,
                            GalleryUrl = shareResult.GalleryUrl,
                            ShortUrl = shareResult.ShortUrl,
                            QRCodeImage = shareResult.QRCodeImage
                        };
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
        private async void ProcessQueue(object state)
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
                    
                    // Get pending uploads
                    var cmd = new SQLiteCommand(
                        "SELECT * FROM upload_queue WHERE status = 'pending' AND retry_count < 3", conn);
                    
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
                                    
                                    // Notify UI that upload completed
                                    OnUploadCompleted?.Invoke(upload.SessionId, result.GalleryUrl);
                                }
                                else
                                {
                                    // Increment retry count
                                    IncrementRetryCount("upload_queue", upload.Id, conn);
                                }
                            }
                            catch
                            {
                                IncrementRetryCount("upload_queue", upload.Id, conn);
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
                    
                    // Get pending SMS
                    var cmd = new SQLiteCommand(
                        "SELECT * FROM sms_queue WHERE status = 'pending' AND retry_count < 3", conn);
                    
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
                        
                        // Send each SMS
                        foreach (var sms in messages)
                        {
                            try
                            {
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
        /// Check if we're online
        /// </summary>
        private void CheckOnlineStatus()
        {
            bool wasOffline = !_isOnline;
            
            try
            {
                using (var client = new System.Net.WebClient())
                {
                    using (client.OpenRead("http://www.google.com"))
                    {
                        _isOnline = true;
                    }
                }
            }
            catch
            {
                _isOnline = false;
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