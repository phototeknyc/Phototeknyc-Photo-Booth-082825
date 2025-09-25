using System;
using System.Collections.Generic;
using System.Linq;
using Photobooth.Database;
using Photobooth.Models;
using Photobooth.Controls;

namespace Photobooth.Services
{
    /// <summary>
    /// Service that manages AI transformation template assignments for events
    /// </summary>
    public class EventAITemplateService
    {
        #region Singleton
        private static EventAITemplateService _instance;
        private static readonly object _lock = new object();

        public static EventAITemplateService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new EventAITemplateService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Properties
        private EventAITemplateDatabase _database;
        private AITemplateService _templateService;
        private int? _currentEventId;
        private List<EventAITemplate> _cachedTemplates;
        private EventAITemplateSettings _cachedSettings;
        #endregion

        #region Events
        public event EventHandler<EventArgs> TemplatesUpdated;
        public event EventHandler<EventArgs> SettingsUpdated;
        #endregion

        #region Constructor
        private EventAITemplateService()
        {
            _database = EventAITemplateDatabase.Instance;
            _templateService = AITemplateService.Instance;
            _cachedTemplates = new List<EventAITemplate>();
        }
        #endregion

        #region Event Management

        /// <summary>
        /// Set the current event
        /// </summary>
        public void SetCurrentEvent(int? eventId)
        {
            if (_currentEventId != eventId)
            {
                _currentEventId = eventId;
                RefreshCache();
            }
        }

        /// <summary>
        /// Get the current event ID
        /// </summary>
        public int? GetCurrentEventId()
        {
            return _currentEventId;
        }

        #endregion

        #region Template Management

        /// <summary>
        /// Add templates to the current event
        /// </summary>
        public void AddTemplatesToCurrentEvent(List<AITransformationTemplate> templates)
        {
            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event set");
                return;
            }

            foreach (var template in templates)
            {
                _database.AddOrUpdateTemplateForEvent(
                    _currentEventId.Value,
                    template.Id,
                    template.Name,
                    template.Category?.Id,
                    template.Category?.Name,
                    isSelected: true,
                    isDefault: false
                );
            }

            RefreshCache();
            TemplatesUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Remove a template from the current event
        /// </summary>
        public void RemoveTemplateFromCurrentEvent(int templateId)
        {
            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event set");
                return;
            }

            _database.RemoveTemplateFromEvent(_currentEventId.Value, templateId);
            RefreshCache();
            TemplatesUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Set template selection status for current event
        /// </summary>
        public void SetTemplateSelected(int templateId, bool isSelected)
        {
            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event set");
                return;
            }

            // Get template details for better saving
            var template = _templateService.GetTemplateById(templateId);
            if (template != null)
            {
                // First ensure the template exists in the event database
                _database.AddOrUpdateTemplateForEvent(
                    _currentEventId.Value,
                    templateId,
                    template.Name,
                    template.Category?.Id,
                    template.Category?.Name,
                    isSelected: isSelected,
                    isDefault: false
                );
            }
            else
            {
                // Fallback to simple selection update
                _database.SetTemplateSelection(_currentEventId.Value, templateId, isSelected);
            }

            RefreshCache();
            TemplatesUpdated?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: Set template {templateId} selected={isSelected} for event {_currentEventId.Value}");
        }

        /// <summary>
        /// Set default template for current event
        /// </summary>
        public void SetDefaultTemplate(int templateId)
        {
            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event set");
                return;
            }

            // Get template details from AITemplateService
            var template = _templateService.GetTemplateById(templateId);
            if (template != null)
            {
                _database.AddOrUpdateTemplateForEvent(
                    _currentEventId.Value,
                    templateId,
                    template.Name,
                    template.Category?.Id,
                    template.Category?.Name,
                    isSelected: true,
                    isDefault: true
                );

                RefreshCache();
                TemplatesUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Get all templates for the current event
        /// </summary>
        public List<AITransformationTemplate> GetTemplatesForCurrentEvent()
        {
            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: GetTemplatesForCurrentEvent - Event ID: {_currentEventId}");

            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event, returning all templates");
                return _templateService.GetAllTemplates();
            }

            // If no templates are selected for this event, return all available templates
            if (_cachedTemplates == null || _cachedTemplates.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"EventAITemplateService: No cached templates for event {_currentEventId.Value}, returning all templates");
                return _templateService.GetAllTemplates();
            }

            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: Found {_cachedTemplates.Count} cached templates for event");

            // Get selected templates only
            var selectedTemplateIds = _cachedTemplates
                .Where(t => t.IsSelected)
                .Select(t => t.TemplateId)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: {selectedTemplateIds.Count} templates are marked as selected");

            if (selectedTemplateIds.Count == 0)
            {
                // If none are explicitly selected, return all available
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No templates explicitly selected, returning all templates");
                return _templateService.GetAllTemplates();
            }

            // Return only selected templates
            var allTemplates = _templateService.GetAllTemplates();
            var selectedTemplates = allTemplates.Where(t => selectedTemplateIds.Contains(t.Id)).ToList();
            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: Returning {selectedTemplates.Count} selected templates");

            return selectedTemplates;
        }

        /// <summary>
        /// Get selected template IDs for current event
        /// </summary>
        public List<int> GetSelectedTemplateIds()
        {
            if (!_currentEventId.HasValue || _cachedTemplates == null)
            {
                return new List<int>();
            }

            return _cachedTemplates
                .Where(t => t.IsSelected)
                .Select(t => t.TemplateId)
                .ToList();
        }

        /// <summary>
        /// Get the default template for the current event
        /// </summary>
        public AITransformationTemplate GetDefaultTemplate()
        {
            if (!_currentEventId.HasValue)
            {
                return null;
            }

            var defaultTemplate = _cachedTemplates?.FirstOrDefault(t => t.IsDefault);
            if (defaultTemplate != null)
            {
                return _templateService.GetTemplateById(defaultTemplate.TemplateId);
            }

            return null;
        }

        /// <summary>
        /// Clear all templates for current event
        /// </summary>
        public void ClearTemplatesForCurrentEvent()
        {
            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event set");
                return;
            }

            _database.ClearTemplatesForEvent(_currentEventId.Value);
            RefreshCache();
            TemplatesUpdated?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Settings Management

        /// <summary>
        /// Get AI transformation settings for current event
        /// </summary>
        public EventAITemplateSettings GetCurrentEventSettings()
        {
            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: GetCurrentEventSettings - Current Event ID: {_currentEventId}");

            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event ID, returning default settings");
                // Return default settings
                return new EventAITemplateSettings
                {
                    EventId = 0,
                    EnableAITransformation = false,
                    AutoApplyDefault = false,
                    ShowSelectionOverlay = true,
                    SelectionTimeout = 120,
                    DefaultTemplateId = null
                };
            }

            if (_cachedSettings == null)
            {
                System.Diagnostics.Debug.WriteLine($"EventAITemplateService: Loading settings from database for event {_currentEventId.Value}");
                _cachedSettings = _database.GetEventSettings(_currentEventId.Value);
                System.Diagnostics.Debug.WriteLine($"EventAITemplateService: Loaded settings - EnableAI: {_cachedSettings?.EnableAITransformation}, ShowOverlay: {_cachedSettings?.ShowSelectionOverlay}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"EventAITemplateService: Using cached settings for event {_currentEventId.Value}");
            }

            return _cachedSettings;
        }

        /// <summary>
        /// Update AI transformation settings for current event
        /// </summary>
        public void UpdateCurrentEventSettings(
            bool? enableAITransformation = null,
            bool? autoApplyDefault = null,
            bool? showSelectionOverlay = null,
            int? selectionTimeout = null,
            int? defaultTemplateId = null)
        {
            if (!_currentEventId.HasValue)
            {
                System.Diagnostics.Debug.WriteLine("EventAITemplateService: No current event set");
                return;
            }

            _database.UpdateEventSettings(
                _currentEventId.Value,
                enableAITransformation,
                autoApplyDefault,
                showSelectionOverlay,
                selectionTimeout,
                defaultTemplateId
            );

            // Refresh cached settings
            _cachedSettings = _database.GetEventSettings(_currentEventId.Value);
            SettingsUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Check if AI transformation is enabled for current event
        /// </summary>
        public bool IsAITransformationEnabled()
        {
            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: IsAITransformationEnabled - Current Event ID: {_currentEventId}");
            var settings = GetCurrentEventSettings();
            bool enabled = settings?.EnableAITransformation ?? false;
            System.Diagnostics.Debug.WriteLine($"EventAITemplateService: AI Transformation enabled: {enabled} (EventId in settings: {settings?.EventId})");
            return enabled;
        }

        /// <summary>
        /// Check if auto-apply default is enabled
        /// </summary>
        public bool IsAutoApplyDefaultEnabled()
        {
            var settings = GetCurrentEventSettings();
            return settings?.AutoApplyDefault ?? false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Refresh cached data
        /// </summary>
        private void RefreshCache()
        {
            if (_currentEventId.HasValue)
            {
                _cachedTemplates = _database.GetTemplatesForEvent(_currentEventId.Value);
                _cachedSettings = _database.GetEventSettings(_currentEventId.Value);
            }
            else
            {
                _cachedTemplates = new List<EventAITemplate>();
                _cachedSettings = null;
            }
        }

        #endregion

        #region Template Selection UI Support

        /// <summary>
        /// Get available templates grouped by category for UI display
        /// </summary>
        public Dictionary<AITemplateCategory, List<AITransformationTemplate>> GetTemplatesGroupedByCategory()
        {
            var templates = GetTemplatesForCurrentEvent();
            var grouped = new Dictionary<AITemplateCategory, List<AITransformationTemplate>>();

            foreach (var template in templates)
            {
                if (template.Category == null)
                {
                    // Create an "Uncategorized" category
                    var uncategorized = new AITemplateCategory { Id = 0, Name = "Uncategorized" };
                    if (!grouped.ContainsKey(uncategorized))
                    {
                        grouped[uncategorized] = new List<AITransformationTemplate>();
                    }
                    grouped[uncategorized].Add(template);
                }
                else
                {
                    if (!grouped.ContainsKey(template.Category))
                    {
                        grouped[template.Category] = new List<AITransformationTemplate>();
                    }
                    grouped[template.Category].Add(template);
                }
            }

            return grouped;
        }

        /// <summary>
        /// Check if a template is available for the current event
        /// </summary>
        public bool IsTemplateAvailable(int templateId)
        {
            var availableTemplates = GetTemplatesForCurrentEvent();
            return availableTemplates.Any(t => t.Id == templateId);
        }

        #endregion
    }
}