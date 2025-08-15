using System;
using System.Collections.Generic;
using System.Linq;

namespace Photobooth.Models
{
    /// <summary>
    /// Represents a photo session with grouped photos
    /// </summary>
    public class PhotoSession
    {
        public string SessionId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public List<SessionPhoto> Photos { get; set; }
        public string EventName { get; set; }
        public string EventType { get; set; }
        public string GuestPhone { get; set; }
        public string GuestName { get; set; }
        public SessionShareInfo ShareInfo { get; set; }
        public SessionStatus Status { get; set; }
        public Dictionary<string, object> Metadata { get; set; }

        public PhotoSession()
        {
            SessionId = GenerateSessionId();
            StartTime = DateTime.Now;
            Photos = new List<SessionPhoto>();
            Metadata = new Dictionary<string, object>();
            Status = SessionStatus.Active;
        }

        public PhotoSession(string sessionId) : this()
        {
            SessionId = sessionId;
        }

        /// <summary>
        /// Add a photo to the session
        /// </summary>
        public void AddPhoto(string filePath, int sequenceNumber = -1)
        {
            var photo = new SessionPhoto
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = this.SessionId,
                FilePath = filePath,
                FileName = System.IO.Path.GetFileName(filePath),
                CapturedAt = DateTime.Now,
                SequenceNumber = sequenceNumber >= 0 ? sequenceNumber : Photos.Count + 1,
                FileSize = new System.IO.FileInfo(filePath).Length
            };
            
            Photos.Add(photo);
        }

        /// <summary>
        /// Complete the session
        /// </summary>
        public void Complete()
        {
            EndTime = DateTime.Now;
            Status = SessionStatus.Completed;
        }

        /// <summary>
        /// Get session duration
        /// </summary>
        public TimeSpan Duration => (EndTime ?? DateTime.Now) - StartTime;

        /// <summary>
        /// Get total size of all photos
        /// </summary>
        public long TotalPhotoSize => Photos.Sum(p => p.FileSize);

        /// <summary>
        /// Get total size in MB
        /// </summary>
        public double TotalPhotoSizeMB => TotalPhotoSize / (1024.0 * 1024.0);

        /// <summary>
        /// Generate unique session ID
        /// </summary>
        private static string GenerateSessionId()
        {
            // Format: YYYYMMDD-HHMMSS-XXXX
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var random = Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();
            return $"{timestamp}-{random}";
        }

        /// <summary>
        /// Create a shareable session name
        /// </summary>
        public string GetShareableName()
        {
            if (!string.IsNullOrEmpty(EventName))
                return EventName;
            
            if (!string.IsNullOrEmpty(GuestName))
                return $"{GuestName}'s Photos";
            
            return $"Session {SessionId.Split('-').Last()}";
        }
    }

    /// <summary>
    /// Individual photo in a session
    /// </summary>
    public class SessionPhoto
    {
        public string Id { get; set; }
        public string SessionId { get; set; }
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public DateTime CapturedAt { get; set; }
        public int SequenceNumber { get; set; }
        public long FileSize { get; set; }
        public string CloudUrl { get; set; }
        public bool IsUploaded { get; set; }
        public DateTime? UploadedAt { get; set; }
        public string ThumbnailPath { get; set; }
    }

    /// <summary>
    /// Session sharing information
    /// </summary>
    public class SessionShareInfo
    {
        public string GalleryUrl { get; set; }
        public string ShortUrl { get; set; }
        public string QRCodeImagePath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool SMSSent { get; set; }
        public string SMSPhoneNumber { get; set; }
        public DateTime? SMSSentAt { get; set; }
    }

    /// <summary>
    /// Session status
    /// </summary>
    public enum SessionStatus
    {
        Active,
        Completed,
        Uploading,
        Shared,
        Failed,
        Expired
    }
}