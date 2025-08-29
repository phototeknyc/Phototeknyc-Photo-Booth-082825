using System;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Services;

namespace Photobooth.Controls
{
    /// <summary>
    /// Simple UI control for selecting number of print copies using +/- buttons
    /// Follows clean architecture - no business logic, only UI concerns
    /// </summary>
    public partial class PrintCopiesModal : UserControl
    {
        private int _currentCopyCount = 1;
        private int _maxCopies = 5;
        
        public event EventHandler<int> CopiesSelected;
        public event EventHandler SelectionCancelled;
        
        public PrintCopiesModal()
        {
            InitializeComponent();
            LoadSettings();
        }
        
        private void LoadSettings()
        {
            try
            {
                var settings = PrintSettingsService.Instance;
                _maxCopies = settings.MaxCopiesInModal;
                _currentCopyCount = 1;
                UpdateDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PrintCopiesModal: Error loading settings: {ex.Message}");
                _maxCopies = 5;
                _currentCopyCount = 1;
                UpdateDisplay();
            }
        }
        
        private void UpdateDisplay()
        {
            CopyCountText.Text = _currentCopyCount.ToString();
            MinusButton.IsEnabled = _currentCopyCount > 1;
            PlusButton.IsEnabled = _currentCopyCount < _maxCopies;
        }
        
        private void MinusButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCopyCount > 1)
            {
                _currentCopyCount--;
                UpdateDisplay();
            }
        }
        
        private void PlusButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentCopyCount < _maxCopies)
            {
                _currentCopyCount++;
                UpdateDisplay();
            }
        }
        
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"PrintCopiesModal: User confirmed {_currentCopyCount} copies");
            CopiesSelected?.Invoke(this, _currentCopyCount);
            this.Visibility = Visibility.Collapsed;
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("PrintCopiesModal: User cancelled selection");
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
            this.Visibility = Visibility.Collapsed;
        }
        
        public void Show()
        {
            LoadSettings();
            _currentCopyCount = 1;
            UpdateDisplay();
            this.Visibility = Visibility.Visible;
        }
        
        public void Hide()
        {
            this.Visibility = Visibility.Collapsed;
        }
    }
}