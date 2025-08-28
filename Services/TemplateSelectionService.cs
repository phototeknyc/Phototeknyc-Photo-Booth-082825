using System;
using System.Collections.Generic;
using System.Linq;
using Photobooth.Database;
using Photobooth.Models;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Service to handle template selection business logic
    /// Follows clean architecture - no UI code here, only business logic
    /// </summary>
    public class TemplateSelectionService
    {
        #region Events
        public event EventHandler<TemplateSelectionRequestedEventArgs> TemplateSelectionRequested;
        public event EventHandler<TemplateSelectedEventArgs> TemplateSelected;
        public event EventHandler<TemplateSelectionCancelledEventArgs> TemplateSelectionCancelled;
        #endregion

        #region Private Fields
        private readonly EventTemplateService _eventTemplateService;
        private readonly TemplateDatabase _database;
        private EventData _currentEvent;
        private TemplateData _selectedTemplate;
        private List<TemplateData> _availableTemplates;
        #endregion

        #region Properties
        public EventData CurrentEvent => _currentEvent;
        public TemplateData SelectedTemplate => _selectedTemplate;
        public List<TemplateData> AvailableTemplates => _availableTemplates;
        public bool HasMultipleTemplates => _availableTemplates?.Count > 1;
        public bool HasSingleTemplate => _availableTemplates?.Count == 1;
        public bool HasNoTemplates => _availableTemplates == null || _availableTemplates.Count == 0;
        #endregion

        #region Constructor
        public TemplateSelectionService()
        {
            _eventTemplateService = new EventTemplateService();
            _database = new TemplateDatabase();
            _availableTemplates = new List<TemplateData>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Initialize template selection for an event
        /// </summary>
        public void InitializeForEvent(EventData eventData)
        {
            try
            {
                Log.Debug($"TemplateSelectionService.InitializeForEvent: Starting initialization for event: {eventData?.Name} (ID: {eventData?.Id})");
                
                if (eventData == null)
                {
                    Log.Error("TemplateSelectionService: Cannot initialize with null event");
                    return;
                }

                _currentEvent = eventData;
                LoadTemplatesForEvent(eventData.Id);

                Log.Debug($"TemplateSelectionService: Initialized for event '{eventData.Name}' with {_availableTemplates.Count} templates");
                Log.Debug($"  HasMultipleTemplates: {HasMultipleTemplates}, HasSingleTemplate: {HasSingleTemplate}, HasNoTemplates: {HasNoTemplates}");

                // Check template count and handle accordingly
                if (HasMultipleTemplates)
                {
                    Log.Debug("TemplateSelectionService: Multiple templates available - requesting user selection");
                    // Multiple templates - need user selection
                    RequestTemplateSelection();
                }
                else if (HasSingleTemplate)
                {
                    Log.Debug("TemplateSelectionService: Single template available - auto-selecting");
                    // Single template - auto-select
                    AutoSelectSingleTemplate();
                }
                else
                {
                    // No templates available
                    Log.Error($"TemplateSelectionService: No templates available for event '{eventData.Name}'");
                    OnTemplateSelectionCancelled("No templates available for this event");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateSelectionService: Error initializing for event: {ex.Message}");
                Log.Error($"TemplateSelectionService: Stack trace: {ex.StackTrace}");
                OnTemplateSelectionCancelled("Failed to load templates");
            }
        }

        /// <summary>
        /// Load templates for current event
        /// </summary>
        public void LoadTemplatesForEvent(int eventId)
        {
            try
            {
                var eventService = new EventService();
                _availableTemplates = eventService.GetEventTemplates(eventId) ?? new List<TemplateData>();
                
                // Sort templates by name for consistent display
                _availableTemplates = _availableTemplates.OrderBy(t => t.Name).ToList();
                
                Log.Debug($"TemplateSelectionService: Loaded {_availableTemplates.Count} templates for event {eventId}");
                
                // Log template details for debugging
                foreach (var template in _availableTemplates)
                {
                    Log.Debug($"  - Template: '{template.Name}' (ID: {template.Id}, Size: {template.CanvasWidth}x{template.CanvasHeight})");
                    var items = GetTemplateItems(template);
                    Log.Debug($"    Canvas items: {items.Count} - {string.Join(", ", items.Select(i => i.ItemType))}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateSelectionService: Failed to load templates: {ex.Message}");
                _availableTemplates = new List<TemplateData>();
            }
        }

        /// <summary>
        /// Select a template
        /// </summary>
        public void SelectTemplate(TemplateData template)
        {
            try
            {
                if (template == null)
                {
                    Log.Error("TemplateSelectionService: Cannot select null template");
                    return;
                }

                _selectedTemplate = template;
                Log.Debug($"TemplateSelectionService: Selected template '{template.Name}'");

                // Fire event
                OnTemplateSelected(template);
            }
            catch (Exception ex)
            {
                Log.Error($"TemplateSelectionService: Error selecting template: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel template selection
        /// </summary>
        public void CancelSelection()
        {
            _selectedTemplate = null;
            OnTemplateSelectionCancelled("User cancelled template selection");
        }

        /// <summary>
        /// Get photo count for a template
        /// </summary>
        public int GetTemplatePhotoCount(TemplateData template)
        {
            if (template == null) return 0;
            
            // Load template items from database and count placeholders
            var items = _database.GetCanvasItems(template.Id);
            int photoCount = items?.Count(item => item.ItemType == "Placeholder") ?? 0;
            
            // Return at least 1 if no placeholders found (fallback)
            return photoCount > 0 ? photoCount : 1;
        }
        
        /// <summary>
        /// Get template items from database
        /// </summary>
        public List<CanvasItemData> GetTemplateItems(TemplateData template)
        {
            if (template == null) return new List<CanvasItemData>();
            return _database.GetCanvasItems(template.Id);
        }

        /// <summary>
        /// Validate template for use
        /// </summary>
        public bool ValidateTemplate(TemplateData template)
        {
            if (template == null) return false;
            
            // Must have valid dimensions
            if (template.CanvasWidth <= 0 || template.CanvasHeight <= 0)
            {
                Log.Error($"TemplateSelectionService: Template '{template.Name}' has invalid dimensions");
                return false;
            }
            
            return true;
        }

        /// <summary>
        /// Clear current selection
        /// </summary>
        public void ClearSelection()
        {
            _selectedTemplate = null;
            _currentEvent = null;
            _availableTemplates?.Clear();
        }
        #endregion

        #region Private Methods
        private void RequestTemplateSelection()
        {
            Log.Debug($"TemplateSelectionService.RequestTemplateSelection: Firing TemplateSelectionRequested event");
            Log.Debug($"  Event: {_currentEvent?.Name}, Templates count: {_availableTemplates?.Count}");
            Log.Debug($"  Has subscribers: {TemplateSelectionRequested != null}");
            
            TemplateSelectionRequested?.Invoke(this, new TemplateSelectionRequestedEventArgs
            {
                Event = _currentEvent,
                Templates = _availableTemplates
            });
        }

        private void AutoSelectSingleTemplate()
        {
            if (_availableTemplates?.Count == 1)
            {
                SelectTemplate(_availableTemplates[0]);
            }
        }

        #endregion

        #region Event Handlers
        private void OnTemplateSelected(TemplateData template)
        {
            TemplateSelected?.Invoke(this, new TemplateSelectedEventArgs
            {
                Template = template,
                Event = _currentEvent
            });
        }

        private void OnTemplateSelectionCancelled(string reason)
        {
            TemplateSelectionCancelled?.Invoke(this, new TemplateSelectionCancelledEventArgs
            {
                Reason = reason
            });
        }
        #endregion
    }

    #region Event Args
    public class TemplateSelectionRequestedEventArgs : EventArgs
    {
        public EventData Event { get; set; }
        public List<TemplateData> Templates { get; set; }
    }

    public class TemplateSelectedEventArgs : EventArgs
    {
        public TemplateData Template { get; set; }
        public EventData Event { get; set; }
    }

    public class TemplateSelectionCancelledEventArgs : EventArgs
    {
        public string Reason { get; set; }
    }

    #endregion
}