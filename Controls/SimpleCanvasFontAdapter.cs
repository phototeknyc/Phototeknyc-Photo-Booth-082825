using System;
using System.Windows;
using System.Windows.Media;

namespace Photobooth.Controls
{
    /// <summary>
    /// Adapter to make SimpleDesignerCanvas work with the existing FontControlsPanel
    /// </summary>
    public class SimpleCanvasFontAdapter
    {
        private SimpleDesignerCanvas _canvas;
        private FontControlsPanel _fontPanel;
        private SimpleTextItem _currentTextItem;

        public SimpleCanvasFontAdapter(SimpleDesignerCanvas canvas, FontControlsPanel fontPanel)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _fontPanel = fontPanel ?? throw new ArgumentNullException(nameof(fontPanel));

            // Connect events
            _canvas.ItemSelected += Canvas_ItemSelected;
            _canvas.SelectionCleared += Canvas_SelectionCleared;
            _fontPanel.FontChanged += FontPanel_FontChanged;
        }

        private void Canvas_ItemSelected(object sender, SimpleCanvasItem item)
        {
            if (item is SimpleTextItem textItem)
            {
                _currentTextItem = textItem;
                UpdateFontPanelFromTextItem(textItem);
                _fontPanel.SetSelectedTextItem(CreateFontControlsTextAdapter(textItem));
            }
            else
            {
                _currentTextItem = null;
                _fontPanel.SetSelectedTextItem(null);
            }
        }

        private void Canvas_SelectionCleared(object sender, EventArgs e)
        {
            _currentTextItem = null;
            _fontPanel.SetSelectedTextItem(null);
        }

        private void FontPanel_FontChanged(object sender, FontChangedEventArgs e)
        {
            if (_currentTextItem != null)
            {
                ApplyFontChangesToTextItem(_currentTextItem, e);
            }
        }

        private void UpdateFontPanelFromTextItem(SimpleTextItem textItem)
        {
            // The FontControlsPanel should automatically update when SetSelectedTextItem is called
            // The adapter object we create will provide the necessary property values
        }

        private void ApplyFontChangesToTextItem(SimpleTextItem textItem, FontChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "FontFamily":
                    textItem.FontFamily = e.FontFamily;
                    break;
                case "FontSize":
                    textItem.FontSize = e.FontSize;
                    break;
                case "FontWeight":
                    textItem.FontWeight = e.FontWeight;
                    break;
                case "FontStyle":
                    textItem.FontStyle = e.FontStyle;
                    break;
                case "Foreground":
                case "TextColor":
                    textItem.TextColor = e.Foreground;
                    break;
                case "TextAlignment":
                    textItem.TextAlignment = e.TextAlignment;
                    break;
                case "Text":
                    textItem.Text = e.Text ?? textItem.Text;
                    break;
            }
        }

        private FontControlsTextAdapter CreateFontControlsTextAdapter(SimpleTextItem textItem)
        {
            return new FontControlsTextAdapter(textItem);
        }

        public void Dispose()
        {
            if (_canvas != null)
            {
                _canvas.ItemSelected -= Canvas_ItemSelected;
                _canvas.SelectionCleared -= Canvas_SelectionCleared;
            }

            if (_fontPanel != null)
            {
                _fontPanel.FontChanged -= FontPanel_FontChanged;
            }
        }
    }

    /// <summary>
    /// Adapter class that makes SimpleTextItem look like the text objects that FontControlsPanel expects
    /// </summary>
    public class FontControlsTextAdapter
    {
        private SimpleTextItem _textItem;

        public FontControlsTextAdapter(SimpleTextItem textItem)
        {
            _textItem = textItem;
        }

        // Properties that FontControlsPanel will read
        public FontFamily FontFamily => _textItem.FontFamily;
        public double FontSize => _textItem.FontSize;
        public FontWeight FontWeight => _textItem.FontWeight;
        public FontStyle FontStyle => _textItem.FontStyle;
        public Brush Foreground => _textItem.TextColor;
        public TextAlignment TextAlignment => _textItem.TextAlignment;
        public string Text => _textItem.Text;

        // Property setters that FontControlsPanel might use
        public void SetFontFamily(FontFamily fontFamily)
        {
            _textItem.FontFamily = fontFamily;
        }

        public void SetFontSize(double fontSize)
        {
            _textItem.FontSize = fontSize;
        }

        public void SetFontWeight(FontWeight fontWeight)
        {
            _textItem.FontWeight = fontWeight;
        }

        public void SetFontStyle(FontStyle fontStyle)
        {
            _textItem.FontStyle = fontStyle;
        }

        public void SetForeground(Brush foreground)
        {
            _textItem.TextColor = foreground;
        }

        public void SetTextAlignment(TextAlignment alignment)
        {
            _textItem.TextAlignment = alignment;
        }

        public void SetText(string text)
        {
            _textItem.Text = text;
        }
    }

    /// <summary>
    /// Event args for font changes - matching what FontControlsPanel expects
    /// </summary>
    public class FontChangedEventArgs : EventArgs
    {
        public string PropertyName { get; set; }
        public FontFamily FontFamily { get; set; }
        public double FontSize { get; set; }
        public FontWeight FontWeight { get; set; }
        public FontStyle FontStyle { get; set; }
        public Brush Foreground { get; set; }
        public TextAlignment TextAlignment { get; set; }
        public string Text { get; set; }
    }
}