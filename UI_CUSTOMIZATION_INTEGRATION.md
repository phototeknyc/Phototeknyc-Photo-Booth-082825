# UI Customization System Integration

## Overview
The photobooth application now features a comprehensive UI customization system that allows visual design of the interface without coding. All UI elements can be customized and the changes are automatically applied to both the original and refactored Modern UI pages.

## System Architecture

### Components
1. **UI Customizer Canvas** (`ModernUICustomizationCanvas`)
   - Visual drag-and-drop designer
   - Real-time preview with device frames
   - Property panel for fine-tuning
   - Save/load layouts to database

2. **UILayoutService** 
   - Applies custom layouts to pages
   - Falls back to default UI if no custom layout
   - Handles responsive positioning and sizing

3. **UILayoutDatabase**
   - SQLite storage for layouts
   - Version control and templates
   - Import/export functionality

4. **UILayoutTemplate Model**
   - Defines layout structure
   - Supports multiple element types
   - Anchor-based responsive positioning

## Supported UI Elements

### Element Types
- **Button** - Interactive buttons with actions
- **Text** - Labels and titles
- **Image** - Logos and graphics
- **Camera** - Live view preview
- **Countdown** - Timer display
- **Gallery** - Photo strip preview
- **Background** - Full-screen backgrounds

### Customizable Properties
- **Position** - Anchor point + percentage offset
- **Size** - Fixed, Relative, Stretch, AspectFit modes
- **Appearance** - Colors, fonts, opacity, corner radius
- **Actions** - Command bindings for buttons
- **Visibility** - Show/hide elements conditionally
- **Z-Index** - Layer ordering

## How to Access

### From Surface Dashboard
1. Launch the application
2. Click **UI Customize** tile (cyan with ✨ icon)
3. Design your layout visually
4. Save to apply changes

### From Modern Settings
1. Open Modern Photobooth
2. Click Settings button
3. Select **UI CUSTOMIZE**
4. Make changes and save

## Integration with Modern UI

### Automatic Application
When the PhotoboothTouchModernRefactored page loads:
1. UILayoutService checks for active custom layout
2. If found, applies custom elements over default UI
3. If not found, uses default UI unchanged
4. Custom elements are positioned responsively

### Button Action Mapping
Custom buttons can trigger these actions:
- `StartPhotoSession` - Begin photo capture
- `OpenSettings` - Open settings window
- `OpenGallery` - View photo gallery
- `OpenCloudSettings` - Cloud configuration
- `ShowInfo` - Display help/info

## Creating Custom Layouts

### Design Process
1. **Choose Device** - Select target screen type
2. **Set Orientation** - Portrait or landscape
3. **Add Background** - Optional background image
4. **Place Elements** - Drag and position UI elements
5. **Configure Properties** - Colors, sizes, actions
6. **Preview** - Test interactions
7. **Save** - Persist to database

### Responsive Design
- **Anchor Points** - 9 anchor positions (corners, edges, center)
- **Percentage Offsets** - Position relative to anchor
- **Size Modes**:
  - Fixed: Always same pixel size
  - Relative: Percentage of screen
  - Stretch: Fill available space
  - AspectFit: Scale maintaining ratio
- **Min/Max Constraints** - Ensure usability across devices

## Default Templates

### Portrait Layout
- Logo at top center
- Large camera preview in center
- Wide start button at bottom
- Settings in top-right corner
- Gallery button bottom-left

### Landscape Layout  
- Small logo top-left
- Full-screen camera preview
- Centered circular start button
- Settings buttons in corners
- Photo strip at bottom

## Database Schema

### UILayouts Table
- `Id` - Unique identifier
- `Name` - Layout name
- `Description` - Purpose/notes
- `Orientation` - Portrait/Landscape
- `IsActive` - Currently active flag
- `CreatedDate` - Creation timestamp
- `ModifiedDate` - Last update
- `JsonData` - Serialized layout

### UIElements Table
- `Id` - Element identifier
- `LayoutId` - Parent layout
- `Type` - Element type
- `Properties` - JSON properties
- `ZIndex` - Layer order

## Best Practices

### Design Guidelines
1. **Touch Targets** - Minimum 44x44px for touch
2. **Contrast** - Ensure text is readable
3. **Consistency** - Use theme colors
4. **Hierarchy** - Important elements prominent
5. **Whitespace** - Don't overcrowd

### Performance
- Limit to 20-30 elements per layout
- Optimize images before use
- Use vector icons when possible
- Test on target hardware

### Testing
1. Preview in designer
2. Test all button actions
3. Check different screen sizes
4. Verify touch responsiveness
5. Test with actual camera

## Troubleshooting

### Layout Not Applying
- Check if layout is marked as active
- Verify orientation matches device
- Check database connection
- Review debug logs

### Elements Not Visible
- Check z-index ordering
- Verify visibility property
- Ensure within screen bounds
- Check opacity settings

### Buttons Not Working
- Verify action command is valid
- Check if element is enabled
- Ensure z-index not blocked
- Test touch/click events

## Future Enhancements
- Animation support
- Conditional visibility rules
- Multi-language text
- Remote layout deployment
- A/B testing support
- Analytics integration

## Technical Details

### File Locations
- Canvas: `/Controls/ModernUICustomizationCanvas.xaml`
- Service: `/Services/UILayoutService.cs`
- Database: `/Database/UILayoutDatabase.cs`
- Models: `/Models/UITemplates/`

### Key Classes
- `UILayoutTemplate` - Layout definition
- `UIElementTemplate` - Element definition  
- `UITheme` - Theme settings
- `UILayoutService` - Application logic
- `ModernUICustomizationCanvas` - Designer UI

### Integration Points
- `PhotoboothTouchModernRefactored.Page_Loaded()`
- `UILayoutService.ApplyLayoutToPage()`
- `SurfacePhotoBoothWindow.NavigateToUICustomize_Click()`

## Conclusion
The UI customization system provides complete control over the photobooth interface appearance and layout. Changes made in the visual designer are automatically applied to the running application, allowing for rapid iteration and customization for different events or branding requirements.