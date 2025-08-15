using System;
using QRCoder;

namespace Photobooth.Test
{
    /// <summary>
    /// Simple test to verify QRCoder works correctly
    /// </summary>
    public class QRCodeTest
    {
        public static void TestQRGeneration()
        {
            try
            {
                Console.WriteLine("Testing QRCoder library...");
                
                using (var qrGenerator = new QRCodeGenerator())
                {
                    var qrCodeData = qrGenerator.CreateQrCode("https://example.com", QRCodeGenerator.ECCLevel.Q);
                    using (var qrCode = new QRCode(qrCodeData))
                    {
                        using (var bitmap = qrCode.GetGraphic(10))
                        {
                            Console.WriteLine($"QR Code generated successfully. Size: {bitmap.Width}x{bitmap.Height}");
                            Console.WriteLine("QRCoder library is working correctly!");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"QRCoder test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}