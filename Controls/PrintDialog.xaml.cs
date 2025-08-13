using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Services;

namespace Photobooth.Controls
{
    public partial class PrintDialog : Window
    {
        private ObservableCollection<SessionGroup> sessions;
        private SessionGroup selectedSession;
        private List<PhotoItem> photosToPrint;

        public PrintDialog(ObservableCollection<SessionGroup> sessionGroups)
        {
            InitializeComponent();
            sessions = sessionGroups;
            photosToPrint = new List<PhotoItem>();
            
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            // Load sessions into combo box
            foreach (var session in sessions)
            {
                var displayText = $"{session.SessionName} - {session.Photos.Count} photos ({session.SessionTime})";
                sessionComboBox.Items.Add(new ComboBoxItem 
                { 
                    Content = displayText, 
                    Tag = session 
                });
            }

            // Set default copies
            copiesTextBox.Text = Properties.Settings.Default.DefaultPrintCopies.ToString();
            
            // Set print only originals
            printOnlyOriginalsCheckBox.IsChecked = Properties.Settings.Default.PrintOnlyOriginals;
            
            // Update print limits display
            UpdatePrintLimitsDisplay();
        }

        private void UpdatePrintLimitsDisplay()
        {
            try
            {
                var printService = PrintService.Instance;
                
                // Calculate remaining limits
                int sessionRemaining = printService.GetRemainingSessionPrints(selectedSession?.SessionId ?? "");
                int eventRemaining = printService.GetRemainingEventPrints();
                
                // Update display
                if (Properties.Settings.Default.MaxSessionPrints <= 0)
                    sessionLimitText.Text = "Unlimited";
                else
                    sessionLimitText.Text = $"{sessionRemaining} remaining";
                
                if (Properties.Settings.Default.MaxEventPrints <= 0)
                    eventLimitText.Text = "Unlimited";
                else
                    eventLimitText.Text = $"{eventRemaining} remaining";
                
                // Update colors based on availability
                sessionLimitText.Foreground = sessionRemaining > 0 || Properties.Settings.Default.MaxSessionPrints <= 0 
                    ? System.Windows.Media.Brushes.LimeGreen 
                    : System.Windows.Media.Brushes.OrangeRed;
                    
                eventLimitText.Foreground = eventRemaining > 0 || Properties.Settings.Default.MaxEventPrints <= 0 
                    ? System.Windows.Media.Brushes.LimeGreen 
                    : System.Windows.Media.Brushes.OrangeRed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating print limits: {ex.Message}");
            }
        }

        private void SessionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = sessionComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is SessionGroup session)
            {
                selectedSession = session;
                LoadPhotosForSession(session);
                UpdatePrintLimitsDisplay();
                
                // Enable print button if photos are available
                printButton.IsEnabled = photosToPrint.Count > 0;
            }
            else
            {
                selectedSession = null;
                photosToPrint.Clear();
                photoPreviewItems.ItemsSource = null;
                photoPreviewHeader.Visibility = Visibility.Collapsed;
                photoPreviewScrollViewer.Visibility = Visibility.Collapsed;
                printButton.IsEnabled = false;
            }
        }

        private void LoadPhotosForSession(SessionGroup session)
        {
            try
            {
                photosToPrint.Clear();
                
                // Add photos based on print settings
                if (printOnlyOriginalsCheckBox.IsChecked == true)
                {
                    photosToPrint.AddRange(session.OriginalPhotos);
                }
                else
                {
                    // Add all photos (originals, filtered, templates)
                    photosToPrint.AddRange(session.Photos);
                }
                
                // Update preview
                photoPreviewItems.ItemsSource = photosToPrint;
                
                // Show preview section if there are photos
                if (photosToPrint.Count > 0)
                {
                    photoPreviewHeader.Visibility = Visibility.Visible;
                    photoPreviewScrollViewer.Visibility = Visibility.Visible;
                    photoPreviewHeader.Text = $"Photos to Print ({photosToPrint.Count} photos)";
                }
                else
                {
                    photoPreviewHeader.Visibility = Visibility.Collapsed;
                    photoPreviewScrollViewer.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading photos for session: {ex.Message}");
                MessageBox.Show($"Error loading photos: {ex.Message}", "Print Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate input
                if (selectedSession == null)
                {
                    MessageBox.Show("Please select a session to print.", "Print", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (photosToPrint.Count == 0)
                {
                    MessageBox.Show("No photos available to print for the selected session.", "Print", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(copiesTextBox.Text, out int copies) || copies <= 0)
                {
                    MessageBox.Show("Please enter a valid number of copies (1 or greater).", "Print", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    copiesTextBox.Focus();
                    return;
                }

                // Get photo paths to print
                var photoPaths = photosToPrint.Select(p => p.FilePath).ToList();
                
                // Print using PrintService
                var printService = PrintService.Instance;
                var result = printService.PrintPhotos(photoPaths, selectedSession.SessionId, copies);
                
                if (result.Success)
                {
                    MessageBox.Show($"{result.Message}\n\nSession prints remaining: {result.RemainingSessionPrints}\nEvent prints remaining: {result.RemainingEventPrints}", 
                        "Print Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Update limits display
                    UpdatePrintLimitsDisplay();
                    
                    // Close dialog on successful print
                    this.DialogResult = true;
                }
                else
                {
                    MessageBox.Show(result.Message, "Print Failed", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error printing photos: {ex.Message}");
                MessageBox.Show($"Error printing photos: {ex.Message}", "Print Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        // Update photos when print only originals checkbox changes
        private void PrintOnlyOriginalsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (selectedSession != null)
            {
                LoadPhotosForSession(selectedSession);
            }
        }

        private void PrintOnlyOriginalsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            PrintOnlyOriginalsCheckBox_CheckedChanged(sender, e);
        }
    }
}