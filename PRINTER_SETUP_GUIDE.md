# Printer Setup Guide for Photobooth Application

## Quick Start Setup

### Step 1: Connect Your Printers
1. Connect your photo printers to the computer
2. Install the manufacturer's drivers (DNP, Canon, Epson, etc.)
3. Verify printers appear in Windows Settings → Printers & Scanners

### Step 2: Initial Configuration in Photobooth

#### Basic Setup
1. **Open Photobooth Application**
2. **Navigate to Settings** (gear icon or Settings menu)
3. **Go to Printer Settings tab**

#### Select Your Printers
1. **Default Printer**: Select your main 4x6 photo printer
2. **Enable Auto-routing**: Check "Auto-route by paper size"
3. **4x6 Printer**: Select printer for standard photos
4. **2x6 Printer**: Select printer for photo strips (can be same as 4x6)

### Step 3: Configure Printer Profiles

#### Method 1: Quick Setup (Recommended)
1. Click **"Configure Printer Profiles"** button
2. Select a profile from the list:
   - 4x6 Portrait
   - 4x6 Landscape  
   - 2x6 Portrait
   - 2x6 Landscape
3. Click **"Configure Printer"** button
4. Adjust settings in the printer dialog:
   - Paper size
   - Quality
   - For DNP: Enable "2inch cut" for 2x6 strips
5. Click OK to save
6. Click **"Save Profile"** to persist settings

#### Method 2: Advanced Driver Settings
1. Select your printer from dropdown
2. Click **"Advanced Driver Settings"**
3. Configure all printer-specific options
4. Click OK - settings are automatically captured
5. Settings persist across application restarts

## DNP Printer Setup (DS40, DS80, DS620, DS820)

### Enabling 2-Inch Cut for Photo Strips
1. Select DNP printer in settings
2. Click **"Advanced Driver Settings"**
3. Navigate to: **Printing Preferences → Advanced → DNP Advanced Options**
4. Find **"2inch cut"** setting
5. Set to **"Enable"**
6. Click OK to save
7. The setting is now captured in DEVMODE

### Recommended DNP Settings
- **Media Size**: 4x6 or 2x6 (depending on use)
- **Quality**: High
- **Color Management**: sRGB
- **2inch cut**: Enable (for 2x6 strips only)
- **Overcoat**: Glossy or Matte (your preference)

## Multi-Format Event Setup

### Scenario: Wedding with Multiple Print Sizes
```
Configuration:
- 4x6 Printer: DNP DS40 (Printer 1)
- 2x6 Printer: DNP DS40 (Same printer, different settings)
- Auto-routing: Enabled
```

1. **Create 4x6 Profile**:
   - Paper: 4x6
   - Orientation: Portrait
   - 2-inch cut: Disabled
   - Save as "Wedding_4x6"

2. **Create 2x6 Profile**:
   - Paper: 4x6 (will print 2x 2x6 strips)
   - Orientation: Landscape
   - 2-inch cut: Enabled
   - Save as "Wedding_2x6"

3. **Enable Auto-routing**:
   - Check "Auto-route by paper size"
   - System automatically selects profile based on template

## Testing Your Setup

### Print Test Page
1. Go to Printer Settings
2. Select printer and profile
3. Click **"Test Print"** button
4. Verify:
   - Correct orientation
   - Proper margins
   - Color accuracy
   - 2-inch cut (if applicable)

### Test Different Orientations
1. **Portrait Template Test**:
   - Load a portrait template (height > width)
   - Print and verify no unwanted rotation

2. **Landscape Template Test**:
   - Load a landscape template (width > height)
   - Print and verify correct orientation

## Common Printer Configurations

### Single Printer Setup
```
Default Printer: DNP DS40
4x6 Printer: DNP DS40
2x6 Printer: DNP DS40
Auto-routing: Enabled
```
- Same printer handles all formats
- Different DEVMODE settings per format

### Dual Printer Setup
```
Default Printer: Canon SELPHY
4x6 Printer: Canon SELPHY
2x6 Printer: DNP DS40
Auto-routing: Enabled
```
- Dedicated printers for each format
- Optimal for high-volume events

### Backup Printer Configuration
```
Primary: DNP DS40
Backup: Canon SELPHY
```
1. Create profiles for both printers
2. Switch printers quickly if needed
3. Maintain consistent output quality

## Troubleshooting Setup Issues

### Problem: Printer Not Appearing
**Solutions**:
1. Restart Windows Print Spooler service
2. Reinstall printer drivers
3. Check USB/network connection
4. Run Windows Printer Troubleshooter

### Problem: Settings Not Saving
**Solutions**:
1. Run Photobooth as Administrator
2. Check write permissions for `%APPDATA%\Photobooth`
3. Verify Registry access (HKCU\SOFTWARE\Photobooth)
4. Clear corrupted profiles and recreate

### Problem: Wrong Orientation Printing
**Solutions**:
1. Recapture DEVMODE settings
2. Check template orientation matches printer setting
3. Verify page bounds detection in debug log
4. Update printer driver to latest version

### Problem: 2-Inch Cut Not Working
**Solutions**:
1. Verify DNP driver version supports 2-inch cut
2. Enable in Advanced Driver Settings
3. Recapture DEVMODE after enabling
4. Check printer firmware is up to date

## Advanced Configuration

### Custom Paper Sizes
1. Open Printer Properties in Windows
2. Go to Advanced → Paper Size
3. Create custom size (e.g., 3.5x5)
4. Configure in Photobooth profiles
5. Test with appropriate template

### Network Printer Setup
1. Install printer on network
2. Add network printer in Windows
3. Configure in Photobooth as normal
4. Consider network latency for large images

### Color Management
1. Install printer ICC profiles
2. Set in Advanced Driver Settings
3. Choose color space (sRGB recommended)
4. Calibrate monitor for accurate preview

## Settings Backup and Migration

### Backup Your Settings
1. **Export Profiles**:
   - Settings → Printer Settings
   - Click "Export Profiles"
   - Save to backup location

2. **Registry Backup**:
   ```
   reg export "HKCU\SOFTWARE\Photobooth" photobooth_backup.reg
   ```

3. **Files to Backup**:
   - `%APPDATA%\Photobooth\PrinterProfiles\*.xml`
   - Application settings file

### Restore Settings on New Computer
1. Install Photobooth application
2. Install same printer drivers
3. Import registry backup
4. Copy XML profiles to `%APPDATA%\Photobooth\PrinterProfiles\`
5. Restart application

## Performance Optimization

### Speed Up Printing
1. **Pre-load Printer**:
   - Keep printer powered on
   - Print test page at event start

2. **Optimize Images**:
   - Resize to printer resolution
   - Use JPEG for faster processing
   - Avoid unnecessary filters

3. **Queue Management**:
   - Enable print spooling
   - Set priority for Photobooth process

### Reduce Print Failures
1. **Stable Power**:
   - Use UPS for printer
   - Avoid power strips

2. **Quality Media**:
   - Use manufacturer-approved paper
   - Store media properly

3. **Regular Maintenance**:
   - Clean printer heads
   - Update firmware
   - Replace consumables timely

## Event Day Checklist

### Before Event
- [ ] Test all printer profiles
- [ ] Verify paper stock
- [ ] Check ink/ribbon levels
- [ ] Print test sheets
- [ ] Backup printer settings
- [ ] Clean printer heads
- [ ] Update printer drivers

### During Event
- [ ] Monitor printer status
- [ ] Check print quality periodically
- [ ] Keep backup printer ready
- [ ] Track paper usage
- [ ] Clear print queue if needed

### After Event
- [ ] Clean printers
- [ ] Note any issues
- [ ] Update profiles if needed
- [ ] Backup successful settings

## Support Resources

### Manufacturer Support
- **DNP**: [dnpphoto.com/support](https://dnpphoto.com)
- **Canon**: [usa.canon.com/support](https://usa.canon.com)
- **Epson**: [epson.com/support](https://epson.com)

### Application Support
- Check PRINTING_WORKFLOW.md for technical details
- Review debug logs in application
- Contact support with:
  - Printer model
  - Driver version
  - DEVMODE capture
  - Error messages

## Quick Reference

### Key Settings Locations
- **Profiles**: `%APPDATA%\Photobooth\PrinterProfiles\`
- **Registry**: `HKEY_CURRENT_USER\SOFTWARE\Photobooth\PrinterSettings`
- **Logs**: Application directory `\logs\`

### Important Files
- `PrinterSettingsManager.cs` - Profile management
- `PrintService.cs` - Printing logic
- `PhotoboothSettingsControl.xaml` - Settings UI

### Keyboard Shortcuts
- `Ctrl+P` - Quick print
- `Ctrl+Shift+P` - Print settings
- `F9` - Test print