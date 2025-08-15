# Demo: Creating a Custom Photobooth Layout

This demo shows how to create a custom layout for the photobooth interface using the new UI Customization system.

## Quick Test Instructions

### 1. Launch the Application
```
Photobooth.exe
```

### 2. Access UI Customization
From the main menu, click the **UI Customize** tile (cyan/teal with ✨ icon)

### 3. Create a Simple Custom Layout

#### Step 1: Add Background
1. Click the **Image Tool** (picture icon) in the left toolbar
2. Click on the canvas to add an image placeholder
3. In the right properties panel:
   - Set Width: 100%
   - Set Height: 100%
   - Set Z-Index: -100 (send to back)
   - Set Anchor: TopLeft

#### Step 2: Add Logo
1. Click the **Image Tool** again
2. Place at top center of canvas
3. Properties:
   - Width: 200px
   - Height: 80px
   - Anchor: TopCenter
   - Offset Y: 20px

#### Step 3: Add Start Button
1. Click the **Button Tool** in toolbar
2. Place at bottom center
3. Properties:
   - Text: "START PHOTO SESSION"
   - Width: 300px
   - Height: 80px
   - Background Color: #4CAF50
   - Corner Radius: 40
   - Font Size: 24
   - Anchor: BottomCenter
   - Offset Y: -100px
   - Action Command: StartPhotoSession

#### Step 4: Add Settings Button
1. Click **Button Tool**
2. Place at top right
3. Properties:
   - Text: "⚙"
   - Width: 60px
   - Height: 60px
   - Background Color: #3A4060
   - Corner Radius: 30
   - Font Size: 28
   - Anchor: TopRight
   - Offset X: -20px
   - Offset Y: 20px
   - Action Command: OpenSettings

#### Step 5: Add Camera Preview Area
1. Click **Camera Tool** (camera icon)
2. Draw rectangle in center
3. Properties:
   - Width: 60%
   - Height: 50%
   - Anchor: Center
   - Border Thickness: 5
   - Corner Radius: 20

### 4. Save and Activate Layout
1. Click **Save** button in top toolbar
2. Enter name: "Event Layout 1"
3. Check "Set as Active" checkbox
4. Click OK

### 5. Test the Layout
1. Navigate back to main menu
2. Open **Modern Photobooth** (or press the photobooth button)
3. Your custom layout should be applied!

## Advanced Customization

### Creating Event-Specific Themes

#### Wedding Theme
```
Background: Soft pink gradient
Buttons: Rose gold (#B76E79)
Font: Script/cursive style
Decorations: Floral corner overlays
```

#### Corporate Event
```
Background: Company brand colors
Logo: Company logo prominent
Buttons: Brand accent color
Font: Corporate font family
```

#### Kids Party
```
Background: Bright colorful pattern
Buttons: Large, colorful with icons
Text: Fun, playful fonts
Animations: Bouncing elements
```

### Responsive Behavior

The layout automatically adapts to different screen sizes:

1. **Tablet (Portrait)**
   - Buttons stack vertically
   - Camera preview adjusts aspect ratio
   - Text scales proportionally

2. **Large Display (Landscape)**
   - Buttons spread horizontally
   - Camera preview maximizes
   - Additional info panels visible

3. **Kiosk Mode**
   - Full-screen optimized
   - Touch-friendly sizing
   - High contrast for visibility

## Troubleshooting

### Layout Not Appearing
1. Ensure layout is marked as "Active" in database
2. Check orientation matches device
3. Restart application after saving

### Buttons Not Working
1. Verify Action Command is set correctly
2. Available commands:
   - StartPhotoSession
   - OpenSettings
   - OpenGallery
   - ReturnHome

### Performance Issues
1. Optimize image sizes (< 500KB)
2. Limit elements to 20-30
3. Avoid complex animations

## Database Management

### View All Layouts
```sql
SELECT * FROM UILayouts;
```

### Set Active Layout
```sql
UPDATE UILayouts SET IsActive = 1 WHERE Name = 'Event Layout 1';
UPDATE UILayouts SET IsActive = 0 WHERE Name != 'Event Layout 1';
```

### Export Layout
The layout data is stored as JSON in the LayoutData column and can be exported/imported.

## Code Integration

### Programmatically Apply Layout
```csharp
var layoutService = new UILayoutService();
layoutService.ApplyLayoutToPage(photoboothPage, mainGrid);
```

### Custom Action Handlers
Add new commands in PhotoboothTouchModern.xaml.cs:
```csharp
public void CustomAction()
{
    // Your custom logic here
}
```

Then in UILayoutService.cs, add case:
```csharp
case "CustomAction":
    modernPage.CustomAction();
    break;
```

## Tips & Best Practices

1. **Start Simple** - Begin with basic layouts before adding complexity
2. **Test Often** - Preview changes before saving
3. **Keep Backups** - Export important layouts
4. **Document Changes** - Note what each layout is for
5. **User Test** - Have others test the interface
6. **Performance First** - Optimize for smooth operation
7. **Accessibility** - Ensure buttons are large enough and have good contrast

## Next Steps

1. Create multiple layouts for different events
2. Experiment with animations and transitions
3. Build a library of reusable components
4. Share layouts with the community
5. Integrate with event management system

---

*For more detailed documentation, see UI_CUSTOMIZATION_GUIDE.md*