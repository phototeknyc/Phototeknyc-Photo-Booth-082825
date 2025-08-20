using System;
using CameraControl.Devices.Classes;

namespace CameraControl.Devices.Wifi
{
    public class SonyProvider : IWifiDeviceProvider
    {
        public string Name { get; set; }
        public string DefaultIp { get; set; }
        
        public DeviceDescriptor Connect(string address)
        {
            // Sony WiFi camera support not implemented
            throw new NotImplementedException("Sony WiFi camera support is not available");
        }

        public SonyProvider()
        {
            Name = "Sony";
            DefaultIp = "<Auto>";
        }
    }
}