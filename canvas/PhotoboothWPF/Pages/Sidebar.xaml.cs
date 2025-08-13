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

namespace PhotoboothWPF.Pages
{
    /// <summary>
    /// Interaction logic for SideNavbar.xaml
    /// </summary>
    public partial class SideNavbar : Page
    {
        public SideNavbar()
        {
            InitializeComponent();
        }

        private void ActionAddPlaceholder(object sender, MouseButtonEventArgs e)
        {

            ToolBoxList.SelectedIndex = -1;
            // unselect this button
            //ToolBoxList.SelectedItems.Clear();
            //(sender as ListViewItem).IsSelected = false;
        }

        private void ActionAddText(object sender, MouseButtonEventArgs e)
        {

            ToolBoxList.SelectedIndex = -1;
            // unselect this button
            //ToolBoxList.SelectedItems.Clear();
            (sender as ListViewItem).IsSelected = false;
        }

        private void ActionImportImage(object sender, MouseButtonEventArgs e)
        {

            ToolBoxList.SelectedIndex = -1;
            // unselect this button
            //ToolBoxList.SelectedItems.Clear();
            (sender as ListViewItem).IsSelected = false;
            MainPage.Instance.dcvs.ImportImage();
        }


        private void ActionCaptureImage(object sender, MouseButtonEventArgs e)
        {
            ToolBoxList.SelectedIndex = -1;
			MainPage.Instance.dcvs.AddPlaceholder();
            // unselect this button
            //ToolBoxList.SelectedItems.Clear();
            (sender as ListViewItem).IsSelected = false;

		}

        private void comboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //get the selected value
            string selectedValue = (sender as ComboBox).SelectedValue.ToString();
            //split the string to get the ratio
            string[] ratio = selectedValue.Split(':')[1].Split('x');
            //set the ratio

            MainPage.Instance.dcvs.SetRatio(int.Parse(ratio[0]), int.Parse(ratio[1]));

        }

        private void comboBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string selectedValue = (sender as ComboBox).SelectedValue.ToString();
            //split the string to get the ratio
            string ratio = selectedValue.Split(':')[1];
            //set the orientation
            

        }

        private void ActionClearCanvas(object sender, MouseButtonEventArgs e)
        {
            MainPage.Instance.dcvs.ClearCanvas();
        }

        private void ActionChangeOrientation(object sender, MouseButtonEventArgs e)
        {
            MainPage.Instance.dcvs.ChangeCanvasOrientation("Portrait");
        }
    }
}
