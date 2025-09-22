using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CameraControl.Devices;
using Photobooth.Database;
using Photobooth.Models;
using System.IO;

namespace Photobooth.Services
{
    /// <summary>
    /// Simple EventBackground class for compatibility
    /// </summary>
    public class EventBackground
    {
        public string Id { get; set; }
        public string BackgroundPath { get; set; }
        public string Name { get; set; }
        public bool IsSelected { get; set; }
        public string ThumbnailPath => BackgroundPath; // Same as background path for compatibility
    }
    /// <summary>
    /// Service that manages background assignments for events
    /// Now using database for per-event storage with auto-save
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

        private EventBackgroundDatabase _database;
        private VirtualBackgroundService _backgroundService;
        private EventData _currentEvent;
        private List<EventBackgroundData> _eventBackgrounds;
        private EventBackgroundSettings _currentSettings;
        private string _selectedBackgroundPath;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the backgrounds assigned to the current event
        /// </summary>
        public List<EventBackgroundData> EventBackgrounds => _eventBackgrounds ?? new List<EventBackgroundData>();

        /// <summary>
        /// Check if event has assigned backgrounds
        /// </summary>
        public bool HasEventBackgrounds => _eventBackgrounds?.Any() == true;

        /// <summary>
        /// Gets the current event
        /// </summary>
        public EventData CurrentEvent => _currentEvent;

        /// <summary>
        /// Gets the current event settings
        /// </summary>
        public EventBackgroundSettings CurrentSettings => _currentSettings;

        /// <summary>
        /// Gets the currently selected background path
        /// </summary>
        public string SelectedBackgroundPath => _selectedBackgroundPath;

        #endregion

        #region Events

        public event EventHandler EventChanged;
        public event EventHandler BackgroundsChanged;
        public event EventHandler SettingsChanged;
        public event EventHandler<string> BackgroundSelected;

        #endregion

        #region Initialization

        private void Initialize()
        {
            _database = new EventBackgroundDatabase();
            _backgroundService = VirtualBackgroundService.Instance;
            _eventBackgrounds = new List<EventBackgroundData>();

            Log.Debug("EventBackgroundService initialized with database support");
        }

        #endregion

        #region Event Management

        /// <summary>
        /// Load backgrounds and settings for a specific event
        /// </summary>
        public async Task LoadEventAsync(EventData eventData)
        {
            if (eventData == null)
            {
                Log.Debug("EventBackgroundService: No event provided");
                _currentEvent = null;
                _eventBackgrounds.Clear();
                _currentSettings = null;
                _selectedBackgroundPath = null;
                return;
            }

            try
            {
                _currentEvent = eventData;
                Log.Debug($"EventBackgroundService: Loading event '{eventData.Name}' (ID: {eventData.Id})");

                // Load settings from database
                _currentSettings = _database.GetEventSettings(eventData.Id);

                // Load backgrounds from database
                _eventBackgrounds = _database.GetEventBackgrounds(eventData.Id);

                // Find selected background
                var selectedBg = _eventBackgrounds.FirstOrDefault(b => b.IsSelected);
                _selectedBackgroundPath = selectedBg?.BackgroundPath;

                // If no backgrounds in database, load defaults for first time
                if (!_eventBackgrounds.Any())
                {
                    await LoadDefaultBackgroundsAsync();
                }

                // Apply selected background to VirtualBackgroundService
                if (!string.IsNullOrEmpty(_selectedBackgroundPath) && File.Exists(_selectedBackgroundPath))
                {
                    _backgroundService.SetSelectedBackground(_selectedBackgroundPath);

                    // Apply placement data if available
                    if (selectedBg?.PhotoPlacementData != null)
                    {
                        ApplyPhotoPlacement(selectedBg.PhotoPlacementData);
                    }
                }

                // Apply settings
                ApplyEventSettings();

                // Notify listeners
                EventChanged?.Invoke(this, EventArgs.Empty);
                BackgroundsChanged?.Invoke(this, EventArgs.Empty);
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Log.Debug($"EventBackgroundService: Loaded {_eventBackgrounds.Count} backgrounds for event '{eventData.Name}'");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to load event: {ex.Message}");
            }
        }

        /// <summary>
        /// Load default backgrounds for first-time event setup
        /// </summary>
        private async Task LoadDefaultBackgroundsAsync()
        {
            if (_currentEvent == null) return;

            try
            {
                // Get popular backgrounds from VirtualBackgroundService
                var popularBackgrounds = GetPopularBackgrounds();

                foreach (var bgPath in popularBackgrounds.Take(6)) // Limit to 6 defaults
                {
                    if (File.Exists(bgPath))
                    {
                        _database.SaveEventBackground(_currentEvent.Id, bgPath,
                            Path.GetFileNameWithoutExtension(bgPath),
                            isSelected: _eventBackgrounds.Count == 0); // Select first one
                    }
                }

                // Reload from database
                _eventBackgrounds = _database.GetEventBackgrounds(_currentEvent.Id);

                Log.Debug($"EventBackgroundService: Loaded {_eventBackgrounds.Count} default backgrounds");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to load default backgrounds: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply event settings to the application
        /// </summary>
        private void ApplyEventSettings()
        {
            if (_currentSettings == null) return;

            // Note: We don't modify global Properties.Settings here
            // The UI should use CurrentSettings to determine behavior

            Log.Debug($"EventBackgroundService: Applied settings for event {_currentEvent?.Id}");
        }

        #endregion

        #region Background Management

        /// <summary>
        /// Add a background to the current event (auto-saves)
        /// </summary>
        public void AddBackground(string backgroundPath, string backgroundName = null)
        {
            if (_currentEvent == null || string.IsNullOrEmpty(backgroundPath)) return;

            try
            {
                _database.SaveEventBackground(_currentEvent.Id, backgroundPath, backgroundName);

                // Reload backgrounds
                _eventBackgrounds = _database.GetEventBackgrounds(_currentEvent.Id);

                BackgroundsChanged?.Invoke(this, EventArgs.Empty);

                Log.Debug($"EventBackgroundService: Added background '{backgroundPath}' to event {_currentEvent.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to add background: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove a background from the current event (auto-saves)
        /// </summary>
        public void RemoveBackground(string backgroundPath)
        {
            if (_currentEvent == null || string.IsNullOrEmpty(backgroundPath)) return;

            try
            {
                _database.RemoveEventBackground(_currentEvent.Id, backgroundPath);

                // Reload backgrounds
                _eventBackgrounds = _database.GetEventBackgrounds(_currentEvent.Id);

                // If removed background was selected, select another
                if (_selectedBackgroundPath == backgroundPath)
                {
                    var firstBg = _eventBackgrounds.FirstOrDefault();
                    if (firstBg != null)
                    {
                        SelectBackground(firstBg.BackgroundPath);
                    }
                    else
                    {
                        _selectedBackgroundPath = null;
                    }
                }

                BackgroundsChanged?.Invoke(this, EventArgs.Empty);

                Log.Debug($"EventBackgroundService: Removed background '{backgroundPath}' from event {_currentEvent.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to remove background: {ex.Message}");
            }
        }

        /// <summary>
        /// Select a background for the current event (auto-saves)
        /// </summary>
        public void SelectBackground(string backgroundPath)
        {
            if (_currentEvent == null || string.IsNullOrEmpty(backgroundPath)) return;

            try
            {
                _database.SetSelectedBackground(_currentEvent.Id, backgroundPath);
                _selectedBackgroundPath = backgroundPath;

                // Apply to VirtualBackgroundService
                _backgroundService.SetSelectedBackground(backgroundPath);

                // Reload to get updated data
                _eventBackgrounds = _database.GetEventBackgrounds(_currentEvent.Id);

                // Apply placement data if available
                var selectedBg = _eventBackgrounds.FirstOrDefault(b => b.BackgroundPath == backgroundPath);
                if (selectedBg?.PhotoPlacementData != null)
                {
                    ApplyPhotoPlacement(selectedBg.PhotoPlacementData);
                }

                BackgroundSelected?.Invoke(this, backgroundPath);

                Log.Debug($"EventBackgroundService: Selected background '{backgroundPath}' for event {_currentEvent.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to select background: {ex.Message}");
            }
        }

        /// <summary>
        /// Update photo placement for current background (auto-saves)
        /// </summary>
        public void UpdatePhotoPlacement(PhotoPlacementData placementData)
        {
            if (_currentEvent == null || string.IsNullOrEmpty(_selectedBackgroundPath)) return;

            try
            {
                // Log the placement data being saved
                if (placementData != null && placementData.PlacementZones != null && placementData.PlacementZones.Count > 0)
                {
                    var zone = placementData.PlacementZones[0];
                    Log.Debug($"[EventBackgroundService] Saving placement data for '{Path.GetFileName(_selectedBackgroundPath)}':");
                    Log.Debug($"  Zone: X={zone.X:F3}, Y={zone.Y:F3}, Width={zone.Width:F3}, Height={zone.Height:F3}");
                    Log.Debug($"  MaintainAspectRatio={placementData.MaintainAspectRatio}, DefaultAspectRatio={placementData.DefaultAspectRatio}");
                }

                _database.UpdatePhotoPlacement(_currentEvent.Id, _selectedBackgroundPath, placementData);

                // Update local cache
                var bg = _eventBackgrounds.FirstOrDefault(b => b.BackgroundPath == _selectedBackgroundPath);
                if (bg != null)
                {
                    bg.PhotoPlacementData = placementData;
                }

                Log.Debug($"EventBackgroundService: Updated photo placement for '{_selectedBackgroundPath}'");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to update photo placement: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all backgrounds for current event
        /// </summary>
        public void ClearAllBackgrounds()
        {
            if (_currentEvent == null) return;

            try
            {
                _database.ClearEventBackgrounds(_currentEvent.Id);
                _eventBackgrounds.Clear();
                _selectedBackgroundPath = null;

                BackgroundsChanged?.Invoke(this, EventArgs.Empty);

                Log.Debug($"EventBackgroundService: Cleared all backgrounds for event {_currentEvent.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to clear backgrounds: {ex.Message}");
            }
        }

        #endregion

        #region Settings Management

        /// <summary>
        /// Update event settings (auto-saves)
        /// </summary>
        public void UpdateSettings(EventBackgroundSettings settings)
        {
            if (_currentEvent == null || settings == null) return;

            try
            {
                settings.EventId = _currentEvent.Id;
                _database.SaveEventSettings(settings);
                _currentSettings = settings;

                ApplyEventSettings();
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Log.Debug($"EventBackgroundService: Updated settings for event {_currentEvent.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to update settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Update a specific setting (auto-saves)
        /// </summary>
        public void UpdateSetting(string settingName, object value)
        {
            if (_currentEvent == null || _currentSettings == null) return;

            try
            {
                switch (settingName)
                {
                    case "EnableBackgroundRemoval":
                        _currentSettings.EnableBackgroundRemoval = (bool)value;
                        break;
                    case "UseGuestBackgroundPicker":
                        _currentSettings.UseGuestBackgroundPicker = (bool)value;
                        break;
                    case "BackgroundRemovalQuality":
                        _currentSettings.BackgroundRemovalQuality = (string)value;
                        break;
                    case "BackgroundRemovalEdgeRefinement":
                        _currentSettings.BackgroundRemovalEdgeRefinement = (int)value;
                        break;
                    case "DefaultBackgroundPath":
                        _currentSettings.DefaultBackgroundPath = (string)value;
                        break;
                    default:
                        Log.Debug($"Unknown setting: {settingName}");
                        return;
                }

                _database.SaveEventSettings(_currentSettings);
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Log.Debug($"EventBackgroundService: Updated setting '{settingName}' = '{value}' for event {_currentEvent.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to update setting '{settingName}': {ex.Message}");
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Copy backgrounds from another event
        /// </summary>
        public void CopyFromEvent(int sourceEventId)
        {
            if (_currentEvent == null) return;

            try
            {
                _database.CopyEventBackgrounds(sourceEventId, _currentEvent.Id);

                // Reload
                _eventBackgrounds = _database.GetEventBackgrounds(_currentEvent.Id);
                _currentSettings = _database.GetEventSettings(_currentEvent.Id);

                BackgroundsChanged?.Invoke(this, EventArgs.Empty);
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Log.Debug($"EventBackgroundService: Copied backgrounds from event {sourceEventId} to {_currentEvent.Id}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventBackgroundService: Failed to copy backgrounds: {ex.Message}");
            }
        }

        /// <summary>
        /// Get popular background paths from VirtualBackgroundService
        /// </summary>
        private List<string> GetPopularBackgrounds()
        {
            var backgrounds = new List<string>();

            try
            {
                // Get backgrounds from VirtualBackgroundService's Models/Backgrounds folder
                string backgroundsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "Backgrounds");

                if (Directory.Exists(backgroundsDir))
                {
                    // Popular subfolder
                    string popularDir = Path.Combine(backgroundsDir, "Popular");
                    if (Directory.Exists(popularDir))
                    {
                        backgrounds.AddRange(Directory.GetFiles(popularDir, "*.jpg"));
                        backgrounds.AddRange(Directory.GetFiles(popularDir, "*.png"));
                    }

                    // Also check Custom folder
                    string customDir = Path.Combine(backgroundsDir, "Custom");
                    if (Directory.Exists(customDir))
                    {
                        backgrounds.AddRange(Directory.GetFiles(customDir, "*.jpg"));
                        backgrounds.AddRange(Directory.GetFiles(customDir, "*.png"));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get popular backgrounds: {ex.Message}");
            }

            return backgrounds;
        }

        /// <summary>
        /// Apply photo placement data to the current template
        /// </summary>
        private void ApplyPhotoPlacement(PhotoPlacementData placementData)
        {
            if (placementData == null) return;

            try
            {
                // Store in Properties.Settings for compatibility
                Properties.Settings.Default.PhotoPlacementData = placementData.ToJson();
                Properties.Settings.Default.Save();

                Log.Debug("EventBackgroundService: Applied photo placement data");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to apply photo placement: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a background exists for the current event
        /// </summary>
        public bool HasBackground(string backgroundPath)
        {
            return _eventBackgrounds?.Any(b => b.BackgroundPath == backgroundPath) == true;
        }

        /// <summary>
        /// Get placement data for a specific background
        /// </summary>
        public PhotoPlacementData GetPlacementData(string backgroundPath)
        {
            var bg = _eventBackgrounds?.FirstOrDefault(b => b.BackgroundPath == backgroundPath);
            var placementData = bg?.PhotoPlacementData;

            if (placementData != null && placementData.PlacementZones != null && placementData.PlacementZones.Count > 0)
            {
                var zone = placementData.PlacementZones[0];
                Log.Debug($"[EventBackgroundService] Retrieved placement data for '{Path.GetFileName(backgroundPath)}':");
                Log.Debug($"  Zone: X={zone.X:F3}, Y={zone.Y:F3}, Width={zone.Width:F3}, Height={zone.Height:F3}");
                Log.Debug($"  MaintainAspectRatio={placementData.MaintainAspectRatio}, DefaultAspectRatio={placementData.DefaultAspectRatio}");
            }
            else
            {
                Log.Debug($"[EventBackgroundService] No placement data found for '{Path.GetFileName(backgroundPath)}'");
            }

            return placementData;
        }

        #endregion

        #region Compatibility Methods (for existing code)

        /// <summary>
        /// Compatibility method - redirects to LoadEventAsync
        /// </summary>
        public Task LoadEventBackgroundsAsync(EventData eventData)
        {
            return LoadEventAsync(eventData);
        }

        /// <summary>
        /// Get photo placement data for a background (compatibility)
        /// </summary>
        public PhotoPlacementData GetPhotoPlacementForBackground(string backgroundPath)
        {
            return GetPlacementData(backgroundPath);
        }

        /// <summary>
        /// Save photo placement for a background (compatibility)
        /// </summary>
        public void SavePhotoPlacementForBackground(string backgroundPath, PhotoPlacementData placementData)
        {
            if (_currentEvent == null || string.IsNullOrEmpty(backgroundPath)) return;
            _database.UpdatePhotoPlacement(_currentEvent.Id, backgroundPath, placementData);
        }

        /// <summary>
        /// Load last selected event on startup (compatibility)
        /// </summary>
        public async Task LoadLastSelectedEventOnStartup()
        {
            try
            {
                // Get saved event ID from settings
                int savedEventId = Properties.Settings.Default.SelectedEventId;
                if (savedEventId > 0)
                {
                    var eventService = new EventService();
                    var allEvents = eventService.GetAllEvents();
                    var eventData = allEvents.FirstOrDefault(e => e.Id == savedEventId);
                    if (eventData != null)
                    {
                        await LoadEventAsync(eventData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load last selected event: {ex.Message}");
            }
        }

        /// <summary>
        /// Get guest background options (compatibility)
        /// </summary>
        public List<EventBackground> GetGuestBackgroundOptions()
        {
            if (_eventBackgrounds == null) return new List<EventBackground>();

            // Convert to old EventBackground format for compatibility
            return _eventBackgrounds.Select(eb => new EventBackground
            {
                Id = Guid.NewGuid().ToString(),
                BackgroundPath = eb.BackgroundPath,
                Name = eb.BackgroundName ?? Path.GetFileNameWithoutExtension(eb.BackgroundPath),
                IsSelected = eb.IsSelected
            }).ToList();
        }

        /// <summary>
        /// Select background for session (compatibility)
        /// </summary>
        public void SelectBackgroundForSession(string backgroundPath)
        {
            SelectBackground(backgroundPath);
        }

        #endregion
    }
}