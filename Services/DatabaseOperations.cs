using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Photobooth.Database;
using Photobooth.Models;
using CameraControl.Devices.Classes;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles all database operations for photo sessions
    /// </summary>
    public class DatabaseOperations
    {
        private readonly TemplateDatabase database;
        private int? currentDatabaseSessionId = null;
        private string currentSessionGuid = null;
        private List<int> currentSessionPhotoIds = new List<int>();
        
        public int? CurrentSessionId => currentDatabaseSessionId;
        public string CurrentSessionGuid => currentSessionGuid;
        public List<int> CurrentPhotoIds => currentSessionPhotoIds;
        
        public DatabaseOperations()
        {
            database = new TemplateDatabase();
            // Run database cleanup for sessions older than 24 hours
            database.RunPeriodicCleanup();
        }
        
        /// <summary>
        /// Create a new database session
        /// </summary>
        public void CreateSession(int? eventId, int? templateId)
        {
            try
            {
                Log.Debug($"★★★ DatabaseOperations.CreateSession called: eventId={eventId}, templateId={templateId}");
                
                // Generate new session GUID
                currentSessionGuid = Guid.NewGuid().ToString();
                
                // Create database session with the same GUID
                string sessionName = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";
                currentDatabaseSessionId = database.CreatePhotoSession(
                    eventId ?? 0,
                    templateId ?? 0,
                    sessionName,
                    currentSessionGuid  // Pass the GUID to ensure consistency
                );
                
                Log.Debug($"★★★ DatabaseOperations.CreateSession completed: sessionId={currentDatabaseSessionId}, guid={currentSessionGuid}");
                
                // Clear photo ID list for new session
                currentSessionPhotoIds.Clear();
                
                Log.Debug($"DatabaseOperations: Created session {currentDatabaseSessionId} with GUID {currentSessionGuid}");
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to create database session: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save a photo to the database
        /// </summary>
        public void SavePhoto(string filePath, int sequenceNumber, string photoType = "Original")
        {
            try
            {
                Log.Debug($"★★★ DatabaseOperations.SavePhoto called: filePath={filePath}, sessionId={currentDatabaseSessionId}, fileExists={File.Exists(filePath)}");
                if (currentDatabaseSessionId.HasValue && File.Exists(filePath))
                {
                    string fileName = Path.GetFileName(filePath);

                    // Generate thumbnail - now actually creates the file
                    string thumbnailPath = GenerateThumbnailForRegularPhoto(filePath);

                    // Save to database
                    var photoData = new PhotoData
                    {
                        SessionId = currentDatabaseSessionId.Value,
                        FileName = fileName,
                        FilePath = filePath,
                        ThumbnailPath = thumbnailPath ?? filePath, // Fallback to original if thumbnail creation fails
                        SequenceNumber = sequenceNumber,
                        PhotoType = photoType,
                        CreatedDate = DateTime.Now
                    };

                    Log.Debug($"★★★ Calling database.SavePhoto with SessionId={currentDatabaseSessionId.Value}, PhotoType={photoType}");
                    int photoId = database.SavePhoto(photoData);

                    // Track photo ID for this session
                    currentSessionPhotoIds.Add(photoId);
                    
                    Log.Debug($"★★★ DatabaseOperations: Successfully saved photo {photoId} to session {currentDatabaseSessionId}");
                }
                else
                {
                    Log.Error($"★★★ DatabaseOperations.SavePhoto SKIPPED: SessionId={currentDatabaseSessionId.HasValue}, FileExists={File.Exists(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to save photo to database: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save composed image to database
        /// </summary>
        public void SaveComposedImage(string filePath, string outputFormat = "4x6")
        {
            try
            {
                if (currentDatabaseSessionId.HasValue && File.Exists(filePath))
                {
                    string fileName = Path.GetFileName(filePath);
                    
                    // Generate thumbnail for composed image
                    string thumbnailPath = GenerateThumbnailForComposedImage(filePath);
                    
                    // Save to database as composed image
                    var composedData = new ComposedImageData
                    {
                        SessionId = currentDatabaseSessionId.Value,
                        FileName = fileName,
                        FilePath = filePath,
                        ThumbnailPath = thumbnailPath,
                        OutputFormat = outputFormat,
                        CreatedDate = DateTime.Now,
                        TemplateId = 0 // TODO: Pass actual template ID if available
                    };
                    
                    int composedId = database.SaveComposedImage(composedData);
                    
                    Log.Debug($"DatabaseOperations: Saved composed image {composedId} to session {currentDatabaseSessionId}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to save composed image: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save animation to database (as composed image)
        /// </summary>
        public void SaveAnimation(string animationPath, string animationType)
        {
            try
            {
                if (currentDatabaseSessionId.HasValue && File.Exists(animationPath))
                {
                    // Save animation as a composed image
                    SaveComposedImage(animationPath, animationType);
                    Log.Debug($"DatabaseOperations: Saved {animationType} animation to session {currentDatabaseSessionId}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to save animation: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update gallery URL for current session
        /// </summary>
        public void UpdateGalleryUrl(string galleryUrl)
        {
            try
            {
                if (!string.IsNullOrEmpty(currentSessionGuid) && !string.IsNullOrEmpty(galleryUrl))
                {
                    database.UpdatePhotoSessionGalleryUrl(currentSessionGuid, galleryUrl);
                    Log.Debug($"DatabaseOperations: Updated gallery URL for session {currentSessionGuid}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to update gallery URL: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save video session data to the database
        /// </summary>
        public void SaveVideoSessionData(string videoPath, string thumbnailPath, long fileSize, int durationSeconds)
        {
            try
            {
                if (currentDatabaseSessionId.HasValue)
                {
                    // Update the session with video information
                    database.UpdateSessionWithVideoData(currentDatabaseSessionId.Value, videoPath, thumbnailPath, fileSize, durationSeconds);
                    Log.Debug($"DatabaseOperations: Updated session {currentDatabaseSessionId} with video data");
                }
                else
                {
                    Log.Error("DatabaseOperations: No active session to save video data");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Error saving video session data: {ex.Message}");
            }
        }
        
        /// <summary>
        /// End the current database session
        /// </summary>
        public void EndSession()
        {
            try
            {
                if (currentDatabaseSessionId.HasValue)
                {
                    // Update session end time
                    database.EndPhotoSession(currentDatabaseSessionId.Value);
                    
                    Log.Debug($"DatabaseOperations: Ended session {currentDatabaseSessionId} with {currentSessionPhotoIds.Count} photos");
                    
                    // Clear session data
                    currentDatabaseSessionId = null;
                    currentSessionGuid = null;
                    currentSessionPhotoIds.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to end session: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get gallery URL for a session
        /// </summary>
        public string GetGalleryUrl(string sessionGuid)
        {
            try
            {
                return database.GetPhotoSessionGalleryUrl(sessionGuid);
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to get gallery URL: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Log SMS send to database
        /// </summary>
        public void LogSMS(string sessionGuid, string phoneNumber, string galleryUrl, bool sentImmediately, string queueStatus = null)
        {
            try
            {
                database.LogSMSSend(sessionGuid, phoneNumber, galleryUrl, sentImmediately, queueStatus);
                Log.Debug($"DatabaseOperations: Logged SMS for session {sessionGuid}");
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to log SMS: {ex.Message}");
            }
        }
        
        private string GenerateThumbnailPath(string originalPath)
        {
            string directory = Path.GetDirectoryName(originalPath);
            string fileName = Path.GetFileNameWithoutExtension(originalPath);
            string extension = Path.GetExtension(originalPath);

            // Create thumbs subfolder within the same directory type
            // e.g., originals/thumbs/, composed/thumbs/, print/thumbs/
            string thumbsFolder = Path.Combine(directory, "thumbs");
            Directory.CreateDirectory(thumbsFolder);

            return Path.Combine(thumbsFolder, $"{fileName}_thumb{extension}");
        }

        private string GenerateThumbnailForRegularPhoto(string photoPath)
        {
            try
            {
                // Skip if photo doesn't exist
                if (!File.Exists(photoPath))
                {
                    Log.Error($"DatabaseOperations: Photo file not found for thumbnail generation: {photoPath}");
                    return null;
                }

                // Generate thumbnail path
                string thumbnailPath = GenerateThumbnailPath(photoPath);

                // Check if thumbnail already exists
                if (File.Exists(thumbnailPath))
                {
                    Log.Debug($"DatabaseOperations: Thumbnail already exists: {thumbnailPath}");
                    return thumbnailPath;
                }

                // Load the original image
                var originalImage = new BitmapImage();
                originalImage.BeginInit();
                originalImage.CacheOption = BitmapCacheOption.OnLoad;
                originalImage.UriSource = new Uri(photoPath);
                originalImage.EndInit();

                // Create thumbnail (300x300 max)
                var thumbnail = CreateThumbnail(originalImage, 300, 300);

                // Save thumbnail
                SaveThumbnail(thumbnail, thumbnailPath);

                Log.Debug($"DatabaseOperations: Generated thumbnail: {thumbnailPath}");
                return thumbnailPath;
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to generate thumbnail for {photoPath}: {ex.Message}");
                return null;
            }
        }

        private string GenerateThumbnailForComposedImage(string composedImagePath)
        {
            try
            {
                // Load the composed image
                var originalImage = new BitmapImage();
                originalImage.BeginInit();
                originalImage.CacheOption = BitmapCacheOption.OnLoad;
                originalImage.UriSource = new Uri(composedImagePath);
                originalImage.EndInit();
                
                // Create thumbnail
                var thumbnail = CreateThumbnail(originalImage, 300, 300);
                
                // Save thumbnail
                string thumbnailPath = GenerateThumbnailPath(composedImagePath);
                SaveThumbnail(thumbnail, thumbnailPath);
                
                return thumbnailPath;
            }
            catch (Exception ex)
            {
                Log.Error($"DatabaseOperations: Failed to generate thumbnail: {ex.Message}");
                return null;
            }
        }
        
        private BitmapImage CreateThumbnail(BitmapImage source, int maxWidth, int maxHeight)
        {
            double scale = Math.Min(maxWidth / source.Width, maxHeight / source.Height);
            
            var thumbnail = new BitmapImage();
            thumbnail.BeginInit();
            thumbnail.CacheOption = BitmapCacheOption.OnLoad;
            thumbnail.UriSource = source.UriSource;
            thumbnail.DecodePixelWidth = (int)(source.Width * scale);
            thumbnail.DecodePixelHeight = (int)(source.Height * scale);
            thumbnail.EndInit();
            thumbnail.Freeze();
            
            return thumbnail;
        }
        
        private void SaveThumbnail(BitmapImage thumbnail, string path)
        {
            using (var fileStream = new FileStream(path, FileMode.Create))
            {
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 85;
                encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                encoder.Save(fileStream);
            }
        }
    }
}