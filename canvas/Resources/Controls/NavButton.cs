using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Photobooth.Resources.Controls
{
	/// <summary>
	/// Follow steps 1a or 1b and then 2 to use this custom control in a XAML file.
	///
	/// Step 1a) Using this custom control in a XAML file that exists in the current project.
	/// Add this XmlNamespace attribute to the root element of the markup file where it is 
	/// to be used:
	///
	///     xmlns:MyNamespace="clr-namespace:Photobooth"
	///
	///
	/// Step 1b) Using this custom control in a XAML file that exists in a different project.
	/// Add this XmlNamespace attribute to the root element of the markup file where it is 
	/// to be used:
	///
	///     xmlns:MyNamespace="clr-namespace:Photobooth;assembly=Photobooth"
	///
	/// You will also need to add a project reference from the project where the XAML file lives
	/// to this project and Rebuild to avoid compilation errors:
	///
	///     Right click on the target project in the Solution Explorer and
	///     "Add Reference"->"Projects"->[Browse to and select this project]
	///
	///
	/// Step 2)
	/// Go ahead and use your control in the XAML file.
	///
	///     <MyNamespace:NavButton/>
	///
	/// </summary>
	public class NavButton : Button
	{
		// Using a DependencyProperty as the backing store for Label.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty LabelProperty =
			DependencyProperty.Register("Label", typeof(string), typeof(NavButton), new PropertyMetadata(default(NavButton)));

		// Using a DependencyProperty as the backing store for NavLink.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty NavLinkProperty = DependencyProperty.Register("NavLink", typeof(Uri), typeof(NavButton), new PropertyMetadata(default(NavButton)));

		// Using a DependencyProperty as the backing store for Icon.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty IconProperty = DependencyProperty.Register("Icon", typeof(ImageSource), typeof(NavButton), new PropertyMetadata(default(NavButton)));

		static NavButton()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(NavButton), new FrameworkPropertyMetadata(typeof(NavButton)));
		}

		public Uri NavLink
		{
			get { return (Uri)GetValue(NavLinkProperty); }
			set { SetValue(NavLinkProperty, value); }
		}

		public ImageSource Icon
		{
			get { return (ImageSource)GetValue(IconProperty); }
			set { SetValue(IconProperty, value); }
		}

		public string Label
		{
			get { return (string)GetValue(LabelProperty); }
			set { SetValue(LabelProperty, value); }
		}

		protected virtual void OnClick()
		{
			RoutedEventArgs args = new RoutedEventArgs(ClickEvent);
			RaiseEvent(args);
		}
	}
}
