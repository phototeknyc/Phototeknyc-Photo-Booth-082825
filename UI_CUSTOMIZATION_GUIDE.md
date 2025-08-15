# UI Customization System Guide

## Overview
The UI Customization System allows users to visually design and customize the photobooth interface layout using a modern drag-and-drop canvas. Users can create custom layouts for different events, screen orientations, and branding requirements without writing any code.

## Features

### 🎨 Visual Designer Canvas
- **Modern Dark Theme** - Professional design interface with dark purple/blue aesthetic
- **Device Preview** - Real-time preview in iPad, Desktop, or custom device frames
- **Drag & Drop** - Intuitive element positioning
- **Smart Guides** - Automatic alignment assistance
- **Grid Snapping** - 10px grid for precise placement
- **Zoom Controls** - Scale canvas for detailed work

### 📱 Responsive Design System
- **Anchor-Based Positioning** - Elements stay relative to screen edges
- **Percentage Sizing** - Scale proportionally with screen size
- **Min/Max Constraints** - Ensure usability across devices
- **Orientation Support** - Separate layouts for portrait/landscape
- **Multi-Device** - Works on tablets, kiosks, and desktop displays

### 🛠️ Design Tools
- **Selection Tool** - Select and manipulate elements
- **Add Button** - Create interactive buttons
- **Add Text** - Insert labels and titles
- **Add Image** - Place logos and graphics
- **Add Shape** - Decorative elements
- **Alignment Tools** - Align left, center, right

### 💾 Data Management
- **Database Storage** - Layouts saved in SQLite database
- **Version Control** - Track layout changes
- **Import/Export** - Share layouts between installations
- **Templates** - Pre-built layouts for common scenarios

## How to Access

### From Main Menu
1. Launch the Photobooth application
2. Click the **UI Customize** tile (cyan/teal with ✨ icon)
3. The customization canvas opens in full-screen mode

### From Settings
1. Open Modern Settings window
2. Click **UI CUSTOMIZE** button in the header
3. Canvas opens for editing

## User Interface Components

### Top Toolbar
- **Device Selector** - Choose preview device (iPad, Desktop, Portrait, Custom)
- **Orientation Toggle** - Switch between landscape/portrait
- **Undo/Redo** - History management (Ctrl+Z, Ctrl+Y)
- **Preview** - Test interactions without saving
- **Save** - Persist layout to database

### Left Toolbox
- **Select Tool (V)** - Default selection mode
- **Button Tool** - Add interactive buttons
- **Text Tool (T)** - Add text labels
- **Image Tool** - Add images/logos
- **Shape Tool** - Add decorative shapes
- **Alignment Tools** - Align selected elements

### Center Canvas
- **Device Frame** - Realistic device preview with bezel
- **Design Surface** - Drag-drop area for elements
- **Grid Pattern** - Visual alignment aid (toggle-able)
- **Smart Guides** - Auto-appear during drag operations

### Right Properties Panel
- **Element Properties** - Position, size, anchor settings
- **Appearance** - Colors, fonts, opacity
- **Responsive Settings** - Size mode, constraints
- **Layers Panel** - Z-order management
- **Quick Actions** - Duplicate, delete buttons

### Bottom Status Bar
- **Zoom Controls** - Zoom in/out, fit to screen
- **Canvas Info** - Current dimensions and element count
- **Snap Settings** - Toggle grid snap and smart guides

## Creating a Custom Layout

### Step 1: Choose Device & Orientation
1. Select target device from dropdown
2. Click orientation toggle if needed
3. Canvas adjusts to selected dimensions

### Step 2: Add Background
1. Click Image tool
2. Drag onto canvas
3. Set as background layer (z-index: -100)
4. Stretch to fill screen

### Step 3: Add Interactive Elements
1. **Start Button**
   - Click Button tool
   - Position at bottom center
   - Set anchor: BottomCenter
   - Size: 20% width, 10% height
   - Action: "StartPhotoSession"

2. **Logo**
   - Click Image tool
   - Position at top
   - Anchor: TopCenter
   - Size mode: AspectFit

3. **Settings Access**
   - Add small button
   - Position: TopRight corner
   - Icon: Gear symbol
   - Action: "OpenSettings"

### Step 4: Configure Responsive Behavior
1. Select each element
2. Set anchor point (where it attaches to screen)
3. Choose size mode:
   - **Fixed** - Always same pixel size
   - **Relative** - Percentage of screen
   - **Stretch** - Fill available space
4. Set min/max size constraints

### Step 5: Test & Save
1. Click Preview to test interactions
2. Check different orientations
3. Click Save when satisfied
4. Name your layout
5. Set as active for current orientation

## Element Types

### Button
- **Purpose**: User interaction points
- **Properties**: Text, icon, background color, action command
- **Actions**: StartPhotoSession, OpenSettings, OpenGallery, etc.

### Text
- **Purpose**: Labels, titles, instructions
- **Properties**: Font family, size, color, alignment
- **Responsive**: Scale with screen or fixed size

### Image
- **Purpose**: Logos, backgrounds, decorations
- **Properties**: Source path, stretch mode, opacity
- **Formats**: PNG, JPG, GIF

### Camera
- **Purpose**: Live camera preview area
- **Properties**: Border style, corner radius
- **Special**: Only visible during photo session

### Countdown
- **Purpose**: Countdown timer display
- **Properties**: Font size, shape, animation
- **Special**: Auto-appears during capture

### Gallery
- **Purpose**: Photo strip preview
- **Properties**: Orientation, spacing, item count
- **Special**: Shows captured photos

## Responsive Design Principles

### Anchor Points
Elements attach to screen edges/corners:
- **TopLeft** - Stays in top-left corner
- **TopCenter** - Centered at top
- **Center** - Always centered
- **BottomCenter** - Centered at bottom
- **Custom** - Percentage-based position

### Size Modes
- **Fixed Pixels** - Consistent physical size
- **Relative %** - Scales with screen
- **Aspect Fit** - Maintains aspect ratio
- **Stretch** - Fills available space

### Constraints
- **Min Size** - Never smaller than (touch-friendly)
- **Max Size** - Never larger than (readability)

## Database Schema

### UILayouts Table
- `Id` - Unique identifier
- `Name` - Layout name
- `PreferredOrientation` - Portrait/Landscape
- `IsActive` - Currently active flag
- `LayoutData` - JSON element definitions
- `ThemeData` - JSON theme settings

### UIElements Table
- `LayoutId` - Parent layout reference
- `ElementId` - Unique element ID
- `ElementType` - Button/Text/Image/etc
- `AnchorPoint` - Screen attachment point
- `RelativeSize` - Percentage dimensions
- `Properties` - JSON additional settings

## Keyboard Shortcuts

- **Ctrl+Z** - Undo
- **Ctrl+Y** - Redo
- **Ctrl+S** - Save layout
- **Ctrl+C** - Copy element
- **Ctrl+V** - Paste element
- **Delete** - Delete selected element
- **V** - Selection tool
- **ESC** - Close designer

## Default Templates

### Portrait Layout
- Logo at top (15% height)
- Camera preview center (45% height)
- Start button bottom (12% height)
- Optimized for vertical screens

### Landscape Layout
- Logo top-left (12% height)
- Camera preview center (60% height)
- Start button right side (15% height)
- Gallery strip at bottom

## Best Practices

### Touch Targets
- Minimum button size: 200x80 pixels
- Spacing between buttons: 20+ pixels
- High contrast colors for visibility

### Performance
- Optimize image sizes (< 500KB)
- Limit total elements to 20-30
- Use vector icons when possible

### Accessibility
- Clear button labels
- Sufficient color contrast
- Logical tab order
- Consistent positioning

## Troubleshooting

### Elements Not Visible
- Check z-index ordering
- Verify visibility property
- Confirm within canvas bounds

### Layout Not Applying
- Ensure layout is set as active
- Check orientation matches device
- Restart application after changes

### Performance Issues
- Reduce image resolutions
- Minimize animation effects
- Clear unused layouts

## File Locations

- **Layouts Database**: `%LocalAppData%\Photobooth\UILayouts.db`
- **Default Templates**: `/Models/UITemplates/UILayoutTemplate.cs`
- **Canvas Control**: `/Controls/ModernUICustomizationCanvas.xaml`
- **Design Documents**: 
  - `/PHOTOBOOTH_UI_CUSTOMIZATION_DESIGN.md`
  - `/RESPONSIVE_UI_LAYOUT_SYSTEM.md`

## Advanced Features (Planned)

- **Animation Properties** - Entrance/exit animations
- **Conditional Visibility** - Show/hide based on state
- **Multi-Language** - Text localization support
- **Theme Variations** - Quick color scheme changes
- **Component Library** - Reusable element groups
- **Collaboration** - Share layouts online

## Support

For issues or feature requests related to UI Customization:
1. Check this documentation
2. Review example templates
3. Contact support with layout export file

---

*Last Updated: 2025-01-15*
*Version: 1.0.0*