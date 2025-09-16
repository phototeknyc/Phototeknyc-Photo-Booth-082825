# Testing Sync by Deleting Local Content

## Safe Testing Procedure

### 1. **Backup First (Important!)**
Before testing deletion, backup your templates and events:

```powershell
# Backup templates database
copy "%APPDATA%\PhotoBooth\Templates\templates.db" "%APPDATA%\PhotoBooth\Templates\templates_backup.db"

# Backup events database
copy "%APPDATA%\PhotoBooth\events.db" "%APPDATA%\PhotoBooth\events_backup.db"

# Backup template files
xcopy "%APPDATA%\PhotoBooth\Templates" "%APPDATA%\PhotoBooth\Templates_Backup" /E /I
```

### 2. **Verify Cloud Has Your Data**
1. Open AWS S3 Console
2. Navigate to your bucket → `photobooth-sync/`
3. Verify these exist:
   - `sync-manifest.json` (the manifest)
   - `templates/` folder with template JSON files
   - `templates/assets/` with images
   - `events/` folder with event JSON files

### 3. **Test Template Sync from Cloud**

#### Method A: Delete via UI
1. Open Template Browser
2. Delete a template (note its name)
3. Go to Settings → Cloud Sync
4. Click "Sync Now"
5. Template should reappear with notification: "Updating template 'X' from cloud sync..."

#### Method B: Delete Database Entry
```sql
-- In DB Browser for SQLite
-- Open %APPDATA%\PhotoBooth\Templates\templates.db
DELETE FROM Templates WHERE Name = 'Test Template';
```
Then sync to restore.

#### Method C: Clean Slate Test
```powershell
# WARNING: This deletes ALL local templates!
# Make sure you have backups first

# Stop the application
# Delete local template database
del "%APPDATA%\PhotoBooth\Templates\templates.db"

# Delete template files
del "%APPDATA%\PhotoBooth\Templates\*.json"

# Restart application
# Click "Sync Now" - all templates should download
```

### 4. **Test Event Sync from Cloud**

#### Delete Specific Event
```sql
-- In DB Browser for SQLite
-- Open %APPDATA%\PhotoBooth\events.db
DELETE FROM Events WHERE EventName = 'Test Event';
```

#### Clean Events Test
```powershell
# Delete all local events (after backup!)
del "%APPDATA%\PhotoBooth\events.db"

# Restart and sync
```

### 5. **Test Settings Sync from Cloud**
1. Note current settings (like CountdownSeconds)
2. Change settings to different values
3. Delete local settings:
```powershell
# Reset to defaults
del "%APPDATA%\PhotoBooth\user.config"
```
4. Restart app and sync
5. Settings should restore from cloud

## Expected Behavior

### When You Delete Locally and Sync:

✅ **Templates**: Should download from cloud and recreate
- Template appears in list
- Images/assets download
- "Updating template..." notification shows

✅ **Events**: Should download and recreate
- Event appears in event list
- Template associations maintained
- Event settings restored

✅ **Settings**: Should apply from cloud
- Previous values restored
- No notification (happens silently)

### What WON'T Sync Back:
❌ Photos taken (session photos)
❌ Print history
❌ Local gallery items
❌ Temporary files

## Monitoring the Sync

### Watch Debug Output
Enable debug logging to see what's happening:
```csharp
// In immediate window or add to code
System.Diagnostics.Debug.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
```

You'll see:
```
PhotoBoothSyncService: Downloaded template template_5.json
PhotoBoothSyncService: Importing new template 5
PhotoBoothSyncService: Downloaded asset background for template 5
PhotoBoothSyncService: Successfully imported template
```

### Check Notifications
You should see yellow toast notifications:
- "Updating template 'Name' from cloud sync..."
- "Adding new event 'Name' from cloud sync..."

## Troubleshooting

### Templates Don't Reappear?
1. Check S3 has the template files
2. Verify manifest lists the templates:
   - Download `sync-manifest.json` from S3
   - Open in text editor
   - Look for template entries
3. Check local manifest isn't blocking:
   ```powershell
   del "%APPDATA%\PhotoBooth\Sync\sync-manifest.json"
   ```
4. Force full resync

### Events Don't Sync?
1. Verify "Sync Events" is enabled in settings
2. Check event JSON files exist in S3
3. Check event has templates that exist locally

### Partial Sync?
If some items sync but not others:
1. Check individual toggle settings (Sync Templates, Sync Events, etc.)
2. Look for errors in debug output
3. Verify file permissions in S3

## Recovery

### If Something Goes Wrong:
```powershell
# Restore templates database
copy "%APPDATA%\PhotoBooth\Templates\templates_backup.db" "%APPDATA%\PhotoBooth\Templates\templates.db" /Y

# Restore events
copy "%APPDATA%\PhotoBooth\events_backup.db" "%APPDATA%\PhotoBooth\events.db" /Y

# Restore template files
xcopy "%APPDATA%\PhotoBooth\Templates_Backup" "%APPDATA%\PhotoBooth\Templates" /E /I /Y
```

## Test Checklist

- [ ] Backed up templates database
- [ ] Backed up events database
- [ ] Verified S3 has content
- [ ] Deleted local template
- [ ] Clicked "Sync Now"
- [ ] Template reappeared
- [ ] Notification showed
- [ ] Template images loaded
- [ ] Deleted local event
- [ ] Event reappeared with templates
- [ ] Settings restored after deletion