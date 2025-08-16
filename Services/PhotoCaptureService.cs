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
            if (eventArgs == null)
            {
                throw new ArgumentNullException(nameof(eventArgs));
            }
            
            try
            {
                // Generate file path
                string fileName = GeneratePhotoPath(eventArgs.FileName);
                
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
        /// Transfer photo from camera to disk
        /// </summary>
        private void TransferPhotoFromCamera(PhotoCapturedEventArgs eventArgs, string fileName)
        {
            try
            {
                Log.Debug($"PhotoCaptureService: Transferring to {fileName}");
                eventArgs.CameraDevice.TransferFile(eventArgs.Handle, fileName);
                Log.Debug("PhotoCaptureService: Transfer completed");
            }
            catch (Exception ex)
            {
                Log.Error($"PhotoCaptureService: Transfer failed: {ex.Message}");
                throw;
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
                
                // Reset retake state
                IsRetakingPhoto = false;
                PhotoIndexToRetake = -1;
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
            if (photoIndex >= 0 && photoIndex < CapturedPhotoPaths.Count)
            {
                IsRetakingPhoto = true;
                PhotoIndexToRetake = photoIndex;
                Log.Debug($"PhotoCaptureService: Starting retake for photo {photoIndex + 1}");
            }
        }
    }
}