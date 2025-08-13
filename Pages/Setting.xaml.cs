using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Photobooth.Pages
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
            // Check which ListBox triggered the event
            var listBox = sender as ListBox;
            if (listBox == null) return;
            
            var selectedButton = listBox.SelectedItem as NavButton;
            if (selectedButton != null)
            {
                PhotoBoothWindow.Navigate(frame, selectedButton);
            }
        }

    
    }
}
