using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Photobooth.Pages
{
    /// <summary>
    /// Interaction logic for BackgroundSettingControl.xaml
    /// </summary>
    public partial class BackgroundSettingControl : UserControl
    {
        public BackgroundSettingControl()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {            
            Photobooth.Properties.Settings.Default.BackgroundColor = themeColor.Text;
            Photobooth.Properties.Settings.Default.Save();
		}

        private void btnImage_Click(object sender, RoutedEventArgs e)

        { 
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".jpg";
            dlg.Filter = "Image Files (*.jpg, *.png)|*.jpg;*.png";
            Nullable<bool> result = dlg.ShowDialog();
            if (result == true)
            {
                string filename = dlg.FileName;
                Photobooth.Properties.Settings.Default.BackgroundImage = filename;
                Photobooth.Properties.Settings.Default.Save();
            }

        }
    }
}
