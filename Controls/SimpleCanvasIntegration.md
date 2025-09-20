# SimpleDesignerCanvas Integration Guide

## Overview

The SimpleDesignerCanvas system provides a new, independent canvas implementation that avoids the freezing issues with the old DesignerCanvas system. It's designed to integrate seamlessly with the existing TouchTemplateDesigner interface while providing reliable manipulation capabilities.

## Key Components

### 1. SimpleCanvasItem (Base Class)
- **File**: `Controls/SimpleCanvasItem.cs`
- **Purpose**: Base class for all canvas items with drag, resize, and selection capabilities
- **Features**:
  - Direct mouse manipulation (no adorners)
  - Built-in selection handles
  - Property change notifications
  - Z-index management

### 2. SimpleTextItem
- **File**: `Controls/SimpleTextItem.cs`
- **Purpose**: Text items with full typography control
- **Features**:
  - Font family, size, weight, style
  - Text color and alignment
  - Auto-sizing to content
  - Integration with FontControlsPanel

### 3. SimpleImageItem
- **File**: `Controls/SimpleImageItem.cs`
- **Purpose**: Image and placeholder items
- **Features**:
  - Image loading from file or byte array
  - Placeholder mode for photo slots
  - Drag-and-drop support
  - Multiple image format support

### 4. SimpleDesignerCanvas
- **Files**: `Controls/SimpleDesignerCanvas.xaml`, `Controls/SimpleDesignerCanvas.xaml.cs`
- **Purpose**: Main canvas control that manages items
- **Features**:
  - Item collection management
  - Selection handling
  - Grid display (optional)
  - Drag-and-drop support

### 5. Integration Adapters
- **LayersAdapter**: `Controls/SimpleCanvasLayersAdapter.cs`
- **FontAdapter**: `Controls/SimpleCanvasFontAdapter.cs`
- **Purpose**: Allow existing LayersPanel and FontControlsPanel to work with new canvas

## Integration with TouchTemplateDesigner

### Step 1: Replace the Old Canvas

In `TouchTemplateDesigner.xaml`, replace the old DesignerCanvas reference:

```xml
<!-- OLD: -->
<dc:TouchEnabledCanvas x:Name="DesignerCanvas"
                      Width="600" Height="1800"
                      Background="White"
                      ClipToBounds="True"/>

<!-- NEW: -->
<local:SimpleDesignerCanvas x:Name="SimpleCanvas"
                           Width="600" Height="1800"
                           ShowGrid="False"/>
```

### Step 2: Update the Code-Behind

In `TouchTemplateDesigner.xaml.cs`, initialize the adapters:

```csharp
private SimpleDesignerCanvas _simpleCanvas;
private SimpleCanvasLayersAdapter _layersAdapter;
private SimpleCanvasFontAdapter _fontAdapter;

private void InitializeDesigner()
{
    // Get reference to the new canvas
    _simpleCanvas = SimpleCanvas;

    // Create adapters to connect with existing panels
    _layersAdapter = new SimpleCanvasLayersAdapter(_simpleCanvas, LayersPanel);
    _fontAdapter = new SimpleCanvasFontAdapter(_simpleCanvas, FontControlsPanel);

    // Optional: Set up canvas properties
    _simpleCanvas.ShowGrid = Properties.Settings.Default.ShowGrid;
    _simpleCanvas.GridSize = 20;
}
```

### Step 3: Update Button Event Handlers

Replace the old canvas operations with new SimpleCanvas methods:

```csharp
private void AddText_Click(object sender, RoutedEventArgs e)
{
    var textItem = _simpleCanvas.AddTextItem("New Text");
    _simpleCanvas.SelectItem(textItem);
}

private void AddPlaceholder_Click(object sender, RoutedEventArgs e)
{
    var placeholder = _simpleCanvas.AddImageItem(isPlaceholder: true);
    _simpleCanvas.SelectItem(placeholder);
}

private void DeleteSelected_Click(object sender, RoutedEventArgs e)
{
    _simpleCanvas.RemoveSelectedItem();
}

private void BringToFront_Click(object sender, RoutedEventArgs e)
{
    _simpleCanvas.BringToFront();
}

private void SendToBack_Click(object sender, RoutedEventArgs e)
{
    _simpleCanvas.SendToBack();
}
```

### Step 4: Template Loading/Saving

Update template serialization to work with SimpleCanvas items:

```csharp
public void LoadTemplate(TemplateData template)
{
    _simpleCanvas.ClearAllItems();

    foreach (var itemData in template.Items)
    {
        SimpleCanvasItem item = null;

        switch (itemData.Type)
        {
            case "Text":
                item = new SimpleTextItem(itemData.Text)
                {
                    FontFamily = new FontFamily(itemData.FontFamily),
                    FontSize = itemData.FontSize,
                    TextColor = (Brush)new BrushConverter().ConvertFromString(itemData.Color)
                };
                break;

            case "Image":
                item = new SimpleImageItem();
                if (!string.IsNullOrEmpty(itemData.ImagePath))
                {
                    ((SimpleImageItem)item).LoadImage(itemData.ImagePath);
                }
                break;

            case "Placeholder":
                item = new SimpleImageItem(isPlaceholder: true);
                break;
        }

        if (item != null)
        {
            item.Left = itemData.Left;
            item.Top = itemData.Top;
            item.Width = itemData.Width;
            item.Height = itemData.Height;
            item.ZIndex = itemData.ZIndex;

            _simpleCanvas.AddItem(item);
        }
    }
}

public TemplateData SaveTemplate()
{
    var template = new TemplateData();

    foreach (var item in _simpleCanvas.Items)
    {
        var itemData = new TemplateItemData
        {
            Left = item.Left,
            Top = item.Top,
            Width = item.Width,
            Height = item.Height,
            ZIndex = item.ZIndex
        };

        switch (item)
        {
            case SimpleTextItem textItem:
                itemData.Type = "Text";
                itemData.Text = textItem.Text;
                itemData.FontFamily = textItem.FontFamily.Source;
                itemData.FontSize = textItem.FontSize;
                itemData.Color = textItem.TextColor.ToString();
                break;

            case SimpleImageItem imageItem:
                itemData.Type = imageItem.IsPlaceholder ? "Placeholder" : "Image";
                itemData.ImagePath = imageItem.ImagePath;
                // Could also serialize image data as base64
                break;
        }

        template.Items.Add(itemData);
    }

    return template;
}
```

## Key Differences from Old System

### What's Better
1. **No Freezing**: Direct manipulation without complex adorner system
2. **Simpler**: Fewer dependencies and cleaner code
3. **Touch-Friendly**: Built-in touch support
4. **Reliable**: Tested manipulation patterns
5. **Independent**: No external DesignerCanvas library dependency

### What's Different
1. **Selection Handles**: Simple rectangles instead of complex adorners
2. **Manipulation**: Direct mouse events instead of manipulation processors
3. **Architecture**: Self-contained instead of external library
4. **Events**: Simplified event model

## Migration Checklist

- [ ] Replace canvas XAML reference
- [ ] Update code-behind initialization
- [ ] Replace button event handlers
- [ ] Update template loading/saving
- [ ] Test all manipulation features
- [ ] Test LayersPanel integration
- [ ] Test FontControlsPanel integration
- [ ] Verify touch functionality
- [ ] Test drag-and-drop
- [ ] Verify Z-order operations

## Example Usage

See `Controls/SimpleDesignerCanvasExample.cs` for a complete working example that demonstrates all integration patterns.

## Troubleshooting

### Canvas Not Responding
- Ensure proper event handler wiring in initialization
- Check that adapters are created after panels are loaded

### LayersPanel Not Updating
- Verify LayersAdapter is created and connected
- Check that LayersPanel.Layers collection is being used

### FontControlsPanel Not Working
- Ensure FontAdapter is connected
- Verify text item is selected when accessing font controls

### Performance Issues
- Consider limiting the number of items on canvas
- Use virtualization for large item counts
- Optimize selection handle updates

## Future Enhancements

1. **Undo/Redo System**: Command pattern for operations
2. **Grouping**: Multi-select and group operations
3. **Snapping**: Grid and object snapping
4. **Rulers**: Visual measurement aids
5. **Templates**: Reusable item templates
6. **Export**: High-quality image export