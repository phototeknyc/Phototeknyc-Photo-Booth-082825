using Photobooth.MVVM.ViewModels.Camera;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Photobooth.MVVM.Views.UserControls.Camera
{
	/// <summary>
	/// Interaction logic for CameraMain.xaml
	/// </summary>
	public partial class CameraMain : UserControl, IDisposable
	{
		private bool _disposed;

		public CameraMain()
		{
			this.DataContext = new CameraMainVM();
			InitializeComponent();
			DataContextChanged += OnDataContextChanged;
			Unloaded += OnUnloaded;
		}

		public CameraMain Refresh()
		{
			if (DataContext is CameraMainVM viewModel)
			{
				viewModel.Refresh();
			}
			return this;
		}

		private void OnUnloaded(object sender, RoutedEventArgs e)
		{
			Dispose();
		}

		public void Dispose()
		{
			if (DataContext is CameraMainVM viewModel)
			{
				viewModel.Dispose();
			}
		}

		private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (e.OldValue is CameraMainVM oldViewModel)
			{
				oldViewModel.Dispose();
			}
		}

		// Protected virtual method to dispose of resources
		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			// Datacontext is disposed in the Dispose method

		}

		void IDisposable.Dispose()
		{
			Dispose();
		}
	}
}
