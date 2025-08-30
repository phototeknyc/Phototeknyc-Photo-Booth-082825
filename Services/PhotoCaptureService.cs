using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using Photobooth.Database;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles photo capture operations and file management
    /// </summary>
    public class PhotoCaptureService
    {
        private readonly string photoFolder;
        private readonly DatabaseOperations databaseOperations;
        
        public List<string> CapturedPhotoPaths { get; private set; }
        public int PhotoCount { get; private set; }
        public int CurrentPhotoIndex { get; private set; }
        public bool IsRetakingPhoto { get; set; }
        public int PhotoIndexToRetake { get; set; } = -1;
        
        public PhotoCaptureService(DatabaseOperations dbOps)
        {
            databaseOperations = dbOps;
            CapturedPhotoPaths = new List<string>();
            
            // Set up photo folder
            photoFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                "Photobooth"
            );
            
            if (!Directory.Exists(photoFolder))
            {
                Directory.CreateDirectory(photoFolder);
            }
        }
        
        /// <summary>
        /// Process a captured photo
        /// </summary>
        public string ProcessCapturedPhoto(PhotoCapturedEventArgs eventArgs)
        {
            return ProcessCapturedPhoto(eventArgs, null);
        }
        
        /// <summary>
        /// Process a captured photo with event context for proper folder organization
        /// </summary>
        public string ProcessCapturedPhoto(PhotoCapturedEventArgs eventArgs, EventData eventData)
        {
            if (eventArgs == null)
            {
                throw new ArgumentNullException(nameof(eventArgs));
            }
            
            try
            {
                // Generate file path with proper event-based folder structure
                string fileName = GeneratePhotoPathWithEvent(eventArgs.FileName, eventData);
                
                // Transfer file from camera
                TransferPhotoFromCamera(eventArgs, fileName);
                
                // Release camera resources
                ReleaseCamera(eventArgs);
                
                // Update tracking
                UpdatePhotoTracking(fileName);
                
                // Save to database
                SavePhotoToDatabase(fileName);
                
                return fileName;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoCaptureService: Failed to process photo: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Generate unique photo path
        /// </summary>
        private string GeneratePhotoPath(string originalFileName)
        {
            string fileName = Path.Combine(photoFolder, Path.GetFileName(originalFileName));
            
            // Generate unique filename if exists
            if (File.Exists(fileName))
            {
                fileName = StaticHelper.GetUniqueFilename(
                    Path.GetDirectoryName(fileName) + "\\" + 
                    Path.GetFileNameWithoutExtension(fileName) + "_", 
                    0,
                    Path.GetExtension(fileName)
                );
            }
            
            // Ensure directory exists
            string directory = Path.GetDirectoryName(fileName);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            Log.Debug($"PhotoCaptureService: Generated path {fileName}");
            return fileName;
        }
        
        /// <summary>
        /// Generate photo path with proper event-based folder structure
        /// Creates: EventName/originals/, EventName/thumbs/, EventName/animation/, EventName/print/, EventName/composed/
        /// </summary>
        private string GeneratePhotoPathWithEvent(string originalFileName, EventData eventData)
        {
            // Create event-based folder structure
            string eventName = GetSafeEventName(eventData);
            string eventFolder = Path.Combine(photoFolder, eventName);
            
            // Create subfolders: originals, thumbs, animation, print, composed
            string originalsFolder = Path.Combine(eventFolder, "originals");
            string thumbsFolder = Path.Combine(eventFolder, "thumbs");
            string animationFolder = Path.Combine(eventFolder, "animation");
            string printFolder = Path.Combine(eventFolder, "print");
            string composedFolder = Path.Combine(eventFolder, "composed");
            
            // Create all directories
            Directory.CreateDirectory(originalsFolder);
            Directory.CreateDirectory(thumbsFolder);
            Directory.CreateDirectory(animationFolder);
            Directory.CreateDirectory(printFolder);
            Directory.CreateDirectory(composedFolder);
            
            // Place original photos in the "originals" subfolder
            string fileName = Path.Combine(originalsFolder, Path.GetFileName(originalFileName));
            
            // Generate unique filename if exists
            if (File.Exists(fileName))
            {
                fileName = StaticHelper.GetUniqueFilename(
                    Path.GetDirectoryName(fileName) + "\\" + 
                    Path.GetFileNameWithoutExtension(fileName) + "_", 
                    0,
                    Path.GetExtension(fileName)
                );
            }
            
            Log.Debug($"PhotoCaptureService: Generated event-based path {fileName}");
            return fileName;
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
        
        /// <summary>
        /// Transfer photo from camera to disk
        /// </summary>
        private void TransferPhotoFromCamera(PhotoCapturedEventArgs eventArgs, string fileName)
        {
            try
            {
                Log.Debug($"PhotoCaptureService: Starting transfer to {fileName}");
                Log.Debug($"PhotoCaptureService: EventArgs - Handle={eventArgs.Handle}, FileName={eventArgs.FileName}");
                Log.Debug($"PhotoCaptureService: CameraDevice={eventArgs.CameraDevice?.GetType().Name}");
                
                // Check if handle is valid
                if (eventArgs.Handle == null)
                {
                    Log.Error("PhotoCaptureService: Handle is null - cannot transfer file");
                    throw new InvalidOperationException("Camera handle is null - photo capture may have failed");
                }
                
                // Check if camera device is valid
                if (eventArgs.CameraDevice == null)
                {
                    Log.Error("PhotoCaptureService: CameraDevice is null - cannot transfer file");
                    throw new InvalidOperationException("Camera device is null - cannot retrieve photo");
                }
                
                // Perform the transfer
                eventArgs.CameraDevice.TransferFile(eventArgs.Handle, fileName);
                
                // Verify file was created
                if (!File.Exists(fileName))
                {
                    Log.Error($"PhotoCaptureService: File was not created at {fileName} after transfer");
                    throw new FileNotFoundException("Photo file was not created after transfer", fileName);
                }
                
                var fileInfo = new FileInfo(fileName);
                Log.Debug($"PhotoCaptureService: Transfer completed - File size: {fileInfo.Length} bytes");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoCaptureService: Transfer failed - {ex.GetType().Name}: {ex.Message}");
                Log.Error($"PhotoCaptureService: Stack trace: {ex.StackTrace}");
                throw new InvalidOperationException($"Failed to retrieve photo from camera: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Release camera resources
        /// </summary>
        private void ReleaseCamera(PhotoCapturedEventArgs eventArgs)
        {
            try
            {
                eventArgs.CameraDevice.ReleaseResurce(eventArgs.Handle);
                Log.Debug("PhotoCaptureService: Camera resources released");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoCaptureService: Failed to release camera: {ex.Message}");
                // Don't throw - camera might still work
            }
        }
        
        /// <summary>
        /// Update photo tracking after capture
        /// </summary>
        private void UpdatePhotoTracking(string fileName)
        {
            PhotoCount++;
            CurrentPhotoIndex++;
            
            if (IsRetakingPhoto && PhotoIndexToRetake >= 0)
            {
                // Replace photo for retake
                if (PhotoIndexToRetake < CapturedPhotoPaths.Count)
                {
                    CapturedPhotoPaths[PhotoIndexToRetake] = fileName;
                    Log.Debug($"PhotoCaptureService: Replaced photo {PhotoIndexToRetake + 1} with retake");
                    
                    // Don't increment index for retakes
                    CurrentPhotoIndex--;
                }
                
                // DON'T reset retake state here - it needs to be preserved for the capture completion handler
                // The state will be reset by ResetRetakeState() method called from the page
                Log.Debug($"PhotoCaptureService: Retake state preserved - IsRetaking={IsRetakingPhoto}, Index={PhotoIndexToRetake}");
            }
            else
            {
                // Add new photo
                CapturedPhotoPaths.Add(fileName);
                Log.Debug($"PhotoCaptureService: Added photo {CurrentPhotoIndex} to list");
            }
        }
        
        /// <summary>
        /// Save photo to database
        /// </summary>
        private void SavePhotoToDatabase(string fileName)
        {
            if (!IsRetakingPhoto && databaseOperations != null)
            {
                databaseOperations.SavePhoto(fileName, CurrentPhotoIndex, "Original");
            }
        }
        
        /// <summary>
        /// Create thumbnail for photo strip
        /// </summary>
        public BitmapImage CreatePhotoThumbnail(string filePath, int maxWidth = 240)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath);
                bitmap.DecodePixelWidth = maxWidth;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoCaptureService: Failed to create thumbnail: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Reset session
        /// </summary>
        public void ResetSession()
        {
            CapturedPhotoPaths.Clear();
            PhotoCount = 0;
            CurrentPhotoIndex = 0;
            IsRetakingPhoto = false;
            PhotoIndexToRetake = -1;
            Log.Debug("PhotoCaptureService: Session reset");
        }
        
        /// <summary>
        /// Start retake for specific photo
        /// </summary>
        public void StartRetake(int photoIndex)
        {
            // Reset any previous retake state before setting new
            if (IsRetakingPhoto)
            {
                Log.Debug($"PhotoCaptureService: Resetting previous retake state before starting new retake");
                ResetRetakeState();
            }
            
            if (photoIndex >= 0 && photoIndex < CapturedPhotoPaths.Count)
            {
                IsRetakingPhoto = true;
                PhotoIndexToRetake = photoIndex;
                Log.Debug($"PhotoCaptureService: Starting retake for photo {photoIndex + 1}");
            }
        }
        
        /// <summary>
        /// Reset retake state after processing
        /// </summary>
        public void ResetRetakeState()
        {
            Log.Debug($"PhotoCaptureService: Resetting retake state (was IsRetaking={IsRetakingPhoto}, Index={PhotoIndexToRetake})");
            IsRetakingPhoto = false;
            PhotoIndexToRetake = -1;
        }
    }
}