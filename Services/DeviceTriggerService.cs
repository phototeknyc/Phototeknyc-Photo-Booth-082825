using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Text;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace Photobooth.Services
{
    public enum TriggerType
    {
        Serial,
        Bluetooth,
        HTTP,
        TCP,
        WebSocket
    }

    public enum TriggerEvent
    {
        SessionStart,
        SessionEnd,
        PhotoCapture,
        PhotoCaptured,
        CountdownStart,
        CountdownTick,
        PrintStart,
        PrintComplete,
        VideoStart,
        VideoStop,
        GifCreated,
        FilterApplied
    }

    public class DeviceTrigger
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public TriggerType Type { get; set; }
        public string ConnectionString { get; set; } // COM port, Bluetooth MAC, URL, etc.
        public Dictionary<TriggerEvent, string> Commands { get; set; }
        public bool IsEnabled { get; set; }
        public int DelayMs { get; set; } // Delay before sending command
        
        public DeviceTrigger()
        {
            Commands = new Dictionary<TriggerEvent, string>();
            Id = Guid.NewGuid().ToString();
            IsEnabled = true;
        }
    }

    public class DeviceTriggerService
    {
        private static DeviceTriggerService _instance;
        public static DeviceTriggerService Instance => _instance ?? (_instance = new DeviceTriggerService());

        private List<DeviceTrigger> _triggers;
        private Dictionary<string, SerialPort> _serialPorts;
        private Dictionary<string, BluetoothClient> _bluetoothClients;
        private DispatcherTimer _reconnectTimer;
        
        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> DeviceConnected;
        public event EventHandler<string> DeviceDisconnected;
        public event EventHandler<Exception> ErrorOccurred;

        private DeviceTriggerService()
        {
            _triggers = new List<DeviceTrigger>();
            _serialPorts = new Dictionary<string, SerialPort>();
            _bluetoothClients = new Dictionary<string, BluetoothClient>();
            
            LoadTriggers();
            InitializeReconnectTimer();
        }

        private void InitializeReconnectTimer()
        {
            _reconnectTimer = new DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(30);
            _reconnectTimer.Tick += async (s, e) => await ReconnectDisconnectedDevices();
            _reconnectTimer.Start();
        }

        #region Trigger Management

        public void AddTrigger(DeviceTrigger trigger)
        {
            _triggers.Add(trigger);
            SaveTriggers();
            
            if (trigger.IsEnabled)
            {
                _ = ConnectDevice(trigger);
            }
        }

        public void RemoveTrigger(string triggerId)
        {
            var trigger = _triggers.FirstOrDefault(t => t.Id == triggerId);
            if (trigger != null)
            {
                DisconnectDevice(trigger);
                _triggers.Remove(trigger);
                SaveTriggers();
            }
        }

        public void UpdateTrigger(DeviceTrigger trigger)
        {
            var existing = _triggers.FirstOrDefault(t => t.Id == trigger.Id);
            if (existing != null)
            {
                DisconnectDevice(existing);
                _triggers.Remove(existing);
                _triggers.Add(trigger);
                SaveTriggers();
                
                if (trigger.IsEnabled)
                {
                    _ = ConnectDevice(trigger);
                }
            }
        }

        public List<DeviceTrigger> GetTriggers()
        {
            return _triggers.ToList();
        }

        #endregion

        #region Device Connection

        private async Task ConnectDevice(DeviceTrigger trigger)
        {
            try
            {
                switch (trigger.Type)
                {
                    case TriggerType.Serial:
                        ConnectSerialDevice(trigger);
                        break;
                    case TriggerType.Bluetooth:
                        await ConnectBluetoothDevice(trigger);
                        break;
                    case TriggerType.HTTP:
                    case TriggerType.TCP:
                    case TriggerType.WebSocket:
                        // HTTP, TCP and WebSocket don't need persistent connections here
                        DeviceConnected?.Invoke(this, $"{trigger.Name} ready");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to connect {trigger.Name}: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        private void ConnectSerialDevice(DeviceTrigger trigger)
        {
            try
            {
                if (_serialPorts.ContainsKey(trigger.Id))
                {
                    _serialPorts[trigger.Id]?.Close();
                    _serialPorts.Remove(trigger.Id);
                }

                var port = new SerialPort(trigger.ConnectionString);
                
                // Parse connection string for advanced settings
                // Format: "COM3:9600,8,N,1" or just "COM3"
                var parts = trigger.ConnectionString.Split(':');
                if (parts.Length > 1)
                {
                    var settings = parts[1].Split(',');
                    if (settings.Length >= 1) port.BaudRate = int.Parse(settings[0]);
                    if (settings.Length >= 2) port.DataBits = int.Parse(settings[1]);
                    if (settings.Length >= 3)
                    {
                        switch (settings[2])
                        {
                            case "N": port.Parity = Parity.None; break;
                            case "E": port.Parity = Parity.Even; break;
                            case "O": port.Parity = Parity.Odd; break;
                        }
                    }
                    if (settings.Length >= 4)
                    {
                        switch (settings[3])
                        {
                            case "1": port.StopBits = StopBits.One; break;
                            case "1.5": port.StopBits = StopBits.OnePointFive; break;
                            case "2": port.StopBits = StopBits.Two; break;
                        }
                    }
                }
                else
                {
                    // Default Arduino settings
                    port.BaudRate = 9600;
                    port.DataBits = 8;
                    port.Parity = Parity.None;
                    port.StopBits = StopBits.One;
                }

                port.Open();
                _serialPorts[trigger.Id] = port;
                
                DeviceConnected?.Invoke(this, $"{trigger.Name} connected on {parts[0]}");
                Debug.WriteLine($"Serial device connected: {trigger.Name} on {parts[0]}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Serial connection failed: {ex.Message}");
                throw;
            }
        }

        private async Task ConnectBluetoothDevice(DeviceTrigger trigger)
        {
            try
            {
                if (_bluetoothClients.ContainsKey(trigger.Id))
                {
                    _bluetoothClients[trigger.Id]?.Close();
                    _bluetoothClients.Remove(trigger.Id);
                }

                var client = new BluetoothClient();
                
                // Parse Bluetooth address - supports both MAC format (AA:BB:CC:DD:EE:FF) and standard format
                var addressString = trigger.ConnectionString.Replace(":", "").Replace("-", "");
                if (addressString.Length != 12)
                {
                    throw new ArgumentException($"Invalid Bluetooth address: {trigger.ConnectionString}");
                }
                
                var address = BluetoothAddress.Parse(addressString);
                
                // Try to connect with Serial Port Profile (SPP) - most common for Arduino/ESP32
                var ep = new BluetoothEndPoint(address, BluetoothService.SerialPort);
                await Task.Run(() => client.Connect(ep));
                
                _bluetoothClients[trigger.Id] = client;
                
                DeviceConnected?.Invoke(this, $"{trigger.Name} connected via Bluetooth");
                Debug.WriteLine($"Bluetooth device connected: {trigger.Name} at {trigger.ConnectionString}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bluetooth connection failed: {ex.Message}");
                throw;
            }
        }

        private void DisconnectDevice(DeviceTrigger trigger)
        {
            try
            {
                switch (trigger.Type)
                {
                    case TriggerType.Serial:
                        if (_serialPorts.ContainsKey(trigger.Id))
                        {
                            _serialPorts[trigger.Id]?.Close();
                            _serialPorts.Remove(trigger.Id);
                        }
                        break;
                    case TriggerType.Bluetooth:
                        if (_bluetoothClients.ContainsKey(trigger.Id))
                        {
                            _bluetoothClients[trigger.Id]?.Close();
                            _bluetoothClients.Remove(trigger.Id);
                        }
                        break;
                }
                
                DeviceDisconnected?.Invoke(this, $"{trigger.Name} disconnected");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disconnecting device: {ex.Message}");
            }
        }

        private async Task ReconnectDisconnectedDevices()
        {
            foreach (var trigger in _triggers.Where(t => t.IsEnabled))
            {
                try
                {
                    bool needsReconnect = false;
                    
                    switch (trigger.Type)
                    {
                        case TriggerType.Serial:
                            if (!_serialPorts.ContainsKey(trigger.Id) || !_serialPorts[trigger.Id].IsOpen)
                                needsReconnect = true;
                            break;
                        case TriggerType.Bluetooth:
                            if (!_bluetoothClients.ContainsKey(trigger.Id) || !_bluetoothClients[trigger.Id].Connected)
                                needsReconnect = true;
                            break;
                    }
                    
                    if (needsReconnect)
                    {
                        Debug.WriteLine($"Attempting to reconnect {trigger.Name}");
                        await ConnectDevice(trigger);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Reconnection failed for {trigger.Name}: {ex.Message}");
                }
            }
        }

        #endregion

        #region Event Triggering

        public async Task FireTriggerEvent(TriggerEvent triggerEvent, Dictionary<string, string> parameters = null)
        {
            var tasks = new List<Task>();
            
            foreach (var trigger in _triggers.Where(t => t.IsEnabled && t.Commands.ContainsKey(triggerEvent)))
            {
                tasks.Add(SendTriggerCommand(trigger, triggerEvent, parameters));
            }
            
            await Task.WhenAll(tasks);
        }

        private async Task SendTriggerCommand(DeviceTrigger trigger, TriggerEvent triggerEvent, Dictionary<string, string> parameters)
        {
            try
            {
                if (trigger.DelayMs > 0)
                {
                    await Task.Delay(trigger.DelayMs);
                }

                var command = trigger.Commands[triggerEvent];
                
                // Replace parameters in command
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command = command.Replace($"{{{param.Key}}}", param.Value);
                    }
                }

                switch (trigger.Type)
                {
                    case TriggerType.Serial:
                        await SendSerialCommand(trigger, command);
                        break;
                    case TriggerType.Bluetooth:
                        await SendBluetoothCommand(trigger, command);
                        break;
                    case TriggerType.HTTP:
                        await SendHttpCommand(trigger, command);
                        break;
                    case TriggerType.TCP:
                        await SendTcpCommand(trigger, command);
                        break;
                    case TriggerType.WebSocket:
                        await SendWebSocketCommand(trigger, command);
                        break;
                }
                
                StatusChanged?.Invoke(this, $"Triggered {trigger.Name}: {triggerEvent}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error triggering {trigger.Name}: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        private async Task SendSerialCommand(DeviceTrigger trigger, string command)
        {
            if (_serialPorts.ContainsKey(trigger.Id) && _serialPorts[trigger.Id].IsOpen)
            {
                await Task.Run(() =>
                {
                    _serialPorts[trigger.Id].WriteLine(command);
                    Debug.WriteLine($"Serial command sent to {trigger.Name}: {command}");
                });
            }
            else
            {
                throw new InvalidOperationException($"Serial port not connected for {trigger.Name}");
            }
        }

        private async Task SendBluetoothCommand(DeviceTrigger trigger, string command)
        {
            if (_bluetoothClients.ContainsKey(trigger.Id) && _bluetoothClients[trigger.Id].Connected)
            {
                var stream = _bluetoothClients[trigger.Id].GetStream();
                var data = Encoding.UTF8.GetBytes(command + "\n");
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
                Debug.WriteLine($"Bluetooth command sent to {trigger.Name}: {command}");
            }
            else
            {
                throw new InvalidOperationException($"Bluetooth not connected for {trigger.Name}");
            }
        }

        private async Task SendWebSocketCommand(DeviceTrigger trigger, string command)
        {
            // Simple WebSocket implementation for IoT devices
            using (var client = new System.Net.WebSockets.ClientWebSocket())
            {
                await client.ConnectAsync(new Uri(trigger.ConnectionString), System.Threading.CancellationToken.None);
                var data = Encoding.UTF8.GetBytes(command);
                await client.SendAsync(new ArraySegment<byte>(data), 
                    System.Net.WebSockets.WebSocketMessageType.Text, 
                    true, System.Threading.CancellationToken.None);
                Debug.WriteLine($"WebSocket command sent to {trigger.Name}: {command}");
            }
        }

        private async Task SendHttpCommand(DeviceTrigger trigger, string command)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                
                // Parse command as JSON for POST or as query string for GET
                if (command.StartsWith("{"))
                {
                    // POST with JSON body
                    var content = new System.Net.Http.StringContent(command, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(trigger.ConnectionString, content);
                    response.EnsureSuccessStatusCode();
                }
                else
                {
                    // GET with command as parameter
                    var url = $"{trigger.ConnectionString}?cmd={Uri.EscapeDataString(command)}";
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                }
                
                Debug.WriteLine($"HTTP command sent to {trigger.Name}: {command}");
            }
        }

        private async Task SendTcpCommand(DeviceTrigger trigger, string command)
        {
            var parts = trigger.ConnectionString.Split(':');
            var host = parts[0];
            var port = int.Parse(parts[1]);
            
            using (var client = new System.Net.Sockets.TcpClient())
            {
                await client.ConnectAsync(host, port);
                var stream = client.GetStream();
                var data = Encoding.UTF8.GetBytes(command + "\n");
                await stream.WriteAsync(data, 0, data.Length);
                Debug.WriteLine($"TCP command sent to {trigger.Name}: {command}");
            }
        }

        #endregion

        #region Device Discovery

        public List<string> GetAvailableSerialPorts()
        {
            return SerialPort.GetPortNames().ToList();
        }

        public async Task<List<BluetoothDeviceInfo>> DiscoverBluetoothDevices()
        {
            var devices = new List<BluetoothDeviceInfo>();
            
            try
            {
                var client = new BluetoothClient();
                var bluetoothDevices = await Task.Run(() => client.DiscoverDevices());
                
                foreach (var device in bluetoothDevices)
                {
                    devices.Add(new BluetoothDeviceInfo
                    {
                        Name = string.IsNullOrEmpty(device.DeviceName) ? "Unknown Device" : device.DeviceName,
                        Address = device.DeviceAddress.ToString("C"),  // Format as AA:BB:CC:DD:EE:FF
                        IsConnected = device.Connected,
                        IsAuthenticated = device.Authenticated
                    });
                }
                
                Debug.WriteLine($"Discovered {devices.Count} Bluetooth devices");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bluetooth discovery error: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex);
            }
            
            return devices;
        }

        #endregion

        #region Persistence

        private void LoadTriggers()
        {
            try
            {
                var triggersJson = Properties.Settings.Default.DeviceTriggers;
                if (!string.IsNullOrEmpty(triggersJson))
                {
                    _triggers = Newtonsoft.Json.JsonConvert.DeserializeObject<List<DeviceTrigger>>(triggersJson);
                    
                    // Auto-connect enabled devices
                    foreach (var trigger in _triggers.Where(t => t.IsEnabled))
                    {
                        _ = ConnectDevice(trigger);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading triggers: {ex.Message}");
                _triggers = new List<DeviceTrigger>();
            }
        }

        private void SaveTriggers()
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_triggers);
                Properties.Settings.Default.DeviceTriggers = json;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving triggers: {ex.Message}");
            }
        }

        #endregion

        #region Preset Configurations

        public static class ArduinoPresets
        {
            public static DeviceTrigger CreateLedStripTrigger(string comPort)
            {
                return new DeviceTrigger
                {
                    Name = "LED Strip Controller",
                    Type = TriggerType.Serial,
                    ConnectionString = $"{comPort}:9600,8,N,1",
                    Commands = new Dictionary<TriggerEvent, string>
                    {
                        { TriggerEvent.SessionStart, "LED:RAINBOW" },
                        { TriggerEvent.CountdownStart, "LED:PULSE:BLUE" },
                        { TriggerEvent.CountdownTick, "LED:FLASH:WHITE" },
                        { TriggerEvent.PhotoCapture, "LED:FLASH:BRIGHT" },
                        { TriggerEvent.PhotoCaptured, "LED:SOLID:GREEN" },
                        { TriggerEvent.SessionEnd, "LED:OFF" }
                    }
                };
            }

            public static DeviceTrigger CreateRelayTrigger(string comPort)
            {
                return new DeviceTrigger
                {
                    Name = "Relay Controller",
                    Type = TriggerType.Serial,
                    ConnectionString = $"{comPort}:9600,8,N,1",
                    Commands = new Dictionary<TriggerEvent, string>
                    {
                        { TriggerEvent.PhotoCapture, "RELAY:1:ON" },
                        { TriggerEvent.PhotoCaptured, "RELAY:1:OFF" },
                        { TriggerEvent.PrintStart, "RELAY:2:ON" },
                        { TriggerEvent.PrintComplete, "RELAY:2:OFF" }
                    }
                };
            }

            public static DeviceTrigger CreateDmxLightingTrigger(string comPort)
            {
                return new DeviceTrigger
                {
                    Name = "DMX Lighting Controller",
                    Type = TriggerType.Serial,
                    ConnectionString = $"{comPort}:115200,8,N,1",
                    Commands = new Dictionary<TriggerEvent, string>
                    {
                        { TriggerEvent.SessionStart, "DMX:SCENE:1" },
                        { TriggerEvent.CountdownStart, "DMX:SCENE:2" },
                        { TriggerEvent.PhotoCapture, "DMX:SCENE:3" },
                        { TriggerEvent.SessionEnd, "DMX:SCENE:0" }
                    }
                };
            }
        }

        public static class BluetoothPresets
        {
            public static DeviceTrigger CreateESP32BluetoothTrigger(string macAddress, string deviceName = "ESP32 Controller")
            {
                return new DeviceTrigger
                {
                    Name = deviceName,
                    Type = TriggerType.Bluetooth,
                    ConnectionString = macAddress,
                    Commands = new Dictionary<TriggerEvent, string>
                    {
                        { TriggerEvent.SessionStart, "LED:RAINBOW" },
                        { TriggerEvent.CountdownStart, "LED:PULSE:BLUE" },
                        { TriggerEvent.CountdownTick, "LED:FLASH:WHITE" },
                        { TriggerEvent.PhotoCapture, "LED:FLASH:BRIGHT" },
                        { TriggerEvent.PhotoCaptured, "LED:SOLID:GREEN" },
                        { TriggerEvent.SessionEnd, "LED:OFF" }
                    }
                };
            }

            public static DeviceTrigger CreateHC05ArduinoTrigger(string macAddress)
            {
                return new DeviceTrigger
                {
                    Name = "HC-05 Arduino Controller",
                    Type = TriggerType.Bluetooth,
                    ConnectionString = macAddress,
                    Commands = new Dictionary<TriggerEvent, string>
                    {
                        { TriggerEvent.SessionStart, "START" },
                        { TriggerEvent.CountdownStart, "COUNTDOWN" },
                        { TriggerEvent.PhotoCapture, "CAPTURE" },
                        { TriggerEvent.PhotoCaptured, "DONE" },
                        { TriggerEvent.SessionEnd, "END" }
                    }
                };
            }
        }

        public static class SmartLightPresets
        {
            public static DeviceTrigger CreatePhilipsHueTrigger(string bridgeIp, string apiKey)
            {
                return new DeviceTrigger
                {
                    Name = "Philips Hue Lights",
                    Type = TriggerType.HTTP,
                    ConnectionString = $"http://{bridgeIp}/api/{apiKey}/lights/1/state",
                    Commands = new Dictionary<TriggerEvent, string>
                    {
                        { TriggerEvent.SessionStart, "{\"on\":true,\"bri\":254,\"hue\":25500}" },
                        { TriggerEvent.CountdownStart, "{\"on\":true,\"bri\":200,\"hue\":46920,\"alert\":\"select\"}" },
                        { TriggerEvent.PhotoCapture, "{\"on\":true,\"bri\":254,\"hue\":0,\"sat\":0}" },
                        { TriggerEvent.SessionEnd, "{\"on\":false}" }
                    }
                };
            }
            
            public static DeviceTrigger CreateWLEDTrigger(string deviceIp)
            {
                return new DeviceTrigger
                {
                    Name = "WLED Controller",
                    Type = TriggerType.HTTP,
                    ConnectionString = $"http://{deviceIp}/json/state",
                    Commands = new Dictionary<TriggerEvent, string>
                    {
                        { TriggerEvent.SessionStart, "{\"on\":true,\"ps\":1}" }, // Preset 1
                        { TriggerEvent.CountdownStart, "{\"on\":true,\"ps\":2}" }, // Preset 2
                        { TriggerEvent.PhotoCapture, "{\"on\":true,\"ps\":3}" }, // Preset 3
                        { TriggerEvent.SessionEnd, "{\"on\":false}" }
                    }
                };
            }
        }

        #endregion

        public void Dispose()
        {
            _reconnectTimer?.Stop();
            
            foreach (var port in _serialPorts.Values)
            {
                port?.Close();
            }
            
            foreach (var client in _bluetoothClients.Values)
            {
                client?.Close();
            }
        }
    }

    public class BluetoothDeviceInfo
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public bool IsConnected { get; set; }
        public bool IsAuthenticated { get; set; }
    }

}