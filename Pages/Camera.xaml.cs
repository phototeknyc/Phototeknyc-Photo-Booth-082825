using CameraControl.Devices;
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


using System.IO;
using CameraControl.Devices.Classes;
using CameraControl.Devices.Wifi;
using System.Threading;
using Path = System.IO.Path;
using System.Windows.Threading;
using System.Drawing;
using System.Windows.Interop;

namespace Photobooth.Pages
{
    /// <summary>
    /// Interaction logic for Camera.xaml
    /// </summary>
    public partial class Camera : Page
    {

        // Use singleton camera manager to maintain session across screens
        public CameraDeviceManager DeviceManager => Services.CameraSessionManager.Instance.DeviceManager;
        public string FolderForPhotos { get; set; }

        // for live view
        private DispatcherTimer dispatcherTimer = new DispatcherTimer();


        public Camera()
        {
            // Use singleton camera manager - don't create new instance
            // Event subscriptions moved to Loaded event to prevent duplicates

            
            InitializeComponent();


            
            Loaded += Camera_Screen_Loaded;
            Unloaded += Camera_Screen_Unloaded;


            cmb_cameras.SelectionChanged += cmb_cameras_SelectedIndexChanged;



            FolderForPhotos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Test");
            Log.LogError += Log_LogDebug;
            Log.LogDebug += Log_LogDebug;
            Log.LogInfo += Log_LogDebug;



            //////////////// for live view ////////////////
            // init timer
            dispatcherTimer.Tick += dispatcherTimer_Tick;
             int interval = 1000 / 30; // 30 fps
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, interval);

            // Diagnostics UI wiring
            DiagToggle.Checked += (s, e) => { DiagPanel.Visibility = Visibility.Visible; UpdateDiagnostics(); };
            DiagToggle.Unchecked += (s, e) => { DiagPanel.Visibility = Visibility.Collapsed; };
            Loaded += (s, e) => UpdateDiagnostics();
        }




        void Log_LogDebug(LogEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log_LogDebug(e));
                return;
            }
            
            textBoxLogs.AppendText((string)e.Message);
            if (e.Exception != null)
                textBoxLogs.AppendText((string)e.Exception.StackTrace);
            textBoxLogs.AppendText(Environment.NewLine);

            // Keep last error text for diagnostics (use exception presence as signal)
            if (e.Exception != null)
            {
                _lastErrorText = e.Message?.ToString();
                UpdateDiagnostics();
            }
        }

        private string _lastErrorText = string.Empty;

        private void UpdateDiagnostics()
        {
            if (DiagPanel.Visibility != Visibility.Visible) return;
            try
            {
                uint ver = CameraControl.Devices.Sony.SonySDKWrapper.GetSDKVersion();
                int major = (int)((ver & 0xFF000000) >> 24);
                int minor = (int)((ver & 0x00FF0000) >> 16);
                int patch = (int)((ver & 0x0000FF00) >> 8);
                DiagSdkVersion.Text = $"SDK: {major}.{minor}.{patch:D2}";

                var cam = DeviceManager?.SelectedCameraDevice;
                DiagConnection.Text = $"Connected: { (cam != null && cam.IsConnected ? "Yes" : "No") } ({cam?.DeviceName ?? "-"})";
                DiagLiveView.Text = $"Live View: {(cam != null && cam.GetCapability(CapabilityEnum.LiveView) ? "Yes" : "No")}";
                // PC Remote / Recording state are Sony-specific; show placeholder if not Sony
                DiagPcRemote.Text = "PC Remote: n/a";
                DiagRecording.Text = "Recording: n/a";
                DiagBattery.Text = (cam != null && cam.Battery > 0) ? $"Battery: {cam.Battery}%" : "Battery: n/a";
                DiagLastError.Text = string.IsNullOrWhiteSpace(_lastErrorText) ? "Last Error: (none)" : $"Last Error: {_lastErrorText}";
            }
            catch { }
        }

        private void RefreshDisplay()
        {
            cmb_cameras.Items.Clear();
            foreach (ICameraDevice cameraDevice in DeviceManager.ConnectedDevices)
            {
                cmb_cameras.Items.Add(cameraDevice);
            }
            cmb_cameras.DisplayMemberPath = "DeviceName";
            cmb_cameras.SelectedItem = DeviceManager.SelectedCameraDevice;
            DeviceManager.SelectedCameraDevice.CaptureInSdRam = true;
            // check if camera support live view
            btn_liveview.IsEnabled = DeviceManager.SelectedCameraDevice.GetCapability(CapabilityEnum.LiveView);
        }

        private void PhotoCaptured(object o)
        {
            PhotoCapturedEventArgs eventArgs = o as PhotoCapturedEventArgs;
            if (eventArgs == null)
            {
                Log.Error("PhotoCaptured: eventArgs is null");
                return;
            }
            
            Log.Debug($"PhotoCaptured: Device={eventArgs.CameraDevice?.GetType().Name}, FileName={eventArgs.FileName}, Handle={eventArgs.Handle?.GetType().Name}");
            
            try
            {
                string fileName = Path.Combine(FolderForPhotos, Path.GetFileName(eventArgs.FileName));
                Log.Debug($"PhotoCaptured: Target file path={fileName}");
                
                // if file exist try to generate a new filename to prevent file lost. 
                // This useful when camera is set to record in ram the the all file names are same.
                if (File.Exists(fileName))
                {
                    fileName =
                      StaticHelper.GetUniqueFilename(
                        Path.GetDirectoryName(fileName) + "\\" + Path.GetFileNameWithoutExtension(fileName) + "_", 0,
                        Path.GetExtension(fileName));
                    Log.Debug($"PhotoCaptured: Using unique filename={fileName}");
                }

                // check the folder of filename, if not found create it
                if (!Directory.Exists(Path.GetDirectoryName(fileName)))
                {
                    Log.Debug($"PhotoCaptured: Creating directory={Path.GetDirectoryName(fileName)}");
                    Directory.CreateDirectory(Path.GetDirectoryName(fileName));
                }
                
                Log.Debug("PhotoCaptured: Starting TransferFile");
                eventArgs.CameraDevice.TransferFile(eventArgs.Handle, fileName);
                Log.Debug("PhotoCaptured: TransferFile completed");
                
                // the IsBusy may used internally, if file transfer is done should set to false  
                eventArgs.CameraDevice.IsBusy = false;
                
                if (File.Exists(fileName))
                {
                    // Marshal UI update to UI thread
                    Dispatcher.Invoke(() => 
                    {
                        img_photo.Source = (new ImageSourceConverter()).ConvertFromString(fileName) as ImageSource;
                    });
                    Log.Debug($"PhotoCaptured: Image saved successfully, size={new FileInfo(fileName).Length} bytes");
                }
                else
                {
                    Log.Error($"PhotoCaptured: File was not created: {fileName}");
                }
            }
            catch (Exception exception)
            {
                Log.Error("PhotoCaptured: Exception occurred", exception);
                eventArgs.CameraDevice.IsBusy = false;
                MessageBox.Show("Error download photo from camera :\n" + exception.Message + "\n" + exception.StackTrace);
            }
        }

        void DeviceManager_CameraDisconnected(ICameraDevice cameraDevice)
        {
            RefreshDisplay();
        }

        void DeviceManager_PhotoCaptured(object sender, PhotoCapturedEventArgs eventArgs)
        {
            PhotoCaptured(eventArgs);
        }

        void DeviceManager_CameraConnected(ICameraDevice cameraDevice)
        {
            RefreshDisplay();
        }

        void DeviceManager_CameraSelected(ICameraDevice oldcameraDevice, ICameraDevice newcameraDevice)
        {
            btn_liveview.IsEnabled = newcameraDevice.GetCapability(CapabilityEnum.LiveView);
        }

        private void Camera_Screen_Loaded(object sender, EventArgs e)
        {
            // Subscribe to camera events (will be unsubscribed in Unloaded)
            DeviceManager.CameraSelected += DeviceManager_CameraSelected;
            DeviceManager.CameraConnected += DeviceManager_CameraConnected;
            DeviceManager.PhotoCaptured += DeviceManager_PhotoCaptured;
            DeviceManager.CameraDisconnected += DeviceManager_CameraDisconnected;
            
            // Prepare camera using singleton manager
            Services.CameraSessionManager.Instance.PrepareCameraForUse();
            
            // Connect to camera if not already connected
            if (DeviceManager.ConnectedDevices.Count == 0)
            {
                DeviceManager.ConnectToCamera();
            }
            RefreshDisplay();
        }
        
        private void Camera_Screen_Unloaded(object sender, EventArgs e)
        {
            // Unsubscribe from camera events to prevent duplicate handlers
            DeviceManager.CameraSelected -= DeviceManager_CameraSelected;
            DeviceManager.CameraConnected -= DeviceManager_CameraConnected;
            DeviceManager.PhotoCaptured -= DeviceManager_PhotoCaptured;
            DeviceManager.CameraDisconnected -= DeviceManager_CameraDisconnected;
            
            // Stop live view timer
            dispatcherTimer.Stop();
            
            // Cleanup camera for screen change using singleton manager
            Services.CameraSessionManager.Instance.CleanupCameraForScreenChange();
        }

        private void btn_capture_Click(object sender, RoutedEventArgs e)
        {
            Capture();
        }

        private void Capture()
        {
            bool retry;
            do
            {
                retry = false;
                try
                {
                    DeviceManager.SelectedCameraDevice.CapturePhoto();
                }
                catch (DeviceException exception)
                {
                    // if device is bussy retry after 100 miliseconds
                    if (exception.ErrorCode == ErrorCodes.MTP_Device_Busy ||
                        exception.ErrorCode == ErrorCodes.ERROR_BUSY)
                    {
                        // !!!!this may cause infinite loop
                        Thread.Sleep(100);
                        retry = true;
                    }
                    else
                    {
                        MessageBox.Show("Error occurred :" + exception.Message);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error occurred :" + ex.Message);
                }

            } while (retry);
        }

        private void cmb_cameras_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            int index = cmb_cameras.SelectedIndex;
            if (cmb_cameras.SelectedIndex >= 0 && index < DeviceManager.ConnectedDevices.Count)
            {
                DeviceManager.SelectedCameraDevice = DeviceManager.ConnectedDevices[index];
            }
        }

        private void btn_wifi_Click(object sender, EventArgs e)
        {
            try
            {
                IWifiDeviceProvider wifiDeviceProvider = new SonyProvider();
                DeviceManager.AddDevice(wifiDeviceProvider.Connect("<Auto>"));
            }
            catch (Exception exception)
            {
                MessageBox.Show("Unable to connect to WiFi device " + exception.Message);
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                IWifiDeviceProvider wifiDeviceProvider = new PtpIpProvider();
                DeviceManager.AddDevice(wifiDeviceProvider.Connect("192.168.1.1"));
            }
            catch (Exception exception)
            {
                MessageBox.Show("Unable to connect to WiFi device " + exception.Message);
            }
        }








        /////////////////////////////////////////////////////////////////////////////////////////
        ///                              LIVE VIEW                                            ///
        /////////////////////////////////////////////////////////////////////////////////////////
        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            // code goes here
            LiveViewData liveViewData = null;

            //log current camera name
            Log.InfoWithWriteLine("Current camera: " + DeviceManager.SelectedCameraDevice.DeviceName);
            try
            {
                liveViewData = DeviceManager.SelectedCameraDevice.GetLiveViewImage();
            }
            catch (Exception)
            {
                return;
            }

            if (liveViewData == null || liveViewData.ImageData == null)
            {
                return;
            }
            try
            {
                // convert bitmap to BitmapImage
                var dst = new Bitmap(new MemoryStream(
                                            liveViewData.ImageData,
                                            liveViewData.ImageDataPosition,
                                            liveViewData.ImageData.Length -
                                            liveViewData.ImageDataPosition));
                img_photo.Source = Imaging.CreateBitmapSourceFromHBitmap(dst.GetHbitmap(),
                                  IntPtr.Zero,
                                  Int32Rect.Empty,
                                  BitmapSizeOptions.FromEmptyOptions());
            }
            catch (Exception)
            {
            }
        }

        // to open new form
        private void btn_liveview_Click(object sender, EventArgs e)
        {
            //LiveViewForm form = new LiveViewForm(DeviceManager.SelectedCameraDevice);
            //form.ShowDialog();
        }

        private void startButton_clicked(object sender, RoutedEventArgs e)
        {
            dispatcherTimer.Start();

            bool retry;
            do
            {
                retry = false;
                try
                {
                    DeviceManager.SelectedCameraDevice.StartLiveView();
                }
                catch (DeviceException exception)
                {
                    if (exception.ErrorCode == ErrorCodes.MTP_Device_Busy || exception.ErrorCode == ErrorCodes.ERROR_BUSY)
                    {
                        // this may cause infinite loop
                        Thread.Sleep(100);
                        retry = true;
                    }
                    else
                    {
                        MessageBox.Show("Error occurred :" + exception.Message);
                    }
                }

            } while (retry);
        }

        private void stopButton_Clicked(object sender, RoutedEventArgs e)
        {
            bool retry;
            do
            {
                retry = false;
                try
                {
                    dispatcherTimer.Stop();
                    // wait for last get live view image
                    Thread.Sleep(500);
                    DeviceManager.SelectedCameraDevice.StopLiveView();
                }
                catch (DeviceException exception)
                {
                    if (exception.ErrorCode == ErrorCodes.MTP_Device_Busy || exception.ErrorCode == ErrorCodes.ERROR_BUSY)
                    {
                        // this may cause infinite loop
                        Thread.Sleep(100);
                        retry = true;
                    }
                    else
                    {
                        MessageBox.Show("Error occurred :" + exception.Message);
                    }
                }

            } while (retry);
        }
    }
}
