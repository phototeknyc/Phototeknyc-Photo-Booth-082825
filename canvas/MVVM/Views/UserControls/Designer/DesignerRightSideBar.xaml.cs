using Photobooth.MVVM.ViewModels.Designer;
using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Photobooth.MVVM.Views.UserControls.Designer
{
	/// <summary>
	/// Interaction logic for DesignerRightSideBar.xaml
	/// </summary>
	public partial class DesignerRightSideBar : UserControl
	{
		public DesignerRightSideBar(DesignerVM designerVM)
		{
			this.DataContext = designerVM;
			InitializeComponent();
		}

		private void tbEnter_KeyDown(object sender, KeyEventArgs e)
		{
			// if the pressed key is Enter then commit the text to call the binding property.
			if (e.Key == Key.Enter)
			{
				TextBox tb = sender as TextBox;
				tb.GetBindingExpression(TextBox.TextProperty).UpdateSource();
			}
		}
	}
}
