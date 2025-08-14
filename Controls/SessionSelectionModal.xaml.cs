using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Photobooth.Database;
using Photobooth.Services;

namespace Photobooth.Controls
{
    public partial class SessionSelectionModal : UserControl
    {
        private TemplateDatabase database;
        private EventService eventService;
        private ObservableCollection<SessionDisplayItem> allSessions;
        private ObservableCollection<SessionDisplayItem> filteredSessions;
        private SessionDisplayItem selectedSession;
        
        // Current event filter
        private int? currentEventId = null;
        
        // Events
        public event Action<PhotoSessionData> SessionSelected;
        public event Action ModalClosed;

        public SessionSelectionModal()
        {
            InitializeComponent();
            
            database = new TemplateDatabase();
            eventService = new EventService();
            allSessions = new ObservableCollection<SessionDisplayItem>();
            filteredSessions = new ObservableCollection<SessionDisplayItem>();
            
            SessionsItemsControl.ItemsSource = filteredSessions;
            
            LoadEvents();
        }

        public void ShowModal(int? eventId = null)
        {
            currentEventId = eventId;
            ModalOverlay.Visibility = Visibility.Visible;
            
            // Load sessions with event filter
            LoadSessions();
            
            // Animate modal in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            var scaleIn = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            ModalOverlay.BeginAnimation(OpacityProperty, fadeIn);
            
            // Get the existing ScaleTransform from XAML
            if (ModalContainer.RenderTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.CenterX = ModalContainer.ActualWidth / 2;
                scaleTransform.CenterY = ModalContainer.ActualHeight / 2;
                scaleTransform.ScaleX = 0.8;
                scaleTransform.ScaleY = 0.8;
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
            }
            
            LoadSessions();
        }

        public void HideModal()
        {
            // Animate modal out
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => ModalOverlay.Visibility = Visibility.Collapsed;
            
            ModalOverlay.BeginAnimation(OpacityProperty, fadeOut);
            ModalClosed?.Invoke();
        }

        private async void LoadEvents()
        {
            try
            {
                var events = database.GetAllEvents().Where(e => e.IsActive).ToList();
                
                EventFilterCombo.Items.Clear();
                EventFilterCombo.Items.Add(new ComboBoxItem { Content = "All Events", Tag = -1 });
                
                foreach (var evt in events)
                {
                    EventFilterCombo.Items.Add(new ComboBoxItem { Content = evt.Name, Tag = evt.Id });
                }
                
                EventFilterCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading events: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"LoadEvents error: {ex}");
            }
        }

        private async void LoadSessions()
        {
            try
            {
                StatusText.Text = "Loading sessions...";
                
                await Task.Run(() =>
                {
                    var sessions = currentEventId.HasValue 
                        ? database.GetPhotoSessions(currentEventId.Value) 
                        : database.GetPhotoSessions();
                    
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        allSessions.Clear();
                        
                        foreach (var session in sessions)
                        {
                            // Get thumbnail from composed images
                            string thumbnailPath = null;
                            try
                            {
                                var composedImages = database.GetSessionComposedImages(session.Id);
                                if (composedImages != null && composedImages.Any())
                                {
                                    // Use the thumbnail from the first composed image
                                    var firstComposed = composedImages.OrderByDescending(c => c.CreatedDate).FirstOrDefault();
                                    if (firstComposed != null && !string.IsNullOrEmpty(firstComposed.ThumbnailPath))
                                    {
                                        thumbnailPath = firstComposed.ThumbnailPath;
                                    }
                                    else if (firstComposed != null && !string.IsNullOrEmpty(firstComposed.FilePath) && System.IO.File.Exists(firstComposed.FilePath))
                                    {
                                        // Use the composed image itself as thumbnail if no thumbnail exists
                                        thumbnailPath = firstComposed.FilePath;
                                    }
                                }
                            }
                            catch { /* Ignore thumbnail loading errors */ }
                            
                            var displayItem = new SessionDisplayItem
                            {
                                SessionData = session,
                                SessionName = session.SessionName ?? "Unnamed Session",
                                EventName = session.EventName ?? "Unknown Event",
                                TemplateName = session.TemplateName ?? "Unknown Template",
                                SessionTimeText = FormatSessionTime(session.StartTime, session.EndTime),
                                PhotoCountText = $"{session.ActualPhotoCount} photos",
                                ComposedImageCount = session.ComposedImageCount,
                                IsCompleted = session.EndTime.HasValue,
                                ThumbnailPath = thumbnailPath
                            };
                            
                            allSessions.Add(displayItem);
                        }
                        
                        ApplyFilters();
                        StatusText.Text = $"Found {allSessions.Count} sessions";
                    }));
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading sessions: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"LoadSessions error: {ex}");
            }
        }

        private void ApplyFilters()
        {
            try
            {
                var filtered = allSessions.AsEnumerable();
                
                // Event filter
                if (EventFilterCombo.SelectedItem is ComboBoxItem eventItem && 
                    eventItem.Tag is int eventId && eventId != -1)
                {
                    filtered = filtered.Where(s => s.SessionData.EventId == eventId);
                }
                
                // Date filter
                if (DateFilterCombo.SelectedItem is ComboBoxItem dateItem)
                {
                    var dateFilter = dateItem.Content.ToString();
                    var now = DateTime.Now;
                    
                    if (dateFilter == "Today")
                        filtered = filtered.Where(s => s.SessionData.StartTime.Date == now.Date);
                    else if (dateFilter == "Yesterday")
                        filtered = filtered.Where(s => s.SessionData.StartTime.Date == now.Date.AddDays(-1));
                    else if (dateFilter == "This Week")
                        filtered = filtered.Where(s => s.SessionData.StartTime >= now.AddDays(-(int)now.DayOfWeek));
                    else if (dateFilter == "This Month")
                        filtered = filtered.Where(s => s.SessionData.StartTime.Month == now.Month && s.SessionData.StartTime.Year == now.Year);
                }
                
                // Search filter
                if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
                {
                    var searchTerm = SearchTextBox.Text.ToLower();
                    filtered = filtered.Where(s => 
                        s.SessionName.ToLower().Contains(searchTerm) ||
                        s.EventName.ToLower().Contains(searchTerm) ||
                        s.TemplateName.ToLower().Contains(searchTerm));
                }
                
                // Update filtered collection
                filteredSessions.Clear();
                foreach (var session in filtered.OrderByDescending(s => s.SessionData.StartTime))
                {
                    filteredSessions.Add(session);
                }
                
                StatusText.Text = $"Showing {filteredSessions.Count} of {allSessions.Count} sessions";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error filtering sessions: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"ApplyFilters error: {ex}");
            }
        }

        private string FormatSessionTime(DateTime startTime, DateTime? endTime)
        {
            var start = startTime.ToString("MMM dd, yyyy HH:mm");
            if (endTime.HasValue)
            {
                var duration = endTime.Value - startTime;
                return $"{start} ({duration.TotalMinutes:F0} min)";
            }
            return $"{start} (Active)";
        }

        #region Event Handlers

        private void ModalOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close modal when clicking outside the container
            if (e.OriginalSource == ModalOverlay)
            {
                HideModal();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideModal();
        }


        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSessions();
        }

        private void EventFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (allSessions != null)
                ApplyFilters();
        }

        private void DateFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (allSessions != null)
                ApplyFilters();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (allSessions != null)
                ApplyFilters();
        }

        private void SessionCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(78, 78, 82));
            }
        }

        private void SessionCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && !Equals(border.Tag, selectedSession))
            {
                border.Background = new SolidColorBrush(Color.FromRgb(62, 62, 66));
            }
        }

        private void SessionCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is SessionDisplayItem sessionItem)
            {
                // One-click open - immediately load the session
                selectedSession = sessionItem;
                
                // Brief visual feedback
                border.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                
                // Update status
                SelectedSessionText.Text = $"Loading: {sessionItem.EventName}";
                
                // Invoke session selected and close modal
                SessionSelected?.Invoke(sessionItem.SessionData);
                HideModal();
            }
        }
        
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            HideModal();
        }

        #endregion

        #region Helper Methods

        private T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;

                var childOfChild = FindChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }

            return null;
        }

        #endregion
    }

    // Display item for session list
    public class SessionDisplayItem : INotifyPropertyChanged
    {
        private string _sessionName;
        private string _eventName;
        private string _templateName;
        private string _sessionTimeText;
        private string _photoCountText;
        private int _composedImageCount;
        private bool _isCompleted;
        private string _thumbnailPath;

        public PhotoSessionData SessionData { get; set; }

        public string SessionName
        {
            get => _sessionName;
            set { _sessionName = value; OnPropertyChanged(nameof(SessionName)); }
        }

        public string EventName
        {
            get => _eventName;
            set { _eventName = value; OnPropertyChanged(nameof(EventName)); }
        }

        public string TemplateName
        {
            get => _templateName;
            set { _templateName = value; OnPropertyChanged(nameof(TemplateName)); }
        }

        public string SessionTimeText
        {
            get => _sessionTimeText;
            set { _sessionTimeText = value; OnPropertyChanged(nameof(SessionTimeText)); }
        }

        public string PhotoCountText
        {
            get => _photoCountText;
            set { _photoCountText = value; OnPropertyChanged(nameof(PhotoCountText)); }
        }

        public int ComposedImageCount
        {
            get => _composedImageCount;
            set { _composedImageCount = value; OnPropertyChanged(nameof(ComposedImageCount)); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(nameof(IsCompleted)); }
        }

        public string ThumbnailPath
        {
            get => _thumbnailPath;
            set { _thumbnailPath = value; OnPropertyChanged(nameof(ThumbnailPath)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}