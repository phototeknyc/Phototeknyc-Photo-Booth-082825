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
using System.Windows.Navigation;
using System.Windows.Shapes;
using PhotoboothWPF;

namespace PhotoboothWPF.Pages
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
            PhotoboothWPF.Properties.AppSetting.Default.BackgroundColor = themeColor.Text;
            PhotoboothWPF.Properties.AppSetting.Default.Save();
            
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
                PhotoboothWPF.Properties.AppSetting.Default.BackgroundImage = filename;
                PhotoboothWPF.Properties.AppSetting.Default.Save();
            }

        }
    }
}
