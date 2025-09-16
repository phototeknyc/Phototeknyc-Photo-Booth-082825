// Test template loading in editor
// Run this in C# Interactive or Immediate Window

// Check what's in the database
var templateDb = new Photobooth.Database.TemplateDatabase();
var template2 = templateDb.GetTemplate(2);

if (template2 != null)
{
    Console.WriteLine($"Template: {template2.Name} (ID: {template2.Id})");
    Console.WriteLine($"Canvas Size: {template2.CanvasWidth} x {template2.CanvasHeight}");

    var items = templateDb.GetCanvasItems(2);
    Console.WriteLine($"Canvas Items in DB: {items.Count}");

    foreach (var item in items)
    {
        Console.WriteLine($"  - Item ID: {item.Id}, Type: {item.ItemType}, Name: {item.Name}");
        Console.WriteLine($"    Position: ({item.X}, {item.Y}), Size: {item.Width} x {item.Height}");
        Console.WriteLine($"    PlaceholderNumber: {item.PlaceholderNumber}");
    }
}

// Check all templates
Console.WriteLine("\n=== All Templates ===");
var allTemplates = templateDb.GetAllTemplates();
foreach (var t in allTemplates)
{
    var itemCount = templateDb.GetCanvasItems(t.Id).Count;
    Console.WriteLine($"Template {t.Id} ({t.Name}): {itemCount} items");
}