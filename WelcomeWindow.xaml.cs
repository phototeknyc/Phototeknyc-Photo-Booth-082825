using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Photobooth
{
    /// <summary>
    /// Interaction logic for WelcomeWindow.xaml
    /// </summary>
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();
            // Attach the Loaded event handler
            Loaded += WelcomeWindow_Loaded;
        }

        private void WelcomeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Set the background color from AppSetting.settings
            this.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(Properties.Settings.Default.BackgroundColor);
            if(Properties.Settings.Default.BackgroundImage != "")
            {
                // Set the background image from AppSetting.settings
                this.Background = new ImageBrush(new BitmapImage(new Uri(Properties.Settings.Default.BackgroundImage)));
            }
        }

        private static WelcomeWindow instance = null;
        public static WelcomeWindow Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new WelcomeWindow();
                }
                return instance;
            }
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            // Go to PhotoBoothWindow
            PhotoBoothWindow photoBoothWindow = new PhotoBoothWindow();
            
            // Handle window closing to show welcome window again
            photoBoothWindow.Closed += (s, args) =>
            {
                this.Show();
            };
            
            // Show as non-modal window for true fullscreen
            photoBoothWindow.Show();
            this.Hide();
        }
    }
}
