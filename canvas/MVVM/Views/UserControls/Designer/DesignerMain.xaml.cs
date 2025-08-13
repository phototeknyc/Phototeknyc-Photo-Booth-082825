using Photobooth.MVVM.ViewModels.Designer;
using System.Windows.Controls;

namespace Photobooth.MVVM.Views.UserControls.Designer
{
	public partial class DesignerMain : UserControl
	{
		private DesignerLeftSideBar _designerLeftSideBar;
		private DesignerRightSideBar _designerRightSideBar;

		public DesignerMain()
		{
			InitializeComponent();
			DesignerVM vm = new DesignerVM();
			DataContext = vm;
			_designerLeftSideBar = new DesignerLeftSideBar(vm);
			_designerRightSideBar = new DesignerRightSideBar(vm);
			LeftSideBarControl.Content = _designerLeftSideBar;
			RightSideBarControl.Content = _designerRightSideBar;
		}
	}
}
