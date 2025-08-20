using System;
using System.Collections.Generic;
using System.Management;

class TestSonyUSBDetection
{
    static void Main()
    {
        Console.WriteLine("Testing Sony USB device detection...");
        
        var serialNumbers = GetSonyUSBSerialNumbers();
        Console.WriteLine($"Found {serialNumbers.Count} Sony USB devices:");
        
        foreach (var serial in serialNumbers)
        {
            Console.WriteLine($"  - Serial: {serial}");
        }
        
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
    
    private static List<string> GetSonyUSBSerialNumbers()
    {
        var serialNumbers = new List<string>();
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity"))
            {
                foreach (ManagementObject device in searcher.Get())
                {
                    var deviceId = device["DeviceID"] as string;
                    var name = device["Name"] as string;
                    
                    // Look for Sony USB devices (VID_054C)
                    if (!string.IsNullOrEmpty(deviceId) && deviceId.Contains("VID_054C"))
                    {
                        Console.WriteLine($"Found Sony device: {name} - {deviceId}");
                        
                        // Extract serial number from device ID
                        // Format is typically USB\VID_054C&PID_XXXX\SerialNumber
                        var parts = deviceId.Split('\\');
                        if (parts.Length >= 3)
                        {
                            var serialPart = parts[2];
                            if (!serialPart.Contains("&") && serialPart.Length > 0)
                            {
                                serialNumbers.Add(serialPart);
                                Console.WriteLine($"Extracted serial number: {serialPart}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error detecting USB devices: {ex.Message}");
        }
        return serialNumbers;
    }
}