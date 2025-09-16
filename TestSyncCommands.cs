// Test commands to run in C# Interactive or Immediate Window

// 1. Check sync status
var syncService = Photobooth.Services.PhotoBoothSyncService.Instance;
var status = syncService.GetSyncStatus();
Console.WriteLine($"Sync Enabled: {status.IsEnabled}");
Console.WriteLine($"Auto-Sync: {status.IsAutoSyncEnabled}");
Console.WriteLine($"Last Sync: {status.LastSyncTime}");
Console.WriteLine($"Next Sync: {status.NextSyncTime}");
Console.WriteLine($"Booth ID: {status.BoothId}");

// 2. Test connection
bool connected = await syncService.TestConnectionAsync();
Console.WriteLine($"Connection test: {connected}");

// 3. Force sync now
var result = await syncService.SyncAsync();
Console.WriteLine($"Sync result: {result.Success}");
Console.WriteLine($"Templates synced: {result.TemplatesSynced}");
Console.WriteLine($"Events synced: {result.EventsSynced}");

// 4. Change sync interval to 2 minutes
syncService.SetSyncInterval(2);

// 5. Enable/disable auto-sync
syncService.SetAutoSyncEnabled(true);

// 6. Check what will sync
var localSettings = await Photobooth.Services.SettingsSyncService.Instance.GetLocalSettingsAsync();
Console.WriteLine($"Local settings to sync: {localSettings.Count}");

// 7. Create test template for sync
var templateDb = new Photobooth.Database.TemplateDatabase();
var testTemplate = new Photobooth.Database.TemplateData
{
    Name = "Sync Test " + DateTime.Now.ToString("HH:mm:ss"),
    Description = "Created for sync testing",
    CanvasWidth = 800,
    CanvasHeight = 600,
    BackgroundColor = "#FF0000"
};
templateDb.SaveTemplate(testTemplate);
Console.WriteLine($"Created test template: {testTemplate.Name}");

// 8. Monitor sync events
syncService.SyncStarted += (s, e) => Console.WriteLine("Sync started!");
syncService.SyncCompleted += (s, e) => Console.WriteLine($"Sync completed: {e.Result?.Success}");
syncService.SyncProgress += (s, e) => Console.WriteLine($"Progress: {e.Message} - {e.ProgressPercentage}%");
syncService.TemplateUpdating += (s, e) => Console.WriteLine($"Updating template: {e.TemplateName}");

// 9. Check manifest
var manifest = await syncService.GetRemoteManifestAsync();
if (manifest != null)
{
    Console.WriteLine($"Remote manifest has {manifest.Items.Count} items");
    Console.WriteLine($"Last modified by: {manifest.ModifiedBy}");
}

// 10. Test notification
Photobooth.Services.SyncNotificationService.Instance.ShowNotification(
    "Test sync notification",
    Photobooth.Services.SyncNotificationService.NotificationType.Success,
    5
);