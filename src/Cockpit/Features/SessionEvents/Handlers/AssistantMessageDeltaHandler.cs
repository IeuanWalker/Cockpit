using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class AssistantMessageDeltaHandler
{
	internal static void Handle(SessionModel session, AssistantMessageDeltaEvent evt)
	{
		string messageId = evt.Data.MessageId ?? "streaming";
		string delta = evt.Data.DeltaContent ?? string.Empty;

		// Don't add to chat if we have an active thinking group
		if(session.ActiveWorkingGroup is not null && session.ActiveWorkingGroup.Status == GroupStatusEnum.Running)
		{
			// Keep a ChatMessageModel buffer so AssistantMessageHandler can do final cleanup
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
					EventType = evt.Type,
					EventJson = []
				};
				session.StreamingMessages[messageId] = message;
			}

			message.EventJson?.Add(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
			message.Content += delta;

			// Also route deltas live into the thinking panel so content is visible as it streams
			if(!session.StreamingThinkingEvents.TryGetValue(messageId, out ThinkingEventModel? thinkingEvent))
			{
				thinkingEvent = new ThinkingEventModel
				{
					Id = messageId,
					Type = ThinkingEventTypeEnum.Message,
					Message = string.Empty,
					Timestamp = evt.Timestamp.LocalDateTime,
					EventJson = []
				};

				ToolExecutionModel? parentTool = evt.AgentId is not null
					? SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.AgentId)
					: null;

				if(parentTool is not null)
				{
					parentTool.AddChildEvent(thinkingEvent);
				}
				else
				{
					session.ActiveWorkingGroup.AddEvent(thinkingEvent);
				}

				session.StreamingThinkingEvents[messageId] = thinkingEvent;
			}

			thinkingEvent.Message += delta;
			thinkingEvent.EventJson?.Add(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
			return;
		}

		// Not in thinking mode — add to chat normally
		if(!session.StreamingMessages.TryGetValue(messageId, out ChatMessageModel? msg))
		{
			msg = new ChatMessageModel
			{
				Id = messageId,
				Content = string.Empty,
				IsUser = false,
				Timestamp = evt.Timestamp,
				Type = MessageTypeEnum.Text,
				IsStreaming = true,
				IsComplete = false,
				EventType = evt.Type,
				EventJson = []
			};
			session.StreamingMessages[messageId] = msg;
			session.Messages.Add(msg);
		}

		msg.EventJson?.Add(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));
		msg.Content += delta;
	}
}
