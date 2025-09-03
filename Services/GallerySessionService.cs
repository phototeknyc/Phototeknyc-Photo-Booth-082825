using System;
using System.Collections.Generic;
using System.Linq;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to handle gallery session loading logic
    /// Moves complex business logic out of event handlers (clean architecture)
    /// </summary>
    public class GallerySessionService
    {
        private static GallerySessionService _instance;
        public static GallerySessionService Instance => _instance ?? (_instance = new GallerySessionService());

        private readonly FileValidationService _fileValidation;

        public GallerySessionService()
        {
            _fileValidation = FileValidationService.Instance;
        }

        /// <summary>
        /// Prepares a gallery session for loading into the UI
        /// Centralizes the complex logic from event handlers
        /// </summary>
        public SessionLoadResult PrepareSessionForLoading(SessionGalleryData session, List<PhotoGalleryData> photos)
        {
            var result = new SessionLoadResult();
            
            if (session == null || photos == null)
            {
                Log.Debug("Invalid session data for loading");
                result.StatusMessage = "Invalid session data";
                return result;
            }

            Log.Debug($"Preparing to load session: {session.SessionName} with {photos.Count} photos");

            foreach (var photo in photos)
            {
                Log.Debug($"  Processing: {photo.FileName} (Type: {photo.PhotoType}) - Path: {photo.FilePath}");
                
                // Validate file exists
                if (!_fileValidation.ValidatePhotoFile(photo))
                {
                    Log.Debug($"  File not found: {photo.FilePath}, skipping");
                    result.PhotosSkipped++;
                    continue;
                }
                
                // Skip 4x6_print thumbnails (but keep in session for printing)
                if (photo.PhotoType == "4x6_print")
                {
                    Log.Debug($"  Skipping 4x6_print thumbnail display (keeping for print): {photo.FilePath}");
                    result.PhotosSkipped++;
                    continue;
                }
                
                // Debug logging for photo types
                Log.Debug($"  Photo type '{photo.PhotoType}' will be processed");
                
                // Determine action based on photo type
                var action = DeterminePhotoAction(photo, photos);
                if (action != null)
                {
                    result.PhotoActions.Add(action);
                    result.PhotosLoaded++;
                    Log.Debug($"  Prepared action: {action.Action} for {photo.PhotoType}");
                }
            }
            
            result.StatusMessage = $"Viewing: {session.SessionName} ({result.PhotosLoaded} photos)";
            Log.Debug($"Session preparation complete: {result.PhotosLoaded} to load, {result.PhotosSkipped} skipped");
            
            return result;
        }

        /// <summary>
        /// Determines the appropriate UI action for a photo
        /// </summary>
        private PhotoLoadAction DeterminePhotoAction(PhotoGalleryData photo, List<PhotoGalleryData> allPhotos)
        {
            var action = new PhotoLoadAction 
            { 
                FilePath = photo.FilePath, 
                PhotoType = photo.PhotoType 
            };
            
            switch (photo.PhotoType)
            {
                case "GIF":
                case "MP4":
                    action.Action = "AddGif";
                    // For MP4, find thumbnail from original photos
                    if (photo.PhotoType == "MP4")
                    {
                        var firstOrigPhoto = _fileValidation.FindFirstValidPhotoByTypes(allPhotos, "ORIG", "Original");
                        action.ThumbnailPath = firstOrigPhoto?.FilePath;
                        Log.Debug($"  MP4 will use thumbnail: {action.ThumbnailPath}");
                    }
                    break;
                    
                case "COMP":
                case "2x6":
                    action.Action = "AddComposed";
                    break;
                    
                case "ORIG":
                case "Original":
                case "originals":  // Add support for 'originals' type from database
                default:
                    action.Action = "AddPhoto";
                    Log.Debug($"  Photo type '{photo.PhotoType}' mapped to AddPhoto action");
                    break;
            }
            
            return action;
        }

        /// <summary>
        /// Extracts photo index from filename for proper ordering
        /// </summary>
        public int ExtractPhotoIndex(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return 0;
            
            try
            {
                // Extract number from filenames like "photo_01.jpg", "photo_02.jpg", etc.
                var match = System.Text.RegularExpressions.Regex.Match(filename, @"(\d+)");
                if (match.Success)
                {
                    return int.Parse(match.Groups[1].Value);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Error extracting photo index from {filename}: {ex.Message}");
            }
            
            return 0;
        }

        /// <summary>
        /// Processes photo display completion
        /// </summary>
        public class PhotoProcessResult
        {
            public bool ShouldUpdateUI { get; set; }
            public string UpdateMessage { get; set; }
            public int PhotoIndex { get; set; }
            public bool IsLastPhoto { get; set; }
        }

        /// <summary>
        /// Analyzes a photo processed event and determines UI actions
        /// </summary>
        public PhotoProcessResult AnalyzePhotoProcessed(string photoPath, int currentPhotoIndex, int totalPhotos)
        {
            var result = new PhotoProcessResult
            {
                PhotoIndex = currentPhotoIndex,
                IsLastPhoto = currentPhotoIndex >= totalPhotos - 1
            };

            if (_fileValidation.ValidateFilePath(photoPath))
            {
                result.ShouldUpdateUI = true;
                result.UpdateMessage = result.IsLastPhoto ? 
                    "All photos captured! Processing..." : 
                    $"Photo {currentPhotoIndex + 1} of {totalPhotos} captured";
            }
            else
            {
                Log.Error($"Photo path invalid: {photoPath}");
                result.UpdateMessage = "Error processing photo";
            }

            return result;
        }
    }

    /// <summary>
    /// Result of preparing a session for loading
    /// </summary>
    public class SessionLoadResult
    {
        public int PhotosLoaded { get; set; }
        public int PhotosSkipped { get; set; }
        public string StatusMessage { get; set; }
        public List<PhotoLoadAction> PhotoActions { get; set; } = new List<PhotoLoadAction>();
    }

    /// <summary>
    /// Represents an action to load a photo into the UI
    /// </summary>
    public class PhotoLoadAction
    {
        public string FilePath { get; set; }
        public string PhotoType { get; set; }
        public string Action { get; set; } // "AddGif", "AddComposed", "AddPhoto"
        public string ThumbnailPath { get; set; } // For MP4 thumbnails
    }
}