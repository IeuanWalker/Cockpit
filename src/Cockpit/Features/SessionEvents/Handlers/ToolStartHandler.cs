using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;

namespace Cockpit.Features.SessionEvents.Handlers;

static class ToolStartHandler
{
	internal static void Handle(SessionModel session, ToolExecutionStartEvent evt)
	{
		// When no group exists yet (tool fired after safety net closed the prior group, before a new
		// turn_start could create one), create the group here and anchor it to the triggering user
		// message.  Also absorb any agent messages that leaked into chat while there was no active
		// group — they belong in the ops panel, not as standalone chat messages.
		if(session.ActiveWorkingGroup is null || session.ActiveWorkingGroup.IsPlaceholder)
		{
			ChatMessageModel? triggerMsg = session.Messages
				.LastOrDefault(m => m.IsUser && m.IsComplete && !m.IsPending);

			session.ActiveWorkingGroup = new ActivityGroupModel
			{
				StartTime = evt.Timestamp.LocalDateTime,
				Status = GroupStatusEnum.Running,
				IsExpanded = true,
				TriggeredByUserMessageId = triggerMsg?.Id
			};

			// Absorb leaked pre-group text messages (flagged by AssistantMessageHandler /
			// AssistantMessageDeltaHandler when no active group was present).
			if(triggerMsg is not null)
			{
				int anchorIndex = session.Messages.IndexOf(triggerMsg);
				if(anchorIndex >= 0)
				{
					for(int i = anchorIndex + 1; i < session.Messages.Count;)
					{
						ChatMessageModel m = session.Messages[i];
						if(!m.IsUser && m.Type == MessageTypeEnum.Text && m.IsLeakedPreGroupMessage)
						{
							session.ActiveWorkingGroup.AddEvent(new ThinkingEventModel
							{
								Id = m.Id,
								Type = ThinkingEventTypeEnum.Message,
								Message = m.Content,
								Timestamp = m.Timestamp.LocalDateTime,
								EventJson = m.EventJson
							});
							session.Messages.RemoveAt(i);
							if(m.Id is not null)
							{
								session.StreamingMessages.Remove(m.Id);
							}
						}
						else
						{
							i++;
						}
					}
				}
			}
		}

		// Set InitialMessageId if not already set — find the last assistant message after the last user message
		if(string.IsNullOrEmpty(session.ActiveWorkingGroup.InitialMessageId))
		{
			int lastUserIndex = -1;
			for(int i = session.Messages.Count - 1; i >= 0; i--)
			{
				// Skip pending or not-yet-confirmed user messages — they belong to a future turn
				if(session.Messages[i].IsUser && session.Messages[i].IsComplete && !session.Messages[i].IsPending)
				{
					lastUserIndex = i;
					break;
				}
			}

			ChatMessageModel? lastAssistantMessage = null;
			if(lastUserIndex >= 0)
			{
				lastAssistantMessage = session.Messages
					.Skip(lastUserIndex + 1)
					.Where(m => !m.IsUser && m.Type == MessageTypeEnum.Text)
					.LastOrDefault();
			}

			if(lastAssistantMessage is not null)
			{
				session.ActiveWorkingGroup.InitialMessageId = lastAssistantMessage.Id;
				Debug.WriteLine($"Tracked initial message: {lastAssistantMessage.Id}");
			}
		}

		ToolExecutionModel toolExec = new()
		{
			ToolName = evt.Data.ToolName ?? "unknown",
			ToolCallId = evt.Data.ToolCallId,
			InputParameters = SessionEventHelpers.DeserializeArguments(evt.Data.Arguments),
			StartTime = evt.Timestamp.LocalDateTime,
			Status = ToolStatusEnum.Running
		};

		toolExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));

		// If this is a child call (belongs to a subagent), nest it under the parent
		string? parentCallId = evt.AgentId;
		if(parentCallId is not null)
		{
			ToolExecutionModel? parent = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, parentCallId);
			if(parent is not null)
			{
				parent.AddChild(toolExec);
				return;
			}
		}

		// Top-level tool — add as a thinking event (chronologically ordered with messages)
		session.ActiveWorkingGroup.AddEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Tool,
			Tool = toolExec,
			Timestamp = evt.Timestamp.LocalDateTime,
			EventJson = [new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt))]
		});
	}
}
