# Template Designer Overlay Usage

The Template Designer Overlay allows you to open the template designer within a modal overlay instead of navigating to a separate window or page.

## Features

- **Modal Overlay**: Opens the template designer in a full-screen overlay with semi-transparent background
- **Save/Close Controls**: Built-in save button and close button with unsaved changes warning
- **Load Templates**: Can load existing templates for editing
- **Create New Templates**: Can create new templates from scratch

## How to Use

### 1. From PhotoboothTouchModernRefactored Page

The overlay is already integrated into `PhotoboothTouchModernRefactored.xaml`. You can call these methods:

```csharp
// Open designer for a new template
CreateNewTemplate();

// Edit the currently selected template
EditCurrentTemplate();

// Open designer for a specific template
ShowTemplateDesignerOverlay("path/to/template.xml");
```

### 2. Adding to Other Pages

To add the template designer overlay to any other page:

#### Step 1: Add to XAML
```xml
<!-- Add namespace if not already present -->
xmlns:controls="clr-namespace:Photobooth.Controls"

<!-- Add the overlay control (usually at the end of your main Grid) -->
<controls:TemplateDesignerOverlay x:Name="TemplateDesignerOverlayControl"
                                   Visibility="Collapsed"
                                   Grid.RowSpan="10"
                                   Panel.ZIndex="1001"/>
```

#### Step 2: Add Methods to Code-Behind
```csharp
// Show the template designer overlay
private void ShowTemplateDesignerOverlay(string templatePath = null)
{
    if (TemplateDesignerOverlayControl != null)
    {
        TemplateDesignerOverlayControl.ShowOverlay(templatePath);
    }
}

// Example button click handler to create new template
private void CreateNewTemplate_Click(object sender, RoutedEventArgs e)
{
    ShowTemplateDesignerOverlay(null);
}

// Example button click handler to edit existing template
private void EditTemplate_Click(object sender, RoutedEventArgs e)
{
    string templatePath = "path/to/your/template.xml";
    ShowTemplateDesignerOverlay(templatePath);
}
```

### 3. Adding a Button to Open the Designer

You can add a button anywhere in your UI to open the template designer:

```xml
<!-- Example button in settings or admin area -->
<Button Content="Template Designer"
        Click="OpenTemplateDesigner_Click"
        Style="{StaticResource ModernButtonStyle}"
        Width="200"
        Height="50"/>
```

Code-behind:
```csharp
private void OpenTemplateDesigner_Click(object sender, RoutedEventArgs e)
{
    ShowTemplateDesignerOverlay();
}
```

### 4. Handling Events

The overlay provides two events you can subscribe to:

```csharp
// In your page constructor or initialization
TemplateDesignerOverlayControl.OverlayClosed += OnTemplateDesignerClosed;
TemplateDesignerOverlayControl.TemplateSaved += OnTemplateSaved;

// Event handlers
private void OnTemplateDesignerClosed(object sender, EventArgs e)
{
    // Refresh template list or perform other actions
}

private void OnTemplateSaved(object sender, EventArgs e)
{
    // Template was saved successfully
    // Refresh template list or update UI
}
```

## Implementation Details

The overlay uses:
- **MainPage**: The existing template designer page
- **DesignerVM**: The existing view model with all template editing functionality
- **Frame Navigation**: The designer is loaded into a Frame control within the overlay

## Benefits

1. **No Navigation Required**: Users stay on the same page
2. **Context Preservation**: The underlying page state is maintained
3. **Better UX**: Modal overlay provides clear focus on the task
4. **Reusable**: Can be added to any page in the application
5. **Consistent Design**: Follows the modern dark theme of the application

## Example Use Cases

1. **Admin Settings**: Add a "Design Templates" button in admin settings
2. **Template Selection**: Add an "Edit" button next to each template in selection screens
3. **Quick Access**: Add a keyboard shortcut (e.g., Ctrl+T) to open the designer
4. **Event Setup**: Allow event organizers to create custom templates on-the-fly

## Notes

- The overlay automatically handles unsaved changes warnings
- The template designer maintains full functionality within the overlay
- All existing template operations (save, load, export) work as expected
- The overlay can be styled to match your application theme