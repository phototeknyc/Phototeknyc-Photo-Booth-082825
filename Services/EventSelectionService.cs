using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        public event EventHandler SearchCleared;
        public event PropertyChangedEventHandler PropertyChanged;
        #endregion
        
        private EventSelectionService()
        {
            _eventService = new EventService();
            _templateDatabase = new TemplateDatabase();
            _allEvents = new ObservableCollection<EventData>();
            _filteredEvents = new ObservableCollection<EventData>();
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
        public void SelectEvent(EventData eventData)
        {
            if (eventData == null) return;
            
            Log.Debug($"EventSelectionService: Selecting event '{eventData.Name}'");
            SelectedEvent = eventData;
            EventSelected?.Invoke(this, eventData);
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
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}