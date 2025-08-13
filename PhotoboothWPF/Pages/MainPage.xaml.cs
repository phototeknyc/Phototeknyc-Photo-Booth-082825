using System.Windows.Controls;

namespace PhotoboothWPF.Pages
{
    /// <summary>
    /// Interaction logic for MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {

        private static MainPage _instance;
        public static MainPage Instance
        {
			get
            {
				if (_instance == null)
                {
					_instance = new MainPage();
				}
				return _instance;
			}
		}

        public MainPage()
        {
            InitializeComponent();
            dcvs.SetRatio(2, 3);
            dcvs.SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;
            _instance = this;
        }

        private void SelectedItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // todo it will be done in parent

            //// clear selection of orientation checkboxes, and properties
            //comboBox.SelectedIndex = -1;
            //comboBox1.SelectedIndex = -1;
        }

        private void Image_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MainPage.Instance.dcvs.ClearCanvas();
        }

        private void Image_MouseLeftButtonDown_1(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MainPage.Instance.dcvs.ChangeCanvasOrientation("Portrait");
        }
    }
}
