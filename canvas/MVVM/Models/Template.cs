using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Photobooth.MVVM.Models
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
		public List<ElementBase> Elements { get; set; }

		private static string templatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Templates");

		public static void SaveTemplateToFile(Template template)
		{
			string templateFilePath = Path.Combine(templatePath, $"{template.Id}.xml");
			Directory.CreateDirectory(templatePath);

			XmlSerializer serializer = new XmlSerializer(typeof(Template));
			using (StreamWriter writer = new StreamWriter(templateFilePath))
			{
				serializer.Serialize(writer, template);
			}
		}

		public static Template LoadTemplateById(string id)
		{
			string templateFilePath = Path.Combine(templatePath, $"{id}.xml");

			if (File.Exists(templateFilePath))
			{
				XmlSerializer serializer = new XmlSerializer(typeof(Template));
				using (StreamReader reader = new StreamReader(templateFilePath))
				{
					return (Template)serializer.Deserialize(reader);
				}
			}

			return null;
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
}

