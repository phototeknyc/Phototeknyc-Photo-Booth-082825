using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Database;
using Photobooth.Services;

namespace Photobooth.Windows
{
    public partial class EventBrowserWindow : Window, INotifyPropertyChanged
    {
        private readonly EventService eventService;
        private readonly TemplateDatabase templateDatabase;
        private List<EventItemViewModel> allEvents;
        private ObservableCollection<EventItemViewModel> filteredEvents;
        
        public EventData SelectedEvent { get; private set; }

        public EventBrowserWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            // Ensure fullscreen mode
            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            
            eventService = new EventService();
            templateDatabase = new TemplateDatabase();
            filteredEvents = new ObservableCollection<EventItemViewModel>();
            
            EventsItemsControl.ItemsSource = filteredEvents;
            
            Loaded += EventBrowserWindow_Loaded;
        }

        private async void EventBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEvents();
        }

        private async System.Threading.Tasks.Task LoadEvents()
        {
            try
            {
                StatusText.Text = "Loading events...";
                
                var events = eventService.GetAllEvents();
                allEvents = new List<EventItemViewModel>();
                
                foreach (var eventData in events)
                {
                    var viewModel = new EventItemViewModel(eventData);
                    
                    // Get template count for this event
                    var eventTemplates = eventService.GetEventTemplates(eventData.Id);
                    viewModel.TemplateCount = eventTemplates.Count;
                    
                    allEvents.Add(viewModel);
                }
                
                FilterAndSortEvents();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading events: {ex.Message}";
                ShowEmptyState();
            }
        }

        private void FilterAndSortEvents()
        {
            var searchText = SearchBox.Text?.ToLower() ?? "";
            
            // Get filter status based on RadioButton selection
            string filterType = "All";
            if (UpcomingFilter != null && UpcomingFilter.IsChecked == true)
                filterType = "Upcoming";
            else if (PastFilter != null && PastFilter.IsChecked == true)
                filterType = "Past";
            
            var filtered = allEvents.Where(e => 
                string.IsNullOrEmpty(searchText) ||
                (e.Event.Name?.ToLower().Contains(searchText) == true) ||
                (e.Event.Description?.ToLower().Contains(searchText) == true) ||
                (e.Event.EventType?.ToLower().Contains(searchText) == true) ||
                (e.Event.Location?.ToLower().Contains(searchText) == true) ||
                (e.Event.HostName?.ToLower().Contains(searchText) == true)
            );
            
            // Apply date filter
            var today = DateTime.Today;
            switch (filterType)
            {
                case "Upcoming":
                    filtered = filtered.Where(e => e.Event.EventDate >= today);
                    break;
                case "Past":
                    filtered = filtered.Where(e => e.Event.EventDate < today);
                    break;
            }
            
            // Sort by event date by default (newest first)
            filtered = filtered.OrderByDescending(e => e.Event.EventDate ?? DateTime.MinValue);
            
            filteredEvents.Clear();
            foreach (var eventItem in filtered)
            {
                filteredEvents.Add(eventItem);
            }
            
            // Update event count
            UpdateEventCount();
        }

        private void UpdateStatus()
        {
            if (allEvents?.Count == 0)
            {
                StatusText.Text = "No events found";
            }
            else if (filteredEvents.Count != allEvents.Count)
            {
                StatusText.Text = $"Showing {filteredEvents.Count} of {allEvents.Count} events";
            }
            else
            {
                StatusText.Text = $"{allEvents.Count} event(s) loaded";
            }
        }
        
        private void UpdateEventCount()
        {
            if (EventCountText != null)
            {
                EventCountText.Text = $"{filteredEvents.Count} event{(filteredEvents.Count != 1 ? "s" : "")}";
            }
        }

        private void ShowEmptyState()
        {
            // Empty state is handled by having no items in the ItemsControl
        }

        private void HideEmptyState()
        {
            // Empty state is handled by having no items in the ItemsControl
        }

        // Event Handlers
        private async void RefreshEvents_Click(object sender, RoutedEventArgs e)
        {
            await LoadEvents();
        }

        private async void CreateEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var createDialog = new EventCreateEditDialog();
                createDialog.Owner = this;
                
                if (createDialog.ShowDialog() == true)
                {
                    await LoadEvents();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAndSortEvents();
            UpdateStatus();
        }

        private void FilterChanged(object sender, RoutedEventArgs e)
        {
            if (allEvents != null)
            {
                FilterAndSortEvents();
            }
        }

        private void EventCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) // Double click
            {
                var border = sender as Border;
                var eventItem = border?.Tag as EventItemViewModel;
                if (eventItem != null)
                {
                    LoadSelectedEvent(eventItem);
                }
            }
        }

        private void LoadEvent_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var eventItem = button?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                LoadSelectedEvent(eventItem);
            }
        }
        
        private async void CreateGallery_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var eventItem = menuItem?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                await CreateGalleryForEvent(eventItem);
            }
        }
        
        private async void CreateGalleryButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var eventItem = button?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                await CreateGalleryForEvent(eventItem);
            }
        }
        
        private void CopyGalleryLinkButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var eventItem = button?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                GalleryInfoHelper.CopyGalleryInfo(eventItem.Event.Id);
            }
        }
        
        private void CopyGalleryLink_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var eventItem = menuItem?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                GalleryInfoHelper.CopyGalleryInfo(eventItem.Event.Id);
            }
        }
        
        private async Task CreateGalleryForEvent(EventItemViewModel eventItem)
        {
            try
            {
                StatusText.Text = $"Creating gallery for {eventItem.Event.Name}...";
                
                // Get the cloud share service
                var shareService = CloudShareProvider.GetShareService();
                
                if (shareService is CloudShareServiceRuntime runtimeService)
                {
                    // Create the event gallery (passwordless by default)
                    var (galleryUrl, password) = await runtimeService.CreateEventGalleryAsync(
                        eventItem.Event.Name, 
                        eventItem.Event.Id, 
                        usePassword: false);
                    
                    if (!string.IsNullOrEmpty(galleryUrl))
                    {
                        // Save gallery info to database
                        var eventData = eventItem.Event;
                        eventData.GalleryUrl = galleryUrl;
                        eventData.GalleryPassword = password;
                        
                        var database = new TemplateDatabase();
                        database.UpdateEvent(eventItem.Event.Id, eventData);
                        
                        StatusText.Text = $"Gallery created for {eventItem.Event.Name}";
                        
                        // Refresh the events list to show the new gallery indicator
                        await LoadEvents();
                        
                        // Show the gallery info
                        MessageBox.Show($"Gallery created successfully!\n\nURL: {galleryUrl}\n\nThe URL has been copied to your clipboard.", 
                            "Gallery Created", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                        // Copy to clipboard
                        Clipboard.SetText(galleryUrl);
                    }
                    else
                    {
                        StatusText.Text = "Failed to create gallery";
                        MessageBox.Show("Failed to create gallery. Please check your AWS settings.", 
                            "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    StatusText.Text = "Cloud service not available";
                    MessageBox.Show("Cloud share service is not configured.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error creating gallery";
                MessageBox.Show($"Error creating gallery: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void AddGalleryPassword_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var eventItem = menuItem?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                GalleryInfoHelper.AddPasswordToGallery(eventItem.Event.Id);
                // Refresh after adding password
                _ = LoadEvents();
            }
        }

        private async void EditEvent_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var eventItem = button?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                try
                {
                    var editDialog = new EventCreateEditDialog(eventItem.Event);
                    editDialog.Owner = this;
                    
                    if (editDialog.ShowDialog() == true)
                    {
                        await LoadEvents();
                        StatusText.Text = "Event updated successfully";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to edit event: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void CopyEvent_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var eventItem = button?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                try
                {
                    var originalEvent = eventItem.Event;
                    var copyName = $"{originalEvent.Name} - Copy";
                    
                    var newEventId = eventService.CreateEvent(
                        copyName,
                        originalEvent.Description,
                        originalEvent.EventType,
                        originalEvent.Location,
                        originalEvent.EventDate,
                        originalEvent.StartTime,
                        originalEvent.EndTime,
                        originalEvent.HostName,
                        originalEvent.ContactEmail,
                        originalEvent.ContactPhone);

                    if (newEventId > 0)
                    {
                        // Copy all template assignments
                        var originalTemplates = eventService.GetEventTemplates(originalEvent.Id);
                        foreach (var template in originalTemplates)
                        {
                            eventService.AssignTemplateToEvent(newEventId, template.Id, false);
                        }
                        
                        await LoadEvents();
                        StatusText.Text = "Event copied successfully";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to copy event: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteEvent_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var eventItem = button?.Tag as EventItemViewModel;
            if (eventItem != null)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the event '{eventItem.Event.Name}'?\n\nThis will also remove all template assignments for this event.\n\nThis action cannot be undone.",
                    "Delete Event",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        eventService.DeleteEvent(eventItem.Event.Id);
                        await LoadEvents();
                        StatusText.Text = "Event deleted successfully";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete event: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void LoadSelectedEvent(EventItemViewModel eventItem)
        {
            SelectedEvent = eventItem.Event;
            
            // Update the event's modified date to track it as recently used
            if (SelectedEvent != null)
            {
                SelectedEvent.ModifiedDate = DateTime.Now;
                var database = new TemplateDatabase();
                database.UpdateEvent(SelectedEvent.Id, SelectedEvent);
            }
            
            this.DialogResult = true;
            Close();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        // Toolbar event handlers
        private async void ImportEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog()
                {
                    Title = "Import Event",
                    Filter = "Event Package (*.zip)|*.zip|Event JSON (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".zip"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusText.Text = "Importing event...";
                    // TODO: Implement event import logic
                    await LoadEvents();
                    StatusText.Text = "Event import completed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportEvent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (allEvents == null || allEvents.Count == 0)
                {
                    MessageBox.Show("No events to export.", "Information", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog()
                {
                    Title = "Export Event",
                    Filter = "Event Package (*.zip)|*.zip|Event JSON (*.json)|*.json",
                    DefaultExt = ".zip"
                };

                if (dialog.ShowDialog() == true)
                {
                    StatusText.Text = "Exporting events...";
                    // TODO: Implement event export logic
                    StatusText.Text = "Event export completed";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EventTemplates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open template browser focused on event templates
                var templateBrowser = new TemplateBrowserWindow();
                templateBrowser.Owner = this;
                templateBrowser.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open template browser: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AssignTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBox.Show("To assign a template to an event, please select an event and use the 'Assign Current Template' button.", 
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to assign template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void HostGallery_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Generating host gallery dashboard...";
                
                var hostGalleryService = new HostGalleryService();
                var dashboardHtml = await hostGalleryService.GenerateMasterGalleryHtml();
                
                if (!string.IsNullOrEmpty(dashboardHtml))
                {
                    // Save to temp file and open in browser
                    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "host_gallery_dashboard.html");
                    System.IO.File.WriteAllText(tempPath, dashboardHtml);
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tempPath,
                        UseShellExecute = true
                    });
                    
                    StatusText.Text = "Host gallery dashboard opened in browser";
                }
                else
                {
                    StatusText.Text = "No gallery data available";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate host gallery: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to generate host gallery";
            }
        }
        
        private void EventSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MessageBox.Show("Event settings functionality coming soon!", "Information", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ViewModel for event items
    public class EventItemViewModel : INotifyPropertyChanged
    {
        public EventData Event { get; }

        public string Name => Event.Name ?? "Untitled Event";
        public string Description => Event.Description ?? "No description";
        public string EventType => Event.EventType ?? "Event";
        public string Location => Event.Location ?? "Location TBD";
        public string HostName => Event.HostName ?? "Unknown Host";
        public DateTime CreatedDate => Event.CreatedDate;
        public DateTime? EventDate => Event.EventDate;
        
        public string FormattedDate
        {
            get
            {
                if (EventDate.HasValue)
                {
                    var date = EventDate.Value;
                    var today = DateTime.Today;
                    
                    if (date.Date == today)
                        return "Today";
                    else if (date.Date == today.AddDays(1))
                        return "Tomorrow";
                    else if (date.Date == today.AddDays(-1))
                        return "Yesterday";
                    else if ((date - today).TotalDays > 0 && (date - today).TotalDays < 7)
                        return date.ToString("dddd"); // Day name for upcoming week
                    else
                        return date.ToString("MMM d, yyyy");
                }
                return "Date TBD";
            }
        }
        
        // Gallery info
        public string GalleryUrl => Event.GalleryUrl;
        public string GalleryPassword => Event.GalleryPassword;
        public bool HasGallery => !string.IsNullOrEmpty(GalleryUrl);
        public bool IsPasswordProtected => !string.IsNullOrEmpty(GalleryPassword);
        
        private int _templateCount;
        public int TemplateCount
        {
            get => _templateCount;
            set
            {
                _templateCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TemplateCountText));
            }
        }
        
        public string TemplateCountText => $"{TemplateCount} template(s) assigned";

        public EventItemViewModel(EventData eventData)
        {
            Event = eventData;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}