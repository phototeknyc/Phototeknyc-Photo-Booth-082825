using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhotoboothWPF.Pages
{
    /// <summary>
    /// Interaction logic for Setting.xaml
    /// </summary>
    public partial class Setting : Page
    {
        public Setting()
        {
            InitializeComponent();
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedButton = styleMenu.SelectedItem as NavButton;
            PhotoBoothWindow.Navigate(frame, selectedButton);
        }

    
    }
}
