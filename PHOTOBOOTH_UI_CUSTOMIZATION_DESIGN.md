# Photobooth UI Customization System Design

## Overview
A canvas-based customization system for the PhotoboothTouchModern interface, allowing users to customize backgrounds, icons, button placement, and overall layout.

## Architecture

### 1. Core Components

#### UICustomizationCanvas (extends DesignerCanvas)
- Inherits from existing DesignerCanvas
- Specialized for UI element manipulation
- Supports layers (background, controls, overlays)

#### UICanvasItem Types
```csharp
- BackgroundCanvasItem : ImageCanvasItem
  - Full-screen background images
  - Supports gradients and solid colors
  
- ButtonCanvasItem : CanvasItem  
  - Represents interactive buttons
  - Links to action commands
  - Custom styles (shape, color, icon)
  
- IconCanvasItem : ImageCanvasItem
  - Decorative icons
  - Non-interactive elements
  
- TextLabelCanvasItem : TextCanvasItem
  - Labels and titles
  - Font customization
  
- CountdownCanvasItem : CanvasItem
  - Special countdown display element
  - Position and style customizable
```

### 2. Customization Modes

#### Design Mode
- Accessed from settings
- Shows design canvas overlay
- All elements become draggable/resizable
- Properties panel for detailed editing

#### Preview Mode  
- Test interactions without saving
- Simulates actual photobooth flow

#### Runtime Mode
- Normal operation with custom layout
- No editing capabilities

### 3. Data Model

```csharp
public class UILayout
{
    public string Name { get; set; }
    public string Version { get; set; }
    public DateTime LastModified { get; set; }
    public List<UIElement> Elements { get; set; }
    public Dictionary<string, object> Settings { get; set; }
}

public class UIElement
{
    public string Id { get; set; }
    public string Type { get; set; } // Button, Icon, Text, etc.
    public Point Position { get; set; }
    public Size Size { get; set; }
    public double Rotation { get; set; }
    public int ZIndex { get; set; }
    public Dictionary<string, object> Properties { get; set; }
    public string ActionCommand { get; set; } // For buttons
}
```

### 4. Implementation Steps

#### Phase 1: Foundation
1. Create UICustomizationCanvas class
2. Implement UICanvasItem types
3. Add customization mode toggle

#### Phase 2: Designer Interface
1. Create properties panel for elements
2. Add element library/palette
3. Implement snap-to-grid functionality

#### Phase 3: Persistence
1. Save/load custom layouts (JSON/XML)
2. Export/import functionality
3. Preset templates

#### Phase 4: Advanced Features
1. Animation properties
2. Responsive scaling
3. Multi-language support
4. Theme variations

## User Workflow

1. **Enter Customization Mode**
   - Settings → UI Customization
   - Canvas overlay appears

2. **Edit Elements**
   - Drag to reposition
   - Resize handles for scaling
   - Right-click for properties
   - Delete key to remove

3. **Add New Elements**
   - Drag from palette
   - Upload custom images
   - Configure actions

4. **Save & Apply**
   - Name the layout
   - Set as active
   - Export for backup

## Technical Considerations

### Performance
- Virtualization for complex layouts
- Lazy loading of images
- Cached rendering

### Compatibility
- Minimum element sizes for touch
- Required elements validation
- Fallback to default layout

### Security
- Sanitize uploaded images
- Validate action commands
- Restrict file system access

## Benefits

1. **Brand Customization** - Match venue/event branding
2. **Workflow Optimization** - Arrange buttons for specific needs
3. **Accessibility** - Adjust sizes/positions for different users
4. **Multi-purpose** - Different layouts for different events
5. **No Code Required** - Visual editing for non-developers

## Example Use Cases

- **Wedding Layout** - Romantic backgrounds, heart icons
- **Corporate Event** - Company logo, brand colors
- **Kids Party** - Cartoon characters, larger buttons
- **Museum Kiosk** - Minimal design, educational text
- **Night Club** - Neon effects, dynamic elements

## File Structure

```
/Services/UICustomization/
  - UICustomizationCanvas.cs
  - UICanvasItems/
    - ButtonCanvasItem.cs
    - BackgroundCanvasItem.cs
    - IconCanvasItem.cs
    - TextLabelCanvasItem.cs
  - UILayoutManager.cs
  - UILayoutSerializer.cs
  
/Pages/
  - UICustomizationPage.xaml
  - UICustomizationPage.xaml.cs
  
/Models/
  - UILayout.cs
  - UIElement.cs
  
/Resources/UILayouts/
  - Default.json
  - Wedding.json
  - Corporate.json
  - Kids.json
```

## Next Steps

1. Create proof-of-concept with basic button repositioning
2. Test touch interaction in design mode
3. Implement save/load functionality
4. Add property editing panel
5. Create preset templates
6. Documentation and tutorials