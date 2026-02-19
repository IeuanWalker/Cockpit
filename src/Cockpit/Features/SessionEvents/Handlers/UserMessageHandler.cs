using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class UserMessageHandler
{
	internal static void Handle(ChatSession session, UserMessageEvent evt)
	{
		Debug.WriteLine("UserMessageHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Content ?? string.Empty,
			IsUser = true,
			Timestamp = evt.Timestamp,
			Type = MessageType.Text,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.Status = SessionStatus.Running;
	}
}
