using Microsoft.Windows.Themes;
using PhotoboothWPF.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PhotoboothWPF
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
            this.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(Properties.AppSetting.Default.BackgroundColor);
            if(Properties.AppSetting.Default.BackgroundImage != "")
            {
                // Set the background image from AppSetting.settings
                this.Background = new ImageBrush(new BitmapImage(new Uri(Properties.AppSetting.Default.BackgroundImage)));
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
            this.Hide();
            if((bool)photoBoothWindow.ShowDialog())
            {
				this.Show();
			}
            else
            {
                this.Close();
            }
        }

      

       
    }
}
