using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionErrorHandler
{
	internal static void Handle(ChatSession session, SessionErrorEvent evt)
	{
		Debug.WriteLine("SessionErrorHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Message ?? "An error occurred",
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageType.Error,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.Status = SessionStatus.Error;
	}
}
