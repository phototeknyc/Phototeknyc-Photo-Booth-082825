// Check canvas items in database
// Run in C# Interactive or Immediate Window

var templateDb = new Photobooth.Database.TemplateDatabase();
var templates = templateDb.GetAllTemplates();

Console.WriteLine("=== Templates and their Canvas Items ===");
foreach (var template in templates)
{
    Console.WriteLine($"\nTemplate: {template.Name} (ID: {template.Id})");

    var items = templateDb.GetCanvasItems(template.Id);
    Console.WriteLine($"  Canvas Items: {items.Count}");

    foreach (var item in items)
    {
        Console.WriteLine($"    - {item.Name} ({item.ItemType}) at ({item.X}, {item.Y}) size: {item.Width}x{item.Height}");
        if (item.ItemType == "Placeholder")
        {
            Console.WriteLine($"      Placeholder Number: {item.PlaceholderNumber}");
        }
    }
}

// Also check if the items are being loaded when template is opened
Console.WriteLine("\n=== Testing Template Load ===");
var testTemplate = templates.FirstOrDefault();
if (testTemplate != null)
{
    Console.WriteLine($"Loading template: {testTemplate.Name}");
    var loadedItems = templateDb.GetCanvasItems(testTemplate.Id);
    Console.WriteLine($"Loaded {loadedItems.Count} items");
}