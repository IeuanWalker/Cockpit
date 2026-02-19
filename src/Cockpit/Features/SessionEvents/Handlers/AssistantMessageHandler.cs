using System.Diagnostics;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantMessageHandler
{
	internal static void Handle(ChatSession session, AssistantMessageEvent evt)
	{
		Debug.WriteLine("AssistantMessageHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		string messageId = evt.Data.MessageId ?? Guid.NewGuid().ToString();
		string content = evt.Data.Content ?? string.Empty;

		bool isStreamingMessage = session.StreamingMessages.TryGetValue(messageId, out ChatMessage? streamingMsg);
		bool isInChat = session.Messages.Any(m => m.Id == messageId);

		// Check if we have an active thinking group
		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Status == GroupStatus.Running)
		{
			// If this message is already in chat, it's the initial message - keep it there
			if(isInChat)
			{
				if(streamingMsg is not null)
				{
					streamingMsg.Content = content;
					streamingMsg.IsStreaming = false;
					streamingMsg.IsComplete = true;
					session.StreamingMessages.Remove(messageId);
				}

				if(session.ActiveWorkingGroup.InitialMessageId is null)
				{
					session.ActiveWorkingGroup.InitialMessageId = messageId;
				}
				return;
			}

			// All messages during thinking go to the thinking panel
			if(!string.IsNullOrWhiteSpace(content))
			{
				session.ActiveWorkingGroup.AddEvent(new ThinkingEvent
				{
					Id = messageId,
					Type = ThinkingEventType.Message,
					Message = content,
					Timestamp = evt.Timestamp.LocalDateTime
				});
				Debug.WriteLine("Added intermediate message to thinking group");
			}

			if(streamingMsg is not null)
			{
				session.StreamingMessages.Remove(messageId);
			}
			return;
		}

		if(streamingMsg is not null)
		{
			streamingMsg.Content = content;
			streamingMsg.IsStreaming = false;
			streamingMsg.IsComplete = true;
			session.StreamingMessages.Remove(messageId);
		}
		else if(!string.IsNullOrWhiteSpace(content))
		{
			ChatMessage message = new()
			{
				Id = messageId,
				Content = content,
				IsUser = false,
				Timestamp = evt.Timestamp,
				Type = MessageType.Text,
				IsComplete = true,
				EventType = evt.Type
			};
			session.Messages.Add(message);
		}
	}
}
