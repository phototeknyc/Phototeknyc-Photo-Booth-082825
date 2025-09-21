using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Photobooth.Database;
using Photobooth.Models;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to handle event selection business logic with search and preview capabilities
    /// </summary>
    public class EventSelectionService : INotifyPropertyChanged
    {
        #region Singleton
        private static EventSelectionService _instance;
        private static readonly object _lock = new object();
        
        public static EventSelectionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new EventSelectionService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion
        
        #region Properties
        private readonly EventService _eventService;
        private readonly TemplateDatabase _templateDatabase;
        private ObservableCollection<EventData> _allEvents;
        private ObservableCollection<EventData> _filteredEvents;
        private string _searchText;
        private EventData _selectedEvent;
        private TemplateData _previewTemplate;
        private BitmapImage _templatePreviewImage;
        private System.Timers.Timer _eventExpirationTimer;
        private DateTime _eventSelectionTime;
        
        public ObservableCollection<EventData> FilteredEvents
        {
            get => _filteredEvents;
            private set
            {
                _filteredEvents = value;
                OnPropertyChanged();
            }
        }
        
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterEvents();
            }
        }
        
        public EventData SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                _selectedEvent = value;
                OnPropertyChanged();
                if (value != null)
                {
                    LoadTemplatePreview(value);
                    // Save the selected event ID for persistence
                    Properties.Settings.Default.SelectedEventId = value.Id;
                    Properties.Settings.Default.Save();
                    Log.Debug($"Saved selected event ID: {value.Id} - {value.Name}");
                }
                else
                {
                    // Clear the saved event ID
                    Properties.Settings.Default.SelectedEventId = 0;
                    Properties.Settings.Default.Save();
                }
            }
        }
        
        public TemplateData PreviewTemplate
        {
            get => _previewTemplate;
            private set
            {
                _previewTemplate = value;
                OnPropertyChanged();
            }
        }
        
        public BitmapImage TemplatePreviewImage
        {
            get => _templatePreviewImage;
            private set
            {
                _templatePreviewImage = value;
                OnPropertyChanged();
            }
        }
        #endregion
        
        #region Events
        public event EventHandler<EventData> EventSelected;
        public event EventHandler EventExpired;
        public event EventHandler SearchCleared;
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion
        
        private EventSelectionService()
        {
            _eventService = new EventService();
            _templateDatabase = new TemplateDatabase();
            _allEvents = new ObservableCollection<EventData>();
            _filteredEvents = new ObservableCollection<EventData>();

            // Initialize event expiration timer (5 hours = 18000000 milliseconds)
            _eventExpirationTimer = new System.Timers.Timer(5 * 60 * 60 * 1000);
            _eventExpirationTimer.Elapsed += OnEventExpired;
            _eventExpirationTimer.AutoReset = false;
        }
        
        /// <summary>
        /// Load all available events
        /// </summary>
        public void LoadEvents()
        {
            try
            {
                Log.Debug("EventSelectionService: Loading events");
                
                var events = _eventService.GetAllEvents() ?? new List<EventData>();
                
                // Load template preview for each event
                foreach (var eventData in events)
                {
                    LoadEventTemplatePreview(eventData);
                }
                
                _allEvents = new ObservableCollection<EventData>(events);
                FilteredEvents = new ObservableCollection<EventData>(_allEvents);
                
                Log.Debug($"EventSelectionService: Loaded {_allEvents.Count} events");
                
                // Select first event if available for preview
                if (_allEvents.Any() && SelectedEvent == null)
                {
                    SelectedEvent = _allEvents.First();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load events: {ex.Message}");
                _allEvents = new ObservableCollection<EventData>();
                FilteredEvents = new ObservableCollection<EventData>();
            }
        }
        
        /// <summary>
        /// Load template preview for a specific event
        /// </summary>
        private void LoadEventTemplatePreview(EventData eventData)
        {
            try
            {
                if (eventData == null) return;
                
                // Get templates for this event
                var templates = _eventService.GetEventTemplates(eventData.Id);
                
                if (templates != null && templates.Any())
                {
                    // Get first active template
                    var template = templates.FirstOrDefault(t => t.IsActive) ?? templates.First();
                    
                    // Try to load preview image
                    BitmapImage previewImage = null;
                    
                    if (!string.IsNullOrEmpty(template.ThumbnailImagePath) && File.Exists(template.ThumbnailImagePath))
                    {
                        previewImage = LoadImageFromFile(template.ThumbnailImagePath);
                    }
                    else if (!string.IsNullOrEmpty(template.BackgroundImagePath) && File.Exists(template.BackgroundImagePath))
                    {
                        previewImage = LoadImageFromFile(template.BackgroundImagePath);
                    }
                    
                    // Store the preview image in a dynamic property (we'll extend EventData)
                    // For now, we'll use a dictionary to map event to preview
                    if (previewImage != null)
                    {
                        _eventPreviews[eventData.Id] = previewImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load template preview for event {eventData.Name}: {ex.Message}");
            }
        }
        
        private Dictionary<int, BitmapImage> _eventPreviews = new Dictionary<int, BitmapImage>();
        
        /// <summary>
        /// Get template preview for an event
        /// </summary>
        public BitmapImage GetEventTemplatePreview(int eventId)
        {
            return _eventPreviews.ContainsKey(eventId) ? _eventPreviews[eventId] : null;
        }
        
        /// <summary>
        /// Filter events based on search text
        /// </summary>
        private void FilterEvents()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                {
                    FilteredEvents = new ObservableCollection<EventData>(_allEvents);
                    SearchCleared?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    var searchLower = SearchText.ToLower();
                    var filtered = _allEvents.Where(e => 
                        e.Name.ToLower().Contains(searchLower) || 
                        (e.Description?.ToLower().Contains(searchLower) ?? false)
                    ).ToList();
                    
                    FilteredEvents = new ObservableCollection<EventData>(filtered);
                }
                
                Log.Debug($"EventSelectionService: Filtered to {FilteredEvents.Count} events with search '{SearchText}'");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to filter events: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Load template preview for selected event
        /// </summary>
        private void LoadTemplatePreview(EventData eventData)
        {
            try
            {
                if (eventData == null)
                {
                    PreviewTemplate = null;
                    TemplatePreviewImage = null;
                    return;
                }
                
                Log.Debug($"EventSelectionService: Loading template preview for event {eventData.Name}");
                
                // Get templates for this event
                var templates = _eventService.GetEventTemplates(eventData.Id);
                
                if (templates != null && templates.Any())
                {
                    // Get first active template
                    PreviewTemplate = templates.FirstOrDefault(t => t.IsActive) ?? templates.First();
                    
                    // Load template preview image
                    if (!string.IsNullOrEmpty(PreviewTemplate.ThumbnailImagePath) && File.Exists(PreviewTemplate.ThumbnailImagePath))
                    {
                        LoadPreviewImage(PreviewTemplate.ThumbnailImagePath);
                    }
                    else if (!string.IsNullOrEmpty(PreviewTemplate.BackgroundImagePath) && File.Exists(PreviewTemplate.BackgroundImagePath))
                    {
                        LoadPreviewImage(PreviewTemplate.BackgroundImagePath);
                    }
                    else
                    {
                        // Try to generate a preview from the template data
                        GenerateTemplatePreview(PreviewTemplate);
                    }
                    
                    Log.Debug($"EventSelectionService: Loaded preview template '{PreviewTemplate.Name}' for event");
                }
                else
                {
                    PreviewTemplate = null;
                    TemplatePreviewImage = null;
                    Log.Debug("EventSelectionService: No templates found for event");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load template preview: {ex.Message}");
                PreviewTemplate = null;
                TemplatePreviewImage = null;
            }
        }
        
        /// <summary>
        /// Load preview image from file
        /// </summary>
        private void LoadPreviewImage(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 400; // Limit size for performance
                bitmap.EndInit();
                bitmap.Freeze();
                
                TemplatePreviewImage = bitmap;
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load preview image: {ex.Message}");
                TemplatePreviewImage = null;
            }
        }
        
        /// <summary>
        /// Load image from file path
        /// </summary>
        private BitmapImage LoadImageFromFile(string imagePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 400; // Limit size for performance
                bitmap.EndInit();
                bitmap.Freeze();
                
                return bitmap;
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load image from file: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Generate a preview from template data
        /// </summary>
        private void GenerateTemplatePreview(TemplateData template)
        {
            try
            {
                // Try to use the background image as preview if available
                if (!string.IsNullOrEmpty(template.BackgroundImagePath) && File.Exists(template.BackgroundImagePath))
                {
                    LoadPreviewImage(template.BackgroundImagePath);
                    return;
                }
                
                // Look for composed images in common session folders
                var sessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Photobooth Sessions");
                if (Directory.Exists(sessionPath))
                {
                    // Look for any recent composed image
                    var composedFiles = Directory.GetFiles(sessionPath, "*composed*.jpg", SearchOption.AllDirectories)
                        .Union(Directory.GetFiles(sessionPath, "*composed*.png", SearchOption.AllDirectories))
                        .Union(Directory.GetFiles(sessionPath, "*final*.jpg", SearchOption.AllDirectories))
                        .Union(Directory.GetFiles(sessionPath, "*final*.png", SearchOption.AllDirectories))
                        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                        .Take(1)
                        .FirstOrDefault();
                    
                    if (!string.IsNullOrEmpty(composedFiles) && File.Exists(composedFiles))
                    {
                        LoadPreviewImage(composedFiles);
                        return;
                    }
                }
                
                // If no preview found, clear the image
                TemplatePreviewImage = null;
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to generate template preview: {ex.Message}");
                TemplatePreviewImage = null;
            }
        }
        
        /// <summary>
        /// Select an event and notify subscribers
        /// </summary>
        public async void SelectEvent(EventData eventData)
        {
            if (eventData == null) return;

            Log.Debug($"EventSelectionService: Selecting event '{eventData.Name}'");
            SelectedEvent = eventData;

            // Load event backgrounds
            try
            {
                await EventBackgroundService.Instance.LoadEventBackgroundsAsync(eventData);
                Log.Debug($"EventSelectionService: Loaded event backgrounds for '{eventData.Name}'");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to load event backgrounds: {ex.Message}");
            }

            // Start the 5-hour expiration timer
            StartEventExpirationTimer();

            EventSelected?.Invoke(this, eventData);
        }

        /// <summary>
        /// Start timer for already selected event (used by UI)
        /// </summary>
        public void StartTimerForCurrentEvent()
        {
            if (SelectedEvent != null)
            {
                Log.Debug($"EventSelectionService: Starting timer for current event '{SelectedEvent.Name}'");
                StartEventExpirationTimer();

                // Save the selection time to settings for persistence
                SaveEventSelectionTime();
            }
        }

        /// <summary>
        /// Save event selection time to settings
        /// </summary>
        private void SaveEventSelectionTime()
        {
            try
            {
                Properties.Settings.Default.EventSelectionTime = _eventSelectionTime;
                Properties.Settings.Default.Save();
                Log.Debug($"EventSelectionService: Saved event selection time: {_eventSelectionTime}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to save event selection time: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if saved event has expired and restore if still valid
        /// </summary>
        public void CheckAndRestoreSavedEvent()
        {
            try
            {
                var savedEventId = Properties.Settings.Default.SelectedEventId;
                var savedTime = Properties.Settings.Default.EventSelectionTime;

                if (savedEventId > 0 && savedTime != DateTime.MinValue)
                {
                    var elapsed = DateTime.Now - savedTime;
                    var expirationTime = TimeSpan.FromHours(5);

                    if (elapsed < expirationTime)
                    {
                        // Event is still valid, restore it
                        Log.Debug($"EventSelectionService: Restoring saved event {savedEventId}, selected {elapsed.TotalHours:F1} hours ago");

                        // Load the event from database
                        var eventData = _eventService.GetEvent(savedEventId);
                        if (eventData != null)
                        {
                            SelectedEvent = eventData;
                            _eventSelectionTime = savedTime;

                            // Load event backgrounds
                            try
                            {
                                Task.Run(async () => await EventBackgroundService.Instance.LoadEventBackgroundsAsync(eventData));
                                Log.Debug($"EventSelectionService: Loading event backgrounds for restored event '{eventData.Name}'");
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"EventSelectionService: Failed to load event backgrounds: {ex.Message}");
                            }

                            // Restart timer with remaining time
                            var remainingTime = expirationTime - elapsed;
                            RestartTimerWithRemainingTime(remainingTime);

                            Log.Debug($"EventSelectionService: Event restored successfully, {remainingTime.TotalHours:F1} hours remaining");
                        }
                        else
                        {
                            Log.Debug($"EventSelectionService: Saved event {savedEventId} not found in database");
                            ClearSavedEventSelection();
                        }
                    }
                    else
                    {
                        // Event has expired
                        Log.Debug($"EventSelectionService: Saved event has expired (selected {elapsed.TotalHours:F1} hours ago)");
                        ClearSavedEventSelection();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to restore saved event: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear saved event selection from settings
        /// </summary>
        private void ClearSavedEventSelection()
        {
            Properties.Settings.Default.SelectedEventId = 0;
            Properties.Settings.Default.EventSelectionTime = DateTime.MinValue;
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Restart timer with specific remaining time
        /// </summary>
        private void RestartTimerWithRemainingTime(TimeSpan remainingTime)
        {
            try
            {
                StopEventExpirationTimer();

                // Set timer interval to remaining time
                _eventExpirationTimer.Interval = remainingTime.TotalMilliseconds;
                _eventExpirationTimer.Start();

                Log.Debug($"EventSelectionService: Timer restarted with {remainingTime.TotalMinutes:F0} minutes remaining");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to restart timer: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clear search and reset filter
        /// </summary>
        public void ClearSearch()
        {
            SearchText = string.Empty;
        }
        
        /// <summary>
        /// Reset service state
        /// </summary>
        public void Reset()
        {
            SearchText = string.Empty;
            SelectedEvent = null;
            PreviewTemplate = null;
            TemplatePreviewImage = null;
            _allEvents.Clear();
            FilteredEvents.Clear();
            StopEventExpirationTimer();
        }

        /// <summary>
        /// Start the 5-hour event expiration timer
        /// </summary>
        private void StartEventExpirationTimer()
        {
            try
            {
                StopEventExpirationTimer();
                _eventSelectionTime = DateTime.Now;
                _eventExpirationTimer.Start();
                Log.Debug($"EventSelectionService: Started 5-hour expiration timer at {_eventSelectionTime}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to start expiration timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the event expiration timer
        /// </summary>
        private void StopEventExpirationTimer()
        {
            if (_eventExpirationTimer != null)
            {
                _eventExpirationTimer.Stop();
                Log.Debug("EventSelectionService: Stopped expiration timer");
            }
        }

        /// <summary>
        /// Handle event expiration after 5 hours
        /// </summary>
        private void OnEventExpired(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                Log.Debug($"EventSelectionService: Event expired after 5 hours. Event was '{SelectedEvent?.Name}', selected at {_eventSelectionTime}");

                // Stop the timer first to prevent any re-triggering
                StopEventExpirationTimer();

                // Clear the current event
                SelectedEvent = null;
                PreviewTemplate = null;
                TemplatePreviewImage = null;

                // Clear PhotoboothService static event
                PhotoboothService.CurrentEvent = null;
                PhotoboothService.CurrentTemplate = null;

                // Clear saved settings
                ClearSavedEventSelection();

                // Notify subscribers that the event has expired
                EventExpired?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Error handling event expiration: {ex.Message}");
            }
        }

        /// <summary>
        /// Get remaining time for current event (if any)
        /// </summary>
        public TimeSpan? GetRemainingEventTime()
        {
            if (SelectedEvent == null || !_eventExpirationTimer.Enabled)
                return null;

            var elapsed = DateTime.Now - _eventSelectionTime;
            var remaining = TimeSpan.FromHours(5) - elapsed;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        
        /// <summary>
        /// Duplicate an event with all its templates
        /// </summary>
        public void DuplicateEvent(EventData sourceEvent, string newEventName)
        {
            try
            {
                Log.Debug($"EventSelectionService: Duplicating event '{sourceEvent.Name}' as '{newEventName}'");

                // Create new event
                int newEventId = _eventService.CreateEvent(newEventName, sourceEvent.Description ?? "");

                if (newEventId > 0)
                {
                    // Get templates associated with source event
                    var sourceTemplates = _templateDatabase.GetEventTemplates(sourceEvent.Id);

                    // Associate same templates with new event
                    foreach (var template in sourceTemplates)
                    {
                        _templateDatabase.AssignTemplateToEvent(newEventId, template.Id, false);
                        Log.Debug($"EventSelectionService: Associated template {template.Id} with new event {newEventId}");
                    }

                    Log.Debug($"EventSelectionService: Successfully duplicated event with {sourceTemplates.Count} templates");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to duplicate event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Rename an existing event
        /// </summary>
        public void RenameEvent(EventData eventToRename, string newName)
        {
            try
            {
                Log.Debug($"EventSelectionService: Renaming event '{eventToRename.Name}' to '{newName}'");

                eventToRename.Name = newName;
                _eventService.UpdateEvent(eventToRename);

                Log.Debug($"EventSelectionService: Successfully renamed event");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to rename event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete an event
        /// </summary>
        public void DeleteEvent(EventData eventToDelete)
        {
            try
            {
                Log.Debug($"EventSelectionService: Deleting event '{eventToDelete.Name}' (ID: {eventToDelete.Id})");

                // Delete the event (cascade delete will handle EventTemplates associations)
                _eventService.DeleteEvent(eventToDelete.Id);

                Log.Debug($"EventSelectionService: Successfully deleted event");
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to delete event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Create a new event based on the last opened event
        /// </summary>
        public void CreateNewEventFromLast(string eventName)
        {
            try
            {
                Log.Debug($"EventSelectionService: Creating new event '{eventName}'");

                // Create the new event
                int newEventId = _eventService.CreateEvent(eventName, "Created on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

                if (newEventId > 0)
                {
                    // Get the last opened event
                    EventData lastEvent = null;

                    // Try to get from saved settings first (check for SelectedEventId)
                    var lastSelectedEventId = Properties.Settings.Default.SelectedEventId;
                    if (lastSelectedEventId > 0)
                    {
                        lastEvent = _eventService.GetEvent(lastSelectedEventId);
                    }

                    // If not found, get the most recent event
                    if (lastEvent == null && _allEvents != null && _allEvents.Count > 0)
                    {
                        lastEvent = _allEvents.OrderByDescending(e => e.Id).FirstOrDefault();
                    }

                    // Copy templates from last event if exists
                    if (lastEvent != null)
                    {
                        var lastEventTemplates = _templateDatabase.GetEventTemplates(lastEvent.Id);

                        foreach (var template in lastEventTemplates)
                        {
                            _templateDatabase.AssignTemplateToEvent(newEventId, template.Id, false);
                            Log.Debug($"EventSelectionService: Associated template {template.Id} with new event {newEventId}");
                        }

                        Log.Debug($"EventSelectionService: Created event with {lastEventTemplates.Count} templates from last event '{lastEvent.Name}'");
                    }
                    else
                    {
                        Log.Debug($"EventSelectionService: Created empty event (no last event found)");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventSelectionService: Failed to create new event: {ex.Message}");
                throw;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}