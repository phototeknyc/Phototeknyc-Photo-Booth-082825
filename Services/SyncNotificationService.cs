using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Documents;

namespace Photobooth.Services
{
    /// <summary>
    /// Service for displaying sync notifications that auto-dismiss
    /// </summary>
    public class SyncNotificationService
    {
        private static SyncNotificationService _instance;
        public static SyncNotificationService Instance => _instance ?? (_instance = new SyncNotificationService());

        private readonly Queue<NotificationData> _notificationQueue = new Queue<NotificationData>();
        private Border _currentNotification;
        private DispatcherTimer _dismissTimer;
        private bool _isShowing;

        // Default timeout in seconds
        private const int DEFAULT_TIMEOUT = 5;

        private SyncNotificationService()
        {
            // Subscribe to sync events
            PhotoBoothSyncService.Instance.TemplateUpdating += OnTemplateUpdating;
            PhotoBoothSyncService.Instance.SettingsUpdating += OnSettingsUpdating;
            PhotoBoothSyncService.Instance.EventUpdating += OnEventUpdating;
            PhotoBoothSyncService.Instance.SyncStarted += OnSyncStarted;
            PhotoBoothSyncService.Instance.SyncCompleted += OnSyncCompleted;
            PhotoBoothSyncService.Instance.SyncError += OnSyncError;
        }

        /// <summary>
        /// Initialize the notification service with the main window
        /// </summary>
        public void Initialize()
        {
            // Service is initialized through singleton access
            Debug.WriteLine("SyncNotificationService: Initialized");
        }

        /// <summary>
        /// Find or create a notification layer that sits above all content
        /// </summary>
        private Grid FindOrCreateNotificationLayer(Window window)
        {
            // Try to find existing notification layer
            var adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(window.Content as Visual);
            if (adornerLayer != null)
            {
                // Look for existing notification adorner
                var adorners = adornerLayer.GetAdorners(window.Content as UIElement);
                if (adorners != null)
                {
                    foreach (var adorner in adorners)
                    {
                        if (adorner is NotificationAdorner notifAdorner)
                        {
                            return notifAdorner.NotificationGrid;
                        }
                    }
                }

                // Create new notification adorner
                var newAdorner = new NotificationAdorner(window.Content as UIElement);
                adornerLayer.Add(newAdorner);
                return newAdorner.NotificationGrid;
            }

            // Fallback: Create overlay on top of existing content
            if (window.Content is Grid existingGrid)
            {
                // Check if we already have a notification layer
                foreach (var child in existingGrid.Children)
                {
                    if (child is Grid g && g.Name == "NotificationLayer")
                    {
                        return g;
                    }
                }

                // Create new notification layer
                var notificationGrid = new Grid
                {
                    Name = "NotificationLayer",
                    IsHitTestVisible = false,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Set to span all rows and columns
                Grid.SetRowSpan(notificationGrid, Math.Max(1, existingGrid.RowDefinitions.Count));
                Grid.SetColumnSpan(notificationGrid, Math.Max(1, existingGrid.ColumnDefinitions.Count));
                Panel.SetZIndex(notificationGrid, int.MaxValue);

                existingGrid.Children.Add(notificationGrid);
                return notificationGrid;
            }

            // Last resort: wrap existing content
            var wrapper = new Grid();
            var content = window.Content as UIElement;
            window.Content = null;

            wrapper.Children.Add(content);

            var notifLayer = new Grid
            {
                Name = "NotificationLayer",
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Panel.SetZIndex(notifLayer, int.MaxValue);
            wrapper.Children.Add(notifLayer);

            window.Content = wrapper;
            return notifLayer;
        }

        /// <summary>
        /// Show a notification that auto-dismisses
        /// </summary>
        public void ShowNotification(string message, NotificationType type = NotificationType.Info, int timeoutSeconds = DEFAULT_TIMEOUT)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var notification = new NotificationData
                {
                    Message = message,
                    Type = type,
                    TimeoutSeconds = timeoutSeconds
                };

                _notificationQueue.Enqueue(notification);

                if (!_isShowing)
                {
                    ShowNextNotification();
                }
            });
        }

        private void ShowNextNotification()
        {
            if (_notificationQueue.Count == 0)
            {
                _isShowing = false;
                return;
            }

            _isShowing = true;
            var notification = _notificationQueue.Dequeue();

            // Find the main window
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null) return;

            // Get the root element (could be Grid, Panel, etc.)
            var rootElement = mainWindow.Content as FrameworkElement;
            if (rootElement == null) return;

            // Create or find the notification layer
            Grid notificationLayer = FindOrCreateNotificationLayer(mainWindow);
            if (notificationLayer == null) return;

            // Create notification UI
            _currentNotification = CreateNotificationUI(notification);

            // Position at the top of the notification layer
            Grid.SetRow(_currentNotification, 0);
            Grid.SetColumn(_currentNotification, 0);

            // Add to notification layer
            notificationLayer.Children.Add(_currentNotification);

            // Animate in
            AnimateIn(_currentNotification);

            // Setup auto-dismiss timer
            _dismissTimer?.Stop();
            _dismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(notification.TimeoutSeconds)
            };
            _dismissTimer.Tick += (s, e) =>
            {
                _dismissTimer.Stop();
                DismissCurrentNotification();
            };
            _dismissTimer.Start();
        }

        private Border CreateNotificationUI(NotificationData notification)
        {
            SolidColorBrush backgroundColor;
            switch (notification.Type)
            {
                case NotificationType.Success:
                    backgroundColor = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                    break;
                case NotificationType.Warning:
                    backgroundColor = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                    break;
                case NotificationType.Error:
                    backgroundColor = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                    break;
                default:
                    backgroundColor = new SolidColorBrush(Color.FromRgb(0, 123, 255));
                    break;
            }

            string icon;
            switch (notification.Type)
            {
                case NotificationType.Success:
                    icon = "✓";
                    break;
                case NotificationType.Warning:
                    icon = "⚠";
                    break;
                case NotificationType.Error:
                    icon = "✕";
                    break;
                default:
                    icon = "ℹ";
                    break;
            }

            var border = new Border
            {
                Background = backgroundColor,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(20, 20, 20, 0),
                Padding = new Thickness(20, 15, 20, 15),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                MaxWidth = 600,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TranslateTransform(0, -20),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    ShadowDepth = 2,
                    BlurRadius = 10,
                    Opacity = 0.3
                }
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var iconText = new TextBlock
            {
                Text = icon,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 15, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var messageText = new TextBlock
            {
                Text = notification.Message,
                FontSize = 16,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(iconText);
            panel.Children.Add(messageText);
            border.Child = panel;

            return border;
        }

        private void AnimateIn(Border notification)
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var slideDown = new DoubleAnimation(-20, 0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            notification.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            (notification.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }

        private void AnimateOut(Border notification, Action onComplete)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var slideUp = new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, e) => onComplete?.Invoke();

            notification.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            (notification.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void DismissCurrentNotification()
        {
            if (_currentNotification == null) return;

            AnimateOut(_currentNotification, () =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    // Remove from parent
                    if (_currentNotification.Parent is Grid grid)
                    {
                        grid.Children.Remove(_currentNotification);
                    }

                    _currentNotification = null;

                    // Show next notification if any
                    ShowNextNotification();
                });
            });
        }

        #region Event Handlers

        private void OnTemplateUpdating(object sender, TemplateUpdateEventArgs e)
        {
            ShowNotification(
                e.Message ?? $"Template '{e.TemplateName}' is being updated from cloud sync",
                NotificationType.Warning,
                3
            );
        }

        private void OnSettingsUpdating(object sender, SettingsUpdateEventArgs e)
        {
            ShowNotification(
                e.Message ?? "Settings are being updated from cloud sync",
                NotificationType.Warning,
                3
            );
        }

        private void OnEventUpdating(object sender, EventUpdateEventArgs e)
        {
            ShowNotification(
                e.Message ?? $"Event '{e.EventName}' is being updated from cloud sync",
                NotificationType.Warning,
                3
            );
        }

        private void OnSyncStarted(object sender, SyncEventArgs e)
        {
            ShowNotification(
                "Cloud sync in progress...",
                NotificationType.Info,
                2
            );
        }

        private void OnSyncCompleted(object sender, SyncEventArgs e)
        {
            if (e.Result?.Success == true)
            {
                var message = $"Sync completed: {e.Result.TemplatesSynced} templates, {e.Result.EventsSynced} events synced";
                ShowNotification(message, NotificationType.Success, 3);
            }
        }

        private void OnSyncError(object sender, SyncErrorEventArgs e)
        {
            ShowNotification(
                $"Sync error: {e.Message}",
                NotificationType.Error,
                5
            );
        }

        #endregion

        private class NotificationData
        {
            public string Message { get; set; }
            public NotificationType Type { get; set; }
            public int TimeoutSeconds { get; set; }
        }

        public enum NotificationType
        {
            Info,
            Success,
            Warning,
            Error
        }

        /// <summary>
        /// Custom adorner that hosts notifications above all other content
        /// </summary>
        private class NotificationAdorner : Adorner
        {
            private readonly Grid _notificationGrid;

            public Grid NotificationGrid => _notificationGrid;

            public NotificationAdorner(UIElement adornedElement) : base(adornedElement)
            {
                _notificationGrid = new Grid
                {
                    IsHitTestVisible = false,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Create a visual child
                AddVisualChild(_notificationGrid);
            }

            protected override int VisualChildrenCount => 1;

            protected override Visual GetVisualChild(int index)
            {
                if (index != 0) throw new ArgumentOutOfRangeException();
                return _notificationGrid;
            }

            protected override Size MeasureOverride(Size constraint)
            {
                _notificationGrid.Measure(constraint);
                return constraint;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                _notificationGrid.Arrange(new Rect(finalSize));
                return finalSize;
            }
        }
    }
}