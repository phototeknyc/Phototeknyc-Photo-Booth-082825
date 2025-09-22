using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Photobooth.Services;

namespace Photobooth
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		[DllImport("user32.dll")]
		private static extern bool SetProcessDPIAware();
		
		private static CameraControl.Devices.CameraDeviceManager _deviceManager;
		
		public static CameraControl.Devices.CameraDeviceManager DeviceManager
		{
			get { return _deviceManager; }
			set { _deviceManager = value; }
		}
		
		protected override void OnStartup(StartupEventArgs e)
		{
			// Set shutdown mode to close when main window closes
			this.ShutdownMode = ShutdownMode.OnMainWindowClose;

			// Upgrade user settings from previous version if needed
			// This preserves user settings across application updates
			try
			{
				// First, try to load settings normally to check their current state
				var selectedEventId = Photobooth.Properties.Settings.Default.SelectedEventId;
				var eventSelectionTime = Photobooth.Properties.Settings.Default.EventSelectionTime;
				var enableBackgroundRemoval = Photobooth.Properties.Settings.Default.EnableBackgroundRemoval;

				System.Diagnostics.Debug.WriteLine($"Current settings before upgrade check:");
				System.Diagnostics.Debug.WriteLine($"  - SelectedEventId: {selectedEventId}");
				System.Diagnostics.Debug.WriteLine($"  - EventSelectionTime: {eventSelectionTime}");
				System.Diagnostics.Debug.WriteLine($"  - EnableBackgroundRemoval: {enableBackgroundRemoval}");

				if (Photobooth.Properties.Settings.Default.UpgradeRequired)
				{
					Photobooth.Properties.Settings.Default.Upgrade();
					Photobooth.Properties.Settings.Default.UpgradeRequired = false;
					Photobooth.Properties.Settings.Default.Save();
					System.Diagnostics.Debug.WriteLine("User settings upgraded from previous version");
				}
			}
			catch (Exception upgradeEx)
			{
				System.Diagnostics.Debug.WriteLine($"Settings upgrade failed: {upgradeEx.Message}");
				// Continue with default settings if upgrade fails
			}

			// Enable DPI awareness for crisp rendering on high-DPI displays
			if (Environment.OSVersion.Version.Major >= 6)
			{
				SetProcessDPIAware();
			}

			// Initialize sync notification service
			SyncNotificationService.Instance.Initialize();
			
			// Always use software rendering by default for better compatibility
			// Can be overridden with /hardware command line argument
			bool forceSoftwareRendering = !(e.Args.Length > 0 && e.Args.Contains("/hardware"));
			
			if (forceSoftwareRendering)
			{
				// Disable hardware acceleration for Surface devices
				RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
				System.Diagnostics.Debug.WriteLine("Hardware acceleration disabled - using software rendering");
			}
			else
			{
				// Use default hardware acceleration
				RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
			}
			
			// Force ClearType rendering for text
			TextOptions.TextFormattingModeProperty.OverrideMetadata(
				typeof(Window),
				new FrameworkPropertyMetadata(TextFormattingMode.Display));
			
			// Initialize queue services early to ensure they start processing
			try
			{
				System.Diagnostics.Debug.WriteLine("Initializing queue services...");
				
				// Initialize PhotoboothQueueService - this will start processing pending items
				var photoboothQueue = Photobooth.Services.PhotoboothQueueService.Instance;
				System.Diagnostics.Debug.WriteLine("PhotoboothQueueService initialized");
				
				// Initialize OfflineQueueService - this will start processing pending uploads
				var offlineQueue = Photobooth.Services.OfflineQueueService.Instance;
				System.Diagnostics.Debug.WriteLine("OfflineQueueService initialized");
			}
			catch (Exception queueEx)
			{
				System.Diagnostics.Debug.WriteLine($"Error initializing queue services: {queueEx.Message}");
			}
			
			// Initialize Web API if enabled
			// For now, always enable Web API on default port
			// You can add settings later if needed
			bool enableWebApi = true;
			// Port 8080 - requires running SetupWebApi.bat as admin first
			int webApiPort = 8080; // Standard port (setup completed)
			
			try
			{
				if (enableWebApi)
				{
					System.Diagnostics.Debug.WriteLine($"Starting Web API on port {webApiPort}...");
					Services.WebApiStartup.Initialize(webApiPort);
					System.Diagnostics.Debug.WriteLine($"Web API started successfully!");
					System.Diagnostics.Debug.WriteLine($"");
					System.Diagnostics.Debug.WriteLine($"==========================================");
					System.Diagnostics.Debug.WriteLine($"  Web Control Panel Available!");
					System.Diagnostics.Debug.WriteLine($"  Open your browser and go to:");
					System.Diagnostics.Debug.WriteLine($"  http://localhost:{webApiPort}/");
					System.Diagnostics.Debug.WriteLine($"==========================================");
					System.Diagnostics.Debug.WriteLine($"");
					System.Diagnostics.Debug.WriteLine($"API endpoints available at http://localhost:{webApiPort}/api/");
				}
				else
				{
					System.Diagnostics.Debug.WriteLine("Web API is disabled");
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to start Web API: {ex.Message}");
				System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
				
				// Show a message box for debugging
				System.Windows.MessageBox.Show(
					$"Web API failed to start on port {webApiPort}:\n\n{ex.Message}\n\n" +
					"This is usually due to:\n" +
					"• Port already in use\n" +
					"• Missing URL reservation (for ports below 49152)\n\n" +
					"The app is using port {webApiPort} which should work without admin.\n\n" +
					"To use port 8080 instead:\n" +
					"1. Run SetupWebApi.bat as administrator\n" +
					"2. Change webApiPort to 8080 in App.xaml.cs",
					"Web API Warning",
					System.Windows.MessageBoxButton.OK,
					System.Windows.MessageBoxImage.Warning);
				
				// Don't fail the entire application if Web API fails to start
				// It might be a port conflict or permission issue
			}
			
			base.OnStartup(e);
		}
		
		protected override void OnExit(ExitEventArgs e)
		{
			try
			{
				System.Diagnostics.Debug.WriteLine("Application shutting down - cleaning up resources...");
				
				// Stop queue services first - these have background timers
				try
				{
					System.Diagnostics.Debug.WriteLine("Stopping queue services...");
					
					// Dispose PhotoboothQueueService
					var photoboothQueue = Photobooth.Services.PhotoboothQueueService.Instance;
					if (photoboothQueue != null)
					{
						System.Diagnostics.Debug.WriteLine("Disposing PhotoboothQueueService...");
						photoboothQueue.Dispose();
					}
					
					// Dispose OfflineQueueService
					var offlineQueue = Photobooth.Services.OfflineQueueService.Instance;
					if (offlineQueue != null)
					{
						System.Diagnostics.Debug.WriteLine("Disposing OfflineQueueService...");
						offlineQueue.Dispose();
					}
				}
				catch (Exception queueEx)
				{
					System.Diagnostics.Debug.WriteLine($"Error stopping queue services: {queueEx.Message}");
				}
				
				// Stop Web API if running
				if (Services.WebApiStartup.IsRunning)
				{
					System.Diagnostics.Debug.WriteLine("Stopping Web API...");
					Services.WebApiStartup.Stop();
				}
				
				// Stop all live view operations
				if (_deviceManager != null && _deviceManager.ConnectedDevices != null)
				{
					foreach (var device in _deviceManager.ConnectedDevices)
					{
						try
						{
							if (device != null && device.IsConnected)
							{
								System.Diagnostics.Debug.WriteLine($"Stopping live view for device: {device.DeviceName}");
								device.StopLiveView();
							}
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"Error stopping live view: {ex.Message}");
						}
					}
				}
				
				// Disconnect all cameras
				if (_deviceManager != null)
				{
					System.Diagnostics.Debug.WriteLine("Closing all camera connections...");
					try
					{
						_deviceManager.CloseAll();
						_deviceManager = null;
					}
					catch (Exception camEx)
					{
						System.Diagnostics.Debug.WriteLine($"Error closing cameras: {camEx.Message}");
					}
				}
				
				// Force garbage collection to release resources
				System.Diagnostics.Debug.WriteLine("Forcing garbage collection...");
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
				
				System.Diagnostics.Debug.WriteLine("Application cleanup completed");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error during application shutdown: {ex.Message}");
			}
			
			// Don't call base.OnExit - it might be blocking
			// base.OnExit(e);
			
			// Force immediate termination
			System.Diagnostics.Debug.WriteLine("Forcing immediate process termination...");
			System.Diagnostics.Process.GetCurrentProcess().Kill();
		}
		
		private bool IsSurfaceDevice()
		{
			try
			{
				// Check for Surface-specific identifiers
				var manufacturer = GetWMIProperty("Win32_ComputerSystem", "Manufacturer");
				var model = GetWMIProperty("Win32_ComputerSystem", "Model");
				
				return (manufacturer != null && manufacturer.Contains("Microsoft")) &&
				       (model != null && model.Contains("Surface"));
			}
			catch
			{
				return false;
			}
		}
		
		private string GetWMIProperty(string wmiClass, string propertyName)
		{
			try
			{
				using (var searcher = new System.Management.ManagementObjectSearcher($"SELECT {propertyName} FROM {wmiClass}"))
				{
					foreach (var obj in searcher.Get())
					{
						return obj[propertyName]?.ToString();
					}
				}
			}
			catch { }
			return null;
		}
	}
}
