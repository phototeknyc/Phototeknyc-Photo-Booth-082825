# Responsive UI Layout System Design

## Overview
Enhanced customization system supporting multiple screen sizes and orientations without requiring separate designs for each.

## Responsive Strategies

### Option 1: Anchor-Based System (Recommended)
**Best for: Simplicity and predictable behavior**

```csharp
public class ResponsiveUIElement
{
    // Positioning
    public AnchorPoint Anchor { get; set; } // TopLeft, Center, BottomRight, etc.
    public Vector2 AnchorOffset { get; set; } // Offset from anchor in %
    
    // Sizing
    public SizeMode SizeMode { get; set; } // Fixed, Relative, Stretch
    public Size RelativeSize { get; set; } // As % of screen
    public Size MinSize { get; set; } // Minimum pixels
    public Size MaxSize { get; set; } // Maximum pixels
    
    // Margins
    public Thickness RelativeMargins { get; set; } // As % of screen
}

public enum AnchorPoint
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, Center, MiddleRight,
    BottomLeft, BottomCenter, BottomRight,
    Custom // Uses X,Y percentages
}

public enum SizeMode
{
    Fixed,      // Always same pixel size
    Relative,   // Percentage of screen
    Stretch,    // Fill available space
    AspectFit   // Scale maintaining aspect ratio
}
```

#### Example Usage:
- **Start Button**: Anchor=BottomCenter, RelativeSize=20%x10%, MinSize=200x80
- **Logo**: Anchor=TopLeft, SizeMode=AspectFit, MaxSize=300x150
- **Background**: Anchor=Center, SizeMode=Stretch

### Option 2: Grid-Based System
**Best for: Complex layouts with multiple elements**

```csharp
public class GridLayout
{
    public int Columns { get; set; }
    public int Rows { get; set; }
    public List<GridDefinition> ColumnDefinitions { get; set; }
    public List<GridDefinition> RowDefinitions { get; set; }
}

public class GridDefinition
{
    public GridSizeType Type { get; set; } // Fixed, Star, Auto
    public double Value { get; set; }
}

public class GridUIElement : ResponsiveUIElement
{
    public int Column { get; set; }
    public int Row { get; set; }
    public int ColumnSpan { get; set; }
    public int RowSpan { get; set; }
}
```

### Option 3: Breakpoint System
**Best for: Dramatically different layouts per device**

```csharp
public class BreakpointLayout
{
    public List<Breakpoint> Breakpoints { get; set; }
}

public class Breakpoint
{
    public string Name { get; set; } // "Phone", "Tablet", "Desktop"
    public Size MinSize { get; set; }
    public Size MaxSize { get; set; }
    public Orientation? Orientation { get; set; }
    public UILayout Layout { get; set; }
}

// Usage
var breakpoints = new List<Breakpoint>
{
    new Breakpoint 
    { 
        Name = "Portrait Phone",
        MaxSize = new Size(768, double.MaxValue),
        Orientation = Orientation.Portrait,
        Layout = portraitPhoneLayout
    },
    new Breakpoint
    {
        Name = "Landscape Tablet", 
        MinSize = new Size(768, 0),
        Orientation = Orientation.Landscape,
        Layout = landscapeTabletLayout
    }
};
```

## Recommended Hybrid Approach

Combine **Anchor-Based** (primary) with **Optional Breakpoints** (overrides):

```csharp
public class ResponsiveUILayout
{
    // Base responsive layout using anchors
    public UILayout BaseLayout { get; set; }
    
    // Optional orientation-specific adjustments
    public UILayoutAdjustment PortraitAdjustment { get; set; }
    public UILayoutAdjustment LandscapeAdjustment { get; set; }
    
    // Optional size-specific breakpoints
    public List<Breakpoint> Breakpoints { get; set; }
}

public class UILayoutAdjustment
{
    // Only specify what changes
    public Dictionary<string, ElementAdjustment> ElementAdjustments { get; set; }
}

public class ElementAdjustment
{
    public Vector2? AnchorOffsetDelta { get; set; }
    public Size? RelativeSizeDelta { get; set; }
    public bool? Hidden { get; set; }
    public int? ZIndexOverride { get; set; }
}
```

## Implementation in Designer

### Visual Helpers
1. **Device Preview Dropdown**
   - Common sizes: 1920x1080, 1024x768, 768x1024
   - Rotate button for orientation
   - Custom size input

2. **Anchor Visualization**
   - Show anchor lines when element selected
   - Highlight anchor point on canvas
   - Display percentage offsets

3. **Responsive Handles**
   - Different color for relative vs fixed sizing
   - Show min/max size constraints
   - Preview scaling in real-time

### Designer UI Mockup
```
┌─────────────────────────────────────────┐
│ [Device: iPad] [1024x768] [🔄 Rotate]   │
├─────────────────────────────────────────┤
│                                         │
│  ┌─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┐   │
│         Design Canvas              │    │
│  │                                 │    │
│    ╔════════════╗                       │
│  │ ║   Button   ║ ← Anchored 80%,90%│   │
│    ╚════════════╝                       │
│  └─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─┘   │
│                                         │
│ Properties Panel:                       │
│ ┌─────────────────────────────┐        │
│ │ Anchor: BottomCenter       ▼│        │
│ │ Offset X: 0%  Y: -10%       │        │
│ │ Size Mode: Relative         ▼│        │
│ │ Width: 20%  Height: 10%     │        │
│ │ Min W: 200px  Min H: 80px   │        │
│ └─────────────────────────────┘        │
└─────────────────────────────────────────┘
```

## Automatic Responsive Behaviors

### Smart Defaults
```csharp
public class ResponsiveDefaults
{
    public static ResponsiveUIElement CreateButton(string text)
    {
        return new ResponsiveUIElement
        {
            Anchor = AnchorPoint.BottomCenter,
            SizeMode = SizeMode.Relative,
            RelativeSize = new Size(0.2, 0.1), // 20% x 10%
            MinSize = new Size(150, 60),
            MaxSize = new Size(400, 120)
        };
    }
    
    public static ResponsiveUIElement CreateLogo()
    {
        return new ResponsiveUIElement
        {
            Anchor = AnchorPoint.TopCenter,
            SizeMode = SizeMode.AspectFit,
            RelativeSize = new Size(0.3, 0.15),
            MaxSize = new Size(500, 250)
        };
    }
}
```

### Layout Rules Engine
```csharp
public class LayoutRules
{
    public void ApplyRules(UILayout layout, Size screenSize)
    {
        // Stack buttons vertically in portrait
        if (screenSize.Height > screenSize.Width)
        {
            StackVertically(layout.Buttons);
        }
        
        // Hide decorative elements on small screens
        if (screenSize.Width < 800)
        {
            HideDecorativeElements(layout);
        }
        
        // Increase touch target sizes for tablets
        if (IsTablet(screenSize))
        {
            EnlargeTouchTargets(layout);
        }
    }
}
```

## Comparison Table

| Feature | Separate Layouts | Anchor-Based | Grid-Based | Breakpoints |
|---------|-----------------|--------------|------------|-------------|
| Setup Complexity | High | Low | Medium | Medium |
| Flexibility | Highest | Good | Good | High |
| Maintenance | Difficult | Easy | Easy | Medium |
| File Size | Large | Small | Small | Medium |
| Performance | Best | Good | Good | Good |
| Learning Curve | Easy | Easy | Medium | Medium |

## Recommended Solution

**Use Anchor-Based with Optional Overrides:**

1. **Single Base Design** - Create once, works everywhere
2. **Percentage-Based** - Elements scale proportionally
3. **Min/Max Constraints** - Ensure usability limits
4. **Orientation Adjustments** - Fine-tune without full redesign
5. **Breakpoint Escape Hatch** - Complete override when needed

## Example Configuration

```json
{
  "name": "Modern Photobooth Layout",
  "version": "1.0",
  "baseLayout": {
    "elements": [
      {
        "id": "startButton",
        "type": "Button",
        "anchor": "BottomCenter",
        "anchorOffset": { "x": 0, "y": -10 },
        "sizeMode": "Relative",
        "relativeSize": { "width": 25, "height": 12 },
        "minSize": { "width": 200, "height": 80 },
        "content": "START"
      },
      {
        "id": "logo",
        "type": "Image",
        "anchor": "TopCenter",
        "anchorOffset": { "x": 0, "y": 5 },
        "sizeMode": "AspectFit",
        "maxSize": { "width": 400, "height": 200 }
      }
    ]
  },
  "portraitAdjustments": {
    "startButton": {
      "relativeSize": { "width": 60, "height": 10 }
    }
  },
  "landscapeAdjustments": {
    "startButton": {
      "relativeSize": { "width": 20, "height": 15 }
    }
  }
}
```

## Benefits of This Approach

1. **Single Source of Truth** - One layout to maintain
2. **Predictable Scaling** - Elements maintain relationships
3. **Touch-Friendly** - Min sizes ensure usability
4. **Designer-Friendly** - Visual anchor system
5. **Future-Proof** - Works on any screen size
6. **Efficient** - No duplicate assets or layouts
7. **Testable** - Preview any size/orientation instantly

## Migration Path

For existing fixed layouts:
1. Analyze current element positions
2. Convert to nearest anchor points
3. Calculate percentage offsets
4. Set appropriate min/max constraints
5. Test on various screen sizes
6. Add orientation adjustments if needed