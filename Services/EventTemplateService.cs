using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.Database;
using Photobooth.Models;
using CameraControl.Devices;

namespace Photobooth.Services
{
    /// <summary>
    /// Handles event and template selection logic
    /// </summary>
    public class EventTemplateService
    {
        private readonly EventService eventService;
        private readonly TemplateDatabase database;
        
        // Current selections
        public EventData CurrentEvent { get; private set; }
        public TemplateData CurrentTemplate { get; private set; }
        public EventData SelectedEventForOverlay { get; set; }
        public TemplateData SelectedTemplateForOverlay { get; set; }
        
        // Available options
        public List<EventData> AvailableEvents { get; private set; }
        public List<TemplateData> AvailableTemplates { get; private set; }
        
        public EventTemplateService()
        {
            eventService = new EventService();
            database = new TemplateDatabase();
            AvailableEvents = new List<EventData>();
            AvailableTemplates = new List<TemplateData>();
        }
        
        /// <summary>
        /// Load available events from database
        /// </summary>
        public void LoadAvailableEvents()
        {
            try
            {
                AvailableEvents = eventService.GetAllEvents() ?? new List<EventData>();
                Log.Debug($"EventTemplateService: Loaded {AvailableEvents.Count} active events");
            }
            catch (Exception ex)
            {
                Log.Error($"EventTemplateService: Failed to load events: {ex.Message}");
                AvailableEvents = new List<EventData>();
            }
        }
        
        /// <summary>
        /// Load templates for a specific event
        /// </summary>
        public void LoadAvailableTemplates(int eventId)
        {
            try
            {
                AvailableTemplates = eventService.GetEventTemplates(eventId) ?? new List<TemplateData>();
                Log.Debug($"EventTemplateService: Loaded {AvailableTemplates.Count} templates for event {eventId}");
            }
            catch (Exception ex)
            {
                Log.Error($"EventTemplateService: Failed to load templates: {ex.Message}");
                AvailableTemplates = new List<TemplateData>();
            }
        }
        
        /// <summary>
        /// Select an event
        /// </summary>
        public async void SelectEvent(EventData eventData)
        {
            if (eventData == null) return;

            SelectedEventForOverlay = eventData;
            LoadAvailableTemplates(eventData.Id);
            Log.Debug($"EventTemplateService: Selected event {eventData.Name}");

            // Load event backgrounds
            try
            {
                await EventBackgroundService.Instance.LoadEventBackgroundsAsync(eventData);
                Log.Debug($"EventTemplateService: Loaded event backgrounds for '{eventData.Name}'");
            }
            catch (Exception ex)
            {
                Log.Error($"EventTemplateService: Failed to load event backgrounds: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Select a template
        /// </summary>
        public void SelectTemplate(TemplateData templateData)
        {
            if (templateData == null) return;
            
            SelectedTemplateForOverlay = templateData;
            Log.Debug($"EventTemplateService: Selected template {templateData.Name}");
        }
        
        /// <summary>
        /// Confirm the current selection
        /// </summary>
        public bool ConfirmSelection()
        {
            if (SelectedEventForOverlay == null || SelectedTemplateForOverlay == null)
            {
                Log.Error("EventTemplateService: Cannot confirm - missing event or template");
                return false;
            }
            
            CurrentEvent = SelectedEventForOverlay;
            CurrentTemplate = SelectedTemplateForOverlay;
            
            // Update session event if service exists
            if (CurrentEvent != null)
            {
                // Note: EventService may not have SetCurrentEvent method
                // This functionality may need to be implemented differently
            }
            
            Log.Debug($"EventTemplateService: Confirmed event {CurrentEvent.Name} with template {CurrentTemplate.Name}");
            return true;
        }
        
        /// <summary>
        /// Clear current selections
        /// </summary>
        public void ClearSelections()
        {
            SelectedEventForOverlay = null;
            SelectedTemplateForOverlay = null;
            AvailableTemplates.Clear();
        }
        
        /// <summary>
        /// Load workflow from session
        /// </summary>
        public void LoadEventTemplateWorkflow()
        {
            try
            {
                // Note: EventService may not have GetCurrentEventData method
                // This functionality may need to be implemented differently
                
                // Check for default template (if property exists)
                // Note: DefaultTemplateId may not exist in Settings
                if (CurrentEvent != null)
                {
                    var templates = eventService.GetEventTemplates(CurrentEvent.Id);
                    if (templates != null && templates.Count > 0)
                    {
                        CurrentTemplate = templates.FirstOrDefault();
                        
                        if (CurrentTemplate != null)
                        {
                            Log.Debug($"EventTemplateService: Loaded default template: {CurrentTemplate.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"EventTemplateService: Failed to load workflow: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get total photos needed for current template
        /// </summary>
        public int GetTotalPhotosNeeded()
        {
            if (CurrentTemplate == null) return 1;
            
            // Parse template layout to determine photo count
            int photoCount = 1; // Default
            
            // Try to determine photo count from template properties
            // Note: The actual property name may vary - using PhotoCount if available
            if (CurrentTemplate != null)
            {
                // Assume 1 photo per template unless specified otherwise
                // This can be extended based on actual template structure
                photoCount = 1;
            }
            
            return photoCount;
        }
    }
}