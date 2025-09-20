using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace Photobooth.Models
{
	[Serializable]
	[XmlRoot("Template")]
	public class Template
	{
		[XmlAttribute("Name")]
		public string Name { get; set; }

		[XmlAttribute("Width")]
		public string Width { get; set; }

		[XmlAttribute("Height")]
		public string Height { get; set; }

		[XmlAttribute("Dimensions")]
		public string Dimensions { get; set; }

		[XmlAttribute("Resolution")]
		public string Resolution { get; set; }

		[XmlAttribute("BackgroundColor")]
		public string BackgroundColor { get; set; }

		[XmlAttribute("print_2_per_page")]
		public string Print2PerPage { get; set; }

		[XmlAttribute("PrintToSecondaryPrinter")]
		public string PrintToSecondaryPrinter { get; set; }

		[XmlAttribute("LastSavedDate")]
		public string LastSavedDate { get; set; }

		[XmlAttribute("Id")]
		public string Id { get; set; }

		[XmlAttribute("IsLegacy")]
		public string IsLegacy { get; set; }

		[XmlAttribute("OriginalDevice")]
		public string OriginalDevice { get; set; }

		[XmlArray("Elements")]
		[XmlArrayItem("Image", typeof(ImageElement))]
		[XmlArrayItem("Photo", typeof(PhotoElement))]
		[XmlArrayItem("Text", typeof(TextElement))]
		[XmlArrayItem("Shape", typeof(ShapeElement))]
		[XmlArrayItem("QRCode", typeof(QrCodeElement))]
		[XmlArrayItem("Barcode", typeof(BarcodeElement))]
		public List<ElementBase> Elements { get; set; }

		public static void SaveTemplateToFile(Template template)
		{
			try
			{
				var templatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Templates");
				if (!Directory.Exists(templatePath))
				{
					Directory.CreateDirectory(templatePath);
				}

				var filePath = Path.Combine(templatePath, template.Id + ".xml");
				var serializer = new XmlSerializer(typeof(Template));
				using (var writer = new StreamWriter(filePath))
				{
					serializer.Serialize(writer, template);
				}
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Failed to save template: {ex.Message}", ex);
			}
		}

		public static Template LoadTemplateFromFile(string filePath)
		{
			try
			{
				if (!File.Exists(filePath))
					return null;

				var serializer = new XmlSerializer(typeof(Template));
				using (var reader = new StreamReader(filePath))
				{
					return (Template)serializer.Deserialize(reader);
				}
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Failed to load template: {ex.Message}", ex);
			}
		}

		public static Template LoadTemplateById(string templateId)
		{
			var templatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Templates");
			var filePath = Path.Combine(templatePath, templateId + ".xml");
			return LoadTemplateFromFile(filePath);
		}
	}

	public abstract class ElementBase
	{
		[XmlAttribute("Name")]
		public string Name { get; set; }

		[XmlAttribute("Opacity")]
		public int Opacity { get; set; }

		[XmlAttribute("ShadowEnabled")]
		public string ShadowEnabled { get; set; }

		[XmlAttribute("ShadowColor")]
		public string ShadowColor { get; set; }

		[XmlAttribute("ShadowDepth")]
		public int ShadowDepth { get; set; }

		[XmlAttribute("ShadowRadius")]
		public int ShadowRadius { get; set; }

		[XmlAttribute("ShadowDirection")]
		public int ShadowDirection { get; set; }

		[XmlAttribute("Width")]
		public int Width { get; set; }

		[XmlAttribute("Height")]
		public int Height { get; set; }

		[XmlAttribute("Top")]
		public int Top { get; set; }

		[XmlAttribute("Left")]
		public int Left { get; set; }

		[XmlAttribute("ImagePath")]
		public string ImagePath { get; set; }

		[XmlAttribute("KeepAspect")]
		public string KeepAspect { get; set; }

		[XmlAttribute("Thickness")]
		public int Thickness { get; set; }

		[XmlAttribute("StrokeColor")]
		public string StrokeColor { get; set; }

		[XmlAttribute("ZIndex")]
		public int ZIndex { get; set; }
	}

	public class ImageElement : ElementBase
	{
		// Additional properties for Image elements

		[XmlAttribute("ImageRotation")]
		public int ImageRotation { get; set; }

		[XmlAttribute("ImageScaleX")]
		public double ImageScaleX { get; set; }

		[XmlAttribute("ImageScaleY")]
		public double ImageScaleY { get; set; }

		// You can add more properties here as needed for your application
	}

	public class PhotoElement : ElementBase
	{
		[XmlAttribute("PhotoNumber")]
		public int PhotoNumber { get; set; }
	}

	public class TextElement : ElementBase
	{
		[XmlAttribute("Text")]
		public string Text { get; set; }

		[XmlAttribute("FontFamily")]
		public string FontFamily { get; set; }

		[XmlAttribute("FontSize")]
		public double FontSize { get; set; }

		[XmlAttribute("FontWeight")]
		public string FontWeight { get; set; }

		[XmlAttribute("FontStyle")]
		public string FontStyle { get; set; }

		[XmlAttribute("TextColor")]
		public string TextColor { get; set; }

		[XmlAttribute("IsBold")]
		public bool IsBold { get; set; }

		[XmlAttribute("IsItalic")]
		public bool IsItalic { get; set; }

		[XmlAttribute("IsUnderlined")]
		public bool IsUnderlined { get; set; }

		[XmlAttribute("LineHeight")]
		public double LineHeight { get; set; }

		[XmlAttribute("LetterSpacing")]
		public double LetterSpacing { get; set; }

		[XmlAttribute("IsVertical")]
		public bool IsVertical { get; set; }

		[XmlAttribute("IsVerticalStack")]
		public bool IsVerticalStack { get; set; }
	}

	public class QrCodeElement : ElementBase
	{
		[XmlAttribute("Value")]
		public string Value { get; set; }

		[XmlAttribute("ECC")]
		public string ECC { get; set; }

		[XmlAttribute("PixelsPerModule")]
		public int PixelsPerModule { get; set; }

		// CustomProperties for storing additional data when loaded from database
		public string CustomProperties { get; set; }
	}

	public class BarcodeElement : ElementBase
	{
		[XmlAttribute("Value")]
		public string Value { get; set; }

		[XmlAttribute("Symbology")]
		public string Symbology { get; set; }

		[XmlAttribute("ModuleWidth")]
		public double ModuleWidth { get; set; }

		[XmlAttribute("IncludeLabel")]
		public bool IncludeLabel { get; set; }

		// CustomProperties for storing additional data when loaded from database
		public string CustomProperties { get; set; }
	}

	public class ShapeElement : ElementBase
	{
		[XmlAttribute("ShapeType")]
		public string ShapeType { get; set; }

		[XmlAttribute("FillColor")]
		public string FillColor { get; set; }

		[XmlAttribute("StrokeColor")]
		public new string StrokeColor { get; set; }

		[XmlAttribute("StrokeThickness")]
		public double StrokeThickness { get; set; }

		[XmlAttribute("HasNoFill")]
		public bool HasNoFill { get; set; }

		[XmlAttribute("HasNoStroke")]
		public bool HasNoStroke { get; set; }
	}
}
