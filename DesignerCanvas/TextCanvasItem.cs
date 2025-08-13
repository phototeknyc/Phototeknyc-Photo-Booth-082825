using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DesignerCanvas
{
    public class TextCanvasItem : IBoxCanvasItem, INotifyPropertyChanged
    {
        #region Private Fields
        private double _left;
        private double _top;
        private double _width = 100;
        private double _height = 30;
        private double _angle;
        private bool _lockedPosition;
        private bool _lockedAspectRatio;
        private bool _resizeable = false;
        private string _text = "Text";
        private string _fontFamily = "Arial";
        private double _fontSize = 16;
        private FontWeight _fontWeight = FontWeights.Normal;
        private FontStyle _fontStyle = FontStyles.Normal;
        private TextDecorationCollection _textDecorations;
        private Brush _foreground = Brushes.Black;
        private TextAlignment _textAlignment = TextAlignment.Left;
        private bool _isEditing;
        
        // Text Effects
        private bool _hasShadow;
        private Color _shadowColor = Colors.Gray;
        private double _shadowOffsetX = 2;
        private double _shadowOffsetY = 2;
        private double _shadowBlurRadius = 4;
        
        private bool _hasOutline;
        private Brush _outlineColor = Brushes.Black;
        private double _outlineThickness = 1;
        
        private double _letterSpacing;
        private double _lineHeight = 1.2;
        private bool _suppressAutoSize = false;
        #endregion

        #region IBoxCanvasItem Properties
        public double Left
        {
            get => _left;
            set
            {
                _left = value;
                OnPropertyChanged();
                OnBoundsChanged();
            }
        }

        public double Top
        {
            get => _top;
            set
            {
                _top = value;
                OnPropertyChanged();
                OnBoundsChanged();
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                if (value > 0)
                {
                    _width = value;
                    OnPropertyChanged();
                    OnBoundsChanged();
                }
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (value > 0)
                {
                    _height = value;
                    OnPropertyChanged();
                    OnBoundsChanged();
                }
            }
        }

        public double Angle
        {
            get => _angle;
            set
            {
                _angle = value;
                OnPropertyChanged();
            }
        }

        public bool LockedPosition
        {
            get => _lockedPosition;
            set
            {
                _lockedPosition = value;
                OnPropertyChanged();
            }
        }

        public bool LockedAspectRatio
        {
            get => _lockedAspectRatio;
            set
            {
                _lockedAspectRatio = value;
                OnPropertyChanged();
            }
        }

        public bool Resizeable
        {
            get => _resizeable;
            set
            {
                _resizeable = value;
                OnPropertyChanged();
            }
        }

        public Rect Bounds => new Rect(Left, Top, Width, Height);

        public double AspectRatio
        {
            get => Width > 0 && Height > 0 ? Width / Height : 1.0;
            set
            {
                if (value > 0 && !LockedAspectRatio)
                {
                    Height = Width / value;
                }
            }
        }

        public event EventHandler BoundsChanged;

        protected virtual void OnBoundsChanged()
        {
            BoundsChanged?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Text Properties
        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                OnTextPropertyChanged();
            }
        }

        public string FontFamily
        {
            get => _fontFamily;
            set
            {
                _fontFamily = value;
                OnTextPropertyChanged();
            }
        }

        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (value > 0)
                {
                    _fontSize = value;
                    OnTextPropertyChanged();
                }
            }
        }

        public FontWeight FontWeight
        {
            get => _fontWeight;
            set
            {
                _fontWeight = value;
                OnTextPropertyChanged();
            }
        }

        public FontStyle FontStyle
        {
            get => _fontStyle;
            set
            {
                _fontStyle = value;
                OnTextPropertyChanged();
            }
        }

        public TextDecorationCollection TextDecorations
        {
            get => _textDecorations;
            set
            {
                _textDecorations = value;
                OnPropertyChanged();
            }
        }

        public Brush Foreground
        {
            get => _foreground;
            set
            {
                _foreground = value;
                OnPropertyChanged();
            }
        }

        public TextAlignment TextAlignment
        {
            get => _textAlignment;
            set
            {
                _textAlignment = value;
                OnPropertyChanged();
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                _isEditing = value;
                OnPropertyChanged();
            }
        }

        public double LetterSpacing
        {
            get => _letterSpacing;
            set
            {
                _letterSpacing = value;
                OnPropertyChanged();
            }
        }

        public double LineHeight
        {
            get => _lineHeight;
            set
            {
                if (value > 0)
                {
                    _lineHeight = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Text Effects Properties
        public bool HasShadow
        {
            get => _hasShadow;
            set
            {
                _hasShadow = value;
                OnTextPropertyChanged();
            }
        }

        public Color ShadowColor
        {
            get => _shadowColor;
            set
            {
                _shadowColor = value;
                OnPropertyChanged();
            }
        }

        public double ShadowOffsetX
        {
            get => _shadowOffsetX;
            set
            {
                _shadowOffsetX = value;
                OnPropertyChanged();
            }
        }

        public double ShadowOffsetY
        {
            get => _shadowOffsetY;
            set
            {
                _shadowOffsetY = value;
                OnPropertyChanged();
            }
        }

        public double ShadowBlurRadius
        {
            get => _shadowBlurRadius;
            set
            {
                if (value >= 0)
                {
                    _shadowBlurRadius = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasOutline
        {
            get => _hasOutline;
            set
            {
                _hasOutline = value;
                OnTextPropertyChanged();
            }
        }

        public Brush OutlineColor
        {
            get => _outlineColor;
            set
            {
                _outlineColor = value;
                OnPropertyChanged();
            }
        }

        public double OutlineThickness
        {
            get => _outlineThickness;
            set
            {
                if (value >= 0)
                {
                    _outlineThickness = value;
                    OnTextPropertyChanged();
                }
            }
        }
        #endregion

        #region Auto-Sizing Control
        /// <summary>
        /// Gets or sets whether to suppress automatic resizing when text properties change.
        /// Used when loading from database to preserve saved dimensions.
        /// </summary>
        public bool SuppressAutoSize
        {
            get => _suppressAutoSize;
            set => _suppressAutoSize = value;
        }
        #endregion

        #region Convenience Properties
        public bool IsBold
        {
            get => FontWeight == FontWeights.Bold;
            set
            {
                FontWeight = value ? FontWeights.Bold : FontWeights.Normal;
                OnPropertyChanged();
            }
        }

        public bool IsItalic
        {
            get => FontStyle == FontStyles.Italic;
            set
            {
                FontStyle = value ? FontStyles.Italic : FontStyles.Normal;
                OnPropertyChanged();
            }
        }

        public bool IsUnderlined
        {
            get => TextDecorations?.Contains(System.Windows.TextDecorations.Underline[0]) == true;
            set
            {
                if (value)
                {
                    TextDecorations = System.Windows.TextDecorations.Underline;
                }
                else
                {
                    TextDecorations = null;
                }
                OnPropertyChanged();
            }
        }
        #endregion

        #region Constructors
        public TextCanvasItem()
        {
            UpdateSizeToFitText();
        }

        public TextCanvasItem(double left, double top, string text = "Text")
        {
            Left = left;
            Top = top;
            Text = text;
            // Text setter will call UpdateSizeToFitText()
        }

        public TextCanvasItem(double left, double top, double width, double height, string text = "Text")
        {
            Left = left;
            Top = top;
            // Set size first, then text (for cases where manual sizing is desired)
            _width = width;
            _height = height;
            Text = text;
        }
        #endregion

        #region ICanvasItem Methods
        public HitTestResult HitTest(Point point)
        {
            return Bounds.Contains(point) ? HitTestResult.Contains : HitTestResult.None;
        }

        public HitTestResult HitTest(Rect region)
        {
            if (region.Contains(Bounds))
                return HitTestResult.Contains;
            else if (region.IntersectsWith(Bounds))
                return HitTestResult.Intersects;
            else
                return HitTestResult.None;
        }

        public void NotifyUserDragging(double deltaX, double deltaY)
        {
            // Called during drag operations
        }

        public void NotifyUserDraggingStarted()
        {
            // Called when drag starts
        }

        public void NotifyUserDraggingCompleted()
        {
            // Called when drag completes
        }

        public ICanvasItem Clone()
        {
            return new TextCanvasItem(Left + 10, Top + 10, Width, Height, Text)
            {
                FontFamily = this.FontFamily,
                FontSize = this.FontSize,
                FontWeight = this.FontWeight,
                FontStyle = this.FontStyle,
                Foreground = this.Foreground,
                TextAlignment = this.TextAlignment,
                TextDecorations = this.TextDecorations,
                LetterSpacing = this.LetterSpacing,
                LineHeight = this.LineHeight,
                HasShadow = this.HasShadow,
                ShadowColor = this.ShadowColor,
                ShadowOffsetX = this.ShadowOffsetX,
                ShadowOffsetY = this.ShadowOffsetY,
                ShadowBlurRadius = this.ShadowBlurRadius,
                HasOutline = this.HasOutline,
                OutlineColor = this.OutlineColor,
                OutlineThickness = this.OutlineThickness,
                Angle = this.Angle,
                LockedPosition = this.LockedPosition,
                LockedAspectRatio = this.LockedAspectRatio,
                Resizeable = this.Resizeable
            };
        }
        #endregion

        #region Text Editing Methods
        /// <summary>
        /// Enters text editing mode
        /// </summary>
        public void StartEditing()
        {
            IsEditing = true;
        }

        /// <summary>
        /// Exits text editing mode
        /// </summary>
        public void StopEditing()
        {
            IsEditing = false;
            // Update size after editing is complete
            UpdateSizeToFitText();
        }

        /// <summary>
        /// Toggles between editing and display mode
        /// </summary>
        public void ToggleEditing()
        {
            if (IsEditing)
                StopEditing();
            else
                StartEditing();
        }
        #endregion

        #region Text Measurement and Auto-sizing
        /// <summary>
        /// Measures the text and updates the Width/Height properties to fit the content
        /// </summary>
        public void UpdateSizeToFitText()
        {
            if (string.IsNullOrEmpty(Text))
            {
                Width = 50;
                Height = 20;
                return;
            }

            try
            {
                var typeface = new Typeface(new FontFamily(FontFamily), FontStyle, FontWeight, FontStretches.Normal);
                var formattedText = new FormattedText(
                    Text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    FontSize,
                    Brushes.Black,
                    new NumberSubstitution(),
                    TextFormattingMode.Display);

                // Add some padding to account for effects
                var padding = 10.0;
                if (HasOutline)
                    padding += OutlineThickness * 2;
                if (HasShadow)
                    padding += Math.Max(Math.Abs(ShadowOffsetX), Math.Abs(ShadowOffsetY)) + ShadowBlurRadius;

                // Update size with measured text dimensions plus padding
                Width = Math.Max(formattedText.Width + padding, 20);
                Height = Math.Max(formattedText.Height + padding, 20);
            }
            catch
            {
                // Fallback if measurement fails
                Width = Math.Max(Text.Length * FontSize * 0.6, 50);
                Height = Math.Max(FontSize * LineHeight + 10, 20);
            }
        }

        /// <summary>
        /// Updates size when text properties change
        /// </summary>
        private void OnTextPropertyChanged([CallerMemberName] string propertyName = null)
        {
            OnPropertyChanged(propertyName);
            if (!_suppressAutoSize)
            {
                UpdateSizeToFitText();
            }
        }
        #endregion

        #region Commands for Text Editing
        private ICommand _stopEditingCommand;
        private ICommand _cancelEditingCommand;

        public ICommand StopEditingCommand
        {
            get
            {
                return _stopEditingCommand ?? (_stopEditingCommand = new TextEditCommand(
                    param => StopEditing(),
                    param => IsEditing));
            }
        }

        public ICommand CancelEditingCommand
        {
            get
            {
                return _cancelEditingCommand ?? (_cancelEditingCommand = new TextEditCommand(
                    param => CancelEditing(),
                    param => IsEditing));
            }
        }

        private void CancelEditing()
        {
            // Could restore original text here if needed
            IsEditing = false;
        }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    /// <summary>
    /// Simple command implementation for TextCanvasItem text editing commands
    /// </summary>
    public class TextEditCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public TextEditCommand(Action<object> execute) : this(execute, null) { }

        public TextEditCommand(Action<object> execute, Predicate<object> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}