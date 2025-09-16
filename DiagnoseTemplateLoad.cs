// Diagnostic script to check template loading issues
// Run this in C# Interactive or Immediate Window

// 1. Check what's in the database for template 2
var templateDb = new Photobooth.Database.TemplateDatabase();
var template2 = templateDb.GetTemplate(2);

if (template2 != null)
{
    Console.WriteLine("=== DATABASE CHECK ===");
    Console.WriteLine($"Template: {template2.Name} (ID: {template2.Id})");
    Console.WriteLine($"Canvas Size: {template2.CanvasWidth} x {template2.CanvasHeight}");
    Console.WriteLine($"Background Color: {template2.BackgroundColor}");

    var items = templateDb.GetCanvasItems(2);
    Console.WriteLine($"\nCanvas Items in DB: {items.Count}");

    foreach (var item in items)
    {
        Console.WriteLine($"\n  Item #{item.Id}:");
        Console.WriteLine($"    Type: {item.ItemType}");
        Console.WriteLine($"    Name: {item.Name}");
        Console.WriteLine($"    Position: ({item.X}, {item.Y})");
        Console.WriteLine($"    Size: {item.Width} x {item.Height}");
        Console.WriteLine($"    Rotation: {item.Rotation}");
        Console.WriteLine($"    ZIndex: {item.ZIndex}");
        Console.WriteLine($"    IsVisible: {item.IsVisible}");
        Console.WriteLine($"    IsLocked: {item.IsLocked}");

        if (item.ItemType == "Placeholder")
        {
            Console.WriteLine($"    PlaceholderNumber: {item.PlaceholderNumber}");
            Console.WriteLine($"    PlaceholderColor: {item.PlaceholderColor}");
        }
    }
}

// 2. Try to load the template using TemplateService
Console.WriteLine("\n=== TEMPLATE SERVICE LOAD TEST ===");
var templateService = new Photobooth.Services.TemplateService();
bool loaded = templateService.LoadTemplate(2, (template, canvasItems) =>
{
    Console.WriteLine($"Template loaded: {template.Name}");
    Console.WriteLine($"Canvas items created: {canvasItems.Count}");

    foreach (var item in canvasItems)
    {
        Console.WriteLine($"\n  Canvas Item:");
        Console.WriteLine($"    Type: {item.GetType().Name}");

        if (item is DesignerCanvas.Controls.IBoxCanvasItem boxItem)
        {
            Console.WriteLine($"    Position: ({boxItem.Left}, {boxItem.Top})");
            Console.WriteLine($"    Size: {boxItem.Width} x {boxItem.Height}");
        }

        if (item is DesignerCanvas.Controls.PlaceholderCanvasItem placeholder)
        {
            Console.WriteLine($"    PlaceholderNo: {placeholder.PlaceholderNo}");
            Console.WriteLine($"    Background: {placeholder.Background}");
            Console.WriteLine($"    Visibility: {placeholder.Visibility}");
            Console.WriteLine($"    Opacity: {placeholder.Opacity}");
        }
    }
});

Console.WriteLine($"\nLoad result: {loaded}");

// 3. Check the current canvas
Console.WriteLine("\n=== CURRENT CANVAS CHECK ===");
var mainWindow = System.Windows.Application.Current.MainWindow;
if (mainWindow != null)
{
    // Try to find the designer canvas - adjust the path based on your UI structure
    var designerVM = mainWindow.DataContext as Photobooth.MVVM.ViewModels.Designer.DesignerVM;
    if (designerVM != null && designerVM.CustomDesignerCanvas != null)
    {
        var canvas = designerVM.CustomDesignerCanvas;
        Console.WriteLine($"Canvas found: {canvas.Name ?? "Unnamed"}");
        Console.WriteLine($"Canvas size: {canvas.Width} x {canvas.Height}");
        Console.WriteLine($"Canvas items count: {canvas.Items.Count}");
        Console.WriteLine($"Canvas background: {canvas.Background}");

        foreach (var item in canvas.Items)
        {
            Console.WriteLine($"  - Item type: {item.GetType().Name}");
            if (item is DesignerCanvas.Controls.IBoxCanvasItem boxItem)
            {
                Console.WriteLine($"    Position: ({boxItem.Left}, {boxItem.Top}), Size: {boxItem.Width} x {boxItem.Height}");
            }
        }
    }
    else
    {
        Console.WriteLine("Could not find DesignerVM or Canvas");
    }
}

// 4. Test creating a placeholder directly
Console.WriteLine("\n=== DIRECT PLACEHOLDER CREATION TEST ===");
try
{
    var testPlaceholder = new DesignerCanvas.Controls.PlaceholderCanvasItem();
    testPlaceholder.PlaceholderNo = 1;
    testPlaceholder.Width = 100;
    testPlaceholder.Height = 100;
    testPlaceholder.Left = 10;
    testPlaceholder.Top = 10;

    Console.WriteLine($"Created test placeholder: {testPlaceholder.PlaceholderNo}");
    Console.WriteLine($"Size: {testPlaceholder.Width} x {testPlaceholder.Height}");
    Console.WriteLine($"Position: ({testPlaceholder.Left}, {testPlaceholder.Top})");
    Console.WriteLine($"Background: {testPlaceholder.Background}");

    // Try to add it to canvas if we have access
    if (designerVM != null && designerVM.CustomDesignerCanvas != null)
    {
        designerVM.CustomDesignerCanvas.Items.Add(testPlaceholder);
        Console.WriteLine("Added test placeholder to canvas");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error creating placeholder: {ex.Message}");
}

Console.WriteLine("\n=== DIAGNOSTIC COMPLETE ===");