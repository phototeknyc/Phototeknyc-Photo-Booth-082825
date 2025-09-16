using DesignerCanvas;
using Microsoft.Win32;
using Photobooth.MVVM.Models;
using Photobooth.Models;
using Photobooth.Services;
using Photobooth.Database;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace Photobooth.MVVM.ViewModels.Designer
{
	public class DesignerVM : BaseViewModel
	{
		#region properties
		private bool _isBoundChanged;
		
		// Event for canvas size changes
		public event EventHandler CanvasSizeChanged;
		public event EventHandler BrowseTemplatesRequested;
		private const string ImgLockPath = "/images/lock.png";
		private const string ImgUnLockPath = "/images/Padlock.png";
		public List<string> ListOfRatios { get; } = new List<string> { "2x6", "4x6", "5x7", "8x10" };
		
		// Paper size definitions with actual print dimensions at 300 DPI
		private static readonly Dictionary<string, (double WidthInches, double HeightInches, int PixelsAt300DPI_Width, int PixelsAt300DPI_Height)> PaperSizes = 
			new Dictionary<string, (double, double, int, int)>
		{
			{ "2x6", (2.0, 6.0, 600, 1800) },    // 2" x 6" strip
			{ "4x6", (4.0, 6.0, 1200, 1800) },   // 4" x 6" standard photo
			{ "5x7", (5.0, 7.0, 1500, 2100) },   // 5" x 7" photo
			{ "8x10", (8.0, 10.0, 2400, 3000) }, // 8" x 10" photo
		};
		
		private const double PrintDPI = 300.0; // Standard photo print DPI
		private const double DisplayPixelsPerInch = 72.0; // Good for screen display (not too large)
		
		// Services
		private int _lastTemplateId = -1;
		public int LastTemplateId => _lastTemplateId;
		
		// Undo/Redo System
		private readonly Stack<CanvasState> undoStack = new Stack<CanvasState>();
		private readonly Stack<CanvasState> redoStack = new Stack<CanvasState>();
		private const int MaxUndoLevels = 50;
		
		// Template Collections
		private ObservableCollection<TemplateData> _savedTemplates;
		public ObservableCollection<TemplateData> SavedTemplates
		{
			get => _savedTemplates;
			set => SetProperty(ref _savedTemplates, value);
		}
		
		private TemplateData _selectedTemplate;
		public TemplateData SelectedTemplate
		{
			get => _selectedTemplate;
			set => SetProperty(ref _selectedTemplate, value);
		}
		
		// Track the currently loaded template ID
		private int? _currentLoadedTemplateId;
		public int? CurrentLoadedTemplateId
		{
			get => _currentLoadedTemplateId;
			set => SetProperty(ref _currentLoadedTemplateId, value);
		}
		
		// Event Collections
		private ObservableCollection<EventData> _events;
		public ObservableCollection<EventData> Events
		{
			get => _events;
			set => SetProperty(ref _events, value);
		}
		
		private EventData _selectedEvent;
		public EventData SelectedEvent
		{
			get => _selectedEvent;
			set 
			{ 
				SetProperty(ref _selectedEvent, value);
				if (value != null)
				{
					LoadEventTemplates(value.Id);
					SaveLastEventId(value.Id);
					
					// Auto-load the default event template if one exists
					Task.Run(async () => await LoadDefaultEventTemplateIfExists(value.Id));
				}
			}
		}
		
		private ObservableCollection<TemplateData> _eventTemplates;
		public ObservableCollection<TemplateData> EventTemplates
		{
			get => _eventTemplates;
			set => SetProperty(ref _eventTemplates, value);
		}
		
		private TemplateData _selectedEventTemplate;
		public TemplateData SelectedEventTemplate
		{
			get => _selectedEventTemplate;
			set => SetProperty(ref _selectedEventTemplate, value);
		}

		private bool _isEnabledSelectedItemsPositionChange;
		public bool IsEnabledSelectedItemsPositionChange
		{
			get => _isEnabledSelectedItemsPositionChange;
			set
			{
				SetProperty(ref _isEnabledSelectedItemsPositionChange, value);
				if (value) UnLockItemsPosition();
				else LockItemsPosition();
			}
		}

		private bool _isEnabledSelectedItemsSizeChange;
		public bool IsEnabledSelectedItemsSizeChange
		{
			get => _isEnabledSelectedItemsSizeChange;
			set
			{
				SetProperty(ref _isEnabledSelectedItemsSizeChange, value);
				if (value) UnlockItemsSize();
				else LockItemsSize();
			}
		}

		private string _selectedElementsPositionX;
		public string SelectedElementsPositionX
		{
			get => _selectedElementsPositionX;
			set => SetElementProperty(ref _selectedElementsPositionX, value, nameof(SelectedElementsPositionX), delta => SetSelectedItemsPositionX(delta));
		}

		private string _selectedElementsPositionY;
		public string SelectedElementsPositionY
		{
			get => _selectedElementsPositionY;
			set => SetElementProperty(ref _selectedElementsPositionY, value, nameof(SelectedElementsPositionY), delta => SetSelectedItemsPositionY(delta));
		}

		private string _selectedElementsWidth;
		public string SelectedElementsWidth
		{
			get => _selectedElementsWidth;
			set => SetElementProperty(ref _selectedElementsWidth, value, nameof(SelectedElementsWidth), delta => SetSelectedItemsWidth(delta));
		}

		private string _selectedElementsHeight;
		public string SelectedElementsHeight
		{
			get => _selectedElementsHeight;
			set => SetElementProperty(ref _selectedElementsHeight, value, nameof(SelectedElementsHeight), delta => SetSelectedItemsHeight(delta));
		}

		// Rotation property
		private double _selectedElementsRotation;
		public double SelectedElementsRotation
		{
			get => _selectedElementsRotation;
			set
			{
				if (SetProperty(ref _selectedElementsRotation, value))
				{
					// Update rotation for all selected items using their Angle property
					foreach (var item in CustomDesignerCanvas.SelectedItems)
					{
						if (item is IBoxCanvasItem canvasItem)
						{
							canvasItem.Angle = value;
						}
					}
				}
			}
		}

		// Actual pixel dimensions (for print/export)
		private string _selectedElementsActualPixelWidth;
		public string SelectedElementsActualPixelWidth
		{
			get => _selectedElementsActualPixelWidth;
			set
			{
				if (SetProperty(ref _selectedElementsActualPixelWidth, value))
				{
					// Convert actual pixels to display size and update items
					if (int.TryParse(value, out int pixelWidth) && CustomDesignerCanvas != null && CustomDesignerCanvas.ActualPixelWidth > 0)
					{
						double displayScale = CustomDesignerCanvas.ActualWidth / CustomDesignerCanvas.ActualPixelWidth;
						double displayWidth = pixelWidth * displayScale;
						
						foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
						{
							item.Width = displayWidth;
						}
						
						// Update the display width property
						_selectedElementsWidth = displayWidth.ToString("F0");
						OnPropertyChanged(nameof(SelectedElementsWidth));
					}
				}
			}
		}

		private string _selectedElementsActualPixelHeight;
		public string SelectedElementsActualPixelHeight
		{
			get => _selectedElementsActualPixelHeight;
			set
			{
				if (SetProperty(ref _selectedElementsActualPixelHeight, value))
				{
					// Convert actual pixels to display size and update items
					if (int.TryParse(value, out int pixelHeight) && CustomDesignerCanvas != null && CustomDesignerCanvas.ActualPixelHeight > 0)
					{
						double displayScale = CustomDesignerCanvas.ActualHeight / CustomDesignerCanvas.ActualPixelHeight;
						double displayHeight = pixelHeight * displayScale;
						
						foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
						{
							item.Height = displayHeight;
						}
						
						// Update the display height property
						_selectedElementsHeight = displayHeight.ToString("F0");
						OnPropertyChanged(nameof(SelectedElementsHeight));
					}
				}
			}
		}

		private string _selectedItemsPositionChangeSource;
		public string SelectedItemsPositionChangeSource
		{
			get => _selectedItemsPositionChangeSource;
			set => SetProperty(ref _selectedItemsPositionChangeSource, value);
		}

		private string _selectedItemsSizeChangeSource;
		public string SelectedItemsSizeChangeSource
		{
			get => _selectedItemsSizeChangeSource;
			set => SetProperty(ref _selectedItemsSizeChangeSource, value);
		}

		private bool _isEnabledSelectedItemsAspectRatioChange;
		public bool IsEnabledSelectedItemsAspectRatioChange
		{
			get => _isEnabledSelectedItemsAspectRatioChange;
			set
			{
				SetProperty(ref _isEnabledSelectedItemsAspectRatioChange, value);
				if (value) UnlockItemsAspectRatio();
				else LockItemsAspectRatio();
			}
		}

		private string _selectedItemsAspectRatioLockSource;
		public string SelectedItemsAspectRatioLockSource
		{
			get => _selectedItemsAspectRatioLockSource;
			set => SetProperty(ref _selectedItemsAspectRatioLockSource, value);
		}

		private Brush _canvasBackgroundColor = Brushes.White;
		public Brush CanvasBackgroundColor
		{
			get => _canvasBackgroundColor;
			set 
			{
				if (SetProperty(ref _canvasBackgroundColor, value))
				{
					// Explicitly update the canvas background
					if (CustomDesignerCanvas != null)
					{
						CustomDesignerCanvas.Background = value;
					}
				}
			}
		}

		private PlaceholderCanvasItem _selectedPlaceholderItem;
		public PlaceholderCanvasItem SelectedPlaceholderItem
		{
			get => _selectedPlaceholderItem;
			set => SetProperty(ref _selectedPlaceholderItem, value);
		}
		private ShapeCanvasItem _selectedShapeItem;
		public ShapeCanvasItem SelectedShapeItem
		{
			get => _selectedShapeItem;
			set => SetProperty(ref _selectedShapeItem, value);
		}

		private int _selectedItemsCount;
		public int SelectedItemsCount
		{
			get => _selectedItemsCount;
			set => SetProperty(ref _selectedItemsCount, value);
		}

		private string _selectedRatio;
		public string SelectedRatio
		{
			get => _selectedRatio;
			set
			{
				SetProperty(ref _selectedRatio, value);
				SetCanvasRatio(value);
			}
		}

		private DesignerCanvas.Controls.TouchEnabledCanvas _canvas;
		public DesignerCanvas.Controls.TouchEnabledCanvas CustomDesignerCanvas
		{
			get => _canvas;
			set => SetProperty(ref _canvas, value);
		}

		private IBoxCanvasItem _listenerItem;
		public IBoxCanvasItem ListenerItem
		{
			get => _listenerItem;
			set => SetProperty(ref _listenerItem, value);
		}

		private Visibility _isRightSidebarVisible;
		public Visibility IsRightSidebarVisible
		{
			get => _isRightSidebarVisible;
			set => SetProperty(ref _isRightSidebarVisible, value);
		}

		private Template _currentTemplate;
		public Template CurrentTemplate
		{
			get => _currentTemplate;
			set
			{
				SetProperty(ref _currentTemplate, value);
				CustomDesignerCanvas.ClearCanvas();
				ApplyTemplate(value);
			}
		}

		private List<Template> _templates;
		public List<Template> Templates
		{
			get => _templates;
			set => SetProperty(ref _templates, value);
		}

		// Text-specific properties
		private ObservableCollection<string> _systemFonts;
		public ObservableCollection<string> SystemFonts
		{
			get => _systemFonts;
			set => SetProperty(ref _systemFonts, value);
		}

		private TextCanvasItem _selectedTextItem;
		public TextCanvasItem SelectedTextItem
		{
			get => _selectedTextItem;
			set => SetProperty(ref _selectedTextItem, value);
		}

		private ObservableCollection<CanvasLayer> _canvasLayers;
		public ObservableCollection<CanvasLayer> CanvasLayers
		{
			get => _canvasLayers;
			set => SetProperty(ref _canvasLayers, value);
		}

		private CanvasLayer _selectedLayer;
		public CanvasLayer SelectedLayer
		{
			get => _selectedLayer;
			set
			{
				SetProperty(ref _selectedLayer, value);
				if (value != null)
				{
					// Select the corresponding canvas item
					CustomDesignerCanvas.SelectedItems.Clear();
					CustomDesignerCanvas.SelectedItems.Add(value.CanvasItem);
				}
			}
		}

		public ICommand AddPlaceholderCmd { get; }
		public ICommand AlignBottomCmd { get; }
		public ICommand AlignHCenterCmd { get; }
		public ICommand AlignLeftCmd { get; }
		public ICommand AlignRightCmd { get; }
		public ICommand AlignStretchHCmd { get; }
		public ICommand AlignStretchVCmd { get; }
		public ICommand AlignTopCmd { get; }
		public ICommand AlignVCenterCmd { get; }
		public ICommand DistributeHorizontalCmd { get; }
		public ICommand DistributeVerticalCmd { get; }
		public ICommand BringToFrontCmd { get; }
		public ICommand ChangeCanvasOrientationCmd { get; }
		public ICommand ChangeSelectedOrientationCmd { get; }
		public ICommand ClearCanvasCmd { get; }
		public ICommand DuplicateSelectedCmd { get; }
		public ICommand ImportImageCmd { get; }
		public ICommand LoadTemplateCmd { get; }
		public ICommand PrintCmd { get; }
		public ICommand SaveAsCmd { get; }
		public ICommand SaveTemplateCmd { get; }
		public ICommand SaveCmd { get; }
		public ICommand SaveToDbCmd { get; }
		public ICommand LoadFromDbCmd { get; }
		public ICommand DeleteTemplateCmd { get; }
		public ICommand ClearAllTemplatesCmd { get; }
		public ICommand CreateEventCmd { get; }
		public ICommand RefreshEventsCmd { get; }
		public ICommand DuplicateTemplateCmd { get; }
		public ICommand AssignTemplateToEventCmd { get; }
		public ICommand RemoveTemplateFromEventCmd { get; }
		public ICommand SetAsDefaultTemplateCmd { get; }
		public ICommand LoadEventTemplateCmd { get; }
		public ICommand SendToBackCmd { get; }
		
		// Rotation commands
		public ICommand RotateLeft90Cmd { get; }
		public ICommand RotateRight90Cmd { get; }
		public ICommand ResetRotationCmd { get; }
		public ICommand FlipVerticalCmd { get; }
		public ICommand SetImageCmd { get; }
		public ICommand ToggleLockPositionCmd { get; }
		public ICommand ToggleLockSizeCmd { get; }
		public ICommand ToggleLockAspectRatioCmd { get; }
		public ICommand ChangeCanvasBackgroundCmd { get; }
		public ICommand UnselectAllCmd { get; }
		public ICommand ViewImageCmd { get; }
		public ICommand MoveLayerUpCmd { get; }
		public ICommand MoveLayerDownCmd { get; }
		public ICommand DeleteLayerCmd { get; }
		public ICommand DeleteSelectedItemsCmd { get; }
		public ICommand ToggleLayerVisibilityCmd { get; }
		public ICommand ToggleLayerLockCmd { get; }
		public ICommand AddTextCmd { get; }
		public ICommand AddRectangleCmd { get; }
		public ICommand AddCircleCmd { get; }
		public ICommand AddLineCmd { get; }
		
		// New functionality commands
		public ICommand NewTemplateCmd { get; }
		public ICommand ImportTemplateCmd { get; }
		public ICommand ExportTemplateCmd { get; }
		public ICommand BrowseTemplatesCmd { get; }
		public ICommand BrowseEventsCmd { get; }
		public ICommand LaunchPhotoboothCmd { get; }
		
		// Public property to check for unsaved changes
		public bool HasUnsavedChanges { get; private set; }
		public ICommand UndoCmd { get; }
		public ICommand RedoCmd { get; }
		#endregion

		public DesignerVM()
		{
			IsRightSidebarVisible = Visibility.Collapsed;
			SelectedItemsPositionChangeSource = ImgUnLockPath;
			SelectedItemsSizeChangeSource = ImgUnLockPath;
			SelectedItemsAspectRatioLockSource = ImgUnLockPath;
			CanvasLayers = new ObservableCollection<CanvasLayer>();
			
			// Initialize Services
			// Initialize services (removed for now)
			
			// Initialize Collections
			SavedTemplates = new ObservableCollection<TemplateData>();
			Events = new ObservableCollection<EventData>();
			EventTemplates = new ObservableCollection<TemplateData>();
			
			LoadSavedTemplates();
			LoadEvents();
			
			// Load last template on startup ONLY if no event is selected
			// (LoadEvents may have selected an event and loaded its template)
			LoadLastTemplate();
			
			LoadSystemFonts();
			CustomDesignerCanvas = new DesignerCanvas.Controls.TouchEnabledCanvas 
			{ 
				Background = Brushes.White,  // Set initial background to white
				MinWidth = 200,
				MinHeight = 300
			};
			
			// Set the DataContext so the canvas can bind to our properties
			CustomDesignerCanvas.DataContext = this;
			
			// Initialize the canvas background
			CustomDesignerCanvas.Background = CanvasBackgroundColor;
			
			// Set initial size to 2x6 with proper pixel dimensions
			if (PaperSizes.TryGetValue("2x6", out var defaultSize))
			{
				CustomDesignerCanvas.SetRatioWithPixels(2, 6, defaultSize.PixelsAt300DPI_Width, defaultSize.PixelsAt300DPI_Height);
			}
			else
			{
				// Fallback to old method
				CustomDesignerCanvas.SetRatio(2, 6);
			}
			
			CustomDesignerCanvas.SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;
			CustomDesignerCanvas.Items.CollectionChanged += Items_CollectionChanged;
			
			// Initialize SelectedRatio to match the default canvas ratio
			_selectedRatio = "2x6";
			
			// Subscribe to window size changes to update canvas size
			if (Application.Current?.MainWindow != null)
			{
				Application.Current.MainWindow.SizeChanged += (s, e) => RefreshCanvasSize();
			}

			AddPlaceholderCmd = new RelayCommand(_ => CustomDesignerCanvas.AddPlaceholder());
			AlignBottomCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignBottom());
			AlignHCenterCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignCenter());
			AlignLeftCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignLeft());
			AlignRightCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignRight());
			AlignStretchHCmd = new RelayCommand(_ => { /* Not implemented in current canvas */ });
			AlignStretchVCmd = new RelayCommand(_ => { /* Not implemented in current canvas */ });
			AlignTopCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignTop());
			AlignVCenterCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignMiddle());
			DistributeHorizontalCmd = new RelayCommand(_ => DistributeHorizontally(), _ => CanDistribute());
			DistributeVerticalCmd = new RelayCommand(_ => DistributeVertically(), _ => CanDistribute());
			BringToFrontCmd = new RelayCommand(_ => CustomDesignerCanvas.BringToFront());
			ChangeCanvasOrientationCmd = new RelayCommand(_ => 
			{
				CustomDesignerCanvas.ChangeCanvasOrientation("Portrait");
				// Update the selected ratio to reflect the new orientation
				UpdateSelectedRatioAfterOrientationChange();
			});
			ChangeSelectedOrientationCmd = new RelayCommand(_ => CustomDesignerCanvas.ChangeOrientationOfSelectedItems(true));
			ClearCanvasCmd = new RelayCommand(_ => { CustomDesignerCanvas.ClearCanvas(); _currentTemplate = null; });
			DuplicateSelectedCmd = new RelayCommand(_ => CustomDesignerCanvas.DuplicateSelected());
			ImportImageCmd = new RelayCommand(_ => CustomDesignerCanvas.ImportImage());
			LoadTemplateCmd = new RelayCommand(async _ => await LoadTemplateAsync());
			PrintCmd = new RelayCommand(_ => { /* Print functionality not implemented */ });
			SaveAsCmd = new RelayCommand(_ => { /* Save As functionality not implemented */ });
			SaveTemplateCmd = new RelayCommand(async _ => await SaveAsTemplateAsync());
			SaveCmd = new RelayCommand(_ => { /* Save functionality not implemented */ });
			SaveToDbCmd = new RelayCommand(async _ => await SaveTemplateToDbAsync());
			LoadFromDbCmd = new RelayCommand(async _ => await LoadTemplateFromDbAsync());
			DeleteTemplateCmd = new RelayCommand(async _ => await DeleteTemplateAsync());
			ClearAllTemplatesCmd = new RelayCommand(async _ => await ClearAllTemplatesAsync());
			CreateEventCmd = new RelayCommand(async _ => await CreateEventAsync());
			RefreshEventsCmd = new RelayCommand(_ => RefreshEvents());
			DuplicateTemplateCmd = new RelayCommand(async _ => await DuplicateTemplateAsync());
			AssignTemplateToEventCmd = new RelayCommand(async _ => await AssignTemplateToEventAsync());
			RemoveTemplateFromEventCmd = new RelayCommand(async _ => await RemoveTemplateFromEventAsync());
			SetAsDefaultTemplateCmd = new RelayCommand(async _ => await SetAsDefaultTemplateAsync());
			LoadEventTemplateCmd = new RelayCommand(async _ => await LoadEventTemplateAsync());
			SendToBackCmd = new RelayCommand(_ => CustomDesignerCanvas.SendToBack());
			
			// Initialize rotation commands
			RotateLeft90Cmd = new RelayCommand(_ => SelectedElementsRotation -= 90);
			RotateRight90Cmd = new RelayCommand(_ => SelectedElementsRotation += 90);
			ResetRotationCmd = new RelayCommand(_ => SelectedElementsRotation = 0);
			FlipVerticalCmd = new RelayCommand(_ => FlipSelectedItemsVertical());
			SetImageCmd = new RelayCommand(_ => { /* Not implemented in current canvas */ });
			ToggleLockPositionCmd = new RelayCommand(_ => IsEnabledSelectedItemsPositionChange = !IsEnabledSelectedItemsPositionChange);
			ToggleLockSizeCmd = new RelayCommand(_ => IsEnabledSelectedItemsSizeChange = !IsEnabledSelectedItemsSizeChange);
			ToggleLockAspectRatioCmd = new RelayCommand(_ => IsEnabledSelectedItemsAspectRatioChange = !IsEnabledSelectedItemsAspectRatioChange);
			ChangeCanvasBackgroundCmd = new RelayCommand(_ => ChangeCanvasBackground());
			UnselectAllCmd = new RelayCommand(_ => CustomDesignerCanvas.SelectedItems.Clear());
			ViewImageCmd = new RelayCommand(_ => { /* Not implemented in current canvas */ });
			MoveLayerUpCmd = new RelayCommand(_ => MoveLayerUp(), _ => CanMoveLayerUp());
			MoveLayerDownCmd = new RelayCommand(_ => MoveLayerDown(), _ => CanMoveLayerDown());
			DeleteLayerCmd = new RelayCommand(_ => DeleteLayer(), _ => SelectedLayer != null);
			DeleteSelectedItemsCmd = new RelayCommand(_ => DeleteSelectedItems(), _ => CanDeleteSelectedItems());
			ToggleLayerVisibilityCmd = new RelayCommand(layer => ToggleLayerVisibility(layer as CanvasLayer));
			ToggleLayerLockCmd = new RelayCommand(layer => ToggleLayerLock(layer as CanvasLayer));
			AddTextCmd = new RelayCommand(_ => AddText());
			AddRectangleCmd = new RelayCommand(_ => AddRectangle());
			AddCircleCmd = new RelayCommand(_ => AddCircle());
			AddLineCmd = new RelayCommand(_ => AddLine());
			
			// Initialize new functionality commands
			NewTemplateCmd = new RelayCommand(async _ => await NewTemplateAsync());
			ImportTemplateCmd = new RelayCommand(async _ => await ImportTemplateAsync());
			ExportTemplateCmd = new RelayCommand(async _ => await ExportTemplateAsync());
			BrowseTemplatesCmd = new RelayCommand(async _ => await BrowseTemplatesAsync());
			BrowseEventsCmd = new RelayCommand(async _ => await BrowseEventsAsync());
			LaunchPhotoboothCmd = new RelayCommand(async _ => await LaunchPhotoboothAsync(), _ => CanLaunchPhotobooth());
			UndoCmd = new RelayCommand(_ => UndoAction(), _ => CanUndo());
			RedoCmd = new RelayCommand(_ => RedoAction(), _ => CanRedo());
			
			// Initialize undo/redo system
			InitializeUndoRedoSystem();
			
			LoadTemplates();
		}

		private void LoadTemplates()
		{
			// Load templates from default location
			var templatesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Templates");
			if (Directory.Exists(templatesPath))
			{
				var templateFiles = Directory.GetFiles(templatesPath, "*.xml");
				Templates = templateFiles.Select(f => Template.LoadTemplateFromFile(f)).Where(t => t != null).ToList();
			}
		}
		
		#region Public Methods for Overlay
		
		/// <summary>
		/// Load a template from file path
		/// </summary>
		public bool LoadTemplate(string templatePath)
		{
			try
			{
				if (!File.Exists(templatePath))
				{
					System.Diagnostics.Debug.WriteLine($"Template file not found: {templatePath}");
					return false;
				}
				
				var template = Template.LoadTemplateFromFile(templatePath);
				if (template != null)
				{
					CurrentTemplate = template;  // This will call ApplyTemplate through the setter
					HasUnsavedChanges = false;
					return true;
				}
				return false;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error loading template: {ex.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// Create a new blank template
		/// </summary>
		public void CreateNewTemplate()
		{
			try
			{
				CustomDesignerCanvas.ClearCanvas();
				_currentTemplate = null;
				HasUnsavedChanges = false;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error creating new template: {ex.Message}");
			}
		}
		
		/// <summary>
		/// Save the current template
		/// </summary>
		public bool SaveTemplate()
		{
			try
			{
				// Execute the save template command
				if (SaveTemplateCmd.CanExecute(null))
				{
					SaveTemplateCmd.Execute(null);
					HasUnsavedChanges = false;
					return true;
				}
				return false;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Error saving template: {ex.Message}");
				return false;
			}
		}
		
		#endregion

		private async Task SaveAsTemplateAsync()
		{
			try
			{
				var templateService = new TemplateService();
				
				// If we have a currently loaded template, ask if user wants to update it or save as new
				if (CurrentLoadedTemplateId.HasValue && SelectedTemplate != null)
				{
					var result = MessageBox.Show(
						$"Do you want to update the current template '{SelectedTemplate.Name}'?\n\n" +
						"Yes - Update current template\n" +
						"No - Save as new template\n" +
						"Cancel - Cancel operation",
						"Save Template",
						MessageBoxButton.YesNoCancel,
						MessageBoxImage.Question);
					
					if (result == MessageBoxResult.Cancel)
						return;
					
					if (result == MessageBoxResult.Yes)
					{
						// Update existing template
						// Build a list of items with their actual z-index from the visual tree
						var itemsWithZIndex = new List<(ICanvasItem item, int zIndex)>();
						foreach (var item in CustomDesignerCanvas.Items)
						{
							if (item is ICanvasItem canvasItem)
							{
								// Get the actual z-index from the visual tree
								var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
								int actualZIndex = 0;
								if (container?.Parent is Canvas canvas)
								{
									actualZIndex = canvas.Children.IndexOf(container);
								}
								itemsWithZIndex.Add((canvasItem, actualZIndex));
							}
						}
						
						// Sort by z-index to ensure proper ordering
						itemsWithZIndex.Sort((a, b) => a.zIndex.CompareTo(b.zIndex));
						
						// Extract just the items in the correct order
						var canvasItems = itemsWithZIndex.Select(x => x.item).ToList();
						var canvasBackground = CustomDesignerCanvas.Background;
						
						// Update the template canvas items
						bool success = templateService.UpdateTemplateCanvas(CurrentLoadedTemplateId.Value,
							canvasItems, CustomDesignerCanvas.ActualPixelWidth, CustomDesignerCanvas.ActualPixelHeight, canvasBackground);
						
						if (success)
						{
							MessageBox.Show($"Template '{SelectedTemplate.Name}' updated successfully!", "Template Updated", 
								MessageBoxButton.OK, MessageBoxImage.Information);
							
							// Reload templates to show updated thumbnail
							LoadSavedTemplates();
							
							// If this template is assigned to an event, reload event templates
							if (SelectedEvent != null)
							{
								// Check if this template is assigned to the current event
								var eventService = new EventService();
								var eventTemplates = eventService.GetEventTemplates(SelectedEvent.Id);
								if (eventTemplates.Any(t => t.Id == CurrentLoadedTemplateId.Value))
								{
									// This template is assigned to the current event, reload event templates
									LoadEventTemplates(SelectedEvent.Id);
									
									// Also update the selected event template if it's the same one
									if (SelectedEventTemplate?.Id == CurrentLoadedTemplateId.Value)
									{
										SelectedEventTemplate = SavedTemplates.FirstOrDefault(t => t.Id == CurrentLoadedTemplateId.Value);
									}
								}
							}
						}
						return;
					}
				}
				
				// Save as new template
				await SaveTemplateToDbAsync();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error saving template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private static string PrintFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Prints");

		public void PrintTemplateWithImages(Template template, List<string> photoPaths)
		{
			photoPaths = photoPaths.Take(template.Elements.Count).ToList();
			string printfileName = Guid.NewGuid().ToString() + ".png";
			string printFilePath = Path.Combine(PrintFolder, printfileName);
			if (!Directory.Exists(PrintFolder))
			{
				Directory.CreateDirectory(PrintFolder);
			}

			ApplyTemplate(template, photoPaths);
			CanvasImageExporter.ExportImage(CustomDesignerCanvas, printFilePath, 300, 300);
			CustomDesignerCanvas.ClearCanvas();
		}

		private Template InitializeTemplate()
		{
			return new Template
			{
				Name = "Template",
				Id = Guid.NewGuid().ToString(),
				Resolution = "300",
				Print2PerPage = "False",
				PrintToSecondaryPrinter = "False",
				IsLegacy = "False",
				OriginalDevice = Environment.MachineName,
				BackgroundColor = CustomDesignerCanvas.Background.ToString()
			};
		}

		private Template MapTemplate(Template template)
		{
			template.Width = CustomDesignerCanvas.Width.ToString();
			template.Height = CustomDesignerCanvas.Height.ToString();
			template.Dimensions = $"{CustomDesignerCanvas.RatioX}x{CustomDesignerCanvas.RatioY}";
			template.LastSavedDate = DateTime.Now.ToString();
			template.BackgroundColor = CustomDesignerCanvas.Background.ToString();
			template.Elements = new List<ElementBase>();
			var converter = new BrushConverter();
			
			int index = 0;
			foreach (var item in CustomDesignerCanvas.Items)
			{
				if (item is PlaceholderCanvasItem phItem)
				{
					template.Elements.Add(new PhotoElement
					{
						Name = "Placeholder",
						Opacity = 100,
						Top = Convert.ToInt32(phItem.Top),
						Left = Convert.ToInt32(phItem.Left),
						Height = Convert.ToInt32(phItem.Height),
						Width = Convert.ToInt32(phItem.Width),
						ShadowEnabled = "False",
						ShadowColor = "Black",
						ShadowDepth = 0,
						StrokeColor = converter.ConvertToString(phItem.Background),
						KeepAspect = phItem.LockedAspectRatio.ToString(),
						ZIndex = index,
						PhotoNumber = phItem.PlaceholderNo
					});
				}
				else if (item is ImageCanvasItem imgItem)
				{
					template.Elements.Add(new ImageElement
					{
						Name = "Image",
						Opacity = 100,
						Top = Convert.ToInt32(imgItem.Top),
						Left = Convert.ToInt32(imgItem.Left),
						Height = Convert.ToInt32(imgItem.Height),
						Width = Convert.ToInt32(imgItem.Width),
						ShadowEnabled = "False",
						ShadowColor = "Black",
						ShadowDepth = 0,
						// ImagePath property not available in current ImageCanvasItem
						KeepAspect = imgItem.LockedAspectRatio.ToString(),
						ZIndex = index
					});
				}
				index++;
			}

			return template;
		}

		private async Task LoadTemplateAsync()
		{
			try
			{
				OpenFileDialog openFileDialog = new OpenFileDialog
				{
					Filter = "Template files (*.xml)|*.xml",
					Title = "Load template",
					InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Templates")
				};
				if (openFileDialog.ShowDialog() == true)
				{
					var template = Template.LoadTemplateFromFile(openFileDialog.FileName);
					if (template != null)
					{
						CurrentTemplate = template;
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error loading template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void ApplyTemplate(Template template, List<string> placeholderImages = null)
		{
			try
			{
				CustomDesignerCanvas.ClearCanvas();

				if (template == null) return;

				var dimensions = template.Dimensions.Split('x').Select(int.Parse).ToArray();
				CustomDesignerCanvas.SetRatio(dimensions[0], dimensions[1]);
				var converter = new BrushConverter();

				foreach (var element in template.Elements.OrderBy(e => e.ZIndex))
				{
					if (element is PhotoElement p)
					{
						if (placeholderImages != null && placeholderImages.Count > p.PhotoNumber - 1)
						{
							string photoPath = placeholderImages[p.PhotoNumber - 1];
							var imageSource = new BitmapImage(new Uri(photoPath, UriKind.RelativeOrAbsolute));
							CustomDesignerCanvas.Items.Add(new ImageCanvasItem(p.Left, p.Top, p.Width, p.Height, imageSource, 1, 1)
							{
								LockedAspectRatio = bool.Parse(p.KeepAspect)
							});
						}
						else
						{
							var placeholder = new PlaceholderCanvasItem(p.Left, p.Top, p.Width, p.Height, 1, 1)
							{
								LockedAspectRatio = bool.Parse(p.KeepAspect)
							};
							placeholder.Background = (Brush)converter.ConvertFromString(p.StrokeColor ?? "#FF0000FF");
							CustomDesignerCanvas.Items.Add(placeholder);
						}
					}
					else if (element is ImageElement i)
					{
						var imageSource = new BitmapImage(new Uri(i.ImagePath, UriKind.RelativeOrAbsolute));
					CustomDesignerCanvas.Items.Add(new ImageCanvasItem(i.Left, i.Top, i.Width, i.Height, imageSource, 1, 1)
						{
							LockedAspectRatio = bool.Parse(i.KeepAspect)
						});
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error applying template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void UnLockItemsPosition()
		{
			// Unlock position for selected items
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.LockedPosition = false;
			}
			SelectedItemsPositionChangeSource = ImgUnLockPath;
		}

		private void LockItemsPosition()
		{
			// Lock position for selected items
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.LockedPosition = true;
			}
			SelectedItemsPositionChangeSource = ImgLockPath;
		}

		private void UnlockItemsSize()
		{
			// Unlock size for selected items
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.Resizeable = true;
			}
			SelectedItemsSizeChangeSource = ImgUnLockPath;
		}

		private void LockItemsSize()
		{
			// Lock size for selected items
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.Resizeable = false;
			}
			SelectedItemsSizeChangeSource = ImgLockPath;
		}

		private void UnlockItemsAspectRatio()
		{
			// Unlock aspect ratio for selected items
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.LockedAspectRatio = false;
			}
			SelectedItemsAspectRatioLockSource = ImgUnLockPath;
		}

		private void LockItemsAspectRatio()
		{
			// Lock aspect ratio for selected items
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.LockedAspectRatio = true;
			}
			SelectedItemsAspectRatioLockSource = ImgLockPath;
		}

		private void ChangeCanvasBackground()
		{
			// Get current color from brush
			var currentColor = (CanvasBackgroundColor as SolidColorBrush)?.Color ?? Colors.White;
			
			// Show color picker dialog
			var newColor = Photobooth.Controls.PixiEditorColorPickerDialog.ShowDialog(
				Application.Current.MainWindow, 
				"Canvas Background Color", 
				currentColor);
			
			if (newColor.HasValue)
			{
				CanvasBackgroundColor = new SolidColorBrush(newColor.Value);
			}
		}

		private void SelectedItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			// Update selected items count
			SelectedItemsCount = CustomDesignerCanvas.SelectedItems.Count;
			
			// Trigger command re-evaluation for distribution buttons
			CommandManager.InvalidateRequerySuggested();
			
			if (CustomDesignerCanvas.SelectedItems.Count > 0)
			{
				IsRightSidebarVisible = Visibility.Visible;
				var firstItem = CustomDesignerCanvas.SelectedItems.FirstOrDefault();
				if (firstItem is IBoxCanvasItem item && ListenerItem != item)
				{
					ListenerItem = item;
					IsEnabledSelectedItemsPositionChange = !item.LockedPosition;
					IsEnabledSelectedItemsSizeChange = item.Resizeable;
					IsEnabledSelectedItemsAspectRatioChange = !item.LockedAspectRatio;
					
					// Update position and size display
					_isBoundChanged = true;
					SelectedElementsPositionX = item.Left.ToString("F0");
					SelectedElementsPositionY = item.Top.ToString("F0");
					SelectedElementsWidth = item.Width.ToString("F0");
					SelectedElementsHeight = item.Height.ToString("F0");
					
					// Get rotation angle from canvas item
					if (item is IBoxCanvasItem canvasItem)
					{
						SelectedElementsRotation = canvasItem.Angle;
					}
					else
					{
						SelectedElementsRotation = 0;
					}
					
					// Calculate actual pixel dimensions (for export/print quality)
					CalculateActualPixelDimensions(item);
					
					_isBoundChanged = false;

					// Handle text item selection
					SelectedTextItem = firstItem as TextCanvasItem;
					
					// Handle placeholder item selection
					SelectedPlaceholderItem = firstItem as PlaceholderCanvasItem;
					
					// Handle shape item selection
					SelectedShapeItem = firstItem as ShapeCanvasItem;
				}
			}
			else
			{
				IsRightSidebarVisible = Visibility.Collapsed;
				ListenerItem = null;
				SelectedTextItem = null;
				SelectedPlaceholderItem = null;
				SelectedShapeItem = null;
			}
		}

		public void SetCanvasRatio(string strRatio)
		{
			if (!string.IsNullOrEmpty(strRatio) && PaperSizes.TryGetValue(strRatio, out var paperSize))
			{
				// Set canvas to actual print dimensions in pixels (300 DPI)
				CustomDesignerCanvas.Width = paperSize.PixelsAt300DPI_Width;
				CustomDesignerCanvas.Height = paperSize.PixelsAt300DPI_Height;
				
				// Also update the ratio properties for compatibility
				var ratio = strRatio.Trim().Split('x').Select(int.Parse).ToArray();
				CustomDesignerCanvas.SetRatioWithPixels(ratio[0], ratio[1], paperSize.PixelsAt300DPI_Width, paperSize.PixelsAt300DPI_Height);
				
				// Notify UI that canvas properties may have changed
				OnPropertyChanged(nameof(CustomDesignerCanvas));
				
				// Clear any selected items when ratio changes to avoid layout issues
				CustomDesignerCanvas.SelectedItems.Clear();
				
				DebugService.LogDebug($"Set canvas to {strRatio}: {paperSize.PixelsAt300DPI_Width}x{paperSize.PixelsAt300DPI_Height}px at 300 DPI");
				
				// Raise canvas size changed event
				CanvasSizeChanged?.Invoke(this, EventArgs.Empty);
			}
		}
		
		private void UpdateSelectedRatioAfterOrientationChange()
		{
			// After orientation change, update the selected ratio to match the new dimensions
			// This ensures the UI shows the correct paper size after rotation
			var currentWidth = CustomDesignerCanvas.RatioX;
			var currentHeight = CustomDesignerCanvas.RatioY;
			
			// Find matching paper size from list
			var newRatio = $"{currentWidth}x{currentHeight}";
			if (ListOfRatios.Contains(newRatio))
			{
				// Update without triggering SetCanvasRatio again
				_selectedRatio = newRatio;
				OnPropertyChanged(nameof(SelectedRatio));
			}
			else
			{
				// Try to find the reversed ratio (e.g., 6x4 doesn't exist but 4x6 does)
				var reversedRatio = $"{currentHeight}x{currentWidth}";
				if (ListOfRatios.Contains(reversedRatio))
				{
					// For non-standard orientations, keep the current dimensions
					// but update the display to show closest match
					_selectedRatio = reversedRatio;
					OnPropertyChanged(nameof(SelectedRatio));
				}
			}
		}
		
		private void RefreshCanvasSize()
		{
			// Refresh the canvas size based on current window dimensions
			if (CustomDesignerCanvas != null)
			{
				CustomDesignerCanvas.SetRatio(CustomDesignerCanvas.RatioX, CustomDesignerCanvas.RatioY);
				OnPropertyChanged(nameof(CustomDesignerCanvas));
			}
		}

		private void CalculateActualPixelDimensions(IBoxCanvasItem item)
		{
			if (item is ImageCanvasItem imageItem)
			{
				// For images, try to get original pixel dimensions from the image source
				if (imageItem.Image is BitmapSource bitmap)
				{
					// Use the original bitmap dimensions
					SelectedElementsActualPixelWidth = bitmap.PixelWidth.ToString();
					SelectedElementsActualPixelHeight = bitmap.PixelHeight.ToString();
				}
				else
				{
					// Fallback: assume 4x scale factor (inverse of 25% display scale) if auto-fitted
					double scaleFactor = IsLikelyAutoFittedImage(item) ? 4.0 : 1.0;
					SelectedElementsActualPixelWidth = ((int)(item.Width * scaleFactor)).ToString();
					SelectedElementsActualPixelHeight = ((int)(item.Height * scaleFactor)).ToString();
				}
			}
			else
			{
				// For non-image items (text, placeholders, etc.), use canvas scale
				double canvasScaleFactor = GetCanvasToActualPixelRatio();
				SelectedElementsActualPixelWidth = ((int)(item.Width * canvasScaleFactor)).ToString();
				SelectedElementsActualPixelHeight = ((int)(item.Height * canvasScaleFactor)).ToString();
			}
		}

		private bool IsLikelyAutoFittedImage(IBoxCanvasItem item)
		{
			// Check if the item dimensions match canvas dimensions (indicating auto-fit)
			if (CustomDesignerCanvas?.ActualPixelWidth > 0 && CustomDesignerCanvas?.ActualPixelHeight > 0)
			{
				double canvasDisplayWidth = CustomDesignerCanvas.Width;
				double canvasDisplayHeight = CustomDesignerCanvas.Height;
				
				// Check if item size matches canvas size (within tolerance)
				double tolerance = 5.0;
				return Math.Abs(item.Width - canvasDisplayWidth) <= tolerance && 
					   Math.Abs(item.Height - canvasDisplayHeight) <= tolerance;
			}
			return false;
		}

		private double GetCanvasToActualPixelRatio()
		{
			// Calculate scale factor from display size to actual pixel size
			if (CustomDesignerCanvas?.ActualPixelWidth > 0 && CustomDesignerCanvas?.Width > 0)
			{
				return (double)CustomDesignerCanvas.ActualPixelWidth / CustomDesignerCanvas.Width;
			}
			return 1.0; // Default to 1:1 if can't determine scale
		}

		private void SetElementProperty(ref string field, string value, string propertyName, Action<double> setElementProperty)
		{
			double oldValue = string.IsNullOrEmpty(field) ? 0 : Convert.ToDouble(field);
			if (!string.IsNullOrEmpty(value))
			{
				field = value;
				if (!_isBoundChanged)
				{
					setElementProperty(Convert.ToDouble(value) - oldValue);
				}
				OnPropertyChanged(propertyName);
			}
		}

		private void SetSelectedItemsPositionX(double delta)
		{
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.Left += delta;
			}
		}

		private void SetSelectedItemsPositionY(double delta)
		{
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.Top += delta;
			}
		}
		
		/// <summary>
		/// Updates the property display values in real-time without affecting canvas items
		/// </summary>
		public void UpdatePropertyDisplayValues(double left, double top, double width, double height, double rotation = 0)
		{
			System.Diagnostics.Debug.WriteLine($"UpdatePropertyDisplayValues called: {left:F0}, {top:F0}, {width:F0}, {height:F0}");
			Console.WriteLine($"UpdatePropertyDisplayValues called: {left:F0}, {top:F0}, {width:F0}, {height:F0}");
			
			// Directly update backing fields to avoid property setter interference and dispatcher delays
			_selectedElementsPositionX = left.ToString("F0");
			_selectedElementsPositionY = top.ToString("F0");
			_selectedElementsWidth = width.ToString("F0");
			_selectedElementsHeight = height.ToString("F0");
			_selectedElementsRotation = rotation;
			
			// Calculate actual pixel dimensions based on canvas scale
			double canvasScaleFactor = 300.0 / 72.0; // 300 DPI for print / 72 DPI for display
			if (CustomDesignerCanvas != null && CustomDesignerCanvas.ActualPixelWidth > 0)
			{
				// Get the actual scale factor from the canvas
				double displayScale = CustomDesignerCanvas.ActualWidth / CustomDesignerCanvas.ActualPixelWidth;
				double actualPixelWidth = width / displayScale;
				double actualPixelHeight = height / displayScale;
				
				_selectedElementsActualPixelWidth = ((int)actualPixelWidth).ToString();
				_selectedElementsActualPixelHeight = ((int)actualPixelHeight).ToString();
			}
			else
			{
				// Fallback calculation
				_selectedElementsActualPixelWidth = ((int)(width * canvasScaleFactor)).ToString();
				_selectedElementsActualPixelHeight = ((int)(height * canvasScaleFactor)).ToString();
			}
			
			System.Diagnostics.Debug.WriteLine($"Properties updated: X={_selectedElementsPositionX}, Y={_selectedElementsPositionY}, W={_selectedElementsWidth}, H={_selectedElementsHeight}");
			System.Diagnostics.Debug.WriteLine($"Actual pixels: W={_selectedElementsActualPixelWidth}, H={_selectedElementsActualPixelHeight}");
			Console.WriteLine($"Properties updated: X={_selectedElementsPositionX}, Y={_selectedElementsPositionY}, W={_selectedElementsWidth}, H={_selectedElementsHeight}");
			Console.WriteLine($"Actual pixels: W={_selectedElementsActualPixelWidth}, H={_selectedElementsActualPixelHeight}");
			
			// Notify UI of changes immediately
			OnPropertyChanged(nameof(SelectedElementsPositionX));
			OnPropertyChanged(nameof(SelectedElementsPositionY));
			OnPropertyChanged(nameof(SelectedElementsWidth));
			OnPropertyChanged(nameof(SelectedElementsHeight));
			OnPropertyChanged(nameof(SelectedElementsRotation));
			OnPropertyChanged(nameof(SelectedElementsActualPixelWidth));
			OnPropertyChanged(nameof(SelectedElementsActualPixelHeight));
		}

		private void SetSelectedItemsWidth(double delta)
		{
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.Width += delta;
			}
		}

		private bool CanDistribute()
		{
			return CustomDesignerCanvas?.SelectedItems?.Count >= 3;
		}

		private void DistributeHorizontally()
		{
			var selectedItems = CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>().ToList();
			if (selectedItems.Count < 3) return;

			// Sort items by left position
			var sortedItems = selectedItems.OrderBy(item => item.Left).ToList();

			// Get leftmost and rightmost positions
			var leftmost = sortedItems.First().Left;
			var rightmost = sortedItems.Last().Left + sortedItems.Last().Width;

			// Calculate total width available for distribution
			var totalSpace = rightmost - leftmost;

			// Calculate total width of all objects
			var totalObjectWidth = sortedItems.Sum(item => item.Width);

			// Calculate space between objects
			var spaceBetween = (totalSpace - totalObjectWidth) / (sortedItems.Count - 1);

			// Position objects with equal spacing
			var currentPosition = leftmost;
			foreach (var item in sortedItems)
			{
				item.Left = currentPosition;
				currentPosition += item.Width + spaceBetween;
			}
		}

		private void DistributeVertically()
		{
			var selectedItems = CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>().ToList();
			if (selectedItems.Count < 3) return;

			// Sort items by top position
			var sortedItems = selectedItems.OrderBy(item => item.Top).ToList();

			// Get topmost and bottommost positions
			var topmost = sortedItems.First().Top;
			var bottommost = sortedItems.Last().Top + sortedItems.Last().Height;

			// Calculate total height available for distribution
			var totalSpace = bottommost - topmost;

			// Calculate total height of all objects
			var totalObjectHeight = sortedItems.Sum(item => item.Height);

			// Calculate space between objects
			var spaceBetween = (totalSpace - totalObjectHeight) / (sortedItems.Count - 1);

			// Position objects with equal spacing
			var currentPosition = topmost;
			foreach (var item in sortedItems)
			{
				item.Top = currentPosition;
				currentPosition += item.Height + spaceBetween;
			}
		}

		private void FlipSelectedItemsVertical()
		{
			foreach (var item in CustomDesignerCanvas.SelectedItems)
			{
				var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as ContentPresenter;
				if (container != null)
				{
					// Get or create transform group
					TransformGroup transformGroup;
					if (container.RenderTransform is TransformGroup tg)
					{
						transformGroup = tg;
					}
					else if (container.RenderTransform is RotateTransform rt)
					{
						// Convert single RotateTransform to TransformGroup
						transformGroup = new TransformGroup();
						transformGroup.Children.Add(rt);
						container.RenderTransform = transformGroup;
						container.RenderTransformOrigin = new Point(0.5, 0.5);
					}
					else
					{
						// Create new TransformGroup
						transformGroup = new TransformGroup();
						container.RenderTransform = transformGroup;
						container.RenderTransformOrigin = new Point(0.5, 0.5);
					}
					
					// Find or create ScaleTransform
					ScaleTransform scaleTransform = null;
					foreach (var transform in transformGroup.Children)
					{
						if (transform is ScaleTransform st)
						{
							scaleTransform = st;
							break;
						}
					}
					
					if (scaleTransform == null)
					{
						scaleTransform = new ScaleTransform(1, 1);
						transformGroup.Children.Add(scaleTransform);
					}
					
					// Flip vertical
					scaleTransform.ScaleY = -scaleTransform.ScaleY;
				}
			}
		}
		
		private void SetSelectedItemsHeight(double delta)
		{
			foreach (var item in CustomDesignerCanvas.SelectedItems.OfType<IBoxCanvasItem>())
			{
				item.Height += delta;
			}
		}

		#region Layer Management

		private void Items_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			RefreshLayersList();
		}

		private void RefreshLayersList()
		{
			System.Diagnostics.Debug.WriteLine($"RefreshLayersList: Starting with {CustomDesignerCanvas.Items.Count} canvas items");

			// Store current visibility and lock states before clearing
			var visibilityStates = new Dictionary<ICanvasItem, bool>();
			var lockStates = new Dictionary<ICanvasItem, bool>();
			foreach (var layer in CanvasLayers)
			{
				visibilityStates[layer.CanvasItem] = layer.IsVisible;
				lockStates[layer.CanvasItem] = layer.IsLocked;
				layer.PropertyChanged -= Layer_PropertyChanged;
			}

			CanvasLayers.Clear();
			// Since GraphicalObjectCollection doesn't support indexing, we'll use the canvas UI layer order
			// Higher z-index (later in canvas.Children) should appear first in layers list
			var itemsWithZIndex = new List<(ICanvasItem item, int zIndex)>();

			foreach (var item in CustomDesignerCanvas.Items)
			{
				var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
				System.Diagnostics.Debug.WriteLine($"RefreshLayersList: Item {item.GetType().Name}, Container: {container?.GetType().Name ?? "null"}");

				int zIndex = 0;
				if (container?.Parent is Canvas canvas)
				{
					zIndex = canvas.Children.IndexOf(container);
				}
				itemsWithZIndex.Add((item, zIndex));
			}

			// Sort by z-index descending (highest z-index first)
			var orderedItems = itemsWithZIndex.OrderByDescending(x => x.zIndex).ToList();

			foreach (var (item, zIndex) in orderedItems)
			{
				var layer = new CanvasLayer(item)
				{
					ZIndex = zIndex
				};
				
				// Restore previous visibility state if it exists, otherwise read from UI
				if (visibilityStates.ContainsKey(item))
				{
					layer.IsVisible = visibilityStates[item];
				}
				else
				{
					// Set initial visibility state based on current UI element visibility
					var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
					layer.IsVisible = container?.Visibility == Visibility.Visible;
				}
				
				// Restore previous lock state if it exists, otherwise read from canvas item
				if (lockStates.ContainsKey(item))
				{
					layer.IsLocked = lockStates[item];
				}
				else if (item is IBoxCanvasItem boxItem)
				{
					// Initialize lock state based on canvas item's current lock state
					layer.IsLocked = boxItem.LockedPosition;
				}
				
				// Subscribe to property changes for visibility handling
				layer.PropertyChanged += Layer_PropertyChanged;
				
				CanvasLayers.Add(layer);
			}
		}

		private bool CanMoveLayerUp()
		{
			return SelectedLayer != null && CanvasLayers.IndexOf(SelectedLayer) > 0;
		}

		private bool CanMoveLayerDown()
		{
			return SelectedLayer != null && CanvasLayers.IndexOf(SelectedLayer) < CanvasLayers.Count - 1;
		}

		private void MoveLayerUp()
		{
			if (!CanMoveLayerUp()) return;

			var currentIndex = CanvasLayers.IndexOf(SelectedLayer);
			var targetItem = SelectedLayer.CanvasItem;
			
			// Get current z-index and move up by 1
			var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(targetItem) as FrameworkElement;
			if (container?.Parent is Canvas canvas)
			{
				var currentZIndex = canvas.Children.IndexOf(container);
				var newZIndex = Math.Min(currentZIndex + 1, canvas.Children.Count - 1);
				
				// Use the canvas's z-index change method
				ChangeItemZIndex(targetItem, newZIndex);
			}

			RefreshLayersList();
			
			// Maintain selection - find the moved item in the new list
			var movedLayer = CanvasLayers.FirstOrDefault(l => l.CanvasItem == targetItem);
			if (movedLayer != null)
				SelectedLayer = movedLayer;
		}

		private void MoveLayerDown()
		{
			if (!CanMoveLayerDown()) return;

			var currentIndex = CanvasLayers.IndexOf(SelectedLayer);
			var targetItem = SelectedLayer.CanvasItem;
			
			// Get current z-index and move down by 1
			var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(targetItem) as FrameworkElement;
			if (container?.Parent is Canvas canvas)
			{
				var currentZIndex = canvas.Children.IndexOf(container);
				var newZIndex = Math.Max(currentZIndex - 1, 0);
				
				// Use the canvas's z-index change method
				ChangeItemZIndex(targetItem, newZIndex);
			}

			RefreshLayersList();
			
			// Maintain selection - find the moved item in the new list
			var movedLayer = CanvasLayers.FirstOrDefault(l => l.CanvasItem == targetItem);
			if (movedLayer != null)
				SelectedLayer = movedLayer;
		}

		private void ChangeItemZIndex(ICanvasItem item, int newZIndex)
		{
			var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
			if (container?.Parent is Canvas canvas)
			{
				canvas.Children.Remove(container);
				canvas.Children.Insert(newZIndex, container);
			}
		}

		private void DeleteLayer()
		{
			if (SelectedLayer == null) return;

			CustomDesignerCanvas.Items.Remove(SelectedLayer.CanvasItem);
			CustomDesignerCanvas.SelectedItems.Clear();
			RefreshLayersList();
			SelectedLayer = null;
		}

		private void DeleteSelectedItems()
		{
			if (CustomDesignerCanvas?.SelectedItems == null || CustomDesignerCanvas.SelectedItems.Count == 0) 
				return;

			// Create a copy of the selected items list to avoid modification during iteration
			var itemsToDelete = CustomDesignerCanvas.SelectedItems.Cast<IBoxCanvasItem>().ToList();

			// Save current state before deletion for undo functionality
			SaveCurrentState();

			foreach (var item in itemsToDelete)
			{
				CustomDesignerCanvas.Items.Remove(item);
			}

			CustomDesignerCanvas.SelectedItems.Clear();
			RefreshLayersList();
			
			// Clear selected items in properties panel
			SelectedTextItem = null;
			SelectedPlaceholderItem = null;
			SelectedShapeItem = null;
		}

		private bool CanDeleteSelectedItems()
		{
			return CustomDesignerCanvas?.SelectedItems != null && CustomDesignerCanvas.SelectedItems.Count > 0;
		}

		private void ToggleLayerVisibility(CanvasLayer layer)
		{
			if (layer == null) return;
			layer.IsVisible = !layer.IsVisible;
		}

		private void ToggleLayerLock(CanvasLayer layer)
		{
			if (layer == null) return;
			
			// Toggle the layer lock state
			layer.IsLocked = !layer.IsLocked;
			
			// Apply the lock to the actual canvas item
			if (layer.CanvasItem is IBoxCanvasItem boxItem)
			{
				boxItem.LockedPosition = layer.IsLocked;
				// Also lock resizing when locked
				if (boxItem is CanvasItem canvasItem)
				{
					canvasItem.Resizeable = !layer.IsLocked;
				}
			}
		}

		private void Layer_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (sender is CanvasLayer layer)
			{
				if (e.PropertyName == nameof(CanvasLayer.IsVisible))
				{
					SetLayerVisibility(layer.CanvasItem, layer.IsVisible);
				}
				else if (e.PropertyName == nameof(CanvasLayer.IsLocked))
				{
					// Sync lock state with canvas item
					if (layer.CanvasItem is IBoxCanvasItem boxItem)
					{
						boxItem.LockedPosition = layer.IsLocked;
						if (boxItem is CanvasItem canvasItem)
						{
							canvasItem.Resizeable = !layer.IsLocked;
						}
					}
				}
			}
		}

		private void SetLayerVisibility(ICanvasItem item, bool isVisible)
		{
			var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
			if (container != null)
			{
				container.Visibility = isVisible ? Visibility.Visible : Visibility.Hidden;
				
				// If we're making a hidden item visible and it's selected, we might need to clear/restore selection
				if (isVisible && CustomDesignerCanvas.SelectedItems.Contains(item))
				{
					// Ensure the selection is properly refreshed for visible items
					CustomDesignerCanvas.SelectedItems.Remove(item);
					CustomDesignerCanvas.SelectedItems.Add(item);
				}
			}
		}

		private void AddText()
		{
			// Add text at center using native paper dimensions
			var centerX = CustomDesignerCanvas.ActualPixelWidth > 0 ? CustomDesignerCanvas.ActualPixelWidth / 2 - 150 : 300;
			var centerY = CustomDesignerCanvas.ActualPixelHeight > 0 ? CustomDesignerCanvas.ActualPixelHeight / 2 - 50 : 300;
			
			// Create text item with reasonable default font size
			// Don't set Width/Height - let the text auto-size based on content
			var textItem = new TextCanvasItem(centerX, centerY, "Sample Text")
			{
				FontFamily = "Arial",
				FontSize = 72, // Use a reasonable default font size
				Foreground = Brushes.Black
			};
			
			// The TextCanvasItem will automatically size itself to fit the text
			// through its UpdateSizeToFitText() method
			
			CustomDesignerCanvas.Items.Add(textItem);
			
			// Select the newly added text item
			CustomDesignerCanvas.SelectedItems.Clear();
			CustomDesignerCanvas.SelectedItems.Add(textItem);
			
			// Change tracking removed
		}

		private void AddRectangle()
		{
			var centerX = CustomDesignerCanvas.ActualPixelWidth > 0 ? CustomDesignerCanvas.ActualPixelWidth / 2 - 100 : 250;
			var centerY = CustomDesignerCanvas.ActualPixelHeight > 0 ? CustomDesignerCanvas.ActualPixelHeight / 2 - 50 : 250;
			
			var shapeItem = new ShapeCanvasItem(centerX, centerY, ShapeType.Rectangle)
			{
				Width = 200,
				Height = 100,
				Fill = Brushes.LightBlue,
				Stroke = Brushes.DarkBlue,
				StrokeThickness = 2
			};
			
			CustomDesignerCanvas.Items.Add(shapeItem);
			CustomDesignerCanvas.SelectedItems.Clear();
			CustomDesignerCanvas.SelectedItems.Add(shapeItem);
			
			// Change tracking removed
		}

		private void AddCircle()
		{
			var centerX = CustomDesignerCanvas.ActualPixelWidth > 0 ? CustomDesignerCanvas.ActualPixelWidth / 2 - 75 : 225;
			var centerY = CustomDesignerCanvas.ActualPixelHeight > 0 ? CustomDesignerCanvas.ActualPixelHeight / 2 - 75 : 225;
			
			var shapeItem = new ShapeCanvasItem(centerX, centerY, ShapeType.Circle)
			{
				Width = 150,
				Height = 150,
				Fill = Brushes.LightGreen,
				Stroke = Brushes.DarkGreen,
				StrokeThickness = 2
			};
			
			CustomDesignerCanvas.Items.Add(shapeItem);
			CustomDesignerCanvas.SelectedItems.Clear();
			CustomDesignerCanvas.SelectedItems.Add(shapeItem);
			
			// Change tracking removed
		}

		private void AddLine()
		{
			var startX = CustomDesignerCanvas.ActualPixelWidth > 0 ? CustomDesignerCanvas.ActualPixelWidth / 2 - 100 : 200;
			var startY = CustomDesignerCanvas.ActualPixelHeight > 0 ? CustomDesignerCanvas.ActualPixelHeight / 2 : 250;
			
			var shapeItem = new ShapeCanvasItem(startX, startY, ShapeType.Line)
			{
				Width = 200,
				Height = 2,
				Stroke = Brushes.Black,
				StrokeThickness = 3
			};
			
			CustomDesignerCanvas.Items.Add(shapeItem);
			CustomDesignerCanvas.SelectedItems.Clear();
			CustomDesignerCanvas.SelectedItems.Add(shapeItem);
			
			// Change tracking removed
		}


		private void LoadSystemFonts()
		{
			SystemFonts = new ObservableCollection<string>();
			
			// Get all installed fonts from the system
			foreach (var fontFamily in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
			{
				SystemFonts.Add(fontFamily.Source);
			}
			
			// Ensure common fonts are at the top
			var commonFonts = new[] { "Arial", "Times New Roman", "Calibri", "Helvetica", "Georgia", "Verdana" };
			foreach (var font in commonFonts.Reverse())
			{
				if (SystemFonts.Contains(font))
				{
					SystemFonts.Remove(font);
					SystemFonts.Insert(0, font);
				}
			}
		}
		
		#region Template Database Methods
		
		private void LoadSavedTemplates()
		{
			try
			{
				// Initialize collection if null
				if (SavedTemplates == null)
					SavedTemplates = new ObservableCollection<TemplateData>();
				
				// Load templates using TemplateService
				var templateService = new TemplateService();
				var templates = templateService.GetAllTemplates();
				
				SavedTemplates.Clear();
				foreach (var template in templates)
				{
					SavedTemplates.Add(template);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to load saved templates: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		
		private async Task SaveTemplateToDbAsync()
		{
			try
			{
				// Prompt for template name
				var templateName = Microsoft.VisualBasic.Interaction.InputBox(
					"Enter a name for this template:", "Save Template", "New Template");
				
				if (string.IsNullOrWhiteSpace(templateName))
					return;
				
				var description = Microsoft.VisualBasic.Interaction.InputBox(
					"Enter a description (optional):", "Template Description", "");
				
				// Save current canvas to database with actual pixel dimensions
				var templateService = new TemplateService();
				
				// Build a list of items with their actual z-index from the visual tree
				var itemsWithZIndex = new List<(ICanvasItem item, int zIndex)>();
				foreach (var item in CustomDesignerCanvas.Items)
				{
					if (item is ICanvasItem canvasItem)
					{
						// Get the actual z-index from the visual tree
						var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
						int actualZIndex = 0;
						if (container?.Parent is Canvas canvas)
						{
							actualZIndex = canvas.Children.IndexOf(container);
						}
						itemsWithZIndex.Add((canvasItem, actualZIndex));
					}
				}
				
				// Sort by z-index to ensure proper ordering
				itemsWithZIndex.Sort((a, b) => a.zIndex.CompareTo(b.zIndex));
				
				// Extract just the items in the correct order
				var canvasItems = itemsWithZIndex.Select(x => x.item).ToList();
				var canvasBackground = CustomDesignerCanvas.Background;
				
				var templateId = templateService.SaveCurrentCanvas(
					templateName, 
					description, 
					canvasItems,
					CustomDesignerCanvas.ActualPixelWidth,
					CustomDesignerCanvas.ActualPixelHeight,
					canvasBackground);
				
				if (templateId > 0)
				{
					MessageBox.Show($"Template '{templateName}' saved successfully to database!", "Success", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					
					// Track the newly saved template as the current one
					CurrentLoadedTemplateId = templateId;
					
					// Refresh templates list
					LoadSavedTemplates();
					
					// Select the newly saved template in the list
					SelectedTemplate = SavedTemplates.FirstOrDefault(t => t.Id == templateId);
					
					// If an event is selected, ask if the template should be assigned to it
					if (SelectedEvent != null)
					{
						var result = MessageBox.Show(
							$"Would you like to assign this template to the event '{SelectedEvent.Name}'?",
							"Assign to Event",
							MessageBoxButton.YesNo,
							MessageBoxImage.Question);
						
						if (result == MessageBoxResult.Yes)
						{
							var eventService = new EventService();
							eventService.AssignTemplateToEvent(SelectedEvent.Id, templateId, false);
							LoadEventTemplates(SelectedEvent.Id);
						}
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to save template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task LoadTemplateFromDbAsync()
		{
			try
			{
				if (SelectedTemplate == null)
				{
					MessageBox.Show("Please select a template to load.", "No Template Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				// Use the existing LoadTemplateFromData method which properly loads all items
				await LoadTemplateFromData(SelectedTemplate);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to load template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task ClearAllTemplatesAsync()
		{
			try
			{
				var result = MessageBox.Show(
					"Are you sure you want to delete ALL templates from the database?\n\n" +
					"This action cannot be undone!", 
					"Clear All Templates", 
					MessageBoxButton.YesNo, 
					MessageBoxImage.Warning);
				
				if (result == MessageBoxResult.Yes)
				{
					// Double confirmation for safety
					result = MessageBox.Show(
						"This will permanently delete ALL templates and their thumbnails.\n\n" +
						"Are you absolutely sure?", 
						"Final Confirmation", 
						MessageBoxButton.YesNo, 
						MessageBoxImage.Warning);
					
					if (result == MessageBoxResult.Yes)
					{
						var database = new TemplateDatabase();
						database.ClearAllTemplates();
						
						// Clear the current canvas
						CustomDesignerCanvas.ClearCanvas();
						CurrentLoadedTemplateId = null;
						SelectedTemplate = null;
						
						// Refresh the templates list
						LoadSavedTemplates();
						
						MessageBox.Show("All templates have been cleared from the database.", "Templates Cleared", 
							MessageBoxButton.OK, MessageBoxImage.Information);
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to clear templates: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task DeleteTemplateAsync()
		{
			try
			{
				if (SelectedTemplate == null)
				{
					MessageBox.Show("Please select a template to delete.", "No Template Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				var result = MessageBox.Show(
					$"Are you sure you want to delete the template '{SelectedTemplate.Name}'?", 
					"Confirm Delete", 
					MessageBoxButton.YesNo, 
					MessageBoxImage.Question);
				
				if (result == MessageBoxResult.Yes)
				{
					// Store the template ID before deletion
					var templateIdToDelete = SelectedTemplate.Id;
					
					// Actually delete the template from database
					var database = new Database.TemplateDatabase();
					database.DeleteTemplate(templateIdToDelete);
					
					// If this was the currently loaded template, clear the canvas
					if (CurrentLoadedTemplateId.HasValue && CurrentLoadedTemplateId.Value == templateIdToDelete)
					{
						CustomDesignerCanvas.ClearCanvas();
						CurrentLoadedTemplateId = null;
					}
					
					// Reload the template list
					LoadSavedTemplates();
					
					// Clear selection
					SelectedTemplate = null;
					
					// If this template was assigned to the current event, refresh event templates
					if (SelectedEvent != null)
					{
						LoadEventTemplates(SelectedEvent.Id);
						if (SelectedEventTemplate?.Id == templateIdToDelete)
						{
							SelectedEventTemplate = null;
						}
					}
					
					MessageBox.Show("Template deleted successfully!", "Success", 
						MessageBoxButton.OK, MessageBoxImage.Information);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to delete template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		#region Event Management Methods
		
		private void LoadEvents()
		{
			try
			{
				var eventService = new EventService();
				var events = eventService.GetAllEvents();
				
				// Save current selection
				var previousSelectedEventId = SelectedEvent?.Id;
				
				Events.Clear();
				foreach (var evt in events)
				{
					Events.Add(evt);
				}

				// Try to restore previous selection first
				if (previousSelectedEventId.HasValue)
				{
					SelectedEvent = Events.FirstOrDefault(e => e.Id == previousSelectedEventId.Value);
				}
				
				// If no previous selection or it doesn't exist anymore, load last selected
				if (SelectedEvent == null)
				{
					LoadLastSelectedEvent();
				}
				
				// Force UI refresh
				OnPropertyChanged(nameof(Events));
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to load events: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		
		private void RefreshEvents()
		{
			LoadEvents();
			
			// If an event is selected, refresh its templates too
			if (SelectedEvent != null)
			{
				LoadEventTemplates(SelectedEvent.Id);
			}
		}

		private void LoadLastSelectedEvent()
		{
			try
			{
				var lastEventId = LoadLastEventId();
				if (lastEventId > 0)
				{
					var lastEvent = Events.FirstOrDefault(e => e.Id == lastEventId);
					if (lastEvent != null)
					{
						SelectedEvent = lastEvent;
						return;
					}
				}

				// If no last event or it's not found, select the first event
				if (Events.Any())
				{
					SelectedEvent = Events.First();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to load last selected event: {ex.Message}");
				// Select first event as fallback
				if (Events.Any())
				{
					SelectedEvent = Events.First();
				}
			}
		}

		private int LoadLastEventId()
		{
			try
			{
				var filePath = GetLastEventFilePath();
				if (File.Exists(filePath))
				{
					var content = File.ReadAllText(filePath);
					if (int.TryParse(content, out int eventId))
					{
						return eventId;
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to load last event ID: {ex.Message}");
			}
			return -1;
		}

		private void SaveLastEventId(int eventId)
		{
			try
			{
				File.WriteAllText(GetLastEventFilePath(), eventId.ToString());
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to save last event ID: {ex.Message}");
			}
		}

		private string GetLastEventFilePath()
		{
			var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			var photoboothPath = Path.Combine(appDataPath, "Photobooth");
			if (!Directory.Exists(photoboothPath))
			{
				Directory.CreateDirectory(photoboothPath);
			}
			return Path.Combine(photoboothPath, "lastEvent.txt");
		}
		
		private void LoadEventTemplates(int eventId)
		{
			try
			{
				var eventService = new EventService();
				var templates = eventService.GetEventTemplates(eventId);
				EventTemplates.Clear();
				foreach (var template in templates)
				{
					EventTemplates.Add(template);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to load event templates: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		
		private async Task LoadDefaultEventTemplateIfExists(int eventId)
		{
			try
			{
				// Don't auto-load if canvas already has items
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					if (CustomDesignerCanvas != null && CustomDesignerCanvas.Items.Count > 0)
					{
						System.Diagnostics.Debug.WriteLine("Canvas already has items, not loading default event template");
						return;
					}
				});
				
				// Get event templates
				var eventService = new EventService();
				var templates = eventService.GetEventTemplates(eventId);
				
				if (templates != null && templates.Any())
				{
					// For now, load the first template as default
					// TODO: In the future, support marking a template as default
					var defaultTemplate = templates.First();
					
					await Application.Current.Dispatcher.InvokeAsync(async () =>
					{
						// Only load if canvas is still empty
						if (CustomDesignerCanvas != null && CustomDesignerCanvas.Items.Count == 0)
						{
							await LoadTemplateFromData(defaultTemplate);
							SelectedEventTemplate = defaultTemplate;
							System.Diagnostics.Debug.WriteLine($"Auto-loaded default event template: {defaultTemplate.Name}");
						}
					});
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to load default event template: {ex.Message}");
			}
		}
		
		private async Task CreateEventAsync()
		{
			try
			{
				// Simple event creation dialog - can be enhanced with a proper dialog later
				var eventName = Microsoft.VisualBasic.Interaction.InputBox(
					"Enter event name (e.g., 'John & Amanda Wedding'):", "Create Event", "");
				
				if (string.IsNullOrWhiteSpace(eventName))
					return;
				
				var eventType = Microsoft.VisualBasic.Interaction.InputBox(
					"Enter event type:", "Event Type", "Wedding");
				
				var location = Microsoft.VisualBasic.Interaction.InputBox(
					"Enter location (optional):", "Event Location", "");
				
				var description = Microsoft.VisualBasic.Interaction.InputBox(
					"Enter description (optional):", "Event Description", "");
				
				// Create the event
				var eventService = new EventService();
				var eventId = eventService.CreateEvent(eventName, description);
				
				if (eventId > 0)
				{
					// Refresh the events list to show the new event
					RefreshEvents();
					
					// Select the newly created event
					SelectedEvent = Events.FirstOrDefault(e => e.Id == eventId);
					
					MessageBox.Show($"Event '{eventName}' created successfully!", "Success", 
						MessageBoxButton.OK, MessageBoxImage.Information);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to create event: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task DuplicateTemplateAsync()
		{
			try
			{
				if (SelectedTemplate == null)
				{
					MessageBox.Show("Please select a template to duplicate.", "No Template Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				var newName = Microsoft.VisualBasic.Interaction.InputBox(
					$"Enter name for duplicated template:", "Duplicate Template", 
					$"{SelectedTemplate.Name} (Copy)");
				
				if (string.IsNullOrWhiteSpace(newName))
					return;
				
				var templateService = new TemplateService();
				var newTemplateId = templateService.DuplicateTemplate(SelectedTemplate.Id, newName);
				
				if (newTemplateId > 0)
				{
					LoadSavedTemplates();
					
					// Select the newly duplicated template
					SelectedTemplate = SavedTemplates.FirstOrDefault(t => t.Id == newTemplateId);
					
					MessageBox.Show("Template duplicated successfully!", "Success", 
						MessageBoxButton.OK, MessageBoxImage.Information);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to duplicate template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task AssignTemplateToEventAsync()
		{
			try
			{
				if (SelectedEvent == null)
				{
					MessageBox.Show("Please select an event first.", "No Event Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				int templateId;
				string templateName;
				
				// Check if we have a selected template or need to save current canvas
				if (SelectedTemplate != null)
				{
					templateId = SelectedTemplate.Id;
					templateName = SelectedTemplate.Name;
				}
				else
				{
					// No template selected, we need to save current canvas as a template first
					if (CustomDesignerCanvas?.Items == null || CustomDesignerCanvas.Items.Count == 0)
					{
						MessageBox.Show("The current canvas is empty. Please create some content first or select an existing template.", 
							"Empty Canvas", MessageBoxButton.OK, MessageBoxImage.Information);
						return;
					}
					
					// Prompt for template name
					var inputResult = Microsoft.VisualBasic.Interaction.InputBox(
						$"The current canvas will be saved as a new template and assigned to '{SelectedEvent.Name}'.\n\nEnter a name for this template:", 
						"Save and Assign Template", 
						$"{SelectedEvent.Name} Template");
					
					if (string.IsNullOrWhiteSpace(inputResult))
						return;
					
					templateName = inputResult;
					
					// Save current canvas to database as a new template with actual pixel dimensions
					var templateService = new TemplateService();
					var canvasItems = CustomDesignerCanvas.Items.OfType<ICanvasItem>().ToList();
					var canvasBackground = CustomDesignerCanvas.Background;
					
					templateId = templateService.SaveCurrentCanvas(templateName, 
						$"Template for {SelectedEvent.Name}", 
						canvasItems, 
						CustomDesignerCanvas.ActualPixelWidth, 
						CustomDesignerCanvas.ActualPixelHeight, 
						canvasBackground);
					
					if (templateId <= 0)
					{
						MessageBox.Show("Failed to save template. Cannot assign to event.", "Error", 
							MessageBoxButton.OK, MessageBoxImage.Error);
						return;
					}
					
					// Refresh templates list to include the new template
					LoadSavedTemplates();
				}
				
				// Ask if this should be the default template
				var eventService = new EventService();
				var existingTemplates = eventService.GetEventTemplates(SelectedEvent.Id);
				string message = $"Assign template '{templateName}' to event '{SelectedEvent.Name}'?";
				
				if (existingTemplates.Count > 0)
				{
					message += $"\n\nThis event already has {existingTemplates.Count} template(s) assigned.";
				}
				
				message += "\n\nMake this the default template for the event?";
				message += "\n\n• Yes = Make it the default template";
				message += "\n• No = Add as additional template";
				message += "\n• Cancel = Don't assign";
				
				var result = MessageBox.Show(message, "Assign Template", 
					MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
				
				if (result == MessageBoxResult.Cancel)
					return;
				
				bool isDefault = result == MessageBoxResult.Yes;
				
				// Assign the template to the event
				eventService.AssignTemplateToEvent(SelectedEvent.Id, templateId, isDefault);
				
				// Reload the event templates to refresh the sidebar
				LoadEventTemplates(SelectedEvent.Id);
				
				MessageBox.Show($"Template '{templateName}' assigned to event '{SelectedEvent.Name}' successfully!", "Success", 
					MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to assign template to event: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task RemoveTemplateFromEventAsync()
		{
			try
			{
				if (SelectedEvent == null)
				{
					MessageBox.Show("Please select an event first.", "No Event Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				if (SelectedEventTemplate == null)
				{
					MessageBox.Show("Please select a template to remove from the event.", "No Template Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				// Store template name before removal
				string templateName = SelectedEventTemplate.Name;
				string eventName = SelectedEvent.Name;
				
				var result = MessageBox.Show(
					$"Remove template '{templateName}' from event '{eventName}'?\n\nThis will unassign the template but won't delete the template itself.", 
					"Remove Template from Event", 
					MessageBoxButton.YesNo, 
					MessageBoxImage.Question);
				
				if (result != MessageBoxResult.Yes)
					return;
				
				// Remove the template from the event
				var eventService = new EventService();
				eventService.RemoveTemplateFromEvent(SelectedEvent.Id, SelectedEventTemplate.Id);
				
				// Reload the event templates to refresh the sidebar
				LoadEventTemplates(SelectedEvent.Id);
				
				MessageBox.Show($"Template '{templateName}' removed from event '{eventName}' successfully!", "Success", 
					MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to remove template from event: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task SetAsDefaultTemplateAsync()
		{
			try
			{
				if (SelectedEvent == null)
				{
					MessageBox.Show("Please select an event first.", "No Event Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				if (SelectedEventTemplate == null)
				{
					MessageBox.Show("Please select a template to set as default.", "No Template Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				var result = MessageBox.Show(
					$"Set template '{SelectedEventTemplate.Name}' as the default template for event '{SelectedEvent.Name}'?", 
					"Set Default Template", 
					MessageBoxButton.YesNo, 
					MessageBoxImage.Question);
				
				if (result != MessageBoxResult.Yes)
					return;
				
				// Set the template as default (this will clear other defaults)
				// eventService.AssignTemplateToEvent(SelectedEvent.Id, SelectedEventTemplate.Id, true);
				LoadEventTemplates(SelectedEvent.Id);
				
				MessageBox.Show($"Template '{SelectedEventTemplate.Name}' is now the default template for event '{SelectedEvent.Name}'!", "Success", 
					MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to set default template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private async Task LoadEventTemplateAsync()
		{
			try
			{
				if (SelectedEvent == null)
				{
					MessageBox.Show("Please select an event first.", "No Event Selected", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				TemplateData templateToLoad = null;
				
				// If a specific template is selected from the event templates list, use that
				if (SelectedEventTemplate != null)
				{
					templateToLoad = SelectedEventTemplate;
				}
				else
				{
					// Otherwise, get the default template for the event
					templateToLoad = null; // TODO: eventService.GetDefaultEventTemplate(SelectedEvent.Id);
				}
				
				if (templateToLoad == null)
				{
					MessageBox.Show("No template found to load. Please assign a template to this event first.", 
						"No Template", MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				// Load the template using the common method
				await LoadTemplateFromData(templateToLoad);
				
				// Set the selected template so it shows in the main templates list too
				SelectedTemplate = templateToLoad;
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to load event template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		#endregion
		
		#endregion
		
		#region New Functionality Methods
		
		// New Template functionality
		private async Task NewTemplateAsync()
		{
			try
			{
				var result = MessageBox.Show(
					"Create a new template? This will clear the current canvas.", 
					"New Template", 
					MessageBoxButton.YesNo, 
					MessageBoxImage.Question);
					
				if (result == MessageBoxResult.Yes)
				{
					// Save current state for undo
					SaveCurrentState();
					
					// Clear the canvas
					CustomDesignerCanvas.ClearCanvas();
					
					// Clear the currently loaded template ID
					CurrentLoadedTemplateId = null;
					SelectedTemplate = null;
					
					// Set default paper size to first available size (2x6)
					string defaultPaperSize = PaperSizes.Keys.FirstOrDefault() ?? "2x6";
					if (PaperSizes.TryGetValue(defaultPaperSize, out var paperSize))
					{
						// Set canvas to default paper size dimensions
						CustomDesignerCanvas.Width = paperSize.PixelsAt300DPI_Width;
						CustomDesignerCanvas.Height = paperSize.PixelsAt300DPI_Height;
						
						// Parse the ratio from the paper size string
						var ratio = defaultPaperSize.Split('x').Select(int.Parse).ToArray();
						CustomDesignerCanvas.SetRatioWithPixels(ratio[0], ratio[1], 
							paperSize.PixelsAt300DPI_Width, paperSize.PixelsAt300DPI_Height);
							
						// Update the selected paper size in UI if there's a property for it
						// This will trigger any UI that's bound to paper size selection
						SetCanvasRatio(defaultPaperSize);
					}
					else
					{
						// Fallback to reasonable defaults if no paper sizes defined
						CustomDesignerCanvas.Width = 600;
						CustomDesignerCanvas.Height = 1800;
						CustomDesignerCanvas.SetRatioWithPixels(2, 6, 600, 1800);
					}
					
					// Set default white background
					CustomDesignerCanvas.Background = new SolidColorBrush(Colors.White);
					CanvasBackgroundColor = new SolidColorBrush(Colors.White);
					
					// Clear selection
					CustomDesignerCanvas.SelectedItems.Clear();
					
					// Refresh layers
					RefreshLayersList();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to create new template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		// Import Template functionality
		private async Task ImportTemplateAsync()
		{
			try
			{
				var dialog = new Microsoft.Win32.OpenFileDialog()
				{
					Title = "Import Template",
					Filter = "Template Package (*.zip)|*.zip|Template JSON (*.json)|*.json|All files (*.*)|*.*",
					DefaultExt = ".zip"
				};
				
				if (dialog.ShowDialog() == true)
				{
					// Save current state for undo
					SaveCurrentState();
					
					var fileName = dialog.FileName;
					var extension = Path.GetExtension(fileName).ToLower();
					
					if (extension == ".zip")
					{
						await ImportTemplatePackage(fileName);
					}
					else
					{
						// Legacy JSON import
						await ImportJsonTemplate(fileName);
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to import template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		// Import template package (ZIP with assets)
		private async Task ImportTemplatePackage(string zipFilePath)
		{
			var tempDir = Path.Combine(Path.GetTempPath(), $"template_import_{Guid.NewGuid()}");
			
			try
			{
				// Extract ZIP to temporary directory
				ZipFile.ExtractToDirectory(zipFilePath, tempDir);
				
				// Read template JSON
				var templateJsonPath = Path.Combine(tempDir, "template.json");
				if (!File.Exists(templateJsonPath))
				{
					throw new FileNotFoundException("Template JSON file not found in package.");
				}
				
				var json = File.ReadAllText(templateJsonPath);
				var templateData = JsonConvert.DeserializeObject<TemplateData>(json);
				
				if (templateData != null)
				{
					// Copy assets to application directory
					await RestoreAssets(tempDir, templateData);
					
					// Load the template
					await LoadImportedTemplate(templateData);
					
					MessageBox.Show("Template package imported successfully!\nAll assets have been restored.", 
						"Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
				}
			}
			finally
			{
				// Clean up temporary directory
				if (Directory.Exists(tempDir))
				{
					try
					{
						Directory.Delete(tempDir, true);
					}
					catch { /* Ignore cleanup errors */ }
				}
			}
		}
		
		// Import legacy JSON template
		private async Task ImportJsonTemplate(string jsonFilePath)
		{
			var json = File.ReadAllText(jsonFilePath);
			var templateData = JsonConvert.DeserializeObject<TemplateData>(json);
			
			if (templateData != null)
			{
				await LoadImportedTemplate(templateData);
			}
		}
		
		// Restore assets from imported package
		private async Task RestoreAssets(string tempDir, TemplateData templateData)
		{
			if (templateData.AssetMappings == null || templateData.AssetMappings.Count == 0)
				return;
				
			var assetsSourceDir = Path.Combine(tempDir, "assets");
			if (!Directory.Exists(assetsSourceDir))
				return;
			
			// Create assets directory in application folder
			var appAssetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoboothDesigner", "ImportedAssets");
			Directory.CreateDirectory(appAssetsDir);
			
			var newMappings = new Dictionary<string, string>();
			
			foreach (var mapping in templateData.AssetMappings)
			{
				try
				{
					var sourceAssetPath = Path.Combine(tempDir, mapping.Value); // assets/asset_001.jpg
					
					if (File.Exists(sourceAssetPath))
					{
						var fileName = Path.GetFileName(sourceAssetPath);
						var targetAssetPath = Path.Combine(appAssetsDir, fileName);
						
						// Copy asset to app directory
						File.Copy(sourceAssetPath, targetAssetPath, true);
						
						// Update mapping to new location
						newMappings[mapping.Key] = targetAssetPath;
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"Failed to restore asset {mapping.Value}: {ex.Message}");
				}
			}
			
			// Update template data with new asset paths
			templateData.AssetMappings = newMappings;
		}
		
		// Export Template functionality with assets
		private async Task ExportTemplateAsync()
		{
			try
			{
				if (CustomDesignerCanvas.Items.Count == 0)
				{
					MessageBox.Show("Canvas is empty. Nothing to export.", "Export Template", 
						MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}
				
				var dialog = new Microsoft.Win32.SaveFileDialog()
				{
					Title = "Export Template Package",
					Filter = "Template Package (*.zip)|*.zip|All files (*.*)|*.*",
					DefaultExt = ".zip",
					FileName = "template-package.zip"
				};
				
				if (dialog.ShowDialog() == true)
				{
					await ExportTemplatePackage(dialog.FileName);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to export template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		// Export template with all assets as ZIP package
		private async Task ExportTemplatePackage(string zipFilePath)
		{
			var tempDir = Path.Combine(Path.GetTempPath(), $"template_export_{Guid.NewGuid()}");
			
			try
			{
				// Create temporary directory
				Directory.CreateDirectory(tempDir);
				
				// Create assets directory
				var assetsDir = Path.Combine(tempDir, "assets");
				Directory.CreateDirectory(assetsDir);
				
				// Collect all assets and update template data
				var templateData = await CreateTemplateDataWithAssets(assetsDir);
				
				// Save template JSON
				var templateJsonPath = Path.Combine(tempDir, "template.json");
				var json = JsonConvert.SerializeObject(templateData, Formatting.Indented);
				File.WriteAllText(templateJsonPath, json);
				
				// Create manifest file
				await CreateManifest(tempDir, templateData);
				
				// Create ZIP package
				if (File.Exists(zipFilePath))
					File.Delete(zipFilePath);
					
				ZipFile.CreateFromDirectory(tempDir, zipFilePath);
				
				MessageBox.Show($"Template package exported successfully!\n\nPackage contains:\n- Template layout (template.json)\n- All image assets (assets/)\n- Manifest file (manifest.json)", 
					"Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			finally
			{
				// Clean up temporary directory
				if (Directory.Exists(tempDir))
				{
					try
					{
						Directory.Delete(tempDir, true);
					}
					catch { /* Ignore cleanup errors */ }
				}
			}
		}
		
		// Create template data and copy all assets
		private async Task<TemplateData> CreateTemplateDataWithAssets(string assetsDir)
		{
			var templateData = CreateTemplateDataFromCanvas();
			var assetCounter = 1;
			var assetMappings = new Dictionary<string, string>();
			
			// Process all canvas items to find and copy assets
			foreach (var item in CustomDesignerCanvas.Items)
			{
				if (item is ImageCanvasItem imageItem && imageItem.Image != null)
				{
					await ProcessImageAsset(imageItem, assetsDir, assetCounter++, assetMappings);
				}
				// Add more asset types here (fonts, etc.) if needed
			}
			
			// Update template data with new asset paths
			templateData.AssetMappings = assetMappings;
			
			return templateData;
		}
		
		// Process and copy image assets
		private async Task ProcessImageAsset(ImageCanvasItem imageItem, string assetsDir, int assetId, Dictionary<string, string> assetMappings)
		{
			try
			{
				if (imageItem.Image is BitmapImage bitmapImage && bitmapImage.UriSource != null)
				{
					var originalPath = bitmapImage.UriSource.LocalPath;
					
					if (File.Exists(originalPath))
					{
						var extension = Path.GetExtension(originalPath);
						var newFileName = $"asset_{assetId:D3}{extension}";
						var newFilePath = Path.Combine(assetsDir, newFileName);
						
						// Copy the asset file
						File.Copy(originalPath, newFilePath);
						
						// Store the mapping (original path -> new relative path)
						assetMappings[originalPath] = $"assets/{newFileName}";
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to process image asset: {ex.Message}");
			}
		}
		
		// Create manifest file with package information
		private async Task CreateManifest(string tempDir, TemplateData templateData)
		{
			var manifest = new
			{
				PackageVersion = "1.0",
				ExportDate = DateTime.Now,
				ApplicationName = "Photobooth Designer",
				Template = new
				{
					templateData.Name,
					templateData.Description,
					templateData.CanvasWidth,
					templateData.CanvasHeight,
					ItemCount = CustomDesignerCanvas.Items.Count
				},
				Assets = templateData.AssetMappings?.Count ?? 0,
				Instructions = new[]
				{
					"To import this template package:",
					"1. Use the Import function in Photobooth Designer",
					"2. Select this ZIP file",
					"3. All assets will be automatically restored"
				}
			};
			
			var manifestPath = Path.Combine(tempDir, "manifest.json");
			var manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
			File.WriteAllText(manifestPath, manifestJson);
		}
		
		// Helper method to create template data from current canvas
		private TemplateData CreateTemplateDataFromCanvas()
		{
			var templateData = new TemplateData
			{
				Name = "Exported Template",
				Description = "Template exported from canvas",
				CanvasWidth = CustomDesignerCanvas.ActualPixelWidth,
				CanvasHeight = CustomDesignerCanvas.ActualPixelHeight,
				CreatedDate = DateTime.Now,
				ModifiedDate = DateTime.Now,
				IsActive = true
			};
			
			// Set background color if available
			if (CanvasBackgroundColor is SolidColorBrush brush)
			{
				templateData.BackgroundColor = brush.Color.ToString();
			}
			
			// Convert all canvas items to CanvasItemData for export
			// Get items with their actual z-index from the canvas
			var canvasItems = new List<CanvasItemData>();
			
			// Build a list of items with their actual z-index from the visual tree
			var itemsWithZIndex = new List<(ICanvasItem item, int zIndex)>();
			foreach (var item in CustomDesignerCanvas.Items)
			{
				if (item is ICanvasItem canvasItem)
				{
					// Get the actual container from the canvas to find its z-index
					var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
					int actualZIndex = 0;
					
					if (container?.Parent is Canvas canvas)
					{
						// Get the actual position in the canvas children collection
						actualZIndex = canvas.Children.IndexOf(container);
					}
					
					itemsWithZIndex.Add((canvasItem, actualZIndex));
				}
			}
			
			// Sort by actual z-index and convert to CanvasItemData
			// Normalize z-indices to be sequential starting from 0
			int normalizedZIndex = 0;
			foreach (var (item, originalZIndex) in itemsWithZIndex.OrderBy(x => x.zIndex))
			{
				var itemData = ConvertCanvasItemToData(item, normalizedZIndex);
				if (itemData != null)
				{
					canvasItems.Add(itemData);
					normalizedZIndex++;
				}
			}
			
			// Store canvas items in the template data
			// Note: We need to add a property to TemplateData to hold canvas items
			templateData.CanvasItems = canvasItems;
			
			return templateData;
		}
		
		// Helper method to convert canvas item to data (similar to TemplateService)
		private CanvasItemData ConvertCanvasItemToData(ICanvasItem item, int zIndex)
		{
			var data = new CanvasItemData
			{
				ZIndex = zIndex,
				IsVisible = true
			};
			
			// Common properties for all items that implement IBoxCanvasItem
			if (item is IBoxCanvasItem boxItem)
			{
				data.X = boxItem.Left;
				data.Y = boxItem.Top;
				data.Width = boxItem.Width;
				data.Height = boxItem.Height;
				data.LockedPosition = boxItem.LockedPosition;
			}
			
			// Common properties - handle rotation and aspect ratio
			if (item is CanvasItem canvasItem)
			{
				data.Rotation = canvasItem.Angle;
				data.LockedAspectRatio = canvasItem.LockedAspectRatio;
			}
			else if (item is TextCanvasItem textCanvasItem)
			{
				data.Rotation = textCanvasItem.Angle;
				data.LockedAspectRatio = textCanvasItem.LockedAspectRatio;
			}
			
			// Type-specific properties
			switch (item)
			{
				case TextCanvasItem textItem:
					data.ItemType = "Text";
					data.Name = $"Text: {textItem.Text?.Substring(0, Math.Min(20, textItem.Text?.Length ?? 0))}...";
					data.Text = textItem.Text;
					data.FontFamily = textItem.FontFamily;
					data.FontSize = textItem.FontSize;
					data.TextColor = BrushToColorString(textItem.Foreground);
					data.IsBold = textItem.IsBold;
					data.IsItalic = textItem.IsItalic;
					data.IsUnderlined = textItem.IsUnderlined;
					data.HasShadow = textItem.HasShadow;
					data.ShadowOffsetX = textItem.ShadowOffsetX;
					data.ShadowOffsetY = textItem.ShadowOffsetY;
					data.ShadowBlurRadius = textItem.ShadowBlurRadius;
					data.ShadowColor = ColorToString(textItem.ShadowColor);
					data.HasOutline = textItem.HasOutline;
					data.OutlineThickness = textItem.OutlineThickness;
					data.OutlineColor = BrushToColorString(textItem.OutlineColor);
					data.TextAlignment = textItem.TextAlignment.ToString();
					break;
					
				case PlaceholderCanvasItem placeholderItem:
					data.ItemType = "Placeholder";
					data.Name = $"Placeholder {placeholderItem.PlaceholderNo}";
					data.PlaceholderNumber = placeholderItem.PlaceholderNo;
					// Store the placeholder color
					data.PlaceholderColor = BrushToColorString(placeholderItem.Background);
					break;
					
				case ImageCanvasItem imageItem:
					data.ItemType = "Image";
					data.Name = "Image";
					if (imageItem.Image is BitmapImage bitmapImage && bitmapImage.UriSource != null)
					{
						string imagePath = bitmapImage.UriSource.ToString();
						if (bitmapImage.UriSource.IsFile)
						{
							imagePath = bitmapImage.UriSource.LocalPath;
						}
						data.ImagePath = imagePath;
					}
					break;
					
				case ShapeCanvasItem shapeItem:
					data.ItemType = "Shape";
					data.Name = $"Shape: {shapeItem.ShapeType}";
					data.ShapeType = shapeItem.ShapeType.ToString();
					data.FillColor = BrushToColorString(shapeItem.Fill);
					data.StrokeColor = BrushToColorString(shapeItem.Stroke);
					data.StrokeThickness = shapeItem.StrokeThickness;
					data.HasNoFill = shapeItem.HasNoFill;
					data.HasNoStroke = shapeItem.HasNoStroke;
					break;
					
				default:
					data.ItemType = "Unknown";
					data.Name = item.GetType().Name;
					break;
			}
			
			return data;
		}
		
		// Helper method to convert Brush to color string
		private string BrushToColorString(Brush brush)
		{
			if (brush is SolidColorBrush solidBrush)
			{
				return solidBrush.Color.ToString();
			}
			return null;
		}
		
		// Helper method to convert Color to string
		private string ColorToString(Color color)
		{
			return color.ToString();
		}
		
		// Helper method to load imported template
		private async Task LoadImportedTemplate(TemplateData templateData)
		{
			// Clear current canvas
			CustomDesignerCanvas.ClearCanvas();
			
			// Set canvas size and background
			CustomDesignerCanvas.Width = templateData.CanvasWidth;
			CustomDesignerCanvas.Height = templateData.CanvasHeight;
			
			if (!string.IsNullOrEmpty(templateData.BackgroundColor))
			{
				try
				{
					var color = (Color)ColorConverter.ConvertFromString(templateData.BackgroundColor);
					CustomDesignerCanvas.Background = new SolidColorBrush(color);
					CanvasBackgroundColor = new SolidColorBrush(color);
				}
				catch
				{
					// Use default background if color parsing fails
				}
			}
			
			// Restore canvas items if available
			if (templateData.CanvasItems != null && templateData.CanvasItems.Count > 0)
			{
				// Sort by ZIndex to ensure correct layering order
				// Items with lower ZIndex should be added first (appear behind)
				var sortedItems = templateData.CanvasItems.OrderBy(i => i.ZIndex).ToList();
				
				// First, add all items to the canvas
				var addedItems = new List<(ICanvasItem item, int zIndex)>();
				foreach (var itemData in sortedItems)
				{
					var canvasItem = ConvertDataToCanvasItem(itemData, templateData.AssetMappings);
					if (canvasItem != null)
					{
						CustomDesignerCanvas.Items.Add(canvasItem);
						addedItems.Add((canvasItem, itemData.ZIndex));
					}
				}
				
				// Then, explicitly set z-indices after all items are added
				// This ensures proper layering especially for placeholders
				Application.Current.Dispatcher.BeginInvoke(
					System.Windows.Threading.DispatcherPriority.Loaded,
					new Action(() =>
					{
						foreach (var (item, zIndex) in addedItems)
						{
							var container = CustomDesignerCanvas.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
							if (container != null)
							{
								// Remove and re-add at the correct position to ensure proper z-order
								var parent = container.Parent as Canvas;
								if (parent != null)
								{
									parent.Children.Remove(container);
									// Insert at the correct z-index position
									if (zIndex < parent.Children.Count)
									{
										parent.Children.Insert(zIndex, container);
									}
									else
									{
										parent.Children.Add(container);
									}
								}
							}
						}
					}));
			}
			
			// Refresh layers
			RefreshLayersList();
			
			MessageBox.Show("Template imported successfully!", "Import Complete", 
				MessageBoxButton.OK, MessageBoxImage.Information);
		}
		
		// Helper method to convert CanvasItemData back to ICanvasItem
		private ICanvasItem ConvertDataToCanvasItem(CanvasItemData data, Dictionary<string, string> assetMappings)
		{
			ICanvasItem item = null;
			
			switch (data.ItemType)
			{
				case "Text":
					var textItem = new TextCanvasItem();
					textItem.SuppressAutoSize = true;
					
					// Set text properties
					textItem.Text = data.Text ?? "";
					if (!string.IsNullOrEmpty(data.FontFamily))
						textItem.FontFamily = data.FontFamily;
					if (data.FontSize.HasValue)
						textItem.FontSize = data.FontSize.Value;
					textItem.Foreground = ColorStringToBrush(data.TextColor);
					textItem.IsBold = data.IsBold;
					textItem.IsItalic = data.IsItalic;
					textItem.IsUnderlined = data.IsUnderlined;
					textItem.HasShadow = data.HasShadow;
					textItem.ShadowOffsetX = data.ShadowOffsetX;
					textItem.ShadowOffsetY = data.ShadowOffsetY;
					textItem.ShadowBlurRadius = data.ShadowBlurRadius;
					textItem.ShadowColor = ColorStringToColor(data.ShadowColor);
					textItem.HasOutline = data.HasOutline;
					textItem.OutlineThickness = data.OutlineThickness;
					textItem.OutlineColor = ColorStringToBrush(data.OutlineColor);
					if (!string.IsNullOrEmpty(data.TextAlignment) && 
						Enum.TryParse<TextAlignment>(data.TextAlignment, out var alignment))
					{
						textItem.TextAlignment = alignment;
					}
					
					// Re-enable auto-sizing
					textItem.SuppressAutoSize = false;
					// TextCanvasItem doesn't have UpdateSize method, the size will update automatically
					item = textItem;
					break;
					
				case "Placeholder":
					var placeholderItem = new PlaceholderCanvasItem();
					if (data.PlaceholderNumber.HasValue)
						placeholderItem.PlaceholderNo = data.PlaceholderNumber.Value;
					// PlaceholderCanvasItem uses Background property for its color
					if (!string.IsNullOrEmpty(data.PlaceholderColor))
						placeholderItem.Background = ColorStringToBrush(data.PlaceholderColor);
					item = placeholderItem;
					break;
					
				case "Image":
					var imageItem = new ImageCanvasItem();
					
					// Resolve image path using asset mappings if available
					string imagePath = data.ImagePath;
					if (assetMappings != null && !string.IsNullOrEmpty(imagePath))
					{
						// Check if we have a new path for this asset
						if (assetMappings.ContainsKey(imagePath))
						{
							imagePath = assetMappings[imagePath];
						}
					}
					
					// Load the image if path is valid
					if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
					{
						try
						{
							var bitmap = new BitmapImage();
							bitmap.BeginInit();
							bitmap.CacheOption = BitmapCacheOption.OnLoad;
							bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
							bitmap.EndInit();
							imageItem.Image = bitmap;
						}
						catch (Exception ex)
						{
							System.Diagnostics.Debug.WriteLine($"Failed to load image: {ex.Message}");
						}
					}
					item = imageItem;
					break;
					
				case "Shape":
					// Parse shape type first
					ShapeType shapeType = ShapeType.Rectangle;
					if (!string.IsNullOrEmpty(data.ShapeType))
					{
						Enum.TryParse<ShapeType>(data.ShapeType, out shapeType);
					}
					
					// Create shape with required constructor parameters
					var shapeItem = new ShapeCanvasItem(data.X, data.Y, shapeType);
					
					// Set additional properties
					shapeItem.Fill = ColorStringToBrush(data.FillColor);
					shapeItem.Stroke = ColorStringToBrush(data.StrokeColor);
					shapeItem.StrokeThickness = data.StrokeThickness;
					shapeItem.HasNoFill = data.HasNoFill;
					shapeItem.HasNoStroke = data.HasNoStroke;
					item = shapeItem;
					break;
			}
			
			// Set common properties
			if (item != null)
			{
				// Position and size
				if (item is IBoxCanvasItem boxItem)
				{
					boxItem.Left = data.X;
					boxItem.Top = data.Y;
					boxItem.Width = data.Width;
					boxItem.Height = data.Height;
					boxItem.LockedPosition = data.LockedPosition;
				}
				
				// Rotation and aspect ratio
				if (item is CanvasItem canvasItem)
				{
					canvasItem.Angle = data.Rotation;
					canvasItem.LockedAspectRatio = data.LockedAspectRatio;
				}
				else if (item is TextCanvasItem textCanvasItem)
				{
					textCanvasItem.Angle = data.Rotation;
					textCanvasItem.LockedAspectRatio = data.LockedAspectRatio;
				}
			}
			
			return item;
		}
		
		// Helper method to convert color string to Brush
		private Brush ColorStringToBrush(string colorString)
		{
			if (!string.IsNullOrEmpty(colorString))
			{
				try
				{
					var color = (Color)ColorConverter.ConvertFromString(colorString);
					return new SolidColorBrush(color);
				}
				catch { }
			}
			return null;
		}
		
		// Helper method to convert color string to Color
		private Color ColorStringToColor(string colorString)
		{
			if (!string.IsNullOrEmpty(colorString))
			{
				try
				{
					return (Color)ColorConverter.ConvertFromString(colorString);
				}
				catch { }
			}
			return Colors.Black;
		}
		
		#endregion
		
		#region Undo/Redo System
		
		// Initialize the undo/redo system
		private void InitializeUndoRedoSystem()
		{
			// Save initial empty state
			SaveCurrentState();
		}
		
		// Save current canvas state
		private void SaveCurrentState()
		{
			try
			{
				var state = new CanvasState
				{
					CanvasWidth = CustomDesignerCanvas.ActualPixelWidth,
					CanvasHeight = CustomDesignerCanvas.ActualPixelHeight,
					BackgroundColor = (CanvasBackgroundColor as SolidColorBrush)?.Color.ToString(),
					Items = new List<ICanvasItem>()
				};
				
				// Clone all canvas items
				foreach (var item in CustomDesignerCanvas.Items)
				{
					if (item is ICanvasItem canvasItem)
					{
						state.Items.Add(canvasItem.Clone());
					}
				}
				
				// Add to undo stack
				undoStack.Push(state);
				
				// Limit stack size
				if (undoStack.Count > MaxUndoLevels)
				{
					var tempStack = new Stack<CanvasState>();
					for (int i = 0; i < MaxUndoLevels - 1; i++)
					{
						tempStack.Push(undoStack.Pop());
					}
					undoStack.Clear();
					while (tempStack.Count > 0)
					{
						undoStack.Push(tempStack.Pop());
					}
				}
				
				// Clear redo stack when new action is performed
				redoStack.Clear();
			}
			catch (Exception ex)
			{
				// Log error but don't show to user as this shouldn't interrupt workflow
				System.Diagnostics.Debug.WriteLine($"Failed to save canvas state: {ex.Message}");
			}
		}
		
		// Undo functionality
		private void UndoAction()
		{
			try
			{
				if (undoStack.Count <= 1) return; // Keep at least one state
				
				// Move current state to redo stack
				var currentState = undoStack.Pop();
				redoStack.Push(currentState);
				
				// Get previous state
				var previousState = undoStack.Peek();
				
				// Restore previous state
				RestoreCanvasState(previousState);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to undo: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		// Redo functionality
		private void RedoAction()
		{
			try
			{
				if (redoStack.Count == 0) return;
				
				// Get state from redo stack
				var redoState = redoStack.Pop();
				
				// Add back to undo stack
				undoStack.Push(redoState);
				
				// Restore state
				RestoreCanvasState(redoState);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to redo: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		// Check if undo is possible
		private bool CanUndo()
		{
			return undoStack.Count > 1;
		}
		
		// Check if redo is possible
		private bool CanRedo()
		{
			return redoStack.Count > 0;
		}
		
		// Restore canvas to a previous state
		private void RestoreCanvasState(CanvasState state)
		{
			// Clear current canvas
			CustomDesignerCanvas.ClearCanvas();
			
			// Restore canvas properties
			CustomDesignerCanvas.Width = state.CanvasWidth;
			CustomDesignerCanvas.Height = state.CanvasHeight;
			
			// Restore background
			if (!string.IsNullOrEmpty(state.BackgroundColor))
			{
				try
				{
					var color = (Color)ColorConverter.ConvertFromString(state.BackgroundColor);
					var brush = new SolidColorBrush(color);
					CustomDesignerCanvas.Background = brush;
					CanvasBackgroundColor = brush;
				}
				catch
				{
					// Use default background if color parsing fails
					CustomDesignerCanvas.Background = new SolidColorBrush(Colors.White);
					CanvasBackgroundColor = new SolidColorBrush(Colors.White);
				}
			}
			
			// Restore items
			foreach (var item in state.Items)
			{
				CustomDesignerCanvas.Items.Add(item);
			}
			
			// Clear selection
			CustomDesignerCanvas.SelectedItems.Clear();
			
			// Refresh layers
			RefreshLayersList();
		}
		
		// Method to be called after any canvas modification to save state
		public void SaveStateAfterAction()
		{
			SaveCurrentState();
		}
		
		#endregion
		
		#region Template Browser
		
		// Browse templates functionality
		private async Task BrowseTemplatesAsync()
		{
			try
			{
				// Raise event to show the overlay (handled by MainPage)
				BrowseTemplatesRequested?.Invoke(this, EventArgs.Empty);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to browse templates: {ex.Message}", "Error",
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		// Load template from TemplateData object
		public async Task LoadTemplateFromData(TemplateData templateData)
		{
			try
			{
				// Load template on UI thread since it creates UI elements
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					var templateService = new TemplateService();
					bool loaded = templateService.LoadTemplate(templateData.Id, (template, canvasItems) =>
					{
						try
						{
							// Clear current canvas
							CustomDesignerCanvas.ClearCanvas();
							
							// Set canvas dimensions - need to determine if this is an old or new template
							// Old templates saved display dimensions (e.g., 261x783)
							// New templates save pixel dimensions (e.g., 600x1800)
							
							// Check if dimensions look like pixel dimensions (typically > 500)
							bool isPixelDimensions = template.CanvasWidth > 500 || template.CanvasHeight > 500;
							
							if (isPixelDimensions)
							{
								// New template with pixel dimensions
								int ratioX = (int)(template.CanvasWidth / 300);
								int ratioY = (int)(template.CanvasHeight / 300);
								CustomDesignerCanvas.SetRatioWithPixels(ratioX, ratioY, (int)template.CanvasWidth, (int)template.CanvasHeight);
							}
							else
							{
								// Old template with display dimensions - need to convert to proper ratio
								// Estimate the actual pixel dimensions (assuming ~2.3x scale factor)
								int estimatedPixelWidth = (int)(template.CanvasWidth * 2.3);
								int estimatedPixelHeight = (int)(template.CanvasHeight * 2.3);
								int ratioX = Math.Max(1, estimatedPixelWidth / 300);
								int ratioY = Math.Max(1, estimatedPixelHeight / 300);
								CustomDesignerCanvas.SetRatioWithPixels(ratioX, ratioY, estimatedPixelWidth, estimatedPixelHeight);
							}
							
							// Load background color
							if (!string.IsNullOrEmpty(template.BackgroundColor))
							{
								try
								{
									var color = (Color)ColorConverter.ConvertFromString(template.BackgroundColor);
									var brush = new SolidColorBrush(color);
									CustomDesignerCanvas.Background = brush;
									CanvasBackgroundColor = brush;
								}
								catch
								{
									// Use default background if color parsing fails
								}
							}
							
							// Load actual canvas items
							System.Diagnostics.Debug.WriteLine($"DesignerVM.LoadTemplateFromData: Adding {canvasItems.Count} items to canvas");
							foreach (var item in canvasItems)
							{
								System.Diagnostics.Debug.WriteLine($"DesignerVM.LoadTemplateFromData: Adding item type {item.GetType().Name} to canvas");

								if (item is IBoxCanvasItem boxItem)
								{
									System.Diagnostics.Debug.WriteLine($"  Position: ({boxItem.Left}, {boxItem.Top}), Size: {boxItem.Width}x{boxItem.Height}");
								}

								CustomDesignerCanvas.Items.Add(item);
								System.Diagnostics.Debug.WriteLine($"  Canvas now has {CustomDesignerCanvas.Items.Count} items");
							}

							System.Diagnostics.Debug.WriteLine($"DesignerVM.LoadTemplateFromData: Final canvas item count: {CustomDesignerCanvas.Items.Count}");

							// Force container generation before refreshing layers
							System.Diagnostics.Debug.WriteLine($"DesignerVM.LoadTemplateFromData: Before UpdateLayout, canvas has {CustomDesignerCanvas.Items.Count} items");
							CustomDesignerCanvas.UpdateLayout();

							// Give WPF time to generate containers
							Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

							// Now refresh the UI
							System.Diagnostics.Debug.WriteLine($"DesignerVM.LoadTemplateFromData: Before RefreshLayersList, canvas has {CustomDesignerCanvas.Items.Count} items");
							RefreshLayersList();
							System.Diagnostics.Debug.WriteLine($"DesignerVM.LoadTemplateFromData: After RefreshLayersList, canvas has {CustomDesignerCanvas.Items.Count} items");
							System.Diagnostics.Debug.WriteLine($"DesignerVM.LoadTemplateFromData: Layers count: {CanvasLayers.Count}");
							SaveCurrentState();
							
							// Track the currently loaded template
							CurrentLoadedTemplateId = template.Id;
							
							// Save as the last template
							SaveLastTemplateId(template.Id);
						}
						catch (Exception ex)
						{
							MessageBox.Show($"Failed to apply template data: {ex.Message}", "Error", 
								MessageBoxButton.OK, MessageBoxImage.Error);
						}
					});
					
					if (!loaded)
					{
						MessageBox.Show("Failed to load template from database.", "Error", 
							MessageBoxButton.OK, MessageBoxImage.Error);
					}
				});
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to load template: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		#endregion
		
		#region Event Browser
		
		// Browse events functionality
		private async Task BrowseEventsAsync()
		{
			try
			{
				var browserWindow = new Windows.EventBrowserWindow();
				browserWindow.Owner = Application.Current.MainWindow;
				
				if (browserWindow.ShowDialog() == true && browserWindow.SelectedEvent != null)
				{
					// Set the selected event and load its templates
					SelectedEvent = browserWindow.SelectedEvent;
					LoadEventTemplates(SelectedEvent.Id);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to browse events: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		// Launch Photobooth functionality
		private async Task LaunchPhotoboothAsync()
		{
			try
			{
				DebugService.LogDebug($"LaunchPhotoboothAsync called - SelectedEvent: {SelectedEvent?.Name ?? "null"}");
				
				if (SelectedEvent == null)
				{
					MessageBox.Show("Please select an event first to launch the photobooth.", 
						"No Event Selected", MessageBoxButton.OK, MessageBoxImage.Information);
					return;
				}

				var photoboothService = new PhotoboothService();
				await photoboothService.LaunchPhotoboothAsync(SelectedEvent.Id);
				
				// Force command re-evaluation after window closes
				Application.Current.Dispatcher.Invoke(() =>
				{
					CommandManager.InvalidateRequerySuggested();
				});
				
				DebugService.LogDebug("LaunchPhotoboothAsync completed");
			}
			catch (Exception ex)
			{
				DebugService.LogDebug($"LaunchPhotoboothAsync failed: {ex.Message}");
				MessageBox.Show($"Failed to launch photobooth: {ex.Message}", "Error", 
					MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		
		private bool CanLaunchPhotobooth()
		{
			return true; // Always enable the button for now to diagnose the issue
		}
		
		#endregion

		#region Last Template Tracking
		
		private string GetLastTemplateFilePath()
		{
			var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Photobooth");
			if (!Directory.Exists(appDataPath))
				Directory.CreateDirectory(appDataPath);
			return Path.Combine(appDataPath, "last_template.txt");
		}
		
		public void SaveLastTemplateId(int templateId)
		{
			try
			{
				File.WriteAllText(GetLastTemplateFilePath(), templateId.ToString());
				_lastTemplateId = templateId;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to save last template ID: {ex.Message}");
			}
		}
		
		private int LoadLastTemplateId()
		{
			try
			{
				var filePath = GetLastTemplateFilePath();
				if (File.Exists(filePath))
				{
					var content = File.ReadAllText(filePath);
					if (int.TryParse(content, out int templateId))
					{
						_lastTemplateId = templateId;
						return templateId;
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to load last template ID: {ex.Message}");
			}
			return -1;
		}
		
		private void LoadLastTemplate()
		{
			try
			{
				// Don't auto-load if we already have items on the canvas (from event template)
				if (CustomDesignerCanvas != null && CustomDesignerCanvas.Items.Count > 0)
				{
					System.Diagnostics.Debug.WriteLine("Canvas already has items, skipping auto-load of last template");
					return;
				}
				
				// Don't auto-load if an event is selected (it will load its own template)
				if (SelectedEvent != null)
				{
					System.Diagnostics.Debug.WriteLine("Event is selected, skipping auto-load of last template");
					return;
				}
				
				var lastTemplateId = LoadLastTemplateId();
				if (lastTemplateId > 0)
				{
					// Try to find and auto-load the last template
					LoadSavedTemplates();
					var lastTemplate = SavedTemplates.FirstOrDefault(t => t.Id == lastTemplateId);
					if (lastTemplate != null)
					{
						SelectedTemplate = lastTemplate;
						// Auto-load the template
						Task.Run(async () => await LoadTemplateFromData(lastTemplate));
						System.Diagnostics.Debug.WriteLine($"Auto-loaded last template: {lastTemplate.Name}");
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to auto-load last template: {ex.Message}");
			}
		}
		
		#endregion

		#endregion
	}
	
	// Canvas state class for undo/redo functionality
	public class CanvasState
	{
		public double CanvasWidth { get; set; }
		public double CanvasHeight { get; set; }
		public string BackgroundColor { get; set; }
		public List<ICanvasItem> Items { get; set; } = new List<ICanvasItem>();
	}
}