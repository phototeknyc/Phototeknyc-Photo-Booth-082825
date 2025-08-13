using CameraControl.Core;
using CameraControl.Core.Classes;
using CameraControl.Core.Database;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using CameraControl.Devices.Others;
using CameraControl.Devices.Wifi;
using Photobooth.MVVM.Models;
using Photobooth.MVVM.ViewModels.Designer;
using Photobooth.MVVM.ViewModels.Settings;
using Photobooth.MVVM.Views.Windows;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Photobooth.MVVM.ViewModels.Camera
{
	public class CameraMainVM : BaseViewModel, IDisposable
	{
		#region Observable Collections and Properties

		private bool _isLiveViewSupported;
		public bool IsLiveViewSupported
		{
			get => _isLiveViewSupported;
			set => SetProperty(ref _isLiveViewSupported, value);
		}

		private BitmapImage _liveImage;
		public BitmapImage LiveImage
		{
			get => _liveImage;
			set => SetProperty(ref _liveImage, value);
		}

		private bool _isLoadingDeviceManager;
		public bool IsLoadingDeviceManager
		{
			get => _isLoadingDeviceManager;
			set => SetProperty(ref _isLoadingDeviceManager, value);
		}

		private bool _isRunning = false;
		private bool IsRunning
		{
			get => _isRunning;
			set => SetProperty(ref _isRunning, value);
		}

		private List<Template> _templates;
		public List<Template> Templates
		{
			get => _templates;
			set => SetProperty(ref _templates, value);
		}

		private Template _selectedTemplate;
		public Template SelectedTemplate
		{
			get => _selectedTemplate;
			set => SetProperty(ref _selectedTemplate, value);
		}

		public CameraDeviceManager DeviceManager { get; set; }
		public string FolderForPhotos { get; set; }

		private List<String> _templateImages = new List<string>();
		private DesignerVM designerVM;
		public DesignerVM CameraDesignerVM
		{
			get => designerVM;
			set => SetProperty(ref designerVM, value);
		}

		#endregion

		#region Commands
		public ICommand CaptureCommand { get; set; }
		public ICommand LiveViewCommand { get; set; }
		public ICommand StopCommand { get; set; }
		public ICommand StartCaptureWithTemplate { get; set; }
		#endregion

		#region Constructor

		public CameraMainVM()
		{
			IsLoadingDeviceManager = true;
			Task.Factory.StartNew(InitializeDeviceManagerAsync);
			Task.Factory.StartNew(LoadTemplates);

			CaptureCommand = new RelayCommand(async _ => await OnCaptureAsync());
			LiveViewCommand = new RelayCommand(OnLiveView);
			StopCommand = new RelayCommand(OnStop);
			StartCaptureWithTemplate = new RelayCommand(CaptureWithTemplate);

			designerVM = new DesignerVM();

			Application.Current.Exit += OnApplicationExit;
		}

		private Task OnCapture(object obj)
		{
			Thread thread = new Thread(new ThreadStart(CameraHelper.Capture));
			thread.Start();
			return Task.CompletedTask;
		}

		private void LoadTemplates()
		{
			Templates = SessionsVM.Instance.CurrentSession.GetTemplates();
		}

		//private Task OnCaptureWithTemplateAsync()
		//{
		//	return Task.Factory.StartNew(CaptureWithTemplate);
		//}

		private async void CaptureWithTemplate(Object o)
		{
			_templateImages.Clear();
			if (SelectedTemplate == null)
			{
				MessageBox.Show("Please select a template to capture with.");
				return;
			}
			int photosCount = 0;
			foreach (var element in SelectedTemplate.Elements)
			{
				if (element is PhotoElement)
				{
					photosCount++;
				}
			}

			if (photosCount <= 0)
			{
				MessageBox.Show("The selected template does not contain any photos.");
				return;
			}


			MessageBox.Show("Please pose for the camera. The camera will capture " + photosCount + " photos in 3 seconds intervals.");
			await TakePhotes(photosCount);

			if (_templateImages.Count > 0)
			{
				if (_templateImages.Count < photosCount)
				{
					MessageBoxResult result = MessageBox.Show("Failed to capture the required amount of photos, total captured: " + _templateImages.Count + "\n" +
						"\n" +
						"\n" +
						"Do you wish to print the template with the given number of phots?", "Photos not captured", MessageBoxButton.YesNo, MessageBoxImage.Warning);

					if (result != MessageBoxResult.Yes)
					{
						return;
					}
				}
				else
				{
					MessageBox.Show("Photos captured successfully.");
				}

				Application.Current.Dispatcher.Invoke(() =>
				{
					CameraDesignerVM.printTemplateWithImages(SelectedTemplate, _templateImages);
				});
				MessageBox.Show("Template printed successfully.");
			}
			else
			{
				MessageBox.Show("Not able to capture a single photo, kindly check the camera.");
			}
		}

		private async Task TakePhotes(int photosCount)
		{
			Thread.Sleep(3000);
			int tries = 0;
			while (_templateImages.Count < photosCount && tries - photosCount < 10)
			{
				int retryAttempts = 3;
				while (retryAttempts > 0)
				{
					try
					{
						CameraHelper.WaitForCamera(DeviceManager.SelectedCameraDevice, 5000);
						CameraHelper.Capture();
						await Task.Delay(3000);
						retryAttempts = 0;
					}
					catch (Exception ex)
					{
						retryAttempts--;
					}
				}
				tries++;
			}
			CameraHelper.WaitForCamera(DeviceManager.SelectedCameraDevice, 5000);
		}

		private void PhotoCaptured(object o)
		{
			PhotoCapturedEventArgs eventArgs = o as PhotoCapturedEventArgs;
			if (eventArgs == null)
				return;

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + eventArgs.FileName;
            string fileName = Path.Combine(FolderForPhotos, uniqueFileName);

            if (!Directory.Exists(Path.GetDirectoryName(fileName)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            }

            try
			{
				eventArgs.CameraDevice.TransferFile(eventArgs.Handle, fileName);
                eventArgs.CameraDevice.IsBusy = false;
            }
			catch (Exception exception)
			{
				eventArgs.CameraDevice.IsBusy = false;
				//Message.Show("Error download photo from camera :\n" + exception.Message);
			}
			finally
			{
				if (File.Exists(fileName))
                {
                    BitmapImage bmap = null;
                    bmap = new BitmapImage(new Uri(fileName));
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        LiveImage = bmap;
                        _templateImages.Add(fileName);
                    });
				}
			}
		}

		//Initialize the device manager asynchronously.
		private void InitializeDeviceManagerAsync()
		{
			InitializeDeviceManager();
			IsLoadingDeviceManager = false;
		}

		private void InitializeDeviceManager()
		{
			ServiceProvider.Configure();
			ServiceProvider.Settings = new CameraControl.Core.Classes.Settings();
			ServiceProvider.Branding = new CameraControl.Core.Classes.Branding();
			ServiceProvider.DeviceManager = ServiceProvider.DeviceManager ?? new CameraDeviceManager();
			DeviceManager = ServiceProvider.DeviceManager;
			DeviceManager.CameraSelected += DeviceManager_CameraSelected;
			DeviceManager.CameraConnected += DeviceManager_CameraConnected;
			DeviceManager.PhotoCaptured += DeviceManager_PhotoCaptured;
			DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;


			// For experimental Canon driver support- to use canon driver the canon sdk files should be copied in application folder
			DeviceManager.UseExperimentalDrivers = true;
			DeviceManager.DisableNativeDrivers = false;

			FolderForPhotos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Orignals", DateTime.Now.ToString("dd-MM-yyyy"));

			Application.Current.Dispatcher.Invoke(() => { DeviceManager.ConnectToCamera(); });
		}

		void DeviceManager_CameraDisconnected(ICameraDevice cameraDevice)
		{
		}

		void DeviceManager_PhotoCaptured(object sender, PhotoCapturedEventArgs eventArgs)
		{
            Thread thread = new Thread(new ThreadStart(() => PhotoCaptured(eventArgs)));
            thread.Start();
		}

		private void NotifyUser(string message)
		{
			// Implement View interaction through events or other UI-specific mechanisms.
			Message.Show(message);
		}

		private void HandleError(string message, Exception ex = null)
		{
			// Log error and notify user as needed.
			NotifyUser($"{message}\n{ex?.Message}");
		}

		void DeviceManager_CameraConnected(ICameraDevice cameraDevice)
		{
			if (cameraDevice != null && !IsRunning)
			{
				cameraDevice.StopLiveView();
			}
		}

		void DeviceManager_CameraSelected(ICameraDevice oldcameraDevice, ICameraDevice newcameraDevice)
		{
			IsLiveViewSupported = newcameraDevice.GetCapability(CapabilityEnum.LiveView);
		}
		#endregion

		#region Command Methods
		private async Task OnCaptureAsync()
		{
			//await Task.Run(() => Capture());

			Application.Current.Dispatcher.Invoke(() =>
			{
				Capture();
			});
		}

		private void OnLiveView(object obj)
		{
			if (!IsRunning)
			{
				DeviceManager.SelectedCameraDevice.StartLiveView();
				IsRunning = true;
				Task.Factory.StartNew(StartLiveView);

			}
		}

		private void OnStop(object obj)
		{
			Task.Factory.StartNew(StopLiveView);
		}
		#endregion

		#region Device Manager Methods

		private void Capture()
		{
			const int maxRetries = 10;
			int retryCount = 0;

			while (retryCount < maxRetries)
			{
				try
				{
					var cameraDevice = DeviceManager.SelectedCameraDevice as ICameraDevice;
					cameraDevice?.CapturePhoto();
					break;
				}
				catch (DeviceException exception) when (
					exception.ErrorCode == ErrorCodes.MTP_Device_Busy ||
					exception.ErrorCode == ErrorCodes.ERROR_BUSY)
				{
					retryCount++;
					Task.Delay(100).Wait(); // Avoid blocking thread; consider using async instead.
				}
				catch (Exception ex)
				{
					MessageBox.Show("Error occurred:");
					break;
				}
			}

			if (retryCount == maxRetries)
			{
				MessageBox.Show("Capture failed after maximum retries.");
			}
		}


		private void TemplateCapture()
		{
			const int maxRetries = 10;
			int retryCount = 0;

			while (retryCount < maxRetries)
			{
				try
				{
					var cameraDevice = DeviceManager.SelectedCameraDevice as ICameraDevice;
					cameraDevice?.CapturePhoto();
					break;
				}
				catch (DeviceException exception) when (
					exception.ErrorCode == ErrorCodes.MTP_Device_Busy ||
					exception.ErrorCode == ErrorCodes.ERROR_BUSY)
				{
					retryCount++;
					Task.Delay(100).Wait(); // Avoid blocking thread; consider using async instead.
				}
			}

			if (retryCount >= maxRetries)
			{
				throw new Exception("Capture failed after maximum retries.");
			}
		}

		public void StartLiveView()
		{
			do
			{
				try
				{
					LiveImage = createImage(DeviceManager.SelectedCameraDevice.GetLiveViewImage());
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"Error retrieving live view image: {ex.Message}");
				}
			} while (IsRunning);
		}

		private BitmapImage createImage(LiveViewData liveViewData)
		{
			if (liveViewData?.ImageData != null)
			{
				using (var stream = new MemoryStream(
					liveViewData.ImageData,
					liveViewData.ImageDataPosition,
					liveViewData.ImageData.Length - liveViewData.ImageDataPosition))
				{
					var bitmapImage = new BitmapImage();
					bitmapImage.BeginInit();
					bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage.StreamSource = stream;
					bitmapImage.EndInit();
					bitmapImage.Freeze();
					return bitmapImage;
				}
			}
			return null;
		}

		private void StopLiveView()
		{
			while (IsRunning)
			{
				try
				{
					DeviceManager.SelectedCameraDevice.StopLiveView();
					IsRunning = false;
				}
				catch (DeviceException exception)
				{
					if (exception.ErrorCode == ErrorCodes.MTP_Device_Busy || exception.ErrorCode == ErrorCodes.ERROR_BUSY)
					{
						Task.Delay(500);
						IsRunning = true;
					}
					else
					{
						MessageBox.Show("Error occurred :" + exception.Message);
					}
				}
			};
		}
		#endregion


		#region IDisposable Implementation
		private bool _disposed = false;

		// Protected virtual method to dispose of resources
		protected virtual void Dispose(bool disposing)
		{
			//if (_disposed)
			//	return;

			//if (disposing)
			//{
			//	DeviceManager?.CloseAll();
			//	GC.SuppressFinalize(this);
			//}

			//_disposed = true;
			//StopCommand.Execute(null);
		}

		// Public method to dispose of resources
		public void Dispose()
		{
			Dispose(true);
		}

		private void OnApplicationExit(object sender, ExitEventArgs e)
		{
			if (_disposed)
				return;

			try
			{
				DeviceManager?.CloseAll();
				GC.SuppressFinalize(this);
			}
			catch
			{
			}

			_disposed = true;
		}

		internal void Refresh()
		{
			//IsLiveViewSupported = true;
		}

		// Destructor
		~CameraMainVM()
		{
			Dispose();
		}
		#endregion
	}
}
