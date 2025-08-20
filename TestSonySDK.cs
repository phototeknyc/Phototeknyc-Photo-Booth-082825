using System;
using System.Runtime.InteropServices;
using CameraControl.Devices.Sony;
using static CameraControl.Devices.Sony.SonySDKWrapper;

class TestSonySDK
{
    static void Main()
    {
        Console.WriteLine("Testing Sony SDK...");
        Console.WriteLine("=====================================");
        
        try
        {
            // Try to initialize SDK
            Console.WriteLine("Step 1: Initializing Sony SDK...");
            bool initResult = SonySDKWrapper.Init();
            Console.WriteLine($"SDK Init Result: {initResult}");
            
            if (!initResult)
            {
                Console.WriteLine("Failed to initialize SDK. Check that Cr_Core.dll is present.");
                return;
            }
            
            // Try to enumerate cameras
            Console.WriteLine("\nStep 2: Enumerating cameras (10 second timeout)...");
            IntPtr cameraEnumPtr;
            var enumResult = SonySDKWrapper.EnumCameraObjects(out cameraEnumPtr, 10);
            
            Console.WriteLine($"Enumeration Result: {enumResult} ({SonySDKWrapper.GetErrorMessage(enumResult)})");
            Console.WriteLine($"Camera Enum Pointer: {cameraEnumPtr}");
            
            if (enumResult == CrError.CrError_None && cameraEnumPtr != IntPtr.Zero)
            {
                Console.WriteLine("\nSuccess! Camera(s) found.");
                
                // Try to read first camera info
                try
                {
                    var cameraInfo = (CrCameraObjectInfo)Marshal.PtrToStructure(cameraEnumPtr, typeof(CrCameraObjectInfo));
                    Console.WriteLine($"Camera Model: {cameraInfo.Model}");
                    Console.WriteLine($"Camera Name: {cameraInfo.Name}");
                    Console.WriteLine($"USB PID: 0x{cameraInfo.UsbPid:X4}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading camera info: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("\nNo cameras found or error occurred.");
                Console.WriteLine("Make sure:");
                Console.WriteLine("1. Camera is connected via USB");
                Console.WriteLine("2. Camera is turned ON");
                Console.WriteLine("3. Camera is in PC Remote mode");
                Console.WriteLine("4. libusbK driver is installed (check with Zadig)");
            }
            
            // Clean up
            Console.WriteLine("\nStep 3: Releasing SDK...");
            SonySDKWrapper.Release();
            Console.WriteLine("SDK Released.");
        }
        catch (DllNotFoundException ex)
        {
            Console.WriteLine($"DLL Not Found: {ex.Message}");
            Console.WriteLine("Make sure the Sony SDK DLLs are in the same directory:");
            Console.WriteLine("- Cr_Core.dll");
            Console.WriteLine("- monitor_protocol.dll");
            Console.WriteLine("- monitor_protocol_pf.dll");
            Console.WriteLine("- CrAdapter\\Cr_PTP_USB.dll");
            Console.WriteLine("- CrAdapter\\libusb-1.0.dll");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"Stack Trace:\n{ex.StackTrace}");
        }
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}