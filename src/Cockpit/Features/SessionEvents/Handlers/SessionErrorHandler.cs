using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionErrorHandler
{
	internal static void Handle(SessionModel session, SessionErrorEvent evt)
	{
		Debug.WriteLine("SessionErrorHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		ChatMessageModel message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Message ?? "An error occurred",
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageTypeEnum.Error,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.Status = SessionStatusEnum.Error;
	}
}
