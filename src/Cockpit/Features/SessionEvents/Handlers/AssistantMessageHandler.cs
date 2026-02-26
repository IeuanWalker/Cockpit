using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantMessageHandler
{
	internal static void Handle(SessionModel session, AssistantMessageEvent evt)
	{
		if(evt.Data is null)
		{
			return;
		}

		string messageId = evt.Data.MessageId ?? Guid.NewGuid().ToString();
		string content = evt.Data.Content ?? string.Empty;

		bool isStreamingMessage = session.StreamingMessages.TryGetValue(messageId, out ChatMessageModel? streamingMsg);
		bool isInChat = session.Messages.Any(m => m.Id == messageId);

		// Check if we have an active thinking group
		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Status == GroupStatusEnum.Running)
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
				ThinkingEventModel thinkingEvent = new()
				{
					Id = messageId,
					Type = ThinkingEventTypeEnum.Message,
					Message = content,
					Timestamp = evt.Timestamp.LocalDateTime
				};

				// If this message belongs to a subagent, nest it under the parent tool call
				string? parentCallId = evt.Data.ParentToolCallId;
				ToolExecutionModel? parentTool = parentCallId is not null
					? SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, parentCallId)
					: null;

				if(parentTool is not null)
				{
					parentTool.AddChildEvent(thinkingEvent);
					Debug.WriteLine("Added intermediate message as child of parent tool");
				}
				else
				{
					session.ActiveWorkingGroup.AddEvent(thinkingEvent);
					Debug.WriteLine("Added intermediate message to thinking group");
				}
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
			ChatMessageModel message = new()
			{
				Id = messageId,
				Content = content,
				IsUser = false,
				Timestamp = evt.Timestamp,
				Type = MessageTypeEnum.Text,
				IsComplete = true,
				EventType = evt.Type
			};
			// Insert before any pending user messages so the summary appears before the next queued message
			int pendingIdx = session.Messages.FindIndex(m => m.IsUser && m.IsPending);
			if(pendingIdx >= 0)
			{
				session.Messages.Insert(pendingIdx, message);
			}
			else
			{
				session.Messages.Add(message);
			}
		}
	}
}
