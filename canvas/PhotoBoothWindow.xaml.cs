using DesignerCanvas;
﻿using Photobooth.Pages;
using Photobooth.Resources.Controls;
using System;
using System.Collections.Generic;
using System.IO;
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

namespace Photobooth
{
    /// <summary>
    /// Interaction logic for PhotoBoothWindow.xaml
    /// </summary>
    public partial class PhotoBoothWindow : Window
    {
        public PhotoBoothWindow()
        {
            InitializeComponent();
            //dcvs.CanvasRatio = 4.0 / 6.0;

            //dcvs.SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;
        }

        private void SelectedItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            //// clear selection of orientation checkboxes, and properties
            //comboBox.SelectedIndex = -1;
            //comboBox1.SelectedIndex = -1;
        }

        private void dcvs_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void dcvs_MouseMove(object sender, MouseEventArgs e)
        {
            //// todo remove it later
            //// this listener is not needed as it is already handled by the designer canvas
            //try
            //{
            //    MousePositionLabel.Content = dcvs_container.dcvs.PointToCanvas(e.GetPosition(dcvs_container.dcvs));
            //}
            //catch (Exception)
            //{ }
        }

        //private void ActionImportImage(object sender, RoutedEventArgs e)
        //{

        //    try
        //    {
        //        Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
        //        openFileDialog.Filter = "Image files (*.jpg, *.jpeg, *.jpe, *.jfif, *.png, *.bmp, *.dib, *.gif, *.tif, *.tiff, *.ico, *.svg, *.svgz, *.wmf, *.emf, *.webp)|*.jpg; *.jpeg; *.jpe; *.jfif; *.png; *.bmp; *.dib; *.gif; *.tif; *.tiff; *.ico; *.svg; *.svgz; *.wmf; *.emf; *.webp|All files (*.*)|*.*";
        //        openFileDialog.Title = "Select a background image";
        //        if (openFileDialog.ShowDialog() == true)
        //        {
        //            // always use for aspect ratio
        //            var img = new ImageCanvasItem(0, 0, dcvs.Width / 2, dcvs.Height, new BitmapImage(new Uri(openFileDialog.FileName)), 2, 6);


        //            dcvs.Items.Add(img);
        //            ShowPropertiesOfCanvasItem(img);
        //        }
        //    }
        //    catch (FileNotFoundException ex)
        //    {
        //        MessageBox.Show(ex.Message, "File not found", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //    catch (Exception)
        //    { }
        //}

        //private void ActionPortraitOrientation(object sender, RoutedEventArgs e)
        //{
        //    ActionOrientation(true);
        //}

        //private void ActionLandscapeOrientation(object sender, RoutedEventArgs e)
        //{
        //    ActionOrientation(false);
        //}

        //private void ActionOrientation(bool isPortrait)
        //{
        //    // for each selected item
        //    foreach(IBoxCanvasItem item in dcvs.SelectedItems)
        //    {
        //        // calculate dimensions, width will be higher in portrait
        //        // if convert to portrait, inverse ratio, if > 1
        //        if (isPortrait && item.AspectRatio > 1)
        //        {
        //            item.AspectRatio = 1 / item.AspectRatio;
        //        }
        //        else if (!isPortrait && item.AspectRatio < 1)
        //        {
        //            item.AspectRatio = 1 / item.AspectRatio;
        //        }
        //    }
        //}

        //private void ActionChangeCanvasSize(object sender, RoutedEventArgs e)
        //{
        //    // get value of content from sender
        //    String content = (sender as ComboBoxItem).Content.ToString();

        //    // split string to width and height
        //    String[] tokens = content.Split("x");


        //    dcvs.Width = Convert.ToInt32(tokens[0]);
        //    dcvs.Height = Convert.ToInt32(tokens[0]);

        //    // update aspect ratio
        //    dcvs.CanvasRatio = dcvs.Width / dcvs.Height;
        //}


        //private void btnExport_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        // save file
        //        Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
        //        saveFileDialog.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tif)|*.tif|GIF Image (*.gif)|*.gif|All files (*.*)|*.*";
        //        saveFileDialog.Title = "Save an image";
        //        if (saveFileDialog.ShowDialog() == true)
        //        {
        //            var encoder = CanvasImageExporter.EncoderFromFileName(saveFileDialog.FileName);
        //            using (var s = File.Open(saveFileDialog.FileName, FileMode.Create))
        //            {
        //                CanvasImageExporter.ExportImage(dcvs_container.dcvs, s, encoder, 300, 300);
        //            }
        //        }
        //    }
        //    catch (Exception)
        //    { }
        //}

        //private void btnPrint_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        BitmapSource image = CanvasImageExporter.CreateImage(dcvs_container.dcvs, 300, 300);
        //        // print as 4x6 image and also show the preview image
        //        var printDialog = new PrintDialog();
        //        if (printDialog.ShowDialog() == true)
        //        {
        //            var printCapabilities = printDialog.PrintQueue.GetPrintCapabilities(printDialog.PrintTicket);
        //            var scale = Math.Min(printCapabilities.PageImageableArea.ExtentWidth / image.Width, printCapabilities.PageImageableArea.ExtentHeight / image.Height);
        //            var printImage = new Image
        //            {
        //                Source = image,
        //                Stretch = Stretch.Uniform,
        //                StretchDirection = StretchDirection.DownOnly,
        //                Width = image.Width * scale,
        //                Height = image.Height * scale
        //            };
        //            var printCanvas = new Canvas
        //            {
        //                Width = printImage.Width,
        //                Height = printImage.Height
        //            };
        //            printCanvas.Children.Add(printImage);
        //            printDialog.PrintVisual(printCanvas, "Photobooth");
        //        }
        //    }
        //    catch (Exception)
        //    { }
        //}

        // todo create clear canvas button, and call it from there
        //private void ActionClearCanvas(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        dcvs.Items.Clear();
        //    }
        //    catch (Exception)
        //    { }
        //}

        //private void ActionPlaceholderSelected(object sender, RoutedEventArgs e)
        //{
        //    ToolBoxList.SelectedIndex = -1;

        //    // Create a new PlaceholderCanvasItem with specified properties
        //    var placeholderItem = new PlaceholderCanvasItem(50, 50, 40, 40, 3, 2);

        //    // Add the PlaceholderCanvasItem to your canvas or UI element
        //    dcvs.Items.Add(placeholderItem);
        //    ShowPropertiesOfCanvasItem(placeholderItem);
        //}

        //private void ShowPropertiesOfCanvasItem(CanvasItem item)
        //{
        //    // show properties of item
        //    tbLocationX.Text = item.Location.X.ToString();
        //    tbLocationY.Text = item.Location.Y.ToString();

        //    tbSizeW.Text = item.Width.ToString();
        //    tbSizeY.Text = item.Height.ToString();
        //}

        //private void btnLockSize_Click(object sender, RoutedEventArgs e)
        //{
        //    // lock all selected items
        //    foreach (IBoxCanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.Resizeable = !item.Resizeable;

        //        //  remove and re-add to update the adorner
        //        dcvs_container.dcvs.Items.Remove(item);
        //        dcvs_container.dcvs.Items.Add(item);
        //    }
        //}

        //private void btnLockAspectRatio_Click(object sender, RoutedEventArgs e)
        //{
        //    // lock all selected items
        //    foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.LockedAspectRatio = !item.LockedAspectRatio;
        //        //if (item.LockedAspectRatio)
        //        //{
        //        //	item.LockedAspectRatio = false;
        //        //} else
        //        //{
        //        //	item.LockedAspectRatio = true;
        //        //	item.AspectRatio = item.AspectRatio;
        //        //}
        //    }
        //}

        //private void btnLockPosition_Click(object sender, RoutedEventArgs e)
        //{
        //    // lock all selected items
        //    foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.LockedPosition = !item.LockedPosition;
        //    }
        //}

        //private void btnHorizontalVerticalRatio_Click(object sender, RoutedEventArgs e)
        //{
        //    // reverse aspect ratios of all selected items
        //    foreach (CanvasItem item in dcvs_container.dcvs.SelectedItems)
        //    {
        //        item.reverseAspectRatio();
        //    }
        //}


        //private void Window_Initialized(object sender, EventArgs e)
        //{
        //    dcvs_container.MouseMove += dcvs_MouseMove;
        //    dcvs_container.MouseDown += dcvs_MouseDown;
        //}

        //private void btnHVCanvas_Click(object sender, RoutedEventArgs e)
        //{
        //    dcvs_container.CanvasRatio = 1 / dcvs_container.CanvasRatio;
        //}
        private void sidebar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedButton = sidebar.SelectedItem as NavButton;
            Navigate(frame,selectedButton);
            
        }
        public static void Navigate(Frame frame, NavButton button)
        {
            if (button != null)
            {
                try
                {
                    var uri = button.NavLink;
                    //Console.WriteLine("URL: ", uri.ToString());
                    //MessageBox.Show("URL: " + uri.ToString());
                    if (uri != null)
                    {
                        frame.Navigate(uri);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }

            }
        }


        private void sidebar1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedButton = sidebar1.SelectedItem as NavButton;
            Navigate(frame, selectedButton);
        }

		private void MenuItem_Click(object sender, RoutedEventArgs e)
		{

		}

     

		private void MenuItem_Click_1(object sender, RoutedEventArgs e)
		{
            if (frame.Content != MainPage.Instance)
            {
				frame.Navigate(MainPage.Instance);
			}

            MainPage.Instance.dcvs.ImportImage();
		}

        private void MenuItem_Click_2(object sender, RoutedEventArgs e)
        {
            if (frame.Content != MainPage.Instance)
            {
                frame.Navigate(MainPage.Instance);
            }

            MainPage.Instance.dcvs.ExportImage();
        }

        private void MenuItem_Click_3(object sender, RoutedEventArgs e)
        {
            if (frame.Content != MainPage.Instance)
            {
                frame.Navigate(MainPage.Instance);
            }

            MainPage.Instance.dcvs.PrintImage();
        }
    }
}
