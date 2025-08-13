# Printer Profile Management Guide

## Overview
The Photobooth application now includes comprehensive printer profile management that captures and restores ALL printer driver settings, including advanced DNP printer options like the "Enable 2" cut" setting for 2x6 prints.

## Features

### 1. Save Printer Profile
- Captures complete DEVMODE structure from printer driver
- Includes ALL driver-specific settings (not just standard Windows settings)
- Saves DNP-specific settings like:
  - Enable 2" cut (for 2x6 prints)
  - Color correction modes
  - Print density
  - Overcoat settings
  - Luster finish
  - Custom color adjustments

### 2. Load Printer Profile
- Restores complete printer configuration
- Applies saved DEVMODE directly to printer
- Preserves all advanced driver settings

### 3. Export/Import Profiles
- Export profiles to JSON files for backup
- Share profiles between computers
- Import profiles from other installations

## How to Use

### Saving the DNP "Enable 2" Cut" Setting

1. **Open Photobooth Settings**
   - Navigate to Settings page
   - Go to Print Settings section

2. **Select Your DNP Printer**
   - Choose your DNP printer from the dropdown
   - Verify it shows as "Online"

3. **Configure Advanced Settings**
   - Click "Advanced Settings" button
   - In the DNP driver dialog:
     - Check "Enable 2" cut" for 2x6 prints
     - Configure any other desired settings
   - Click OK to apply

4. **Save the Profile**
   - Click "Save Profile" button
   - The complete driver configuration is captured
   - Settings are saved to application settings

### Loading a Saved Profile

1. **Select Target Printer**
   - Choose the printer from dropdown

2. **Load Profile**
   - Click "Load Profile" button
   - All settings including "Enable 2" cut" are restored
   - Verify by opening Advanced Settings

### Exporting/Importing Profiles

**Export:**
1. Configure and save a profile
2. Click "Export Profile"
3. Choose location to save JSON file
4. File contains all printer settings in portable format

**Import:**
1. Click "Import Profile"
2. Select the JSON profile file
3. Settings are loaded and applied

## Technical Details

### What's Captured
The profile system captures the complete Windows DEVMODE structure which includes:
- Standard print settings (paper size, orientation, quality)
- Driver-specific private data (DNP settings, custom options)
- Binary driver configuration data

### Storage Format
- DEVMODE is captured as binary data
- Converted to Base64 for text storage
- Saved in application settings or JSON files
- Fully preserves all driver-specific settings

### Compatibility
- Profiles are printer-specific
- Best used with same printer model
- DNP profiles work across DNP printers of same series
- Driver version changes may affect compatibility

## Troubleshooting

### Profile Not Loading
- Ensure printer is online and connected
- Verify printer driver is installed
- Check printer model matches saved profile

### Settings Not Applying
- Some settings require printer to be idle
- Close any open print jobs
- Restart printer if necessary

### DNP 2" Cut Not Working
1. Verify media size supports 2" cuts
2. Ensure printer firmware supports feature
3. Check ribbon type is compatible
4. Test with manual configuration first

## Best Practices

1. **Create Multiple Profiles**
   - Save different profiles for different media types
   - Create profiles for 4x6 vs 2x6 prints
   - Keep backup profiles exported

2. **Test After Loading**
   - Always verify settings after loading profile
   - Do a test print to confirm configuration
   - Check Advanced Settings dialog

3. **Document Profiles**
   - Name exported files descriptively
   - Include printer model in filename
   - Note special settings in filename

## Example Workflow for 2x6 Prints

1. Load 2x6 media in DNP printer
2. Select DNP printer in settings
3. Load "DNP_2x6_Cut_Profile.json"
4. Settings automatically applied including 2" cut
5. Print photos - they'll be cut at 2" intervals
6. Switch back to 4x6 profile when done

## Notes
- USB printer auto-detection works for DNP printers
- Online/offline status shown in dropdown
- Profiles persist across application restarts
- Settings saved per Windows user account