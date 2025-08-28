using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Photobooth.Services;

namespace Photobooth.Controls
{
    /// <summary>
    /// Gallery Browser Modal - CLEAN ARCHITECTURE
    /// This control follows service-oriented architecture:
    /// - NO business logic here
    /// - Only UI event routing
    /// - All data operations through GalleryBrowserService
    /// </summary>
    public partial class GalleryBrowserModal : UserControl
    {
        #region Events
        public event EventHandler<SessionSelectedEventArgs> SessionSelected;
        public event EventHandler ModalClosed;
        #endregion

        #region Services (Clean Architecture)
        private GalleryBrowserService _browserService;
        #endregion

        #region Properties
        public bool IsOpen { get; private set; }
        #endregion

        public GalleryBrowserModal()
        {
            InitializeComponent();
            InitializeServices();
        }

        #region Initialization
        private void InitializeServices()
        {
            // Use the browser service for all data operations
            _browserService = new GalleryBrowserService();
            
            // Subscribe to service events
            _browserService.GalleryBrowseLoaded += OnGalleryBrowseLoaded;
            _browserService.GalleryBrowseError += OnGalleryBrowseError;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Show the modal and load gallery sessions
        /// </summary>
        public async void ShowModal()
        {
            try
            {
                IsOpen = true;
                Visibility = Visibility.Visible;
                
                // Load gallery data through service
                await LoadGalleryData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserModal: Error showing modal: {ex.Message}");
                UpdateStatus("Error loading gallery");
            }
        }

        /// <summary>
        /// Hide the modal
        /// </summary>
        public void HideModal()
        {
            IsOpen = false;
            Visibility = Visibility.Collapsed;
            ModalClosed?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Service Event Handlers
        private void OnGalleryBrowseLoaded(object sender, GalleryBrowseLoadedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.BrowseData?.Sessions != null)
                {
                    // Prepare session data for display
                    var displaySessions = e.BrowseData.Sessions.Select(s => new SessionDisplayData
                    {
                        SessionData = s,
                        SessionName = s.SessionName,
                        SessionTime = s.SessionTimeDisplay,
                        PhotoCountDisplay = s.PhotoCountDisplay,
                        PhotoCount = s.PhotoCount,
                        Photos = s.Photos
                    }).ToList();
                    
                    // Bind to UI
                    sessionsGrid.ItemsSource = displaySessions;
                    
                    // Update stats
                    UpdateStats(e.TotalSessions, e.TotalPhotos);
                    UpdateStatus($"Loaded {e.TotalSessions} sessions");
                }
                else
                {
                    UpdateStatus("No sessions found");
                }
            });
        }

        private void OnGalleryBrowseError(object sender, GalleryBrowseErrorEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserModal: Gallery error: {e.Error.Message}");
                UpdateStatus($"Error: {e.Operation}");
            });
        }
        #endregion

        #region UI Event Handlers (Routing Only)
        private void CloseModal_Click(object sender, RoutedEventArgs e)
        {
            HideModal();
        }

        private void SessionCard_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var border = sender as Border;
                var displayData = border?.Tag as SessionDisplayData;
                
                if (displayData?.SessionData != null)
                {
                    System.Diagnostics.Debug.WriteLine($"GalleryBrowserModal: Session selected: {displayData.SessionName}");
                    
                    // Request session view through service
                    _browserService.RequestSessionView(displayData.SessionData);
                    
                    // Raise event with selected session
                    SessionSelected?.Invoke(this, new SessionSelectedEventArgs
                    {
                        SelectedSession = displayData.SessionData
                    });
                    
                    // Close modal after selection
                    HideModal();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserModal: Error selecting session: {ex.Message}");
            }
        }

        private async void RefreshGallery_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Refreshing gallery...");
            await LoadGalleryData();
        }
        #endregion

        #region Helper Methods (UI Updates Only)
        private async System.Threading.Tasks.Task LoadGalleryData()
        {
            try
            {
                UpdateStatus("Loading sessions...");
                
                // Let the service handle all the data loading
                // The service will fire events when complete
                await _browserService.LoadGallerySessionsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GalleryBrowserModal: Error loading gallery: {ex.Message}");
                UpdateStatus("Failed to load gallery");
            }
        }

        private void UpdateStatus(string status)
        {
            if (statusText != null)
                statusText.Text = status;
        }

        private void UpdateStats(int sessions, int photos)
        {
            if (galleryStatsText != null)
                galleryStatsText.Text = $"{sessions} session{(sessions != 1 ? "s" : "")} • {photos} photo{(photos != 1 ? "s" : "")}";
        }
        #endregion

        #region Cleanup
        public void Cleanup()
        {
            if (_browserService != null)
            {
                _browserService.GalleryBrowseLoaded -= OnGalleryBrowseLoaded;
                _browserService.GalleryBrowseError -= OnGalleryBrowseError;
            }
        }
        #endregion

        #region Display Data Class
        /// <summary>
        /// View model for session display in the grid
        /// </summary>
        private class SessionDisplayData
        {
            public GallerySessionInfo SessionData { get; set; }
            public string SessionName { get; set; }
            public string SessionTime { get; set; }
            public string PhotoCountDisplay { get; set; }
            public int PhotoCount { get; set; }
            public List<GalleryPhotoInfo> Photos { get; set; }
            
            // Single thumbnail path - use first photo or composed image if available
            public string ThumbnailPath 
            { 
                get 
                {
                    if (Photos?.Count > 0)
                    {
                        // Prefer thumbnail path if available, otherwise use full image
                        var firstPhoto = Photos[0];
                        return !string.IsNullOrEmpty(firstPhoto.ThumbnailPath) 
                            ? firstPhoto.ThumbnailPath 
                            : firstPhoto.FilePath;
                    }
                    return null;
                }
            }
        }
        #endregion
    }

    #region Event Args
    public class SessionSelectedEventArgs : EventArgs
    {
        public GallerySessionInfo SelectedSession { get; set; }
    }
    #endregion
}