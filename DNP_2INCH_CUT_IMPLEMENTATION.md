# DNP 2-Inch Cut Implementation

## Overview
The photobooth application now includes full support for the DNP printer's "2 inch cut" setting, which is essential for creating 2x6 photo strips. This setting can be controlled directly within the application without needing to access the Windows printer dialog each time.

## Features Implemented

### 1. UI Control in Settings
- Added checkbox: "Enable 2 inch cut (for 2x6 prints)" in the DNP Printer Settings section
- Setting is saved to application preferences
- Automatically applied when printing to DNP printers

### 2. Direct DEVMODE Integration
- Application captures and stores the complete printer DEVMODE structure
- Includes all driver-specific settings including the 2-inch cut option
- Settings persist across application restarts

### 3. Automatic Application at Print Time
When printing:
1. The saved printer driver settings (including 2-inch cut) are automatically restored
2. If DNP printer is selected and 2-inch cut is enabled, it's applied before printing
3. No manual intervention required once configured

## How to Use

### Initial Setup (One-Time Configuration)

1. **Open Photobooth Settings**
   - Navigate to the Settings page
   - Go to Print Settings section

2. **Select Your DNP Printer**
   - Choose your DNP printer from the dropdown
   - Verify it shows as online

3. **Configure 2-Inch Cut in Driver**
   - Click "Advanced Driver Settings" button
   - The Windows printer dialog opens
   - Navigate to DNP Advanced Options
   - Set "2inch cut" to "Enable"
   - Click OK to save
   - **NEW: Settings are automatically saved!**
   - A dialog asks if you enabled 2-inch cut
   - Click "Yes" to auto-enable it in the application
   - All settings are saved automatically - no need to click "Save Settings"

### Auto-Save Feature (NEW!)
When you click OK in the Advanced Driver Settings:
- DEVMODE settings are automatically captured
- Driver configuration is saved instantly
- For DNP printers, you're prompted about 2-inch cut status
- The checkbox is automatically updated based on your response
- No need to manually save settings anymore!

### Regular Use

Once configured:
1. The 2-inch cut setting is automatically applied when printing
2. No need to access printer properties each time
3. Setting persists across application restarts

### Switching Between 4x6 and 2x6 Prints

**For 2x6 prints:**
- Check "Enable 2 inch cut" in settings
- Load 2x6 media in printer
- Print normally - cuts will be at 2-inch intervals

**For 4x6 prints:**
- Uncheck "Enable 2 inch cut" in settings
- Load 4x6 media in printer
- Print normally - standard 4x6 cuts applied

## Technical Implementation

### Settings Storage
```csharp
// New setting added to Properties.Settings
public bool Dnp2InchCut { get; set; }

// Raw driver settings (includes 2-inch cut in DEVMODE)
public string PrinterDriverSettings { get; set; }
```

### Automatic Application
```csharp
// In PrintService.PrintPhotos()
if (printerName.ToLower().Contains("dnp") && Properties.Settings.Default.Dnp2InchCut)
{
    // Apply 2-inch cut setting
    PhotoboothSettingsControl.ApplyDnp2InchCutSetting(printerName, true);
}
```

### DEVMODE Capture
The application captures the complete Windows DEVMODE structure which includes:
- Standard Windows print settings
- DNP driver-specific private data area
- 2-inch cut flag and other advanced settings

## Benefits

1. **No Manual Steps**: Once configured, 2-inch cut is automatic
2. **Persistent Settings**: Configuration survives application restarts
3. **Easy Switching**: Toggle between 2x6 and 4x6 with a checkbox
4. **Profile Support**: Save multiple printer profiles for different scenarios
5. **Batch Printing**: Apply settings consistently across multiple print jobs

## Troubleshooting

### 2-Inch Cut Not Working
1. Verify DNP printer is selected and online
2. Check "Enable 2 inch cut" is checked in settings
3. Open Advanced Driver Settings to verify it's enabled
4. Ensure correct media (2x6) is loaded in printer

### Settings Not Persisting
1. Click "Save Settings" after making changes
2. Use "Save Profile" to capture driver settings
3. Check application has write permissions to settings file

### Different DNP Models
- DS40: Fully supported
- DS80: Fully supported
- DS620: Fully supported
- DS820: Fully supported
- RX1: Check firmware supports 2-inch cut

## Notes
- The 2-inch cut setting is specific to DNP printers
- Other printer brands may have different cutting options
- Always test with a single print before batch printing
- Media size must support the selected cutting mode