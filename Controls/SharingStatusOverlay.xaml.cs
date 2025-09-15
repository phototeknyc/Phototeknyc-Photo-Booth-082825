using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Photobooth.Services;
using Newtonsoft.Json;

namespace Photobooth.Controls
{
    /// <summary>
    /// Interaction logic for SharingStatusOverlay.xaml
    /// Shows real-time status of all upload and sharing queues
    /// </summary>
    public partial class SharingStatusOverlay : UserControl
    {
        private readonly DispatcherTimer _refreshTimer;
        private readonly ObservableCollection<UploadQueueItem> _uploadItems;
        private readonly ObservableCollection<SMSQueueItem> _smsItems;
        private bool _isPaused = false;
        
        public SharingStatusOverlay()
        {
            InitializeComponent();
            
            _uploadItems = new ObservableCollection<UploadQueueItem>();
            _smsItems = new ObservableCollection<SMSQueueItem>();
            
            UploadItemsControl.ItemsSource = _uploadItems;
            SMSItemsControl.ItemsSource = _smsItems;
            
            // Setup refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            
            // Subscribe to queue events
            SubscribeToQueueEvents();
        }
        
        #region Public Methods
        
        /// <summary>
        /// Show the overlay
        /// </summary>
        public void Show()
        {
            this.Visibility = Visibility.Visible;  // Show the control itself
            MainOverlay.Visibility = Visibility.Visible;
            _refreshTimer.Start();
            RefreshAllQueues();
        }
        
        /// <summary>
        /// Hide the overlay
        /// </summary>
        public void Hide()
        {
            MainOverlay.Visibility = Visibility.Collapsed;
            this.Visibility = Visibility.Collapsed;  // Hide the control itself
            _refreshTimer.Stop();
        }
        
        #endregion
        
        #region Event Handlers
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
        
        private async void ProcessAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessAllButton.IsEnabled = false;
                ProcessingStatusText.Text = "Processing...";
                
                // Process all queues
                var offlineQueue = OfflineQueueService.Instance;
                var photoboothQueue = PhotoboothQueueService.Instance;
                
                await Task.Run(async () =>
                {
                    offlineQueue.ProcessQueue(null);
                    await photoboothQueue.ProcessPendingQueues();
                });
                
                RefreshAllQueues();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing queues: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProcessAllButton.IsEnabled = true;
                UpdateProcessingStatus();
            }
        }
        
        private void ClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Clear all completed items from the queue history?", 
                    "Clear Completed", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    ClearCompletedItems();
                    RefreshAllQueues();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing completed items: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;
            if (PauseButton != null)
            {
                PauseButton.Content = _isPaused ? "Resume Processing" : "Pause Processing";
            }
            if (AutoProcessCheckBox != null)
            {
                AutoProcessCheckBox.IsChecked = !_isPaused;
            }
            
            // TODO: Implement actual pause functionality in queue services
            UpdateProcessingStatus();
        }
        
        private void AutoProcess_Changed(object sender, RoutedEventArgs e)
        {
            _isPaused = !(AutoProcessCheckBox?.IsChecked ?? true);
            if (PauseButton != null)
            {
                PauseButton.Content = _isPaused ? "Resume Processing" : "Pause Processing";
            }
            UpdateProcessingStatus();
        }
        
        private async void RetryUpload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is UploadQueueItem item)
            {
                try
                {
                    btn.IsEnabled = false;
                    
                    // Force retry by clearing failure tracking
                    var offlineQueue = OfflineQueueService.Instance;
                    await Task.Run(() => offlineQueue.ProcessQueue(null));
                    
                    RefreshAllQueues();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error retrying upload: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }
        
        private void DeleteUpload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is UploadQueueItem item)
            {
                var result = MessageBox.Show(
                    $"Delete upload for session {item.SessionId}?", 
                    "Delete Upload", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    DeleteUploadItem(item.Id);
                    RefreshAllQueues();
                }
            }
        }
        
        private void UploadsTab_Click(object sender, RoutedEventArgs e)
        {
            // Show Uploads tab
            UploadsTab.Visibility = Visibility.Visible;
            SMSTab.Visibility = Visibility.Collapsed;
            EmailTab.Visibility = Visibility.Collapsed;
            
            // Update button styles
            UploadsTabButton.Style = FindResource("ActiveTabButtonStyle") as Style;
            SMSTabButton.Style = FindResource("TabButtonStyle") as Style;
            EmailTabButton.Style = FindResource("TabButtonStyle") as Style;
        }
        
        private void SMSTab_Click(object sender, RoutedEventArgs e)
        {
            // Show SMS tab
            UploadsTab.Visibility = Visibility.Collapsed;
            SMSTab.Visibility = Visibility.Visible;
            EmailTab.Visibility = Visibility.Collapsed;
            
            // Update button styles
            UploadsTabButton.Style = FindResource("TabButtonStyle") as Style;
            SMSTabButton.Style = FindResource("ActiveTabButtonStyle") as Style;
            EmailTabButton.Style = FindResource("TabButtonStyle") as Style;
        }
        
        private void EmailTab_Click(object sender, RoutedEventArgs e)
        {
            // Show Email tab
            UploadsTab.Visibility = Visibility.Collapsed;
            SMSTab.Visibility = Visibility.Collapsed;
            EmailTab.Visibility = Visibility.Visible;
            
            // Update button styles
            UploadsTabButton.Style = FindResource("TabButtonStyle") as Style;
            SMSTabButton.Style = FindResource("TabButtonStyle") as Style;
            EmailTabButton.Style = FindResource("ActiveTabButtonStyle") as Style;
        }
        
        private async void RetrySMS_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SMSQueueItem item)
            {
                try
                {
                    btn.IsEnabled = false;
                    
                    // Force retry SMS
                    var queueService = PhotoboothQueueService.Instance;
                    await queueService.ProcessPendingQueues();
                    
                    RefreshAllQueues();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to retry SMS: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }
        
        private async void SendSMS_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SMSQueueItem item)
            {
                try
                {
                    btn.IsEnabled = false;
                    
                    // Force send now
                    var photoboothQueue = PhotoboothQueueService.Instance;
                    await photoboothQueue.ProcessPendingQueues();
                    
                    RefreshAllQueues();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error sending SMS: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }
        
        private void DeleteSMS_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SMSQueueItem item)
            {
                var result = MessageBox.Show(
                    $"Delete SMS to {item.PhoneNumber}?", 
                    "Delete SMS", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    DeleteSMSItem(item.Id);
                    RefreshAllQueues();
                }
            }
        }
        
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshAllQueues();
        }
        
        #endregion
        
        #region Queue Management
        
        private void RefreshAllQueues()
        {
            try
            {
                RefreshUploadQueue();
                RefreshSMSQueue();
                UpdateNetworkStatus();
                UpdateProcessingStatus();
                UpdateSummary();
                
                // LastUpdateText removed in redesign
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing queues: {ex.Message}");
            }
        }
        
        private void RefreshUploadQueue()
        {
            _uploadItems.Clear();
            
            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "offline_queue.db");
            
            if (!System.IO.File.Exists(dbPath)) return;
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand(
                        "SELECT * FROM upload_queue ORDER BY created_at DESC", conn);
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = new UploadQueueItem
                            {
                                Id = reader.GetInt32(0),
                                SessionId = reader.GetString(1),
                                PhotoPaths = JsonConvert.DeserializeObject<List<string>>(reader.GetString(2)),
                                Status = reader.IsDBNull(7) ? "pending" : reader.GetString(7),
                                RetryCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                                CreatedAt = reader.GetDateTime(3)
                            };
                            
                            // Set display properties
                            item.PhotoCount = $"{item.PhotoPaths?.Count ?? 0} photos";
                            item.UpdateStatusDisplay();
                            
                            _uploadItems.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading upload queue: {ex.Message}");
            }
        }
        
        private void RefreshSMSQueue()
        {
            _smsItems.Clear();
            
            // Check PhotoboothQueueService database
            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "photobooth_queue.db");
            
            if (!System.IO.File.Exists(dbPath)) return;
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand(
                        "SELECT * FROM sms_queue ORDER BY requested_at DESC", conn);
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = new SMSQueueItem
                            {
                                Id = reader.GetInt32(0),                    // Column 0: id
                                SessionId = reader.GetString(1),            // Column 1: session_id  
                                PhoneNumber = reader.GetString(2),          // Column 2: phone_number
                                Status = reader.IsDBNull(7) ? "pending" : reader.GetString(7),    // Column 7: status
                                RetryCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),        // Column 8: retry_count
                                CreatedAt = reader.GetDateTime(4)           // Column 4: requested_at
                            };
                            
                            item.UpdateStatusDisplay();
                            _smsItems.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading SMS queue: {ex.Message}");
            }
        }
        
        private void UpdateNetworkStatus()
        {
            if (NetworkStatusText == null) return;
            
            bool isOnline = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            
            // NetworkStatusIndicator removed in redesign
            NetworkStatusText.Text = isOnline ? "Network: Online" : "Network: Offline";
            NetworkStatusText.Foreground = new SolidColorBrush(isOnline ? Colors.LimeGreen : Colors.Red);
        }
        
        private void UpdateProcessingStatus()
        {
            if (ProcessingStatusText == null) return;
            
            var offlineQueue = OfflineQueueService.Instance;
            
            if (_isPaused)
            {
                ProcessingStatusText.Text = "Processing: Paused";
                ProcessingStatusText.Foreground = new SolidColorBrush(Colors.Orange);
            }
            else if (offlineQueue.IsUploading)
            {
                ProcessingStatusText.Text = $"Processing: Uploading... {offlineQueue.UploadProgress:P0}";
                ProcessingStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            }
            else
            {
                ProcessingStatusText.Text = "Processing: Idle";
                ProcessingStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }
        
        private void UpdateSummary()
        {
            var uploadsPending = _uploadItems.Count(i => i.Status == "pending");
            var smsPending = _smsItems.Count(i => i.Status == "pending");
            
            // StatusSummaryText removed in redesign - using tab count displays instead
        }
        
        #endregion
        
        #region Database Operations
        
        private void DeleteUploadItem(int id)
        {
            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "offline_queue.db");
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand("DELETE FROM upload_queue WHERE id = @id", conn);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting upload item: {ex.Message}");
            }
        }
        
        private void DeleteSMSItem(int id)
        {
            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "photobooth_queue.db");
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand("DELETE FROM sms_queue WHERE id = @id", conn);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting SMS item: {ex.Message}");
            }
        }
        
        private void ClearCompletedItems()
        {
            // Clear completed uploads
            var uploadDbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "offline_queue.db");
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={uploadDbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand("DELETE FROM upload_queue WHERE status = 'completed'", conn);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
            
            // Clear completed SMS
            var smsDbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "photobooth_queue.db");
            
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={smsDbPath}"))
                {
                    conn.Open();
                    var cmd = new SQLiteCommand("DELETE FROM sms_queue WHERE status = 'sent'", conn);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }
        
        #endregion
        
        #region Event Subscriptions
        
        private void SubscribeToQueueEvents()
        {
            try
            {
                var offlineQueue = OfflineQueueService.Instance;
                offlineQueue.UploadProgressChanged += OnUploadProgressChanged;
                offlineQueue.OnUploadCompleted += OnUploadCompleted;
                offlineQueue.OnSMSSent += OnSMSSent;
                
                var photoboothQueue = PhotoboothQueueService.Instance;
                photoboothQueue.OnSMSProcessed += OnSMSProcessed;
                photoboothQueue.OnQueueStatusChanged += OnQueueStatusChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error subscribing to events: {ex.Message}");
            }
        }
        
        private void OnUploadProgressChanged(double progress, string name)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateProcessingStatus();
            });
        }
        
        private void OnUploadCompleted(string sessionId, string url)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAllQueues();
            });
        }
        
        private void OnSMSSent(string phoneNumber)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAllQueues();
            });
        }
        
        private void OnSMSProcessed(string phoneNumber)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAllQueues();
            });
        }
        
        private void OnQueueStatusChanged(PhotoboothQueueStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshAllQueues();
            });
        }
        
        #endregion
    }
    
    #region View Models
    
    public class UploadQueueItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string SessionId { get; set; }
        public List<string> PhotoPaths { get; set; }
        public string PhotoCount { get; set; }
        public string Status { get; set; }
        public int RetryCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public double Progress { get; set; }
        
        // Display properties
        public string StatusColor { get; set; }
        public string StatusIcon { get; set; }
        public string StatusTextColor { get; set; }
        public Visibility ProgressVisibility { get; set; }
        public Visibility RetryVisibility { get; set; }
        
        public void UpdateStatusDisplay()
        {
            switch (Status?.ToLower())
            {
                case "completed":
                    StatusColor = "#4CAF50";
                    StatusIcon = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z";
                    StatusTextColor = "#4CAF50";
                    Status = "✓ Uploaded successfully";
                    ProgressVisibility = Visibility.Collapsed;
                    RetryVisibility = Visibility.Collapsed;
                    break;
                    
                case "uploading":
                    StatusColor = "#2196F3";
                    StatusIcon = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";
                    StatusTextColor = "#2196F3";
                    Status = "Uploading...";
                    ProgressVisibility = Visibility.Visible;
                    RetryVisibility = Visibility.Collapsed;
                    break;
                    
                case "failed":
                    StatusColor = "#F44336";
                    StatusIcon = "M12,2C17.53,2 22,6.47 22,12C22,17.53 17.53,22 12,22C6.47,22 2,17.53 2,12C2,6.47 6.47,2 12,2Z";
                    StatusTextColor = "#F44336";
                    Status = $"Failed (Retry {RetryCount})";
                    ProgressVisibility = Visibility.Collapsed;
                    RetryVisibility = Visibility.Visible;
                    break;
                    
                default: // pending
                    StatusColor = "#FFC107";
                    StatusIcon = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";
                    StatusTextColor = "#FFC107";
                    Status = $"Pending (Retry {RetryCount})";
                    ProgressVisibility = Visibility.Collapsed;
                    RetryVisibility = RetryCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
            
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusTextColor));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(ProgressVisibility));
            OnPropertyChanged(nameof(RetryVisibility));
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    
    public class SMSQueueItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string SessionId { get; set; }
        public string PhoneNumber { get; set; }
        public string Status { get; set; }
        public int RetryCount { get; set; }
        public DateTime CreatedAt { get; set; }
        
        // Display properties
        public string StatusColor { get; set; }
        public string StatusIcon { get; set; }
        public string StatusTextColor { get; set; }
        public Visibility SendVisibility { get; set; }
        
        public void UpdateStatusDisplay()
        {
            switch (Status?.ToLower())
            {
                case "sent":
                    StatusColor = "#4CAF50";
                    StatusIcon = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z";
                    StatusTextColor = "#4CAF50";
                    Status = "✓ Sent successfully";
                    SendVisibility = Visibility.Collapsed;
                    break;
                    
                case "failed":
                    StatusColor = "#F44336";
                    StatusIcon = "M12,2C17.53,2 22,6.47 22,12C22,17.53 17.53,22 12,22C6.47,22 2,17.53 2,12C2,6.47 6.47,2 12,2Z";
                    StatusTextColor = "#F44336";
                    Status = $"Failed (Retry {RetryCount})";
                    SendVisibility = Visibility.Visible;
                    break;
                    
                default: // pending
                    StatusColor = "#FFC107";
                    StatusIcon = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";
                    StatusTextColor = "#FFC107";
                    Status = "Waiting for URL";
                    SendVisibility = Visibility.Collapsed;
                    break;
            }
            
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusTextColor));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(SendVisibility));
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    
    #endregion
}