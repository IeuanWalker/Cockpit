using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
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
		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Status == GroupStatusEnum.Running)
		{
			if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessageModel? message))
			{
				message = new ChatMessageModel
				{
					Id = messageId,
					Content = string.Empty,
					IsUser = false,
					Timestamp = evt.Timestamp,
					Type = MessageTypeEnum.Text,
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
		if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessageModel? msg))
		{
			msg = new ChatMessageModel
			{
				Id = messageId,
				Content = string.Empty,
				IsUser = false,
				Timestamp = DateTime.Now,
				Type = MessageTypeEnum.Text,
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
