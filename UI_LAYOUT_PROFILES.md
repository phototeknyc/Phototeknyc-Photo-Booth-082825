# UI Layout Profile System

## Overview
The UI Layout Profile System allows you to save and manage multiple UI layouts optimized for different screen sizes and devices. Each profile contains layouts for both portrait and landscape orientations, making it easy to switch between different screen configurations.

## Features

### 🎯 Profile Management
- **Save Multiple Profiles** - Create unlimited profiles for different screens
- **Quick Switching** - Instantly switch between profiles
- **Import/Export** - Share profiles between installations
- **Predefined Profiles** - Built-in profiles for common devices
- **Database Storage** - All profiles saved in SQLite database

### 📱 Device Configurations
Each profile stores:
- Screen resolution (e.g., 1920x1080, 2736x1824)
- Device type (Tablet, Kiosk, Desktop, Surface)
- Screen diagonal size
- Aspect ratio
- Touch capability
- DPI settings
- Preferred orientation

### 🔄 Orientation Support
- Separate layouts for portrait and landscape
- Automatic orientation detection
- Smooth transitions between orientations
- Optimized element positioning for each orientation

## Predefined Profiles

### 1. Surface Pro
- Resolution: 2736x1824
- Size: 12.3"
- Aspect: 3:2
- Touch: Enabled
- DPI: 267

### 2. iPad Pro 12.9"
- Resolution: 2732x2048
- Size: 12.9"
- Aspect: 4:3
- Touch: Enabled
- DPI: 264

### 3. Kiosk Full HD
- Resolution: 1920x1080
- Size: 32"
- Aspect: 16:9
- Touch: Enabled
- DPI: 96

### 4. Kiosk 4K
- Resolution: 3840x2160
- Size: 43"
- Aspect: 16:9
- Touch: Enabled
- DPI: 103

### 5. Portrait Kiosk
- Resolution: 1080x1920
- Size: 32"
- Aspect: 9:16
- Touch: Enabled
- DPI: 96

### 6. Desktop
- Resolution: 1920x1080
- Size: 24"
- Aspect: 16:9
- Touch: Disabled
- DPI: 96

## Database Schema

### UILayoutProfiles Table
```sql
- Id (Primary Key)
- Name
- Description
- Category (Tablet/Kiosk/Desktop/Custom)
- DeviceType
- ResolutionWidth/Height
- DiagonalSize
- AspectRatio
- IsTouchEnabled
- DPI
- PreferredOrientation
- IsDefault
- IsActive
- ThumbnailPath
- CreatedDate
- LastUsedDate
- Author
- Version
- IsLocked
- Notes
- Metadata (JSON)
```

### ProfileLayoutMappings Table
```sql
- Id (Primary Key)
- ProfileId (Foreign Key)
- LayoutId (Foreign Key)
- Orientation (Portrait/Landscape)
```

## API Usage

### Creating a Profile
```csharp
var profile = new UILayoutProfile
{
    Name = "My Custom Kiosk",
    Description = "Optimized for 32-inch touch kiosk",
    Category = "Kiosk",
    ScreenConfig = new ScreenConfiguration
    {
        DeviceType = "Kiosk",
        Resolution = new Size(1920, 1080),
        DiagonalSize = 32,
        AspectRatio = 16.0 / 9.0,
        IsTouchEnabled = true,
        DPI = 96,
        PreferredOrientation = ScreenOrientation.Landscape
    }
};

// Add layouts for each orientation
profile.Layouts["Portrait"] = CreatePortraitLayout();
profile.Layouts["Landscape"] = CreateLandscapeLayout();

// Save to database
database.SaveProfile(profile);
```

### Loading and Activating a Profile
```csharp
// Get all profiles
var profiles = database.GetAllProfiles();

// Get profiles by category
var kioskProfiles = database.GetProfilesByCategory("Kiosk");

// Activate a profile
database.SetActiveProfile(profileId);

// Get active profile
var activeProfile = database.GetActiveProfile();
```

### Quick Switching in UI
```csharp
// In PhotoboothTouchModernRefactored
var layoutService = new UILayoutService();

// Get available profiles
var profiles = layoutService.GetAvailableProfiles();

// Switch to a profile
layoutService.SwitchToProfile(profileId, this, MainGrid);
```

### Import/Export Profiles
```csharp
// Export profile
database.ExportProfile(profileId, "C:\\Profiles\\MyProfile.json");

// Import profile
var importedProfile = database.ImportProfile("C:\\Profiles\\SharedProfile.json");
```

## Integration with UI Customizer

### Profile Selection in Customizer
1. Open UI Customizer from Surface dashboard
2. Click "Profiles" button in toolbar
3. Select target profile from dropdown
4. Design layouts for portrait/landscape
5. Save changes to profile

### Profile Metadata
- **Author** - Designer name
- **Version** - Profile version number
- **Tags** - Searchable tags
- **IsLocked** - Prevent accidental changes
- **Notes** - Additional documentation

## Automatic Profile Selection

The system can automatically select profiles based on:
1. Current screen resolution
2. Device type detection
3. Touch capability
4. User preferences

## Benefits

### For Developers
- No code changes needed for different screens
- Automatic responsive scaling
- Easy profile management
- Version control for layouts

### For Users
- Quick adaptation to different venues
- Professional layouts for each device
- Easy switching between setups
- Import/export for sharing

### For Events
- Pre-configure for venue screens
- Quick setup at location
- Consistent experience across devices
- Backup profiles for reliability

## Workflow Example

### Setting Up for an Event
1. **Before Event**
   - Create profile for venue's screen
   - Design custom layouts
   - Test on similar device
   - Export profile as backup

2. **At Venue**
   - Import profile if needed
   - Activate venue profile
   - Fine-tune if necessary
   - Lock profile to prevent changes

3. **During Event**
   - Profile automatically applies
   - Layouts optimize for screen
   - Touch/mouse handled correctly
   - Consistent experience

4. **After Event**
   - Export successful profile
   - Share with team
   - Archive for future use
   - Build profile library

## Best Practices

### Profile Naming
- Include device type: "iPad_Pro_12.9"
- Include resolution: "Kiosk_1920x1080"
- Include venue: "Marriott_Ballroom"
- Version profiles: "v2.1"

### Profile Organization
- Group by category
- Tag with keywords
- Document in notes
- Lock production profiles

### Testing
- Test on actual device if possible
- Verify touch targets
- Check text readability
- Test both orientations

### Maintenance
- Regular profile backups
- Version control changes
- Document modifications
- Clean unused profiles

## Troubleshooting

### Profile Not Loading
- Check if profile is active
- Verify orientation matches
- Check database connection
- Review debug logs

### Layout Issues
- Verify screen resolution
- Check DPI settings
- Test anchor points
- Validate constraints

### Performance
- Limit elements per layout
- Optimize images
- Test on target hardware
- Monitor memory usage

## Future Enhancements
- Cloud profile sync
- Auto-detect optimal profile
- Profile marketplace
- A/B testing support
- Analytics per profile
- Remote profile deployment

## Conclusion
The UI Layout Profile System provides comprehensive management of UI layouts for different screen configurations. By saving profiles optimized for specific devices, you can quickly deploy the photobooth application to any venue or screen size with confidence that the UI will look and function perfectly.