using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Database;
using Photobooth.Services;
using CameraControl.Devices;

namespace Photobooth.Windows
{
    public partial class PhotoboothSessionWindow : Window, INotifyPropertyChanged
    {
        private readonly TemplateData template;
        private readonly EventData eventData;
        private readonly PhotoboothService photoboothService;
        private readonly ImageProcessingService imageProcessingService;
        private readonly List<PhotoPlaceholder> photoPlaceholders;
        
        private DispatcherTimer countdownTimer;
        private int countdownValue;
        private int currentPhotoIndex = 0;
        private int totalPhotos;

        public ObservableCollection<CapturedPhotoViewModel> CapturedPhotos { get; set; }

        // Binding Properties
        private string _eventTitle;
        public string EventTitle
        {
            get => _eventTitle;
            set { _eventTitle = value; OnPropertyChanged(); }
        }

        private string _templateInfo;
        public string TemplateInfo
        {
            get => _templateInfo;
            set { _templateInfo = value; OnPropertyChanged(); }
        }

        private string _progressText;
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        private double _progressPercentage;
        public double ProgressPercentage
        {
            get => _progressPercentage;
            set { _progressPercentage = value; OnPropertyChanged(); }
        }

        private string _currentInstruction;
        public string CurrentInstruction
        {
            get => _currentInstruction;
            set { _currentInstruction = value; OnPropertyChanged(); }
        }

        private bool _canCapture = true;
        public bool CanCapture
        {
            get => _canCapture;
            set { _canCapture = value; OnPropertyChanged(); }
        }

        private bool _canRetake = false;
        public bool CanRetake
        {
            get => _canRetake;
            set { _canRetake = value; OnPropertyChanged(); }
        }

        private bool _canFinish = false;
        public bool CanFinish
        {
            get => _canFinish;
            set { _canFinish = value; OnPropertyChanged(); }
        }

        private bool _showRetakeButton = false;
        public bool ShowRetakeButton
        {
            get => _showRetakeButton;
            set { _showRetakeButton = value; OnPropertyChanged(); }
        }

        private bool _showFinishButton = false;
        public bool ShowFinishButton
        {
            get => _showFinishButton;
            set { _showFinishButton = value; OnPropertyChanged(); }
        }

        public PhotoboothSessionWindow(TemplateData template, EventData eventData)
        {
            InitializeComponent();
            DataContext = this;

            this.template = template;
            this.eventData = eventData;
            
            photoboothService = new PhotoboothService();
            imageProcessingService = new ImageProcessingService();
            photoPlaceholders = photoboothService.GetPhotoPlaceholders(template);
            
            totalPhotos = Math.Max(1, photoPlaceholders.Count);
            
            CapturedPhotos = new ObservableCollection<CapturedPhotoViewModel>();
            
            InitializeSession();
        }

        private void InitializeSession()
        {
            EventTitle = $"{eventData.Name} - {eventData.EventType}";
            TemplateInfo = $"{template.Name} ({totalPhotos} photo{(totalPhotos > 1 ? "s" : "")})";
            
            UpdateProgress();
            UpdateInstructions();
            
            // Initialize captured photos collection with placeholders
            for (int i = 0; i < totalPhotos; i++)
            {
                CapturedPhotos.Add(new CapturedPhotoViewModel(i + 1));
            }
        }

        private void UpdateProgress()
        {
            ProgressText = $"Photo {currentPhotoIndex + 1} of {totalPhotos}";
            ProgressPercentage = (double)currentPhotoIndex / totalPhotos * 100;
            
            ShowRetakeButton = currentPhotoIndex > 0;
            ShowFinishButton = currentPhotoIndex >= totalPhotos;
            CanFinish = currentPhotoIndex >= totalPhotos;
        }

        private void UpdateInstructions()
        {
            if (currentPhotoIndex >= totalPhotos)
            {
                CurrentInstruction = "🎉 All photos captured! Review your photos below.";
                CanCapture = false;
            }
            else
            {
                var photoNumber = currentPhotoIndex + 1;
                CurrentInstruction = totalPhotos == 1 
                    ? "📸 Ready to take your photo? Press the camera button!"
                    : $"📸 Ready for photo {photoNumber} of {totalPhotos}? Strike a pose!";
                CanCapture = true;
            }
        }

        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPhotoIndex >= totalPhotos) return;

            await StartCountdownAndCapture();
        }

        private async Task StartCountdownAndCapture()
        {
            try
            {
                CanCapture = false;
                
                // Show countdown (3-2-1)
                await ShowCountdown();
                
                // Capture photo
                var capturedImage = await CapturePhoto();
                
                if (capturedImage != null)
                {
                    // Flash effect immediately after capture
                    await ShowFlashEffect();
                    
                    // Process and add to collection
                    var photoViewModel = CapturedPhotos[currentPhotoIndex];
                    photoViewModel.IsProcessing = true;
                    photoViewModel.PreviewImage = capturedImage;
                    
                    // Process image for template insertion
                    await ProcessCapturedPhoto(capturedImage, currentPhotoIndex);
                    
                    photoViewModel.IsProcessing = false;
                    
                    // Show preview delay (let user see their photo)
                    CurrentInstruction = "📷 Great shot! Take a moment to review...";
                    await Task.Delay(3000); // 3 second preview delay
                    
                    // Move to next photo
                    currentPhotoIndex++;
                    UpdateProgress();
                    UpdateInstructions();
                    
                    // Brief pause before next photo if not finished
                    if (currentPhotoIndex < totalPhotos)
                    {
                        CurrentInstruction = "Get ready for the next photo...";
                        await Task.Delay(2000); // 2 second preparation delay
                        UpdateInstructions(); // Update to show next photo instruction
                    }
                }
                else
                {
                    MessageBox.Show("Failed to capture photo. Please try again.", "Camera Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during photo capture: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CanCapture = currentPhotoIndex < totalPhotos;
            }
        }

        private async Task ShowCountdown()
        {
            CountdownOverlay.Visibility = Visibility.Visible;
            
            for (int i = 3; i > 0; i--)
            {
                CountdownText.Text = i.ToString();
                
                // Animate countdown
                var scaleAnimation = new DoubleAnimation(1.5, 1.0, TimeSpan.FromMilliseconds(800));
                var opacityAnimation = new DoubleAnimation(1.0, 0.7, TimeSpan.FromMilliseconds(800));
                
                var scaleTransform = new ScaleTransform(1.0, 1.0);
                CountdownText.RenderTransform = scaleTransform;
                CountdownText.RenderTransformOrigin = new Point(0.5, 0.5);
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
                CountdownText.BeginAnimation(OpacityProperty, opacityAnimation);
                
                await Task.Delay(1000);
            }
            
            CountdownText.Text = "SMILE!";
            await Task.Delay(500);
            
            CountdownOverlay.Visibility = Visibility.Collapsed;
        }

        private async Task<WriteableBitmap> CapturePhoto()
        {
            try
            {
                // TODO: Integrate with your existing camera system
                // This is a placeholder - you'll need to connect to your CameraDeviceManager
                
                // For now, return a test image
                return await CreateTestPhoto();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Camera capture error: {ex.Message}");
                return null;
            }
        }

        private async Task<WriteableBitmap> CreateTestPhoto()
        {
            // Create a test photo (replace with actual camera capture)
            return await Task.Run(() =>
            {
                var width = 800;
                var height = 600;
                var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                
                var stride = width * 4;
                var pixels = new byte[height * stride];
                var random = new Random();
                
                // Generate random colored image as test photo
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var offset = y * stride + x * 4;
                        pixels[offset] = (byte)random.Next(100, 255);     // Blue
                        pixels[offset + 1] = (byte)random.Next(100, 255); // Green  
                        pixels[offset + 2] = (byte)random.Next(100, 255); // Red
                        pixels[offset + 3] = 255; // Alpha
                    }
                }
                
                bitmap.Dispatcher.Invoke(() =>
                {
                    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
                });
                
                return bitmap;
            });
        }

        private async Task ProcessCapturedPhoto(WriteableBitmap capturedImage, int photoIndex)
        {
            try
            {
                if (photoIndex < photoPlaceholders.Count)
                {
                    var placeholder = photoPlaceholders[photoIndex];
                    
                    // Resize and process image to fit placeholder
                    var processedImage = await imageProcessingService.ResizeImageForPlaceholder(
                        capturedImage, placeholder);
                    
                    // Store processed image (you might want to save this to database or file system)
                    // For now, we just keep it in memory
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Image processing error: {ex.Message}");
            }
        }

        private async Task ShowFlashEffect()
        {
            FlashOverlay.Visibility = Visibility.Visible;
            
            var flashAnimation = new DoubleAnimation(0, 0.8, TimeSpan.FromMilliseconds(100))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(1)
            };
            
            flashAnimation.Completed += (s, e) => FlashOverlay.Visibility = Visibility.Collapsed;
            
            FlashOverlay.BeginAnimation(OpacityProperty, flashAnimation);
            
            await Task.Delay(200);
        }

        private void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPhotoIndex > 0)
            {
                currentPhotoIndex--;
                
                // Reset the photo slot
                var photoToRetake = CapturedPhotos[currentPhotoIndex];
                photoToRetake.PreviewImage = null;
                photoToRetake.IsProcessing = false;
                
                UpdateProgress();
                UpdateInstructions();
            }
        }

        private async void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPhotoIndex >= totalPhotos)
            {
                // All photos captured - generate final composite
                await GenerateFinalComposite();
                
                this.DialogResult = true;
                Close();
            }
        }

        private async Task GenerateFinalComposite()
        {
            try
            {
                // TODO: Generate final composite image with all photos inserted into template
                // This would combine the template background with all captured photos
                
                MessageBox.Show("Photos captured successfully! Final composite will be generated.", 
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating final image: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to cancel the photo session? All captured photos will be lost.", 
                "Cancel Session", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                this.DialogResult = false;
                Close();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CapturedPhotoViewModel : INotifyPropertyChanged
    {
        public int PhotoNumber { get; }
        
        private WriteableBitmap _previewImage;
        public WriteableBitmap PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;
                OnPropertyChanged();
            }
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                _isProcessing = value;
                OnPropertyChanged();
            }
        }

        public CapturedPhotoViewModel(int photoNumber)
        {
            PhotoNumber = photoNumber;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}