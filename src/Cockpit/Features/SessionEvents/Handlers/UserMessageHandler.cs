using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class UserMessageHandler
{
	internal static void Handle(ChatSession session, UserMessageEvent evt, bool wasAgentBusy = false)
	{
		Debug.WriteLine("UserMessageHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		string eventMessageId = evt.Id.ToString();
		ChatMessageModel? optimistic = session.Messages.FirstOrDefault(m => m.IsUser && !m.IsComplete && m.Id == eventMessageId);
		optimistic ??= session.Messages.FirstOrDefault(m => m.IsUser && !m.IsComplete && m.Content == (evt.Data.Content ?? string.Empty));

		if(optimistic is not null)
		{
			// Confirm the optimistic message: update its metadata from the real event
			optimistic.Timestamp = evt.Timestamp;
			optimistic.EventType = evt.Type;
			optimistic.IsComplete = true;
			// Keep IsPending=true if already set (optimistic was created while agent was busy),
			// or set it now if the agent is still busy when the SDK echo arrives
			optimistic.IsPending = optimistic.IsPending || wasAgentBusy;
		}
		else
		{
			ChatMessageModel message = new()
			{
				Id = Guid.NewGuid().ToString(),
				Content = evt.Data.Content ?? string.Empty,
				IsUser = true,
				Timestamp = evt.Timestamp,
				Type = MessageTypeEnum.Text,
				EventType = evt.Type,
				IsPending = wasAgentBusy
			};
			session.Messages.Add(message);
		}

		session.Status = SessionStatus.Running;
	}
}
