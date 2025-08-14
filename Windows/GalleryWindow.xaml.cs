using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth.Windows
{
    public partial class GalleryWindow : Window
    {
        private ObservableCollection<SessionGroup> sessions;
        private List<PhotoItem> allPhotos;
        private int currentImageIndex = -1;
        private string photoDirectory;

        public GalleryWindow()
        {
            InitializeComponent();
            sessions = new ObservableCollection<SessionGroup>();
            allPhotos = new List<PhotoItem>();
            sessionsItemsControl.ItemsSource = sessions;
            
            // Set the photo directory from settings or default
            photoDirectory = Properties.Settings.Default.PhotoLocation;
            if (string.IsNullOrEmpty(photoDirectory))
            {
                photoDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");
            }
            
            LoadPhotos();
        }

        private void LoadPhotos()
        {
            sessions.Clear();
            allPhotos.Clear();
            
            if (!Directory.Exists(photoDirectory))
            {
                statusText.Text = "Photo directory not found";
                return;
            }
            
            try
            {
                // Get all image files
                var imageFiles = Directory.GetFiles(photoDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsImageFile(f))
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToList();
                
                // Group photos by session (based on creation time proximity)
                var sessionGroups = new Dictionary<string, SessionGroup>();
                DateTime? lastPhotoTime = null;
                string currentSessionId = null;
                int sessionCounter = 1;
                
                foreach (var file in imageFiles)
                {
                    var fileInfo = new FileInfo(file);
                    var creationTime = fileInfo.CreationTime;
                    
                    // Create new session if time gap is more than 30 minutes or first photo
                    if (lastPhotoTime == null || (creationTime - lastPhotoTime.Value).TotalMinutes > 30)
                    {
                        currentSessionId = $"Session {sessionCounter}";
                        sessionGroups[currentSessionId] = new SessionGroup
                        {
                            SessionName = currentSessionId,
                            SessionTime = creationTime.ToString("yyyy-MM-dd HH:mm"),
                            Photos = new ObservableCollection<PhotoItem>()
                        };
                        sessionCounter++;
                    }
                    
                    // Determine photo type
                    string photoType = "Original";
                    string badgeColor = "#2196F3"; // Blue for original
                    
                    // Check if it's a video/animation
                    string extension = Path.GetExtension(file).ToLower();
                    if (extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".wmv")
                    {
                        photoType = "Video";
                        badgeColor = "#FF5722"; // Orange for video
                    }
                    else if (extension == ".gif")
                    {
                        photoType = "GIF";
                        badgeColor = "#FF9800"; // Amber for GIF
                    }
                    else if (file.Contains("Animation"))
                    {
                        photoType = "Animation";
                        badgeColor = "#FF5722"; // Orange for animation
                    }
                    else if (file.Contains("_filtered"))
                    {
                        photoType = "Filtered";
                        badgeColor = "#9C27B0"; // Purple for filtered
                    }
                    else if (file.Contains("template") || file.Contains("processed") || file.Contains("Composed"))
                    {
                        photoType = "Template";
                        badgeColor = "#4CAF50"; // Green for template
                    }
                    
                    // For videos, try to find a thumbnail or use a placeholder
                    string thumbnailPath = file;
                    if (photoType == "Video" || photoType == "Animation")
                    {
                        // Look for a thumbnail in the same directory
                        string dir = Path.GetDirectoryName(file);
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                        
                        // Check for possible thumbnail files
                        var possibleThumbnails = new[] 
                        {
                            Path.Combine(dir, nameWithoutExt + "_thumb.jpg"),
                            Path.Combine(dir, nameWithoutExt + ".jpg"),
                            Path.Combine(dir, "..", "Thumbnails", Path.GetFileName(file) + ".jpg")
                        };
                        
                        foreach (var thumb in possibleThumbnails)
                        {
                            if (File.Exists(thumb))
                            {
                                thumbnailPath = thumb;
                                break;
                            }
                        }
                        
                        // If no thumbnail found and it's in Animations folder, 
                        // try to use the first photo from the parent folder
                        if (thumbnailPath == file && dir.Contains("Animations"))
                        {
                            string parentDir = Path.GetDirectoryName(dir);
                            var firstPhoto = Directory.GetFiles(parentDir, "IMG_*.JPG")
                                                     .OrderBy(f => f)
                                                     .FirstOrDefault();
                            if (firstPhoto != null)
                                thumbnailPath = firstPhoto;
                        }
                    }
                    
                    var photoItem = new PhotoItem
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        FileSize = FormatFileSize(fileInfo.Length),
                        PhotoType = photoType,
                        TypeBadgeColor = badgeColor,
                        ThumbnailPath = thumbnailPath,
                        CreationTime = creationTime
                    };
                    
                    sessionGroups[currentSessionId].Photos.Add(photoItem);
                    allPhotos.Add(photoItem);
                    lastPhotoTime = creationTime;
                }
                
                // Add sessions to collection
                foreach (var session in sessionGroups.Values)
                {
                    session.PhotoCount = $"{session.Photos.Count} photos";
                    sessions.Add(session);
                }
                
                // Update status
                int totalPhotos = allPhotos.Count;
                int totalSessions = sessions.Count;
                photoCountText.Text = $"{totalPhotos} photos in {totalSessions} sessions";
                statusText.Text = "Photos loaded successfully";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error loading photos: {ex.Message}";
            }
        }

        private bool IsImageFile(string path)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };
            string[] videoExtensions = { ".mp4", ".avi", ".mov", ".wmv" };
            string extension = Path.GetExtension(path).ToLower();
            return imageExtensions.Contains(extension) || videoExtensions.Contains(extension);
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void Photo_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border?.Tag is string filePath)
            {
                ShowFullScreenImage(filePath);
            }
        }

        private void ShowFullScreenImage(string filePath)
        {
            try
            {
                // Check if it's a video file
                string extension = Path.GetExtension(filePath).ToLower();
                if (extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".wmv")
                {
                    // Open video with default player
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    return;
                }
                
                // For images, show in full screen viewer
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath);
                bitmap.EndInit();
                
                fullScreenImage.Source = bitmap;
                imageViewerOverlay.Visibility = Visibility.Visible;
                
                // Find current image index for navigation
                currentImageIndex = allPhotos.FindIndex(p => p.FilePath == filePath);
                UpdateNavigationButtons();
                
                selectedInfoText.Text = Path.GetFileName(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateNavigationButtons()
        {
            prevImageButton.IsEnabled = currentImageIndex > 0;
            nextImageButton.IsEnabled = currentImageIndex < allPhotos.Count - 1;
        }

        private void PrevImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageIndex > 0)
            {
                currentImageIndex--;
                ShowFullScreenImage(allPhotos[currentImageIndex].FilePath);
            }
        }

        private void NextImage_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageIndex < allPhotos.Count - 1)
            {
                currentImageIndex++;
                ShowFullScreenImage(allPhotos[currentImageIndex].FilePath);
            }
        }

        private void CloseImageViewer_Click(object sender, RoutedEventArgs e)
        {
            imageViewerOverlay.Visibility = Visibility.Collapsed;
            selectedInfoText.Text = "";
        }

        private void ImageViewerOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            // Close viewer when clicking outside the image
            if (e.OriginalSource == imageViewerOverlay)
            {
                imageViewerOverlay.Visibility = Visibility.Collapsed;
                selectedInfoText.Text = "";
            }
        }

        private void ViewMode_Changed(object sender, RoutedEventArgs e)
        {
            // TODO: Implement different view modes if needed
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadPhotos();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            
            if (imageViewerOverlay.Visibility == Visibility.Visible)
            {
                switch (e.Key)
                {
                    case Key.Escape:
                        imageViewerOverlay.Visibility = Visibility.Collapsed;
                        break;
                    case Key.Left:
                        if (prevImageButton.IsEnabled)
                            PrevImage_Click(null, null);
                        break;
                    case Key.Right:
                        if (nextImageButton.IsEnabled)
                            NextImage_Click(null, null);
                        break;
                }
            }
        }
    }

    // Data Models
    public class SessionGroup : INotifyPropertyChanged
    {
        private string _sessionName;
        private string _sessionTime;
        private string _photoCount;
        private ObservableCollection<PhotoItem> _photos;

        public string SessionName
        {
            get => _sessionName;
            set { _sessionName = value; OnPropertyChanged(nameof(SessionName)); }
        }

        public string SessionTime
        {
            get => _sessionTime;
            set { _sessionTime = value; OnPropertyChanged(nameof(SessionTime)); }
        }

        public string PhotoCount
        {
            get => _photoCount;
            set { _photoCount = value; OnPropertyChanged(nameof(PhotoCount)); }
        }

        public ObservableCollection<PhotoItem> Photos
        {
            get => _photos;
            set { _photos = value; OnPropertyChanged(nameof(Photos)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PhotoItem : INotifyPropertyChanged
    {
        private string _filePath;
        private string _fileName;
        private string _fileSize;
        private string _photoType;
        private string _typeBadgeColor;
        private string _thumbnailPath;
        private DateTime _creationTime;

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(nameof(FilePath)); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(nameof(FileName)); }
        }

        public string FileSize
        {
            get => _fileSize;
            set { _fileSize = value; OnPropertyChanged(nameof(FileSize)); }
        }

        public string PhotoType
        {
            get => _photoType;
            set { _photoType = value; OnPropertyChanged(nameof(PhotoType)); }
        }

        public string TypeBadgeColor
        {
            get => _typeBadgeColor;
            set { _typeBadgeColor = value; OnPropertyChanged(nameof(TypeBadgeColor)); }
        }

        public string ThumbnailPath
        {
            get => _thumbnailPath;
            set { _thumbnailPath = value; OnPropertyChanged(nameof(ThumbnailPath)); }
        }

        public DateTime CreationTime
        {
            get => _creationTime;
            set { _creationTime = value; OnPropertyChanged(nameof(CreationTime)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}