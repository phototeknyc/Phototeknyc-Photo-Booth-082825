using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Photobooth.Models;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for managing gallery browsing functionality
    /// Handles all business logic for the gallery browser modal
    /// </summary>
    public class GalleryBrowserService
    {
        #region Events
        public event EventHandler<GalleryBrowseLoadedEventArgs> GalleryBrowseLoaded;
        public event EventHandler<GalleryBrowseErrorEventArgs> GalleryBrowseError;
        public event EventHandler<SessionViewRequestedEventArgs> SessionViewRequested;
        public event EventHandler<GalleryPreviewUpdateEventArgs> PreviewUpdateRequested;
        #endregion

        #region Properties
        private string _photoDirectory;
        private List<GallerySessionInfo> _cachedSessions;
        private DateTime _lastRefresh;
        private readonly TimeSpan _cacheTimeout = TimeSpan.FromMinutes(5);
        private readonly Database.TemplateDatabase _database;
        private int? _currentEventId;
        #endregion

        public GalleryBrowserService()
        {
            // Initialize database
            _database = new Database.TemplateDatabase();
            
            // Get photo directory from settings
            _photoDirectory = Properties.Settings.Default.PhotoLocation;
            if (string.IsNullOrEmpty(_photoDirectory))
            {
                _photoDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth"
                );
            }
        }

        /// <summary>
        /// Set the current event ID for filtering sessions
        /// </summary>
        public void SetCurrentEventId(int? eventId)
        {
            System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Setting current event ID to {eventId}");
            _currentEventId = eventId;
            // Clear cache when event changes to force reload
            _cachedSessions = null;
            _lastRefresh = DateTime.MinValue;
        }

        /// <summary>
        /// Load all gallery sessions for browsing
        /// </summary>
        public async Task<GalleryBrowseData> LoadGallerySessionsAsync(bool forceRefresh = false)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("GalleryBrowserService: Loading gallery sessions");

                // Check cache
                if (!forceRefresh && _cachedSessions != null && 
                    DateTime.Now - _lastRefresh < _cacheTimeout)
                {
                    System.Diagnostics.Debug.WriteLine("GalleryBrowserService: Using cached sessions");
                    return CreateBrowseData(_cachedSessions);
                }

                // Load sessions from database first, fallback to disk
                var sessions = await Task.Run(() => LoadSessionsFromDatabase(0, 20) ?? LoadSessionsFromDisk());
                
                // Update cache
                _cachedSessions = sessions;
                _lastRefresh = DateTime.Now;

                var browseData = CreateBrowseData(sessions);
                
                // Fire loaded event
                GalleryBrowseLoaded?.Invoke(this, new GalleryBrowseLoadedEventArgs
                {
                    BrowseData = browseData,
                    TotalSessions = sessions.Count,
                    TotalPhotos = sessions.Sum(s => s.PhotoCount)
                });

                return browseData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error loading sessions: {ex.Message}");
                
                GalleryBrowseError?.Invoke(this, new GalleryBrowseErrorEventArgs
                {
                    Error = ex,
                    Operation = "LoadGallerySessions"
                });

                return new GalleryBrowseData { Sessions = new List<GallerySessionInfo>() };
            }
        }

        /// <summary>
        /// Request to view a specific session in the main view
        /// </summary>
        public void RequestSessionView(GallerySessionInfo session)
        {
            if (session == null)
            {
                System.Diagnostics.Debug.WriteLine("GalleryBrowserService: Invalid session for viewing");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Requesting view for session: {session.SessionName}");
            
            // Load full photo details only when session is selected for viewing
            // Check if we have all photos (optimized load only loads 1 photo for thumbnail)
            if (session.Photos == null || session.Photos.Count < session.PhotoCount)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Loading all photos - current: {session.Photos?.Count ?? 0}, expected: {session.PhotoCount}");
                LoadSessionPhotosOnDemand(session);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Photos already loaded: {session.Photos.Count}");
            }

            SessionViewRequested?.Invoke(this, new SessionViewRequestedEventArgs
            {
                Session = session,
                SessionIndex = _cachedSessions?.IndexOf(session) ?? 0,
                TotalSessions = _cachedSessions?.Count ?? 1
            });
        }
        
        private void LoadSessionPhotosOnDemand(GallerySessionInfo session)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Loading photos on demand for session {session.SessionId}");
                
                // Get the database session by GUID
                var dbSessions = _database.GetPhotoSessions();
                var dbSession = dbSessions.FirstOrDefault(s => s.SessionGuid == session.SessionId);
                
                if (dbSession != null)
                {
                    var photos = _database.GetSessionPhotos(dbSession.Id);
                    var composedImages = _database.GetSessionComposedImages(dbSession.Id);
                    
                    // Clear any existing photos (from optimized load) and load all photos
                    session.Photos = new List<GalleryPhotoInfo>();
                    
                    // Add photos
                    System.Diagnostics.Debug.WriteLine($"★★★ GalleryBrowserService: Processing {photos.Count} photos from database for session {session.SessionId}");
                    foreach (var photo in photos)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Checking photo: {photo.FileName} at {photo.FilePath}");
                        if (File.Exists(photo.FilePath))
                        {
                            System.Diagnostics.Debug.WriteLine($"    ✓ File exists, adding with type: {photo.PhotoType ?? "ORIG"}");
                            session.Photos.Add(new GalleryPhotoInfo
                            {
                                FilePath = photo.FilePath,
                                FileName = photo.FileName,
                                PhotoType = photo.PhotoType ?? "ORIG",
                                FileSize = photo.FileSize ?? 0
                            });
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"    ✗ FILE NOT FOUND, skipping: {photo.FilePath}");
                        }
                    }
                    
                    // Add composed images - group by type to avoid duplicates
                    // Only add ONE of each type (COMP, GIF, MP4) to prevent duplicates
                    // INCLUDE 4x6_print versions for printing from gallery
                    var composedByType = composedImages
                                                       
                                                       .GroupBy(c => c.OutputFormat ?? "COMP")
                                                       .Select(g => g.OrderByDescending(c => c.CreatedDate).First());
                    
                    foreach (var composed in composedByType)
                    {
                        if (File.Exists(composed.FilePath))
                        {
                            session.Photos.Add(new GalleryPhotoInfo
                            {
                                FilePath = composed.FilePath,
                                FileName = composed.FileName,
                                PhotoType = composed.OutputFormat ?? "COMP",
                                FileSize = composed.FileSize ?? 0
                            });
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Loaded {session.Photos.Count} photos for session:");
                    System.Diagnostics.Debug.WriteLine($"  - {photos.Count} regular photos");
                    System.Diagnostics.Debug.WriteLine($"  - {composedImages.Count} composed images");
                    foreach (var photo in session.Photos)
                    {
                        System.Diagnostics.Debug.WriteLine($"    => {photo.FileName} (Type: {photo.PhotoType}, Path: {photo.FilePath})");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error loading photos on demand: {ex.Message}");
            }
        }

        /// <summary>
        /// Get preview data for the gallery preview box
        /// </summary>
        public async Task<GalleryPreviewData> GetPreviewDataAsync()
        {
            try
            {
                if (!Directory.Exists(_photoDirectory))
                {
                    return null;
                }

                // Get most recent event folder (avoiding Session_* folders which are deprecated)
                var eventFolders = Directory.GetDirectories(_photoDirectory)
                    .Where(d => !Path.GetFileName(d).StartsWith("Session_")) // Skip old session folders
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToList();

                if (eventFolders.Count == 0)
                {
                    return null;
                }

                // Look for first photo in the most recent event's originals folder
                string firstPhoto = null;
                foreach (var eventFolder in eventFolders)
                {
                    var originalsFolder = Path.Combine(eventFolder, "originals");
                    if (Directory.Exists(originalsFolder))
                    {
                        firstPhoto = Directory.GetFiles(originalsFolder, "*.jpg")
                            .Concat(Directory.GetFiles(originalsFolder, "*.png"))
                            .OrderBy(f => Path.GetFileName(f))
                            .FirstOrDefault();
                        
                        if (firstPhoto != null)
                            break; // Found a photo, stop looking
                    }
                }

                if (firstPhoto == null || !File.Exists(firstPhoto))
                {
                    return null;
                }

                // Count event folders instead of old Session_* folders
                var eventCount = eventFolders.Count;

                return new GalleryPreviewData
                {
                    PreviewImagePath = firstPhoto,
                    SessionCount = eventCount,
                    HasContent = true
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error getting preview data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clear the session cache
        /// </summary>
        public void ClearCache()
        {
            _cachedSessions = null;
            _lastRefresh = DateTime.MinValue;
        }

        #region Private Methods
        private List<GallerySessionInfo> LoadSessionsFromDatabase(int offset = 0, int limit = 20)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Loading sessions from database for eventId={_currentEventId} (offset: {offset}, limit: {limit})");
                
                // Don't load any sessions if no event is selected
                if (!_currentEventId.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine("GalleryBrowserService: No event selected, returning empty list");
                    return new List<GallerySessionInfo>();
                }
                
                var sessions = new List<GallerySessionInfo>();
                
                // Get only recent photo sessions from database filtered by event ID
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Calling GetPhotoSessions with eventId={_currentEventId.Value}");
                var dbSessions = _database.GetPhotoSessions(eventId: _currentEventId.Value, limit: limit, offset: offset);
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: GetPhotoSessions returned {dbSessions.Count} sessions");
                
                // Process sessions quickly without loading all photo details
                foreach (var dbSession in dbSessions)
                {
                    System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Processing session ID={dbSession.Id}, GUID={dbSession.SessionGuid}, IsVideo={dbSession.IsVideoSession}, EventId={dbSession.EventId}");
                    var session = LoadSessionFromDatabaseOptimized(dbSession);
                    if (session != null)
                    {
                        sessions.Add(session);
                        System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Added session to list - {session.SessionName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Session was null after LoadSessionFromDatabaseOptimized");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Loaded {sessions.Count} sessions from database");
                return sessions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error loading from database: {ex.Message}");
                return null;
            }
        }
        
        private GallerySessionInfo LoadSessionFromDatabase(Database.PhotoSessionData dbSession)
        {
            try
            {
                // Get photos and composed images for this session
                var photos = _database.GetSessionPhotos(dbSession.Id);
                var composedImages = _database.GetSessionComposedImages(dbSession.Id);
                
                var galleryPhotos = new List<GalleryPhotoInfo>();
                
                // Add original photos
                foreach (var photo in photos)
                {
                    if (File.Exists(photo.FilePath))
                    {
                        galleryPhotos.Add(new GalleryPhotoInfo
                        {
                            FilePath = photo.FilePath,
                            FileName = photo.FileName,
                            PhotoType = photo.PhotoType ?? "ORIG",
                            FileSize = photo.FileSize ?? 0
                        });
                    }
                }
                
                // Add composed images and GIFs - group by type to avoid duplicates
                // Only add ONE of each type (COMP, GIF, MP4) to prevent duplicates
                // INCLUDE 4x6_print versions for printing from gallery
                var composedByType = composedImages
                     
                    .GroupBy(c => {
                        if (c.OutputFormat == "GIF" || c.OutputFormat == "MP4" || 
                            c.FileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                            c.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        {
                            return c.OutputFormat ?? "GIF";
                        }
                        return "COMP";
                    }).Select(g => g.OrderByDescending(c => c.CreatedDate).First());
                
                foreach (var composed in composedByType)
                {
                    if (File.Exists(composed.FilePath))
                    {
                        string photoType = "COMP";
                        if (composed.OutputFormat == "GIF" || composed.FileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                        {
                            photoType = "GIF";
                        }
                        else if (composed.OutputFormat == "MP4" || composed.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        {
                            photoType = "MP4";
                        }
                        
                        galleryPhotos.Add(new GalleryPhotoInfo
                        {
                            FilePath = composed.FilePath,
                            FileName = composed.FileName,
                            PhotoType = photoType,
                            FileSize = composed.FileSize ?? 0
                        });
                    }
                }
                
                return new GallerySessionInfo
                {
                    SessionId = dbSession.SessionGuid,
                    SessionName = $"{dbSession.EventName} - {dbSession.StartTime:HH:mm:ss}",
                    EventName = dbSession.EventName,
                    SessionFolder = dbSession.SessionGuid, // Use GUID as folder reference
                    CreatedTime = dbSession.StartTime,
                    PhotoCount = galleryPhotos.Count,
                    Photos = galleryPhotos
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error loading session {dbSession.Id}: {ex.Message}");
                return null;
            }
        }
        
        private GallerySessionInfo LoadSessionFromDatabaseOptimized(Database.PhotoSessionData dbSession)
        {
            try
            {
                // Create lightweight session info with just first photo/video for thumbnail
                var sessionInfo = new GallerySessionInfo
                {
                    SessionId = dbSession.SessionGuid,
                    SessionName = $"{dbSession.EventName} - {dbSession.StartTime:HH:mm:ss}",
                    EventName = dbSession.EventName,
                    SessionFolder = dbSession.SessionGuid, // Use GUID as folder reference
                    CreatedTime = dbSession.StartTime,
                    PhotoCount = dbSession.IsVideoSession ? 1 : (dbSession.ActualPhotoCount + dbSession.ComposedImageCount), // Videos count as 1 item
                    Photos = new List<GalleryPhotoInfo>(),
                    IsVideoSession = dbSession.IsVideoSession,
                    VideoPath = dbSession.VideoPath,
                    VideoDurationSeconds = dbSession.VideoDurationSeconds
                };
                
                // For video sessions, use video thumbnail if available
                if (dbSession.IsVideoSession && !string.IsNullOrEmpty(dbSession.VideoThumbnailPath))
                {
                    if (File.Exists(dbSession.VideoThumbnailPath))
                    {
                        sessionInfo.Photos.Add(new GalleryPhotoInfo
                        {
                            FilePath = dbSession.VideoThumbnailPath,
                            ThumbnailPath = dbSession.VideoThumbnailPath,
                            FileName = Path.GetFileName(dbSession.VideoThumbnailPath),
                            PhotoType = "VIDEO_THUMBNAIL",
                            FileSize = 0,
                            IsVideoThumbnail = true
                        });
                    }
                    // If no thumbnail but video exists, try to generate one
                    else if (!string.IsNullOrEmpty(dbSession.VideoPath) && File.Exists(dbSession.VideoPath))
                    {
                        // Try to generate a thumbnail
                        System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Generating thumbnail for video: {dbSession.VideoPath}");
                        var thumbnailPath = GenerateVideoThumbnail(dbSession.VideoPath);
                        
                        if (!string.IsNullOrEmpty(thumbnailPath))
                        {
                            sessionInfo.Photos.Add(new GalleryPhotoInfo
                            {
                                FilePath = thumbnailPath,
                                ThumbnailPath = thumbnailPath,
                                FileName = Path.GetFileName(thumbnailPath),
                                PhotoType = "VIDEO_THUMBNAIL",
                                FileSize = 0,
                                IsVideoThumbnail = true
                            });
                            
                            // Update database with thumbnail path
                            _database.UpdateSessionWithVideoData(dbSession.Id, dbSession.VideoPath, thumbnailPath, 
                                dbSession.VideoFileSize, dbSession.VideoDurationSeconds);
                        }
                        else
                        {
                            // If generation failed, use a placeholder or first frame
                            sessionInfo.NeedsVideoThumbnail = true;
                            
                            // Create a placeholder thumbnail entry
                            sessionInfo.Photos.Add(new GalleryPhotoInfo
                            {
                                FilePath = dbSession.VideoPath, // Use video path as fallback
                                ThumbnailPath = null, // No thumbnail available
                                FileName = Path.GetFileName(dbSession.VideoPath),
                                PhotoType = "VIDEO",
                                FileSize = dbSession.VideoFileSize,
                                IsVideoThumbnail = false
                            });
                        }
                    }
                }
                
                // If not a video session or no video thumbnail, try regular photos
                if (sessionInfo.Photos.Count == 0)
                {
                    // First try to get a composed image (better for thumbnail)
                    var composedImage = _database.GetSessionComposedImages(dbSession.Id).FirstOrDefault();
                    if (composedImage != null && File.Exists(composedImage.FilePath))
                    {
                        sessionInfo.Photos.Add(new GalleryPhotoInfo
                        {
                            FilePath = composedImage.FilePath,
                            ThumbnailPath = composedImage.ThumbnailPath ?? composedImage.FilePath,
                            FileName = composedImage.FileName,
                            PhotoType = "COMPOSED",
                            FileSize = composedImage.FileSize ?? 0
                        });
                    }
                    else
                    {
                        // No composed image, get first regular photo
                        var firstPhoto = _database.GetSessionPhotos(dbSession.Id).FirstOrDefault();
                        if (firstPhoto != null && File.Exists(firstPhoto.FilePath))
                        {
                            sessionInfo.Photos.Add(new GalleryPhotoInfo
                            {
                                FilePath = firstPhoto.FilePath,
                                ThumbnailPath = firstPhoto.ThumbnailPath ?? firstPhoto.FilePath,
                                FileName = firstPhoto.FileName,
                                PhotoType = firstPhoto.PhotoType,
                                FileSize = firstPhoto.FileSize ?? 0
                            });
                        }
                    }
                }
                
                return sessionInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error loading session {dbSession.Id}: {ex.Message}");
                return null;
            }
        }
        
        private List<GallerySessionInfo> LoadSessionsFromDisk()
        {
            var sessions = new List<GallerySessionInfo>();

            if (!Directory.Exists(_photoDirectory))
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Photo directory not found: {_photoDirectory}");
                return sessions;
            }

            // Get all session folders
            var sessionFolders = Directory.GetDirectories(_photoDirectory, "Session_*")
                .OrderByDescending(d => Directory.GetCreationTime(d))
                .ToList();

            foreach (var folder in sessionFolders)
            {
                var session = LoadSessionFromFolder(folder);
                if (session != null)
                {
                    sessions.Add(session);
                }
            }

            System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Loaded {sessions.Count} sessions");
            return sessions;
        }

        private GallerySessionInfo LoadSessionFromFolder(string folderPath)
        {
            try
            {
                var folderName = Path.GetFileName(folderPath);
                var creationTime = Directory.GetCreationTime(folderPath);
                
                // Load session metadata if available
                var metadataPath = Path.Combine(folderPath, "session_info.json");
                string sessionName = folderName;
                string eventName = null;

                if (File.Exists(metadataPath))
                {
                    try
                    {
                        var json = File.ReadAllText(metadataPath);
                        dynamic metadata = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                        eventName = metadata.Event;
                        sessionName = $"{metadata.Event} - {((DateTime)metadata.CompletedAt).ToString("HH:mm:ss")}";
                    }
                    catch
                    {
                        // Use folder name if metadata parse fails
                    }
                }

                // Load photos
                var photos = new List<GalleryPhotoInfo>();
                
                // Get all image files
                var imageFiles = Directory.GetFiles(folderPath, "*.jpg")
                    .Concat(Directory.GetFiles(folderPath, "*.png"))
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                foreach (var imagePath in imageFiles)
                {
                    var fileName = Path.GetFileName(imagePath);
                    var photoType = "ORIG";
                    
                    if (fileName.Contains("composed"))
                        photoType = "COMP";
                    else if (fileName.Contains("animated"))
                        photoType = "GIF";

                    photos.Add(new GalleryPhotoInfo
                    {
                        FilePath = imagePath,
                        FileName = fileName,
                        PhotoType = photoType,
                        FileSize = new FileInfo(imagePath).Length
                    });
                }

                // Check for GIF
                var gifPath = Path.Combine(folderPath, "animated.gif");
                if (File.Exists(gifPath) && !photos.Any(p => p.FilePath == gifPath))
                {
                    photos.Add(new GalleryPhotoInfo
                    {
                        FilePath = gifPath,
                        FileName = "animated.gif",
                        PhotoType = "GIF",
                        FileSize = new FileInfo(gifPath).Length
                    });
                }

                return new GallerySessionInfo
                {
                    SessionId = Guid.NewGuid().ToString(),
                    SessionName = sessionName,
                    EventName = eventName,
                    SessionFolder = folderPath,
                    CreatedTime = creationTime,
                    PhotoCount = photos.Count,
                    Photos = photos
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error loading session from {folderPath}: {ex.Message}");
                return null;
            }
        }

        private GalleryBrowseData CreateBrowseData(List<GallerySessionInfo> sessions)
        {
            return new GalleryBrowseData
            {
                Sessions = sessions,
                TotalSessions = sessions.Count,
                TotalPhotos = sessions.Sum(s => s.PhotoCount)
            };
        }
        
        private string GenerateVideoThumbnail(string videoPath)
        {
            try
            {
                // Check if VideoCompressionService is available (it has FFmpeg)
                var compressionService = VideoCompressionService.Instance;
                if (compressionService == null || !compressionService.IsFFmpegAvailable())
                {
                    System.Diagnostics.Debug.WriteLine("GalleryBrowserService: FFmpeg not available for thumbnail generation");
                    return null;
                }
                
                // Generate thumbnail path
                string videoDir = Path.GetDirectoryName(videoPath);
                string videoName = Path.GetFileNameWithoutExtension(videoPath);
                string thumbnailPath = Path.Combine(videoDir, $"{videoName}_thumb.jpg");
                
                // Check if thumbnail already exists
                if (File.Exists(thumbnailPath))
                {
                    System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Thumbnail already exists: {thumbnailPath}");
                    return thumbnailPath;
                }
                
                // Generate thumbnail at 2 seconds into the video
                var task = compressionService.GenerateThumbnailAsync(videoPath, thumbnailPath, 2);
                task.Wait(TimeSpan.FromSeconds(5)); // Wait max 5 seconds
                
                if (task.IsCompleted && !string.IsNullOrEmpty(task.Result))
                {
                    System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Generated thumbnail: {thumbnailPath}");
                    return thumbnailPath;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("GalleryBrowserService: Thumbnail generation failed or timed out");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserService: Error generating thumbnail: {ex.Message}");
                return null;
            }
        }
        #endregion
    }

    #region Data Models
    public class GalleryBrowseData
    {
        public List<GallerySessionInfo> Sessions { get; set; }
        public int TotalSessions { get; set; }
        public int TotalPhotos { get; set; }
    }

    public class GallerySessionInfo
    {
        public string SessionId { get; set; }
        public string SessionName { get; set; }
        public string EventName { get; set; }
        public string SessionFolder { get; set; }
        public DateTime CreatedTime { get; set; }
        public int PhotoCount { get; set; }
        public List<GalleryPhotoInfo> Photos { get; set; }
        
        // Video session properties
        public bool IsVideoSession { get; set; }
        public string VideoPath { get; set; }
        public int VideoDurationSeconds { get; set; }
        public bool NeedsVideoThumbnail { get; set; }

        // Display properties
        public string SessionTimeDisplay => CreatedTime.ToString("yyyy-MM-dd HH:mm");
        public string PhotoCountDisplay => IsVideoSession ? 
            $"Video ({VideoDurationSeconds}s)" : 
            $"{PhotoCount} photo{(PhotoCount != 1 ? "s" : "")}";
        public string SessionTypeIcon => IsVideoSession ? "🎬" : "📷";
    }

    public class GalleryPhotoInfo
    {
        public string FilePath { get; set; }
        public string ThumbnailPath { get; set; }
        public string FileName { get; set; }
        public string PhotoType { get; set; } // ORIG, COMP, GIF, VIDEO_THUMBNAIL
        public long FileSize { get; set; }
        public bool IsVideoThumbnail { get; set; }
        public string FileSizeDisplay => $"{FileSize / 1024:N0} KB";
    }

    public class GalleryPreviewData
    {
        public string PreviewImagePath { get; set; }
        public int SessionCount { get; set; }
        public bool HasContent { get; set; }
    }
    #endregion

    #region Event Args
    public class GalleryBrowseLoadedEventArgs : EventArgs
    {
        public GalleryBrowseData BrowseData { get; set; }
        public int TotalSessions { get; set; }
        public int TotalPhotos { get; set; }
    }

    public class GalleryBrowseErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
        public string Operation { get; set; }
    }

    public class SessionViewRequestedEventArgs : EventArgs
    {
        public GallerySessionInfo Session { get; set; }
        public int SessionIndex { get; set; }
        public int TotalSessions { get; set; }
    }

    public class GalleryPreviewUpdateEventArgs : EventArgs
    {
        public GalleryPreviewData PreviewData { get; set; }
    }
    #endregion
}