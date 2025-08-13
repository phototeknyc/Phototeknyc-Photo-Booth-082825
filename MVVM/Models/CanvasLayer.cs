using DesignerCanvas;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Photobooth.MVVM.Models
{
    public class CanvasLayer : INotifyPropertyChanged
    {
        private bool _isVisible = true;
        private bool _isSelected;
        private bool _isLocked = false;
        
        public ICanvasItem CanvasItem { get; set; }
        
        public string LayerName
        {
            get
            {
                if (CanvasItem is ImageCanvasItem) return "Image Layer";
                if (CanvasItem is PlaceholderCanvasItem placeholder) return $"Placeholder {placeholder.PlaceholderNo}";
                if (CanvasItem is TextCanvasItem textItem) return string.IsNullOrEmpty(textItem.Text) ? "Text Layer" : $"Text: {textItem.Text.Substring(0, Math.Min(textItem.Text.Length, 20))}";
                return "Layer";
            }
        }
        
        public string LayerInfo
        {
            get
            {
                if (CanvasItem is IBoxCanvasItem item)
                {
                    return $"{item.Width:F0} × {item.Height:F0}";
                }
                return "";
            }
        }
        
        public string LayerTypeIcon
        {
            get
            {
                if (CanvasItem is ImageCanvasItem) return "🖼";
                if (CanvasItem is PlaceholderCanvasItem) return "📷";
                if (CanvasItem is TextCanvasItem) return "📝";
                return "■";
            }
        }
        
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
        
        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                _isLocked = value;
                OnPropertyChanged();
            }
        }
        
        public int ZIndex { get; set; }
        
        public CanvasLayer(ICanvasItem canvasItem)
        {
            CanvasItem = canvasItem;
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}