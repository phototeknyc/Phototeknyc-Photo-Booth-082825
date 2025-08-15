using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace Photobooth
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		[DllImport("user32.dll")]
		private static extern bool SetProcessDPIAware();
		
		protected override void OnStartup(StartupEventArgs e)
		{
			// Enable DPI awareness for crisp rendering on high-DPI displays
			if (Environment.OSVersion.Version.Major >= 6)
			{
				SetProcessDPIAware();
			}
			
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
			
			base.OnStartup(e);
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
