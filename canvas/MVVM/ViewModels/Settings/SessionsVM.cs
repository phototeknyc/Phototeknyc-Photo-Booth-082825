using Photobooth.MVVM.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace Photobooth.MVVM.ViewModels.Settings
{
	public class SessionsVM : BaseViewModel
	{
		private static SessionsVM _instance;
		public static SessionsVM Instance => _instance ?? (_instance = new SessionsVM());

		private bool _isSessionSelected;
		public bool IsSessionSelected { get => _isSessionSelected; set => SetProperty(ref _isSessionSelected, value); }

		private List<Session> _sessions;
		public List<Session> Sessions { get => _sessions; set => SetProperty(ref _sessions, value); }

		private Session _currentSession;
		public Session CurrentSession
		{
			get => _currentSession;
			set
			{
				SetProperty(ref _currentSession, value);
				if (value != null) IsSessionSelected = true;
			}
		}

		private SessionsVM() => Sessions = Session.LoadAllSessions();

		public bool CreateNewSession(string name)
		{
			try
			{
				var session = new Session
				{
					Id = Guid.NewGuid().ToString(),
					Name = name,
					_path = Path.Combine(Session.SESSIONS_PATH, $"{Guid.NewGuid()}.xml"),
					SavedTemplatesIds = new List<string>()
				};
				Session.SaveSessionToFile(session);
				Sessions.Add(session);
				CurrentSession = session;
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal void SaveTempalte(Template template)
		{
			Template.SaveTemplateToFile(template);
		}

		internal bool? CreateNewTemplate(string text, object temp)
		{
			if (temp is Template template)
			{
				template.Name = text;
				Template.SaveTemplateToFile(template);
				CurrentSession.AddTemplate(template.Id);
				return true;
			}
			else
			{
				MessageBox.Show("Template not found.");
			}

			return false;
		}

		internal List<Template> getAllCurrentSessionTemplates()
		{
			return CurrentSession.GetTemplates();
		}
	}
}