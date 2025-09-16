// Script to fix event-template associations after templates were re-imported with new IDs
// Run this in C# Interactive or Immediate Window

// Get the current template mappings
var templateDb = new Photobooth.Database.TemplateDatabase();
var templates = templateDb.GetAllTemplates();

// Create a name-to-ID mapping
var templateNameToId = new Dictionary<string, int>();
foreach (var template in templates)
{
    templateNameToId[template.Name] = template.Id;
    Console.WriteLine($"Template: {template.Name} -> ID: {template.Id}");
}

// Load events and update template references
var eventService = Photobooth.Services.EventService.Instance;
var events = eventService.GetAllEvents();

foreach (var evt in events)
{
    bool updated = false;

    // Check each template in the event
    foreach (var templateConfig in evt.Templates)
    {
        // Try to find the template by name
        if (!string.IsNullOrEmpty(templateConfig.TemplateName))
        {
            if (templateNameToId.ContainsKey(templateConfig.TemplateName))
            {
                var newId = templateNameToId[templateConfig.TemplateName];
                if (templateConfig.TemplateId != newId)
                {
                    Console.WriteLine($"Event {evt.EventName}: Updating template '{templateConfig.TemplateName}' from ID {templateConfig.TemplateId} to {newId}");
                    templateConfig.TemplateId = newId;
                    updated = true;
                }
            }
            else
            {
                Console.WriteLine($"Event {evt.EventName}: Template '{templateConfig.TemplateName}' not found in database");
            }
        }
    }

    // Save the event if any templates were updated
    if (updated)
    {
        eventService.SaveEvent(evt);
        Console.WriteLine($"Event {evt.EventName} updated successfully");
    }
}

Console.WriteLine("Event-template associations fixed!");

// Force a sync to upload the corrected events
var syncService = Photobooth.Services.PhotoBoothSyncService.Instance;
await syncService.SyncAsync();
Console.WriteLine("Sync completed");