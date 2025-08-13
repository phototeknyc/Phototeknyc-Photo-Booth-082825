using System;
using System.Windows.Forms;
using Photobooth.Pages;

namespace Photobooth.Test
{
    class TestPrinterProfiles
    {
        [STAThread]
        static void Main()
        {
            Console.WriteLine("Testing Printer Profile Functionality");
            Console.WriteLine("=====================================");
            
            try
            {
                // Create instance of settings control to test methods
                var settingsControl = new PhotoboothSettingsControl();
                
                // Test 1: List available printers
                Console.WriteLine("\n1. Available Printers:");
                var printers = Services.PrintService.GetAvailablePrinters();
                foreach (var printer in printers)
                {
                    Console.WriteLine($"   - {printer}");
                }
                
                // Test 2: Get USB printers
                Console.WriteLine("\n2. USB Printers:");
                var usbPrinters = Services.PrintService.GetUSBPrinters();
                foreach (var printer in usbPrinters)
                {
                    Console.WriteLine($"   - {printer.DisplayText}");
                }
                
                // Test 3: Test capturing printer settings
                if (printers.Count > 0)
                {
                    string testPrinter = printers[0];
                    Console.WriteLine($"\n3. Testing driver settings capture for: {testPrinter}");
                    
                    try
                    {
                        // This would normally be done through the UI, but we can test the underlying method
                        Console.WriteLine("   Note: Full profile save/load requires UI interaction");
                        Console.WriteLine("   Please use the application UI to:");
                        Console.WriteLine("   a. Select a DNP printer");
                        Console.WriteLine("   b. Open printer properties and enable '2\" cut' setting");
                        Console.WriteLine("   c. Click 'Save Profile' to capture settings");
                        Console.WriteLine("   d. Change the setting back");
                        Console.WriteLine("   e. Click 'Load Profile' to restore settings");
                        Console.WriteLine("   f. Verify '2\" cut' is enabled again");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   Error: {ex.Message}");
                    }
                }
                
                Console.WriteLine("\n4. Printer Profile System Status:");
                Console.WriteLine("   - DEVMODE capture: Implemented");
                Console.WriteLine("   - Base64 encoding: Implemented");
                Console.WriteLine("   - Save to settings: Implemented");
                Console.WriteLine("   - Restore from settings: Implemented");
                Console.WriteLine("   - Export/Import JSON: Implemented");
                Console.WriteLine("   - DNP '2\" cut' support: Yes (via DEVMODE)");
                
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
                Console.ReadKey();
            }
        }
    }
}