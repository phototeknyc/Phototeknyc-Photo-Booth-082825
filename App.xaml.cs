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
			
			// Set default rendering options for entire application
			RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
			
			// Force ClearType rendering for text
			TextOptions.TextFormattingModeProperty.OverrideMetadata(
				typeof(Window),
				new FrameworkPropertyMetadata(TextFormattingMode.Display));
			
			base.OnStartup(e);
		}
	}
}
