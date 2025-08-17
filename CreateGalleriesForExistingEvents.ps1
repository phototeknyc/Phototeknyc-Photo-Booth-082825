# PowerShell script to create galleries for existing events
Write-Host "Creating galleries for existing events..." -ForegroundColor Cyan

Add-Type -Path "bin\Debug\Photobooth.exe"
Add-Type -Path "bin\Debug\CameraControl.Devices.dll"
Add-Type -Path "bin\Debug\Amazon.S3.dll"
Add-Type -Path "bin\Debug\Amazon.Core.dll"
Add-Type -Path "bin\Debug\Amazon.Runtime.dll"

# Create service instances
$eventService = New-Object Photobooth.Services.EventService
$database = New-Object Photobooth.Database.TemplateDatabase

# Get all events
$events = $eventService.GetAllEvents()

Write-Host "Found $($events.Count) events" -ForegroundColor Yellow

foreach ($event in $events) {
    $galleryInfo = $eventService.GetEventGalleryInfo($event.Id)
    
    if ([string]::IsNullOrEmpty($galleryInfo.Item1)) {
        Write-Host "Creating gallery for event: $($event.Name)" -ForegroundColor Green
        
        # Trigger gallery creation
        $eventService.CreateEvent(
            "$($event.Name)_temp",
            $event.Description,
            $event.EventType,
            $event.Location,
            $event.EventDate,
            $event.StartTime,
            $event.EndTime,
            $event.HostName,
            $event.ContactEmail,
            $event.ContactPhone
        )
        
        # Delete the temp event
        $tempEvents = $eventService.GetAllEvents() | Where-Object { $_.Name -eq "$($event.Name)_temp" }
        if ($tempEvents) {
            $eventService.DeleteEvent($tempEvents[0].Id)
        }
    }
    else {
        Write-Host "Event '$($event.Name)' already has gallery: $($galleryInfo.Item1)" -ForegroundColor Gray
    }
}

Write-Host "`nDone! Restart the application to see gallery indicators." -ForegroundColor Green