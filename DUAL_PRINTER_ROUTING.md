# Automatic Dual Printer Routing

## Overview
The photobooth now supports automatic printer selection based on image dimensions. This allows you to have two different printers configured:
- One for **4x6 photos** (standard photos)
- One for **2x6 strips** (photo booth strips with 2-inch cut)

The system automatically detects the image format and routes it to the correct printer!

## How It Works

### Automatic Detection
The system analyzes the aspect ratio of your images:
- **Aspect ratio < 0.5** → Detected as 2x6 strip → Routes to 2x6 printer
- **Aspect ratio ≥ 0.5** → Detected as 4x6 photo → Routes to 4x6 printer

### Automatic Settings
When routing to each printer:
- **2x6 Printer**: Automatically enables 2-inch cut setting
- **4x6 Printer**: Automatically disables 2-inch cut setting

## Setup Instructions

### Step 1: Enable Auto-Routing
1. Open **Settings** → **Print Settings**
2. Check **"Automatically select printer based on image size"**

### Step 2: Configure 4x6 Printer
1. In the **4x6 Photo Printer** section:
   - Select your standard photo printer from the dropdown
   - This printer will handle all 4x6 photos

### Step 3: Configure 2x6 Printer
1. In the **2x6 Strip Printer** section:
   - Select your DNP printer (or printer configured for strips)
   - Click **Advanced Driver Settings** for this printer
   - Enable **"2inch cut"** in the driver
   - Click OK to save

### Step 4: Save Settings
1. Click **Save Settings** to store your configuration
2. Your dual printer setup is now ready!

## Usage

### Printing 4x6 Photos
1. Create or load a 4x6 image (typical aspect ratio ~0.67)
2. Click Print
3. System automatically:
   - Detects 4x6 format
   - Routes to 4x6 printer
   - Disables 2-inch cut
   - Prints as standard 4x6 photo

### Printing 2x6 Strips
1. Create or load a 2x6 strip image (aspect ratio ~0.33)
2. Click Print
3. System automatically:
   - Detects 2x6 format
   - Routes to 2x6 printer
   - Enables 2-inch cut
   - Prints with 2-inch cuts for strips

## Common Configurations

### Configuration 1: Same Printer, Different Settings
- **4x6 Printer**: DNP DS40 (without 2-inch cut)
- **2x6 Printer**: DNP DS40 (with 2-inch cut enabled)
- Load appropriate media for each print job

### Configuration 2: Different Printers
- **4x6 Printer**: Canon SELPHY for standard photos
- **2x6 Printer**: DNP DS620 for photo strips
- Each printer optimized for its format

### Configuration 3: Mixed Event
- **4x6 Printer**: DNP DS80 with 4x6 media cassette
- **2x6 Printer**: DNP DS40 with 2x6 media roll
- No media changes needed during event!

## Advanced Features

### Manual Override
If auto-routing is disabled:
- The system uses the legacy single printer selection
- You must manually switch printers for different formats

### Aspect Ratio Details
| Format | Typical Dimensions | Aspect Ratio | Routes To |
|--------|-------------------|--------------|-----------|
| 2x6 Strip | 600x1800 pixels | 0.33 | 2x6 Printer |
| 4x6 Photo | 1200x1800 pixels | 0.67 | 4x6 Printer |
| Square | 1800x1800 pixels | 1.00 | 4x6 Printer |

### Detection Logic
```csharp
float aspectRatio = image.Width / image.Height;
bool is2x6 = aspectRatio < 0.5f;
```

## Troubleshooting

### Wrong Printer Selected?
1. Check image dimensions and aspect ratio
2. Verify both printers are properly configured
3. Ensure auto-routing is enabled

### 2-inch Cut Not Working?
1. Verify 2-inch cut is enabled in driver settings for 2x6 printer
2. Check that DNP printer is selected for 2x6
3. Ensure correct media (2x6) is loaded

### Printer Not Available?
- If a configured printer is offline, system falls back to default printer
- Check printer connections and power
- Refresh printer list in settings

## Benefits

1. **No Manual Switching**: Automatically uses the right printer
2. **Event Flexibility**: Offer both formats without operator intervention
3. **Optimal Settings**: Each format gets its ideal printer settings
4. **Reduced Errors**: No wrong media/setting combinations
5. **Faster Workflow**: No need to change settings between prints

## Tips

- Test both printers before your event
- Keep both media types stocked
- Use printer profiles to save configurations
- Consider using USB connections for reliability
- Label your printers clearly (e.g., "4x6 ONLY", "2x6 STRIPS")

The dual printer routing makes it seamless to offer multiple print formats at your photobooth events!