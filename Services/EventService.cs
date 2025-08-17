using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Photobooth.Database;

namespace Photobooth.Services
{
    public class EventService
    {
        private readonly TemplateDatabase database;
        
        public EventService()
        {
            database = new TemplateDatabase();
        }
        
        #region Event Management
        
        public int CreateEvent(string eventName, string description = "", string eventType = "", 
            string location = "", DateTime? eventDate = null, TimeSpan? startTime = null, 
            TimeSpan? endTime = null, string hostName = "", string contactEmail = "", 
            string contactPhone = "")
        {
            try
            {
                var eventData = new EventData
                {
                    Name = eventName,
                    Description = description,
                    EventType = eventType,
                    Location = location,
                    EventDate = eventDate,
                    StartTime = startTime,
                    EndTime = endTime,
                    HostName = hostName,
                    ContactEmail = contactEmail,
                    ContactPhone = contactPhone
                };
                
                int eventId = database.CreateEvent(eventData);
                
                // Automatically create event gallery in S3
                if (eventId > 0)
                {
                    _ = Task.Run(async () => 
                    {
                        await CreateEventGalleryAsync(eventName, eventId);
                    });
                }
                
                return eventId;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        
        private async Task CreateEventGalleryAsync(string eventName, int eventId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Creating gallery for event: {eventName} (ID: {eventId})");
                
                // Get the cloud share service
                var shareService = CloudShareProvider.GetShareService();
                
                if (shareService is CloudShareServiceRuntime runtimeService)
                {
                    // Create the event gallery (passwordless by default)
                    var (galleryUrl, password) = await runtimeService.CreateEventGalleryAsync(eventName, eventId, usePassword: false);
                    
                    if (!string.IsNullOrEmpty(galleryUrl))
                    {
                        System.Diagnostics.Debug.WriteLine($"Event gallery created: {galleryUrl}");
                        
                        // Silently save gallery info to database
                        var eventData = database.GetEvent(eventId);
                        if (eventData != null)
                        {
                            eventData.GalleryUrl = galleryUrl;
                            eventData.GalleryPassword = password; // Will be empty for passwordless
                            database.UpdateEvent(eventId, eventData);
                            System.Diagnostics.Debug.WriteLine($"Gallery info saved to database for event {eventId}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to create event gallery - no URL returned");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("CloudShareServiceRuntime not available - gallery not created");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating event gallery: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get gallery info for an event
        /// </summary>
        public (string url, string password) GetEventGalleryInfo(int eventId)
        {
            try
            {
                var eventData = database.GetEvent(eventId);
                if (eventData != null)
                {
                    return (eventData.GalleryUrl, eventData.GalleryPassword);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting gallery info: {ex.Message}");
            }
            return (null, null);
        }
        
        /// <summary>
        /// Generate password for existing gallery
        /// </summary>
        public async Task<string> AddPasswordToGallery(int eventId)
        {
            try
            {
                var eventData = database.GetEvent(eventId);
                if (eventData != null && !string.IsNullOrEmpty(eventData.GalleryUrl))
                {
                    // Generate password
                    string password = GenerateGalleryPassword(eventData.Name);
                    
                    // Update the gallery with password
                    await CreateEventGalleryAsync(eventData.Name, eventId, usePassword: true);
                    
                    // Save password to database
                    eventData.GalleryPassword = password;
                    database.UpdateEvent(eventId, eventData);
                    
                    return password;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding password: {ex.Message}");
            }
            return null;
        }
        
        private string GenerateGalleryPassword(string eventName)
        {
            if (string.IsNullOrEmpty(eventName))
                return "";
            
            var hashCode = eventName.GetHashCode();
            var password = hashCode.ToString("X");
            return password.Substring(0, Math.Min(4, password.Length));
        }
        
        /// <summary>
        /// Create gallery with optional password
        /// </summary>
        private async Task CreateEventGalleryAsync(string eventName, int eventId, bool usePassword)
        {
            try
            {
                var shareService = CloudShareProvider.GetShareService();
                if (shareService is CloudShareServiceRuntime runtimeService)
                {
                    var (galleryUrl, password) = await runtimeService.CreateEventGalleryAsync(eventName, eventId, usePassword);
                    
                    if (!string.IsNullOrEmpty(galleryUrl))
                    {
                        var eventData = database.GetEvent(eventId);
                        if (eventData != null)
                        {
                            eventData.GalleryUrl = galleryUrl;
                            eventData.GalleryPassword = password;
                            database.UpdateEvent(eventId, eventData);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating gallery with password: {ex.Message}");
            }
        }
        
        public List<EventData> GetAllEvents()
        {
            try
            {
                return database.GetAllEvents();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load events: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<EventData>();
            }
        }
        
        public EventData GetEvent(int eventId)
        {
            try
            {
                return database.GetEvent(eventId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }
        
        public void DeleteEvent(int eventId)
        {
            try
            {
                database.DeleteEvent(eventId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public void UpdateEvent(EventData eventData)
        {
            try
            {
                database.UpdateEvent(eventData.Id, eventData);
                
                // Update the event gallery if it exists
                _ = Task.Run(async () => 
                {
                    await CreateEventGalleryAsync(eventData.Name, eventData.Id);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
        
        #endregion
        
        #region Template Assignment
        
        public void AssignTemplateToEvent(int eventId, int templateId, bool isDefault = false)
        {
            try
            {
                database.AssignTemplateToEvent(eventId, templateId, isDefault);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to assign template to event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public List<TemplateData> GetEventTemplates(int eventId)
        {
            try
            {
                return database.GetEventTemplates(eventId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load event templates: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<TemplateData>();
            }
        }
        
        public TemplateData GetDefaultEventTemplate(int eventId)
        {
            try
            {
                var templates = GetEventTemplates(eventId);
                return templates.FirstOrDefault(); // Default templates are ordered first
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to get default template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }
        
        public void RemoveTemplateFromEvent(int eventId, int templateId)
        {
            try
            {
                database.RemoveTemplateFromEvent(eventId, templateId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to remove template from event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
        
        #endregion
        
        #region Template Duplication
        
        public int DuplicateTemplateForEvent(int templateId, int eventId, string customName = null)
        {
            try
            {
                // Get original template and event info for naming
                var originalTemplate = database.GetTemplate(templateId);
                var eventData = database.GetEvent(eventId);
                
                if (originalTemplate == null || eventData == null)
                {
                    throw new Exception("Template or event not found");
                }
                
                // Create custom name if not provided
                if (string.IsNullOrEmpty(customName))
                {
                    customName = $"{originalTemplate.Name} - {eventData.Name}";
                }
                
                // Duplicate the template
                var newTemplateId = database.DuplicateTemplate(templateId, customName);
                
                // Assign the duplicated template to the event
                database.AssignTemplateToEvent(eventId, newTemplateId, false);
                
                return newTemplateId;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to duplicate template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        
        public int CreateEventSpecificTemplate(int baseTemplateId, int eventId, string eventSpecificName = null)
        {
            try
            {
                var eventData = database.GetEvent(eventId);
                if (eventData == null)
                    throw new Exception("Event not found");
                
                // Create event-specific name
                var templateName = eventSpecificName ?? $"{eventData.Name} Template";
                
                // Duplicate and customize for the event
                var newTemplateId = DuplicateTemplateForEvent(baseTemplateId, eventId, templateName);
                
                // Set as default if it's the first template for this event
                var existingTemplates = GetEventTemplates(eventId);
                if (existingTemplates.Count == 1) // Only the newly created one
                {
                    database.AssignTemplateToEvent(eventId, newTemplateId, true);
                }
                
                return newTemplateId;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create event-specific template: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        
        #endregion
        
        #region Event Statistics
        
        public Dictionary<string, object> GetEventStatistics(int eventId)
        {
            var stats = new Dictionary<string, object>();
            
            try
            {
                var eventData = database.GetEvent(eventId);
                var templates = GetEventTemplates(eventId);
                
                stats["EventName"] = eventData?.Name ?? "Unknown";
                stats["EventType"] = eventData?.EventType ?? "Unknown";
                stats["EventDate"] = eventData?.EventDate?.ToString("yyyy-MM-dd") ?? "Not set";
                stats["Location"] = eventData?.Location ?? "Not set";
                stats["TemplateCount"] = templates.Count;
                stats["HasDefaultTemplate"] = templates.Any();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to get event statistics: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                stats["Error"] = ex.Message;
            }
            
            return stats;
        }
        
        public List<EventData> GetUpcomingEvents(int daysAhead = 30)
        {
            try
            {
                var allEvents = GetAllEvents();
                var cutoffDate = DateTime.Today.AddDays(daysAhead);
                
                return allEvents
                    .Where(e => e.EventDate.HasValue && e.EventDate.Value >= DateTime.Today && e.EventDate.Value <= cutoffDate)
                    .OrderBy(e => e.EventDate)
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to get upcoming events: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<EventData>();
            }
        }
        
        #endregion
        
        #region Event Types
        
        public List<string> GetEventTypes()
        {
            return new List<string>
            {
                "Wedding",
                "Birthday",
                "Anniversary",
                "Corporate",
                "Holiday",
                "Graduation",
                "Baby Shower",
                "Retirement",
                "Fundraiser",
                "Party",
                "Other"
            };
        }
        
        public List<EventData> GetEventsByType(string eventType)
        {
            try
            {
                var allEvents = GetAllEvents();
                return allEvents.Where(e => string.Equals(e.EventType, eventType, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to filter events by type: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<EventData>();
            }
        }
        
        #endregion
        
        #region Quick Event Setup
        
        public int QuickSetupWedding(string coupleName, DateTime weddingDate, string venue, 
            int templateId, string contactEmail = "")
        {
            try
            {
                // Create wedding event
                var eventId = CreateEvent(
                    eventName: $"{coupleName} Wedding",
                    description: $"Wedding celebration for {coupleName}",
                    eventType: "Wedding",
                    location: venue,
                    eventDate: weddingDate,
                    contactEmail: contactEmail
                );
                
                if (eventId > 0)
                {
                    // Create custom template for the wedding
                    CreateEventSpecificTemplate(templateId, eventId, $"{coupleName} Wedding Template");
                }
                
                return eventId;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to setup wedding event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        
        public int QuickSetupBirthday(string birthdayPersonName, DateTime birthdayDate, 
            int age, string location, int templateId)
        {
            try
            {
                // Create birthday event
                var eventId = CreateEvent(
                    eventName: $"{birthdayPersonName}'s {age}th Birthday",
                    description: $"Birthday celebration for {birthdayPersonName}",
                    eventType: "Birthday",
                    location: location,
                    eventDate: birthdayDate
                );
                
                if (eventId > 0)
                {
                    // Create custom template for the birthday
                    CreateEventSpecificTemplate(templateId, eventId, $"{birthdayPersonName}'s Birthday Template");
                }
                
                return eventId;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to setup birthday event: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return -1;
            }
        }
        
        #endregion
    }
}