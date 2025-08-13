using System;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;

namespace Photobooth.Test
{
    class CompareDEVMODE
    {
        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int DocumentProperties(IntPtr hWnd, IntPtr hPrinter, string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        private const int DM_OUT_BUFFER = 2;
        private const int DM_IN_PROMPT = 4;

        static void Main()
        {
            Console.WriteLine("DNP DEVMODE 2-Inch Cut Analysis Tool");
            Console.WriteLine("=====================================\n");
            
            string printerName = "DS40"; // Adjust if needed
            
            Console.WriteLine($"Analyzing printer: {printerName}\n");
            
            IntPtr hPrinter = IntPtr.Zero;
            IntPtr pDevMode1 = IntPtr.Zero;
            IntPtr pDevMode2 = IntPtr.Zero;
            
            try
            {
                // Open printer
                if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
                {
                    Console.WriteLine("Failed to open printer");
                    return;
                }
                
                // Get DEVMODE size
                int size = DocumentProperties(IntPtr.Zero, hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                if (size <= 0)
                {
                    Console.WriteLine("Failed to get DEVMODE size");
                    return;
                }
                
                Console.WriteLine($"DEVMODE size: {size} bytes\n");
                
                // Allocate memory
                pDevMode1 = Marshal.AllocHGlobal(size);
                
                // Get current DEVMODE
                int result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode1, IntPtr.Zero, DM_OUT_BUFFER);
                if (result < 0)
                {
                    Console.WriteLine("Failed to get DEVMODE");
                    return;
                }
                
                // Save current state
                byte[] beforeBytes = new byte[size];
                Marshal.Copy(pDevMode1, beforeBytes, 0, size);
                
                Console.WriteLine("STEP 1: Current DEVMODE captured");
                Console.WriteLine("Now please do the following:");
                Console.WriteLine("1. Open Printer Properties for DS40");
                Console.WriteLine("2. Go to Advanced -> DNP Advanced Options");
                Console.WriteLine("3. Toggle the '2inch cut' setting");
                Console.WriteLine("4. Click OK to save");
                Console.WriteLine("\nPress ENTER when done...");
                Console.ReadLine();
                
                // Allocate for second capture
                pDevMode2 = Marshal.AllocHGlobal(size);
                
                // Get DEVMODE after change
                result = DocumentProperties(IntPtr.Zero, hPrinter, printerName, pDevMode2, IntPtr.Zero, DM_OUT_BUFFER);
                if (result < 0)
                {
                    Console.WriteLine("Failed to get second DEVMODE");
                    return;
                }
                
                // Save after state
                byte[] afterBytes = new byte[size];
                Marshal.Copy(pDevMode2, afterBytes, 0, size);
                
                Console.WriteLine("\nSTEP 2: New DEVMODE captured");
                Console.WriteLine("\nAnalyzing differences...\n");
                
                // Find differences
                bool foundDifference = false;
                for (int i = 0; i < size; i++)
                {
                    if (beforeBytes[i] != afterBytes[i])
                    {
                        foundDifference = true;
                        Console.WriteLine($"Difference at offset 0x{i:X4} ({i}):");
                        Console.WriteLine($"  Before: 0x{beforeBytes[i]:X2} ({beforeBytes[i]})");
                        Console.WriteLine($"  After:  0x{afterBytes[i]:X2} ({afterBytes[i]})");
                        
                        // Show surrounding context
                        int start = Math.Max(0, i - 8);
                        int end = Math.Min(size, i + 8);
                        
                        Console.Write("  Context (before): ");
                        for (int j = start; j < end; j++)
                        {
                            if (j == i) Console.Write("[");
                            Console.Write($"{beforeBytes[j]:X2} ");
                            if (j == i) Console.Write("] ");
                        }
                        Console.WriteLine();
                        
                        Console.Write("  Context (after):  ");
                        for (int j = start; j < end; j++)
                        {
                            if (j == i) Console.Write("[");
                            Console.Write($"{afterBytes[j]:X2} ");
                            if (j == i) Console.Write("] ");
                        }
                        Console.WriteLine("\n");
                    }
                }
                
                if (!foundDifference)
                {
                    Console.WriteLine("NO DIFFERENCES FOUND!");
                    Console.WriteLine("This means the 2-inch cut setting is NOT stored in DEVMODE.");
                    Console.WriteLine("It may be stored in:");
                    Console.WriteLine("- Registry");
                    Console.WriteLine("- Printer configuration file");
                    Console.WriteLine("- Driver-specific storage");
                    Console.WriteLine("\nThe setting may only apply during the print job, not persistently.");
                }
                else
                {
                    Console.WriteLine($"Found differences in DEVMODE!");
                    Console.WriteLine("The 2-inch cut setting IS stored in DEVMODE.");
                    Console.WriteLine("\nTo use this in the app:");
                    Console.WriteLine("1. Save DEVMODE with 2-inch cut enabled");
                    Console.WriteLine("2. Restore this exact DEVMODE when printing");
                }
                
                // Save both DEVMODEs to files for analysis
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                File.WriteAllBytes(Path.Combine(desktopPath, "DEVMODE_before.bin"), beforeBytes);
                File.WriteAllBytes(Path.Combine(desktopPath, "DEVMODE_after.bin"), afterBytes);
                Console.WriteLine($"\nDEVMODE files saved to desktop for analysis.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                if (pDevMode1 != IntPtr.Zero) Marshal.FreeHGlobal(pDevMode1);
                if (pDevMode2 != IntPtr.Zero) Marshal.FreeHGlobal(pDevMode2);
                if (hPrinter != IntPtr.Zero) ClosePrinter(hPrinter);
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}