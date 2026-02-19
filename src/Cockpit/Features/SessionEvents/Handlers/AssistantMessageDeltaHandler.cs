using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantMessageDeltaHandler
{
	internal static void Handle(ChatSession session, AssistantMessageDeltaEvent evt)
	{
		Debug.WriteLine("AssistantMessageDeltaHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		string messageId = evt.Data.MessageId ?? "streaming";

		// Don't add to chat if we have an active thinking group
		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Status == GroupStatus.Running)
		{
			if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessage? message))
			{
				message = new ChatMessage
				{
					Id = messageId,
					Content = string.Empty,
					IsUser = false,
					Timestamp = evt.Timestamp,
					Type = MessageType.Text,
					IsStreaming = true,
					IsComplete = false,
					EventType = evt.Type
				};
				session.StreamingMessages[messageId] = message;
				// DON'T add to session.Messages - it will go in thinking panel
			}

			message.Content += evt.Data.DeltaContent ?? string.Empty;
			return;
		}

		// Not in thinking mode - add to chat normally
		if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessage? msg))
		{
			msg = new ChatMessage
			{
				Id = messageId,
				Content = string.Empty,
				IsUser = false,
				Timestamp = DateTime.Now,
				Type = MessageType.Text,
				IsStreaming = true,
				IsComplete = false,
				EventType = evt.Type
			};
			session.StreamingMessages[messageId] = msg;
			session.Messages.Add(msg);
		}

		msg.Content += evt.Data.DeltaContent ?? string.Empty;
	}
}
