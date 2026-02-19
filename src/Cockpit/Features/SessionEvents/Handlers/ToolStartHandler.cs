using System.Diagnostics;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class ToolStartHandler
{
	internal static void Handle(ChatSession session, ToolExecutionStartEvent evt)
	{
		Debug.WriteLine("ToolStartHandler");
		Debug.WriteLine(evt);

		if(evt.Data is null)
		{
			return;
		}

		if(session.ActiveWorkingGroup is null)
		{
			session.ActiveWorkingGroup = new ActivityGroupModel
			{
				StartTime = evt.Timestamp.LocalDateTime,
				Status = GroupStatusEnum.Running,
				IsExpanded = true
			};

			// Find the most recent assistant message AFTER the last user message (current turn only)
			int lastUserIndex = -1;
			for(int i = session.Messages.Count - 1; i >= 0; i--)
			{
				if(session.Messages[i].IsUser)
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
		string? parentCallId = evt.Data.ParentToolCallId;
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
			Timestamp = evt.Timestamp.LocalDateTime
		});
	}
}
