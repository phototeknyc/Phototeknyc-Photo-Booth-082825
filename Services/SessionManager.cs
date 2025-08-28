using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Photobooth.Models;

namespace Photobooth.Services
{
    /// <summary>
    /// Manages photo sessions and their lifecycle
    /// </summary>
    public class SessionManager
    {
        private PhotoSession _currentSession;
        private readonly List<PhotoSession> _recentSessions;
        private readonly string _sessionsPath;
        private readonly IShareService _shareService;
        private readonly OfflineQueueService _queueService;
        
        public event EventHandler<PhotoSession> SessionStarted;
        public event EventHandler<PhotoSession> SessionCompleted;
        public event EventHandler<PhotoSession> SessionShared;
        public event EventHandler<SessionPhoto> PhotoAdded;
        public event EventHandler<SessionShareResult> ShareCompleted;

        public SessionManager()
        {
            _recentSessions = new List<PhotoSession>();
            _shareService = CloudShareProvider.GetShareService();
            _queueService = OfflineQueueService.Instance;
            
            // Subscribe to upload completed events
            _queueService.OnUploadCompleted += OnQueuedUploadCompleted;
            _queueService.OnOnlineStatusChanged += OnOnlineStatusChanged;
            
            // Create sessions directory
            _sessionsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth",
                "Sessions");
            
            Directory.CreateDirectory(_sessionsPath);
            
            // Load recent sessions
            LoadRecentSessions();
        }

        /// <summary>
        /// Get current active session
        /// </summary>
        public PhotoSession CurrentSession => _currentSession;

        /// <summary>
        /// Get recent sessions
        /// </summary>
        public IReadOnlyList<PhotoSession> RecentSessions => _recentSessions.AsReadOnly();

        /// <summary>
        /// Start a new photo session
        /// </summary>
        public PhotoSession StartNewSession(string eventName = null, string eventType = null)
        {
            // Complete any existing session
            if (_currentSession != null && _currentSession.Status == SessionStatus.Active)
            {
                CompleteSession();
            }

            _currentSession = new PhotoSession
            {
                EventName = eventName,
                EventType = eventType
            };

            // Create session directory
            var sessionDir = Path.Combine(_sessionsPath, _currentSession.SessionId);
            Directory.CreateDirectory(sessionDir);
            
            SessionStarted?.Invoke(this, _currentSession);
            
            return _currentSession;
        }

        /// <summary>
        /// Add a photo to the current session
        /// </summary>
        public void AddPhotoToSession(string photoPath)
        {
            if (_currentSession == null || _currentSession.Status != SessionStatus.Active)
            {
                throw new InvalidOperationException("No active session. Start a session first.");
            }

            // Copy photo to session directory
            var sessionDir = Path.Combine(_sessionsPath, _currentSession.SessionId);
            var photoFileName = $"photo_{_currentSession.Photos.Count + 1:D3}_{Path.GetFileName(photoPath)}";
            var destinationPath = Path.Combine(sessionDir, photoFileName);
            
            File.Copy(photoPath, destinationPath, true);
            
            // Add to session
            _currentSession.AddPhoto(destinationPath);
            
            var photo = _currentSession.Photos.Last();
            PhotoAdded?.Invoke(this, photo);
            
            // Save session state
            SaveSession(_currentSession);
        }

        /// <summary>
        /// Complete the current session
        /// </summary>
        public void CompleteSession()
        {
            if (_currentSession == null)
                return;

            _currentSession.Complete();
            _recentSessions.Insert(0, _currentSession);
            
            // Keep only last 50 sessions
            while (_recentSessions.Count > 50)
            {
                _recentSessions.RemoveAt(_recentSessions.Count - 1);
            }
            
            SaveSession(_currentSession);
            SessionCompleted?.Invoke(this, _currentSession);
        }

        /// <summary>
        /// Share a session (upload and generate links) - works offline
        /// </summary>
        public async Task<SessionShareResult> ShareSessionAsync(PhotoSession session, string phoneNumber = null)
        {
            var result = new SessionShareResult
            {
                Session = session,
                Success = false
            };

            try
            {
                session.Status = SessionStatus.Uploading;
                
                // Get all photo paths for the session
                var photoPaths = session.Photos
                    .Where(p => File.Exists(p.FilePath))
                    .Select(p => p.FilePath)
                    .ToList();

                if (!photoPaths.Any())
                {
                    result.ErrorMessage = "No photos to share";
                    return result;
                }

                // Try to upload using queue service (works offline)
                var uploadResult = await _queueService.QueuePhotosForUpload(
                    session.SessionId, 
                    photoPaths);

                if (uploadResult.Success)
                {
                    // Update session with share info
                    session.ShareInfo = new SessionShareInfo
                    {
                        GalleryUrl = uploadResult.GalleryUrl,
                        ShortUrl = uploadResult.ShortUrl ?? uploadResult.GalleryUrl,
                        CreatedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.AddDays(7)
                    };

                    // Save QR code image (always available, even offline)
                    if (uploadResult.QRCodeImage != null)
                    {
                        var qrPath = Path.Combine(_sessionsPath, session.SessionId, "qr_code.png");
                        SaveQRCodeImage(uploadResult.QRCodeImage, qrPath);
                        session.ShareInfo.QRCodeImagePath = qrPath;
                    }

                    // Queue SMS if phone number provided (works offline)
                    if (!string.IsNullOrEmpty(phoneNumber))
                    {
                        session.GuestPhone = phoneNumber;
                        var smsResult = await _queueService.QueueSMS(
                            phoneNumber, 
                            uploadResult.ShortUrl ?? uploadResult.GalleryUrl,
                            session.SessionId);
                        
                        if (smsResult.Success)
                        {
                            session.ShareInfo.SMSSent = smsResult.Immediate;
                            session.ShareInfo.SMSPhoneNumber = phoneNumber;
                            if (smsResult.Immediate)
                            {
                                session.ShareInfo.SMSSentAt = DateTime.Now;
                            }
                        }
                    }

                    // Status depends on whether upload was immediate or queued
                    session.Status = uploadResult.Immediate ? SessionStatus.Shared : SessionStatus.Uploading;
                    
                    // Update uploaded status for photos only if immediate
                    if (uploadResult.Immediate)
                    {
                        foreach (var photo in session.Photos)
                        {
                            photo.IsUploaded = true;
                            photo.UploadedAt = DateTime.Now;
                        }
                    }
                    
                    result.Success = true;
                    result.GalleryUrl = uploadResult.GalleryUrl;
                    result.ShortUrl = uploadResult.ShortUrl ?? uploadResult.GalleryUrl;
                    result.QRCodeImage = uploadResult.QRCodeImage;
                    result.SMSSent = session.ShareInfo.SMSSent;
                    result.IsQueued = !uploadResult.Immediate;
                }
                else
                {
                    session.Status = SessionStatus.Failed;
                    result.ErrorMessage = uploadResult.Message;
                }

                SaveSession(session);
                SessionShared?.Invoke(this, session);
            }
            catch (Exception ex)
            {
                session.Status = SessionStatus.Failed;
                result.ErrorMessage = ex.Message;
                SaveSession(session);
            }

            return result;
        }

        /// <summary>
        /// Get session by ID
        /// </summary>
        public PhotoSession GetSession(string sessionId)
        {
            // Check current session
            if (_currentSession?.SessionId == sessionId)
                return _currentSession;
            
            // Check recent sessions
            var session = _recentSessions.FirstOrDefault(s => s.SessionId == sessionId);
            if (session != null)
                return session;
            
            // Try to load from disk
            var sessionFile = Path.Combine(_sessionsPath, sessionId, "session.json");
            if (File.Exists(sessionFile))
            {
                var json = File.ReadAllText(sessionFile);
                return JsonConvert.DeserializeObject<PhotoSession>(json);
            }
            
            return null;
        }

        /// <summary>
        /// Save session to disk
        /// </summary>
        private void SaveSession(PhotoSession session)
        {
            var sessionDir = Path.Combine(_sessionsPath, session.SessionId);
            Directory.CreateDirectory(sessionDir);
            
            var sessionFile = Path.Combine(sessionDir, "session.json");
            var json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(sessionFile, json);
        }

        /// <summary>
        /// Load recent sessions from disk
        /// </summary>
        private void LoadRecentSessions()
        {
            try
            {
                var sessionDirs = Directory.GetDirectories(_sessionsPath)
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .Take(50);

                foreach (var dir in sessionDirs)
                {
                    var sessionFile = Path.Combine(dir, "session.json");
                    if (File.Exists(sessionFile))
                    {
                        var json = File.ReadAllText(sessionFile);
                        var session = JsonConvert.DeserializeObject<PhotoSession>(json);
                        if (session != null)
                        {
                            _recentSessions.Add(session);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading sessions: {ex.Message}");
            }
        }

        /// <summary>
        /// Save QR code image to file
        /// </summary>
        private void SaveQRCodeImage(System.Windows.Media.Imaging.BitmapImage qrImage, string path)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(qrImage));
            
            using (var fileStream = new FileStream(path, FileMode.Create))
            {
                encoder.Save(fileStream);
            }
        }

        /// <summary>
        /// Handle when a queued upload completes
        /// </summary>
        private void OnQueuedUploadCompleted(string sessionId, string galleryUrl)
        {
            var session = GetSession(sessionId);
            if (session != null)
            {
                session.Status = SessionStatus.Shared;
                session.ShareInfo.GalleryUrl = galleryUrl;
                
                foreach (var photo in session.Photos)
                {
                    photo.IsUploaded = true;
                    photo.UploadedAt = DateTime.Now;
                }
                
                SaveSession(session);
                
                // Notify UI that share is now complete
                ShareCompleted?.Invoke(this, new SessionShareResult
                {
                    Session = session,
                    Success = true,
                    GalleryUrl = galleryUrl,
                    ShortUrl = session.ShareInfo.ShortUrl,
                    IsQueued = false
                });
            }
        }

        /// <summary>
        /// Handle online status changes
        /// </summary>
        private void OnOnlineStatusChanged(bool isOnline)
        {
            System.Diagnostics.Debug.WriteLine($"Online status changed: {isOnline}");
            
            if (isOnline)
            {
                // Process any pending uploads when we come back online
                var pendingSessions = _recentSessions.Where(s => s.Status == SessionStatus.Uploading).ToList();
                System.Diagnostics.Debug.WriteLine($"Found {pendingSessions.Count} pending sessions to upload");
            }
        }

        /// <summary>
        /// Clean up old sessions (older than 30 days)
        /// </summary>
        public void CleanupOldSessions(int daysToKeep = 30)
        {
            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            
            var sessionDirs = Directory.GetDirectories(_sessionsPath);
            foreach (var dir in sessionDirs)
            {
                var dirInfo = new DirectoryInfo(dir);
                if (dirInfo.CreationTime < cutoffDate)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error deleting old session: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Result of sharing a session
    /// </summary>
    public class SessionShareResult
    {
        public PhotoSession Session { get; set; }
        public bool Success { get; set; }
        public string GalleryUrl { get; set; }
        public string ShortUrl { get; set; }
        public System.Windows.Media.Imaging.BitmapImage QRCodeImage { get; set; }
        public bool SMSSent { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsQueued { get; set; }  // True if upload was queued for later
    }
}