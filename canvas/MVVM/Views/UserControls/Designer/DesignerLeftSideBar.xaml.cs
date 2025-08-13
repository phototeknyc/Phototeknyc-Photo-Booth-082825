using Photobooth.MVVM.ViewModels.Designer;
using System.Windows.Controls;
using System.Windows.Input;

namespace Photobooth.MVVM.Views.UserControls.Designer
{
	/// <summary>
	/// Interaction logic for DesignerLeftSideBar.xaml
	/// </summary>
	public partial class DesignerLeftSideBar : UserControl
	{
		public DesignerLeftSideBar(DesignerVM designerVM)
		{
			this.DataContext = designerVM;
			InitializeComponent();

			comboBox.SelectedIndex = comboBox.Items.Count > 0 ?  0 : -1;
		}
	}
}
