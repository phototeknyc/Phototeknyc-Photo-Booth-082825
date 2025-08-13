using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Services;

namespace Photobooth.Controls
{
    public partial class GalleryOverlayControl : UserControl
    {
        private ObservableCollection<SessionGroup> sessions;
        private List<PhotoItem> allPhotos;
        private int currentImageIndex = -1;
        private string photoDirectory;
        private bool isLoading = false;
        private DispatcherTimer loadTimer;
        private int thumbnailLoadBatchSize = 10;
        private Queue<PhotoItem> thumbnailLoadQueue;

        public GalleryOverlayControl()
        {
            InitializeComponent();
            sessions = new ObservableCollection<SessionGroup>();
            allPhotos = new List<PhotoItem>();
            sessionsItemsControl.ItemsSource = sessions;
            thumbnailLoadQueue = new Queue<PhotoItem>();
            
            // Set the photo directory from settings or default
            photoDirectory = Properties.Settings.Default.PhotoLocation;
            if (string.IsNullOrEmpty(photoDirectory))
            {
                photoDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");
            }
            
            // Setup lazy loading timer for thumbnails
            loadTimer = new DispatcherTimer();
            loadTimer.Interval = TimeSpan.FromMilliseconds(50);
            loadTimer.Tick += LoadThumbnailBatch;
        }

        public async void LoadPhotos()
        {
            if (isLoading) return;
            isLoading = true;
            
            sessions.Clear();
            allPhotos.Clear();
            thumbnailLoadQueue.Clear();
            loadTimer.Stop();
            
            statusText.Text = "Loading photos...";
            
            if (!Directory.Exists(photoDirectory))
            {
                statusText.Text = "Photo directory not found";
                isLoading = false;
                return;
            }
            
            try
            {
                await Task.Run(() => LoadPhotosAsync());
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error loading photos: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Gallery load error: {ex}");
            }
            finally
            {
                isLoading = false;
            }
        }

        private void LoadPhotosAsync()
        {
            try
            {
                // Get all image files
                var imageFiles = Directory.GetFiles(photoDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsImageFile(f))
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Take(500) // Limit initial load to 500 most recent photos
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
                            SessionId = $"{creationTime:yyyyMMdd_HHmmss}_{sessionCounter}",
                            SessionTime = creationTime.ToString("yyyy-MM-dd HH:mm"),
                            SessionStartTime = creationTime,
                            Photos = new ObservableCollection<PhotoItem>(),
                            OriginalPhotos = new List<PhotoItem>(),
                            FilteredPhotos = new List<PhotoItem>(),
                            TemplatePhotos = new List<PhotoItem>()
                        };
                        sessionCounter++;
                    }
                    
                    // Determine photo type
                    string photoType = "Original";
                    string badgeColor = "#2196F3"; // Blue for original
                    
                    if (file.Contains("_filtered"))
                    {
                        photoType = "Filtered";
                        badgeColor = "#9C27B0"; // Purple for filtered
                    }
                    else if (file.Contains("template") || file.Contains("processed"))
                    {
                        photoType = "Template";
                        badgeColor = "#4CAF50"; // Green for template
                    }
                    
                    var photoItem = new PhotoItem
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        FileSize = FormatFileSize(fileInfo.Length),
                        PhotoType = photoType,
                        TypeBadgeColor = badgeColor,
                        ThumbnailPath = null, // Start with null, load lazily
                        CreationTime = creationTime,
                        IsLoading = true,
                        SessionId = sessionGroups[currentSessionId].SessionId,
                        SequenceNumber = sessionGroups[currentSessionId].Photos.Count + 1,
                        OriginalFilePath = photoType == "Original" ? file : null
                    };
                    
                    // Add to main photos collection
                    sessionGroups[currentSessionId].Photos.Add(photoItem);
                    allPhotos.Add(photoItem);
                    thumbnailLoadQueue.Enqueue(photoItem);
                    
                    // Also categorize by type for easy access during printing
                    var session = sessionGroups[currentSessionId];
                    switch (photoType)
                    {
                        case "Original":
                            session.OriginalPhotos.Add(photoItem);
                            break;
                        case "Filtered":
                            session.FilteredPhotos.Add(photoItem);
                            break;
                        case "Template":
                            session.TemplatePhotos.Add(photoItem);
                            break;
                    }
                    
                    lastPhotoTime = creationTime;
                }
                
                // Add sessions to collection on UI thread
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var session in sessionGroups.Values)
                    {
                        session.PhotoCount = $"{session.Photos.Count} photos";
                        sessions.Add(session);
                    }
                    
                    // Update status
                    int totalPhotos = allPhotos.Count;
                    int totalSessions = sessions.Count;
                    photoCountText.Text = $"{totalPhotos} photos in {totalSessions} sessions";
                    statusText.Text = "Loading thumbnails...";
                    
                    // Start loading thumbnails
                    loadTimer.Start();
                }));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    statusText.Text = $"Error: {ex.Message}";
                }));
            }
        }

        private void LoadThumbnailBatch(object sender, EventArgs e)
        {
            if (thumbnailLoadQueue.Count == 0)
            {
                loadTimer.Stop();
                statusText.Text = "Ready";
                return;
            }
            
            // Load a batch of thumbnails
            for (int i = 0; i < thumbnailLoadBatchSize && thumbnailLoadQueue.Count > 0; i++)
            {
                var photoItem = thumbnailLoadQueue.Dequeue();
                LoadThumbnailAsync(photoItem);
            }
            
            statusText.Text = $"Loading thumbnails... {thumbnailLoadQueue.Count} remaining";
        }

        private async void LoadThumbnailAsync(PhotoItem photoItem)
        {
            await Task.Run(() =>
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 200; // Thumbnail size
                        bitmap.UriSource = new Uri(photoItem.FilePath);
                        bitmap.EndInit();
                        bitmap.Freeze(); // Freeze for cross-thread access
                        
                        photoItem.ThumbnailPath = photoItem.FilePath;
                        photoItem.ThumbnailImage = bitmap;
                        photoItem.IsLoading = false;
                    }), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading thumbnail {photoItem.FilePath}: {ex.Message}");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        photoItem.IsLoading = false;
                    }));
                }
            });
        }

        private bool IsImageFile(string path)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };
            string extension = Path.GetExtension(path).ToLower();
            return imageExtensions.Contains(extension);
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
                
                var photoItem = allPhotos[currentImageIndex];
                imageInfoText.Text = $"{photoItem.FileName} • {photoItem.FileSize} • {photoItem.PhotoType}";
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
            imageInfoText.Text = "";
        }

        private void ImageViewerOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            // Close viewer when clicking outside the image
            if (e.OriginalSource == imageViewerOverlay)
            {
                imageViewerOverlay.Visibility = Visibility.Collapsed;
                selectedInfoText.Text = "";
                imageInfoText.Text = "";
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadPhotos();
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check if printing is enabled
                if (!Properties.Settings.Default.EnablePrinting)
                {
                    MessageBox.Show("Printing is disabled in settings.", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Check if there are any sessions to print
                if (sessions.Count == 0)
                {
                    MessageBox.Show("No photo sessions available to print.", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show print selection dialog
                var printDialog = new PrintDialog(sessions);
                printDialog.Owner = Window.GetWindow(this);
                var result = printDialog.ShowDialog();
                
                if (result == true)
                {
                    // Print was successful, maybe update the status
                    statusText.Text = "Photos printed successfully";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening print dialog: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"Print button error: {ex}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
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
            else if (e.Key == Key.Escape)
            {
                this.Visibility = Visibility.Collapsed;
            }
        }

        public void Show()
        {
            this.Visibility = Visibility.Visible;
            
            // Update print button visibility based on settings
            if (printButton != null)
            {
                printButton.Visibility = Properties.Settings.Default.ShowPrintButton && Properties.Settings.Default.EnablePrinting 
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            
            LoadPhotos();
        }

        public void Hide()
        {
            this.Visibility = Visibility.Collapsed;
        }

        // Public methods for accessing session data for printing
        public ObservableCollection<SessionGroup> GetSessions()
        {
            return sessions;
        }

        public SessionGroup GetCurrentSession()
        {
            // Return the most recent session
            return sessions.FirstOrDefault();
        }

        public List<PhotoItem> GetSessionOriginals(string sessionId)
        {
            var session = sessions.FirstOrDefault(s => s.SessionId == sessionId);
            return session?.OriginalPhotos ?? new List<PhotoItem>();
        }

        public List<PhotoItem> GetAllOriginalsForPrinting()
        {
            var originals = new List<PhotoItem>();
            foreach (var session in sessions)
            {
                originals.AddRange(session.OriginalPhotos);
            }
            return originals;
        }

        public Dictionary<string, List<PhotoItem>> GetPhotosBySessionForPrinting()
        {
            var photosBySession = new Dictionary<string, List<PhotoItem>>();
            foreach (var session in sessions)
            {
                var sessionKey = $"{session.SessionName} - {session.SessionTime}";
                var sessionPhotos = new List<PhotoItem>();
                
                // Add originals first, then filtered, then templates
                sessionPhotos.AddRange(session.OriginalPhotos);
                sessionPhotos.AddRange(session.FilteredPhotos);
                sessionPhotos.AddRange(session.TemplatePhotos);
                
                if (sessionPhotos.Count > 0)
                {
                    photosBySession[sessionKey] = sessionPhotos;
                }
            }
            return photosBySession;
        }
    }

    // Data Models
    public class SessionGroup : INotifyPropertyChanged
    {
        private string _sessionName;
        private string _sessionTime;
        private string _photoCount;
        private ObservableCollection<PhotoItem> _photos;
        private List<PhotoItem> _originalPhotos;
        private List<PhotoItem> _filteredPhotos;
        private List<PhotoItem> _templatePhotos;
        private string _sessionId;
        private DateTime _sessionStartTime;

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

        public List<PhotoItem> OriginalPhotos
        {
            get => _originalPhotos ?? (_originalPhotos = new List<PhotoItem>());
            set { _originalPhotos = value; OnPropertyChanged(nameof(OriginalPhotos)); }
        }

        public List<PhotoItem> FilteredPhotos
        {
            get => _filteredPhotos ?? (_filteredPhotos = new List<PhotoItem>());
            set { _filteredPhotos = value; OnPropertyChanged(nameof(FilteredPhotos)); }
        }

        public List<PhotoItem> TemplatePhotos
        {
            get => _templatePhotos ?? (_templatePhotos = new List<PhotoItem>());
            set { _templatePhotos = value; OnPropertyChanged(nameof(TemplatePhotos)); }
        }

        public string SessionId
        {
            get => _sessionId;
            set { _sessionId = value; OnPropertyChanged(nameof(SessionId)); }
        }

        public DateTime SessionStartTime
        {
            get => _sessionStartTime;
            set { _sessionStartTime = value; OnPropertyChanged(nameof(SessionStartTime)); }
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
        private BitmapImage _thumbnailImage;
        private DateTime _creationTime;
        private bool _isLoading;
        private string _originalFilePath;
        private string _sessionId;
        private int _sequenceNumber;

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

        public BitmapImage ThumbnailImage
        {
            get => _thumbnailImage;
            set { _thumbnailImage = value; OnPropertyChanged(nameof(ThumbnailImage)); }
        }

        public DateTime CreationTime
        {
            get => _creationTime;
            set { _creationTime = value; OnPropertyChanged(nameof(CreationTime)); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        public string OriginalFilePath
        {
            get => _originalFilePath;
            set { _originalFilePath = value; OnPropertyChanged(nameof(OriginalFilePath)); }
        }

        public string SessionId
        {
            get => _sessionId;
            set { _sessionId = value; OnPropertyChanged(nameof(SessionId)); }
        }

        public int SequenceNumber
        {
            get => _sequenceNumber;
            set { _sequenceNumber = value; OnPropertyChanged(nameof(SequenceNumber)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}