using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to handle all file system operations and validations
    /// Centralizes file existence checks, path validation, and file operations
    /// This keeps file system logic out of UI pages (clean architecture)
    /// </summary>
    public class FileValidationService
    {
        private static FileValidationService _instance;
        public static FileValidationService Instance => _instance ?? (_instance = new FileValidationService());

        /// <summary>
        /// Validates if a file path exists and is not empty
        /// </summary>
        public bool ValidateFilePath(string filePath)
        {
            return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
        }

        /// <summary>
        /// Validates multiple file paths and returns only valid ones
        /// </summary>
        public List<string> GetValidFilePaths(IEnumerable<string> filePaths)
        {
            if (filePaths == null) return new List<string>();
            return filePaths.Where(path => ValidateFilePath(path)).ToList();
        }

        /// <summary>
        /// Validates a photo object and checks if its file exists
        /// </summary>
        public bool ValidatePhotoFile(PhotoGalleryData photo)
        {
            return photo != null && ValidateFilePath(photo.FilePath);
        }

        /// <summary>
        /// Filters a list of photos to only those with valid file paths
        /// </summary>
        public List<PhotoGalleryData> GetValidPhotos(IEnumerable<PhotoGalleryData> photos)
        {
            if (photos == null) return new List<PhotoGalleryData>();
            return photos.Where(p => ValidatePhotoFile(p)).ToList();
        }

        /// <summary>
        /// Gets photos of a specific type with valid file paths
        /// </summary>
        public List<PhotoGalleryData> GetValidPhotosByType(IEnumerable<PhotoGalleryData> photos, string photoType)
        {
            if (photos == null) return new List<PhotoGalleryData>();
            return photos.Where(p => p.PhotoType == photoType && ValidatePhotoFile(p)).ToList();
        }

        /// <summary>
        /// Finds the first valid photo of specified types (in priority order)
        /// </summary>
        public PhotoGalleryData FindFirstValidPhotoByTypes(IEnumerable<PhotoGalleryData> photos, params string[] photoTypes)
        {
            if (photos == null || photoTypes == null) return null;
            
            foreach (var photoType in photoTypes)
            {
                var photo = photos.FirstOrDefault(p => p.PhotoType == photoType && ValidatePhotoFile(p));
                if (photo != null) return photo;
            }
            return null;
        }

        /// <summary>
        /// Validates if a path is a video file (MP4)
        /// </summary>
        public bool IsVideoFile(string filePath)
        {
            if (!ValidateFilePath(filePath)) return false;
            string extension = Path.GetExtension(filePath)?.ToLower();
            return extension == ".mp4";
        }

        /// <summary>
        /// Validates if a path is a GIF file
        /// </summary>
        public bool IsGifFile(string filePath)
        {
            if (!ValidateFilePath(filePath)) return false;
            string extension = Path.GetExtension(filePath)?.ToLower();
            return extension == ".gif";
        }

        /// <summary>
        /// Gets the file type (MP4, GIF, IMAGE, or UNKNOWN)
        /// </summary>
        public string GetFileType(string filePath)
        {
            if (!ValidateFilePath(filePath)) return "UNKNOWN";
            
            string extension = Path.GetExtension(filePath)?.ToLower();
            switch (extension)
            {
                case ".mp4": return "MP4";
                case ".gif": return "GIF";
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                    return "IMAGE";
                default:
                    return "UNKNOWN";
            }
        }

        /// <summary>
        /// Validates session completed data has valid file paths
        /// </summary>
        public bool ValidateCompletedSession(CompletedSessionData session)
        {
            if (session == null) return false;
            
            // At minimum, should have composed image or photos
            bool hasValidComposedImage = ValidateFilePath(session.ComposedImagePath);
            bool hasValidPhotos = session.PhotoPaths?.Any(path => ValidateFilePath(path)) == true;
            
            return hasValidComposedImage || hasValidPhotos;
        }

        /// <summary>
        /// Gets all valid file paths from a completed session
        /// </summary>
        public List<string> GetValidSessionFiles(CompletedSessionData session)
        {
            var validFiles = new List<string>();
            
            if (session == null) return validFiles;
            
            // Add composed image if valid
            if (ValidateFilePath(session.ComposedImagePath))
                validFiles.Add(session.ComposedImagePath);
            
            // Add GIF/video if valid
            if (ValidateFilePath(session.GifPath))
                validFiles.Add(session.GifPath);
            
            // Add valid photo paths
            if (session.PhotoPaths != null)
                validFiles.AddRange(GetValidFilePaths(session.PhotoPaths));
            
            return validFiles;
        }

        /// <summary>
        /// Logs validation result for debugging
        /// </summary>
        public bool ValidateFilePathWithLogging(string filePath, string context = "")
        {
            bool isValid = ValidateFilePath(filePath);
            
            if (!isValid)
            {
                if (string.IsNullOrEmpty(filePath))
                    Log.Debug($"FileValidation [{context}]: Path is empty");
                else if (!File.Exists(filePath))
                    Log.Debug($"FileValidation [{context}]: File does not exist: {filePath}");
            }
            else
            {
                Log.Debug($"FileValidation [{context}]: Valid file: {filePath}");
            }
            
            return isValid;
        }

        /// <summary>
        /// Analyzes image dimensions and properties
        /// Replaces direct System.Drawing usage in pages
        /// </summary>
        public ImageAnalysisResult AnalyzeImageDimensions(string imagePath)
        {
            if (!ValidateFilePath(imagePath))
            {
                Log.Error($"FileValidation: Cannot analyze invalid image path: {imagePath}");
                return null;
            }

            try
            {
                using (var img = System.Drawing.Image.FromFile(imagePath))
                {
                    var result = new ImageAnalysisResult
                    {
                        FilePath = imagePath,
                        Width = img.Width,
                        Height = img.Height,
                        Is4x6Duplicate = (img.Width == 1200 && img.Height == 1800),
                        Is2x6Format = (img.Width == 600 && img.Height == 1800),
                        AspectRatio = (double)img.Width / img.Height
                    };

                    Log.Debug($"Image Analysis: {imagePath} - {result.Width}x{result.Height}, 4x6: {result.Is4x6Duplicate}, 2x6: {result.Is2x6Format}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error analyzing image dimensions: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Compares two image paths and determines if they are different
        /// Used for print path vs display path comparison
        /// </summary>
        public PrintPathAnalysis ComparePrintPaths(string displayPath, string printPath)
        {
            var analysis = new PrintPathAnalysis
            {
                DisplayPath = displayPath,
                PrintPath = printPath,
                PathsDiffer = displayPath != printPath
            };

            if (analysis.PathsDiffer && ValidateFilePath(printPath))
            {
                var imageInfo = AnalyzeImageDimensions(printPath);
                if (imageInfo != null)
                {
                    analysis.PrintImageInfo = imageInfo;
                    analysis.IsDuplicatedFor4x6 = imageInfo.Is4x6Duplicate;
                }
            }

            return analysis;
        }

        /// <summary>
        /// Scans directories for recent image files
        /// Replaces Directory.GetFiles logic in pages
        /// </summary>
        public List<string> ScanForRecentImages(params string[] folders)
        {
            var recentFiles = new List<string>();
            var imageExtensions = new[] { "*.jpg", "*.jpeg", "*.png" };

            foreach (var folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    continue;

                try
                {
                    foreach (var extension in imageExtensions)
                    {
                        var files = Directory.GetFiles(folder, extension)
                            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                            .Take(10); // Limit to recent files
                        
                        recentFiles.AddRange(files);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"Error scanning folder {folder}: {ex.Message}");
                }
            }

            return recentFiles.OrderByDescending(f => new FileInfo(f).LastWriteTime).ToList();
        }

        /// <summary>
        /// Gets standard photo folders to scan
        /// </summary>
        public string[] GetStandardPhotoFolders()
        {
            return new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Photobooth")
            };
        }

        /// <summary>
        /// Gets default output folder for photos
        /// </summary>
        public string GetDefaultPhotoOutputFolder(string subfolder = "Photobooth")
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                subfolder
            );
        }

        /// <summary>
        /// Filters files by creation time
        /// </summary>
        public List<string> FilterFilesByCreationTime(IEnumerable<string> files, int minutesAgo)
        {
            if (files == null) return new List<string>();
            
            var cutoffTime = DateTime.Now.AddMinutes(-minutesAgo);
            return files
                .Where(f => ValidateFilePath(f) && File.GetCreationTime(f) > cutoffTime)
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToList();
        }

        /// <summary>
        /// Gets file extension without the dot
        /// </summary>
        public string GetFileExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            var extension = Path.GetExtension(filePath);
            return extension?.TrimStart('.').ToLower() ?? string.Empty;
        }
    }

    /// <summary>
    /// Result of image dimension analysis
    /// </summary>
    public class ImageAnalysisResult
    {
        public string FilePath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Is4x6Duplicate { get; set; }
        public bool Is2x6Format { get; set; }
        public double AspectRatio { get; set; }
    }

    /// <summary>
    /// Result of print path comparison
    /// </summary>
    public class PrintPathAnalysis
    {
        public string DisplayPath { get; set; }
        public string PrintPath { get; set; }
        public bool PathsDiffer { get; set; }
        public bool IsDuplicatedFor4x6 { get; set; }
        public ImageAnalysisResult PrintImageInfo { get; set; }
    }
}