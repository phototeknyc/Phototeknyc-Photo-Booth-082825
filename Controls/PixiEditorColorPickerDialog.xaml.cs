using System.Windows;
using System.Windows.Media;

namespace Photobooth.Controls
{
    /// <summary>
    /// Color picker dialog using PixiEditor ColorPicker
    /// </summary>
    public partial class PixiEditorColorPickerDialog : Window
    {
        public Color SelectedColor
        {
            get { return ColorPicker.SelectedColor; }
            set { ColorPicker.SelectedColor = value; }
        }

        public PixiEditorColorPickerDialog()
        {
            InitializeComponent();
        }

        public PixiEditorColorPickerDialog(string title, Color initialColor) : this()
        {
            Title = title;
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

        /// <summary>
        /// Show the color picker dialog and return the selected color
        /// </summary>
        /// <param name="owner">Parent window</param>
        /// <param name="title">Dialog title</param>
        /// <param name="initialColor">Initial color to display</param>
        /// <returns>Selected color, or null if cancelled</returns>
        public static Color? ShowDialog(Window owner, string title, Color initialColor)
        {
            var dialog = new PixiEditorColorPickerDialog(title, initialColor)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.SelectedColor;
            }

            return null;
        }
    }
}