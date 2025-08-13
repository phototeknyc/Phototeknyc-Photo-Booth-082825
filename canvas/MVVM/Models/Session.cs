using Photobooth.MVVM.Views.UserControls.Camera;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Photobooth.MVVM.Models
{
	[Serializable]
	[XmlRoot("Session")]
	public class Session
	{
		[XmlAttribute("Id")]
		public String Id { get; set; }
		[XmlAttribute("Name")]
		public String Name { get; set; }
		[XmlAttribute("SavedTemplatesIds")]
		public List<String> SavedTemplatesIds { get; set; }
		[XmlAttribute("Path")]
		public String _path { get; set; }

		public static string SESSIONS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PhotoBooth", "Sessions");

		public Session()
		{
			SavedTemplatesIds = new List<String>();
		}

		public Template GetTemplate(String templateId)
		{
			return Template.LoadTemplateById(templateId);
		}

		public List<Template> GetTemplates()
		{
			List<Template> templates = new List<Template>();
			foreach (String templateId in SavedTemplatesIds)
			{
				templates.Add(GetTemplate(templateId));
			}
			return templates;
		}

		public void AddTemplate(String templateId)
		{
			SavedTemplatesIds.Add(templateId);
			UpdateSession();
		}

		public void RemoveTemplate(String templateId)
		{
			SavedTemplatesIds.Remove(templateId);
			UpdateSession();
		}

		private void UpdateSession()
		{
			SaveSessionToFile(this);
		}

		public static void SaveSessionToFile(Session session)
		{
			string sessionFilePath = Path.Combine(SESSIONS_PATH, $"{session.Id}.xml");
			Directory.CreateDirectory(SESSIONS_PATH);

			XmlSerializer serializer = new XmlSerializer(typeof(Session));
			using (StreamWriter writer = new StreamWriter(sessionFilePath))
			{
				serializer.Serialize(writer, session);
			}
		}

		public static Session LoadSessionById(string path)
		{
			if (File.Exists(path))
			{
				XmlSerializer serializer = new XmlSerializer(typeof(Session));
				using (StreamReader reader = new StreamReader(path))
				{
					return (Session)serializer.Deserialize(reader);
				}
			}

			return null;
		}

		public static List<Session> LoadAllSessions()
		{
			List<Session> sessions = new List<Session>();
			Directory.CreateDirectory(SESSIONS_PATH);
			foreach (string file in Directory.EnumerateFiles(SESSIONS_PATH, "*.xml"))
			{
				sessions.Add(LoadSessionById(file));
			}
			return sessions;
		}
	}
}
