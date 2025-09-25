using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CameraControl.Devices;
using Photobooth.Controls;
using Photobooth.Database;

namespace Photobooth.Services
{
    /// <summary>
    /// Service that handles AI template selection workflow during photo sessions
    /// Follows clean architecture - all business logic isolated from UI
    /// </summary>
    public class AITemplateSelectionService
    {
        #region Singleton
        private static AITemplateSelectionService _instance;
        private static readonly object _lock = new object();

        public static AITemplateSelectionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AITemplateSelectionService();
                        }
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Events
        /// <summary>
        /// Raised when a template is selected by the user
        /// </summary>
        public event EventHandler<AITemplateSelectionSelectedEventArgs> TemplateSelected;

        /// <summary>
        /// Raised when template selection is skipped/cancelled
        /// </summary>
        public event EventHandler TemplateSelectionSkipped;

        /// <summary>
        /// Raised when selection overlay should be shown
        /// </summary>
        public event EventHandler<ShowAITemplateSelectionEventArgs> ShowSelectionRequested;
        #endregion

        #region Private Fields
        private readonly EventAITemplateService _eventAITemplateService;
        private AITransformationTemplate _selectedTemplate;
        private bool _isSelectionInProgress;
        #endregion

        #region Constructor
        private AITemplateSelectionService()
        {
            _eventAITemplateService = EventAITemplateService.Instance;
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Determines if AI template selection should be shown for the current session
        /// </summary>
        public bool ShouldShowTemplateSelection(EventData currentEvent)
        {
            Log.Debug("================== AI Template Selection Check ==================");

            if (currentEvent == null)
            {
                Log.Debug("AITemplateSelectionService: No current event, skipping template selection");
                return false;
            }

            Log.Debug($"AITemplateSelectionService: Current event: {currentEvent.Name} (ID: {currentEvent.Id})");

            // Set the current event context
            _eventAITemplateService.SetCurrentEvent(currentEvent.Id);
            Log.Debug($"AITemplateSelectionService: Set event context to ID: {currentEvent.Id}");

            // Check if AI transformation is enabled for this event
            bool aiEnabled = _eventAITemplateService.IsAITransformationEnabled();
            Log.Debug($"AITemplateSelectionService: AI transformation enabled for event: {aiEnabled}");

            if (!aiEnabled)
            {
                Log.Debug("AITemplateSelectionService: AI transformation not enabled for this event - returning false");
                return false;
            }

            var eventSettings = _eventAITemplateService.GetCurrentEventSettings();
            Log.Debug($"AITemplateSelectionService: Event settings retrieved:");
            Log.Debug($"  - EventId: {eventSettings.EventId}");
            Log.Debug($"  - EnableAITransformation: {eventSettings.EnableAITransformation}");
            Log.Debug($"  - ShowSelectionOverlay: {eventSettings.ShowSelectionOverlay}");
            Log.Debug($"  - AutoApplyDefault: {eventSettings.AutoApplyDefault}");
            Log.Debug($"  - DefaultTemplateId: {eventSettings.DefaultTemplateId}");

            var availableTemplates = _eventAITemplateService.GetTemplatesForCurrentEvent();
            Log.Debug($"AITemplateSelectionService: Available templates: {availableTemplates?.Count ?? 0}");
            if (availableTemplates != null && availableTemplates.Count > 0)
            {
                foreach (var template in availableTemplates.Take(3))
                {
                    Log.Debug($"  - Template: {template.Name} (ID: {template.Id})");
                }
            }

            var defaultTemplate = _eventAITemplateService.GetDefaultTemplate();
            Log.Debug($"AITemplateSelectionService: Default template: {defaultTemplate?.Name ?? "None"} (ID: {defaultTemplate?.Id})");

            // Show selection overlay if:
            // 1. ShowSelectionOverlay is enabled AND
            // 2. There are templates available AND
            // 3. Either AutoApplyDefault is false OR there's no default template
            bool shouldShow = eventSettings.ShowSelectionOverlay &&
                             availableTemplates != null &&
                             availableTemplates.Count > 0 &&
                             (!eventSettings.AutoApplyDefault || defaultTemplate == null);

            Log.Debug($"AITemplateSelectionService: Decision factors:");
            Log.Debug($"  - ShowSelectionOverlay: {eventSettings.ShowSelectionOverlay}");
            Log.Debug($"  - Has templates: {availableTemplates != null && availableTemplates.Count > 0}");
            Log.Debug($"  - AutoApplyDefault: {eventSettings.AutoApplyDefault}");
            Log.Debug($"  - Has default template: {defaultTemplate != null}");
            Log.Debug($"AITemplateSelectionService: FINAL DECISION - Should show: {shouldShow}");
            Log.Debug("==================================================================");

            return shouldShow;
        }

        /// <summary>
        /// Initiates the template selection process
        /// </summary>
        public async Task<AITemplateSelectionResult> RequestTemplateSelectionAsync(EventData currentEvent)
        {
            if (_isSelectionInProgress)
            {
                Log.Debug("AITemplateSelectionService: Selection already in progress");
                return new AITemplateSelectionResult { Skipped = true };
            }

            try
            {
                _isSelectionInProgress = true;
                _selectedTemplate = null;

                if (!ShouldShowTemplateSelection(currentEvent))
                {
                    // Check if we should auto-apply default
                    var eventSettings = _eventAITemplateService.GetCurrentEventSettings();
                    if (eventSettings.AutoApplyDefault)
                    {
                        var defaultTemplate = _eventAITemplateService.GetDefaultTemplate();
                        if (defaultTemplate != null)
                        {
                            Log.Debug($"AITemplateSelectionService: Auto-applying default template: {defaultTemplate.Name}");
                            return new AITemplateSelectionResult
                            {
                                Selected = true,
                                Template = defaultTemplate,
                                AutoApplied = true
                            };
                        }
                    }

                    return new AITemplateSelectionResult { Skipped = true };
                }

                // Request UI to show selection overlay
                var templates = _eventAITemplateService.GetTemplatesForCurrentEvent();
                ShowSelectionRequested?.Invoke(this, new ShowAITemplateSelectionEventArgs
                {
                    Event = currentEvent,
                    Templates = templates
                });

                // Wait for user selection (this would typically be handled via events)
                // The UI will call NotifyTemplateSelected or NotifySelectionSkipped

                // For now, return a pending result
                // The actual selection will be communicated via events
                return new AITemplateSelectionResult { Pending = true };
            }
            finally
            {
                _isSelectionInProgress = false;
            }
        }

        /// <summary>
        /// Notify the service that a template was selected
        /// </summary>
        public void NotifyTemplateSelected(AITransformationTemplate template)
        {
            _selectedTemplate = template;
            _isSelectionInProgress = false;

            Log.Debug($"AITemplateSelectionService: Template selected: {template?.Name}");

            TemplateSelected?.Invoke(this, new AITemplateSelectionSelectedEventArgs
            {
                Template = template
            });
        }

        /// <summary>
        /// Notify the service that selection was skipped
        /// </summary>
        public void NotifySelectionSkipped()
        {
            _selectedTemplate = null;
            _isSelectionInProgress = false;

            Log.Debug("AITemplateSelectionService: Template selection skipped");

            TemplateSelectionSkipped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gets the currently selected template
        /// </summary>
        public AITransformationTemplate GetSelectedTemplate()
        {
            return _selectedTemplate;
        }

        /// <summary>
        /// Checks if AI transformation should be applied during photo processing
        /// </summary>
        public bool ShouldApplyAITransformation(EventData currentEvent)
        {
            if (currentEvent == null) return false;

            _eventAITemplateService.SetCurrentEvent(currentEvent.Id);

            // Check if AI is enabled
            if (!_eventAITemplateService.IsAITransformationEnabled())
                return false;

            // Check if we have a selected template or should auto-apply
            var eventSettings = _eventAITemplateService.GetCurrentEventSettings();
            if (_selectedTemplate != null)
                return true;

            if (eventSettings.AutoApplyDefault && _eventAITemplateService.GetDefaultTemplate() != null)
                return true;

            return false;
        }

        /// <summary>
        /// Gets the template to apply for transformation
        /// </summary>
        public AITransformationTemplate GetTemplateForTransformation(EventData currentEvent)
        {
            if (currentEvent == null) return null;

            _eventAITemplateService.SetCurrentEvent(currentEvent.Id);

            // Return selected template if available
            if (_selectedTemplate != null)
                return _selectedTemplate;

            // Otherwise check for auto-apply default
            var eventSettings = _eventAITemplateService.GetCurrentEventSettings();
            if (eventSettings.AutoApplyDefault)
            {
                return _eventAITemplateService.GetDefaultTemplate();
            }

            return null;
        }

        /// <summary>
        /// Clears the current selection
        /// </summary>
        public void ClearSelection()
        {
            _selectedTemplate = null;
            _isSelectionInProgress = false;
        }

        #endregion
    }

    #region Event Args

    public class AITemplateSelectionSelectedEventArgs : EventArgs
    {
        public AITransformationTemplate Template { get; set; }
    }

    public class ShowAITemplateSelectionEventArgs : EventArgs
    {
        public EventData Event { get; set; }
        public System.Collections.Generic.List<AITransformationTemplate> Templates { get; set; }
    }

    public class AITemplateSelectionResult
    {
        public bool Selected { get; set; }
        public bool Skipped { get; set; }
        public bool Pending { get; set; }
        public bool AutoApplied { get; set; }
        public AITransformationTemplate Template { get; set; }
    }

    #endregion
}