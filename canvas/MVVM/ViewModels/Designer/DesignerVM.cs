using DesignerCanvas;
using Microsoft.Win32;
using Photobooth.MVVM.Models;
using Photobooth.MVVM.ViewModels.Settings;
using Photobooth.MVVM.Views.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Photobooth.MVVM.ViewModels.Designer
{
	public class DesignerVM : BaseViewModel
	{

        #region properties
        //private static DesignerVM _instance;
        //public static DesignerVM Instance => _instance ?? (_instance = new DesignerVM());
        //private static DesignerVM _camInstance;
        //public static DesignerVM CamInstance => _camInstance ?? (_camInstance = new DesignerVM());

        private bool _isBoundChanged;
		private const string ImgLockPath = "/images/lock.png";
		private const string ImgUnLockPath = "/images/Padlock.png";
		public List<string> ListOfRatios { get; } = new List<string> { "2x6", "4x6", "5x7", "8x10" };

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
			set => SetElementProperty(ref _selectedElementsPositionX, value, nameof(SelectedElementsPositionX), CustomDesignerCanvas.SetExplicitSelectedElementsPositionX);
		}

		private string _selectedElementsPositionY;
		public string SelectedElementsPositionY
		{
			get => _selectedElementsPositionY;
			set => SetElementProperty(ref _selectedElementsPositionY, value, nameof(SelectedElementsPositionY), CustomDesignerCanvas.SetExplicitSelectedElementsPositionY);
		}

		private string _selectedElementsWidth;
		public string SelectedElementsWidth
		{
			get => _selectedElementsWidth;
			set => SetElementProperty(ref _selectedElementsWidth, value, nameof(SelectedElementsWidth), CustomDesignerCanvas.SetExplicitSelectedElementsWidth);
		}

		private string _selectedElementsHeight;
		public string SelectedElementsHeight
		{
			get => _selectedElementsHeight;
			set => SetElementProperty(ref _selectedElementsHeight, value, nameof(SelectedElementsHeight), CustomDesignerCanvas.SetExplicitSelectedElementsHeight);
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

		private DesignerCanvas.Controls.DesignerCanvas _canvas;
		public DesignerCanvas.Controls.DesignerCanvas CustomDesignerCanvas
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

		public ICommand AddPlaceholderCmd { get; }
		public ICommand AlignBottomCmd { get; }
		public ICommand AlignHCenterCmd { get; }
		public ICommand AlignLeftCmd { get; }
		public ICommand AlignRightCmd { get; }
		public ICommand AlignStretchHCmd { get; }
		public ICommand AlignStretchVCmd { get; }
		public ICommand AlignTopCmd { get; }
		public ICommand AlignVCenterCmd { get; }
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
		public ICommand SendToBackCmd { get; }
		public ICommand SetImageCmd { get; }
		public ICommand ToggleLockPositionCmd { get; }
		public ICommand ToggleLockSizeCmd { get; }
		public ICommand UnselectAllCmd { get; }
		public ICommand ViewImageCmd { get; }

        #endregion

        public DesignerVM()
		{
			IsRightSidebarVisible = Visibility.Collapsed;
			SelectedItemsPositionChangeSource = ImgUnLockPath;
			SelectedItemsSizeChangeSource = ImgUnLockPath;
			CustomDesignerCanvas = new DesignerCanvas.Controls.DesignerCanvas { Background = Brushes.White };
			CustomDesignerCanvas.SetRatio(2, 6);
			CustomDesignerCanvas.SelectedItems.CollectionChanged += SelectedItems_CollectionChanged;

			AddPlaceholderCmd = new RelayCommand(_ => CustomDesignerCanvas.AddPlaceholder());
			AlignBottomCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignBottom());
			AlignHCenterCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignHorizontallyCenter());
			AlignLeftCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignLeft());
			AlignRightCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignRight());
			AlignStretchHCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignStretchHorizontal());
			AlignStretchVCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignStretchVertical());
			AlignTopCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignTop());
			AlignVCenterCmd = new RelayCommand(_ => CustomDesignerCanvas.AlignVerticallyCenter());
			BringToFrontCmd = new RelayCommand(_ => CustomDesignerCanvas.BringToTheFront());
			ChangeCanvasOrientationCmd = new RelayCommand(_ => CustomDesignerCanvas.ChangeCanvasOrientation());
			ChangeSelectedOrientationCmd = new RelayCommand(_ => CustomDesignerCanvas.ChangeOrientationOfSelectedItems());
			ClearCanvasCmd = new RelayCommand(_ => { CustomDesignerCanvas.ClearCanvas(); _currentTemplate = null; });
			DuplicateSelectedCmd = new RelayCommand(_ => CustomDesignerCanvas.DuplicateSelected());
			ImportImageCmd = new RelayCommand(_ => CustomDesignerCanvas.ImportImage());
			LoadTemplateCmd = new RelayCommand(async _ => await LoadTemplateAsync());
			PrintCmd = new RelayCommand(_ => CustomDesignerCanvas.PrintFile());
			SaveAsCmd = new RelayCommand(_ => CustomDesignerCanvas.SaveAsFile());
			SaveTemplateCmd = new RelayCommand(async _ => await SaveAsTemplateImplAsync());
			SaveCmd = new RelayCommand(_ => CustomDesignerCanvas.SaveFile());
			SendToBackCmd = new RelayCommand(_ => CustomDesignerCanvas.SendToTheBack());
			SetImageCmd = new RelayCommand(_ => CustomDesignerCanvas.SetImageToCanvas());
			ToggleLockPositionCmd = new RelayCommand(_ => IsEnabledSelectedItemsPositionChange = !IsEnabledSelectedItemsPositionChange);
			ToggleLockSizeCmd = new RelayCommand(_ => IsEnabledSelectedItemsSizeChange = !IsEnabledSelectedItemsSizeChange);
			UnselectAllCmd = new RelayCommand(_ => CustomDesignerCanvas.SelectedItems.Clear());
			ViewImageCmd = new RelayCommand(_ => CustomDesignerCanvas.ViewFileInImageViewer());
			Templates = SessionsVM.Instance.CurrentSession?.GetTemplates();
		}

		private async Task SaveAsTemplateImplAsync()
		{
			try
			{
				if(CurrentTemplate != null)
				{
					MapTemplate(CurrentTemplate);
					Template.SaveTemplateToFile(CurrentTemplate);
					_templates = SessionsVM.Instance.getAllCurrentSessionTemplates();
					MessageBox.Show("Template saved successfully.", "Tempalte Saved", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				}
				else
				{
					Template template = InitializeTemplate();
					if (template.Id != null)
					{
						if (new PopUpWindow(PopUpWindow.PopUpType.Template, MapTemplate(template)).ShowDialog() == true)
						{
							Templates = SessionsVM.Instance.getAllCurrentSessionTemplates();
						}
					}
					else
					{
						MessageBox.Show("Error saving the template, consider clearing the convas and create new template.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
					}
				}
			}
			catch (Exception ex)
			{
				LogError(ex);
				MessageBox.Show(ex.Message);
			}
		}

		private static string PrintFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Prints");

		public void printTemplateWithImages(Template template, List<string> photoPaths)
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
			foreach (var item in CustomDesignerCanvas.Items)
			{
				int index = 0;
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
						ImagePath = imgItem.ImagePath,
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
					var template = Template.LoadTemplateById(Path.GetFileNameWithoutExtension(openFileDialog.SafeFileName));
					if (SessionsVM.Instance.CurrentSession.SavedTemplatesIds.Contains(template.Id))
					{
						CurrentTemplate = Templates[Templates.FindIndex(o => o.Id == template.Id)];
					}
				}
			}
			catch (Exception ex)
			{
				LogError(ex);
				MessageBox.Show(ex.Message);
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
						if (placeholderImages != null)
						{
							string photoPath = placeholderImages.ElementAtOrDefault(p.PhotoNumber - 1);
							CustomDesignerCanvas.Items.Add(new ImageCanvasItem(p.Left, p.Top, p.Width, p.Height, photoPath, 1, 1)
							{
								LockedAspectRatio = bool.Parse(p.KeepAspect)
							});
						}
						else
						{
							CustomDesignerCanvas.Items.Add(new PlaceholderCanvasItem(
							p.Left, p.Top, p.Width, p.Height, 1, 1, p.PhotoNumber, (Brush)converter.ConvertFromString(p.StrokeColor))
							{
								LockedAspectRatio = bool.Parse(p.KeepAspect)
							});
						}
					}
					else if (element is ImageElement i)
					{
						CustomDesignerCanvas.Items.Add(new ImageCanvasItem(i.Left, i.Top, i.Width, i.Height, i.ImagePath, 1, 1)
						{
							LockedAspectRatio = bool.Parse(i.KeepAspect)
						});
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Error applying the template. Try again with another tempalte.");
			}
		}

		private void UnLockItemsPosition()
		{
			CustomDesignerCanvas.FreeSelectedItemsPosition();
			SelectedItemsPositionChangeSource = ImgUnLockPath;
		}

		private void LockItemsPosition()
		{
			CustomDesignerCanvas.LockSelectedItemsPosition();
			SelectedItemsPositionChangeSource = ImgLockPath;
		}

		private void UnlockItemsSize()
		{
			if (CustomDesignerCanvas.SelectedItems.Any(x => (x as IBoxCanvasItem).Resizeable == false))
			{
				CustomDesignerCanvas.FreeSelectedItemsSize();
			}
			SelectedItemsSizeChangeSource = ImgUnLockPath;
		}

		private void LockItemsSize()
		{
			if (CustomDesignerCanvas.SelectedItems.Any(x => (x as IBoxCanvasItem).Resizeable == true))
			{
				CustomDesignerCanvas.LockSelectedItemsSize();
			}
			SelectedItemsSizeChangeSource = ImgLockPath;
		}

		private void SelectedItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
		{
			if (CustomDesignerCanvas.SelectedItems.Count > 0)
			{
				IsRightSidebarVisible = Visibility.Visible;
				if (CustomDesignerCanvas.SelectedItems[0] is IBoxCanvasItem item && ListenerItem != item)
				{
					ListenerItem = item;
					IsEnabledSelectedItemsPositionChange = !item.LockedPosition;
					IsEnabledSelectedItemsSizeChange = item.Resizeable;
				}
			}
			else
			{
				IsRightSidebarVisible = Visibility.Collapsed;
				ListenerItem = null;
			}
		}

		private void SetCanvasRatio(string strRatio)
		{
			var ratio = strRatio.Trim().Split('x').Select(int.Parse).ToArray();
			CustomDesignerCanvas.SetRatio(ratio[0], ratio[1]);
		}

		private void SetElementProperty(ref string field, string value, string propertyName, Action<double> setElementProperty)
		{
			double oldValue = Convert.ToDouble(field);
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

		private void LogError(Exception ex)
		{
			// Implement logging logic here
		}
	}
}
