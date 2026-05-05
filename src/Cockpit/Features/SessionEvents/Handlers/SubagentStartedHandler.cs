using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SubagentStartedHandler
{
	internal static void Handle(SessionModel session, SubagentStartedEvent evt)
	{
		session.ActiveWorkingGroup ??= new ActivityGroupModel
		{
			StartTime = evt.Timestamp.LocalDateTime,
			Status = GroupStatusEnum.Running,
			IsExpanded = true
		};

		// Check if a ToolExecution already exists for this ToolCallId (created by tool.execution_start).
		// Use the recursive helper so nested subagents are found correctly.
		ToolExecutionModel? existingExec = SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.Data.ToolCallId);

		if(existingExec is not null)
		{
			existingExec.ToolName = evt.Data.AgentDisplayName ?? existingExec.ToolName;
			existingExec.IsBackgroundAgent = true;
			existingExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));

			// Always reset to Running — the background tool.execution_complete may arrive
			// after this event (or already has), but the sub-agent is still doing real work.
			existingExec.Status = ToolStatusEnum.Running;
			existingExec.EndTime = null;
			existingExec.Output = null;
		}
		else
		{
			ToolExecutionModel subagentExec = new()
			{
				ToolName = evt.Data.AgentDisplayName ?? string.Empty,
				ToolCallId = evt.Data.ToolCallId,
				StartTime = evt.Timestamp.LocalDateTime,
				Status = ToolStatusEnum.Running,
				IsBackgroundAgent = true
			};

			subagentExec.AddRawEvent(new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt)));

			// Nest under the parent agent's tool if we're inside a nested sub-agent turn
			ToolExecutionModel? parentTool = evt.AgentId is not null
				? SessionEventHelpers.FindToolExecution(session.ActiveWorkingGroup, evt.AgentId)
				: null;

			if(parentTool is not null)
			{
				parentTool.AddChild(subagentExec);
			}
			else
			{
				session.ActiveWorkingGroup.AddEvent(new ThinkingEventModel
				{
					Type = ThinkingEventTypeEnum.Tool,
					Tool = subagentExec,
					Timestamp = evt.Timestamp.LocalDateTime,
					EventJson = [new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt))]
				});
			}
		}
	}
}
