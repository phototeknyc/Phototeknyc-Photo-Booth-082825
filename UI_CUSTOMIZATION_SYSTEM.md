# UI Customization System - Complete Documentation

## Overview
A comprehensive visual design system for customizing the PhotoboothTouchModern interface through a drag-and-drop canvas editor. This system allows users to create custom layouts for different events without writing code.

## System Architecture

### Core Components

#### 1. **ModernUICustomizationCanvas** (`/Controls/ModernUICustomizationCanvas.xaml`)
- Main visual designer interface
- Dark theme with purple/blue accent colors
- Toolbar with 100px left column, 44x44px buttons
- Real-time preview with device frames
- Grid snapping (10px) and smart guides

#### 2. **UILayoutService** (`/Services/UILayoutService.cs`)
- Applies custom layouts to PhotoboothTouchModern
- Non-destructive overlay system
- Fallback to default UI when no custom layout
- Real-time layout refresh capability

#### 3. **UILayoutDatabase** (`/Database/UILayoutDatabase.cs`)
- SQLite database for layout persistence
- Version control and active layout management
- Import/Export functionality
- Default template seeding

#### 4. **UILayoutTemplate** (`/Models/UITemplates/UILayoutTemplate.cs`)
- Data model for UI layouts
- Anchor-based responsive positioning
- Default templates for portrait/landscape
- Element property definitions

## Element Types

### Available Elements
| Type | Description | Key Properties |
|------|------------|----------------|
| **Button** | Interactive controls | Text, BackgroundColor, CornerRadius, ActionCommand |
| **Text** | Labels and titles | FontSize, FontFamily, Color, Alignment |
| **Image** | Logos and graphics | ImagePath, Stretch, Opacity |
| **Background** | Full-screen backgrounds | ImagePath, Color, StretchMode |
| **Camera** | Live camera preview | BorderThickness, CornerRadius |
| **Countdown** | Photo countdown timer | FontSize, BackgroundColor |

### Default Element IDs (Matching PhotoboothTouchModern)
- `liveViewImage` - Camera preview background
- `startButtonOverlay` - Main start button (300x300, centered)
- `modernSettingsButton` - Settings access (bottom-right)
- `cloudSettingsButton` - Cloud settings (top-right)
- `countdownOverlay` - Countdown display (centered)

## Responsive Design System

### Anchor Points
```
TopLeft    TopCenter    TopRight
   ●          ●           ●
   
MiddleLeft  Center    MiddleRight
   ●          ●           ●
   
BottomLeft BottomCenter BottomRight
   ●          ●           ●
```

### Offset System
- **Pixel Offsets** (|value| > 10): Used as exact pixel values
- **Percentage Offsets** (|value| ≤ 10): Calculated as percentage of canvas size

Example:
```csharp
AnchorOffset = new Point(-90, -90)  // 90 pixels from bottom-right
AnchorOffset = new Point(3, 3)      // 3% margin from top-left
```

### Size Modes
| Mode | Description | Use Case |
|------|------------|----------|
| **Fixed** | Constant pixel size | Buttons, icons |
| **Relative** | Percentage of screen | Responsive elements |
| **Stretch** | Fill available space | Backgrounds |
| **AspectFit** | Maintain aspect ratio | Images, logos |

## UI Customization Canvas Features

### Toolbar Layout
- **Left Column**: 100px width
- **Buttons**: 44x44px with 3px margins
- **Tools**:
  - Selection tool (arrow)
  - Button tool
  - Text tool ("T")
  - Image tool
  - Shape tool (rectangle)
  - Alignment tools (left, center, right)

### Canvas Controls
- **Device Preview**: iPad, Desktop, Portrait modes
- **Zoom Controls**: +/- buttons, fit to screen
- **Grid Snap**: 10px grid alignment
- **Smart Guides**: Automatic alignment assistance

### Properties Panel
- Position (X, Y coordinates)
- Size (Width, Height)
- Anchor point selection
- Appearance settings (colors, fonts)
- Layer management (Z-index)

## Database Schema

### UILayouts Table
```sql
CREATE TABLE UILayouts (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT,
    PreferredOrientation INTEGER,
    IsActive INTEGER DEFAULT 0,
    LayoutData TEXT,  -- JSON serialized elements
    ThemeData TEXT,   -- JSON serialized theme
    CreatedDate TEXT,
    ModifiedDate TEXT,
    Version INTEGER DEFAULT 1
);
```

### Default Templates

#### Landscape Layout
```csharp
Elements = {
    startButtonOverlay: Center, 300x300, Green (#CC4CAF50)
    modernSettingsButton: BottomRight(-90,-90), 70x70, Blue (#FF2196F3)
    cloudSettingsButton: TopRight(-20,20), 60x60, Dark (#323C4F)
    countdownOverlay: Center, 200x200, Hidden by default
    liveViewImage: Stretch, Full screen
}
```

## Integration with PhotoboothTouchModern

### Automatic Integration
```csharp
// In PhotoboothTouchModern.xaml.cs
private void Page_Loaded(object sender, RoutedEventArgs e)
{
    uiLayoutService.ApplyLayoutToPage(this, mainGrid);
}
```

### Custom Action Commands
Available commands that custom buttons can trigger:
- `StartPhotoSession` - Begin photo capture
- `OpenSettings` - Open settings window
- `OpenGallery` - View photo gallery
- `ReturnHome` - Return to main menu
- `OpenCloudSettings` - Cloud configuration

### Public Methods for Custom UI
```csharp
public void StartPhotoSession()
public void OpenSettings()
public void OpenGallery()
public void ReturnHome()
```

## File Locations

```
/Photobooth/
├── Controls/
│   ├── ModernUICustomizationCanvas.xaml    # Main designer UI
│   └── ModernUICustomizationCanvas.xaml.cs # Designer logic
├── Services/
│   └── UILayoutService.cs                  # Layout application
├── Database/
│   └── UILayoutDatabase.cs                 # Database operations
├── Models/
│   └── UITemplates/
│       └── UILayoutTemplate.cs             # Data models
└── Documentation/
    ├── UI_CUSTOMIZATION_GUIDE.md           # User guide
    ├── UI_CUSTOMIZATION_SYSTEM.md          # This file
    └── DEMO_CUSTOM_LAYOUT.md               # Demo instructions
```

## Usage Workflow

### Creating a Custom Layout
1. Open UI Customization from main menu
2. Select device type and orientation
3. Add elements using toolbar buttons
4. Position elements by dragging
5. Configure properties in right panel
6. Save layout with descriptive name
7. Set as active for current orientation

### Applying Custom Layout
1. Custom layouts auto-apply when PhotoboothTouchModern loads
2. Service checks for active layout matching current orientation
3. Elements overlay on existing UI (non-destructive)
4. Falls back to default if no custom layout

## Best Practices

### Performance
- Limit to 20-30 elements per layout
- Optimize images to < 500KB
- Use vector icons when possible
- Minimize animation effects

### Design Guidelines
- **Touch Targets**: Minimum 44x44px for buttons
- **Spacing**: 20px minimum between interactive elements
- **Contrast**: Ensure text is readable on backgrounds
- **Consistency**: Maintain visual hierarchy

### Responsive Design
- Use anchor points for edge-relative positioning
- Combine pixel offsets for precise spacing
- Set min/max sizes for constraint
- Test on different screen sizes

## Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| Elements stacked in corner | Incorrect anchor offset calculation | Fixed with pixel/percentage detection |
| Buttons cut off | Toolbar too narrow | Increased to 100px width |
| Layout not applying | Not set as active | Check IsActive flag in database |
| Elements not visible | Z-index conflicts | Adjust layer ordering |

### Debug Mode
```csharp
// Enable debug borders in UIElementControl
BorderBrush = Brushes.Cyan;
BorderThickness = new Thickness(1);
```

## Recent Updates

### Version 1.0 (2025-01-15)
- Initial implementation
- Fixed toolbar button sizing (100px column, 44x44 buttons)
- Corrected element positioning with pixel/percentage offset handling
- Updated default templates to match PhotoboothTouchModern
- Added comprehensive documentation

### Known Improvements
- [ ] Add animation properties for elements
- [ ] Implement conditional visibility rules
- [ ] Add multi-language support
- [ ] Create component library for reusability
- [ ] Add online template sharing

## API Reference

### UILayoutService Methods
```csharp
void ApplyLayoutToPage(Page page, Panel mainContainer)
void RefreshLayout(Page page, Panel mainContainer)
bool IsCustomLayoutActive { get; }
UILayoutTemplate CurrentLayout { get; }
```

### UILayoutDatabase Methods
```csharp
void SaveLayout(UILayoutTemplate layout)
UILayoutTemplate GetLayout(string id)
UILayoutTemplate GetActiveLayout(Orientation orientation)
void SetActiveLayout(string layoutId, Orientation orientation)
List<UILayoutTemplate> GetAllLayouts()
void DeleteLayout(string id)
string ExportLayout(string id)
UILayoutTemplate ImportLayout(string jsonData)
```

## Support

For issues or questions:
1. Check this documentation
2. Review UI_CUSTOMIZATION_GUIDE.md for user instructions
3. See DEMO_CUSTOM_LAYOUT.md for examples
4. Check CLAUDE.md for build commands

---

*System designed and implemented for Photobooth application*  
*Version 1.0 - January 15, 2025*