using System.Windows;
using System.Windows.Input;

namespace Photobooth.MVVM.Utils
{
	public static class EventToCommandBehavior
	{
		public static readonly DependencyProperty CommandProperty =
			DependencyProperty.RegisterAttached("Command", typeof(ICommand), typeof(EventToCommandBehavior),
				new PropertyMetadata(null, OnCommandChanged));

		public static ICommand GetCommand(DependencyObject obj)
		{
			return (ICommand)obj.GetValue(CommandProperty);
		}

		public static void SetCommand(DependencyObject obj, ICommand value)
		{
			obj.SetValue(CommandProperty, value);
		}

		private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is UIElement element)
			{
				if (e.OldValue == null && e.NewValue != null)
				{
					element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
				}
				else if (e.OldValue != null && e.NewValue == null)
				{
					element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
				}
			}
		}

		private static void OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			if (sender is UIElement element)
			{
				var command = GetCommand(element);
				if (command?.CanExecute(null) == true)
				{
					command.Execute(null);
				}
			}
		}
	}
}
