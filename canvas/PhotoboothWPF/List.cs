using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using System.Xml.Linq;

namespace PhotoboothWPF
{
    class List
    {
        public static Border GenerateBorderWithStackPanelAndComboBox(string name, string[] comboBoxItems, SelectionChangedEventHandler selectionChangedHandler)
        {
            // Create Border
            Border border = new Border
            {
                BorderThickness = new System.Windows.Thickness(0, 0,0,0.5),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Padding = new System.Windows.Thickness(0,0,0,5),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC"))
            };


            // Create StackPanel
            StackPanel stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Create TextBlock
            TextBlock textBlock = new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = Brushes.White,
                Margin = new System.Windows.Thickness(5),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Normal
            };

            // Create ComboBox
            ComboBox comboBox = new ComboBox
            {
                FontSize = 12,
                Name = "comboBox",
             
                Height = 25,
                Margin = new System.Windows.Thickness(5),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,

                FontWeight = FontWeights.Normal
            };

            // Add ComboBoxItems to ComboBox
            foreach (var item in comboBoxItems)
            {
                comboBox.Items.Add(new ComboBoxItem { Content = item });
            }

            // Add event handler to ComboBox
            comboBox.SelectionChanged += selectionChangedHandler;

            // Add elements to StackPanel
            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(comboBox);

            // Add StackPanel to Border
            border.Child = stackPanel;

            return border;
        }
    }
}
