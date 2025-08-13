using System.Windows;
using System.Windows.Media;

namespace Photobooth.Controls
{
    public partial class AdvancedColorPickerDialog : Window
    {
        public Color SelectedColor
        {
            get { return ColorPicker.SelectedColor; }
            set { ColorPicker.SelectedColor = value; }
        }

        public AdvancedColorPickerDialog()
        {
            InitializeComponent();
        }

        public AdvancedColorPickerDialog(string title) : this()
        {
            Title = title;
        }

        public AdvancedColorPickerDialog(string title, Color initialColor) : this(title)
        {
            SelectedColor = initialColor;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}