# Photobooth Printing Workflow Documentation

## Overview
This document describes the printing workflow implementation in the Photobooth application, including orientation handling, printer settings persistence, and support for multiple paper sizes.

## Architecture Components

### 1. Core Printing Service (`Services/PrintService.cs`)
The main service responsible for printing photos with the following features:
- Auto-routing to appropriate printers based on image size
- Orientation detection and automatic rotation
- DEVMODE settings application
- Support for 2x6 photo strips with 2-inch cut
- Print history tracking

### 2. Printer Settings Management (`Services/PrinterSettingsManager.cs`)
Manages persistent printer settings that survive application restarts:
- Saves printer configurations to XML files and Windows Registry
- Captures complete DEVMODE data including driver-specific settings
- Supports multiple printer profiles for different paper sizes
- Preserves DNP 2-inch cut settings

### 3. Settings Control (`Pages/PhotoboothSettingsControl.xaml.cs`)
UI for configuring printer settings with:
- DEVMODE capture and restoration functions
- Advanced driver settings dialog integration
- Format-specific printer configuration (4x6, 2x6)
- Profile save/load functionality

## Printing Workflow

### Step 1: Image Size Detection
```csharp
// PrintService.cs - DeterminePrinterByImageSize()
float aspectRatio = (float)image.Width / image.Height;
bool isPhotoStrip = aspectRatio < 0.5f; // 2x6 strips
```

### Step 2: Printer Selection
- **Auto-routing enabled**: Routes to specific printers based on format
  - 2x6 strips → `Printer2x6Name` (with 2-inch cut enabled)
  - 4x6/5x7/8x10 → `Printer4x6Name` (standard photos)
- **Auto-routing disabled**: Uses default printer for all prints

### Step 3: DEVMODE Application
The system applies saved printer settings including:
- Paper size and orientation
- Print quality settings
- DNP-specific settings (2-inch cut)
- Color management

```csharp
// Apply DEVMODE to PrintDocument
bool devmodeApplied = PhotoboothSettingsControl.ApplyDevModeToPrintDocument(
    printDocument, savedDriverSettings);
```

### Step 4: Orientation Matching

#### Detection Logic
```csharp
// Determine actual printer orientation from page bounds
bool actualPrinterIsLandscape = e.PageBounds.Width > e.PageBounds.Height;
bool imageIsLandscape = originalImage.Width > originalImage.Height;

// Rotate if orientations don't match
if (actualPrinterIsLandscape != imageIsLandscape) {
    // Rotate image 90 degrees
}
```

#### Why Page Bounds Instead of DEVMODE?
- DEVMODE orientation field is not always reliable
- Page bounds reflect the actual printer configuration
- Works consistently across all printer drivers

### Step 5: Image Printing
1. Apply scaling to fit page
2. Center image on page
3. Apply alignment adjustments (scale/offset)
4. Render with high-quality interpolation

## Printer Profiles System

### Profile Structure
```csharp
public class PrinterProfile
{
    public string PrinterName { get; set; }
    public string ProfileName { get; set; }
    public string PaperSize { get; set; }
    public bool Landscape { get; set; }
    public string DevModeDataBase64 { get; set; } // Complete DEVMODE
    public bool Enable2InchCut { get; set; }
    // ... other settings
}
```

### Profile Storage
- **Primary**: XML files in `%APPDATA%\Photobooth\PrinterProfiles\`
- **Backup**: Windows Registry under `HKCU\SOFTWARE\Photobooth\PrinterSettings`

### Creating/Saving Profiles
1. Open printer properties dialog
2. Configure settings (including DNP 2-inch cut)
3. Capture DEVMODE using `DocumentProperties` API
4. Save to XML and Registry

## DNP 2-Inch Cut Support

### Configuration
1. Open printer properties for DNP printer
2. Navigate to Advanced → DNP Advanced Options
3. Enable "2inch cut" option
4. Save profile to capture setting

### Application
- Automatically enabled for 2x6 strips when using auto-routing
- Preserved in DEVMODE data
- Applied when loading printer profiles

## Supported Paper Sizes

| Size | Aspect Ratio | Detection | Printer |
|------|-------------|-----------|---------|
| 2x6  | 0.33 (1:3)  | < 0.5     | 2x6 Printer |
| 4x6  | 0.67 (2:3)  | 0.65-0.69 | Default/4x6 |
| 5x7  | 0.71 (5:7)  | 0.70-0.72 | Default/4x6 |
| 8x10 | 0.80 (4:5)  | 0.79-0.81 | Default/4x6 |

## Configuration Settings

### Application Settings
```xml
<!-- Properties.Settings.Default -->
<setting name="AutoRoutePrinter" type="bool">true</setting>
<setting name="PrinterName" type="string">Default Printer</setting>
<setting name="Printer4x6Name" type="string">DS40</setting>
<setting name="Printer2x6Name" type="string">DS40</setting>
<setting name="PrinterDriverSettings" type="string">Base64 DEVMODE</setting>
<setting name="Printer4x6DriverSettings" type="string">Base64 DEVMODE</setting>
<setting name="Printer2x6DriverSettings" type="string">Base64 DEVMODE</setting>
<setting name="Dnp2InchCut" type="bool">false</setting>
```

### Alignment Settings
Each printer can have custom alignment adjustments:
- `ScaleX` / `ScaleY` - Scale factors (default 1.0)
- `OffsetX` / `OffsetY` - Position offsets in pixels

## Troubleshooting

### Issue: Orientation Not Matching
**Solution**: The system now uses page bounds to determine actual orientation:
- Width > Height = Landscape
- Height > Width = Portrait

### Issue: 2-Inch Cut Not Working
**Solution**: 
1. Ensure DNP driver settings have 2-inch cut enabled
2. Recapture DEVMODE after enabling
3. Verify profile is saved with Enable2InchCut = true

### Issue: Wrong Printer Selected
**Solution**: 
1. Check Auto-routing setting
2. Verify printer names in settings
3. Check image aspect ratio detection

## API Reference

### Key Functions

#### PrintService.cs
- `PrintPhotos(List<string> photoPaths, string sessionId, int quantity)` - Main print function
- `DeterminePrinterByImageSize(List<string> photoPaths)` - Auto-routing logic
- `PrintDocument_PrintPage(sender, PrintPageEventArgs e)` - Rendering handler

#### PrinterSettingsManager.cs
- `SavePrinterProfile(PrintDocument, profileName, enable2InchCut)` - Save profile
- `LoadPrinterProfile(PrintDocument, profileName)` - Load profile
- `CreateSimpleProfile(printerName, paperSize, landscape)` - Create default profile

#### PhotoboothSettingsControl.cs
- `CaptureRawDriverSettings(printerName)` - Capture DEVMODE
- `ApplyDevModeToPrintDocument(PrintDocument, base64DevMode)` - Apply DEVMODE
- `RestoreRawDriverSettings(printerName, base64Data)` - Restore settings

## Best Practices

1. **Always capture DEVMODE** after configuring printer settings
2. **Use printer profiles** for consistent results across sessions
3. **Test orientation** with both landscape and portrait templates
4. **Verify 2-inch cut** setting for photo strip printers
5. **Use auto-routing** for multi-format events

## Future Enhancements

- [ ] JSON-based profile storage (alternative to XML)
- [ ] Cloud backup of printer profiles
- [ ] Per-event printer settings
- [ ] Print queue management
- [ ] Print preview with actual printer bounds