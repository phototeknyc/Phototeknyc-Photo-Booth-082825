using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Photobooth.Database;

namespace Photobooth.Services
{
    public class AITransformationUIService
    {
        #region Singleton

        private static AITransformationUIService _instance;
        private static readonly object _lock = new object();

        public static AITransformationUIService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AITransformationUIService();
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Private Fields

        private readonly AITemplateService _templateService;
        private readonly AITransformationService _transformationService;
        private ObservableCollection<AITemplateCategory> _categories;
        private ObservableCollection<AITransformationTemplate> _templates;
        private AITransformationTemplate _selectedTemplate;
        private bool _isProcessing;
        private CancellationTokenSource _cancellationTokenSource;

        #endregion

        #region Properties

        public ObservableCollection<AITemplateCategory> Categories
        {
            get
            {
                if (_categories == null)
                {
                    LoadCategories();
                }
                return _categories;
            }
        }

        public ObservableCollection<AITransformationTemplate> Templates
        {
            get => _templates ?? (_templates = new ObservableCollection<AITransformationTemplate>());
            private set => _templates = value;
        }

        public AITransformationTemplate SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                _selectedTemplate = value;
                SelectedTemplateChanged?.Invoke(this, new AITemplateSelectedEventArgs { Template = value });
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                _isProcessing = value;
                ProcessingStateChanged?.Invoke(this, new ProcessingStateEventArgs { IsProcessing = value });
            }
        }

        public bool IsEnabled => true; // TODO: Change to Properties.Settings.Default.EnableAITransformation after rebuild

        #endregion

        #region Events

        public event EventHandler<AITemplateSelectedEventArgs> SelectedTemplateChanged;
        public event EventHandler<ProcessingStateEventArgs> ProcessingStateChanged;
        public event EventHandler<TransformationUICompletedEventArgs> TransformationCompleted;
        public event EventHandler<TransformationUIProgressEventArgs> TransformationProgress;
        public event EventHandler<TransformationUIErrorEventArgs> TransformationError;

        #endregion

        #region Constructor

        private AITransformationUIService()
        {
            _templateService = AITemplateService.Instance;
            _transformationService = AITransformationService.Instance;
            _categories = new ObservableCollection<AITemplateCategory>();
            _templates = new ObservableCollection<AITransformationTemplate>();

            // Subscribe to transformation service events
            _transformationService.TransformationProgress += OnTransformationProgress;
            _transformationService.TransformationCompleted += OnTransformationCompleted;
            _transformationService.TransformationError += OnTransformationError;

            // Initialize if enabled
            if (IsEnabled)
            {
                Task.Run(() => InitializeAsync());
            }
        }

        #endregion

        #region Initialization

        public async Task<bool> InitializeAsync()
        {
            try
            {
                if (!IsEnabled)
                {
                    Debug.WriteLine("[AITransformationUI] AI Transformation is disabled");
                    return false;
                }

                // Initialize transformation service
                bool initialized = await _transformationService.InitializeAsync();
                if (!initialized)
                {
                    Debug.WriteLine("[AITransformationUI] Failed to initialize transformation service");
                    return false;
                }

                // Load categories and templates
                LoadCategories();
                LoadAllTemplates();

                Debug.WriteLine("[AITransformationUI] Service initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationUI] Initialization failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Category Management

        public void LoadCategories()
        {
            try
            {
                var categories = _templateService.GetCategories();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _categories.Clear();
                    foreach (var category in categories)
                    {
                        _categories.Add(category);
                    }
                });

                Debug.WriteLine($"[AITransformationUI] Loaded {categories.Count} categories");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationUI] Error loading categories: {ex.Message}");
            }
        }

        public void SelectCategory(AITemplateCategory category)
        {
            if (category == null)
            {
                LoadAllTemplates();
            }
            else
            {
                LoadTemplatesByCategory(category.Id);
            }
        }

        #endregion

        #region Template Management

        public void LoadAllTemplates()
        {
            try
            {
                var templates = _templateService.GetTemplates();
                UpdateTemplatesCollection(templates);
                Debug.WriteLine($"[AITransformationUI] Loaded {templates.Count} templates");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationUI] Error loading templates: {ex.Message}");
            }
        }

        public void LoadTemplatesByCategory(int categoryId)
        {
            try
            {
                var templates = _templateService.GetTemplates(categoryId);
                UpdateTemplatesCollection(templates);
                Debug.WriteLine($"[AITransformationUI] Loaded {templates.Count} templates for category {categoryId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationUI] Error loading templates: {ex.Message}");
            }
        }

        public void LoadPopularTemplates(int count = 10)
        {
            try
            {
                var templates = _templateService.GetPopularTemplates(count);
                UpdateTemplatesCollection(templates);
                Debug.WriteLine($"[AITransformationUI] Loaded {templates.Count} popular templates");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationUI] Error loading popular templates: {ex.Message}");
            }
        }

        private void UpdateTemplatesCollection(List<AITransformationTemplate> templates)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Templates.Clear();
                foreach (var template in templates)
                {
                    Templates.Add(template);
                }
            });
        }

        #endregion

        #region Transformation Processing

        public async Task<string> ApplyTransformationAsync(
            string inputImagePath,
            AITransformationTemplate template = null,
            IProgress<int> progress = null)
        {
            if (IsProcessing)
            {
                Debug.WriteLine("[AITransformationUI] Already processing a transformation");
                return null;
            }

            template = template ?? SelectedTemplate;
            if (template == null)
            {
                Debug.WriteLine("[AITransformationUI] No template selected");
                TransformationError?.Invoke(this, new TransformationUIErrorEventArgs
                {
                    Error = "Please select a transformation template"
                });
                return null;
            }

            try
            {
                IsProcessing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                var result = await _templateService.ApplyTransformationAsync(
                    inputImagePath,
                    template,
                    progress);

                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[AITransformationUI] Transformation cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationUI] Transformation failed: {ex.Message}");
                TransformationError?.Invoke(this, new TransformationUIErrorEventArgs
                {
                    Error = ex.Message
                });
                return null;
            }
            finally
            {
                IsProcessing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public async Task<List<string>> ApplyBatchTransformationAsync(
            List<string> inputImagePaths,
            AITransformationTemplate template = null,
            IProgress<int> progress = null)
        {
            if (IsProcessing)
            {
                Debug.WriteLine("[AITransformationUI] Already processing a transformation");
                return new List<string>();
            }

            template = template ?? SelectedTemplate;
            if (template == null)
            {
                Debug.WriteLine("[AITransformationUI] No template selected");
                TransformationError?.Invoke(this, new TransformationUIErrorEventArgs
                {
                    Error = "Please select a transformation template"
                });
                return new List<string>();
            }

            try
            {
                IsProcessing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                var results = await _templateService.BatchApplyTransformationAsync(
                    inputImagePaths,
                    template,
                    progress);

                return results;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[AITransformationUI] Batch transformation cancelled");
                return new List<string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AITransformationUI] Batch transformation failed: {ex.Message}");
                TransformationError?.Invoke(this, new TransformationUIErrorEventArgs
                {
                    Error = ex.Message
                });
                return new List<string>();
            }
            finally
            {
                IsProcessing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public void CancelTransformation()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                Debug.WriteLine("[AITransformationUI] Transformation cancelled by user");
            }
        }

        #endregion

        #region UI Helper Methods

        public Panel CreateTemplateSelectorPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10)
            };

            // Category selector
            var categoryCombo = new ComboBox
            {
                ItemsSource = Categories,
                DisplayMemberPath = "Name",
                Margin = new Thickness(0, 0, 0, 10)
            };
            categoryCombo.SelectionChanged += (s, e) =>
            {
                var selected = categoryCombo.SelectedItem as AITemplateCategory;
                SelectCategory(selected);
            };
            panel.Children.Add(categoryCombo);

            // Template list
            var templateList = new ListBox
            {
                ItemsSource = Templates,
                Height = 300
            };

            // Create item template
            var itemTemplate = new DataTemplate(typeof(AITransformationTemplate));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.BorderBrushProperty, Brushes.LightGray);
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            factory.SetValue(Border.MarginProperty, new Thickness(2));
            factory.SetValue(Border.PaddingProperty, new Thickness(5));

            var stackPanel = new FrameworkElementFactory(typeof(StackPanel));
            stackPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            textBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);

            stackPanel.AppendChild(textBlock);
            factory.AppendChild(stackPanel);
            itemTemplate.VisualTree = factory;

            templateList.ItemTemplate = itemTemplate;
            templateList.SelectionChanged += (s, e) =>
            {
                SelectedTemplate = templateList.SelectedItem as AITransformationTemplate;
            };

            panel.Children.Add(templateList);

            return panel;
        }

        public Panel CreateProgressPanel()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10),
                VerticalAlignment = VerticalAlignment.Center
            };

            var progressBar = new ProgressBar
            {
                Height = 20,
                Minimum = 0,
                Maximum = 100,
                IsIndeterminate = true,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var statusText = new TextBlock
            {
                Text = "Processing transformation...",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 14
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            cancelButton.Click += (s, e) => CancelTransformation();

            panel.Children.Add(progressBar);
            panel.Children.Add(statusText);
            panel.Children.Add(cancelButton);

            // Update based on processing state
            ProcessingStateChanged += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    panel.Visibility = e.IsProcessing ? Visibility.Visible : Visibility.Collapsed;
                });
            };

            // Update progress
            TransformationProgress += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (e.Progress > 0)
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = e.Progress;
                    }
                    statusText.Text = e.Status ?? "Processing...";
                });
            };

            return panel;
        }

        #endregion

        #region Event Handlers

        private void OnTransformationProgress(object sender, TransformationProgressEventArgs e)
        {
            TransformationProgress?.Invoke(this, new TransformationUIProgressEventArgs
            {
                Status = e.Status,
                Progress = e.Progress
            });
        }

        private void OnTransformationCompleted(object sender, TransformationCompletedEventArgs e)
        {
            TransformationCompleted?.Invoke(this, new TransformationUICompletedEventArgs
            {
                InputPath = e.InputPath,
                OutputPath = e.OutputPath,
                Template = e.Template
            });
        }

        private void OnTransformationError(object sender, TransformationErrorEventArgs e)
        {
            TransformationError?.Invoke(this, new TransformationUIErrorEventArgs
            {
                Error = e.Error,
                InputPath = e.InputPath
            });
        }

        #endregion
    }

    #region Event Args

    public class AITemplateSelectedEventArgs : EventArgs
    {
        public AITransformationTemplate Template { get; set; }
    }

    public class ProcessingStateEventArgs : EventArgs
    {
        public bool IsProcessing { get; set; }
    }

    public class TransformationUIProgressEventArgs : EventArgs
    {
        public string Status { get; set; }
        public int Progress { get; set; }
    }

    public class TransformationUICompletedEventArgs : EventArgs
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public AITransformationTemplate Template { get; set; }
    }

    public class TransformationUIErrorEventArgs : EventArgs
    {
        public string Error { get; set; }
        public string InputPath { get; set; }
    }

    #endregion
}