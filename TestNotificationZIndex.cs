// Test commands to verify notifications appear above overlays
// Run these in C# Interactive or Immediate Window

// 1. Show a test notification
Photobooth.Services.SyncNotificationService.Instance.ShowNotification(
    "Test: This notification should appear above all overlays",
    Photobooth.Services.SyncNotificationService.NotificationType.Success,
    5
);

// 2. Show multiple notifications in sequence
Photobooth.Services.SyncNotificationService.Instance.ShowNotification(
    "First notification",
    Photobooth.Services.SyncNotificationService.NotificationType.Info,
    3
);

Photobooth.Services.SyncNotificationService.Instance.ShowNotification(
    "Second notification (queued)",
    Photobooth.Services.SyncNotificationService.NotificationType.Warning,
    3
);

Photobooth.Services.SyncNotificationService.Instance.ShowNotification(
    "Third notification (also queued)",
    Photobooth.Services.SyncNotificationService.NotificationType.Error,
    3
);

// 3. Test with settings overlay open
// First open the settings overlay, then run:
Photobooth.Services.SyncNotificationService.Instance.ShowNotification(
    "This should appear ABOVE the settings overlay",
    Photobooth.Services.SyncNotificationService.NotificationType.Warning,
    5
);

// 4. Simulate sync notifications
var syncService = Photobooth.Services.PhotoBoothSyncService.Instance;
syncService.RaiseTemplateUpdating("Test Template", "Template updated from cloud");
syncService.RaiseSettingsUpdating("Settings synced from cloud");
syncService.RaiseEventUpdating("Birthday Party", "Event data refreshed");

// 5. Test different notification types
foreach (var type in Enum.GetValues(typeof(Photobooth.Services.SyncNotificationService.NotificationType)))
{
    Photobooth.Services.SyncNotificationService.Instance.ShowNotification(
        $"Test {type} notification",
        (Photobooth.Services.SyncNotificationService.NotificationType)type,
        2
    );
    System.Threading.Thread.Sleep(2500);
}