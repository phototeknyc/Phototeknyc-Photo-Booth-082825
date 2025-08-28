using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Photobooth.Models;
using Photobooth.Database;

namespace Photobooth.Services
{
    /// <summary>
    /// Clean service that manages event gallery functionality
    /// Handles session saving, gallery display, and photo management
    /// </summary>
    public class EventGalleryService
    {
        #region Events
        public event EventHandler<GalleryLoadedEventArgs> GalleryLoaded;
        public event EventHandler<GalleryErrorEventArgs> GalleryError;
        public event EventHandler<SessionSavedEventArgs> SessionSaved;
        public event EventHandler<GalleryRequestEventArgs> GalleryDisplayRequested;
        public event EventHandler<SessionLoadRequestEventArgs> SessionLoadRequested;
        #endregion

        #region Dependencies
        private readonly SessionManager _sessionManager;
        private readonly EventService _eventService;
        private readonly TemplateDatabase _database;
        #endregion

        #region Properties
        public bool IsGalleryVisible { get; private set; }
        public string PhotoDirectory { get; private set; }
        #endregion

        public EventGalleryService()
        {
            _sessionManager = new SessionManager();
            _eventService = new EventService();
            _database = new TemplateDatabase();
            
            // Set photo directory from settings
            PhotoDirectory = Properties.Settings.Default.PhotoLocation;
            if (string.IsNullOrEmpty(PhotoDirectory))
            {
                PhotoDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                    "Photobooth"
                );
            }

            // Subscribe to session manager events for automatic saving
            _sessionManager.SessionCompleted += OnSessionCompleted;
        }

        /// <summary>
        /// Show the event gallery in the main view (not overlay)
        /// </summary>
        public async Task ShowGalleryAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("EventGalleryService: Showing gallery in main view");
                IsGalleryVisible = true;
                
                // Load gallery sessions
                var sessions = await GetRecentSessionsAsync();
                
                if (sessions != null && sessions.Any())
                {
                    // Load the most recent session into the photo strip
                    await LoadSessionIntoViewAsync(sessions.First());
                    
                    // Notify that gallery is loaded with all sessions
                    GalleryLoaded?.Invoke(this, new GalleryLoadedEventArgs
                    {
                        GalleryData = new GalleryData { Sessions = sessions },
                        TotalSessions = sessions.Count,
                        TotalPhotos = sessions.Sum(s => s.PhotoCount)
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("EventGalleryService: No sessions found in gallery");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error showing gallery: {ex.Message}");
                GalleryError?.Invoke(this, new GalleryErrorEventArgs { Error = ex, Operation = "ShowGallery" });
            }
        }
        
        /// <summary>
        /// Load a specific session into the main view photo strip
        /// </summary>
        public async Task LoadSessionIntoViewAsync(SessionGalleryData session)
        {
            try
            {
                if (session == null || session.Photos == null)
                {
                    System.Diagnostics.Debug.WriteLine("EventGalleryService: Invalid session data");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Loading session {session.SessionName} into view");
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Session has {session.Photos?.Count ?? 0} photos:");
                if (session.Photos != null)
                {
                    foreach (var photo in session.Photos)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {photo.FileName} ({photo.PhotoType}) - Exists: {File.Exists(photo.FilePath)}");
                    }
                }
                
                // Request UI to load this session's photos
                SessionLoadRequested?.Invoke(this, new SessionLoadRequestEventArgs
                {
                    Session = session,
                    Photos = session.Photos,
                    SessionIndex = 0, // Will be updated based on current navigation
                    TotalSessions = 1 // Will be updated based on loaded sessions
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error loading session: {ex.Message}");
                GalleryError?.Invoke(this, new GalleryErrorEventArgs { Error = ex, Operation = "LoadSession" });
            }
        }
        
        /// <summary>
        /// Get recent sessions from the gallery
        /// </summary>
        private async Task<List<SessionGalleryData>> GetRecentSessionsAsync()
        {
            return await Task.Run(() =>
            {
                var sessions = new List<SessionGalleryData>();
                
                try
                {
                    System.Diagnostics.Debug.WriteLine("EventGalleryService: Loading sessions from database");
                    
                    // Get sessions from database
                    var dbSessions = _database.GetPhotoSessions();
                    
                    // Sort by date and take recent ones
                    var recentSessions = dbSessions
                        .OrderByDescending(s => s.StartTime)
                        .Take(20)
                        .ToList();
                    
                    foreach (var dbSession in recentSessions)
                    {
                        var sessionData = LoadSessionFromDatabase(dbSession);
                        if (sessionData != null)
                        {
                            sessions.Add(sessionData);
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService: Loaded {sessions.Count} sessions from database");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error loading from database: {ex.Message}");
                    // Fallback to file system loading
                    sessions = LoadSessionsFromFileSystem();
                }
                
                return sessions;
            });
        }

        /// <summary>
        /// Hide the event gallery
        /// </summary>
        public void HideGallery()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("EventGalleryService: Hiding gallery");
                IsGalleryVisible = false;
                
                GalleryDisplayRequested?.Invoke(this, new GalleryRequestEventArgs
                {
                    Action = "Hide"
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error hiding gallery: {ex.Message}");
                GalleryError?.Invoke(this, new GalleryErrorEventArgs { Error = ex, Operation = "HideGallery" });
            }
        }

        /// <summary>
        /// Save current session to event gallery
        /// </summary>
        public async Task<bool> SaveSessionToGalleryAsync(CompletedSessionData sessionData)
        {
            try
            {
                if (sessionData?.PhotoPaths == null || !sessionData.PhotoPaths.Any())
                {
                    System.Diagnostics.Debug.WriteLine("EventGalleryService.SaveSessionToGalleryAsync: No photos to save");
                    System.Diagnostics.Debug.WriteLine("EventGalleryService: No photos to save");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"EventGalleryService.SaveSessionToGalleryAsync: Saving session {sessionData.SessionId} to gallery");
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Saving session {sessionData.SessionId} to gallery");

                // IMPORTANT: Ensure the session is saved to the database with all photo records
                var database = new Database.TemplateDatabase();
                
                // Check if session exists in database
                var existingSession = database.GetPhotoSessionByGuid(sessionData.SessionId);
                if (existingSession == null)
                {
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService.SaveSessionToGalleryAsync: Session {sessionData.SessionId} not found in database, creating it");
                    
                    // Create the session in database
                    database.SavePhotoSession(
                        eventId: sessionData.Event?.Id ?? 0,
                        templateId: sessionData.Template?.Id ?? 0,
                        sessionName: $"{sessionData.Event?.Name ?? "Event"} - {DateTime.Now:HH:mm:ss}",
                        sessionGuid: sessionData.SessionId,
                        startTime: sessionData.CompletedAt
                    );
                }
                
                // Save each photo to database if not already saved
                foreach (var photoPath in sessionData.PhotoPaths)
                {
                    if (File.Exists(photoPath))
                    {
                        var fileName = Path.GetFileName(photoPath);
                        var fileInfo = new FileInfo(photoPath);
                        
                        System.Diagnostics.Debug.WriteLine($"EventGalleryService.SaveSessionToGalleryAsync: Saving photo {fileName} to database");
                        
                        // Save photo to database
                        database.SaveSessionPhoto(
                            sessionGuid: sessionData.SessionId,
                            fileName: fileName,
                            filePath: photoPath,
                            fileSize: fileInfo.Length,
                            photoType: "ORIG"
                        );
                    }
                }
                
                // Save GIF if exists
                if (!string.IsNullOrEmpty(sessionData.GifPath) && File.Exists(sessionData.GifPath))
                {
                    var fileName = Path.GetFileName(sessionData.GifPath);
                    var fileInfo = new FileInfo(sessionData.GifPath);
                    
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService.SaveSessionToGalleryAsync: Saving GIF {fileName} to database");
                    
                    database.SaveSessionPhoto(
                        sessionGuid: sessionData.SessionId,
                        fileName: fileName,
                        filePath: sessionData.GifPath,
                        fileSize: fileInfo.Length,
                        photoType: "GIF"
                    );
                }
                
                // Save composed image if exists
                if (!string.IsNullOrEmpty(sessionData.ComposedImagePath) && File.Exists(sessionData.ComposedImagePath))
                {
                    var fileName = Path.GetFileName(sessionData.ComposedImagePath);
                    var fileInfo = new FileInfo(sessionData.ComposedImagePath);
                    
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService.SaveSessionToGalleryAsync: Saving composed image {fileName} to database");
                    
                    database.SaveSessionPhoto(
                        sessionGuid: sessionData.SessionId,
                        fileName: fileName,
                        filePath: sessionData.ComposedImagePath,
                        fileSize: fileInfo.Length,
                        photoType: "COMP"
                    );
                }

                // Use event-based directory structure
                string eventName = GetSafeEventName(sessionData.Event);
                var eventFolder = Path.Combine(PhotoDirectory, eventName);

                SessionSaved?.Invoke(this, new SessionSavedEventArgs
                {
                    SessionId = sessionData.SessionId,
                    SessionFolder = eventFolder,
                    PhotoCount = sessionData.PhotoPaths.Count
                });

                System.Diagnostics.Debug.WriteLine($"EventGalleryService.SaveSessionToGalleryAsync: Session saved to database and event folder {eventFolder}");
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Session saved to event folder {eventFolder}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error saving session: {ex.Message}");
                GalleryError?.Invoke(this, new GalleryErrorEventArgs { Error = ex, Operation = "SaveSession" });
                return false;
            }
        }

        /// <summary>
        /// Load gallery data from photo directory
        /// </summary>
        private async Task LoadGalleryDataAsync()
        {
            try
            {
                if (!Directory.Exists(PhotoDirectory))
                {
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService: Photo directory not found: {PhotoDirectory}");
                    return;
                }

                await Task.Run(() =>
                {
                    var galleryData = new GalleryData
                    {
                        Sessions = new List<SessionGalleryData>()
                    };

                    // Get all session folders
                    var sessionFolders = Directory.GetDirectories(PhotoDirectory, "Session_*")
                        .OrderByDescending(d => Directory.GetCreationTime(d))
                        .Take(20) // Limit to recent sessions
                        .ToList();

                    foreach (var folder in sessionFolders)
                    {
                        var sessionData = LoadSessionData(folder);
                        if (sessionData != null)
                        {
                            galleryData.Sessions.Add(sessionData);
                        }
                    }

                    // Notify on UI thread
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        GalleryLoaded?.Invoke(this, new GalleryLoadedEventArgs
                        {
                            GalleryData = galleryData,
                            TotalSessions = galleryData.Sessions.Count,
                            TotalPhotos = galleryData.Sessions.Sum(s => s.PhotoCount)
                        });
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error loading gallery: {ex.Message}");
                GalleryError?.Invoke(this, new GalleryErrorEventArgs { Error = ex, Operation = "LoadGallery" });
            }
        }

        private SessionGalleryData LoadSessionFromDatabase(PhotoSessionData dbSession)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Loading session {dbSession.Id} from database");
                
                // Get photos for this session
                var photos = _database.GetSessionPhotos(dbSession.Id);
                var composedImages = _database.GetSessionComposedImages(dbSession.Id);
                
                var sessionData = new SessionGalleryData
                {
                    SessionName = $"{dbSession.EventName} - {dbSession.StartTime:HH:mm:ss}",
                    SessionTime = dbSession.StartTime.ToString("yyyy-MM-dd HH:mm"),
                    SessionFolder = dbSession.SessionGuid, // Store GUID for reference
                    Photos = new List<PhotoGalleryData>()
                };
                
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Created SessionGalleryData with SessionFolder (GUID) = {sessionData.SessionFolder}");
                
                // Add original photos
                foreach (var photo in photos)
                {
                    if (File.Exists(photo.FilePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"EventGalleryService: Adding photo: {photo.FileName} (Type: {photo.PhotoType})");
                        sessionData.Photos.Add(new PhotoGalleryData
                        {
                            FilePath = photo.FilePath,
                            FileName = photo.FileName,
                            FileSize = photo.FileSize?.ToString("N0") + " bytes" ?? "Unknown",
                            ThumbnailPath = photo.ThumbnailPath ?? photo.FilePath,
                            PhotoType = photo.PhotoType ?? "ORIG"
                        });
                    }
                }
                
                // Add composed images and GIFs (excluding 4x6_print which is only for backend)
                foreach (var composed in composedImages.Where(c => c.OutputFormat != "4x6_print"))
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
                        
                        System.Diagnostics.Debug.WriteLine($"EventGalleryService: Adding composed: {composed.FileName} (Type: {photoType})");
                        sessionData.Photos.Add(new PhotoGalleryData
                        {
                            FilePath = composed.FilePath,
                            FileName = composed.FileName,
                            FileSize = composed.FileSize?.ToString("N0") + " bytes" ?? "Unknown",
                            ThumbnailPath = composed.ThumbnailPath ?? composed.FilePath,
                            PhotoType = photoType
                        });
                    }
                }
                
                sessionData.PhotoCount = sessionData.Photos.Count;
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Total photos loaded for session: {sessionData.PhotoCount}");
                
                return sessionData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error loading session {dbSession.Id}: {ex.Message}");
                return null;
            }
        }
        
        private List<SessionGalleryData> LoadSessionsFromFileSystem()
        {
            var sessions = new List<SessionGalleryData>();
            
            if (!Directory.Exists(PhotoDirectory))
            {
                return sessions;
            }
            
            // Load from event-based folder structure instead of Session_* folders
            var eventFolders = Directory.GetDirectories(PhotoDirectory)
                .Where(d => !Path.GetFileName(d).StartsWith("Session_")) // Skip old session folders
                .OrderByDescending(d => Directory.GetCreationTime(d))
                .Take(20)
                .ToList();
            
            foreach (var folder in eventFolders)
            {
                var sessionData = LoadSessionData(folder);
                if (sessionData != null)
                {
                    sessions.Add(sessionData);
                }
            }
            
            return sessions;
        }
        
        private SessionGalleryData LoadSessionData(string sessionFolder)
        {
            try
            {
                var folderName = Path.GetFileName(sessionFolder);
                
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Loading session from folder: {sessionFolder}");

                SessionGalleryData sessionData = null;

                // Note: session_info.json loading removed - everything is loaded from database
                // Create session data from folder structure
                sessionData = new SessionGalleryData
                {
                    SessionName = folderName,
                    SessionTime = Directory.GetCreationTime(sessionFolder).ToString("yyyy-MM-dd HH:mm"),
                    SessionFolder = sessionFolder,
                    Photos = new List<PhotoGalleryData>()
                };

                // Load photos from event-based folder structure
                var allPhotos = new List<PhotoGalleryData>();

                // Load original photos from "originals" subfolder
                var originalsFolder = Path.Combine(sessionFolder, "originals");
                if (Directory.Exists(originalsFolder))
                {
                    var originalFiles = Directory.GetFiles(originalsFolder, "*.jpg")
                        .Concat(Directory.GetFiles(originalsFolder, "*.png"))
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();
                    
                    foreach (var imagePath in originalFiles)
                    {
                        allPhotos.Add(new PhotoGalleryData
                        {
                            FilePath = imagePath,
                            FileName = Path.GetFileName(imagePath),
                            FileSize = new FileInfo(imagePath).Length.ToString("N0") + " bytes",
                            ThumbnailPath = imagePath,
                            PhotoType = "ORIG"
                        });
                    }
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService: Found {originalFiles.Count} original photos in {originalsFolder}");
                }

                // Load composed images from "composed" subfolder
                var composedFolder = Path.Combine(sessionFolder, "composed");
                if (Directory.Exists(composedFolder))
                {
                    var composedFiles = Directory.GetFiles(composedFolder, "*.jpg")
                        .Concat(Directory.GetFiles(composedFolder, "*.png"))
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();
                    
                    foreach (var imagePath in composedFiles)
                    {
                        allPhotos.Add(new PhotoGalleryData
                        {
                            FilePath = imagePath,
                            FileName = Path.GetFileName(imagePath),
                            FileSize = new FileInfo(imagePath).Length.ToString("N0") + " bytes",
                            ThumbnailPath = imagePath,
                            PhotoType = "COMP"
                        });
                    }
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService: Found {composedFiles.Count} composed images in {composedFolder}");
                }

                // Load GIFs from "animation" subfolder
                var animationFolder = Path.Combine(sessionFolder, "animation");
                if (Directory.Exists(animationFolder))
                {
                    var gifFiles = Directory.GetFiles(animationFolder, "*.gif")
                        .OrderBy(f => Path.GetFileName(f))
                        .ToList();
                    
                    foreach (var gifPath in gifFiles)
                    {
                        allPhotos.Add(new PhotoGalleryData
                        {
                            FilePath = gifPath,
                            FileName = Path.GetFileName(gifPath),
                            FileSize = new FileInfo(gifPath).Length.ToString("N0") + " bytes",
                            ThumbnailPath = gifPath,
                            PhotoType = "GIF"
                        });
                    }
                    System.Diagnostics.Debug.WriteLine($"EventGalleryService: Found {gifFiles.Count} GIF files in {animationFolder}");
                }

                sessionData.Photos.AddRange(allPhotos);

                sessionData.PhotoCount = sessionData.Photos.Count;
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Total photos loaded for session: {sessionData.PhotoCount}");
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Photo types: {string.Join(", ", sessionData.Photos.Select(p => $"{p.FileName}({p.PhotoType})"))}");
                
                return sessionData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EventGalleryService: Error loading session data from {sessionFolder}: {ex.Message}");
                return null;
            }
        }

        private void OnSessionCompleted(object sender, PhotoSession session)
        {
            // Auto-save completed sessions to gallery
            System.Diagnostics.Debug.WriteLine($"EventGalleryService: Auto-saving completed session {session.SessionId}");
            
            // Note: PhotoSession doesn't have ComposedImagePath and GifPath
            // Those are handled by PhotoboothSessionService, not SessionManager
            // This auto-save is likely redundant as the page already calls SaveSessionToGalleryAsync
            // with the complete data including composed image and GIF paths
            
            // Convert PhotoSession to CompletedSessionData for compatibility
            var sessionData = new CompletedSessionData
            {
                SessionId = session.SessionId,
                Event = new EventData { Name = session.EventName },
                PhotoPaths = session.Photos.Select(p => p.FilePath).ToList(),
                CompletedAt = session.EndTime ?? DateTime.Now,
                // ComposedImagePath and GifPath are not available here
                ComposedImagePath = null,
                GifPath = null
            };

            // Skip auto-save if paths are incomplete
            System.Diagnostics.Debug.WriteLine($"EventGalleryService: Skipping incomplete auto-save (no composed/GIF paths available)");
            // _ = Task.Run(async () => await SaveSessionToGalleryAsync(sessionData));
        }
        
        /// <summary>
        /// Get safe folder name from event data
        /// </summary>
        private string GetSafeEventName(EventData eventData)
        {
            if (eventData?.Name != null && !string.IsNullOrWhiteSpace(eventData.Name))
            {
                // Clean event name for use as folder name
                string safeName = eventData.Name.Trim();
                
                // Remove invalid filename characters
                char[] invalidChars = Path.GetInvalidFileNameChars();
                foreach (char c in invalidChars)
                {
                    safeName = safeName.Replace(c, '_');
                }
                
                // Replace spaces with underscores and limit length
                safeName = safeName.Replace(' ', '_');
                if (safeName.Length > 50)
                {
                    safeName = safeName.Substring(0, 50);
                }
                
                return safeName;
            }
            
            // Fallback to date-based folder name
            return $"Event_{DateTime.Now:yyyy_MM_dd}";
        }
    }

    #region Data Models
    public class GalleryData
    {
        public List<SessionGalleryData> Sessions { get; set; }
    }

    public class SessionGalleryData
    {
        public string SessionName { get; set; }
        public string SessionTime { get; set; }
        public int PhotoCount { get; set; }
        public string SessionFolder { get; set; }
        public List<PhotoGalleryData> Photos { get; set; }
    }

    public class PhotoGalleryData
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string FileSize { get; set; }
        public string ThumbnailPath { get; set; }
        public string PhotoType { get; set; }
        public string TypeBadgeColor
        {
            get
            {
                switch (PhotoType)
                {
                    case "GIF": return "#FF9800";
                    case "COMP": return "#4CAF50";
                    default: return "#2196F3";
                }
            }
        }
    }
    #endregion

    #region Event Args Classes
    public class GalleryLoadedEventArgs : EventArgs
    {
        public GalleryData GalleryData { get; set; }
        public int TotalSessions { get; set; }
        public int TotalPhotos { get; set; }
    }

    public class GalleryErrorEventArgs : EventArgs
    {
        public Exception Error { get; set; }
        public string Operation { get; set; }
    }

    public class SessionSavedEventArgs : EventArgs
    {
        public string SessionId { get; set; }
        public string SessionFolder { get; set; }
        public int PhotoCount { get; set; }
    }

    public class GalleryRequestEventArgs : EventArgs
    {
        public string Action { get; set; } // "Show" or "Hide"
        public string PhotoDirectory { get; set; }
    }
    
    public class SessionLoadRequestEventArgs : EventArgs
    {
        public SessionGalleryData Session { get; set; }
        public List<PhotoGalleryData> Photos { get; set; }
        public int SessionIndex { get; set; }
        public int TotalSessions { get; set; }
    }
    #endregion
}