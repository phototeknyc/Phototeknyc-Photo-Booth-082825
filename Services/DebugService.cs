using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Photobooth.Services
{
    public class DebugService : INotifyPropertyChanged
    {
        private static DebugService _instance;
        public static DebugService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DebugService();
                }
                return _instance;
            }
        }

        private bool _isDebugEnabled = false;
        public bool IsDebugEnabled
        {
            get => _isDebugEnabled;
            set
            {
                if (_isDebugEnabled != value)
                {
                    _isDebugEnabled = value;
                    // Also update DesignerCanvas debug flag
                    DesignerCanvas.Controls.DesignerCanvasDebug.IsDebugEnabled = value;
                    OnPropertyChanged();
                    LogDebug($"Debug mode {(value ? "enabled" : "disabled")}");
                }
            }
        }

        public static void LogDebug(string message)
        {
            if (Instance.IsDebugEnabled)
            {
                System.Diagnostics.Debug.WriteLine(message);
            }
        }

        public static void LogDebug(string format, params object[] args)
        {
            if (Instance.IsDebugEnabled)
            {
                System.Diagnostics.Debug.WriteLine(string.Format(format, args));
            }
        }

        public static void LogError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: {message}");
        }

        public static void LogError(string format, params object[] args)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: {string.Format(format, args)}");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}