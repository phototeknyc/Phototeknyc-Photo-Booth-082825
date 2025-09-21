using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CameraControl.Devices;
using Photobooth.Database;

namespace Photobooth.Services
{
    /// <summary>
    /// Service that manages background assignments for events
    /// Allows organizers to pre-select backgrounds for guest selection
    /// </summary>
    public class EventBackgroundService
    {
        #region Singleton

        private static EventBackgroundService _instance;
        private static readonly object _lock = new object();

        public static EventBackgroundService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new EventBackgroundService();
                        }
                    }
                }
                return _instance;
            }
        }

        private EventBackgroundService()
        {
            Initialize();
        }

        #endregion

        #region Private Fields

        private List<EventBackground> _eventBackgrounds;
        private EventData _currentEvent;
        private VirtualBackgroundService _backgroundService;
        private Dictionary<string, Models.PhotoPlacementData> _placementDataCache = new Dictionary<string, Models.PhotoPlacementData>();

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the backgrounds assigned to the current event
        /// </summary>
        public List<EventBackground> EventBackgrounds => _eventBackgrounds ?? new List<EventBackground>();

        /// <summary>
        /// Check if event has assigned backgrounds
        /// </summary>
        public bool HasEventBackgrounds => _eventBackgrounds?.Any() == true;

        #endregion

        #region Initialization

        private void Initialize()
        {
            _backgroundService = VirtualBackgroundService.Instance;
            _eventBackgrounds = new List<EventBackground>();
            _placementDataCache = new Dictionary<string, Models.PhotoPlacementData>();
            LoadPlacementCacheFromSettings();

            // Ensure VirtualBackgroundService is initialized before we use it
            // This will be done automatically when LoadBackgroundsAsync() is called

            Log.Debug("EventBackgroundService initialized");

            // Note: LoadLastSelectedEventOnStartup is now called explicitly by EventSelectionService
            // to avoid race conditions with EventSelectionService.CheckAndRestoreSavedEvent()
        }

        private void LoadPlacementCacheFromSettings()
        {
            try
            {
                if (!string.IsNullOrEmpty(Properties.Settings.Default.CurrentBackgroundPhotoPosition))
                {
                    var savedCache = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Models.PhotoPlacementData>>(Properties.Settings.Default.CurrentBackgroundPhotoPosition);
                    if (savedCache != null)
                    {
                        _placementDataCache = savedCache;
                        Log.Debug($"Loaded {_placementDataCache.Count} placement settings from cache");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load placement cache: {ex.Message}");
                _placementDataCache = new Dictionary<string, Models.PhotoPlacementData>();
            }
        }

        /// <summary>
        /// Load the last selected event and its backgrounds on application startup
        /// Public method to be called explicitly by EventSelectionService to avoid race conditions
        /// </summary>
        public async Task LoadLastSelectedEventOnStartup()
        {
            try
            {
                // Check if we have a saved event ID
                int savedEventId = Properties.Settings.Default.SelectedEventId;
                if (savedEventId > 0)
                {
                    // Load the event from database
                    var database = new TemplateDatabase();
                    var eventData = database.GetEvent(savedEventId);

                    if (eventData != null)
                    {
                        Log.Debug($"[EventBackgroundService] Loading saved event backgrounds on startup: {eventData.Name}");

                        // Just load the event backgrounds, don't set EventSelectionService.SelectedEvent
                        // EventSelectionService will handle setting its own SelectedEvent
                        await LoadEventBackgroundsAsync(eventData);

                        Log.Debug($"[EventBackgroundService] Successfully loaded {_eventBackgrounds.Count} backgrounds for event '{eventData.Name}'");
                    }
                    else
                    {
                        Log.Debug($"[EventBackgroundService] Saved event ID {savedEventId} not found in database");
                    }
                }
                else
                {
                    Log.Debug("[EventBackgroundService] No saved event ID found, starting with no event selected");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[EventBackgroundService] Failed to load last selected event on startup: {ex.Message}");
            }
        }

        #endregion

        #region Event Management

        /// <summary>
        /// Load backgrounds for a specific event
        /// </summary>
        public async Task LoadEventBackgroundsAsync(EventData eventData)
        {
            if (eventData == null)
            {
                Log.Debug("No event provided, using all available backgrounds");
                await LoadDefaultBackgroundsAsync();
                return;
            }

            // Check if we're already loaded for this event
            if (_currentEvent != null && _currentEvent.Id == eventData.Id && _eventBackgrounds.Any())
            {
                Log.Debug($"Event {eventData.Name} already loaded, skipping reload");
                return;
            }

            _currentEvent = eventData;

            try
            {
                await Task.Run(() =>
                {
                    _eventBackgrounds.Clear();

                    // Load from database or settings
                    var savedBackgrounds = LoadSavedEventBackgrounds(eventData.Id.ToString());

                    if (savedBackgrounds.Any())
                    {
                        _eventBackgrounds = savedBackgrounds;
                        Log.Debug($"Loaded {_eventBackgrounds.Count} backgrounds for event {eventData.Name}");

                        // Restore selected background from settings or use first as default
                        string selectedBg = Properties.Settings.Default.SelectedVirtualBackground;
                        if (string.IsNullOrEmpty(selectedBg) || !_eventBackgrounds.Any(b => b.BackgroundPath == selectedBg))
                        {
                            // Use first background if no saved selection or saved selection not in list
                            var firstBg = _eventBackgrounds.FirstOrDefault();
                            if (firstBg != null && !string.IsNullOrEmpty(firstBg.BackgroundPath))
                            {
                                selectedBg = firstBg.BackgroundPath;
                            }
                        }

                        if (!string.IsNullOrEmpty(selectedBg))
                        {
                            _backgroundService.SetSelectedBackground(selectedBg);
                            Properties.Settings.Default.EnableBackgroundRemoval = true;
                            Properties.Settings.Default.SelectedVirtualBackground = selectedBg;
                            Properties.Settings.Default.Save();
                            Log.Debug($"Set background to: {selectedBg}");

                            // Load placement data for the selected background
                            var placementData = GetPhotoPlacementForBackground(selectedBg);
                            if (placementData != null)
                            {
                                Properties.Settings.Default.PhotoPlacementData = placementData.ToJson();
                                Properties.Settings.Default.Save();
                                Log.Debug($"Restored photo placement for background: {selectedBg}");
                            }
                        }
                    }
                    else
                    {
                        // If no saved backgrounds, load popular defaults
                        LoadPopularDefaults();
                        Log.Debug("No saved backgrounds, loaded popular defaults");

                        // Restore selected background from settings or use first as default
                        string selectedBg = Properties.Settings.Default.SelectedVirtualBackground;
                        if (string.IsNullOrEmpty(selectedBg) || !_eventBackgrounds.Any(b => b.BackgroundPath == selectedBg))
                        {
                            // Use first background if no saved selection
                            var firstBg = _eventBackgrounds.FirstOrDefault();
                            if (firstBg != null && !string.IsNullOrEmpty(firstBg.BackgroundPath))
                            {
                                selectedBg = firstBg.BackgroundPath;
                            }
                        }

                        if (!string.IsNullOrEmpty(selectedBg))
                        {
                            _backgroundService.SetSelectedBackground(selectedBg);
                            Properties.Settings.Default.EnableBackgroundRemoval = true;
                            Properties.Settings.Default.SelectedVirtualBackground = selectedBg;
                            Properties.Settings.Default.Save();
                            Log.Debug($"Set background to: {selectedBg}");

                            // Load placement data for the selected background
                            var placementData = GetPhotoPlacementForBackground(selectedBg);
                            if (placementData != null)
                            {
                                Properties.Settings.Default.PhotoPlacementData = placementData.ToJson();
                                Properties.Settings.Default.Save();
                                Log.Debug($"Restored photo placement for background: {selectedBg}");
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load event backgrounds: {ex.Message}");
                LoadPopularDefaults();
            }
        }

        /// <summary>
        /// Load default backgrounds when no event is selected
        /// </summary>
        private async Task LoadDefaultBackgroundsAsync()
        {
            await _backgroundService.LoadBackgroundsAsync();
            LoadPopularDefaults();
        }

        /// <summary>
        /// Load popular default backgrounds
        /// </summary>
        private void LoadPopularDefaults()
        {
            _eventBackgrounds.Clear();

            // Get popular backgrounds from each category
            var solidBgs = _backgroundService.GetBackgroundsByCategory("Solid").Take(3);
            var gradientBgs = _backgroundService.GetBackgroundsByCategory("Gradient").Take(2);

            int order = 0;
            foreach (var bg in solidBgs)
            {
                _eventBackgrounds.Add(new EventBackground
                {
                    BackgroundId = bg.Id,
                    BackgroundPath = bg.FilePath,
                    ThumbnailPath = bg.ThumbnailPath,
                    Name = bg.Name,
                    Category = bg.Category,
                    DisplayOrder = order++,
                    IsActive = true
                });
            }

            foreach (var bg in gradientBgs)
            {
                _eventBackgrounds.Add(new EventBackground
                {
                    BackgroundId = bg.Id,
                    BackgroundPath = bg.FilePath,
                    ThumbnailPath = bg.ThumbnailPath,
                    Name = bg.Name,
                    Category = bg.Category,
                    DisplayOrder = order++,
                    IsActive = true
                });
            }
        }

        /// <summary>
        /// Save event background assignments
        /// </summary>
        public async Task<bool> SaveEventBackgroundsAsync(EventData eventData, List<string> selectedBackgroundIds)
        {
            try
            {
                _currentEvent = eventData;
                _eventBackgrounds.Clear();

                int order = 0;
                foreach (var bgId in selectedBackgroundIds)
                {
                    var bg = _backgroundService.GetBackgroundById(bgId);
                    if (bg != null)
                    {
                        _eventBackgrounds.Add(new EventBackground
                        {
                            EventId = eventData.Id.ToString(),
                            BackgroundId = bg.Id,
                            BackgroundPath = bg.FilePath,
                            ThumbnailPath = bg.ThumbnailPath,
                            Name = bg.Name,
                            Category = bg.Category,
                            DisplayOrder = order++,
                            IsActive = true
                        });
                    }
                }

                // Save to database/settings
                await SaveEventBackgroundsToDatabase(_currentEvent.Id.ToString(), _eventBackgrounds);

                Log.Debug($"Saved {_eventBackgrounds.Count} backgrounds for event {eventData.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save event backgrounds: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Database Operations

        private List<EventBackground> LoadSavedEventBackgrounds(string eventId)
        {
            var backgrounds = new List<EventBackground>();

            try
            {
                // Load from database
                var database = new TemplateDatabase();
                var eventData = database.GetEvent(int.Parse(eventId));

                if (eventData != null)
                {
                    // Load photo placement dictionary from database
                    Dictionary<string, Models.PhotoPlacementData> placementDictionary = null;
                    if (!string.IsNullOrEmpty(eventData.PhotoPlacementData))
                    {
                        try
                        {
                            placementDictionary = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Models.PhotoPlacementData>>(eventData.PhotoPlacementData);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to parse photo placement data: {ex.Message}");
                        }
                    }

                    // Load background IDs from BackgroundSettings or EventBackgroundIds
                    string savedIds = eventData.BackgroundSettings;
                    if (string.IsNullOrEmpty(savedIds))
                    {
                        savedIds = Properties.Settings.Default.EventBackgroundIds;
                    }

                    if (!string.IsNullOrEmpty(savedIds))
                    {
                        var ids = savedIds.Split(',');
                        int order = 0;

                        foreach (var id in ids)
                        {
                            var bg = _backgroundService.GetBackgroundById(id.Trim());
                            if (bg != null)
                            {
                                var eventBg = new EventBackground
                                {
                                    EventId = eventId,
                                    BackgroundId = bg.Id,
                                    BackgroundPath = bg.FilePath,
                                    ThumbnailPath = bg.ThumbnailPath,
                                    Name = bg.Name,
                                    Category = bg.Category,
                                    DisplayOrder = order++,
                                    IsActive = true
                                };

                                // Load photo placement for this background if available
                                if (placementDictionary != null && placementDictionary.ContainsKey(bg.FilePath))
                                {
                                    eventBg.PhotoPlacement = placementDictionary[bg.FilePath];
                                }

                                backgrounds.Add(eventBg);
                            }
                        }
                    }
                }
                else
                {
                    // Fallback to settings if event not found in database
                    var savedIds = Properties.Settings.Default.EventBackgroundIds;
                    if (!string.IsNullOrEmpty(savedIds))
                    {
                        var ids = savedIds.Split(',');
                        int order = 0;

                        foreach (var id in ids)
                        {
                            var bg = _backgroundService.GetBackgroundById(id.Trim());
                            if (bg != null)
                            {
                                backgrounds.Add(new EventBackground
                                {
                                    EventId = eventId,
                                    BackgroundId = bg.Id,
                                    BackgroundPath = bg.FilePath,
                                    ThumbnailPath = bg.ThumbnailPath,
                                    Name = bg.Name,
                                    Category = bg.Category,
                                    DisplayOrder = order++,
                                    IsActive = true
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load saved event backgrounds: {ex.Message}");
            }

            return backgrounds;
        }

        private async Task SaveEventBackgroundsToDatabase(string eventId, List<EventBackground> backgrounds)
        {
            try
            {
                // Create a dictionary of background paths to photo placement data
                var placementDictionary = new Dictionary<string, Models.PhotoPlacementData>();

                foreach (var bg in backgrounds)
                {
                    if (!string.IsNullOrEmpty(bg.BackgroundPath) && bg.PhotoPlacement != null)
                    {
                        placementDictionary[bg.BackgroundPath] = bg.PhotoPlacement;
                    }
                }

                // Convert dictionary to JSON for storage
                string photoPlacementJson = Newtonsoft.Json.JsonConvert.SerializeObject(placementDictionary);

                // Get the current event from database
                var database = new TemplateDatabase();
                var eventData = database.GetEvent(int.Parse(eventId));

                if (eventData != null)
                {
                    // Update the PhotoPlacementData field with our JSON dictionary
                    eventData.PhotoPlacementData = photoPlacementJson;

                    // Save background IDs for reference
                    var backgroundIds = string.Join(",", backgrounds.Select(b => b.BackgroundId));
                    eventData.BackgroundSettings = backgroundIds;

                    // Update the event in database
                    database.UpdateEvent(int.Parse(eventId), eventData);
                }

                // Also save to settings for quick access
                Properties.Settings.Default.EventBackgroundIds = string.Join(",", backgrounds.Select(b => b.BackgroundId));
                Properties.Settings.Default.Save();

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save event backgrounds to database: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Save photo placement data for a specific background
        /// </summary>
        public async Task<bool> SavePhotoPlacementForBackground(string backgroundPath, Models.PhotoPlacementData placementData)
        {
            try
            {
                // Update in-memory cache for this specific background
                if (!string.IsNullOrEmpty(backgroundPath))
                {
                    _placementDataCache[backgroundPath] = placementData;

                    // Save the entire cache to settings for persistence
                    var cacheJson = Newtonsoft.Json.JsonConvert.SerializeObject(_placementDataCache);
                    Properties.Settings.Default.CurrentBackgroundPhotoPosition = cacheJson;
                    Properties.Settings.Default.Save();
                }

                // Find the background in our list
                var eventBg = _eventBackgrounds?.FirstOrDefault(b => b.BackgroundPath == backgroundPath);
                if (eventBg != null)
                {
                    // Update the placement data
                    eventBg.PhotoPlacement = placementData;

                    // Save to database
                    if (_currentEvent != null)
                    {
                        await SaveEventBackgroundsToDatabase(_currentEvent.Id.ToString(), _eventBackgrounds);
                    }

                    Log.Debug($"Saved photo placement for background: {backgroundPath}");
                    return true;
                }

                // Even if background not in list, we saved to cache
                Log.Debug($"Saved photo placement to cache for: {backgroundPath}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save photo placement: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get photo placement data for a specific background
        /// </summary>
        public Models.PhotoPlacementData GetPhotoPlacementForBackground(string backgroundPath)
        {
            try
            {
                // First try to get from in-memory cache
                if (!string.IsNullOrEmpty(backgroundPath) && _placementDataCache.ContainsKey(backgroundPath))
                {
                    return _placementDataCache[backgroundPath];
                }

                // Then try to get from event backgrounds
                var eventBg = _eventBackgrounds?.FirstOrDefault(b => b.BackgroundPath == backgroundPath);
                if (eventBg?.PhotoPlacement != null)
                {
                    // Add to cache for faster access
                    _placementDataCache[backgroundPath] = eventBg.PhotoPlacement;
                    return eventBg.PhotoPlacement;
                }

                // Try to load cache from settings if not loaded
                if (!string.IsNullOrEmpty(Properties.Settings.Default.CurrentBackgroundPhotoPosition))
                {
                    try
                    {
                        var savedCache = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, Models.PhotoPlacementData>>(Properties.Settings.Default.CurrentBackgroundPhotoPosition);
                        if (savedCache != null)
                        {
                            _placementDataCache = savedCache;
                            if (_placementDataCache.ContainsKey(backgroundPath))
                            {
                                return _placementDataCache[backgroundPath];
                            }
                        }
                    }
                    catch { }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get photo placement: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Guest Selection

        /// <summary>
        /// Get simplified list for guest selection
        /// </summary>
        public List<GuestBackgroundOption> GetGuestBackgroundOptions()
        {
            var options = new List<GuestBackgroundOption>();

            foreach (var bg in _eventBackgrounds.Where(b => b.IsActive).OrderBy(b => b.DisplayOrder))
            {
                options.Add(new GuestBackgroundOption
                {
                    Id = bg.BackgroundId,
                    Name = bg.Name,
                    ThumbnailPath = bg.ThumbnailPath,
                    BackgroundPath = bg.BackgroundPath
                });
            }

            // Always add "No Background" option
            options.Insert(0, new GuestBackgroundOption
            {
                Id = "none",
                Name = "No Background",
                ThumbnailPath = null,
                BackgroundPath = null
            });

            return options;
        }

        /// <summary>
        /// Set the selected background for the session
        /// </summary>
        public void SelectBackgroundForSession(string backgroundId)
        {
            if (backgroundId == "none")
            {
                _backgroundService.SetSelectedBackground(null);
                Properties.Settings.Default.EnableBackgroundRemoval = false;
            }
            else
            {
                var bg = _eventBackgrounds.FirstOrDefault(b => b.BackgroundId == backgroundId);
                if (bg != null)
                {
                    _backgroundService.SetSelectedBackground(bg.BackgroundPath);
                    Properties.Settings.Default.EnableBackgroundRemoval = true;
                }
            }
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Represents a background assigned to an event
    /// </summary>
    public class EventBackground
    {
        public string EventId { get; set; }
        public string BackgroundId { get; set; }
        public string BackgroundPath { get; set; }
        public string ThumbnailPath { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }

        /// <summary>
        /// JSON string containing photo placement data for this background
        /// </summary>
        public string PhotoPlacementJson { get; set; }

        /// <summary>
        /// Gets or sets the photo placement data (deserialized from JSON)
        /// </summary>
        public Models.PhotoPlacementData PhotoPlacement
        {
            get => string.IsNullOrEmpty(PhotoPlacementJson) ? null : Models.PhotoPlacementData.FromJson(PhotoPlacementJson);
            set => PhotoPlacementJson = value?.ToJson();
        }
    }

    /// <summary>
    /// Simplified background option for guest selection
    /// </summary>
    public class GuestBackgroundOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ThumbnailPath { get; set; }
        public string BackgroundPath { get; set; }
    }

    #endregion
}