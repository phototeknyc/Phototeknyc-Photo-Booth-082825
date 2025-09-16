# Testing Cloud Sync Feature

## Prerequisites
1. AWS S3 bucket created
2. AWS credentials (Access Key, Secret Key) with read/write permissions
3. Two photobooth instances (can be same machine with different settings folders)

## Test Setup

### 1. Configure First Photobooth
1. Open Settings Overlay (gear icon)
2. Navigate to "Cloud Sync" section
3. Enter:
   - S3 Access Key
   - S3 Secret Key
   - S3 Bucket Name
   - S3 Region (e.g., us-east-1)
4. Enable:
   - Enable Cloud Sync ✓
   - Auto Sync on Startup ✓
   - Sync Templates ✓
   - Sync Settings ✓
   - Sync Events ✓
5. Set Sync Interval to 1 minute (for testing)
6. Click "Test Connection" - should show "✓ Success"
7. Click "Sync Now" to push initial manifest

### 2. Test Template Sync
1. Create a new template in Designer
2. Save template with name "Test Sync Template"
3. Click "Sync Now" in settings
4. Check AWS S3 Console:
   - Navigate to your bucket
   - Look for: `photobooth-sync/templates/template_X.json`
   - Verify template files and assets uploaded

### 3. Test on Second Instance
1. On another machine (or same machine with different user profile):
2. Configure same AWS credentials
3. Click "Sync Now"
4. Verify "Test Sync Template" appears in template list

### 4. Test Auto-Sync
1. On first machine, modify the template
2. Wait for sync interval (1 minute) or click "Sync Now"
3. On second machine, wait for auto-sync
4. Verify you see notification: "Updating template 'Test Sync Template' from cloud sync..."
5. Check template has updated

### 5. Test Settings Sync
1. On first machine, change a setting like:
   - Countdown Seconds: 10
   - Print Copies: 3
2. Click "Sync Now"
3. On second machine, after sync, verify settings updated

### 6. Test Event Sync
1. Create a new event "Test Sync Event"
2. Attach template to event
3. Click "Sync Now"
4. Verify on second machine event appears with template

## Monitoring Sync Activity

### Check Debug Output
Run with debug console to see sync messages:
```
PhotoBoothSyncService: Starting sync operation
PhotoBoothSyncService: Uploaded template template_5.json
PhotoBoothSyncService: Auto-sync completed successfully
```

### Check S3 Bucket Structure
```
your-bucket/
├── photobooth-sync/
│   ├── sync-manifest.json
│   ├── templates/
│   │   ├── template_1.json
│   │   ├── template_2.json
│   │   └── assets/
│   │       ├── template_1_background.jpg
│   │       └── template_1_item_1.png
│   ├── settings/
│   │   └── settings.json
│   └── events/
│       └── Test_Sync_Event_1.json
```

### Check Notifications
Watch for toast notifications:
- Yellow warning: "Updating template..." (3 seconds)
- Green success: "Sync completed: X templates, Y events synced"
- Red error: "Sync error: [message]"

## Troubleshooting

### If Sync Isn't Working:

1. **Check Credentials:**
   ```powershell
   # In PowerShell, verify environment variables:
   echo $env:AWS_ACCESS_KEY_ID
   echo $env:AWS_SECRET_ACCESS_KEY
   echo $env:S3_BUCKET_NAME
   ```

2. **Check AWS Permissions:**
   Ensure IAM user has these permissions:
   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Action": [
           "s3:GetObject",
           "s3:PutObject",
           "s3:DeleteObject",
           "s3:ListBucket"
         ],
         "Resource": [
           "arn:aws:s3:::your-bucket-name/*",
           "arn:aws:s3:::your-bucket-name"
         ]
       }
     ]
   }
   ```

3. **Check Sync Status:**
   - Last Sync Time shown in overlay
   - Booth ID displayed in status
   - "Ready to Sync" indicator

4. **Force Manifest Reset:**
   Delete local manifest to force full resync:
   ```
   %APPDATA%\PhotoBooth\Sync\sync-manifest.json
   ```

## Testing Conflict Resolution

1. Disconnect network on Machine B
2. Modify same template on both machines
3. Reconnect Machine B
4. Sync - newest modification wins (default)

## Performance Testing

1. Create 20+ templates with images
2. Time initial sync
3. Modify 5 templates
4. Time incremental sync
5. Verify only changed items sync

## Testing Edge Cases

### Test Empty Secret Key
1. Clear S3 Secret Key field
2. Try to sync - should fail with error
3. Re-enter key - should work

### Test Network Interruption
1. Start sync
2. Disconnect network mid-sync
3. Verify error notification
4. Reconnect and retry

### Test Large Files
1. Create template with 10MB background image
2. Sync and verify upload completes
3. Check second machine downloads correctly

### Test Simultaneous Edits
1. Edit same template on both machines within same minute
2. Both sync
3. Verify last-saved wins, notification shows

## Success Criteria

✅ Templates sync between machines
✅ Settings sync and apply
✅ Events sync with template associations
✅ Auto-sync runs at configured interval
✅ Notifications appear for updates
✅ Conflicts resolve by newest-wins
✅ Large files transfer successfully
✅ Credentials persist between sessions
✅ Sync continues after network interruption