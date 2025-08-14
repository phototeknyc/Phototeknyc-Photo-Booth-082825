using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Photobooth.Services;

namespace Photobooth
{
    public partial class PrintCopyDialog : Window
    {
        private int copyCount = 1;
        private int maxCopies = 5;
        private int sessionRemainingPrints;
        private int eventRemainingPrints;
        private string imagePath;
        private string sessionId;
        private bool isOriginal2x6Format;
        private bool isDuplicatedTo4x6;
        private PrintService printService;
        
        public bool PrintConfirmed { get; private set; }
        public int SelectedCopies { get; private set; }
        
        public PrintCopyDialog(string imagePath, string sessionId, bool isOriginal2x6Format = false)
        {
            InitializeComponent();
            
            this.imagePath = imagePath;
            this.sessionId = sessionId;
            this.isOriginal2x6Format = isOriginal2x6Format;
            
            // Check if 2x6 duplication is enabled and this is a 2x6 format
            this.isDuplicatedTo4x6 = isOriginal2x6Format && Properties.Settings.Default.Duplicate2x6To4x6;
            
            this.printService = PrintService.Instance;
            
            // Load default copy count from settings
            if (Properties.Settings.Default.DefaultPrintCopies > 0)
            {
                copyCount = Properties.Settings.Default.DefaultPrintCopies;
            }
            
            // Get remaining prints
            sessionRemainingPrints = printService.GetRemainingSessionPrints(sessionId);
            eventRemainingPrints = printService.GetRemainingEventPrints();
            
            // Calculate max copies based on limits
            maxCopies = Math.Min(
                Properties.Settings.Default.MaxSessionPrints > 0 ? sessionRemainingPrints : int.MaxValue,
                Properties.Settings.Default.MaxEventPrints > 0 ? eventRemainingPrints : int.MaxValue
            );
            
            // Ensure at least 1 copy if limits allow
            if (maxCopies <= 0)
            {
                maxCopies = 0;
                copyCount = 0;
                PrintButton.IsEnabled = false;
                WarningText.Text = "Print limit reached!";
                WarningText.Visibility = Visibility.Visible;
            }
            else
            {
                // Ensure copy count doesn't exceed max
                copyCount = Math.Min(copyCount, maxCopies);
            }
            
            // Load preview image
            LoadPreviewImage();
            
            // Update UI
            UpdateUI();
        }
        
        private void LoadPreviewImage()
        {
            try
            {
                if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 400; // Limit preview size for performance
                    bitmap.EndInit();
                    PreviewImage.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load preview image: {ex.Message}");
            }
        }
        
        private void UpdateUI()
        {
            // For 2x6 duplicated to 4x6, show the actual number of strips
            if (isDuplicatedTo4x6)
            {
                CopyCountText.Text = copyCount.ToString();
                int totalStrips = copyCount * 2; // Each 4x6 print contains 2 strips
                StripCountText.Text = $"({totalStrips} strips)";
                StripCountText.Visibility = Visibility.Visible;
            }
            else
            {
                CopyCountText.Text = copyCount.ToString();
                StripCountText.Visibility = Visibility.Collapsed;
            }
            
            // Update limits display - for 2x6 duplicated, each print counts as 2 strips
            int effectivePrints = isDuplicatedTo4x6 ? copyCount * 2 : copyCount;
            
            if (Properties.Settings.Default.MaxSessionPrints > 0)
            {
                if (isDuplicatedTo4x6)
                {
                    SessionLimitText.Text = $"Session strips remaining: {sessionRemainingPrints}";
                }
                else
                {
                    SessionLimitText.Text = $"Session prints remaining: {sessionRemainingPrints}";
                }
                SessionLimitText.Visibility = Visibility.Visible;
            }
            else
            {
                SessionLimitText.Visibility = Visibility.Collapsed;
            }
            
            if (Properties.Settings.Default.MaxEventPrints > 0)
            {
                if (isDuplicatedTo4x6)
                {
                    EventLimitText.Text = $"Event strips remaining: {eventRemainingPrints}";
                }
                else
                {
                    EventLimitText.Text = $"Event prints remaining: {eventRemainingPrints}";
                }
                EventLimitText.Visibility = Visibility.Visible;
            }
            else
            {
                EventLimitText.Visibility = Visibility.Collapsed;
            }
            
            // Enable/disable buttons based on limits
            DecreaseButton.IsEnabled = copyCount > 1;
            
            // For 2x6 duplicated, check if we can add more (each print = 2 strips)
            if (isDuplicatedTo4x6)
            {
                int nextEffectivePrints = (copyCount + 1) * 2;
                IncreaseButton.IsEnabled = copyCount < maxCopies && 
                                          nextEffectivePrints <= sessionRemainingPrints && 
                                          nextEffectivePrints <= eventRemainingPrints;
            }
            else
            {
                IncreaseButton.IsEnabled = copyCount < maxCopies;
            }
            
            PrintButton.IsEnabled = copyCount > 0 && effectivePrints <= maxCopies;
            
            // Show warning if at limit
            if (isDuplicatedTo4x6 && effectivePrints >= Math.Min(sessionRemainingPrints, eventRemainingPrints))
            {
                WarningText.Text = "Maximum strips reached for this session/event";
                WarningText.Visibility = Visibility.Visible;
            }
            else if (copyCount == maxCopies && maxCopies > 0)
            {
                WarningText.Text = "Maximum copies reached for this session/event";
                WarningText.Visibility = Visibility.Visible;
            }
            else if (maxCopies == 0)
            {
                WarningText.Text = "Print limit reached!";
                WarningText.Visibility = Visibility.Visible;
            }
            else
            {
                WarningText.Visibility = Visibility.Collapsed;
            }
        }
        
        private void DecreaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (copyCount > 1)
            {
                copyCount--;
                UpdateUI();
            }
        }
        
        private void IncreaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (copyCount < maxCopies)
            {
                copyCount++;
                UpdateUI();
            }
        }
        
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            PrintConfirmed = true;
            SelectedCopies = copyCount;
            
            // Save the selected copy count as the new default
            Properties.Settings.Default.DefaultPrintCopies = copyCount;
            Properties.Settings.Default.Save();
            
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            PrintConfirmed = false;
            DialogResult = false;
            Close();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            PrintConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}